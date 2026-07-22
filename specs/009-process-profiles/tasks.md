---
description: "Task list for Process Profiles — System & User Profiles for Machine, Process, and Filament"
---

# Tasks: Process Profiles — System & User Profiles for Machine, Process, and Filament

**Input**: Design documents from `/specs/009-process-profiles/`
**Prerequisites**: plan.md, spec.md, research.md, data-model.md, contracts/profiles-api.md, contracts/system-profile-ingestion.md, quickstart.md (all present)

**Tests**: Included. `plan.md`'s Constitution Check names specific deterministic components requiring TDD (the upsert-by-idempotency-key matcher, the representative-filament tie-break, the fleet-representative-printer pick, the machine↔process compatibility gate, the vendor-index→field mapping, the `IncludedTemplateNames` fold-in merge) — those get a failing-test task before their implementation task. Everything else follows this codebase's test-after convention (integration tests after the behavior exists), per `CLAUDE.md`'s testing strategy.

**Organization**: Tasks are grouped by user story (spec.md priorities: US1/US2/US3 = P1, US4 = P2) so each can be delivered and verified independently once Foundational work lands.

## Format: `[ID] [P?] [Story] Description`

- **[P]**: Can run in parallel (different files, no dependency on an incomplete task)
- **[Story]**: US1 / US2 / US3 / US4, per spec.md
- File paths are exact, relative to repo root

---

## Phase 1: Setup

**Purpose**: Acquire the real vendor fixture data ingestion/tests need — this feature deliberately tests against real, MIT-licensed vendor JSON (research.md §0/§0a), the same precedent feature `003-slicer-profile-import` established, not synthetic stand-ins.

- [X] T001 [P] Add real OrcaSlicer v2.4.2 vendor fixtures under `api/tests/OpenFarm.Infrastructure.Tests/Profiles/Fixtures/VendorIndex/OrcaSlicer/`: `BBL.json` plus a representative slice of its referenced `machine/`, `process/`, `filament/` preset files spanning a full `instantiation:false → true` inheritance chain for at least one machine model, including one preset with `renamed_from` (research.md §1 rename case) — fetched from `https://raw.githubusercontent.com/OrcaSlicer/OrcaSlicer/v2.4.2/resources/profiles/...` per research.md §0's verified source path. Add a `README.md` documenting provenance (mirrors the existing `Fixtures/README.md` convention).
- [X] T002 [P] Add real Bambu Studio vendor fixtures under `api/tests/OpenFarm.Infrastructure.Tests/Profiles/Fixtures/VendorIndex/BambuStudio/`: `BBL.json` plus at least one `machine_list` entry that uses `include` (with its named include-target gcode-template fragment files) and at least one `filament_list` entry that uses `include` (with its fragment files) — fetched from `github.com/bambulab/BambuStudio` `resources/profiles/BBL` (`master`) per research.md §0a.
- [X] T003 [P] Add an "unsupported vendor" fixture variant (a vendor index whose `machine_model_list` has zero overlap with any `PrinterModelCatalog.VendorMachineModelName`) alongside T001/T002, to exercise ingestion scoping (spec.md Acceptance Scenario 3, SC-009).

---

## Phase 2: Foundational (Blocking Prerequisites)

**Purpose**: Schema/domain changes every story reads or writes, plus the wholesale removal of the interim org-level-default and process-import surfaces (FR-009/FR-011/FR-017) that must happen atomically — the new columns and the removed columns are one hand-authored migration (data-model.md), and C# won't compile with the old and new shapes coexisting.

**⚠️ CRITICAL**: No user story work can begin until this phase is complete and the solution builds.

### Domain

- [X] T004 [P] Add `ProfileOrigin` enum (`System` | `User`) in `api/src/OpenFarm.Domain/Profiles/ProfileOrigin.cs`, persisted via `HasConversion<string>()` matching `ProfileSourceFormat`'s existing convention (data-model.md).
- [X] T005 [P] Extend `api/src/OpenFarm.Domain/Profiles/MachineProfile.cs`: add `Origin`, `VendorName`, `ExternalId`, `IsCurrentlyOffered`, `IsSelectable`, `VendorMachineModelName`, `IncludedTemplateNames` (`IReadOnlyList<string>?`); remove `DefaultProcessProfileId`/`SetDefaultProcessProfile`/`ClearDefaultProcessProfile` (superseded by `Printer.ProcessProfileId`, T008). Depends on T004.
- [X] T006 [P] Extend `api/src/OpenFarm.Domain/Profiles/ProcessProfile.cs`: add `Origin`, `VendorName`, `ExternalId`, `IsCurrentlyOffered`, `IsSelectable`, `CompatibleMachineNames` (`IReadOnlyList<string>?`). Depends on T004.
- [X] T007 [P] Extend `api/src/OpenFarm.Domain/Profiles/FilamentProfile.cs`: add the same five base fields as T005/T006 plus `CompatibleMachineNames`, `MaterialType`, `IncludedTemplateNames`. Depends on T004.
- [X] T008 [P] Add `Printer.ProcessProfileId : Guid?` + `LinkProcessProfile(Guid)`/`UnlinkProcessProfile()` to `api/src/OpenFarm.Domain/Printers/Printer.cs`, mirroring the existing `MachineProfileId`/`LinkMachineProfile`/`UnlinkMachineProfile` exactly (data-model.md §"Printer (changed)").
- [X] T009 [P] Add `VendorMachineModelName : string?` to `PrinterModel` and populate the 7 Bambu entries per research.md §0's mapping table (`X1`→`Bambu Lab X1`, `X1C`→`Bambu Lab X1 Carbon`, `X1E`→`Bambu Lab X1E`, `P1P`→`Bambu Lab P1P`, `P1S`→`Bambu Lab P1S`, `A1`→`Bambu Lab A1`, `A1MINI`→`Bambu Lab A1 mini`) in `api/src/OpenFarm.Domain/Printers/PrinterModelCatalog.cs`; leave `System/Simulation` unmapped (no real vendor ships it).
- [X] T010 Remove `Organization.DefaultMachineProfileId`/`SetDefaultMachineProfile`/`ClearDefaultMachineProfile` from `api/src/OpenFarm.Domain/Organizations/Organization.cs` (FR-009).
- [X] T011 [P] Delete the `OrganizationFilamentDefault` entity: `api/src/OpenFarm.Domain/Profiles/OrganizationFilamentDefault.cs` (FR-017).

### Application layer — remove the interim surfaces

- [X] T012 Delete `api/src/OpenFarm.Application/Profiles/IOrganizationFilamentDefaultRepository.cs`. Depends on T011.
- [X] T013 Delete `api/src/OpenFarm.Application/Profiles/SetMachineProfileDefaultProcessCommand.cs` + `SetMachineProfileDefaultProcessCommandHandler.cs` (calls the now-removed `MachineProfile.SetDefaultProcessProfile`). Depends on T005.
- [X] T014 Delete `api/src/OpenFarm.Application/Parts/GetSlicingProfileDefaultsQuery.cs`, `GetSlicingProfileDefaultsQueryHandler.cs`, `UpdateDefaultMachineProfileCommand.cs`, `UpdateDefaultMachineProfileCommandHandler.cs`, `UpdateOrganizationFilamentDefaultCommand.cs`, `UpdateOrganizationFilamentDefaultCommandHandler.cs`, and their test `api/tests/OpenFarm.Application.Tests/Parts/GetSlicingProfileDefaultsQueryHandlerTests.cs`. Depends on T010, T011.
- [X] T015 Remove process-preset handling from `api/src/OpenFarm.Infrastructure/Profiles/ProfileBundleParser.cs` — no `PresetKind.Process` branch feeds a selectable import result; process presets are never parsed out of an uploaded bundle for selection (FR-011).
- [X] T016 Remove process-preset handling from `api/src/OpenFarm.Application/Profiles/PreviewProfileImportCommandHandler.cs` (drop `processViews`/`ResolveProcessChainAsync`) and `api/src/OpenFarm.Application/Profiles/ConfirmProfileImportCommandHandler.cs` (drop the `ProfileType.Process` creation branch) — importing/confirming a process preset is no longer possible (FR-011). Depends on T015.

### Infrastructure — schema, config, migration

- [X] T017 [P] Delete `api/src/OpenFarm.Infrastructure/Profiles/OrganizationFilamentDefaultRepository.cs` and `api/src/OpenFarm.Infrastructure/Persistence/Configurations/OrganizationFilamentDefaultConfiguration.cs`. Depends on T012.
- [X] T018 Update `MachineProfileConfiguration.cs`, `ProcessProfileConfiguration.cs`, `FilamentProfileConfiguration.cs` (`api/src/OpenFarm.Infrastructure/Persistence/Configurations/`) for the new columns from T005-T007, `jsonb` conversions for the list-typed fields, and the two unique partial indexes per table (`UNIQUE (OrganizationId, VendorName, ExternalId) WHERE Origin='System' AND ExternalId IS NOT NULL`; `UNIQUE (OrganizationId, VendorName, Name) WHERE Origin='System' AND ExternalId IS NULL` — research.md §1). Depends on T005-T007.
- [X] T019 Update `PrinterConfiguration.cs` for `Printer.ProcessProfileId` (FK to `ProcessProfiles`, `ON DELETE SET NULL`, matching `MachineProfileId`'s existing behavior). Depends on T008.
- [X] T020 Update `OrganizationConfiguration.cs` to remove the `DefaultMachineProfileId` column mapping. Depends on T010.
- [X] T021 Hand-author EF Core migration `AddSystemProfilesAndProcessLink` in `api/src/OpenFarm.Infrastructure/Persistence/Migrations/` implementing every column add/drop, the `Printers.ProcessProfileId` FK, the two unique partial indexes per profile table, and `DROP TABLE "OrganizationFilamentDefaults"` exactly per data-model.md's SQL delta, with a `Down` that reverses each step. Depends on T017-T020.

### Api layer — remove the interim endpoints

- [X] T022 [P] Delete `api/src/OpenFarm.Api/Endpoints/Profiles/SetMachineProfileDefaultProcessEndpoint.cs`. Depends on T013.
- [X] T023 [P] Delete `api/src/OpenFarm.Api/Endpoints/Settings/GetSlicingProfileDefaultsEndpoint.cs`, `UpdateDefaultMachineProfileEndpoint.cs`, `UpdateOrganizationFilamentDefaultEndpoint.cs`. Depends on T014.
- [X] T024 Remove the DI registration `builder.Services.AddScoped<IOrganizationFilamentDefaultRepository, OrganizationFilamentDefaultRepository>()` (and any other registration referencing types deleted above) from `api/src/OpenFarm.Api/Program.cs`. Depends on T017, T022, T023.
- [X] T025 Delete `api/tests/OpenFarm.Api.IntegrationTests/Settings/SlicingProfileDefaultsEndpointTests.cs`. Depends on T023.

### Frontend — remove the interim UI

- [X] T026 [P] Delete `web/src/features/settings/SlicingProfileDefaultsPage.tsx` + `.test.tsx`; remove the `'settings/slicing-profile-defaults'` route from `web/src/app/routes.tsx` (leave `'settings/slicing-defaults'`, feature 008's page, untouched).
- [X] T027 [P] Delete `web/src/features/profiles/components/MachineDefaultProcessSelect.tsx` + `.test.tsx`.
- [X] T028 [P] Delete `web/src/features/profiles/components/ProcessProfilesTable.tsx` + `.test.tsx`; remove the process tab/section from `web/src/features/profiles/ProfilesPage.tsx` (+ its test).
- [X] T029 Remove the process option from the bundle-kind selector in `web/src/features/profiles/components/ImportProfilesDialog.tsx` (+ `.test.tsx`). Depends on T016.
- [X] T030 Regenerate the Orval-generated API client (`web/src/lib/api/generated/`) now that the removed endpoints are gone from the OpenAPI surface — never hand-edit generated files. Depends on T022-T024.

**Checkpoint**: `dotnet build api/OpenFarm.slnx` and `cd web && bun run build` both succeed with all interim org-default/process-import surfaces gone and the new domain fields/migration in place. No user story work starts before this passes.

---

## Phase 3: User Story 1 - System Profiles are ingested from the bundled slicer during provisioning (Priority: P1) 🎯 MVP prerequisite

**Goal**: A new ingestion step reads a bundled slicer's vendor profile index and creates read-only `Origin=System` rows for machine, process, and filament, scoped to vendors matching `PrinterModelCatalog`, idempotent across re-runs.

**Independent Test**: Run ingestion against the fixture bundle from Phase 1; verify System rows exist for all three kinds, attributed to their vendor, with resolved base/override relationships; verify the unsupported-vendor fixture produces zero rows; re-run and verify no duplicates.

### Tests for User Story 1 (TDD items per plan.md's Constitution Check)

- [X] T031 [P] [US1] Write failing unit tests for the identity-key matcher — leaf key `(OrganizationId, Kind, VendorName, ExternalId=setting_id)`, abstract-base key `(OrganizationId, Kind, VendorName, Name)` (research.md §1) — in `api/tests/OpenFarm.Application.Tests/Profiles/SystemProfileIdentityKeyTests.cs`.
- [X] T032 [US1] Implement `SystemProfileIdentityKey` in `api/src/OpenFarm.Application/Profiles/SystemProfileIdentityKey.cs` to pass T031.
- [X] T033 [P] [US1] Write failing unit tests for the vendor-index → System-Profile field mapping against the T001-T002 fixtures — `instantiation` string → `IsSelectable`; `setting_id` → `ExternalId`; `compatible_printers` → `CompatibleMachineNames`; `printer_model` → `VendorMachineModelName`; and the type-filter guard excluding untyped/mismatched-`type` `include`-target fragments from being treated as selectable presets (research.md §0a point 3) — in `api/tests/OpenFarm.Infrastructure.Tests/Profiles/VendorProfileIndexReaderTests.cs`.
- [X] T034 [US1] Implement `ResourcesProfilesLocator` in `api/src/OpenFarm.Infrastructure/Profiles/ResourcesProfilesLocator.cs` — recursive search of a bundle leaf for a `profiles` directory containing vendor-index-shaped JSON (`machine_model_list`/`machine_list`/`process_list`/`filament_list` keys present), reporting "not found" as a clear provisioning-step failure (research.md "Locating resources/profiles").
- [X] T035 [US1] Implement `VendorProfileIndexReader` in `api/src/OpenFarm.Infrastructure/Profiles/VendorProfileIndexReader.cs` to pass T033 — parses one vendor index + its referenced preset files, filters each fetched file by its own declared `type` matching the expected kind, resolves each filament's `filament_type` by walking its own ancestor chain at read time (research.md §0 point 5). Depends on T034.
- [X] T036 [P] [US1] Write failing unit tests for the `IncludedTemplateNames` fold-in merge step on `ProfileRawDataMerger` — resolving named include targets (scoped to the same `VendorName`+`Kind`+`Origin=System`) and folding each fragment's `RawDataJson` in before the target's own final layer, with an unresolved include name treated as a normal resolve-time gap, not a hard error (research.md §0a point 2) — in `api/tests/OpenFarm.Application.Tests/Profiles/ProfileRawDataMergerTests.cs`.
- [X] T037 [US1] Extend `api/src/OpenFarm.Application/Profiles/ProfileRawDataMerger.cs` to pass T036.

### Implementation for User Story 1

- [X] T038 [US1] Define `ISystemProfileIngestionService.IngestAsync(string bundleLeafPath, CancellationToken ct) -> IngestionSummary` in `api/src/OpenFarm.Application/Profiles/ISystemProfileIngestionService.cs` per contracts/system-profile-ingestion.md.
- [X] T039 [US1] Add ingestion-only upsert-lookup methods to `IMachineProfileRepository`/`IProcessProfileRepository`/`IFilamentProfileRepository` (e.g. `FindByExternalIdAsync(vendorName, externalId, ct)`, `FindSystemByNameAsync(vendorName, name, ct)`) and their EF implementations in `api/src/OpenFarm.Infrastructure/Profiles/` — distinct from the paginated picker `SearchAsync` added in US2 (T047-T048). Depends on T032.
- [X] T040 [US1] Implement `SystemProfileIngestionService` in `api/src/OpenFarm.Infrastructure/Profiles/SystemProfileIngestionService.cs`: scope vendors via `PrinterModelCatalog.All`/`VendorMachineModelName` overlap; two-pass upsert (pass 1 create/update rows keyed by T032, pass 2 resolve `BaseProfileId`/`MaterialType`/`IncludedTemplateNames` within the same batch, mirroring `ConfirmProfileImportCommandHandler`'s existing two-pass shape); mark previously-`System` rows not re-seen this run as `IsCurrentlyOffered = false` (never delete); skip an unparseable/missing vendor or preset file with a logged warning without aborting the rest of the run; return an `IngestionSummary` (vendors scanned/skipped, rows created/updated/marked-stale per kind). Depends on T035, T037, T039.
- [X] T041 [US1] Wire a CLI entry point (`dotnet run --project api/src/OpenFarm.Api -- ingest-system-profiles`) in `api/src/OpenFarm.Api/Program.cs`: when invoked with that argument, resolve `ISystemProfileIngestionService` from the built host, run it against every bundle leaf `ResourcesProfilesLocator` finds under `SlicerBundleOptions:RootDirectory`, print the summary, and exit without starting the web server (contracts/system-profile-ingestion.md's on-demand trigger point, re-runnable any time). Depends on T040.
- [X] T042 [P] [US1] Register `ISystemProfileIngestionService`, `VendorProfileIndexReader`, `ResourcesProfilesLocator` in `api/src/OpenFarm.Api/Program.cs`'s DI container.
- [X] T043 [P] [US1] Integration test: run ingestion against the Phase 1 fixture bundle; assert System rows exist for machine/process/filament attributed to their vendor with `BaseProfileId` resolved where the base was ingested, and assert zero rows for the T003 unsupported-vendor fixture (Acceptance Scenarios 1-3) — `api/tests/OpenFarm.Api.IntegrationTests/Profiles/SystemProfileIngestionTests.cs`. Depends on T041.
- [X] T044 [US1] Integration test: re-run ingestion against unchanged fixture data; assert zero rows created/updated (Acceptance Scenario 5).
- [X] T045 [US1] Integration test: re-run ingestion against a fixture variant with a renamed leaf (`renamed_from` present); assert the same row `Id` is updated in place, not duplicated (research.md §1 rename case).
- [X] T046 [US1] Integration test: ingest the Bambu Studio fixture (T002); assert no `MachineProfile`/`FilamentProfile` rows were created from untyped `include`-target gcode fragments, and a real machine preset's `IncludedTemplateNames` is populated with each named entry resolving to an ingested row of the same kind (quickstart Story 1 step 6).

**Checkpoint**: System Profiles ingest end-to-end from a real bundle leaf via the CLI command, independently verified by integration tests — before US2 needs them to populate the printer-page picker.

---

## Phase 4: User Story 2 - Choose a printer's machine and process profile, from System or User profiles, on the printer page (Priority: P1)

**Goal**: The printer page offers one combined, origin-labeled picker per kind (machine: System+User; process: System-only), gates process selection on a machine being selected first and compatible, and persists both selections per printer.

**Independent Test**: Open a printer's page; select a System machine profile and a compatible System process profile; save; verify a subsequent slice uses both; verify a second printer of the same model can independently select different profiles.

### Implementation for User Story 2

- [X] T047 [P] [US2] Add `SearchAsync(ProfileOrigin? origin, bool? isSelectable, string? compatibleWithMachineName, string? materialType, string? searchText, int page, int pageSize, CancellationToken ct)` to `IMachineProfileRepository`/`IProcessProfileRepository`/`IFilamentProfileRepository` (`api/src/OpenFarm.Application/Profiles/`) per research.md §6 — the existing `ListAsync`/`FindByNameAsync` stay unchanged, still used only by feature 003's User-scoped browse page and base-name resolution.
- [X] T048 [US2] Implement `SearchAsync` in `MachineProfileRepository`/`ProcessProfileRepository`/`FilamentProfileRepository` (`api/src/OpenFarm.Infrastructure/Profiles/`) — indexed `WHERE`/`ILIKE` text search + server-side pagination; every result row is `IsSelectable=true AND IsCurrentlyOffered=true` OR `Origin=User` (contracts/profiles-api.md). Depends on T047.
- [X] T049 [P] [US2] Write failing unit tests for the machine↔process compatibility gate — refuse a process selection when `MachineProfileId` is null (FR-007), refuse a process profile whose `CompatibleMachineNames` doesn't contain the linked machine profile's `Name` (FR-006) — in `api/tests/OpenFarm.Application.Tests/Printers/EditPrinterCommandHandlerTests.cs`.
- [X] T050 [US2] Extend `EditPrinterCommand`/`EditPrinterCommandHandler`/`EditPrinterCommandValidator` (`api/src/OpenFarm.Application/Printers/`) with `MachineProfileId`/`ProcessProfileId : Guid?`, applied exactly as sent (`null` = clear); fold in the T049 compatibility gate; call `Printer.LinkMachineProfile`/`UnlinkMachineProfile`/`LinkProcessProfile`/`UnlinkProcessProfile` as appropriate. Depends on T049.
- [X] T051 [US2] Delete `api/src/OpenFarm.Application/Printers/LinkPrinterMachineProfileCommand.cs` + `LinkPrinterMachineProfileCommandHandler.cs` — folded into T050. Depends on T050.
- [X] T052 [US2] Extend `ListMachineProfilesEndpoint`/`ListProcessProfilesEndpoint`/`ListFilamentProfilesEndpoint` (`api/src/OpenFarm.Api/Endpoints/Profiles/`) in place with `origin` (default `"User"`), `q`, `compatibleWithMachineName`, `printerId`, `materialType`, `page`, `pageSize` query params and the paginated `{items, page, pageSize, totalCount}` envelope, per contracts/profiles-api.md — zero query-string change for feature 003's existing browse-page caller. Depends on T048.
- [X] T053 [US2] Implement the `printerId` sugar on the process endpoint: resolve the printer's current `MachineProfileId` server-side and filter to compatible process profiles; return `{available:false, items:[], ...}` when the printer has no machine profile set (FR-007). Depends on T052.
- [X] T054 [US2] Extend `EditPrinterRequest`/`EditPrinterEndpoint` (`api/src/OpenFarm.Api/Endpoints/Printers/`) with `machineProfileId`/`processProfileId`; return `422` ProblemDetails for the FR-006/FR-007 violations and `400` for a nonexistent or wrong-`origin` profile id, per contracts/profiles-api.md's validation table. Depends on T050.
- [X] T055 [US2] Delete `api/src/OpenFarm.Api/Endpoints/Printers/LinkPrinterMachineProfileEndpoint.cs` — folded into T054. Depends on T054, T051.
- [X] T056 [US2] Wire activity-log events for machine/process link/unlink on the consolidated endpoint (`PrinterMachineProfileLinked`/`Unlinked`, `PrinterProcessProfileLinked`/`Unlinked`) alongside the existing `PrinterEdited` event (FR-016), extending `EditPrinterRequest`'s `IActivityLoggable` usage in `api/src/OpenFarm.Api/Endpoints/Printers/EditPrinterEndpoint.cs`. Depends on T054.
- [X] T057 [P] [US2] Regenerate the Orval frontend client (`web/src/lib/api/generated/`) from the updated OpenAPI surface. Depends on T052-T056. **Done for real**: installed the dotnet SDK (`apt-get install dotnet-sdk-10.0`, Ubuntu archive) and PostgreSQL (`apt-get install postgresql`, bypassing the Docker-registry policy block entirely), booted the real API (`dotnet run`) against a real migrated database, fetched the live `/swagger/v1/swagger.json`, wrote it to `api/openapi.json` (diff confirms exactly the 5 removed endpoints gone and the 1 new endpoint present, nothing else), then ran the real `bunx orval` — regenerating `endpoints.schemas.ts` and every per-tag file (including full query-param-aware hooks for `origin`/`q`/`compatibleWithMachineName`/`printerId`/`materialType`/`page`/`pageSize`) and removing all dead generated code for the 5 deleted endpoints. `bunx tsc -b`, `bun run lint` (same 2 pre-existing errors only), `bun run test` (124/124), and `bun run build` all still pass against the real regenerated client.
- [ ] T058 [P] [US2] Build `ProfilePickerCombobox` in `web/src/features/profiles/components/ProfilePickerCombobox.tsx` — a shared searchable, paginated, origin-labeled picker (SC-008/SC-010) calling the extended GET routes from T052; one shared component, not per-page bespoke rendering. **Not started** — blocked on a real T057 (needs the query-param-aware generated hooks).
- [ ] T059 [US2] Fold machine + process profile pickers into `web/src/features/printers/components/EditPrinterDialog.tsx` as fields in the same form/save action as name/IP/access code, using `ProfilePickerCombobox` (T058); the process picker is disabled with a "select a machine profile first" prompt until `machineProfileId` is set (FR-007). Depends on T057, T058. **Not done** — `MachineProfileLinkCard.tsx` was fixed in place instead (see T060 note) as a stopgap so nothing stays broken, but it's a standalone card, not folded into the dialog, and only shows `origin=User` rows (no picker search yet).
- [ ] T060 [US2] Delete `web/src/features/printers/components/MachineProfileLinkCard.tsx` + `.test.tsx` — superseded by T059. **Not done — kept intentionally**: with T059 not built, deleting it would regress the only working machine/process-link UI. Rewrote it in place instead to stop calling the now-deleted `LinkPrinterMachineProfileEndpoint` (dead 404 since T051/T055) and call the consolidated `PATCH /api/v1/printers/{id}` instead, with a second Select added for the process profile (disabled until a machine profile is chosen, FR-007). Delete it once T059 actually lands.
- [X] T061 [P] [US2] Integration tests for `PATCH /api/v1/printers/{id}` covering Acceptance Scenarios 1-6 (both-origin machine choices, System-only process choices compatible with the selected machine, save-then-slice uses both, two printers keep independent selections, process unavailable until machine selected, activity log on change/clear) in `api/tests/OpenFarm.Api.IntegrationTests/Printers/`. Depends on T054, T056. Added to `EditPrinterEndpointTests.cs`: link machine+compatible process (asserts Linked activity events), process-without-machine 422, incompatible-process 422, two printers keep independent selections, clear-machine-also-clears-process (asserts Unlinked activity events). Requires Docker/Testcontainers to actually run (not runnable in this sandbox — verified by code review only, consistent with every other integration test in this file).
- [ ] T062 [P] [US2] Frontend tests for `EditPrinterDialog`'s new pickers and `ProfilePickerCombobox` (origin labeling, search, disabled-until-machine-selected state) in their respective `.test.tsx` files. Depends on T059. **Not done** — no `EditPrinterDialog`/`ProfilePickerCombobox` integration exists yet to test (see T058/T059).

**Checkpoint**: An operator can select a printer's machine + process profile from combined, labeled System/User lists, independently of US3/US4.

---

## Phase 5: User Story 3 - Slice for a printer using fleet-derived profiles, with no org-level defaults (Priority: P1)

**Goal**: `ResolveSlicingProfileSelection` resolves machine/process from the target printer's own selections (or a deterministic fleet representative for estimate-only slices) and filament from the part's narrowing or a deterministic representative System filament profile — never from any organization-level default.

**Independent Test**: Two printers of different models, each fully configured (US2); slice each and confirm each uses its own presets; clear one printer's process selection and confirm a named refusal; confirm no code path reads a removed org-default field.

### Tests for User Story 3 (TDD items per plan.md's Constitution Check)

- [X] T063 [P] [US3] Write failing unit tests for the fleet-representative-printer pick — smallest `Printer.Id` (GUID) among printers with both `MachineProfileId` and `ProcessProfileId` set; refuse with "no printers configured" when none qualify (research.md §5) — in `api/tests/OpenFarm.Application.Tests/Parts/ResolveSlicingProfileSelectionTests.cs`. Already covered by `ResolveAsync_NoTargetPrinter_PicksSmallestGuidAmongFullyConfiguredPrinters`/`_EmptyFleet_ThrowsNoPrintersConfigured`.
- [X] T064 [P] [US3] Write failing unit tests for `RepresentativeFilamentResolver`'s tie-break — among `Origin=System`, selectable, material-type-matching filament rows, pick the smallest `(VendorName, ExternalId)` ordinal pair; identical result across repeated calls (research.md §2, FR-019) — in `api/tests/OpenFarm.Application.Tests/Profiles/RepresentativeFilamentResolverTests.cs`. New file: covers tie-break ordering, repeat-call determinism, no-candidate refusal, `IsCurrentlyOffered=false` exclusion, and `Origin=User` exclusion.

### Implementation for User Story 3

- [X] T065 [US3] Implement `RepresentativeFilamentResolver` in `api/src/OpenFarm.Application/Profiles/RepresentativeFilamentResolver.cs` to pass T064.
- [X] T066 [US3] Rewrite `api/src/OpenFarm.Application/Parts/ResolveSlicingProfileSelection.cs`: accept `Guid? printerId` (drop `IOrganizationRepository`/`IOrganizationFilamentDefaultRepository` dependencies entirely); printer-targeted branch reads `printer.MachineProfileId`/`ProcessProfileId` directly via `IPrinterRepository.GetByIdAsync`, reusing the existing missing-selection error shape per-printer; no-target branch picks the fleet representative (T063) via `IPrinterRepository.ListAsync`; filament resolves from the part's `NarrowFilamentProfileId` when narrowed (US4), else `RepresentativeFilamentResolver` (T065) for the target material type; fold `IncludedTemplateNames` into the accumulator after each ancestor-chain merge, before the target's own final layer (research.md §0a/§5). Depends on T063, T065. The base rewrite was already done in Phase 2; this session wired the previously-unused `ProfileRawDataMerger.MergeAncestorChainWithIncludes` in for the machine and filament chains specifically (`MergeMachineChainWithIncludesOrThrowAsync`/`MergeFilamentChainWithIncludesOrThrowAsync`), resolving each `IncludedTemplateNames` entry via `FindSystemByNameAsync` scoped to the target's own `VendorName`. Process stays a plain ancestor-chain merge (no `IncludedTemplateNames` field exists on `ProcessProfile` — research.md §0a: `include` is machine+filament only).
- [X] T067 [US3] Delete any remaining reference to `IOrganizationFilamentDefaultRepository`/`Organization.DefaultMachineProfileId`/`MachineProfile.DefaultProcessProfileId` left over in call sites or DI registration after T066's rewrite. Depends on T066. Verified via grep — only pre-existing migration files (`Migrations/2026072*.cs`) mention these symbols, which is correct (immutable schema history), not live code.
- [X] T068 [US3] Rewire the one existing slicing call site — `PartFileProcessor.ResolveEstimateAsync` → `CliSlicerEngine.SliceAsync` (`api/src/OpenFarm.Infrastructure/Parts/PartFileProcessor.cs`, `CliSlicerEngine.cs`) — to pass `printerId: null`: this codebase's only current caller is the pre-print estimate at part upload, which has no specific target printer and is exactly US3's fleet-representative case. Depends on T066. Done in Phase 2.
- [X] T069 [P] [US3] Test: two printers of different models each with their own machine/process selection; resolve via `ResolveSlicingProfileSelection` with each printer's id; assert each uses its own presets, never a shared default (Acceptance Scenario 1) — `api/tests/OpenFarm.Application.Tests/Parts/ResolveSlicingProfileSelectionTests.cs`. Depends on T066. Added `ResolveAsync_TwoPrintersOfDifferentModelsEachWithOwnSelections_EachResolvesItsOwnPresetsNeverAShared`.
- [X] T070 [P] [US3] Test: clear one printer's process selection; request resolution for it; assert refusal naming that printer and the missing process selection, no partial result (Acceptance Scenario 2, SC-003). Strengthened the existing `ResolveAsync_PrinterHasMachineButNoProcessProfile_ThrowsSlicingProfileUnconfigured` test to assert the exception message names the printer and "process profile".
- [X] T071 [US3] Test/verification: `grep -r DefaultMachineProfileId api/src` and `grep -r DefaultProcessProfileId api/src` and `grep -r OrganizationFilamentDefault api/src` all return nothing (FR-009/FR-017, SC-005). Confirmed — only migration files reference these (immutable history), zero live code references.
- [X] T072 [US3] Test: no-target resolution with an empty fleet refuses with "no printers configured"; with a non-empty fleet containing at least one fully-configured printer, resolves via the fleet-representative rule (Acceptance Scenario 4, FR-015). Already covered by `ResolveAsync_NoTargetPrinter_EmptyFleet_ThrowsNoPrintersConfigured`/`_PicksSmallestGuidAmongFullyConfiguredPrinters`.
- [X] T073 [US3] Test: resolution for a material type with no narrowed part filament requirement auto-resolves the deterministic representative System filament profile with zero reads of any settings table, and resolves identically on a second call (Acceptance Scenario 5, FR-018/FR-019). Covered by `RepresentativeFilamentResolverTests.ResolveAsync_RepeatedCalls_ReturnIdenticalResult` plus the fully-configured `ResolveAsync_PrinterTargetedAndFullyConfigured_ReturnsAllThreeMergedJson` test (no narrowed filament id passed, resolves via `RepresentativeFilamentResolver`).
- [X] T074 [US3] Integration test: a printer whose machine/process are Bambu-Studio-sourced resolves a `MachineSettingsJson`/`FilamentSettingsJson` containing `machine_start_gcode`/`change_filament_gcode`/etc. (not silently absent) — `api/tests/OpenFarm.Api.IntegrationTests/Parts/` (quickstart Story 3 step 1, research.md §0a). Depends on T066. New file `BambuStudioSlicingResolutionTests.cs`: ingests the real Bambu Studio fixture, links a printer to the ingested machine+process pair, resolves via `ResolveSlicingProfileSelection`, asserts `machine_start_gcode`/`change_filament_gcode` are present in the resolved JSON. Requires Docker/Testcontainers to run (not runnable in this sandbox — verified by code review only).

**Checkpoint**: Slicing resolves entirely from fleet/System data; zero organization-level defaults remain anywhere in the codebase.

---

## Phase 6: User Story 4 - Narrow a part's filament requirement to a System or User filament profile, on the part detail page (Priority: P2)

**Goal**: A standalone narrow/change/clear action on the part detail page, reachable any time after confirm (not only during the initial confirm-review flow), offering one combined origin-labeled filament list.

**Independent Test**: Open a part with an unnarrowed filament requirement; the narrowing picker offers both System- and User-labeled filament profiles; select a System profile; save; the requirement reflects the narrowed choice.

### Implementation for User Story 4

- [X] T075 [US4] Confirm `IFilamentProfileRepository.SearchAsync` (T047-T048, US2) already supports the `materialType`-only filtering the part-detail picker needs; extend it if a gap exists. Confirmed — `materialType` is a first-class DB-level filter param already, no gap.
- [X] T076 [US4] Add `UpdatePartFilamentRequirementNarrowingCommand`/`CommandHandler`/`Validator` in `api/src/OpenFarm.Application/Parts/` — mutually-exclusive `NarrowFilamentVariantId`/`NarrowFilamentProfileId`, mirroring `ConfirmPartProcessingJobCommandValidator`'s existing rule (data-model.md), addressed by the requirement's real `Id` (already returned by `GetPartEndpoint`). Added `PartFilamentRequirement.Narrow(...)` + `Part.NarrowFilamentRequirement(...)` domain methods and `PartFilamentRequirementNotFoundException`.
- [X] T077 [US4] Add `UpdatePartFilamentRequirementNarrowingEndpoint` — `PATCH /api/v1/parts/{id}/filament-requirements/{requirementId}` — in `api/src/OpenFarm.Api/Endpoints/Parts/`, per contracts/profiles-api.md's validation table (`400` both-set, `404` unknown `requirementId`, `400` unknown profile id, `200` + updated requirement). Depends on T076.
- [X] T078 [US4] Wire activity logging for narrow/change/clear on the new endpoint (FR-016), consistent with existing part-mutation activity logging. Depends on T077. Direct `IActivityLogWriter` injection (like `EditPrinterCommandHandler`) writing `PartFilamentRequirementNarrowed`/`PartFilamentRequirementCleared` against the Part entity.
- [X] T079 [P] [US4] Regenerate the Orval client for the new endpoint (`web/src/lib/api/generated/`). Depends on T077. **Done** — covered by the same real `bunx orval` run as T057 (single regen covers the whole updated OpenAPI surface); `parts.ts` now has the generated request/response types and hooks for `PATCH /api/v1/parts/{id}/filament-requirements/{requirementId}`.
- [ ] T080 [US4] Add a standalone narrow/change/clear control to `web/src/features/parts/components/PartFilamentRequirementsList.tsx`, reachable from `web/src/features/parts/PartDetailPage.tsx` at any time after confirm, using `ProfilePickerCombobox` (T058, US2) for the combined System+User filament list. Depends on T079, T058. **Not started** — blocked on T058/T079 same as T059.
- [X] T081 [P] [US4] Integration tests for the new endpoint covering Acceptance Scenarios 1-3 (both-origin picker, narrow to a System profile, narrow/change/clear all activity-logged) in `api/tests/OpenFarm.Api.IntegrationTests/Parts/`. Depends on T077, T078. New file `UpdatePartFilamentRequirementNarrowingEndpointTests.cs`: narrow-to-System-profile (200 + `PartFilamentRequirementNarrowed` logged), change-then-clear (200 + `PartFilamentRequirementCleared` logged on clear), both-ids-set (400), unknown `requirementId` (404), unknown `narrowFilamentProfileId` (400). Requires Docker/Testcontainers to run (not runnable in this sandbox — verified by code review only).
- [ ] T082 [P] [US4] Frontend test for the part-detail narrowing control (origin-labeled options, save, clear) alongside `PartFilamentRequirementsList`/`PartDetailPage`'s existing tests. Depends on T080. **Not done** — no control exists yet to test (see T080).

**Checkpoint**: An operator can narrow/change/clear a part's filament requirement to a System or User profile from the part detail page at any time after confirm.

---

## Phase 7: Polish & Cross-Cutting Concerns

**Purpose**: Repo-wide verification once all four stories land.

- [X] T083 [P] Run `dotnet format api/OpenFarm.slnx --verify-no-changes` and `cd web && bun run lint && bunx tsc -b`; fix any violations across all files touched by Phases 2-6. **dotnet SDK installed mid-session** via `apt-get install dotnet-sdk-10.0` (Ubuntu archive, not the blocked `dotnetcli.azureedge.net`) — only 10.0.110 is available there (feature band 100), while `api/global.json` pins 10.0.301 (band 300); `dotnet build`/`dotnet test` from the repo root tolerate the mismatch (global.json isn't discovered from that cwd), but `dotnet format` enforces it strictly via a dedicated build-host process, so the pin was temporarily lowered to 10.0.110 to run the formatter and restored immediately after (`git diff api/global.json` shows no change). `dotnet format --verify-no-changes` — **clean, exit 0, zero violations**. `dotnet build --configuration Debug` — **builds clean**, 0 errors (Release config separately fails on a pre-existing `NU1903` transitive-dependency security-advisory-as-error unrelated to this feature — confirmed via `git diff origin/develop...HEAD -- '*.csproj'` showing only one unrelated fixture-linking line changed all session, so this isn't something introduced here). `bunx tsc -b`/`bun run lint` as before: tsc clean, lint has the same 2 pre-existing errors in untouched files.
- [X] T084 Run `dotnet test api/OpenFarm.slnx` (full suite) and `cd web && bun run test`; confirm zero regressions in features 003/005/007/008. **Actually executed**, not just reviewed. Fixed 5 real bugs the compiler/test run caught that manual review had missed: (1) `GetFilamentProfilesQuery.cs`'s lean-summary constructor call was missing one positional `null` (`FanPercent`) — real `CS7036` compile error; (2) `RepresentativeFilamentResolverTests.cs` missing `using OpenFarm.Domain.Parts;`; (3) `BambuStudioSlicingResolutionTests.cs` missing `using OpenFarm.Application.Printers;`; (4) `EditPrinterCommandHandlerTests.cs` called a `FakeProcessProfileRepository` constructor overload that doesn't exist on the shared fake (fixed to construct-then-`AddAsync`, matching every other test in the file); (5) two test-data bugs found via actual test *failures*: `ProfileRawDataMergerTests`'s apostrophe-containing string literal never matched `Utf8JsonWriter`'s default `'`-escaped output (changed to an apostrophe-free literal), and `CliSlicerEngineTests`'s fake filament profile's `MaterialType` ("PLA") didn't match the "Generic PLA" string the tests pass as `SliceAsync`'s materialType argument (aligned both to "Generic PLA", per the resolver's actual material-type-keyed matching). After fixes: `OpenFarm.Domain.Tests` 112/112, `OpenFarm.Application.Tests` 137/137, `OpenFarm.Infrastructure.Tests` 132/132 — **381/381 passing**. `OpenFarm.Api.IntegrationTests` compiles clean (0 errors) but cannot run: Testcontainers needs to pull `testcontainers/ryuk` and `postgres` from Docker Hub, and the registry's CDN (`production.cloudfront.docker.com`) returns a policy-level 403 through this session's egress proxy, confirmed non-transient and not something to route around per the proxy's own documented guidance. `bun run test` (124/124) and `bun run build` unaffected, still green.
- [ ] T085 Run quickstart.md end-to-end: Setup ingestion → Story 2 → Story 3 → Story 4 → Removal verification → Fresh-install check, confirming SC-001 through SC-011. **Still not done** — requires a running Postgres (via Docker or Testcontainers), blocked by the same registry-CDN 403 as T084's integration tests. The backend itself now runs (`dotnet` is installed), so only the database dependency blocks this.
- [ ] T086 [P] Verify SC-006 (an operator reaches a successful slice for a compatible machine+process pair in ≤3 interactions starting from the printer's page) via the actual frontend flow. **Not verifiable as originally scoped** — the ≤3-interaction picker flow depends on `ProfilePickerCombobox`/`EditPrinterDialog` (T058/T059), which remain unbuilt (see Phase 4 notes); today's stopgap `MachineProfileLinkCard` only offers User-origin profiles via two plain `Select`s, so this SC cannot be honestly claimed met yet.
- [ ] T087 [P] Verify SC-010 (a 2,000+-row filament picker stays searchable/responsive) against the real ingested Bambu Studio/OrcaSlicer fixture data from Phase 1, not a synthetic small set. **Not verifiable as originally scoped** — same blocker as T086 (no picker UI yet); the backend `SearchAsync`/paginated-envelope design was built specifically to make this achievable once T058 lands, but that's a design intent, not a verified measurement.
- [X] T088 Final sweep: `grep -rn "DefaultMachineProfileId\|DefaultProcessProfileId\|OrganizationFilamentDefault\|SlicingProfileDefaults" api/src web/src` returns zero matches (SC-005). **Fully clean now** — after the real Orval regeneration (T057/T079), zero matches anywhere in `api/src` or `web/src` outside `api/src/.../Migrations/*.cs` (expected — immutable schema history). The previously-noted stale generated-client leftovers (dead hooks for the 5 deleted endpoints) are gone.

---

## Dependencies & Execution Order

### Phase Dependencies

- **Setup (Phase 1)**: No dependencies — can start immediately, purely additive fixture files.
- **Foundational (Phase 2)**: No dependency on Phase 1's fixtures for the domain/schema work, but Phase 3's tests consume Phase 1's fixtures — both should be done before Phase 3 starts. **BLOCKS all user stories.**
- **User Story 1 (Phase 3)**: Depends on Foundational. Nothing else depends on it structurally, but it is the practical prerequisite for a real end-to-end test of US2/US3/US4 (System rows must exist to select/resolve).
- **User Story 2 (Phase 4)**: Depends on Foundational; its picker is most usefully tested against US1's ingested data, but its own command/endpoint/domain wiring has no hard code dependency on US1's ingestion service.
- **User Story 3 (Phase 5)**: Depends on Foundational (`Printer.ProcessProfileId` from T008) and benefits from US2 existing (a printer needs a saved selection to resolve against) — sequence US2 before US3 in practice even though the resolver code itself only needs the domain fields.
- **User Story 4 (Phase 6)**: Depends on Foundational and reuses `ProfilePickerCombobox` from US2 (T058) — sequence after US2.
- **Polish (Phase 7)**: Depends on all four user stories being complete.

### Recommended Sequence

Given the dependency chain above (US2 needs US1's data to be meaningfully testable; US3 needs US2's printer selections to resolve against; US4 reuses US2's picker component), implement in priority + dependency order: **Foundational → US1 → US2 → US3 → US4 → Polish**, rather than parallelizing the three P1 stories.

### Parallel Opportunities

- All of Phase 1 (T001-T003) in parallel.
- Within Phase 2: T004-T011 (domain) mostly parallel once T004 lands; T017/T022/T023/T026-T028 (deletions of independent files) in parallel.
- Within Phase 3: T031, T033, T036 (test-writing tasks, different files) in parallel; T042-T043 in parallel once T041 lands.
- Within Phase 4: T047, T049 in parallel; T057-T058 in parallel; T061-T062 in parallel.
- Within Phase 5: T063-T064 in parallel; T069-T070 in parallel.
- Within Phase 6: T079, T081-T082 in parallel where marked.

---

## Parallel Example: Foundational Domain Batch

```bash
# After T004 (ProfileOrigin enum) lands, these touch independent files:
Task: "Extend MachineProfile.cs with new fields, remove DefaultProcessProfileId"
Task: "Extend ProcessProfile.cs with new fields"
Task: "Extend FilamentProfile.cs with new fields"
Task: "Add Printer.ProcessProfileId + Link/UnlinkProcessProfile"
Task: "Add PrinterModel.VendorMachineModelName + populate 7 entries"
```

## Parallel Example: User Story 1 Tests

```bash
Task: "Failing tests for SystemProfileIdentityKey upsert-key matcher"
Task: "Failing tests for VendorProfileIndexReader field mapping + type-filter guard"
Task: "Failing tests for ProfileRawDataMerger's IncludedTemplateNames fold-in"
```

---

## Implementation Strategy

### MVP First

The spec itself frames US1 as the true prerequisite ("Everything else... depends on System Profiles existing first") even though US1/US2/US3 are all P1. Recommended MVP slice:

1. Complete Phase 1 (Setup) + Phase 2 (Foundational) — build must pass.
2. Complete Phase 3 (US1) — ingestion works end-to-end against a real bundle; **STOP and VALIDATE** via the CLI command + integration tests.
3. Complete Phase 4 (US2) — an operator can select System/User machine + System process profiles per printer; **STOP and VALIDATE** on the printer page.
4. Complete Phase 5 (US3) — slicing is fully fleet-driven, zero org defaults remain; **STOP and VALIDATE** via quickstart Story 3.
5. This is a shippable MVP (SC-001-SC-010 all achievable without US4).

### Incremental Delivery

6. Add Phase 6 (US4) — part-detail filament narrowing, P2, additive on top of the MVP.
7. Phase 7 (Polish) closes out with the full quickstart pass and the SC-005/SC-009 removal/scoping sweeps.

---

## Notes

- [P] tasks touch different files and have no dependency on an incomplete task in the same batch.
- Commit after each task or logical group; stop at any checkpoint to validate a story independently.
- The Foundational phase's removal work (T010-T030) is unusually large for a "prerequisites" phase because C# won't compile with the interim org-default/process-import surfaces partially removed — this is a single atomic cleanup, not several independent ones, mirroring the one hand-authored migration in data-model.md.
- Avoid: touching `web/src/lib/api/generated/` by hand (T030/T057/T079 regenerate it); re-adding a process-profile import/edit surface anywhere (FR-011 is permanent, not a feature flag).
