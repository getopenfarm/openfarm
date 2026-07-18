---

description: "Task list for printer fleet management refinements"
---

# Tasks: Printer Fleet Management Refinements

**Input**: Design documents from `/specs/005-printer-fleet-refinements/`

**Prerequisites**: plan.md, spec.md, research.md, data-model.md, contracts/printers-api.md, quickstart.md

**Tests**: Not explicitly requested for orchestration-only handler/endpoint work (test-after, per CLAUDE.md's testing strategy), but plan.md's Constitution Check calls out one deliberate TDD carve-out — the `Printer` domain rules (`Rename`, brand/model/serial immutability) — so those are written test-first below. Endpoint/handler and frontend tests are test-after tasks alongside their implementation.

**Organization**: Tasks are grouped by user story (US1–US4, priority order from spec.md) to enable independent implementation and testing of each story.

## Format: `[ID] [P?] [Story] Description`

- **[P]**: Can run in parallel (different files, no dependencies)
- **[Story]**: Which user story this task belongs to (US1, US2, US3, US4)
- Every task includes an exact file path

## Path Conventions

Existing monorepo split (per plan.md): `api/src/OpenFarm.{Domain,Application,Api}/`, `api/tests/OpenFarm.{Domain,Application,Api.IntegrationTests}/`, `web/src/features/printers/`.

---

## Phase 1: Setup

No setup tasks — this feature adds no new dependency, project, or tool (plan.md Technical Context: "all pre-existing, no new dependency introduced"). Proceed directly to Foundational.

---

## Phase 2: Foundational (Blocking Prerequisites)

**Purpose**: The domain-level primitives every later story's handler depends on — the new `Rename` method, the immutability-mismatch exception, and its `ProblemDetails` mapping. No user story can be implemented until these exist.

**⚠️ CRITICAL**: US1's command handler (Phase 3) calls `Printer.Rename` and throws/catches `ImmutablePrinterFieldMismatchException` — both must exist first.

- [X] T001 [P] Write failing tests for `Printer.Rename(string name)` in `api/tests/OpenFarm.Domain.Tests/Printers/PrinterTests.cs` — non-empty name sets `Name`; empty/whitespace name throws (mirrors `Create`'s existing name rule per data-model.md); status is never checked (renaming allowed regardless of `PrinterStatus`).
- [X] T002 [US: shared] Implement `Rename(string name)` on `api/src/OpenFarm.Domain/Printers/Printer.cs` to make T001 pass.
- [X] T003 [P] Create `api/src/OpenFarm.Domain/Printers/ImmutablePrinterFieldMismatchException.cs` — new domain exception carrying a message naming the mismatched field(s), per data-model.md.
- [X] T004 Map `ImmutablePrinterFieldMismatchException` to `422 Unprocessable Entity` with title "Printer identity fields cannot be changed" in `api/src/OpenFarm.Api/ExceptionHandling/DomainExceptionHandler.cs` (same switch expression as the existing `UnknownPrinterModelException`/`PrinterConnectionVerificationFailedException` entries).
- [X] T005 [P] Add an enumerable accessor (e.g. `All`) over `PrinterModelCatalog`'s existing dictionary, grouped by brand, in `api/src/OpenFarm.Domain/Printers/PrinterModelCatalog.cs` — required by US3's catalog query, added here since it's a pure read-only addition to shared domain code with no story-specific behavior.
- [X] T006 [P] Extend `api/tests/OpenFarm.Domain.Tests/Printers/PrinterModelCatalogTests.cs` to cover the new `All` accessor returns every existing `(Brand, ModelCode)` entry grouped by brand.

**Checkpoint**: Foundation ready — `Printer.Rename`, the mismatch exception (with its 422 mapping), and the catalog accessor all exist and are covered by passing tests. User story implementation can now begin.

---

## Phase 3: User Story 1 - Edit a printer's name alongside its connection details (Priority: P1) 🎯 MVP

**Goal**: Replace the connection-only edit with one `PATCH /api/v1/printers/{id}` covering name + optional connection details, with server-side zero-trust rejection of brand/model/serial mismatches.

**Independent Test**: PATCH a registered printer's name only (no re-verification, per quickstart.md Scenario 1); PATCH its connection details (verify-before-save gate applies, Scenario 2); PATCH with a mismatched `serialNumber` and confirm `422` rejection with no partial mutation (Scenario 3).

### Implementation for User Story 1

- [X] T007 [P] [US1] Replace `api/src/OpenFarm.Application/Printers/EditPrinterConnectionCommand.cs` with `EditPrinterCommand.cs` — fields: `PrinterId`, `Name` (required), `IpAddress`/`AccessCode` (optional pair), `Brand`/`ModelCode`/`SerialNumber` (optional, zero-trust check only per research.md §3).
- [X] T008 [US1] Replace `api/src/OpenFarm.Application/Printers/EditPrinterConnectionCommandValidator.cs` with `EditPrinterCommandValidator.cs` — `Name` non-empty; `IpAddress`/`AccessCode` must both be present or both absent (FluentValidation custom rule, `400` per contracts/printers-api.md); depends on T007.
- [X] T009 [US1] Replace `api/src/OpenFarm.Application/Printers/EditPrinterConnectionCommandHandler.cs` with `EditPrinterCommandHandler.cs`: load printer (404 via existing `PrinterNotFoundException` if missing); if any of `Brand`/`ModelCode`/`SerialNumber` supplied, compare against the printer's current value and throw `ImmutablePrinterFieldMismatchException` on any mismatch before any mutation (FR-010); call `Printer.Rename(name)` always; if `IpAddress`+`AccessCode` present, re-verify via `IPrinterConnectorFactory` (same gate as registration/today's connection edit) then call `Printer.EditConnection(ipAddress)` and persist the new `ConnectionSecret`; implement `IActivityLoggable` with `EventType = "PrinterEdited"` (renamed from `"PrinterConnectionEdited"` per data-model.md); depends on T002, T003, T007, T008.
- [X] T010 [US1] Delete the now-superseded `api/src/OpenFarm.Application/Printers/EditPrinterConnectionCommand.cs`, `EditPrinterConnectionCommandValidator.cs`, `EditPrinterConnectionCommandHandler.cs` if not already removed by the T007–T009 replacements (confirm no other caller references the old names via a repo-wide grep before deleting).
- [X] T011 [US1] Replace `api/src/OpenFarm.Api/Endpoints/Printers/EditPrinterConnectionEndpoint.cs` with `EditPrinterEndpoint.cs` — `PATCH /api/v1/printers/{id}`, `[Permission("printers:edit-connection")]` (unchanged string per spec Assumptions), maps request to `EditPrinterCommand`, returns `200` with `PrinterSummaryResponse`; depends on T009.
- [X] T012 [P] [US1] Write `api/tests/OpenFarm.Application.Tests/Printers/EditPrinterCommandHandlerTests.cs` covering: name-only change skips verification; connection-fields change triggers verify-before-save; verification failure leaves the printer unchanged; a mismatched `brand`/`modelCode`/`serialNumber` throws `ImmutablePrinterFieldMismatchException` before any mutation (FR-010, SC-007); a successful edit produces one `IActivityLoggable` entry with `EventType = "PrinterEdited"`; depends on T009.
- [X] T013 [P] [US1] Write `api/tests/OpenFarm.Api.IntegrationTests/Printers/EditPrinterEndpointTests.cs` covering quickstart.md Scenarios 1–3 end-to-end against `WebApplicationFactory` + `FakePrinterConnector`: `200` name-only (no verify call), `200` connection change with correct credentials (reconnect observed), `422` on verification failure, `400` on a mismatched `ipAddress`/`accessCode` pair (one present, one absent), `422` on a mismatched `serialNumber` with zero activity-log entry beyond the rejection itself; depends on T011.
- [X] T014 [P] [US1] Broaden `web/src/features/printers/components/EditPrinterDialog.tsx`: add a `name` field (react-hook-form + zod, always required); keep the existing `ipAddress`/`accessCode` verify-before-save flow but scope it to fire only when `connectionChanged` is true (`ipAddress !== currentIpAddress || accessCode.length > 0`, per research.md §7); when connection is unchanged, Save is gated only on non-empty name; call the regenerated `EditPrinter` Orval hook with the new `PATCH` request shape; brand/model/serial fields remain read-only display or omitted entirely (spec acceptance scenario 3).
- [X] T015 [US1] Regenerate the Orval API client (`bun run` the project's OpenAPI generation script, per CLAUDE.md "never hand-edit generated files") so `src/lib/api/generated` reflects the new `PATCH /api/v1/printers/{id}` and dropped `PUT .../connection` operation; depends on T011.
- [X] T016 [P] [US1] Update/extend the printer-edit test coverage under `web/src/features/printers/` (e.g. a dedicated `EditPrinterDialog.test.tsx` if one doesn't exist, or extend `PrinterDetailPage.test.tsx`) to cover: name-only save requires no verify call; connection-change save still requires verify; brand/model/serial are never rendered as editable controls; depends on T014.

**Checkpoint**: User Story 1 is fully functional and independently testable — printers can be renamed and/or have their connection edited through one endpoint, with server-side immutability enforcement.

---

## Phase 4: User Story 2 - Manage a printer only from its detail page (Priority: P2)

**Goal**: Remove Edit/Remove controls from the fleet table and grid; the detail page becomes the sole place both actions live, with Remove now fully wired there.

**Independent Test**: Inspect a fleet-list row (table) and card (grid) — confirm no Edit/Remove control on either; open a printer's detail page — confirm both actions are present and functional, and Remove returns the operator to the fleet list (quickstart.md Scenario 4).

### Implementation for User Story 2

- [X] T017 [P] [US2] Remove the Edit/Remove actions (row menu items and any associated dialogs/handlers) from `web/src/features/printers/components/PrintersTable.tsx`, keeping navigation-to-detail and the existing "Mark ready" action untouched (spec Assumptions).
- [X] T018 [P] [US2] Remove the Edit/Remove actions (card overlay items) from `web/src/features/printers/components/PrintersGrid.tsx`, same constraints as T017.
- [X] T019 [US2] Remove `editingPrinterId`/`removeMutation` state and handlers from `web/src/features/printers/PrintersPage.tsx` now that the fleet list no longer triggers either action; depends on T017, T018.
- [X] T020 [US2] Wire a real Remove action into `web/src/features/printers/components/PrinterDetailActions.tsx` — confirmation step, calls the existing (unchanged) `DELETE /api/v1/printers/{id}` mutation, matching today's fleet-list removal behavior exactly (FR-006).
- [X] T021 [US2] In `web/src/features/printers/PrinterDetailPage.tsx`, own the edit-dialog-open state (using the broadened `EditPrinterDialog` from US1) and the remove-confirmation state; on successful removal, navigate back to the fleet list (spec Assumptions, SC-004); depends on T020, and on US1's T014.
- [X] T022 [P] [US2] Update `web/src/features/printers/PrintersPage.test.tsx` to assert no Edit/Remove control renders in table or grid view (SC-003); depends on T017, T018, T019.
- [X] T023 [P] [US2] Update `web/src/features/printers/PrinterDetailPage.test.tsx` to cover the new Remove action's confirmation flow and post-removal navigation to the fleet list; depends on T020, T021.

**Checkpoint**: User Stories 1 AND 2 both work independently — the fleet list is action-free except navigation/Mark-ready, and the detail page fully owns Edit + Remove.

---

## Phase 5: User Story 3 - Populate the Add-printer catalog from the backend (Priority: P3)

**Goal**: Expose `GET /api/v1/printers/catalog` and have `AddPrinterDialog.tsx` consume it instead of a hardcoded frontend list.

**Independent Test**: Fetch `/api/v1/printers/catalog` and confirm it matches `PrinterModelCatalog`'s contents; open the Add-printer dialog and confirm its Brand/Model options match; simulate a fetch failure and confirm a clear error state with submission disabled (quickstart.md Scenario 5).

### Implementation for User Story 3

- [X] T024 [P] [US3] Create `api/src/OpenFarm.Application/Printers/GetPrinterCatalogQuery.cs` (+ handler) — reads `PrinterModelCatalog.All` (T005) and projects into the `{ brands: [{ brand, models[] }] }` shape from data-model.md/contracts/printers-api.md.
- [X] T025 [US3] Create `api/src/OpenFarm.Api/Endpoints/Printers/GetPrinterCatalogEndpoint.cs` — `GET /api/v1/printers/catalog`, `[Permission("printers:register")]` (reused, no new permission per spec Assumptions), no request body, `200` response only; depends on T024.
- [X] T026 [P] [US3] Write `api/tests/OpenFarm.Api.IntegrationTests/Printers/GetPrinterCatalogEndpointTests.cs` — `200` with a body matching `PrinterModelCatalog`'s current contents (FR-008); depends on T025.
- [X] T027 [US3] Regenerate the Orval API client to add the `GetPrinterCatalog` hook for the new endpoint (same regeneration step as T015); depends on T025.
- [X] T028 [US3] Rework `web/src/features/printers/components/AddPrinterDialog.tsx`: drop the hardcoded `PRINTER_BRANDS`/`MODELS_BY_BRAND` constants; fetch the catalog via the new TanStack Query hook; build Brand/Model `<select>` options from the response; replace the static `z.enum([...])` validation with a runtime check against the fetched list (`z.string().min(1)` + `.refine()`, per research.md §5); disable the selects while loading; on fetch error, disable submission and surface a clear error state (US3 AC3) rather than an empty/stale list; depends on T027.
- [X] T029 [P] [US3] Add/extend tests for `AddPrinterDialog.tsx` (new or existing test file under `web/src/features/printers/components/`) covering: options populate from a mocked catalog response; loading state disables selects; fetch-error state disables submission; depends on T028.
- [X] T030 [US3] Grep `web/src/features/printers` for `PRINTER_BRANDS`/`MODELS_BY_BRAND` and confirm zero remaining references (quickstart.md Scenario 5's explicit verification step); depends on T028.

**Checkpoint**: All three user-facing stories (US1–US3) are independently functional. Adding a brand/model to the backend catalog now requires zero frontend code changes.

---

## Phase 6: User Story 4 - Confirm the real-time event contract stays brand-neutral (Priority: P4)

**Goal**: Verify the SignalR printer-event contract and its frontend consumer have no brand-specific assumption; research.md §6 already performed this audit and found none — this phase re-confirms it holds against the code as actually implemented after US1–US3 land, and closes out the story with a documented outcome.

**Independent Test**: Review every field/event name in `FarmHub.cs`, `FarmHubPrinterEventPublisher.cs`, and `useFarmHub.ts`; confirm each is canonical (not brand-keyed).

### Implementation for User Story 4

- [X] T031 [US4] Re-review `api/src/OpenFarm.Api/Hubs/FarmHub.cs` and its event publisher (`FarmHubPrinterEventPublisher.cs`) against the post-US1–US3 codebase: confirm `PrinterStatusChanged`, `PrinterTelemetryUpdated`, and `CameraFrameReceived` still carry only canonical fields (`status`, `isReachable`, `nozzleTempC`, `bedTempC`, `printProgressPercent`, `currentStageLabel`, `frameJpegBase64`) with no brand/model-keyed field or branch (research.md §6 finding).
- [X] T032 [P] [US4] Re-review `web/src/features/printers/hooks/useFarmHub.ts` for any branch keyed on `brand`/`modelCode` — confirm none exists.
- [X] T033 [US4] Record the audit outcome (expected: zero code changes required, per research.md §6) as the closing confirmation for US4 — if a brand-specific assumption is found during T031/T032 that wasn't caught in research.md, generalize it to the canonical model in the same file(s) without introducing a brand-specific branch to replace it (FR-009's fallback path); otherwise no code change is made.

**Checkpoint**: All four user stories complete. Per research.md §6's expected outcome, this phase should conclude with zero hub/publisher/frontend-subscription code changes.

---

## Phase 7: Polish & Cross-Cutting Concerns

**Purpose**: Final validation across all stories.

- [X] T034 Run `dotnet format api/OpenFarm.slnx --verify-no-changes` and `cd web && bun run lint` — fix any violations introduced by T001–T033.
- [X] T035 Run the full automated verification from quickstart.md: `dotnet test api/OpenFarm.slnx --filter "FullyQualifiedName~Printer"` and `cd web && bunx vitest run src/features/printers && bunx tsc -b`.
- [X] T036 Walk through quickstart.md Scenarios 1–6 manually against `docker compose up` (infra/.env) to confirm end-to-end behavior matches every acceptance scenario in spec.md.

---

## Dependencies & Execution Order

### Phase Dependencies

- **Foundational (Phase 2)**: No dependencies — start immediately. BLOCKS all user stories (US1's handler needs `Rename`/the mismatch exception; US3's query needs the catalog accessor).
- **User Story 1 (Phase 3)**: Depends on Foundational only.
- **User Story 2 (Phase 4)**: Depends on Foundational; its detail-page wiring (T021) additionally depends on US1's broadened `EditPrinterDialog` (T014) being in place, per plan.md's "Story 2... depends on User Story 1 only in that the detail page's edit action should already support the broadened field set."
- **User Story 3 (Phase 5)**: Depends on Foundational only — fully independent of US1/US2 (different endpoint, different dialog).
- **User Story 4 (Phase 6)**: Depends on Foundational; conceptually re-audits the codebase after US1–US3, so sequenced last, though its review scope (SignalR hub/frontend hook) doesn't structurally depend on any of them.
- **Polish (Phase 7)**: Depends on all four stories being complete.

### Parallel Opportunities

- Within Foundational: T001/T003/T005 are independent files ([P]); T006 depends on T005.
- Within US1: T007 stands alone; T012/T013/T014 can run in parallel once their respective dependencies (T009, T011, US1 dialog scope) land.
- US1 and US3 can be implemented in parallel by different developers (no shared files) once Foundational is done.
- US2 must wait for US1's T014 before its final wiring task (T021), but T017/T018 (removing actions from table/grid) can start immediately after Foundational.
- US4 (T031/T032) can run in parallel with any other story, though it's most meaningful as a final re-check.

---

## Implementation Strategy

### MVP First (User Story 1 Only)

1. Complete Phase 2: Foundational.
2. Complete Phase 3: User Story 1.
3. **STOP and VALIDATE**: Run quickstart.md Scenarios 1–3 against a real printer edit flow.
4. Deploy/demo if ready — renaming and unified connection editing is the single most concrete user-facing gap this feature closes.

### Incremental Delivery

1. Foundational → foundation ready.
2. US1 → validate independently → deploy/demo (MVP).
3. US2 → validate independently (depends on US1's broadened dialog for its detail-page wiring) → deploy/demo.
4. US3 → validate independently (fully parallel-capable with US1/US2) → deploy/demo.
5. US4 → audit re-confirmation, expected zero-diff → close out feature.

### Parallel Team Strategy

With multiple developers, once Foundational is done:
- Developer A: User Story 1 (backend PATCH endpoint + broadened dialog).
- Developer B: User Story 3 (catalog endpoint + Add-printer dialog) — no dependency on A.
- Developer C: User Story 2's table/grid removal (T017/T018) — can start immediately; the final detail-page wiring (T021) waits on Developer A's T014.
- User Story 4 (audit) can be picked up by anyone once the hub/publisher files are stable, ideally last.
