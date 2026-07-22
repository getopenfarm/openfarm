# Quickstart & Validation: Process Profiles — System & User Profiles for Machine, Process, and Filament

Runnable scenarios that prove the feature end-to-end. References `contracts/profiles-api.md`,
`contracts/system-profile-ingestion.md`, and `data-model.md` rather than restating them. Commands assume
the repo conventions in `CLAUDE.md`.

## Prerequisites

- .NET 10 SDK, Bun, Docker (Testcontainers / local stack); `ConnectionStrings__Default` set.
- A bundled slicer already provisioned per `008-orca-slicer-multi-version`'s own quickstart
  (`dotnet run --project tools/provision-slicers -- --manifest slicers.manifest.json --out
  "$SlicerBundle__RootDirectory" --platform linux`) — this feature ingests **from** that bundle, it doesn't
  provision one.
- At least one printer registered with a supported model (e.g. `Bambu`/`X1C`).

## Setup — ingest System Profiles (User Story 1)

1. Run the ingestion step (exact invocation per `contracts/system-profile-ingestion.md`'s chosen trigger
   point — settled in tasks.md):

   ```bash
   dotnet run --project api/src/OpenFarm.Api -- ingest-system-profiles
   ```

2. **Expected**: a summary reporting vendors scanned, vendors skipped (unsupported models), and rows
   created per kind. `GET /api/v1/profiles/machine/search?q=X1` returns rows with `origin: "System"`,
   `vendorName: "Bambulab"`.
3. Query `GET /api/v1/profiles/process/search?compatibleWithMachineName=Bambu%20Lab%20X1%20Carbon%200.4%20nozzle`
   → returns only process presets whose `compatibleMachineNames` includes that exact machine preset name.
4. Re-run step 1 with unchanged bundle data → summary reports zero created/updated (idempotent, Acceptance
   Scenario 5).
5. Confirm scoping: a vendor in the bundle whose `machine_model_list` has no overlap with
   `PrinterModelCatalog.All`'s `VendorMachineModelName` values produces **zero** rows for that vendor
   (Acceptance Scenario 3) — verify via the summary's "vendors skipped" count.

## Story 2 — Select a printer's machine and process profile

1. Open a printer's page (`PUT /api/v1/printers/{id}/machine-profile`, existing 005 endpoint) → select a
   `System`-labeled machine profile from `GET /api/v1/profiles/machine/search`.
2. `GET /api/v1/printers/{id}/process-profile-choices` → `available: true`, items limited to
   process profiles compatible with the just-selected machine (Acceptance Scenario 2).
3. `PUT /api/v1/printers/{id}/process-profile` with a compatible `processProfileId` → `200`; verify the
   activity log records the change (FR-016).
4. Attempt the same call with a process profile **not** in that machine's compatible set → `422`
   (Acceptance Scenario, FR-006).
5. On a printer with no machine profile selected: `GET .../process-profile-choices` → `available: false`
   (FR-007, Acceptance Scenario 5).
6. Repeat steps 1–3 on a **second** printer of the same model with a **different** machine/process pair →
   confirm both printers keep independent selections (Acceptance Scenario 4, SC-007).

## Story 3 — Slice using fleet-derived profiles, no org defaults

1. With two printers of different models each fully configured (Story 2), trigger a slice targeted at each
   → confirm each slice's recorded machine/process match that printer's own selections (Acceptance
   Scenario 1, SC-001).
2. Clear one printer's process selection (`PUT .../process-profile` with `null`) and request a slice for
   it → refused, message names that printer and the missing process selection, no partial slice (Acceptance
   Scenario 2, SC-003).
3. Confirm `Organization` no longer exposes a default-machine-profile field anywhere (schema/API) and that
   no code path reads one (FR-009, SC-005) — `grep -r DefaultMachineProfileId api/src` returns nothing.
4. Request an estimate-only slice (no target printer, e.g. at part upload) with a non-empty fleet where at
   least one printer has both selections set → confirm it resolves via the fleet-representative rule
   (research.md §5) rather than failing (Acceptance Scenario 4, FR-015). With an empty fleet → refused with
   "no printers configured."
5. Request a slice for a material type with no narrowed part filament requirement → confirm it
   auto-resolves the deterministic representative System filament profile (research.md §2) with zero reads
   of any settings table (FR-018, SC-011); repeat and confirm the identical profile is chosen both times
   (FR-019).

## Story 4 — Narrow a part's filament requirement, from the part detail page

1. Open a part with an unnarrowed filament requirement; call the new
   `PUT /api/v1/parts/{id}/filament-requirements/{index}/narrow` with a `System`-labeled filament profile
   id → `200`, requirement reflects the narrowed choice (Acceptance Scenario 1/2).
2. Clear it (`narrowFilamentProfileId: null`) → `200`, activity log records the clear (Acceptance Scenario
   3, FR-016).

## Removal verification (FR-009/FR-011/FR-013/FR-017, SC-005)

- `GET /api/v1/settings/slicing-profile-defaults` → `404` (route no longer registered).
- The Machine/Filament Profile import/browse page (feature 003) still lists only `origin: "User"` rows —
  confirm a freshly-ingested `System` machine profile does **not** appear there (FR-013).
- No UI surface offers importing or hand-editing a process profile anywhere in the app (FR-011).

## Fresh-install check (SC-004)

On a fresh install with a bundled slicer provisioned, a supported-model printer registered, and **zero**
user-imported profiles: complete Story 2 (select System machine + process) and Story 4 (leave filament
unnarrowed, rely on Story 3 step 5's auto-resolution) and reach a successful slice — no import required for
any of the three profile kinds.
