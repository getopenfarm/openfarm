# Implementation Plan: Profiles (Machine + Filament, Imported from Slicer Bundles)

**Branch**: `003-slicer-profile-import` | **Date**: 2026-07-16 | **Spec**: [spec.md](./spec.md)

**Input**: Feature specification from `/specs/003-slicer-profile-import/spec.md`

**Note**: This template is filled in by the `/speckit-plan` command. See `.specify/templates/plan-template.md` for the execution workflow.

## Summary

Add two standalone, import-only catalogs — machine profiles and filament profiles — populated by
uploading an OrcaSlicer or Bambu Studio slicer-bundle archive (`.zip`, `.orca_printer`,
`.orca_filament`, `.bbscfg`, `.bbsflmt`). The backend parses whichever machine/filament presets it
recognizes out of the zip-format archive and returns them in a stateless preview response; the operator
selects which ones to keep in a review list (client-side, no server-side staging entity); a second,
separate confirm call re-uploads the same file and re-parses it server-side, persisting only the
presets the operator selected (identified by a compact key, never by re-trusting client-supplied preset
content) and resolving each preset's declared base/override relationship into a self-referencing FK
where the base is resolvable, preserving the base's name as an unresolved reference otherwise. Imported
profiles are viewable in two separate catalog lists and editable only through a raw JSON viewer/editor
— no structured per-field forms, no printer/job linkage, consistent with this phase's deliberately
narrow scope per `docs/FEATURE_PLAN.md` Phase 2.

## Technical Context

**Language/Version**: C# 13 / .NET 10 (`api/`), TypeScript 5 / React 19 (`web/`) — unchanged from
Phase 0/1.

**Primary Dependencies**: No new NuGet packages — bundle parsing uses the .NET BCL's
`System.IO.Compression.ZipArchive` and `System.Text.Json` (`research.md` §2), since the supported
formats are all zip-format archives of JSON files, not a binary protocol needing a dedicated client
library the way Phase 1's MQTT integration needed MQTTnet. No new frontend dependencies — file upload
uses a plain HTML file input + the existing Orval-generated `multipart/form-data` client pattern,
review-list state is local React state (not TanStack Query, since it's never server-persisted until
confirm — `research.md` §4), and the catalog tables reuse the existing `DataTable`/`Tabs` primitives
already in `web/src/components/`.

**Storage**: Existing Postgres via `OpenFarmDbContext`. Two new, deliberately narrow tables,
`MachineProfiles` and `FilamentProfiles` (org id, name, source format, self-referencing
`BaseProfileId` + durable `BaseProfileName`, `RawDataJson` jsonb, created-at) — see `data-model.md`.
Every descriptive/spec field spec.md's Key Entities names (nozzle size, bed volume, enclosure,
supported materials, material, brand, color, tuned settings) is **not** a stored column — it's computed
at read time from `RawDataJson` by the same field-extraction logic the parser uses, so there's exactly
one source of truth and a raw-JSON edit (FR-012) can never leave a cached column stale (`data-model.md`'s
"Derived display fields"). One field Key Entities also names — machine-profile live-integration
capability — is cut entirely for this phase rather than derived (`research.md` §8: no FR/SC backs it,
and its source field is unverified). No changes to any Phase 0/1 table. No "import batch" table — the
review step is a stateless round-trip (`research.md` §4), a deliberate rejection of a staging entity per
the constitution's anti-speculation principle; confirm re-uploads and re-parses the source file rather
than persisting a client-echoed copy of it, so the transient confirm-request shape carries only
selection keys, not duplicated preset data (`data-model.md`'s `SelectedProfileKey`).

**Testing**: TDD (xUnit + FluentAssertions) for the base/override two-pass resolution algorithm and
duplicate-name detection (`OpenFarm.Application.Tests` — pure, deterministic, well-specified logic, the
same bar the constitution sets for BOM decomposition and filament weight math). Fixture-replay,
test-after for `ProfileBundleParser` (`OpenFarm.Infrastructure.Tests`) against real captured export
files, mirroring the exact precedent `BambuMqttMessageParser` set in Phase 1 for externally-defined
formats (`research.md` §2) — real fixtures are a follow-up implementation task (`research.md` §1),
not invented from documentation alone. `WebApplicationFactory` + Testcontainers.PostgreSql for API
integration tests (preview → confirm → catalog-list round trip). Vitest + RTL for
`web/src/features/profiles`, replacing the existing mock-data prototype's tests (see Project Structure).

**Target Platform**: Linux containers (Docker Compose), ASP.NET Core host, Vite-built SPA — unchanged.

**Project Type**: Web application — existing `api/` + `web/` monorepo, extended in place.

**Performance Goals**: SC-001 upload-to-catalog under 2 minutes for a bundle under 50 combined presets
— a pure in-memory zip-parse plus one DB round-trip for duplicate lookups (preview) and one transactional
insert batch (confirm), well within that budget without any special-casing. SC-006 ≤3 interactions from
catalog list to a profile's raw data — a direct list-row click into a detail view satisfies this
trivially (1 interaction), leaving headroom.

**Constraints**: Bundles are capped at a conservative upload size (`MaxRequestBodySize`, `research.md`
§6) generous for any realistic slicer export while bounding worst-case memory from an oversized or
malicious upload. The review list must "stay usable (searchable/scrollable)" for "very large bundles
(many dozens of presets)" per spec's Edge Cases — handled by the existing `DataTable` component's
built-in filtering, not a new virtualization requirement (dozens of rows, not the unbounded job-queue
scale the constitution's Principle V calls out for `TanStack Virtual`).

**Scale/Scope**: Single self-hosted organization, import-only (no printer/job linkage this phase, per
spec's FR-015 and Assumptions).

## Constitution Check

*GATE: Must pass before Phase 0 research. Re-check after Phase 1 design.*

- **I. Clean Architecture**: `IProfileBundleParser` is an `Application`-defined contract
  (`Application/Profiles/IProfileBundleParser.cs`), implemented by `ProfileBundleParser` in
  `Infrastructure/Profiles/` — `Domain`/`Application` never reference `System.IO.Compression` directly,
  same boundary shape as `IPrinterConnector`. `Api` endpoints stay thin FastEndpoints adapters (REPR)
  over `Application` commands/queries — the preview/confirm endpoints translate `IFormFile`/JSON
  request bodies into `Application` calls and nothing more. **Pass.**
- **II. Test Discipline**: base/override resolution and duplicate detection get TDD (deterministic,
  well-specified, no external-format uncertainty — `research.md` §3/§5). `ProfileBundleParser` is
  test-after/fixture-replay, explicitly *not* TDD, following the `BambuMqttMessageParser` precedent for
  "correctness against what a real external tool actually produces" rather than an assumed schema
  (`research.md` §2). Application command handlers (`ConfirmProfileImportCommandHandler`'s
  orchestration, the query handlers) and React components are test-after, per the constitution's
  existing carve-outs — nothing here calls for TDD beyond the two named exceptions. **Pass.**
- **III. Consistent UX**: `Profiles` has no real-time/SignalR dimension (no live device to push updates
  from — spec's Assumptions), so Principle III's SignalR-cache requirement doesn't apply; there is
  correspondingly no risk of a competing polling path to violate it. Forms use react-hook-form + zod
  (the raw-JSON-edit form, the import-preview file-select step) and primitives come from the existing
  `src/components/ui/` kit (`DataTable`, `Tabs`, dialogs) — no feature-invented UI pattern. All errors
  surface as `ProblemDetails`, same single shape the frontend already renders everywhere. **Pass.**
- **IV. Build for Expansion, Not Speculation**: `organization_id` present on both new tables from the
  first migration. The rejected `PendingImportBatch` staging-entity alternative (`research.md` §4) is
  this principle applied to a design choice, not just an architectural layer: no speculative persistence
  for a review step nothing else in this phase needs. The same reasoning shaped the schema itself
  (`data-model.md`'s "Derived display fields"): no typed column is added for nozzle size, bed volume,
  material, color, etc. without a stated query/filter/sort requirement driving it — today every FR only
  asks for these to be *displayed*, so they're computed from `RawDataJson` on read rather than
  speculatively promoted to indexed columns nothing yet needs. It also drove cutting
  `SupportsLiveIntegration` entirely rather than building a fuzzy match against Phase 1's
  `PrinterModelCatalog` (`research.md` §8): reusing that catalog would have been a legitimate instance
  of this principle, but only once a real requirement and a verified source field justify it — building
  the matching logic now, against an unconfirmed preset field and with no FR/SC behind it, would have
  been the speculative direction this principle warns against, not the expansion-friendly one. Structured
  per-field forms are explicitly deferred (FR-014) rather than half-built. **Pass.**
- **V. Performance and Real-Time Responsiveness**: No polling anywhere in this feature (nothing to
  poll — import is a synchronous request/response, catalogs are plain TanStack Query fetches). Review
  list uses the existing non-virtualized `DataTable` — correctly scoped as a non-violation per the
  constitution's own carve-out (only the *unbounded* job queue requires `TanStack Virtual`; "many dozens"
  of presets is a bounded, small list). **Pass.**
- **Security & Multi-Tenancy Isolation**: Both new tables use Finbuckle's `IsMultiTenant()` global query
  filter exclusively — no hand-rolled tenant `WHERE` clause (`data-model.md`). No secrets are involved
  in this feature (profiles carry no credentials), so `IDataProtector` is out of scope entirely. Every
  profile creation is captured by the activity log — the one real design decision this feature adds is
  the `IHasEntityIds` extension to `ActivityLogPostProcessor` (`research.md` §7) so a single
  bulk-confirm HTTP call still logs one entry per created profile structurally, rather than the handler
  reaching for `IActivityLogWriter` directly and reopening the exact gap the constitution's Security
  section warns against. Auth is the existing real JWT flow — no new endpoint skips it. A second,
  smaller integrity point sharpened during design: `confirm` persists only data the server itself
  parsed from a re-uploaded file, never a client-echoed copy of preset content (`research.md` §4's
  revision note) — what's written to the database always traces back to something the server's own
  `IProfileBundleParser` actually produced, not an unverified claim in the request body. **Pass, with
  one explicit, justified post-processor extension** (see Complexity Tracking).

No Constitution Check violations. The `IHasEntityIds` post-processor extension and the
`PendingImportBatch`-rejection design decision are both recorded in Complexity Tracking below for
visibility, even though neither is a violation — they're the two points in this feature where the
"obvious" implementation (bypass the post-processor; add a staging table) was rejected in favor of the
one that holds up the existing structural guarantees.

**Post-Phase-1-design re-check** (per `research.md`/`data-model.md`/`contracts/`): confirms all of the
above with no new violations. One item sharpened during design: `BaseProfileId`'s `ON DELETE SET NULL`
foreign-key behavior is specified in `data-model.md` even though this feature ships no delete endpoint
(FR-001–015 name no deletion capability) — recorded there as a defensive default for whenever deletion
is added later, not scope creep now (no delete endpoint, UI, or command is being built in this PR).

## Project Structure

### Documentation (this feature)

```text
specs/003-slicer-profile-import/
├── plan.md              # This file (/speckit-plan command output)
├── research.md          # Phase 0 output
├── data-model.md         # Phase 1 output
├── quickstart.md         # Phase 1 output
├── contracts/            # Phase 1 output
│   └── profiles-api.md
└── tasks.md              # Phase 2 output (/speckit-tasks command - NOT created by /speckit-plan)
```

### Source Code (repository root)

```text
api/src/OpenFarm.Domain/Profiles/
  MachineProfile.cs                   # entity — see data-model.md
  FilamentProfile.cs                  # entity — see data-model.md
  ProfileSourceFormat.cs              # enum: OrcaSlicer, BambuStudio

api/src/OpenFarm.Application/Profiles/
  IProfileBundleParser.cs             # Application-defined contract; Infrastructure implements.
                                       # ParseAsync(stream) -> full bundle, for preview.
                                       # ExtractMachineDisplayFields(rawDataJson, sourceFormat) / ExtractFilamentDisplayFields(rawDataJson, sourceFormat)
                                       # -> the same per-format field-mapping logic, reused by the Get*Query handlers
                                       # below to compute data-model.md's "Derived display fields" from a stored row
                                       # instead of a fresh archive — one extraction implementation, two call sites.
                                       # sourceFormat is required: SourceFormat isn't derivable from RawDataJson alone
                                       # (data-model.md) — the per-SourceFormat field-mapping table needs it explicitly
                                       # when called against a stored row, where (unlike a fresh archive entry) the
                                       # parser doesn't already know which format branch it's in.
  IMachineProfileRepository.cs / IFilamentProfileRepository.cs
  PreviewProfileImportCommand.cs / Handler         # parses upload, flags duplicates (research.md §5) — no persistence
  ConfirmProfileImportCommand.cs / Handler         # re-parses the re-uploaded file (research.md §4), two-pass base/override resolution (research.md §3) — TDD
  GetMachineProfilesQuery.cs / Handler             # catalog list; computes derived display fields per row via IProfileBundleParser.ExtractMachineDisplayFields
  GetFilamentProfilesQuery.cs / Handler            # same, via ExtractFilamentDisplayFields
  GetMachineProfileQuery.cs / Handler               # single, raw data (FR-011) + derived display fields
  GetFilamentProfileQuery.cs / Handler
  UpdateMachineProfileRawDataCommand.cs / Handler / Validator   # FR-012/013 — validates JSON, overwrites RawDataJson; no caching step, nothing else to keep in sync
  UpdateFilamentProfileRawDataCommand.cs / Handler / Validator
  UnsupportedProfileBundleFormatException.cs        # FR-007 — unrecognized/corrupted archive
  MachineProfileNotFoundException.cs / FilamentProfileNotFoundException.cs
  InvalidProfileJsonException.cs                    # FR-013

api/src/OpenFarm.Infrastructure/Profiles/
  ProfileBundleParser.cs              # ZipArchive-based; classifies by subfolder + per-format field map (research.md §1/§2);
                                       # also implements the two ExtractXDisplayFields(rawDataJson, sourceFormat) methods
                                       # against a bare RawDataJson string + its SourceFormat
  Persistence/Configurations/MachineProfileConfiguration.cs   # narrow schema — no typed spec-field columns, see data-model.md
  Persistence/Configurations/FilamentProfileConfiguration.cs
  Persistence/Migrations/<timestamp>_AddProfiles.cs   # single migration, per constitution's one-per-PR rule
  MachineProfileRepository.cs / FilamentProfileRepository.cs

api/src/OpenFarm.Api/
  Endpoints/Profiles/
    PreviewProfileImportEndpoint.cs     # POST /api/v1/profiles/imports/preview, AllowFileUploads()
    ConfirmProfileImportEndpoint.cs     # POST /api/v1/profiles/imports/confirm, AllowFileUploads() (re-uploads + re-parses, research.md §4)
    ListMachineProfilesEndpoint.cs / GetMachineProfileEndpoint.cs / UpdateMachineProfileRawDataEndpoint.cs
    ListFilamentProfilesEndpoint.cs / GetFilamentProfileEndpoint.cs / UpdateFilamentProfileRawDataEndpoint.cs
  Processors/ActivityLogPostProcessor.cs      # extended with IHasEntityIds support (research.md §7) — existing file, not new
  Processors/IHasEntityIds.cs                 # new marker interface

web/src/features/profiles/
  ProfilesPage.tsx                    # replaces the mock-data prototype; Machine/Filament Tabs (shadcn `tabs.tsx`)
  components/ImportProfilesDialog.tsx  # file select -> preview -> selection list -> confirm, replaces the "Not yet implemented" toast button
  components/ProfileReviewList.tsx     # selection checkboxes, duplicate flags, FR-006 unselected-base warning (client-side, research.md §4)
  components/MachineProfilesTable.tsx / FilamentProfilesTable.tsx   # replace ProfilesTable.tsx's single merged table
  components/ProfileRawDataDialog.tsx  # JSON viewer/editor, FR-011/012/013
  data/ (Orval-generated hooks consumed directly; mockProfiles.ts / mockProfilesStore.ts deleted)
```

**Structure Decision**: Existing four-project Clean Architecture backend + single-SPA frontend,
extended in place — no new top-level project, same shape Phase 1 used. One notable frontend cleanup
folded into this feature rather than deferred: `web/src/features/profiles/` currently holds a
mock-data-only prototype (`mockProfiles.ts`, `mockProfilesStore.ts`, a single merged `ProfilesTable`)
built ahead of this spec as a placeholder — this plan replaces it with the real API-backed
implementation rather than layering the real feature alongside a stale mock, per CLAUDE.md's "never
hand-edit generated files" spirit applied more broadly: no throwaway scaffolding survives once its real
replacement exists. The mock's field shape (`nozzleSizeMm`, `nozzleTempC`, `bedTempC`,
`maxVolumetricSpeedMm3s`, `fanPercent`, `source`) already anticipated this feature's real
`FilamentProfile` derived display fields closely (`data-model.md` — these are computed at read time
from `RawDataJson`, not persisted columns), so the migration is a direct one, not a redesign.

## Complexity Tracking

> **Fill ONLY if Constitution Check has violations that must be justified**

No Constitution Check violations. The two design decisions below are recorded for visibility per the
Constitution Check section's note, not because either is a deviation:

| Decision | Why Needed | Simpler Alternative Rejected Because |
|-----------|------------|-------------------------------------|
| `IHasEntityIds` extension to `ActivityLogPostProcessor` | The confirm-import endpoint creates many entities in one HTTP call — the existing post-processor's single-`Id`-reflection design can't express that | Having `ConfirmProfileImportCommandHandler` call `IActivityLogWriter` directly (bypassing the post-processor) was rejected because it would silently reopen the "every state-mutating command is captured structurally" guarantee the constitution's Security section calls out by name — this endpoint would become the one exception, undocumented at the pipeline level |
| No `PendingImportBatch` staging entity between preview and confirm; confirm re-uploads + re-parses the file instead of trusting echoed preset content | Keeps the review step reversible-by-construction (a cancelled import has no server state to clean up, satisfying spec's interrupted-upload edge case for free) *and* guarantees persisted data always traces back to the server's own parse of a real file, never an unverified client claim | A staging table was considered so confirm could reference a short opaque batch id instead of re-parsing, but it needs its own cleanup/TTL story (abandoned batches, multi-tab conflicts) that nothing else in this phase needs — rejected per Constitution Principle IV ("designed extension points are required, preemptive abstraction is not"). A client-echoed full-payload confirm (no re-upload) was also considered and rejected as a real integrity gap, not just a style choice (`research.md` §4's revision note); re-parsing a second time is cheap at this feature's scale (SC-001, under 50 presets) |
| Descriptive/spec fields (nozzle size, bed volume, enclosure, supported materials, material, brand, color, tuned settings) are computed from `RawDataJson` at read time, not stored as typed columns | Guarantees one source of truth — a raw-JSON edit (FR-012) can never leave a stale cached column behind, and the `UpdateXProfileRawDataCommand` handlers stay a pure validate-and-overwrite with no re-derivation step to get wrong | Storing them as typed columns (matching `Printer`'s inline-telemetry-column precedent) was the original design — reverted because no FR asks for DB-level filter/sort on any of these fields, only display, so caching them was schema surface with no query requirement behind it (Constitution Principle IV); re-extracting from a small `RawDataJson` blob on every catalog read is computationally trivial at this feature's scale |
| `SourceFileName` column and machine-profile `SupportsLiveIntegration` field cut entirely (not persisted, not derived) | Neither has a documented consumer/requirement behind it — `SourceFileName` is written once at confirm and never read back by any endpoint/UI, and `SupportsLiveIntegration` is backed only by spec.md's Key Entities prose (no numbered FR/SC) and an unverified source field | Keeping `SourceFileName` as "cheap provenance" and `SupportsLiveIntegration` as a best-effort heuristic were the original designs — both reverted for consistency with the same "no FR/SC, no keep" standard applied to every other descriptive field above; `SupportsLiveIntegration` can be reintroduced once a later phase both needs it and confirms the real preset field it would read (`research.md` §8) |
| Base/override resolution treats an ambiguous same-name match (2+ candidate rows, in-batch or prior-import) as unresolved, same as no match | FR-009 permits importing duplicate-named presets as separate rows, so `Name` isn't unique — a `BaseProfileName` lookup by `Name` can legitimately match more than one row, which the original two-pass algorithm didn't account for (an in-batch `Name → Id` map can only hold one `Id` per name; a prior-import lookup could return 2+ rows) | A deterministic tie-break (e.g. most-recently-created same-named row) was considered and rejected — it resolves to *something*, but with no principled way to know which same-named row a preset's declared base actually meant; a silently wrong link is worse than a visibly missing one, since it looks correct in the UI and is much harder to notice or correct later |
