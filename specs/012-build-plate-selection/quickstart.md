# Quickstart & Validation: Build Plate Selection

Runnable scenarios that prove the feature end-to-end. References `contracts/printers-api.md` and
`data-model.md` rather than restating them. Commands assume the repo conventions in `CLAUDE.md`.

## Prerequisites

- .NET 10 SDK, Bun, Docker (Testcontainers / local stack); `ConnectionStrings__Default` set.
- At least two printers registered with a supported model whose `PrinterModelCatalog` entry has a
  non-empty `CompatiblePlateTypes` set (e.g. `Bambu`/`A1`, compatible with `TexturedPei`/`HighTemp` —
  vendor-confirmed per `research.md` R2).
- At least one `System`-origin `FilamentProfile` ingested for that model (`009-process-profiles`'s
  own quickstart) whose raw data declares real (non-`"0"`) values for at least two of the six
  plate-temp keys.

## Story 1 — Select a printer's build plate on its configure page

1. Register (or reuse) a printer with **no** machine profile linked at all.
2. `GET /api/v1/printers/{id}` → `compatibleBuildPlateTypes` is present and non-empty, driven solely
   by the printer's registered `brand`/`modelCode`; `buildPlateType` is `null`; `machineProfileId` is
   `null`. **Expected**: build plate choices are available even with no machine profile linked
   (Acceptance Scenario 2).
3. `PATCH /api/v1/printers/{id}` with `{ ..., "buildPlateType": "TexturedPei" }` (a value present in
   step 2's `compatibleBuildPlateTypes`) → `200`.
4. `GET /api/v1/printers/{id}` again → `buildPlateType: "TexturedPei"` persists (Acceptance Scenario 3).
5. Check the activity log for that printer → a `PrinterBuildPlateSelected` entry is present (FR-009,
   Acceptance Scenario 5).
6. `PATCH` again with a `buildPlateType` **not** in `compatibleBuildPlateTypes` (e.g. `"Cool"` on an
   A1) → `422 Build plate incompatible with printer model` (FR-002).
7. `PATCH` again with `{ ..., "buildPlateType": null }` → `200`; `GET` confirms `buildPlateType: null`;
   activity log gains a `PrinterBuildPlateCleared` entry.
8. Register a **second** printer of the same model, select a **different** compatible build plate →
   confirm both printers independently retain their own selections (Acceptance Scenario 4, SC-007
   variant for same-model/different-plate).

## Story 2 — Slice using the bed temperature for the printer's actual installed plate

1. Configure two printers of the same model (Story 1) with the same machine, process, and filament
   selections, but **different** build plates (e.g. `TexturedPei` vs `HighTemp`).
2. Trigger a slice for printer A → inspect the resolved process settings passed to the slicer contain
   `"curr_bed_type": "Textured PEI Plate"` (matching printer A's selection).
3. Trigger a slice for printer B → `"curr_bed_type": "High Temp Plate"` (matching printer B's
   selection) — **different** from step 2's value for the same underlying filament (SC-003).
4. On a printer with no build plate selected, trigger a slice → `422 Slicing profile unconfigured`,
   `detail` names the specific printer and states no build plate is selected — no slice is produced
   (FR-006, Acceptance Scenario 3).
5. Select a build plate on a printer whose resolved filament preset has no non-`"0"` value for that
   plate's temp key (or use a filament preset ingested only with common defaults for a different
   plate) → trigger a slice → `422 Slicing profile unconfigured`, `detail` names both the filament
   preset and the plate type — no slice is produced (FR-006, spec Edge Cases).
6. With an otherwise-empty fleet (no printer has machine + process + build plate all set), trigger an
   estimate-only slice (no target printer) → `422`, same "no printers configured" refusal already used
   for machine/process, now also requiring build plate (FR-007).
7. Add one fully-configured printer (machine + process + build plate) to that fleet → repeat the
   estimate-only slice → succeeds, using that printer's own build plate selection as the
   fleet-representative pick.

## Regression checks

- Re-run `EditPrinterEndpointTests.cs`'s existing machine/process profile cases → unaffected by the
  new field (build plate is independent, FR-008).
- Re-run the existing `RealSlicerGoldenFileTests` suite → unaffected (its own `WithBedTypeOverride`
  test helper remains test-only scaffolding; production code now reaches the same `curr_bed_type` key
  through `ResolveSlicingProfileSelection` instead).
- `dotnet format api/OpenFarm.slnx --verify-no-changes`, `bun run lint` (web/) — both clean.
