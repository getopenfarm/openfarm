# Tasks: Multi-Engine, Multi-Version Slicer Management

**Input**: Design documents from `/specs/008-orca-slicer-multi-version/`

**Prerequisites**: plan.md, spec.md, research.md, data-model.md, contracts/slicer-management-api.md, contracts/slicer-manifest.schema.json, quickstart.md

**Tests**: Included. Constitution II / plan.md mandate TDD for the deterministic new logic (`SlicerVersionComparer`, default resolution, `ResolveSlicerSelection`, provisioning manifest/asset/idempotency logic); quickstart.md's "Automated test coverage" section spells out the remaining test-after coverage CI is expected to assert, so this list is treated as explicitly requested rather than optional.

**Organization**: Tasks are grouped by user story. Phase 2 (Foundational) carries the shared domain/infrastructure/persistence plumbing both P1 stories build on — this mirrors research.md/data-model.md, where the catalog, invocation strategy, and migration are single cross-cutting deltas, not per-story work.

## Format: `[ID] [P?] [Story] Description`

- **[P]**: Can run in parallel (different files, no dependencies)
- **[Story]**: US1, US2, US3 — omitted for Setup/Foundational/Polish
- File paths are exact, based on the existing Phase 4 `Parts` slicing code already in the repo

---

## Phase 1: Setup

**Purpose**: Nothing new needs scaffolding beyond what Phase 2 introduces in place — the backend already has all four projects, the frontend already has the `settings` and `parts` features, and `tools/provision-slicers` is genuinely new but is scoped entirely to US3 (Phase 5), so its project-creation task lives there per "story-specific setup → within that story's phase."

- [ ] T001 Remove `Slicing:OrcaSlicerCliPath` / `Slicing:BambuStudioCliPath` and add `SlicerBundle:RootDirectory` in `api/src/OpenFarm.Api/appsettings.json` and `api/src/OpenFarm.Api/appsettings.Development.json` (research §11); update `.env.example` / `infra/.env` sample if a `SlicerBundle__RootDirectory` entry is needed for local compose

**Checkpoint**: Config keys exist for Phase 2 to bind against.

---

## Phase 2: Foundational (Blocking Prerequisites)

**Purpose**: The engine-generic discovery/selection/recording model both P1 stories depend on — the catalog, the invocation strategy, the migration, and the `Organization` default shape. No user story can be completed without this phase (US1 needs discovery + one invocation path; US2 needs the same catalog plus multi-engine comparison + the default-setting gate).

**⚠️ CRITICAL**: Complete this phase before starting Phase 3 or Phase 4.

### Domain value types & enum

- [ ] T002 [P] Add `SuperSlicer` to the `SlicerEngine` enum in `api/src/OpenFarm.Domain/Parts/SlicerEngine.cs` (FR-001/FR-022)
- [ ] T003 [P] Create `SlicerPlatform` enum (`Linux | Windows | MacOs`) in `api/src/OpenFarm.Domain/Parts/SlicerPlatform.cs` (FR-009/FR-010)
- [ ] T004 [P] Create `SlicerRef` value object `(SlicerEngine Engine, string Version)` with non-empty-version validation and engine+ordinal-version equality in `api/src/OpenFarm.Domain/Parts/SlicerRef.cs` (data-model.md "SlicerRef")
- [ ] T005 [P] Create `AvailableSlicer` value object `(SlicerEngine Engine, string Version, SlicerPlatform Platform)` plus an internal (non-serialized) executable-location field in `api/src/OpenFarm.Domain/Parts/AvailableSlicer.cs` (data-model.md "AvailableSlicer")
- [ ] T006 [P] Create `SlicerUnavailableException` in `api/src/OpenFarm.Domain/Parts/SlicerUnavailableException.cs`, message naming the engine+version (FR-006/FR-021), e.g. `"SuperSlicer 2.5.59.13 is not bundled for this platform (Linux)."`

### TDD: SlicerVersionComparer (write tests first)

- [ ] T007 [P] Write failing tests for `SlicerVersionComparer` in `api/tests/OpenFarm.Domain.Tests/Parts/SlicerVersionComparerTests.cs` covering: `2.2.0` vs `2.3.0`; `2.5.59.13` (4-component) vs `2.2.0` (3-component, zero-padded); Bambu's `01.10.00.00` form; an RC/non-numeric-suffix build ordering before its clean release; lexical tiebreak; same-engine-only comparison (research §3)
- [ ] T008 Implement `SlicerVersionComparer : IComparer<string>` in `api/src/OpenFarm.Domain/Parts/SlicerVersionComparer.cs` to make T007 pass (depends on T007)

### Application contracts

- [ ] T009 [P] Create `IAvailableSlicerCatalog` interface (e.g. `IReadOnlyList<AvailableSlicer> GetAvailable(); AvailableSlicer? Find(SlicerRef, SlicerPlatform);`) in `api/src/OpenFarm.Application/Parts/IAvailableSlicerCatalog.cs` (research §1/§2)
- [ ] T010 [P] Create `ISlicerInvocation` interface — builds process `FileName` + argument list from an `AvailableSlicer` + slice inputs (model file path, material profile, nozzle diameter, output path) — in `api/src/OpenFarm.Application/Parts/ISlicerInvocation.cs` (research §4)
- [ ] T011 Change `ISlicerEngine.SliceAsync` in `api/src/OpenFarm.Application/Parts/ISlicerEngine.cs` to accept a resolved `SlicerRef` (or `AvailableSlicer`) instead of a raw `SlicerEngine` value (depends on T004/T005)

### TDD: ResolveSlicerSelection (write tests first)

- [ ] T012 [P] Write failing tests for `ResolveSlicerSelection` in `api/tests/OpenFarm.Application.Tests/Parts/ResolveSlicerSelectionTests.cs` covering: explicit `(engine, version)` present-for-host-platform → accepted; explicit pair absent → `SlicerUnavailableException`; engine-only override → newest version of that engine; no override → effective org default; no default and empty catalog → typed failure naming "no slicer available" (research §5/§7, FR-003/FR-006/FR-007/FR-010)
- [ ] T013 Implement `ResolveSlicerSelection` application service in `api/src/OpenFarm.Application/Parts/ResolveSlicerSelection.cs`, using `IAvailableSlicerCatalog` + `SlicerVersionComparer` for the OrcaSlicer-preferred/newest-any-engine fallback (research §5), to make T012 pass (depends on T008, T009, T012)

### Organization default (engine, version) pair

- [ ] T014 Add nullable `DefaultSlicerVersion : string?` alongside the existing `DefaultSlicerEngine` on `api/src/OpenFarm.Domain/Organizations/Organization.cs`; replace `SetDefaultSlicerEngine(engine)` with `SetDefaultSlicer(SlicerRef)` (data-model.md "Organization", depends on T004)
- [ ] T015 [P] Write/extend unit tests for `Organization.SetDefaultSlicer` in `api/tests/OpenFarm.Domain.Tests/Organizations/OrganizationTests.cs` (create the `Organizations/` folder if absent) — accepts a `SlicerRef`, persists both halves

### Infrastructure: discovery, catalog, invocation, migration

- [ ] T016 [P] Create `HostPlatform` resolver (`RuntimeInformation.IsOSPlatform` → `SlicerPlatform`) in `api/src/OpenFarm.Infrastructure/Parts/HostPlatform.cs` (research §4/§9)
- [ ] T017 Create `SlicerBundleOptions { RootDirectory }` in `api/src/OpenFarm.Infrastructure/Parts/SlicerBundleOptions.cs`, replacing `SlicerOptions`'s path fields; keep `CliTimeout`, `MaxConcurrentSlicingOperations`, `GenericFallbackMaterialProfile` (extend the map with a `SuperSlicer` entry) either on this class or a retained sibling options class — mirror today's `SlicerOptions.cs` shape minus `OrcaSlicerCliPath`/`BambuStudioCliPath`/`CliPathFor` (data-model.md "Configuration delta")
- [ ] T018 [P] Write tests for `BundledSlicerScanner` in `api/tests/OpenFarm.Infrastructure.Tests/Parts/BundledSlicerScannerTests.cs` against a temp directory tree following the `<root>/<engineSlug>/<version>/<platformSlug>/…` convention (research §1) — covers empty root, one leaf, multiple engines/versions, duplicate-leaf de-duplication with a logged warning, and platform filtering (spec Edge Cases)
- [ ] T019 Implement `BundledSlicerScanner` in `api/src/OpenFarm.Infrastructure/Parts/BundledSlicerScanner.cs` — walks `SlicerBundleOptions.RootDirectory` into the `<engineSlug>/<version>/<platformSlug>` convention, builds `AvailableSlicer` entries, de-duplicates deterministically (stable order by resolved path) — to make T018 pass (depends on T005, T016, T017, T018)
- [ ] T020 Implement `AvailableSlicerCatalog : IAvailableSlicerCatalog` as an eager-initialized singleton in `api/src/OpenFarm.Infrastructure/Parts/AvailableSlicerCatalog.cs` — runs `BundledSlicerScanner` once at construction, exposes an immutable snapshot (research §2, depends on T009, T019)
- [ ] T021 [P] Write tests for the two `ISlicerInvocation` strategies in `api/tests/OpenFarm.Infrastructure.Tests/Parts/SlicerInvocationTests.cs` — expected argument lists for `PrusaSliceFamilyInvocation` (today's `CliSlicerEngine` arg string, unchanged) vs. `SuperSlicerInvocation` (research §4)
- [ ] T022 [P] Implement `PrusaSliceFamilyInvocation` in `api/src/OpenFarm.Infrastructure/Parts/SlicerInvocation/PrusaSliceFamilyInvocation.cs` — lift the existing `--slice 0 --load-filaments … --nozzle-diameter … --export-gcode …` argument-building logic out of `CliSlicerEngine.cs` unchanged, used for `OrcaSlicer` and `BambuStudio` (depends on T010, T021)
- [ ] T023 [P] Implement `SuperSlicerInvocation` in `api/src/OpenFarm.Infrastructure/Parts/SlicerInvocation/SuperSlicerInvocation.cs` per SuperSlicer's PrusaSlicer-fork CLI form; include per-platform executable-entry resolution (AppImage/`AppRun` on Linux, `*.exe` on Windows, `.app/Contents/MacOS/…` on macOS) shared with T022 (depends on T010, T021)
- [ ] T024 Update `CliSlicerEngine` in `api/src/OpenFarm.Infrastructure/Parts/CliSlicerEngine.cs` to resolve the `AvailableSlicer` from `IAvailableSlicerCatalog` and delegate argument-building to the `ISlicerInvocation` selected by engine, keeping its existing orchestration (temp dir, process lifetime, timeout, failure → `SlicingFailedException`) (depends on T011, T020, T022, T023)
- [ ] T025 [P] Update `FakeSlicerEngine` in `api/src/OpenFarm.Infrastructure/Parts/FakeSlicerEngine.cs` to match the new `ISlicerEngine.SliceAsync` signature (depends on T011)
- [ ] T026 [P] Update `SlicerDefaultsProvider`/`ISlicerDefaultsProvider` (`api/src/OpenFarm.Infrastructure/Parts/SlicerDefaultsProvider.cs`, `api/src/OpenFarm.Application/Parts/ISlicerDefaultsProvider.cs`) so `GetGenericFallbackMaterialProfile(SlicerEngine)` covers the new `SuperSlicer` member (depends on T002)

### Persistence migration (one, hand-authored)

- [ ] T027 Add `builder.Property(x => x.SlicerVersionUsed)` (nullable `text`) to `api/src/OpenFarm.Infrastructure/Persistence/Configurations/PartConfiguration.cs`, `PartProcessingJobConfiguration.cs`, and `DefaultSlicerVersion` to `OrganizationConfiguration.cs` (data-model.md "Persistence delta", depends on T014)
- [ ] T028 Add `SlicerVersionUsed : string?` to `api/src/OpenFarm.Domain/Parts/Part.cs` and `api/src/OpenFarm.Domain/Parts/PartProcessingJob.cs`, set together with the existing engine field when a resolved `SlicerRef` is recorded (FR-005, depends on T004)
- [ ] T029 Author the hand-written EF migration `AddSlicerVersionTracking` in `api/src/OpenFarm.Infrastructure/Persistence/Migrations/<yyyyMMddHHmmss>_AddSlicerVersionTracking.cs`, following the `[DbContext]`/`[Migration]` attribute pattern from `20260719150200_AddOrganizationSlicingDefaults.cs` — `Up` adds the three nullable `text` columns (`Parts.SlicerVersionUsed`, `PartProcessingJobs.SlicerVersionUsed`, `Organizations.DefaultSlicerVersion`), `Down` drops them (depends on T027, T028)

### DI wiring & exception mapping

- [ ] T030 Update `api/src/OpenFarm.Api/Program.cs`: replace the `SlicerOptions` registration with `SlicerBundleOptions`, register `IAvailableSlicerCatalog`/`AvailableSlicerCatalog` as an eager singleton (resolved once at startup, not lazily on first request — research §2), register the two `ISlicerInvocation` strategies keyed by engine (depends on T017, T020, T022, T023)
- [ ] T031 [P] Add `SlicerUnavailableException => (StatusCodes.Status422UnprocessableEntity, "...")` mapping to `api/src/OpenFarm.Api/ExceptionHandling/DomainExceptionHandler.cs` (depends on T006)

**Checkpoint**: Foundation ready — catalog discovers bundled slicers at startup, `ResolveSlicerSelection` gates both call sites, the migration is authored, and `CliSlicerEngine` is catalog-driven. User story work can begin.

---

## Phase 3: User Story 1 - Slice with a bundled, backend-discovered slicer (Priority: P1) 🎯 MVP

**Goal**: With at least one `(engine, version)` present in the bundle, an upload that requires slicing is sliced with that discovered engine+version, without any path/binary ever surfacing, and the result is recorded and reproducible; an empty/broken bundle fails fast instead of hanging.

**Independent Test**: With one supported engine+version present in the bundle, upload a part that requires slicing and confirm it slices successfully and the resulting part records the engine and version used. With the bundled binary absent/broken, confirm the upload fails fast with a clear message instead of hanging.

### Tests for User Story 1

- [ ] T032 [P] [US1] Integration test in `api/tests/OpenFarm.Api.IntegrationTests/Parts/CreatePartEndpointTests.cs`: with one discovered `(engine, version)`, upload a requires-slicing part → `202`, then the job and part read models expose the resolved `slicer: { engine, version }` (spec Acceptance Scenario US1-2)
- [ ] T033 [P] [US1] Integration test (same file or a new `SlicingAvailabilityTests.cs` in `api/tests/OpenFarm.Api.IntegrationTests/Parts/`): empty bundle root → uploading a requires-slicing part returns `422`/`409` "no slicer available" `ProblemDetails` promptly, and the job never reaches/stays in a Processing state (spec Acceptance Scenario US1-4, FR-006/SC-002)
- [ ] T034 [P] [US1] Integration or infra test: a bundled leaf whose binary is broken → the slice fails at slice time via `SlicingFailedException`, reaching terminal `Failed` status, never a hang (spec Acceptance Scenario US1-3, Constitution V)
- [ ] T035 [P] [US1] Extend `api/tests/OpenFarm.Infrastructure.Tests/Parts/CliSlicerEngineTests.cs` for the new catalog-driven resolution path (depends on T024)

### Implementation for User Story 1

- [ ] T036 [US1] Update `GetSlicingDefaultsQuery`/`GetSlicingDefaultsQueryHandler` (`api/src/OpenFarm.Application/Parts/GetSlicingDefaultsQuery.cs` + handler) to return the effective default (`{ engine, version, isExplicit }`, `null` when no slicer is available) plus the full `available[]` list ordered by engine then version descending via `SlicerVersionComparer` (contracts/slicer-management-api.md GET, depends on T009, T013)
- [ ] T037 [US1] Update `GetSlicingDefaultsEndpoint` (`api/src/OpenFarm.Api/Endpoints/Settings/GetSlicingDefaultsEndpoint.cs`) response DTO to the new `{ default, available[] }` shape — no path field anywhere (depends on T036)
- [ ] T038 [US1] Update `AddPartCommand`/`AddPartCommandHandler` (`api/src/OpenFarm.Application/Parts/AddPartCommand.cs` + handler) to resolve the slicer selection via `ResolveSlicerSelection` when no explicit override is given (the "no override → effective default" path), record the resolved `SlicerRef` on the created `PartProcessingJob` (depends on T013, T028)
- [ ] T039 [US1] Update `PartProcessingBackgroundService`/job-confirm flow (`api/src/OpenFarm.Infrastructure/Parts/PartProcessingBackgroundService.cs` and the confirm command/handler under `api/src/OpenFarm.Application/Parts/`) so a requires-slicing job with no available slicer fails promptly with `SlicerUnavailableException` rather than starting/hanging (FR-006, depends on T013, T024)
- [ ] T040 [US1] Propagate the resolved engine+version from the confirmed `PartProcessingJob` onto the `Part` (`api/src/OpenFarm.Application/Parts/ConfirmPartProcessingJobCommandHandler.cs` or equivalent) so `Part.SlicerVersionUsed` is set alongside the existing engine field (FR-005, depends on T028)
- [ ] T041 [P] [US1] Add `slicer: { engine, version } | null` to the `GetPartProcessingJobEndpoint` response (`api/src/OpenFarm.Api/Endpoints/PartProcessingJobs/GetPartProcessingJobEndpoint.cs`) (contracts/slicer-management-api.md, depends on T028)
- [ ] T042 [P] [US1] Add `slicer: { engine, version } | null` to the `GetPartEndpoint` response (`api/src/OpenFarm.Api/Endpoints/Parts/GetPartEndpoint.cs`) (contracts/slicer-management-api.md, depends on T028)

**Checkpoint**: A single bundled `(engine, version)` slices parts end-to-end with the result recorded and readable; an empty/broken bundle fails fast. User Story 1 is independently testable and deployable as the MVP.

---

## Phase 4: User Story 2 - Run multiple engines and versions side by side and choose one (Priority: P1)

**Goal**: With several `(engine, version)` slicers available, an administrator sets an org default and an operator can override per part; each part records exactly what it was sliced with; changing the default never rewrites already-processed parts.

**Independent Test**: With versions of two different engines in the bundle, set one `(engine, version)` as default, upload one part with the default and another part overriding to a different engine+version, and confirm each part records the engine+version it was actually sliced with.

### Tests for User Story 2

- [ ] T043 [P] [US2] Integration test in `api/tests/OpenFarm.Api.IntegrationTests/Settings/UpdateSlicingDefaultsEndpointTests.cs` (new file, mirror `CreatePartEndpointTests.cs` conventions): valid `(engine, version)` → `200`, `default.isExplicit: true`, and a `SlicingDefaultsUpdated` activity-log entry exists (contracts/slicer-management-api.md PUT, FR-019)
- [ ] T044 [P] [US2] Extend the same file (or a sibling) for the `422` unavailable-pair case (`(engine, version)` not present for host platform) (contracts/slicer-management-api.md PUT, FR-007)
- [ ] T045 [P] [US2] Integration test in `api/tests/OpenFarm.Api.IntegrationTests/Parts/CreatePartEndpointTests.cs`: two available engines, upload with no override uses the org default; upload with an explicit `SlicerEngine` + `SlicerVersion` uses that exact pair and leaves the default unchanged (spec US2 Acceptance Scenarios 1–2)
- [ ] T046 [P] [US2] Integration test: uploading with an `(engine, version)` not present in the bundle (stale client) → `422` naming the pair, before any job/processing starts (spec US2 Acceptance Scenario 3, FR-006)
- [ ] T047 [P] [US2] Integration test: change the org default, then re-fetch an already-processed part → it still shows its original recorded engine+version (spec US2 Acceptance Scenario 4, FR-008)
- [ ] T048 [P] [US2] Vitest + RTL test for `SlicingDefaultsPage` in `web/src/features/settings/SlicingDefaultsPage.test.tsx` — renders the available list, submits a default, shows the "no slicer available" empty state
- [ ] T049 [P] [US2] Vitest + RTL test extending `web/src/features/parts/components/AddOrReplacePartDialog.test.tsx` (create if absent) — the optional engine+version override defaults to the org default and is only shown for requires-slicing files

### Implementation for User Story 2

- [ ] T050 [US2] Update `UpdateSlicingDefaultsCommand`/`UpdateSlicingDefaultsCommandHandler` (`api/src/OpenFarm.Application/Parts/UpdateSlicingDefaultsCommand.cs` + handler) to accept a `SlicerRef`, validate via `ResolveSlicerSelection` (reject unavailable pairs, FR-007), and call `Organization.SetDefaultSlicer` (depends on T013, T014)
- [ ] T051 [US2] Update `UpdateSlicingDefaultsEndpoint` (`api/src/OpenFarm.Api/Endpoints/Settings/UpdateSlicingDefaultsEndpoint.cs`) request/response DTOs to `{ engine, version }` in / `{ default, available[] }` out, `422` on `SlicerUnavailableException` (contracts/slicer-management-api.md PUT, depends on T050, T031)
- [ ] T052 [US2] Confirm/extend the activity-log post-processor coverage so `UpdateSlicingDefaultsCommand` success writes a `SlicingDefaultsUpdated` entry against the `Organization` entity (FR-019) — verify the existing FastEndpoints post-processor picks this command up without new wiring; add it explicitly if not
- [ ] T053 [US2] Add `SlicerVersion : string?` alongside the existing `SlicerEngine` override on `AddPartCommand` (`api/src/OpenFarm.Application/Parts/AddPartCommand.cs`) and `ReplacePartFileCommand` (`ReplacePartFileCommand.cs`); update `AddPartCommandHandler`/`ReplacePartFileCommandHandler` to build a `SlicerRef?` from the pair and pass it through `ResolveSlicerSelection` (engine-only → newest version of that engine; both present → exact pair; contracts/slicer-management-api.md, depends on T013)
- [ ] T054 [P] [US2] Update `AddPartCommandValidator` (`api/src/OpenFarm.Application/Parts/AddPartCommandValidator.cs`) and the equivalent `ReplacePartFileCommand` validator for the new `SlicerVersion` field (empty-string / present-without-context rules)
- [ ] T055 [US2] Add the `SlicerVersion` multipart form field to `CreatePartEndpoint` (`api/src/OpenFarm.Api/Endpoints/Parts/CreatePartEndpoint.cs`) and `ReplacePartFileEndpoint` (`api/src/OpenFarm.Api/Endpoints/Parts/ReplacePartFileEndpoint.cs`), mapping `422 SlicerUnavailableException` before processing begins (contracts/slicer-management-api.md, depends on T053, T031)
- [ ] T056 [US2] Regenerate the Orval-generated API client (`cd web && bun run <orval script>` per `web/package.json`) into `web/src/lib/api/generated` to pick up the new request/response shapes — do not hand-edit generated files (depends on T037, T051, T055)
- [ ] T057 [US2] Create `web/src/features/settings/SlicingDefaultsPage.tsx` — lists available `(engine, version, platform)` slicers via the generated `useGetSlicingDefaults` hook, lets an admin submit a default via `useUpdateSlicingDefaults`, react-hook-form + zod + shadcn/ui, gated on `settings:slicing-defaults:update` (research §10, depends on T056)
- [ ] T058 [US2] Add a route for `SlicingDefaultsPage` and a "Slicing Defaults" card to `web/src/features/settings/SettingsPage.tsx`, mirroring the existing Connection Secrets card pattern (depends on T057)
- [ ] T059 [US2] Add an optional engine+version override control to `web/src/features/parts/components/AddOrReplacePartDialog.tsx` — defaults to the org default from `useGetSlicingDefaults`, only shown for files that require slicing (research §10, depends on T056)

**Checkpoint**: Multiple engines/versions coexist; the admin sets a default; per-part override works and is recorded; changing the default never retroactively rewrites processed parts. User Stories 1 and 2 together deliver the full "multi-engine, multi-version" P1 scope.

---

## Phase 5: User Story 3 - Provision bundled slicers from a manifest at dev/build time (Priority: P2)

**Goal**: A developer/release process declares desired `(engine, version, platforms[])` entries in `slicers.manifest.json` and runs `tools/provision-slicers` to fetch, verify, and place each build into the bundle layout Phase 2's scanner already understands — idempotently, per-entry-fault-tolerant, and without the running app ever contacting GitHub.

**Independent Test**: On a supported platform, list two entries of different engines in the manifest, run the provisioning script, and confirm both are downloaded from their official sources into the bundled location and become discoverable/usable by the app without hand-typing paths — with the app itself making no network calls.

### Project setup for User Story 3

- [ ] T060 [US3] Create the `tools/provision-slicers` console project: `tools/provision-slicers/ProvisionSlicers.csproj` (`Sdk="Microsoft.NET.Sdk"`, `<OutputType>Exe</OutputType>`, `<TargetFramework>net10.0</TargetFramework>`, matching `api/global.json`'s pinned SDK) — first plain-console project in the repo, kept out of `api/OpenFarm.slnx`'s runtime dependency graph but added to it under a new `/tools/` solution folder so `dotnet build`/`dotnet test` still cover it (plan.md "Structure Decision")
- [ ] T061 [P] [US3] Create `tools/provision-slicers.Tests/ProvisionSlicers.Tests.csproj` (xUnit + FluentAssertions, mirroring the `api/tests` project shape) and add it to `api/OpenFarm.slnx` under the same `/tools/` folder (depends on T060)

### Tests for User Story 3 (TDD — write first)

- [ ] T062 [P] [US3] Write failing tests for manifest parsing/validation against `contracts/slicer-manifest.schema.json` in `tools/provision-slicers.Tests/ManifestParserTests.cs` — valid manifest, missing required fields, unknown engine value, empty platforms array
- [ ] T063 [P] [US3] Write failing tests for per-engine asset resolution in `tools/provision-slicers.Tests/EngineReleaseSourcesTests.cs` — given `(engine, version, platform)`, resolves the expected GitHub repo (`SoftFever/OrcaSlicer` | `bambulab/BambuStudio` | `supermerill/SuperSlicer`) and asset-name pattern; unresolvable combination → a typed per-entry failure, not a thrown/aborting exception
- [ ] T064 [P] [US3] Write failing tests for idempotency in `tools/provision-slicers.Tests/ProvisioningIdempotencyTests.cs` — an entry already present with a matching `slicers.lock.json` digest is skipped and reported "already present"; re-running twice produces identical results
- [ ] T065 [P] [US3] Write failing tests for integrity verification in `tools/provision-slicers.Tests/IntegrityVerificationTests.cs` — SHA-256 match/mismatch against a lockfile entry; first-fetch-of-a-new-entry records the computed digest

### Implementation for User Story 3

- [ ] T066 [P] [US3] Implement the manifest model + parser/validator (`tools/provision-slicers/ManifestParser.cs`, `Manifest.cs`) reading `slicers.manifest.json` against `contracts/slicer-manifest.schema.json` — to make T062 pass (depends on T062)
- [ ] T067 [P] [US3] Implement `EngineReleaseSources.cs` in `tools/provision-slicers/EngineReleaseSources.cs` — per-engine GitHub repo + asset-name resolution per platform, using `System.Net.Http` against the GitHub REST releases API — to make T063 pass (depends on T063)
- [ ] T068 [US3] Implement the lockfile reader/writer (`tools/provision-slicers/Lockfile.cs`) for `slicers.lock.json` — resolved asset URL + SHA-256 per `(engine, version, platform)` entry — to make T064/T065 pass (depends on T064, T065)
- [ ] T069 [US3] Implement the download → SHA-256 verify (`System.Security.Cryptography`) → extract pipeline (`tools/provision-slicers/Provisioner.cs`) placing each entry at `<out>/<engineSlug>/<version>/<platformSlug>/…`, matching `BundledSlicerScanner`'s convention exactly (research §1/§9, depends on T066, T067, T068)
- [ ] T070 [US3] Implement `Program.cs` CLI entry (`tools/provision-slicers/Program.cs`) — `--manifest <path> --out <dir> --platform <Linux|Windows|MacOs>` args, runs the pipeline per manifest entry, reports per-entry success/failure without aborting on one failure, non-zero exit if any required entry failed (FR-015/FR-017, depends on T069)
- [ ] T071 [P] [US3] Create the example `slicers.manifest.json` at the repo root declaring at least one OrcaSlicer and one SuperSlicer entry, validating against `contracts/slicer-manifest.schema.json` (quickstart.md Setup step 1)
- [ ] T072 [US3] Replace the hard-coded OrcaSlicer install section (`ARG ORCASLICER_VERSION`, `curl`/`appimage-extract`/symlink to `/usr/local/bin/orca-slicer-cli`, lines ~5–20) in `infra/docker/api.Dockerfile` with a build step that runs `dotnet run --project tools/provision-slicers -- --manifest slicers.manifest.json --out <bundle path> --platform Linux`, and set `SlicerBundle__RootDirectory` in the image to that bundle path (research §9, depends on T070, T071)

**Checkpoint**: `dotnet run --project tools/provision-slicers` populates a bundle from the manifest, idempotently, on any platform; the Dockerfile builds a bundled image with no hard-coded single version; the app itself never calls GitHub. All three user stories are independently functional.

---

## Phase 6: Polish & Cross-Cutting Concerns

**Purpose**: Validation and cleanup that spans all three stories.

- [ ] T073 [P] Run `dotnet format api/OpenFarm.slnx --verify-no-changes` and `cd web && bun run lint` / `bunx tsc -b`, fixing any violations introduced by this feature
- [ ] T074 [P] Update `slicers.manifest.json`/`slicers.lock.json` references and the `SlicerBundle:RootDirectory` local-dev instructions in `.env.example` if not already covered by T001/T071
- [ ] T075 Walk through `quickstart.md` end-to-end against a local `docker compose up` stack (provision → discover → slice with default → slice with override → change default → re-provision idempotently) and fix any gaps found
- [ ] T076 Confirm SC-001 through SC-007 are each demonstrably satisfied by the test suite added in Phases 3–5 (traceability pass — no new code expected, only gap-filling if a success criterion lacks coverage)

---

## Dependencies & Execution Order

### Phase Dependencies

- **Setup (Phase 1)**: No dependencies — can start immediately
- **Foundational (Phase 2)**: Depends on Phase 1 (T001's config keys) — **BLOCKS Phase 3 and Phase 4** (both need the catalog, `ResolveSlicerSelection`, the migration, and `Organization.SetDefaultSlicer`)
- **User Story 1 (Phase 3)**: Depends on Phase 2 only
- **User Story 2 (Phase 4)**: Depends on Phase 2; independent of Phase 3's tasks but naturally implemented after it (extends the same `AddPartCommand`/`GetSlicingDefaultsEndpoint` surfaces US1 touches first) — T043–T047/T050–T055 can start as soon as Phase 2 is done
- **User Story 3 (Phase 5)**: Depends on Phase 2 only for the bundle-directory *convention* (T019's `<engineSlug>/<version>/<platformSlug>` layout, which the provisioning tool must match) — otherwise fully independent; can be built in parallel with Phases 3–4 by a different contributor
- **Polish (Phase 6)**: Depends on Phases 3, 4, and 5 all being complete

### Within Foundational (Phase 2)

T002–T006 (value types) → T007/T008 (comparer, TDD) → T009–T011 (contracts) → T012/T013 (`ResolveSlicerSelection`, TDD, needs T008+T009) → T014/T015 (Organization) → T016–T026 (infra: scanner/catalog/invocation, needs T005/T009/T010) → T027–T029 (migration, needs T014/T004/T028) → T030/T031 (DI + exception mapping, needs almost everything above)

### Parallel Opportunities

- All `[P]` tasks within Phase 2's "Domain value types" block (T002–T006) run in parallel — different files
- T018/T021 (infra test-writing) run in parallel with each other and with T007/T012 (different projects)
- T022/T023 (the two invocation strategies) run in parallel once T021 exists
- Once Phase 2 completes: Phase 3, Phase 4, and Phase 5 can proceed in parallel across different contributors (Phase 4 has a soft ordering preference after Phase 3 touches the same files, but no hard dependency)
- Within Phase 4: T043–T049 (all test-writing) run in parallel; T054 runs in parallel with T053's command changes only if scoped to the validator file alone (otherwise sequence after T053)
- Within Phase 5: T062–T065 (test-writing) run in parallel; T066/T067 run in parallel once their tests exist

---

## Parallel Example: Phase 2 Foundational, value types

```bash
Task: "Add SuperSlicer to SlicerEngine enum in api/src/OpenFarm.Domain/Parts/SlicerEngine.cs"
Task: "Create SlicerPlatform enum in api/src/OpenFarm.Domain/Parts/SlicerPlatform.cs"
Task: "Create SlicerRef value object in api/src/OpenFarm.Domain/Parts/SlicerRef.cs"
Task: "Create AvailableSlicer value object in api/src/OpenFarm.Domain/Parts/AvailableSlicer.cs"
Task: "Create SlicerUnavailableException in api/src/OpenFarm.Domain/Parts/SlicerUnavailableException.cs"
```

## Parallel Example: Phase 4 User Story 2, test-writing

```bash
Task: "Integration test: valid default update in api/tests/OpenFarm.Api.IntegrationTests/Settings/UpdateSlicingDefaultsEndpointTests.cs"
Task: "Integration test: 422 unavailable-pair case (same file)"
Task: "Integration test: default + explicit override in api/tests/OpenFarm.Api.IntegrationTests/Parts/CreatePartEndpointTests.cs"
Task: "Integration test: stale-pair 422 before processing"
Task: "Integration test: default change doesn't retroactively rewrite processed parts"
Task: "Vitest test for SlicingDefaultsPage in web/src/features/settings/SlicingDefaultsPage.test.tsx"
Task: "Vitest test for AddOrReplacePartDialog override in web/src/features/parts/components/AddOrReplacePartDialog.test.tsx"
```

---

## Implementation Strategy

### MVP First (User Story 1 Only)

1. Complete Phase 1: Setup
2. Complete Phase 2: Foundational (critical — blocks everything)
3. Complete Phase 3: User Story 1
4. **STOP and VALIDATE**: run quickstart.md's Story 1 section against a bundle with one engine+version
5. Deploy/demo — the Part Library is functional again, just with a single discovered slicer

### Incremental Delivery

1. Setup + Foundational → catalog/invocation/migration ready
2. Add User Story 1 → validate → deploy (MVP: slicing works again, reproducibly)
3. Add User Story 2 → validate → deploy (multi-engine choice, admin default, per-part override)
4. Add User Story 3 → validate → deploy (repeatable provisioning; no more hand-placed binaries)
5. Polish pass → full quickstart.md walkthrough, SC-001–SC-007 traceability

### Parallel Team Strategy

1. Team completes Setup + Foundational together (Phase 2 is the one genuinely shared bottleneck)
2. Once Foundational is done:
   - Developer A: User Story 1 (Phase 3)
   - Developer B: User Story 2 (Phase 4) — mind the file overlap with Phase 3 on `AddPartCommand`/`CreatePartEndpoint`/`GetSlicingDefaultsEndpoint`; sequence commits or coordinate
   - Developer C: User Story 3 (Phase 5) — fully independent, touches only `tools/` and the Dockerfile
3. Stories complete and integrate independently; Polish once all three land

---

## Notes

- [P] tasks touch different files with no dependency on an incomplete task
- [Story] labels map every Phase 3+ task to US1/US2/US3 for traceability back to spec.md
- Phase 3 and Phase 4 both touch `AddPartCommand`/`AddPartCommandHandler`, `CreatePartEndpoint`, `ReplacePartFileEndpoint`, and `GetSlicingDefaultsEndpoint` — implement Phase 3's version first (single-engine default path), then Phase 4 extends the same files (engine-only/full-pair override, PUT endpoint) rather than conflicting rewrites
- Verify each TDD-flagged test (T007, T012, T018, T021, T062–T065) actually fails before writing its implementation
- Commit after each task or logical group
- Stop at either P1 checkpoint (end of Phase 3, end of Phase 4) to validate independently before continuing
