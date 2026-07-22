# Implementation Plan: Process Profiles — System & User Profiles for Machine, Process, and Filament

**Branch**: `009-process-profiles` | **Date**: 2026-07-22 | **Spec**: [spec.md](./spec.md)

**Input**: Feature specification from `/specs/009-process-profiles/spec.md`

## Summary

The interim fix bolted process profiles on wrong: process presets were user-imported/hand-edited data, and
slicing read one organization-wide "default machine/process/filament" setting — which can never be correct
for a multi-model fleet. This feature makes **process profiles exclusively System-sourced** — ingested
directly from the bundled slicer's own vendor profile data (`resources/profiles/<Vendor>.json`, verified
against a real OrcaSlicer v2.4.2 release, research.md §0) — extends that same ingestion to machine and
filament profiles too (so User-imported profiles' previously-dead-ended base references can resolve), and
makes **all slicing resolution fleet-driven**: a printer selects its own machine and process profile
(replacing the org-level default-machine/default-process settings and the machine→process pairing), a
part's filament requirement narrows to a specific profile or auto-resolves a deterministic representative
System filament profile per material type (replacing the org-level default-filament-per-material setting).
The entire interim "Slicing Profile Defaults" settings page is removed.

HTTP surface is kept deliberately small (contracts/profiles-api.md, revised after review): no new
`/search` routes — the three existing `GET /api/v1/profiles/{machine,process,filament}` endpoints gain
optional query params instead; no separate process-profile-link or process-profile-choices endpoints — both
fold into the existing `PATCH /api/v1/printers/{id}` (which already handles name + optional connection
details) and the extended `GET .../profiles/process?printerId=` respectively; and the part filament-
narrowing endpoint addresses a requirement by its own real `Id` (already returned by `GetPartEndpoint`)
rather than an invented positional index.

Technical approach: add `Origin`/`VendorName`/`ExternalId`/`IsCurrentlyOffered`/`IsSelectable` (+
kind-specific `CompatibleMachineNames`/`MaterialType`/`VendorMachineModelName`/`IncludedTemplateNames`)
columns to the three existing profile tables rather than new System-specific tables (research.md §3) —
System and User rows share one table per kind, so base-reference resolution (FR-014) and the existing
`ProfileChainWalker`/`ProfileRawDataMerger` merge machinery work unchanged across origins, extended once
(research.md §0a/§5) to also fold in Bambu Studio's `include`-referenced gcode-template fragments — a real,
independently-verified fork divergence from OrcaSlicer (which uses none) that would otherwise silently drop
`machine_start_gcode`/`change_filament_gcode`/etc. from every Bambu-Studio-sourced slice. Add
`Printer.ProcessProfileId` parallel to the existing `Printer.MachineProfileId`. Add a new,
provisioning-time (or on-demand) ingestion step that walks a bundled slicer's vendor index files,
cross-references `machine_model_list` against a hand-authored `PrinterModelCatalog.VendorMachineModelName`
mapping (research.md §0), filters fetched presets by their own declared `type` (not just list membership —
Bambu Studio's `machine_list` is 60% untyped gcode-template fragments, research.md §0a), and upserts rows
idempotently by `(VendorName, ExternalId-or-Name)` (research.md §1). Rewrite `ResolveSlicingProfileSelection`
to resolve per-printer (or per-fleet-representative for estimate-only slices) instead of
per-organization-default (research.md §5), and delete `Organization.DefaultMachineProfileId`,
`MachineProfile.DefaultProcessProfileId`, and the `OrganizationFilamentDefault` entity/table/repository/
endpoints entirely.

## Technical Context

**Language/Version**: C# 13 on .NET 10 (backend); TypeScript 5 (`strict`) on React 19, built with Bun +
Vite (frontend). No new language/runtime.

**Primary Dependencies**: FastEndpoints + FluentValidation, EF Core 10 + Npgsql, Finbuckle.MultiTenant —
all existing. No new backend package. Frontend: React Router v7, TanStack Query, Orval-generated client,
react-hook-form + zod, shadcn/ui, TanStack Table (for the searchable picker lists at SC-010 scale) — all
existing.

**Storage**: PostgreSQL via `OpenFarmDbContext`. Schema deltas: new columns on `MachineProfiles`,
`ProcessProfiles`, `FilamentProfiles` (incl. `IncludedTemplateNames jsonb` on `MachineProfiles`/
`FilamentProfiles` only, research.md §0a), `Printers`; dropped `Organizations.DefaultMachineProfileId` and
`MachineProfiles.DefaultProcessProfileId`; dropped `OrganizationFilamentDefaults` table entirely
(data-model.md). System Profile *content* (the ingested vendor data) lives in these same tables as
ordinary rows — no separate store.

**Testing**: xUnit + FluentAssertions (Domain/Application unit — TDD for the deterministic new logic: the
idempotency-key upsert matcher, the representative-filament tie-break, the fleet-representative-printer
pick, the machine↔process compatibility gate, the `include` fold-in merge step); `WebApplicationFactory` +
Testcontainers.PostgreSql (API integration — ingestion against real fixture vendor JSON, end-to-end printer
selection, slice resolution); Vitest + React Testing Library (frontend pickers, printer page, part detail
narrowing). Real ingestion against the actual bundled binary's on-disk `resources/profiles/` tree is
exercised in integration tests using the same real, MIT-licensed vendor JSON fixtures feature 003 already
established the precedent for (`api/tests/.../Profiles/Fixtures/`) — extended with a small real `BBL.json` +
a handful of real machine/process/filament preset files **from both bundled engines, not OrcaSlicer alone**
(this plan's own research.md §0/§0a fetches) — specifically including at least one Bambu-Studio-sourced
machine and filament fixture that exercises `include`, since that path has no OrcaSlicer-sourced analogue to
catch it.

**Target Platform**: Same self-hosted Linux/Windows/macOS hosts as 008. Ingestion runs against whichever
platform leaf `BundledSlicerScanner` already resolves for the host — no new platform-specific code beyond
the recursive `resources/profiles` search already justified by `SlicerExecutableResolver`'s precedent.

**Project Type**: Web application (ASP.NET Core API + React SPA) in the existing monorepo — no new
top-level project. Ingestion is a new step inside the existing runtime (`OpenFarm.Api`/`OpenFarm.Infrastructure`),
not a new standalone tool like `tools/provision-slicers` (unlike binary provisioning, ingestion needs a DB
connection, so it belongs where the DB context already lives, not in the network-fetching-only tool that
FR-018-of-008 deliberately keeps DB-free).

**Performance Goals**: Ingestion is a one-shot batch job over on-disk JSON (thousands of small files for a
real vendor) — expected to complete in low single-digit seconds to a couple minutes for Bambu's ~2,500
combined presets; not on any request-latency-sensitive path. Picker queries (SC-010) must stay responsive
(sub-second) against a 2,000+-row filament table via ordinary indexed `WHERE`/`ILIKE` queries and
server-side pagination (research.md §6) — no client-side full-table fetch.

**Constraints**: Zero organization-level default machine/process/filament settings may remain anywhere
(FR-009/FR-017, SC-005) — this is a hard removal, not a feature-flagged deprecation. Process profiles MUST
remain exclusively System-sourced with no import/hand-author surface (FR-011) — enforced by never wiring a
`User`-origin write path for `ProcessProfile`, not by a runtime check. Every profile picker offering both
origins MUST label each entry (FR-004/FR-012, SC-008). Re-ingestion MUST be idempotent and non-destructive
to existing FK references (research.md §1) — a printer's or part's selection is never silently invalidated
by a re-run, only flagged stale if genuinely gone from the re-ingested set.

**Scale/Scope**: A single vendor (Bambu Lab) today, ~2,500 combined System Profile rows; the ingestion
scoping mechanism (catalog cross-reference, not a hardcoded vendor name) means a second vendor's rows
appear automatically once `PrinterModelCatalog` gains that vendor's models, with no ingestion-logic change
(FR-001/Acceptance Scenario 4) — scale grows accordingly and the search/pagination design (research.md §6)
is sized for that, not just today's single vendor.

## Constitution Check

*GATE: Must pass before Phase 0 research. Re-checked after Phase 1 design — still passing; no violations.*

- **I. Clean Architecture, enforced.** New `ProfileOrigin` enum and the extended profile/`Printer`
  properties live in `Domain`; the search/ingestion contracts (`ISystemProfileIngestionService`,
  extended repository interfaces) live in `Application`; the ingestion implementation (vendor JSON
  parsing, filesystem walk, EF upsert) and repository EF implementations live in `Infrastructure`; `Api`
  only adds thin endpoints/DTOs per `contracts/profiles-api.md`. No `EF Core`/`FastEndpoints` leaks into
  `Domain`/`Application`. **PASS.**
- **II. Test discipline where it pays.** TDD for: the upsert-by-idempotency-key matcher, the
  representative-filament tie-break (research.md §2), the fleet-representative-printer pick (research.md
  §5), the machine↔process compatibility gate, the vendor-index → System-Profile field mapping, and the
  `IncludedTemplateNames` fold-in merge (research.md §0a — deterministic, well-specified transforms, and the
  one path with no OrcaSlicer-sourced equivalent to catch a regression, so it needs its own direct
  coverage). Test-after/fixture-based for the ingestion filesystem walk itself, the EF repository/
  configuration code, and React components — consistent with how feature 003 already tests
  `ProfileBundleParser` (fixture-replay, test-after) rather than TDD. **PASS.**
- **III. Consistent UX.** New pickers gate on the `permissions` catalog, never role names; all new errors
  surface as RFC 7807 `ProblemDetails`; forms use react-hook-form + zod + shadcn/ui primitives; the printer
  page's machine/process selections and part detail's filament narrowing are not currently real-time-pushed
  data, so no new SignalR obligation is introduced (consistent with how `MachineProfileLinkCard` behaves
  today). Every combined System+User list labels origin uniformly (SC-008) via one shared frontend list-item
  component, not a per-page bespoke rendering. **PASS.**
- **IV. Build for expansion, not speculation.** Ingestion scoping reads `PrinterModelCatalog.All` rather
  than hard-coding "Bambu" anywhere (FR-001/Acceptance Scenario 4) — the one deliberate extension point
  this feature is built around. Reusing the existing three profile tables (research.md §3) instead of
  forking new System-specific ones is itself an application of "extraction of shared structure is deferred
  until duplication is real" — here, the *opposite* direction: two natural instances (System, User) already
  need identical fields (`BaseProfileId` resolution, `RawDataJson`, chain merge), so sharing the table is
  the non-speculative choice, not a new abstraction. `organization_id` stays present on every touched
  table. **PASS.**
- **V. Performance & durable recovery.** Ingestion is a one-shot batch step with no non-terminal
  persisted per-row state machine — a row is either upserted or it isn't, and a mid-run failure on one
  vendor file doesn't strand a partially-written row (each preset is a single upsert transaction, not a
  multi-step in-flight status); no new `BackgroundService` non-terminal state is introduced, so the
  Durable Recovery guardrail's reconciliation-pass/catch-every-failure/enqueue-never-drops rules don't gain
  new surface to violate. Picker queries use server-side pagination (SC-010) rather than an unbounded
  render, consistent with the existing TanStack Virtual/pagination discipline. **PASS.**

No entries required in Complexity Tracking.

## Project Structure

### Documentation (this feature)

```text
specs/009-process-profiles/
├── plan.md                              # This file
├── spec.md                              # Feature specification (input)
├── research.md                          # Phase 0 output
├── data-model.md                        # Phase 1 output
├── quickstart.md                        # Phase 1 output
├── contracts/                           # Phase 1 output
│   ├── profiles-api.md                  # HTTP surface: search pickers, printer process-profile link, part narrowing, removed endpoints
│   └── system-profile-ingestion.md      # Ingestion step's input/output/idempotency contract
├── checklists/                          # existing
└── tasks.md                             # Phase 2 output (/speckit-tasks — NOT created here)
```

### Source Code (repository root)

```text
api/
├── src/
│   ├── OpenFarm.Domain/
│   │   ├── Profiles/
│   │   │   ├── ProfileOrigin.cs                 # NEW enum: System | User
│   │   │   ├── MachineProfile.cs                # + Origin/VendorName/ExternalId/IsCurrentlyOffered/IsSelectable/VendorMachineModelName/IncludedTemplateNames; DefaultProcessProfileId REMOVED
│   │   │   ├── ProcessProfile.cs                # + same 5 base fields + CompatibleMachineNames
│   │   │   └── FilamentProfile.cs               # + same 5 base fields + CompatibleMachineNames + MaterialType + IncludedTemplateNames
│   │   ├── Printers/
│   │   │   ├── Printer.cs                       # + ProcessProfileId, LinkProcessProfile/UnlinkProcessProfile
│   │   │   └── PrinterModelCatalog.cs            # PrinterModel + VendorMachineModelName
│   │   └── Organizations/Organization.cs        # DefaultMachineProfileId + Set/ClearDefaultMachineProfile REMOVED
│   ├── OpenFarm.Application/
│   │   ├── Profiles/
│   │   │   ├── ISystemProfileIngestionService.cs # NEW: IngestAsync(bundle leaf path) -> IngestionSummary
│   │   │   ├── SystemProfileIdentityKey.cs       # NEW: research.md §1 upsert key logic (TDD)
│   │   │   ├── IMachineProfileRepository.cs      # + SearchAsync(...)
│   │   │   ├── IProcessProfileRepository.cs      # + SearchAsync(...); remove default-process-pairing method(s)
│   │   │   ├── IFilamentProfileRepository.cs     # + SearchAsync(...)
│   │   │   ├── ProfileRawDataMerger.cs           # + fold in IncludedTemplateNames after the ancestor-chain merge (research.md §0a/§5, TDD)
│   │   │   ├── RepresentativeFilamentResolver.cs # NEW: research.md §2 tie-break (TDD)
│   │   │   ├── UpdatePartFilamentRequirementNarrowingCommand*.cs  # NEW (FR-012), addressed by the requirement's real Id
│   │   │   ├── SetMachineProfileDefaultProcessCommand*.cs         # REMOVED
│   │   │   ├── UpdateDefaultMachineProfileCommand*.cs             # REMOVED
│   │   │   ├── IOrganizationFilamentDefaultRepository.cs          # REMOVED
│   │   │   └── GetSlicingProfileDefaultsQueryHandler.cs           # REMOVED
│   │   ├── Printers/
│   │   │   ├── EditPrinterCommand.cs             # + MachineProfileId/ProcessProfileId (contracts/profiles-api.md — folds in the two link mutations, no separate command)
│   │   │   ├── EditPrinterCommandHandler.cs      # + machine-first/compatibility gate (FR-006/FR-007), moved in from the removed LinkPrinterProcessProfileCommandHandler
│   │   │   └── LinkPrinterMachineProfileCommand*.cs  # REMOVED — folded into EditPrinterCommand
│   │   └── Parts/
│   │       ├── ResolveSlicingProfileSelection.cs # rewritten: printer-targeted / fleet-representative (research.md §5)
│   │       └── CliSlicerEngine.cs                # call-site change only: pass printerId through
│   ├── OpenFarm.Infrastructure/
│   │   ├── Profiles/
│   │   │   ├── SystemProfileIngestionService.cs  # NEW: filesystem walk + vendor JSON parse + upsert
│   │   │   ├── VendorProfileIndexReader.cs        # NEW: parses one Vendor.json + its referenced preset files; filters fetched files by declared "type" (research.md §0a — Bambu Studio's machine_list is 60% untyped include-target fragments)
│   │   │   ├── ResourcesProfilesLocator.cs        # NEW: recursive search per research.md §0
│   │   │   └── Repositories: extend Machine/Process/FilamentProfileRepository with SearchAsync (backs the existing GET routes' new query params, contracts/profiles-api.md — no new HTTP routes)
│   │   └── Persistence/
│   │       ├── Configurations/{MachineProfileConfiguration,ProcessProfileConfiguration,FilamentProfileConfiguration,PrinterConfiguration}.cs  # updated
│   │       ├── Configurations/OrganizationFilamentDefaultConfiguration.cs  # REMOVED
│   │       └── Migrations/2026..._AddSystemProfilesAndProcessLink.cs       # NEW hand-authored migration
│   └── OpenFarm.Api/Endpoints/
│       ├── Profiles/
│       │   ├── ListMachineProfilesEndpoint.cs / ListProcessProfilesEndpoint.cs / ListFilamentProfilesEndpoint.cs  # EXTENDED IN PLACE — existing routes gain origin/q/compatibleWithMachineName/printerId/materialType/page/pageSize query params and a paginated response envelope (contracts/profiles-api.md); no new routes, no separate Search*Endpoint.cs files
│       │   └── SetMachineProfileDefaultProcessEndpoint.cs  # REMOVED
│       ├── Printers/
│       │   ├── EditPrinterEndpoint.cs                       # EXTENDED IN PLACE — request gains machineProfileId/processProfileId (contracts/profiles-api.md); replaces both the link-machine-profile endpoint below and the separate process-profile-choices endpoint via the extended GET above
│       │   └── LinkPrinterMachineProfileEndpoint.cs         # REMOVED — folded into EditPrinterEndpoint
│       ├── Parts/
│       │   └── UpdatePartFilamentRequirementNarrowingEndpoint.cs  # NEW: PATCH .../filament-requirements/{requirementId}, addressed by the requirement's real Id, not an invented index
│       └── Settings/
│           ├── GetSlicingProfileDefaultsEndpoint.cs         # REMOVED
│           ├── UpdateDefaultMachineProfileEndpoint.cs       # REMOVED
│           └── UpdateOrganizationFilamentDefaultEndpoint.cs # REMOVED
└── tests/  # unit (Domain/Application: TDD items above), integration (Api: ingestion + selection + slice flow), mirrors above

web/src/features/
├── printers/components/
│   ├── EditPrinterDialog.tsx              # EXTENDED: machine + process profile pickers added as fields in
│   │                                      # the SAME dialog as name/IP/access code, one Save action → one
│   │                                      # PATCH /api/v1/printers/{id} call (contracts/profiles-api.md) —
│   │                                      # not two separate cards with independent save buttons, per the
│   │                                      # requested UX consolidation
│   └── MachineProfileLinkCard.tsx        # REMOVED — its picker moves into the edit form above; its
│                                          # standalone save action is superseded by the form's single save
├── parts/components/
│   └── PartFilamentRequirementsList.tsx  # + narrow action wired to the new endpoint, used from PartDetailPage
├── profiles/components/
│   ├── ProfilePickerCombobox.tsx         # NEW: shared searchable, paginated, origin-labeled picker (SC-008/SC-010) — backs both the printer edit form's two pickers and the part detail narrowing picker, calling the extended GET routes above
│   ├── ProcessProfilesTable.tsx          # REMOVED (no more process import/browse table)
│   ├── ProfileRawDataDialog.tsx          # kept for Machine/Filament User rows only; process usage removed
│   ├── ImportProfilesDialog.tsx          # process option removed from the bundle-kind selector
│   └── MachineDefaultProcessSelect.tsx   # REMOVED (superseded by the printer edit form's process picker)
└── settings/
    ├── SlicingProfileDefaultsPage.tsx (+ .test.tsx)  # REMOVED
    └── SlicingDefaultsPage.tsx           # UNCHANGED — this is 008's slicer engine/version default, a distinct page

web/src/app/routes.tsx     # 'settings/slicing-profile-defaults' route removed; 'settings/slicing-defaults' (008) untouched
web/src/lib/api/generated/ # regenerated via Orval once the OpenAPI surface above lands
```

**Structure Decision**: Retain the existing four-project Clean Architecture backend + React SPA monorepo.
System Profile ingestion is new `Infrastructure`/`Application` code inside the existing `OpenFarm.Api` host
— not a new standalone tool — because it needs a live DB context, unlike `tools/provision-slicers` which is
deliberately kept network-only and DB-free (008's own FR-018 boundary). All new domain concepts (`Origin`,
the printer↔process link, the search contracts) extend existing entities/tables in place rather than
introducing parallel System-specific types, per research.md §3's reasoning.

## Complexity Tracking

> No Constitution Check violations — this section is intentionally empty.
