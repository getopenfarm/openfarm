# Quickstart: Validating Fleet- and Inventory-Aware Filament Matching

## Prerequisites

- Local full stack running (`cd infra && docker compose up`) or `dotnet test`/manual API calls against a
  seeded organization.
- System Profile ingestion has run at least once (`009-process-profiles`'s provisioning step) so at least one
  System filament profile with `MaterialType = "PLA"` exists.
- At least one printer registered in the fleet (`002-printer-integration`), and the filament-tracking feature
  (`006-filament-tracking`) available to register Vendors/Filaments/Spools and load/unload them onto feed
  slots.

## Scenario 1 — The bug is fixed: adding an STL no longer fails (US1)

1. With no printer configured and no filament inventory registered at all, upload any `.stl` file via
   `POST /api/v1/parts`.
2. Poll the returned job until processing completes (or check `GET /api/v1/parts/{id}` once confirmed).
3. **Expect**: no `SlicingProfileUnconfiguredException`/422 naming `"Generic PLA"` or any literal profile
   name. The job/part resolves successfully, with `estimatedFilamentMatchSource: "SystemCatalog"`.
4. Repeat against an organization where PLA System ingestion genuinely has not run (or seed one with zero
   PLA System profiles). **Expect**: a clear failure naming the material `"PLA"` as unresolved — not the
   old, misleading `"Generic PLA"` wording.

## Scenario 2 — Fleet-loaded filament wins over the catalog (US2)

1. Register a Vendor, a Filament (`materialType: "PLA"`, with `filamentProfileId` set to an existing System
   or User `FilamentProfile` id), and a `FilamentVariant`.
2. Register a Spool of that variant and load it onto a feed slot on Printer A
   (`POST` the load-spool-onto-feed-slot command from `006-filament-tracking`).
3. Upload an STL with no printer targeted.
4. **Expect**: `estimatedFilamentMatchSource: "FleetLoaded"`, `estimatedFilamentMatchLabel: "Fleet: <Printer
   A's name>"` — not a generic System pick, even though System PLA profiles also exist.
5. Repeat with a second printer, Printer B, also loaded with a *different* PLA spool. **Expect**: resolution
   still picks exactly one (deterministically — re-run the same request and confirm the same printer's spool
   is chosen both times).
6. If a job/estimate is instead requested *for Printer B specifically* (a printer-targeted resolution, once a
   caller supplies `printerId`), **expect** Printer B's own loaded spool to win over Printer A's, even if
   Printer A's spool would otherwise have won the fleet-wide tie-break.

## Scenario 3 — Filament library, then catalog fallback (US3)

1. Unload Printer A's spool from Scenario 2 back to storage (or start fresh with no printer loaded).
2. Register a second Spool of a PLA variant, left `InStorage` (never loaded).
3. Upload an STL. **Expect**: `estimatedFilamentMatchSource: "FilamentLibrary"`.
4. Remove/deplete that stored spool (or use an organization with no PLA spools anywhere). **Expect**: falls
   back to `"SystemCatalog"`, exactly as in Scenario 1.
5. Register a stored PLA spool whose `Filament.FilamentProfileId` is left unset (no link). **Expect**: that
   spool is skipped — resolution proceeds to the next eligible candidate or the catalog fallback, never an
   error.

## Scenario 4 — Estimate reflects the match, and its source is visible (US4)

1. Add the same STL twice: once under Scenario 2's fleet-loaded setup, once under an organization with only a
   generic System PLA profile (no owned spool anywhere).
2. Compare `GET /api/v1/parts/{id}` for both. **Expect**: `estimatedFilamentMatchSource`/
   `estimatedFilamentMatchLabel` differ (`"FleetLoaded"`/`"Fleet: ..."` vs. `"SystemCatalog"`/`"System:
   ..."`), and (if the two filament profiles' own settings genuinely differ) the estimate values themselves
   may differ too.
3. Confirm the part detail page (frontend) renders the label without needing to navigate elsewhere — visual
   check against `SC-003`.

## Regression checks

- A part whose filament requirement was manually narrowed (`007-part-library`'s
  `filamentRequirementOverrides`) still uses the narrowed profile directly — `estimatedFilamentMatchSource`
  stays whatever it was before narrowing (or `null`, if never matched), never overwritten by the matching
  capability (`FR-011`).
- `dotnet test api/OpenFarm.slnx --filter "FullyQualifiedName~RepresentativeFilamentResolver|FullyQualifiedName~SlicerOptions|FullyQualifiedName~ResolveSlicingProfileSelection"` — existing unit/integration tests for the touched classes still pass (adjusted for the new signature, per `data-model.md`).
- `dotnet format api/OpenFarm.slnx --verify-no-changes` and `bun run lint` (web) stay clean.
