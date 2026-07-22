# Contract: System Profile Ingestion Step

Not an HTTP surface — this documents the new provisioning-time step's input/output contract, the same way
`008-orca-slicer-multi-version`'s `contracts/slicer-manifest.schema.json` documents its provisioning
manifest rather than an API.

## Trigger

Runs once per bundled `(engine, version, platform)` leaf already provisioned by `tools/provision-slicers`
(research.md §0 "Locating resources/profiles"). Two acceptable trigger points, either is spec-compliant
(Acceptance Scenario 1 only requires it runs "as part of provisioning"); the concrete choice is an
implementation detail for tasks.md, not fixed here:

- Extend `tools/provision-slicers` itself to invoke ingestion after a successful `Provisioner.ProvisionAsync`
  (keeps the "provisioning" step self-contained, but that tool has no DB access today — it would need one).
- A separate one-shot step run against the running API's DB context (e.g. `dotnet run --project
  api/src/OpenFarm.Api -- ingest-system-profiles`), reading the same bundle root
  `BundledSlicerScanner`/`SlicerBundleOptions` already point at. Re-runnable any time, not just at
  provisioning (matches the spec's own "e.g. a subsequent provisioning run" wording — "e.g.", not "only").

## Input

- Filesystem: one bundled slicer leaf directory (`<root>/<engine>/<version>/<platform>/…`).
- Database (read-only for scoping): `PrinterModelCatalog.All` (compiled into the running assembly, not a
  table — no DB read actually needed for this specific input; listed for completeness).

## Output

- `MachineProfile`/`ProcessProfile`/`FilamentProfile` rows with `Origin = System`, upserted per research.md
  §1's identity key. Every previously-`System` row not re-seen in this run has `IsCurrentlyOffered` set to
  `false` (never deleted).
- Each fetched preset file is kept only if its own declared `type` matches the list it was fetched from
  (`machine`/`process`/`filament`) — a vendor's `machine_list`/`filament_list` can reference untyped or
  mismatched-type files (confirmed on Bambu Studio: 89 of 148 `machine_list` entries are untyped
  gcode-template fragments, not machine presets — research.md §0a); anything failing this check is still
  ingested as a plain named row (so `IncludedTemplateNames` resolution below has something to resolve
  against) but is excluded from ever being treated as a selectable preset of that kind.
- Machine and filament rows additionally capture `IncludedTemplateNames` verbatim from the source preset's
  own `include` array where present (Bambu Studio only, as of this research — universal on its machine
  presets, ~68% of its filament presets; absent from OrcaSlicer's data entirely). Process rows never carry
  this field (research.md §0a confirms zero usage there in either engine).
- A summary report (counts: vendors scanned, vendors skipped as unsupported, rows created / updated /
  marked stale, per kind) — logged, not persisted; mirrors `Provisioner.ProvisionAsync`'s own
  per-entry `ProvisionEntryResult` reporting convention (never throws for one bad vendor file; a
  malformed/unreadable vendor JSON is skipped with a logged reason, the rest of the run continues).

## Idempotency guarantee

Re-running ingestion against **byte-identical** vendor data produces zero writes (Acceptance Scenario 5) —
every row's upsert key already exists and its `RawDataJson` compares equal. Re-running against a **newer**
bundled version's vendor data updates existing rows in place (same `Id`, so FKs from `Printer`/
`PartFilamentRequirement` stay valid) and marks anything genuinely removed as `IsCurrentlyOffered = false`.

## Failure handling

A vendor file that fails to parse, or a preset file a vendor index references but that's missing/corrupt on
disk, is skipped with a logged warning — it does not abort ingestion of the rest of that vendor's presets
or any other vendor. This mirrors the constitution's "Durable Recovery of In-Flight Background Work"
guardrail in spirit (route every failure to a terminal, reported outcome, never a silent stall) even though
ingestion itself is not a `BackgroundService` — it's a one-shot provisioning-time or on-demand step with no
non-terminal persisted state to strand.
