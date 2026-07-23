# Quickstart: Validating Fleet- and Inventory-Aware Filament Matching

## Prerequisites

- Local full stack running (`cd infra && docker compose up`) or `dotnet test`/manual API calls against a
  seeded organization.
- System Profile ingestion has run at least once (`009-process-profiles`'s provisioning step) so at least one
  System filament profile with `MaterialType = "PLA"` exists.
- At least one printer registered in the fleet (`002-printer-integration`), and the filament-tracking feature
  (`006-filament-tracking`) available to register Vendors/Filaments/Spools and load/unload them onto feed
  slots.

This feature has no API-visible response change (per the decision to defer match-source visibility, see
`research.md` R6) — verification below checks either the *estimate outcome* (via `GET /api/v1/parts/{id}`,
same fields as today) or the matching capability's own return value directly at the test level.

## Scenario 1 — The bug is fixed: adding an STL no longer fails (US1)

1. With no printer configured and no filament inventory registered at all, upload any `.stl` file via
   `POST /api/v1/parts`.
2. Poll the returned job until processing completes (or check `GET /api/v1/parts/{id}` once confirmed).
3. **Expect**: no `SlicingProfileUnconfiguredException`/422 naming `"Generic PLA"` or any literal profile
   name. The job/part resolves successfully with a normal estimate.
4. Repeat against an organization where PLA System ingestion genuinely has not run (or seed one with zero
   PLA System profiles). **Expect**: a clear failure naming the material `"PLA"` as unresolved — not the
   old, misleading `"Generic PLA"` wording.

## Scenario 2 — Fleet-loaded filament wins over the catalog (US2)

1. Register a Vendor, a Filament (`materialType: "PLA"`, with `filamentProfileId` set to an existing System
   or User `FilamentProfile` id whose settings are distinguishable from any generic System PLA profile), and
   a `FilamentVariant`.
2. Register a Spool of that variant and load it onto a feed slot on Printer A
   (`POST` the load-spool-onto-feed-slot command from `006-filament-tracking`).
3. Upload an STL with no printer targeted.
4. **Expect** (test-level, calling the matching resolver directly): the returned `FilamentMatchResult.Source`
   is `FleetLoaded` and `.Profile` is the one linked from Printer A's loaded spool's `Filament` — not an
   unrelated System catalog pick, even though System PLA profiles also exist. At the API level, the part's
   estimate reflects that specific profile's settings.
5. Repeat with a second printer, Printer B, also loaded with a *different* PLA spool. **Expect**: resolution
   still picks exactly one (deterministically — re-run the same request and confirm the same printer's spool
   is chosen both times).
6. Request resolution *for Printer B specifically* (passing `printerId`). **Expect** Printer B's own loaded
   spool to win over Printer A's, even if Printer A's spool would otherwise have won the fleet-wide
   tie-break.

## Scenario 3 — Filament library, then catalog fallback (US3)

1. Unload Printer A's spool from Scenario 2 back to storage (or start fresh with no printer loaded).
2. Register a second Spool of a PLA variant, left `InStorage` (never loaded).
3. Resolve for PLA. **Expect**: `FilamentMatchResult.Source == FilamentLibrary`, `.Profile` is the one
   linked from that stored spool's `Filament`.
4. Remove/deplete that stored spool (or use an organization with no PLA spools anywhere). **Expect**: falls
   back to `SystemCatalog`, exactly as in Scenario 1.
5. Register a stored PLA spool whose `Filament.FilamentProfileId` is left unset (no link). **Expect**: that
   spool is skipped — resolution proceeds to the next eligible candidate or the catalog fallback, never an
   error.

## Regression checks

- A part whose filament requirement was manually narrowed (`007-part-library`'s
  `filamentRequirementOverrides`) still uses the narrowed profile directly — the matching capability is never
  invoked for that part at all (`FR-011`, already guaranteed by `ResolveSlicingProfileSelection`'s existing
  branch).
- `dotnet test api/OpenFarm.slnx --filter "FullyQualifiedName~RepresentativeFilamentResolver|FullyQualifiedName~SlicerOptions|FullyQualifiedName~ResolveSlicingProfileSelection"` — existing unit/integration tests for the touched classes still pass (adjusted for the new signature, per `data-model.md`).
- `dotnet format api/OpenFarm.slnx --verify-no-changes` stays clean. No frontend/`bun run lint` impact — this
  feature touches no `web/` code.
