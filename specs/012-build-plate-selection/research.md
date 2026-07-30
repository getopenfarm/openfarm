# Research: Build Plate Selection

**Feature**: `012-build-plate-selection` | **Date**: 2026-07-30

All unknowns below were resolved by reading the actual current code (not just the spec's prose),
so the plan builds on real shapes, not assumptions. File paths are relative to `api/`/`web/` unless
noted.

## R1: How is a printer's build plate selection stored?

**Decision**: A new nullable C# enum property `Printer.BuildPlateType` (`BuildPlateType?`), persisted
as a single new nullable `text` column on `Printers` via EF's `.HasConversion<string>()` — the exact
pattern already used for `Printer.Status` (`PrinterConfiguration.cs:34-36`, `HasConversion<string>()`
on a `PrinterStatus` enum). **Not** a foreign key to a new lookup table.

**Rationale**: `MachineProfileId`/`ProcessProfileId` are `Guid?` FKs because machine/process are real
persisted, vendor-ingested profile rows (`MachineProfiles`/`ProcessProfiles` tables). Build plate is
explicitly *not* a profile catalog (FR-004) — it is a small, fixed, hand-authored enumeration, which
is exactly the shape `PrinterStatus` already has on this same entity. Reusing the enum-as-text
pattern needs no new table, no new repository, and can't ever have a "dangling reference" failure
mode the way a profile FK can (`GetPrinterDetailQuery`'s existing dangling-FK-tolerance code becomes
unnecessary for this field).

**Alternatives considered**:
- A native Postgres enum type (`CREATE TYPE build_plate_type AS ENUM (...)`) — rejected: no other
  column on `Printers` uses a native PG enum (`Status` uses `HasConversion<string>()` over a plain
  `text` column instead), and a native PG enum requires a separate ALTER TYPE migration path to add
  a new plate type later, which the codebase has deliberately avoided elsewhere.
- A `BuildPlateTypes` lookup table + FK — rejected: would look identical to a profile catalog table
  at the schema level, directly contradicting FR-004/FR-010 ("not a fourth profile kind"); an enum on
  `Printer` communicates "fixed, non-extensible-by-users" far more directly than a table an admin
  could technically INSERT into.

**Migration shape**: One new migration, structurally identical to
`20260719150100_AddPrinterMachineProfileLink.cs` but simpler (single `AddColumn<string>`, no index,
no FK, no `Down()` FK/index drops) — see `data-model.md`.

## R2: Where does per-exact-model build plate compatibility (FR-002) live, and how is it sourced?

**Decision**: Extend the existing `PrinterModel` record (`Domain/Printers/PrinterModel.cs`) with a new
field `IReadOnlySet<BuildPlateType> CompatiblePlateTypes`, populated by hand for every entry already in
`PrinterModelCatalog.Entries` (`Domain/Printers/PrinterModelCatalog.cs`) — the same static,
compile-time, hand-authored mechanism the catalog already uses for `VendorMachineModelName`.

**Rationale**: `PrinterModelCatalog` is deliberately *not* ingested (its own doc comment: "adding a
brand/model is a new entry here, never a rewrite"). Build plate compatibility must never be ingested
either (FR-004), so it belongs on the exact same static record, keyed by the exact same
`(Brand, ModelCode)` the catalog already validates a printer's identity against at registration
(`Printer.Create` → `PrinterModelCatalog.Resolve`). This directly satisfies the clarification that
compatibility is gated by the printer's own registered Brand/ModelCode, independent of whichever
machine profile (if any) is linked.

**Sourcing signal priority** (spec Clarifications, session 2026-07-30): a model's own machine preset
declares two independent, directly-inspectable signals in its raw vendor data — verified firsthand by
installing the real OrcaSlicer/Bambu Studio apps and inspecting their full bundled `resources/profiles`
vendor bundle (not just this repo's narrow test fixtures, which only vendor `X1 Carbon` in depth):

1. **`not_support_bed_type`** — an explicit, semicolon-separated denylist some machine presets declare
   directly (e.g. `Bambu Lab H2D.json`: `"not_support_bed_type": "Smooth Cool Plate;Textured Cool
   Plate"`; `Bambu Lab A1 mini.json`: `"not_support_bed_type": "Smooth Cool Plate;Engineering Plate"`).
   **Authoritative when present** — subtract the listed types from the full six-value set.
2. **Filament-preset temp-key differentiation** (the original plan) — which of the six
   `*_plate_temp` keys a model's own filament presets leave genuinely non-`"0"` (see R3). **Fallback**
   for any model whose machine preset doesn't declare `not_support_bed_type` at all — confirmed still
   necessary because several current catalog entries (`X1`, `X1C`, `X1E`, `P1P`, `P1S`, `A1`) have no
   `not_support_bed_type` field in their real machine preset, yet clearly don't support every plate
   type (e.g. `A1`'s own "PLA Basic" preset only ever overrides `hot_plate_temp`/`textured_plate_temp`
   — confirmed by direct inspection of `Bambu PLA Basic @BBL A1.json` — leaving `cool_plate_temp`/
   `eng_plate_temp`/`supertack_plate_temp`/`textured_cool_plate_temp` at their inherited `"0"`).
   Cross-checked once where both signals exist for the same model (`A1 mini`): the denylist excludes
   `Engineering Plate`, and `A1M`'s own PLA preset's `eng_plate_temp` independently resolves to `"0"`
   through inheritance — the two signals agree, giving confidence the fallback heuristic is sound where
   the authoritative signal is absent.

Two concrete data points seed the static table immediately:
- `Bambu Lab X1 Carbon` machine preset declares `"default_bed_type": "Textured PEI Plate"` and no
  `not_support_bed_type` — so `X1C`'s compatible set is sourced via the filament-temp fallback and must
  include at least `TexturedPei`.
- Per the spec's clarification session, Bambu Lab A1's "PLA Basic" preset only declares `Textured PEI
  Plate`/`High Temp Plate` temperatures (no `not_support_bed_type` on `A1` itself) — so `A1`'s
  compatible set is `{ TexturedPei, HighTemp }`. `A1MINI` additionally has an authoritative
  `not_support_bed_type: "Smooth Cool Plate;Engineering Plate"`, consistent with the same fallback
  result.

The remaining catalog entries (`X1`, `X1E`, `P1P`, `P1S`) need the same one-time two-signal inspection
against the real vendor bundle during implementation (a `tasks.md` data-population item, not a design
decision). `System`/`Simulation` gets every plate type (it's a virtual printer with no real hardware
constraint, matching how it already bypasses other physical-model gates).

**Alternatives considered**:
- Deriving compatibility from whichever `MachineProfile.RawDataJson` is currently linked (its own
  `default_bed_type`/`not_support_bed_type`) — rejected outright per the spec's clarification (Q4):
  today's `LinkMachineProfile`/`EditPrinterCommand` flow has no check tying the linked machine profile
  back to the printer's own registered model, so this would let a mismatched machine profile silently
  corrupt plate compatibility. Gating on the printer's own immutable Brand/ModelCode sidesteps that
  pre-existing gap entirely, and is also why build plate selection needs no machine-profile
  prerequisite (unlike process profile). (`not_support_bed_type` is still read — just from the *matching
  System machine profile for that registered model*, at data-population time, not from whichever
  profile happens to be linked to a given printer instance.)
- Using `not_support_bed_type` as the *sole* signal, dropping the filament-temp fallback entirely —
  rejected: confirmed by direct inspection that several current catalog entries simply don't declare
  the field at all, so a `not_support_bed_type`-only design would silently treat those models as
  compatible with all six plate types, which is wrong (e.g. it would incorrectly offer `A1` a
  `Supertack Plate` choice its own filament data never actually supports).

## R3: How does a slice resolve the correct plate-specific bed temperature?

**Decision**: Two-part mechanism inside `ResolveSlicingProfileSelection.ResolveAsync`
(`Application/Parts/ResolveSlicingProfileSelection.cs`):

1. **Pre-flight validation** (before any slicer invocation): once the filament profile's merged
   `FilamentSettingsJson` is produced, look up the JSON key for the target printer's `BuildPlateType`
   (mapping below) and confirm it's present with a non-`"0"` value. If the key is missing or its value
   is `"0"` (OrcaSlicer's own "not applicable for this material/plate" convention — confirmed by the
   real `fdm_filament_pla.json` fixture overriding `"eng_plate_temp": ["0"]`), throw
   `SlicingProfileUnconfiguredException` naming both the filament preset and the plate type (FR-006).
2. **Injection**: patch `curr_bed_type` into the merged `ProcessSettingsJson` with the plate type's
   exact OrcaSlicer display string (e.g. `"Textured PEI Plate"`) — the identical mechanism the
   existing golden-file test suite already uses and has proven correct
   (`RealSlicerGoldenFileTests.WithBedTypeOverride`, `tests/OpenFarm.Infrastructure.Tests/Parts/
   RealSlicerGoldenFileTests.cs:82-88`, which patches the same `curr_bed_type` key via
   `JsonNode.Parse(...).AsObject()`).

**Rationale**: OrcaSlicer/Bambu Studio's own CLI already resolves the correct plate-specific bed
temperature (including the `_initial_layer` variant) internally once `curr_bed_type` is set on the
process settings — this is exactly how the slicer's own GUI plate selector works, and the golden-file
test fixture (`api/tests/.../RealSlicer/GoldenCaseManifest.cs:17-20`) already documents this as "a
per-slice project setting the GUI applies at slice time." Reusing this means OpenFarm's C# layer never
needs to reimplement the slicer's own temperature-selection logic (no risk of drifting from the
slicer's actual behavior) — it only needs to (a) fail fast with a clear message when the data doesn't
exist, satisfying FR-006, and (b) pass the plate selection through, satisfying FR-005.

**Plate-type → JSON key mapping** (six values per the spec's Clarifications session; field names
confirmed both from the repo's own `tests/.../Fixtures/VendorIndex/OrcaSlicer/filament/
fdm_filament_common.json` and, more broadly, by directly inspecting the real, fully-installed
OrcaSlicer app's own bundled vendor profile set):

| `BuildPlateType` | Bed-temp key | OrcaSlicer `curr_bed_type` string |
|---|---|---|
| `Cool` | `cool_plate_temp` | `Cool Plate` |
| `Engineering` | `eng_plate_temp` | `Engineering Plate` |
| `HighTemp` | `hot_plate_temp` | `High Temp Plate` |
| `TexturedPei` | `textured_plate_temp` | `Textured PEI Plate` |
| `TexturedCool` | `textured_cool_plate_temp` | `Textured Cool Plate` |
| `Supertack` | `supertack_plate_temp` | `Supertack Plate` |

**This schema is confirmed vendor-agnostic, not Bambu-specific** — directly verified by inspecting nine
other OrcaSlicer-bundled vendors' own `fdm_filament_common.json` (Prusa, Creality, Qidi, Anycubic,
Elegoo, Artillery, FLSun, Snapmaker, Flashforge): every one of them declares the exact same six key
names (some leave several at `"0"`, meaning "not applicable," rather than omitting the key). This is an
OrcaSlicer-engine-level schema convention, not something each vendor invents independently, so this
mapping table needs no per-vendor variant even when a second brand is eventually added to
`PrinterModelCatalog` — a structural question worth settling explicitly here, since it directly
answers "does adding a new printer brand ever require touching this table" (no, unless OrcaSlicer's own
engine adds a seventh bed type).

A separate, unrelated field literally named `bed_type` (singular) also appears in several non-Bambu
vendors' `fdm_filament_common.json` (Prusa/Creality/Qidi/Anycubic/Elegoo/Artillery/FLSun all declare
`"bed_type": ["Cool Plate"]`; Snapmaker declares `"bed_type": ["Hot Plate"]`) — confirmed **not** the
same enum as `curr_bed_type` (its `"Hot Plate"` value isn't even one of the six real `curr_bed_type`
values) and not read anywhere by this feature; it looks like a legacy/PrusaSlicer-inherited
filament-level hint distinct from the plate-selection mechanism this feature uses, called out here so
a future implementer doesn't mistake it for the compatibility signal.

**Alternatives considered**: Parsing the winning `*_plate_temp` value in C# and writing a scalar
`bed_temperature`-style override directly — rejected: OrcaSlicer's real preset schema doesn't have a
single portable "the" bed-temp key; different builds vary a nozzle-vs-bed knob-set, and the CLI
already does this resolution correctly today whenever `curr_bed_type` is set — reimplementing it in
C# would be pure duplicated logic with a real chance of drifting from the slicer's own behavior, for
no correctness benefit (and the existing golden-file suite gives no coverage for a hand-rolled
extraction).

## R4: How must `ResolveSlicingProfileSelection`'s printer-tied and fleet-representative paths change?

**Decision**:
- `ResolveForPrinterAsync` (line 138) gets a third required field, mirroring the existing
  `MachineProfileId`/`ProcessProfileId` checks: `printer.BuildPlateType ?? throw new
  SlicingProfileUnconfiguredException($"Printer '{printer.Name}' has no build plate selected.")`
  (FR-006, spec Acceptance Scenario US2#3).
- `ResolveForFleetRepresentativeAsync` (line 157)'s `.Where(...)` predicate gains
  `&& p.BuildPlateType is not null`, so a fleet-representative pick always has a usable plate too
  (FR-007, spec Acceptance Scenario US2#4). The "arbitrary but stable smallest-Guid" tie-break stays
  unchanged.
- `ResolvedSlicingProfiles.ProcessSettingsJson` (the returned record, line 9) carries the
  `curr_bed_type`-patched JSON — no new field needed on the record itself.

**Rationale**: This is the smallest change that satisfies FR-005/FR-006/FR-007 without touching the
resolver's overall shape — the exact same "extend an existing required-field check, extend an
existing filter predicate" pattern 009-process-profiles used to add `ProcessProfileId` alongside
`MachineProfileId` in the first place.

## R5: How is the selection made/edited on the printer's page (FR-001, FR-003)?

**Decision**: Extend the single existing `EditPrinterCommand`/`EditPrinterRequest`/
`EditPrinterCommandHandler`/`EditPrinterEndpoint` (`Application/Printers/EditPrinterCommand*.cs`,
`Api/Endpoints/Printers/EditPrinterEndpoint.cs`) with one more field, `BuildPlateType?
BuildPlateType`, applied exactly as sent (`null` clears) — the same treatment `MachineProfileId`/
`ProcessProfileId` already get, **not** a new standalone endpoint.

**Validation** (in the handler, mirroring the existing machine/process compatibility check at
`EditPrinterCommandHandler.cs:69-96`, but simpler): when `command.BuildPlateType is { } requested`,
resolve `PrinterModelCatalog.Resolve(printer.Brand, printer.ModelCode).CompatiblePlateTypes` and
throw a new `IncompatibleBuildPlateException(requested, printer.Brand, printer.ModelCode)` (422) if
`requested` isn't in that set. Unlike process profile, there is **no** "requires machine profile
first" gate — per the spec's clarification, build plate has no machine-profile prerequisite at all
(`PrinterModelCatalog.Resolve` never fails here since every registered printer already passed it at
registration).

**Activity log** (FR-009): two new granular events, `PrinterBuildPlateSelected`/
`PrinterBuildPlateCleared`, written by the handler only when the value actually changed — the exact
"compare against `previousX`, write only on real change" pattern already used for
`PrinterMachineProfileLinked/Unlinked` (`EditPrinterCommandHandler.cs:145-159`). The base
`PrinterEdited` event the endpoint's `IActivityLoggable` implementation already emits on every
successful call covers "some edit happened"; the granular event is what makes "specifically the
build plate changed" queryable, matching 009's own reasoning documented on `EditPrinterCommand.cs`.

**Rationale**: Folding this into the existing edit-printer flow means the frontend's already-buffered
"select in the picker, commit on Save" UX (`PrinterFormPage.tsx`) extends by one more locally-buffered
field instead of wiring an entirely new mutation, exactly mirroring how 009 added process profile
onto the same endpoint machine profile already used.

**Exception → ProblemDetails mapping**: add
`IncompatibleBuildPlateException => (StatusCodes.Status422UnprocessableEntity, "Build plate
incompatible with printer model")` to the `switch` in `Api/ExceptionHandling/
DomainExceptionHandler.cs:21-71`, following the exact same one-line-per-type pattern already used for
every other domain exception there (e.g. `ProcessProfileIncompatibleWithMachineException`).

## R6: What does the read side (printer detail) need to expose?

**Decision**: Extend `PrinterDetail`/`GetPrinterDetailQueryHandler`
(`Application/Printers/GetPrinterDetailQuery.cs`) with two new fields:
- `BuildPlateType? BuildPlateType` — the printer's current selection (nullable, matches
  `MachineProfileId`).
- `IReadOnlyList<BuildPlateType> CompatibleBuildPlateTypes` — always populated (never null/empty for
  a registered printer) via `PrinterModelCatalog.Resolve(printer.Brand, printer.ModelCode)
  .CompatiblePlateTypes`, so the frontend can render the fixed choice list without a second
  round-trip and without waiting on a machine profile to be linked first (FR-002 Acceptance
  Scenario #2).

**Rationale**: Unlike `MachineProfileName`/`ProcessProfileName`, there's no "resolve a name via a
second repository lookup, tolerate a dangling FK" step needed — `PrinterModelCatalog.Resolve` cannot
fail for an already-registered printer (its Brand/ModelCode passed this exact same resolution at
registration and are immutable thereafter), so this read is a pure, non-throwing static lookup.

## R7: What does the frontend need (FR-001, SC-002)?

**Decision**:
- Orval-regenerate `endpoints.schemas.ts` to add a `BuildPlateType` string-union type,
  `EditPrinterRequest.buildPlateType?: string | null`, and on `PrinterDetailResponse`:
  `buildPlateType?: string | null` + `compatibleBuildPlateTypes?: string[]`.
- `PrinterFormPage.tsx`'s `EditPrinterFormLoaded` gets a third selector, buffered the same way as
  `selectedMachineProfileId`/`selectedProcessProfileId` (local `useState`, committed into the same
  `editMutation.mutate({ ..., buildPlateType: selectedBuildPlateType })` call on Save) — **not** a
  third `ProfilePickerCombobox` instance. `ProfilePickerCombobox` fetches a paginated profile list
  over the network by `kind`; build plate is a small (≤6-item) fixed client-side list scoped by
  `compatibleBuildPlateTypes`, so a plain shadcn `<Select>` is the right-sized primitive (still from
  `src/components/ui/`, per CLAUDE.md's "no feature invents its own... component pattern"). No
  `disabled`/prerequisite gate — available as soon as the printer detail loads, unlike process
  profile's `disabled={!selectedMachineProfileId}`.
- `PrinterDetailPage.tsx`'s read-only "Machine & process profile" card gets a third row ("Build
  plate: `<value>` / Not selected"), same style as the existing two.

**Rationale**: Directly implements FR-001 ("same place and manner machine/process profile are
selected") while respecting that build plate is a fixed enum, not a fetched profile — reusing
`ProfilePickerCombobox` here would be a shape mismatch (it assumes a paginated, name-searchable,
server-fetched list) and would misleadingly suggest build plate is a fourth profile kind, which
FR-004/FR-010 explicitly rule out.

## R8: Testing approach (constitution Principle II — TDD where it pays)

**TDD required** (deterministic, well-specified logic):
- The `BuildPlateType` → JSON-key mapping and "no value recorded" detection inside
  `ResolveSlicingProfileSelection` (R3) — a pure function of enum + JSON in, exception-or-patched-JSON
  out.
- The compatibility gate in `EditPrinterCommandHandler` (R5) — same class of non-trivial validation
  gate `ProcessProfileIncompatibleWithMachineException`'s existing tests already cover with TDD.

**Test-after** (per constitution, explicitly not required): the EF configuration/migration, the
`GetPrinterDetailQuery` read-side wiring, and the React component/page changes — extending the
existing `EditPrinterCommandHandlerTests.cs`, `EditPrinterEndpointTests.cs`, `PrinterFormPage.test.tsx`
and `ProfilePickerCombobox.test.tsx`-style harnesses already used for machine/process profile.

**SC-003 verification** ("two printers of the same model with different build plates produce
different resolved bed temperatures... verifiable in a single test"): a focused unit test on
`ResolveSlicingProfileSelection` asserting the returned `ProcessSettingsJson`'s `curr_bed_type` value
differs between two `BuildPlateType` selections for the same underlying filament/machine/process — no
real slicer invocation needed (mirrors, but does not require extending, the existing
`RealSlicerGoldenFileTests` golden-file suite, since that suite is reserved for hardware/CLI-level
regression coverage, not for exercising the resolver's own branching logic).

## R9: Does supporting a future non-Bambu printer brand require a structural change?

**Decision**: No structural change is needed. The design in R1–R8 already generalizes to any
OrcaSlicer-based vendor without modification, verified by directly inspecting ten real vendor bundles
(Bambu plus nine others: Prusa, Creality, Qidi, Anycubic, Elegoo, Artillery, FLSun, Snapmaker,
Flashforge) rather than assuming from the spec's Bambu-only examples:

- The `BuildPlateType` enum and its JSON-key mapping (R3) are already vendor-agnostic — every vendor
  checked uses the identical six key names, so a new brand never requires a new mapping-table variant.
- `PrinterModelCatalog.CompatiblePlateTypes` (R2) is already a per-`(Brand, ModelCode)` hand-authored
  field using the catalog's own established "add a new entry, never a rewrite" extension pattern (the
  same one `VendorMachineModelName` already relies on) — onboarding e.g. Snapmaker U1 later is adding
  one `PrinterModel` record with its own `CompatiblePlateTypes` (sourced via R2's two-signal method:
  `Snapmaker/machine/Snapmaker U1.json` has no `not_support_bed_type`, so it falls back to
  `Snapmaker/filament/fdm_filament_common.json`'s temp-key data — confirmed directly: only
  `hot_plate_temp` is non-zero there, so `Snapmaker U1`'s compatible set would resolve to `{ HighTemp
  }` alone), not a schema or architecture change.
- `PrinterBrand` (`Domain/Printers/PrinterModel.cs`) already exists specifically to be extended this
  way (`{ Bambu, System }` today) — adding `Snapmaker` is a one-line enum addition plus new catalog
  entries, the same shape of change `PrinterModelCatalog`'s own doc comment already documents as the
  expected way to grow it.

**This directly answers the "reuse the High Temp Plate enum for Snapmaker's plate?" question from
earlier in this session**: no reuse/aliasing decision is needed at all. Snapmaker's own vendor data
uses the *same* `hot_plate_temp` key OrcaSlicer/Bambu already use for `HighTemp` — it isn't a
differently-shaped vendor concept requiring a mapping choice, it's the identical schema field, already
covered by R3's table. (This corrects an earlier answer given in this session, before the real vendor
bundle was inspected, which incorrectly guessed the mapping table would need to become
vendor-keyed.) Separately, Snapmaker's own vendor-authored `bed_type: ["Hot Plate"]` field (R3) is *not*
a `curr_bed_type` value at all and must not be confused with the actual compatibility signal.

**Rationale**: matches the constitution's Principle IV ("Build for Expansion, Not Speculation" —
"extraction of shared structure is deferred until a second real instance creates actual duplication ...
designed extension points are required, preemptive abstraction is not"). `PrinterModelCatalog` already
*is* the designed extension point; building a dynamic, ingestion-time compatibility-derivation pipeline
now (reading `not_support_bed_type`/temp-keys automatically at `SystemProfileIngestionService` runtime
instead of hand-authoring the catalog) would be exactly the kind of preemptive abstraction the
constitution warns against, for a codebase whose `PrinterModelCatalog` currently has exactly one real
brand (`Bambu`) — there is no second real instance yet to generalize from.

**Alternatives considered**: Building the dynamic derivation pipeline now, so any future brand's
compatibility "just works" from ingestion with no hand-authoring step — rejected for now per the
constitution's anti-speculation principle above; noted here as the natural next step *if and when* a
second brand is actually onboarded, so the option isn't lost, just deferred.

## Summary — resolved unknowns

| Area | Resolution |
|---|---|
| Storage of selection | `Printer.BuildPlateType` (nullable enum, `HasConversion<string>()`) |
| Fixed plate set | **Six** values: Cool, Engineering, HighTemp, TexturedPei, TexturedCool, Supertack |
| Compatibility data | `PrinterModel.CompatiblePlateTypes`, hand-authored per catalog entry |
| Compatibility sourcing signal | `not_support_bed_type` (authoritative when declared) → filament temp-key differentiation (fallback) |
| Bed-temp resolution | Pre-flight JSON-key check + `curr_bed_type` injection into `ProcessSettingsJson` |
| Plate-key schema portability | Confirmed vendor-agnostic across 10 real OrcaSlicer vendor bundles — no per-vendor mapping variant needed |
| Printer-tied slice gate | `ResolveForPrinterAsync` requires `BuildPlateType` set |
| Fleet-representative gate | `ResolveForFleetRepresentativeAsync` filter requires `BuildPlateType` set |
| Edit surface | Extends existing `EditPrinterCommand`/`EditPrinterEndpoint`, no new endpoint |
| Activity log | New granular `PrinterBuildPlateSelected`/`Cleared` events, same pattern as 009 |
| Read surface | `PrinterDetail` gains `BuildPlateType` + always-populated `CompatibleBuildPlateTypes` |
| Frontend control | Plain shadcn `<Select>`, not `ProfilePickerCombobox` (not a fetched profile) |
| Future non-Bambu brand support | No structural change needed — `PrinterModelCatalog` is already the designed extension point (R9) |
| Testing | TDD for the temp-key mapping + compatibility gate; test-after for everything else |
