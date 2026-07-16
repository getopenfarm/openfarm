---

description: "Task list for 003-slicer-profile-import"

---

# Tasks: Profiles (Machine + Filament, Imported from Slicer Bundles)

**Input**: Design documents from `/specs/003-slicer-profile-import/`

**Prerequisites**: plan.md, spec.md, research.md, data-model.md, contracts/profiles-api.md, quickstart.md (all present)

**Tests**: Included per plan.md's explicit testing strategy — TDD (write-first) for `ProfileRawDataMerger`
and the base/override resolution algorithm (deterministic, well-specified, no external-format
uncertainty, per the constitution's Principle II carve-out); fixture-replay test-after for
`ProfileBundleParser` (mirrors the `BambuMqttMessageParser` precedent from `specs/002-printer-integration/`);
test-after `WebApplicationFactory` + Testcontainers.PostgreSql integration tests for orchestration
handlers, endpoints, and the round trip; test-after Vitest + RTL for React components. Task order below
reflects each item's actual classification — TDD tasks are sequenced test-then-implementation;
test-after tasks are sequenced implementation-then-test, per CLAUDE.md's "don't apply TDD uniformly"
rule.

**Organization**: Tasks are grouped by user story (spec.md's US1–US4) to enable independent
implementation and testing of each story.

## Format: `[ID] [P?] [Story] Description`

- **[P]**: Can run in parallel (different files, no dependencies on incomplete tasks)
- **[Story]**: Which user story this task belongs to (US1–US4); omitted for Setup/Foundational/Polish
- Every task names exact file paths

## Path Conventions

Existing four-project Clean Architecture backend + single-SPA frontend, extended in place (plan.md
"Structure Decision" — no new top-level project):

- `api/src/OpenFarm.Domain/Profiles/`, `api/src/OpenFarm.Application/Profiles/`,
  `api/src/OpenFarm.Infrastructure/Profiles/`, `api/src/OpenFarm.Api/Endpoints/Profiles/`
- `api/tests/OpenFarm.Domain.Tests/Profiles/`, `api/tests/OpenFarm.Application.Tests/Profiles/`,
  `api/tests/OpenFarm.Infrastructure.Tests/Profiles/`, `api/tests/OpenFarm.Api.IntegrationTests/Profiles/`
- `web/src/features/profiles/` (replacing the existing mock-data prototype)

---

## Phase 1: Setup

**Purpose**: Project scaffolding and test fixtures — no new NuGet or Bun dependencies are needed
(bundle parsing uses the .NET BCL's `System.IO.Compression.ZipArchive`/`System.Text.Json`; frontend
upload uses a plain file input + the existing Orval client pattern, per plan.md's Technical Context).

- [X] T001 Create empty `Profiles/` folders under `api/src/OpenFarm.Domain/`,
  `api/src/OpenFarm.Application/`, `api/src/OpenFarm.Infrastructure/`, and
  `api/src/OpenFarm.Api/Endpoints/`, plus `web/src/features/profiles/components/`, per plan.md's
  Project Structure
- [X] T002 [P] Add real captured OrcaSlicer/Bambu Studio slicer-bundle fixtures — one multi-preset
  `.zip`, one Bambu Studio `.bbscfg`, one single-preset `.orca_printer` or `.orca_filament`, and a
  base+override pair — to `api/tests/OpenFarm.Infrastructure.Tests/Profiles/Fixtures/` for
  fixture-replay `ProfileBundleParser` tests (research.md §1's follow-up task, mirroring Phase 1's real
  MQTT fixture precedent)

---

## Phase 2: Foundational (Blocking Prerequisites)

**Purpose**: Entities, persistence, the parser contract, and the chain-merge utility every user story
(US1–US4) depends on.

**⚠️ CRITICAL**: No user story work can begin until this phase is complete.

- [X] T003 [P] Create `ProfileSourceFormat` enum in `api/src/OpenFarm.Domain/Profiles/ProfileSourceFormat.cs`
  (`OrcaSlicer`, `BambuStudio` — plain enum, matches the existing `PrinterModel.Brand` precedent per
  data-model.md, not a lookup table)
- [X] T004 [P] Create `MachineProfile` entity in `api/src/OpenFarm.Domain/Profiles/MachineProfile.cs`
  (`Id`, `OrganizationId`, `Name`, `SourceFormat`, `BaseProfileName`, `BaseProfileId` self-FK,
  `RawDataJson`, `CreatedAtUtc` per data-model.md — no descriptive/spec fields as columns)
- [X] T005 [P] Create `FilamentProfile` entity in `api/src/OpenFarm.Domain/Profiles/FilamentProfile.cs`
  (identical shape to `MachineProfile` per data-model.md)
- [X] T006 [P] Create `MachineProfileConfiguration` in
  `api/src/OpenFarm.Infrastructure/Persistence/Configurations/MachineProfileConfiguration.cs`
  (`builder.IsMultiTenant()`, self-referencing FK with `ON DELETE SET NULL`, `RawDataJson` as `jsonb`,
  no uniqueness constraint on `Name` — data-model.md's EF Core configuration notes)
- [X] T007 [P] Create `FilamentProfileConfiguration` in
  `api/src/OpenFarm.Infrastructure/Persistence/Configurations/FilamentProfileConfiguration.cs` (same
  rules as `MachineProfileConfiguration`)
- [X] T008 Add the `AddProfiles` EF Core migration in
  `api/src/OpenFarm.Infrastructure/Persistence/Migrations/` for both new tables via
  `dotnet ef migrations add AddProfiles` against `OpenFarmDbContext` (single migration per the
  constitution's one-per-PR rule; depends on T004–T007)
- [X] T009 [P] Create `IMachineProfileRepository` interface in
  `api/src/OpenFarm.Application/Profiles/IMachineProfileRepository.cs` (`AddAsync`, `GetByIdAsync`,
  `ListAsync`, `FindByNameAsync(ProfileSourceFormat, string)` for FR-009 duplicate lookups and §3's base
  resolution, `SaveChangesAsync` — mirrors `IPrinterRepository`'s shape)
- [X] T010 [P] Create `IFilamentProfileRepository` interface in
  `api/src/OpenFarm.Application/Profiles/IFilamentProfileRepository.cs` (same shape as
  `IMachineProfileRepository`)
- [X] T011 [P] Implement `MachineProfileRepository` in
  `api/src/OpenFarm.Infrastructure/Profiles/MachineProfileRepository.cs` against `OpenFarmDbContext`
- [X] T012 [P] Implement `FilamentProfileRepository` in
  `api/src/OpenFarm.Infrastructure/Profiles/FilamentProfileRepository.cs` against `OpenFarmDbContext`
- [X] T013 [P] Create `IProfileBundleParser` contract in
  `api/src/OpenFarm.Application/Profiles/IProfileBundleParser.cs` — `ParseAsync(Stream, CancellationToken)`
  returning the full parsed bundle for preview, plus
  `ExtractMachineDisplayFields(string rawDataJson, ProfileSourceFormat)` /
  `ExtractFilamentDisplayFields(string rawDataJson, ProfileSourceFormat)` reused by both the parser
  itself and every catalog-read query handler (research.md §2)
- [X] T014 Write `ProfileRawDataMerger` tests (TDD, write first) in
  `api/tests/OpenFarm.Application.Tests/Profiles/ProfileRawDataMergerTests.cs` against synthetic JSON
  fixtures — ancestor-first ordering, shallow last-element-wins key overwrite, and an unparseable
  ancestor blob being skipped with a structured log rather than throwing (research.md §10); confirm the
  tests fail before implementing
- [X] T015 Implement `ProfileRawDataMerger.MergeAncestorChain` in
  `api/src/OpenFarm.Application/Profiles/ProfileRawDataMerger.cs` to make T014 pass (research.md §10)
- [X] T016 Write `ProfileBundleParser` tests (fixture-replay, test-after) in
  `api/tests/OpenFarm.Infrastructure.Tests/Profiles/ProfileBundleParserTests.cs` against the real
  fixtures from T002 — classification by `machine`/`filament` subfolder, `"inherits"` extraction into
  `BaseProfileName`, both `compatible_printers`/`compatible_prints` key spellings, tolerance of missing
  keys (field omitted, not a thrown error), and rejection of a non-zip/corrupted input
- [X] T017 Implement `ProfileBundleParser` in
  `api/src/OpenFarm.Infrastructure/Profiles/ProfileBundleParser.cs` (`ZipArchive`-based, per-`SourceFormat`
  field-mapping table, both `ExtractXDisplayFields` methods) to make T016 pass (research.md §1, §2)
- [X] T018 [P] Create `MachineProfileNotFoundException` and `FilamentProfileNotFoundException` in
  `api/src/OpenFarm.Application/Profiles/` (thrown by detail/update handlers on an unknown or
  cross-tenant id, mapped to 404 by the existing `DomainExceptionHandler`)
- [X] T019 Register `IMachineProfileRepository`, `IFilamentProfileRepository`, and
  `IProfileBundleParser` in the composition root's DI container (`api/src/OpenFarm.Api/Program.cs` or
  its existing service-registration extension method)

**Checkpoint**: Foundation ready — user story implementation can now begin.

---

## Phase 3: User Story 1 - Import machine and filament profiles from a slicer bundle (Priority: P1) 🎯 MVP

**Goal**: Upload an OrcaSlicer/Bambu Studio bundle, preview every extracted preset in a selection list,
persist only the operator's selected presets.

**Independent Test**: Upload a known-good bundle containing multiple machine and filament presets,
select a subset, confirm, and verify exactly the selected presets — and only those — exist in the
catalogs afterward.

- [X] T020 [P] [US1] Create `UnsupportedProfileBundleFormatException` in
  `api/src/OpenFarm.Application/Profiles/UnsupportedProfileBundleFormatException.cs` (FR-007, thrown on
  an unrecognized/corrupted archive, mapped to 422)
- [X] T021 [P] [US1] Create the `IHasEntityIds` marker interface in
  `api/src/OpenFarm.Api/Processors/IHasEntityIds.cs` (`IReadOnlyList<string> EntityIds`, research.md §7)
- [X] T022 [US1] Extend `ActivityLogPostProcessor` in
  `api/src/OpenFarm.Api/Processors/ActivityLogPostProcessor.cs` to check the response for
  `IHasEntityIds` and write one `ActivityLogEntry` per id when present, keeping the existing
  single-`Id`-reflection path as the default for every other endpoint (research.md §7)
- [X] T023 [P] [US1] Create `PreviewProfileImportCommand` in
  `api/src/OpenFarm.Application/Profiles/PreviewProfileImportCommand.cs` (carries the uploaded stream)
- [X] T024 [US1] Implement `PreviewProfileImportCommandHandler` in
  `api/src/OpenFarm.Application/Profiles/PreviewProfileImportCommandHandler.cs` — parses the upload via
  `IProfileBundleParser`, flags `IsDuplicate` by `(Name, type)` against existing catalog rows
  (research.md §5), resolves each preset's base chain by `(SourceFormat, Name)` against same-batch
  siblings first, then prior-imported rows, merging via `ProfileRawDataMerger` for derived display
  fields and `hasUnresolvedBaseInChain` (research.md §10), and extracts `compatiblePrinterNames` as
  plain declared text with no catalog resolution (research.md §11, FR-016)
- [X] T025 [P] [US1] Create `ConfirmProfileImportCommand` in
  `api/src/OpenFarm.Application/Profiles/ConfirmProfileImportCommand.cs` (carries the re-uploaded stream
  plus the `SelectedProfileKey` list per data-model.md)
- [X] T026 [US1] Implement `ConfirmProfileImportCommandHandler` in
  `api/src/OpenFarm.Application/Profiles/ConfirmProfileImportCommandHandler.cs` — re-parses the
  re-uploaded file with the same `IProfileBundleParser`, inserts only presets matching a selected key
  whose `DuplicateAction` isn't `Skip`, storing each row's parser-extracted `BaseProfileName` verbatim
  with `BaseProfileId` left null for now (Pass 2 resolution added in US2), and throws a 422-mapped
  exception when a selected key has no match in the fresh parse (research.md §4's consistency check)
- [X] T027 [P] [US1] Create `PreviewProfileImportEndpoint` in
  `api/src/OpenFarm.Api/Endpoints/Profiles/PreviewProfileImportEndpoint.cs` — `POST
  /api/v1/profiles/imports/preview`, `AllowFileUploads()`, a conservative `MaxRequestBodySize()` cap
  (research.md §6), `[Permission("profiles:preview-import")]`, maps
  `UnsupportedProfileBundleFormatException` to 422
- [X] T028 [US1] Create `ConfirmProfileImportEndpoint` in
  `api/src/OpenFarm.Api/Endpoints/Profiles/ConfirmProfileImportEndpoint.cs` — `POST
  /api/v1/profiles/imports/confirm`, `AllowFileUploads()`, binds `Selections` as a JSON-encoded string
  form field deserialized manually (research.md §6), `[Permission("profiles:confirm-import")]`, response
  implements `IHasEntityIds` over every created profile id for T022's post-processor extension
- [X] T029 [P] [US1] Integration test: preview extracts and returns every recognized preset without
  persisting anything, in
  `api/tests/OpenFarm.Api.IntegrationTests/Profiles/PreviewProfileImportEndpointTests.cs` (FR-001–003,
  SC-004 — assert catalog counts are unchanged before/after)
- [X] T030 [P] [US1] Integration test: confirm persists only the selected presets and the rest are
  absent without a trace, in
  `api/tests/OpenFarm.Api.IntegrationTests/Profiles/ConfirmProfileImportEndpointTests.cs` (FR-004,
  SC-001, SC-003)
- [X] T031 [P] [US1] Integration test: an unsupported/corrupted bundle is rejected with 422 and creates
  zero catalog entries; a successfully-opened bundle with nothing recognizable returns 200 with both
  arrays empty, in
  `api/tests/OpenFarm.Api.IntegrationTests/Profiles/RejectedAndEmptyBundleTests.cs` (FR-007, FR-008,
  SC-005)
- [X] T032 [P] [US1] Integration test: a duplicate-named preset is flagged `isDuplicate: true` at
  preview, and confirming with `Skip` vs. `ImportAsNew` produces the correct resulting catalog row
  count, in `api/tests/OpenFarm.Api.IntegrationTests/Profiles/DuplicateProfileImportTests.cs` (FR-009)
- [X] T033 [P] [US1] Integration test: a multi-preset confirm call writes one `ActivityLogEntry` with
  `EventType: "ProfileImported"` per created profile, not one entry for the whole batch, in
  `api/tests/OpenFarm.Api.IntegrationTests/ActivityLog/ProfileImportActivityLogTests.cs` (research.md
  §7)
- [X] T034 [P] [US1] Regenerate the Orval-generated API client against the updated OpenAPI spec to
  produce the preview/confirm hooks in `web/src/lib/api/generated/profiles/`
- [X] T035 [US1] Create `ImportProfilesDialog.tsx` in
  `web/src/features/profiles/components/ImportProfilesDialog.tsx` — file select → preview call →
  selection list → confirm call, replacing the existing "Not yet implemented" toast button
- [X] T036 [US1] Create `ProfileReviewList.tsx` in
  `web/src/features/profiles/components/ProfileReviewList.tsx` — selection checkboxes and per-preset
  skip/import-as-new duplicate controls (FR-003, FR-009); FR-006's base-not-selected warning is deferred
  to US2
- [X] T037 [US1] Create the `ProfilesPage.tsx` shell in `web/src/features/profiles/ProfilesPage.tsx` —
  Machine/Filament `Tabs` (shadcn `tabs.tsx`) housing the Import trigger wired to
  `ImportProfilesDialog`, replacing the mock-data prototype's entry point; catalog table content is
  deferred to US3
- [X] T038 [P] [US1] Vitest + RTL tests for `ImportProfilesDialog.tsx` and `ProfileReviewList.tsx` in
  `web/src/features/profiles/components/` (file-select → preview → select subset → confirm flow;
  closing/cancelling before confirm leaves nothing selected)

**Checkpoint**: US1 is fully functional and independently testable — upload → preview → select →
confirm → catalog has exactly the selected presets.

---

## Phase 4: User Story 2 - Preserve base/override relationships from the source bundle (Priority: P2)

**Goal**: A derived preset's imported profile links to its base profile instead of being flattened into
an independent copy.

**Independent Test**: Import a bundle containing one base filament preset and one override preset that
depends on it, select both, and verify the resulting catalog record shows the override linked to its
base rather than as a flattened standalone copy.

- [ ] T039 Write two-pass base/override resolution tests (TDD, write first) in
  `api/tests/OpenFarm.Application.Tests/Profiles/ConfirmProfileImportCommandHandlerResolutionTests.cs`
  [US2] against synthetic in-batch-match, prior-import-match, no-match, and ambiguous-match (both
  in-batch and prior-import) fixtures, scoped by `(SourceFormat, Name)`; confirm the tests fail before
  implementing (research.md §3)
- [ ] T040 [US2] Extend `ConfirmProfileImportCommandHandler` in
  `api/src/OpenFarm.Application/Profiles/ConfirmProfileImportCommandHandler.cs` with Pass 2 base
  resolution to make T039 pass — resolves `BaseProfileId` only on an unambiguous `(SourceFormat, Name)`
  match against same-batch siblings first, then prior-imported rows, leaving it null on no match or 2+
  candidates from either source (research.md §3)
- [ ] T041 [P] [US2] Integration test: selecting both a base and its override links the override's
  `baseProfileId` to the base's `id`, in
  `api/tests/OpenFarm.Api.IntegrationTests/Profiles/BaseOverrideResolutionTests.cs` (FR-005, SC-002, US2
  Acceptance Scenario 1)
- [ ] T042 [P] [US2] Integration test: importing an override alone keeps `baseProfileName` populated with
  `baseProfileId` null (a dangling built-in-default reference is a normal outcome, not an error), and a
  duplicate-named base produces the same unresolved shape (ambiguous match), in the same test file
  (FR-005, US2 Acceptance Scenario 3, research.md §3)
- [ ] T043 [US2] Add the FR-006 base-not-selected warning to `ProfileReviewList.tsx` in
  `web/src/features/profiles/components/ProfileReviewList.tsx` — computed entirely client-side from the
  full preview array plus live selection state, no backend round trip (research.md §4)
- [ ] T044 [P] [US2] Vitest test for the FR-006 warning in
  `web/src/features/profiles/components/ProfileReviewList.test.tsx` (warning appears when an override is
  checked but its bundle-present base is unchecked, and disappears once the base is also checked)

**Checkpoint**: US1 and US2 both work independently.

---

## Phase 5: User Story 3 - Browse imported profile catalogs (Priority: P2)

**Goal**: See every imported machine profile and every imported filament profile in two separate
catalog lists, plus each profile's own raw data.

**Independent Test**: Import a bundle with known presets and confirm each imported preset appears, with
its key identifying fields, in the corresponding catalog list.

- [X] T045 [P] [US3] Create `GetMachineProfilesQuery` + Handler in
  `api/src/OpenFarm.Application/Profiles/GetMachineProfilesQuery.cs` /
  `GetMachineProfilesQueryHandler.cs` — one `ListAsync` call, builds an in-memory `Id → row` map, walks
  each row's `BaseProfileId` chain in memory (cycle/depth-guarded), merges via `ProfileRawDataMerger`,
  then calls `ExtractMachineDisplayFields` on the merged result — no extra DB round trips, no recursive
  CTE (research.md §10)
- [X] T046 [P] [US3] Create `GetFilamentProfilesQuery` + Handler in
  `api/src/OpenFarm.Application/Profiles/GetFilamentProfilesQuery.cs` /
  `GetFilamentProfilesQueryHandler.cs` — same approach as T045 via `ExtractFilamentDisplayFields`, plus
  `compatiblePrinterNames` (FR-016)
- [X] T047 [P] [US3] Create `GetMachineProfileQuery` + Handler in
  `api/src/OpenFarm.Application/Profiles/GetMachineProfileQuery.cs` / `GetMachineProfileQueryHandler.cs`
  — the row's own `rawDataJson` (FR-011) plus chain-merged derived fields via sequential bounded
  fetch-by-id up `BaseProfileId`, reversed to ancestor-first before merging (research.md §10); throws
  `MachineProfileNotFoundException` on an unknown/cross-tenant id
- [X] T048 [P] [US3] Create `GetFilamentProfileQuery` + Handler in
  `api/src/OpenFarm.Application/Profiles/GetFilamentProfileQuery.cs` / `GetFilamentProfileQueryHandler.cs`
  — same approach as T047 for `FilamentProfile`, throws `FilamentProfileNotFoundException`
- [X] T049 [P] [US3] Create `ListMachineProfilesEndpoint` + `GetMachineProfileEndpoint` in
  `api/src/OpenFarm.Api/Endpoints/Profiles/` — `GET /api/v1/profiles/machine`
  (`[Permission("profiles:list-machine")]`) and `GET /api/v1/profiles/machine/{id}`
  (`[Permission("profiles:get-machine")]`)
- [X] T050 [P] [US3] Create `ListFilamentProfilesEndpoint` + `GetFilamentProfileEndpoint` in
  `api/src/OpenFarm.Api/Endpoints/Profiles/` — `GET /api/v1/profiles/filament`
  (`[Permission("profiles:list-filament")]`) and `GET /api/v1/profiles/filament/{id}`
  (`[Permission("profiles:get-filament")]`)
- [ ] T051 [P] [US3] Integration test: machine and filament catalog list endpoints return every
  imported profile with `name`, `sourceFormat`, and derived spec fields, in
  `api/tests/OpenFarm.Api.IntegrationTests/Profiles/ListProfilesEndpointTests.cs` (FR-010, US3
  Acceptance Scenarios 1–2)
- [ ] T052 [P] [US3] Integration test: a filament profile response includes `compatiblePrinterNames` as
  declared text, identical whether or not the named printer exists elsewhere in the catalog, in
  `api/tests/OpenFarm.Api.IntegrationTests/Profiles/CompatiblePrinterNamesTests.cs` (FR-016, US3
  Acceptance Scenario 3)
- [ ] T053 [P] [US3] Integration test: the single-profile detail endpoint returns full `rawDataJson`
  plus chain-merged derived fields and `hasUnresolvedBaseInChain`, and 404s for an unknown/cross-tenant
  id, in `api/tests/OpenFarm.Api.IntegrationTests/Profiles/GetProfileDetailEndpointTests.cs` (FR-011,
  SC-006)
- [ ] T054 [P] [US3] Regenerate the Orval-generated API client for the four new list/detail endpoints in
  `web/src/lib/api/generated/profiles/`
- [ ] T055 [P] [US3] Create `MachineProfilesTable.tsx` and `FilamentProfilesTable.tsx` in
  `web/src/features/profiles/components/`, replacing the single merged `ProfilesTable.tsx`, wired into
  `ProfilesPage.tsx`'s two Tabs
- [ ] T056 [US3] Create `ProfileRawDataDialog.tsx` (view-only) in
  `web/src/features/profiles/components/ProfileRawDataDialog.tsx` — opens from a catalog row click and
  shows the profile's full raw JSON (FR-011), reachable in ≤3 interactions from the catalog list
  (SC-006); edit capability is deferred to US4
- [ ] T057 [P] [US3] Delete `web/src/features/profiles/data/mockProfiles.ts`,
  `web/src/features/profiles/data/mockProfilesStore.ts`, and the old
  `web/src/features/profiles/components/ProfilesTable.tsx`, updating `ProfilesPage.test.tsx` to cover
  the real API-backed tables instead of the mock prototype
- [ ] T058 [P] [US3] Vitest + RTL tests for `MachineProfilesTable.tsx`, `FilamentProfilesTable.tsx`, and
  `ProfileRawDataDialog.tsx` in `web/src/features/profiles/components/` (catalog rows render key fields;
  opening a row shows raw JSON within the 3-interaction budget)

**Checkpoint**: US1, US2, and US3 all work independently.

---

## Phase 6: User Story 4 - Correct an imported profile's raw data (Priority: P3)

**Goal**: Edit an imported profile's raw JSON through a viewer/editor and save the change.

**Independent Test**: Open an imported profile's JSON editor, change a field value, save, and confirm
the catalog reflects the new value; submitting malformed JSON is rejected with the previous data
unchanged.

- [ ] T059 [P] [US4] Create `InvalidProfileJsonException` in
  `api/src/OpenFarm.Application/Profiles/InvalidProfileJsonException.cs` (FR-013, mapped to 422 by the
  existing `DomainExceptionHandler`)
- [ ] T060 [P] [US4] Create `UpdateMachineProfileRawDataCommand` + Validator in
  `api/src/OpenFarm.Application/Profiles/UpdateMachineProfileRawDataCommand.cs` /
  `UpdateMachineProfileRawDataCommandValidator.cs` (validates the submitted string is non-empty; JSON
  well-formedness is checked in the handler so a parse failure can throw the typed exception above)
- [ ] T061 [US4] Implement `UpdateMachineProfileRawDataCommandHandler` in
  `api/src/OpenFarm.Application/Profiles/UpdateMachineProfileRawDataCommandHandler.cs` — parses the
  submitted JSON, throws `InvalidProfileJsonException` on failure leaving the stored row untouched,
  otherwise overwrites `RawDataJson` only, with no re-derivation step (data-model.md)
- [ ] T062 [P] [US4] Create `UpdateFilamentProfileRawDataCommand` + Validator + Handler in
  `api/src/OpenFarm.Application/Profiles/UpdateFilamentProfileRawDataCommand.cs` /
  `UpdateFilamentProfileRawDataCommandValidator.cs` / `UpdateFilamentProfileRawDataCommandHandler.cs`
  (same behavior as T060/T061, scoped to `FilamentProfile`)
- [ ] T063 [P] [US4] Create `UpdateMachineProfileRawDataEndpoint` in
  `api/src/OpenFarm.Api/Endpoints/Profiles/UpdateMachineProfileRawDataEndpoint.cs` — `PUT
  /api/v1/profiles/machine/{id}/raw-data`, `[Permission("profiles:edit-machine-raw-data")]`, returns the
  same shape as the detail `GET` with derived fields recomputed from the saved value
- [ ] T064 [P] [US4] Create `UpdateFilamentProfileRawDataEndpoint` in
  `api/src/OpenFarm.Api/Endpoints/Profiles/UpdateFilamentProfileRawDataEndpoint.cs` — `PUT
  /api/v1/profiles/filament/{id}/raw-data`, `[Permission("profiles:edit-filament-raw-data")]`
- [ ] T065 [P] [US4] Integration test: a valid raw-data save is reflected on the next `GET`; malformed
  JSON returns 422 and leaves the previously saved value unchanged, in
  `api/tests/OpenFarm.Api.IntegrationTests/Profiles/UpdateProfileRawDataEndpointTests.cs` (FR-012,
  FR-013, US4 Acceptance Scenarios 1–2)
- [ ] T066 [P] [US4] Integration test: editing a base profile's raw JSON changes its descendant's
  derived display fields on the descendant's very next read, with no propagation step anywhere, in the
  same test file (data-model.md's chain-propagation-for-free behavior)
- [ ] T067 [P] [US4] Regenerate the Orval-generated API client for the two raw-data update endpoints in
  `web/src/lib/api/generated/profiles/`
- [ ] T068 [US4] Extend `ProfileRawDataDialog.tsx` in
  `web/src/features/profiles/components/ProfileRawDataDialog.tsx` with an editable JSON textarea
  (react-hook-form + zod), a Save action wired to the `PUT` hook, and inline error display on a 422
  response (FR-012, FR-013)
- [ ] T069 [P] [US4] Vitest + RTL test for the edit/save flow in
  `web/src/features/profiles/components/ProfileRawDataDialog.test.tsx` (successful save updates
  displayed values; malformed JSON shows an error and leaves the last-saved value visible)

**Checkpoint**: All four user stories are independently functional.

---

## Phase 7: Polish & Cross-Cutting Concerns

- [ ] T070 [P] Run `dotnet format api/OpenFarm.slnx --verify-no-changes` and `bun run lint` / `bun run
  format` across all new/changed files, fixing any violations
- [ ] T071 Run the full `quickstart.md` validation (Scenarios 1–6 plus the activity-log coverage check)
  against a local `docker compose up` stack with a real captured bundle
- [ ] T072 Run `dotnet test api/OpenFarm.slnx` and `bun run test` end-to-end and confirm the full suite
  passes

---

## Dependencies & Execution Order

### Phase Dependencies

- **Setup (Phase 1)**: No dependencies — can start immediately.
- **Foundational (Phase 2)**: Depends on Setup completion — BLOCKS all user stories.
- **User Stories (Phase 3+)**: All depend on Foundational completion.
  - US1 (Phase 3) has no dependency on any other story.
  - US2 (Phase 4) depends on US1's `ConfirmProfileImportCommandHandler` (T026) existing, since it
    extends the same handler with Pass 2 resolution (T040) — cannot start until T026 lands.
  - US3 (Phase 5) depends only on Foundational (entities, repositories, parser); it does not depend on
    US1/US2's command handlers, only on data those imports would have created for a real end-to-end
    test, so it can be built in parallel with US1/US2 and exercised independently once at least one
    import path (even a partial one) has run.
  - US4 (Phase 6) depends on US3's `GetMachineProfileQuery`/`GetFilamentProfileQuery` and
    `ProfileRawDataDialog.tsx` (view-only) existing, since it extends the same dialog and read shape
    with edit/save.
- **Polish (Phase 7)**: Depends on all four user stories being complete.

### Within Each User Story

- TDD tasks (T014/T015 merger, T039/T040 resolution) are written test-first, confirmed failing, then
  implemented.
- Test-after tasks (parser fixture-replay, integration tests, component tests) are implemented first,
  then verified.
- Entities/interfaces before handlers; handlers before endpoints; endpoints before frontend hooks;
  frontend hooks before components.

### Parallel Opportunities

- All Setup tasks marked [P] can run in parallel.
- Within Foundational: T003–T005 (entities), T006–T007 (EF configs), T009–T010 (repository interfaces),
  T011–T012 (repository implementations), and T018 (exceptions) are each parallel groups; T008
  (migration) needs T004–T007 first; T017 (parser impl) needs T013 (contract) and T016 (its tests)
  first.
- Once Foundational completes, US1 and US3's backend tasks can largely proceed in parallel (different
  files) — US3's query handlers don't touch anything US1's command handlers touch. US2 must wait on
  US1's T026. US4 must wait on US3's T047/T048/T056.
- Within US1: T020–T022 (exceptions/post-processor), T023/T025 (commands), and T029–T033 (integration
  tests, once their implementation exists) are parallel groups.
- Within US3: T045–T048 (query handlers), T049–T050 (endpoints), and T051–T053 (integration tests) are
  parallel groups.

---

## Parallel Example: User Story 1

```bash
# Once Foundational is complete, launch these together:
Task: "Create UnsupportedProfileBundleFormatException in api/src/OpenFarm.Application/Profiles/UnsupportedProfileBundleFormatException.cs"
Task: "Create IHasEntityIds marker interface in api/src/OpenFarm.Api/Processors/IHasEntityIds.cs"
Task: "Create PreviewProfileImportCommand in api/src/OpenFarm.Application/Profiles/PreviewProfileImportCommand.cs"
Task: "Create ConfirmProfileImportCommand in api/src/OpenFarm.Application/Profiles/ConfirmProfileImportCommand.cs"
```

---

## Implementation Strategy

### MVP First (User Story 1 Only)

1. Complete Phase 1: Setup.
2. Complete Phase 2: Foundational (blocks all stories).
3. Complete Phase 3: User Story 1.
4. **STOP and VALIDATE**: run quickstart.md Scenarios 1, 2, and 4 independently.
5. Deploy/demo if ready — a working import-and-persist path with no base/override linking, browsing, or
   editing yet.

### Incremental Delivery

1. Setup + Foundational → foundation ready.
2. US1 → test independently (quickstart Scenarios 1, 2, 4) → deploy/demo (MVP!).
3. US2 → test independently (quickstart Scenario 3) → deploy/demo.
4. US3 → test independently (quickstart Scenario 6's list/detail parts) → deploy/demo.
5. US4 → test independently (quickstart Scenario 6's edit part) → deploy/demo.
6. Polish → full quickstart + full suite run.

### Parallel Team Strategy

With multiple developers, after Foundational completes:
- Developer A: US1 (blocks US2).
- Developer B: US3 (independent of US1/US2, but needs Foundational's entities/parser/repos).
- Once US1 lands, Developer A or C: US2.
- Once US3 lands, Developer B or C: US4.

---

## Notes

- [P] tasks = different files, no dependencies on incomplete tasks.
- [Story] label maps a task to its user story for traceability; Setup/Foundational/Polish carry no
  label.
- Permission strings (`profiles:preview-import`, `profiles:confirm-import`, `profiles:list-machine`,
  `profiles:get-machine`, `profiles:list-filament`, `profiles:get-filament`,
  `profiles:edit-machine-raw-data`, `profiles:edit-filament-raw-data`) follow the existing
  `printers:*`/`connection-secrets:*` naming convention; per spec.md's Assumptions and
  contracts/profiles-api.md, only the authenticated-only policy shim is enforced today — these strings
  exist so Phase 4's real permission catalog seed needs no code change.
- No `PendingImportBatch` staging entity, no `SourceFileName` column, no `SupportsLiveIntegration`
  field, and no compatible-printer → `MachineProfile` resolution — all deliberately cut per plan.md's
  Complexity Tracking; do not add them back without a new FR/SC driving it.
- Commit after each task or logical group; stop at any checkpoint to validate a story independently.
