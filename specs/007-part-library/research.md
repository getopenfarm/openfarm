# Phase 4 Research: Part Library (with Sliced Files & In-App Slicing)

Supersedes the previous version of this file in §2/§3 — every add/replace, regardless of file type, now
goes through the identical Processing → Ready for Review → Confirm/Discard pipeline (`PartProcessingJob`,
renamed from `PartSlicingJob`), rather than an already-sliced file taking a separate synchronous shortcut.
Per a second follow-up clarification the user raised directly while reviewing this plan
(`checklists/requirements.md`'s note on the round): the two paths already shared nearly everything, and the
only real difference — parse vs. slice — belongs behind one interface's implementation, not exposed as two
different API/UI shapes. A third follow-up clarification further refines §2/§11: `name`/`tags` are no
longer part of the initial submit request at all — attaching a file (plus a Filament, if needed) starts
processing immediately as one continuous add-part dialog, and the operator keeps editing name/tags
concurrently while it runs; both are only actually submitted, together with any narrow filament overrides,
at the Confirm ("Save") step, which the frontend gates on both `ReadyForReview` and (for a new part) a
non-empty name. Everything else carries forward unchanged.

This phase introduces four genuinely new pieces of backend infrastructure with zero prior art in this
codebase (confirmed by direct code search, not assumption): sliced-file metadata inspection, file storage
for retained uploads, headless slicer CLI invocation, and a single async "submit → process in the
background → operator reviews and confirms" pipeline that now covers every file type uniformly. Everything
else — the REPR/FastEndpoints pattern, multi-tenancy, activity logging, SignalR push, Orval/multipart
codegen — is followed exactly as already established in Phases 0–3, per the constitution's "no feature
invents its own pattern" rule.

## 1. Sliced-file metadata inspection is new Infrastructure, patterned after `ProfileBundleParser` but not extending it

**Decision**: introduce a new `Application.Parts.IPartFileClassifier`/`IAlreadySlicedFileMetadataParser`
pair, implemented in `Infrastructure.Parts`, following the exact technique Phase 2's `ProfileBundleParser`
(`api/src/OpenFarm.Infrastructure/Profiles/ProfileBundleParser.cs`) already established: open the upload
with `System.IO.Compression.ZipArchive` directly (no third-party 3MF/zip NuGet package — none is used
anywhere in this repo), route by entry name, parse tolerantly with `System.Text.Json.JsonDocument` where
JSON is present, and treat anything unrecognized as "skip, don't throw."

**Why not extend `IProfileBundleParser` itself**: it's purpose-built to walk `machine`/`filament` preset
JSON subfolders inside an OrcaSlicer/Bambu Studio *profile bundle* — a different question from what Phase 4
asks ("does this 3MF already contain sliced G-code, and if so what does its metadata say about
nozzle/material/time/weight/thumbnail/multi-color mapping"). Copying its proven zip-handling *technique*
into a new, purpose-built interface is the smaller, more honest change.

**Two split interfaces** (both in `Application.Parts`, implemented in `Infrastructure.Parts`):
- `IPartFileClassifier.Classify(Stream, string fileName) -> PartFileFormat, bool requiresSlicing` — STL
  always `requiresSlicing = true`; `.gcode`/`.gco`/`.g` always `false`; a `.3mf` is inspected for Bambu's
  `Metadata/slice_info.config` entry (only present in an already-sliced `.gcode.3mf`) — present → `false`,
  absent → `true`. This step stays **synchronous, at submit time** (§2) — it's cheap I/O (an extension
  check, or at most a single zip-entry-presence check), not the heavy work this phase's async pipeline
  exists for, and its result is needed immediately to validate whether `filamentId` is required (FR-002)
  before a job is even created.
- `IAlreadySlicedFileMetadataParser.ParseAsync(Stream, PartFileFormat) -> SlicedFileMetadata` — nozzle
  diameter, an estimated time/weight, a normalized-to-PNG thumbnail (§8), and one or more filament-requirement
  entries (§7 — a single entry for an ordinary single-material file, one per detected AMS-style color slot
  for a genuine multi-color mapping), or a typed failure. Reads G-code comment headers or a Bambu
  `.gcode.3mf`'s metadata/plate-preview image. Unparseable → the add/replace is rejected outright (FR-006d).

**Alternative considered and rejected**: a dedicated 3MF-parsing NuGet package. Rejected — none is currently
a dependency anywhere in this solution, the actual parsing need is narrow, and the codebase's own precedent
(`ProfileBundleParser`) already proves hand-rolled `ZipArchive` + `JsonDocument` is sufficient without a new
external dependency.

## 2. Every add/replace — regardless of file type — goes through the identical Processing → Ready for Review → Confirm/Discard pipeline

**Decision**: `POST /api/v1/parts` and `PUT /api/v1/parts/{id}/file` are each a single FastEndpoints class
with **one** completion shape, not two. Their request body is deliberately minimal — `file` plus
`filamentId?` only, **no `name`/`tags`** — since the sole purpose of this call is to start processing as
early as possible; the handler calls `IPartFileClassifier` synchronously (§1) purely to validate the
request (`filamentId` required iff `requiresSlicing`, and to determine which downstream processor the job
will need) — but the actual work of turning the file into derived data (nozzle diameter, filament
requirement(s), estimated time/weight, thumbnail) always happens asynchronously via a new
`PartProcessingJob`, and a successful result **always** lands in a "ready for review" state before anything
is saved. There is no longer a synchronous, direct-save branch for an already-sliced file. `name`/`tags`
(and any narrow filament overrides) travel instead with the `Confirm` request (§7) — a third, later
clarification (see this file's header) moved them there specifically so the operator can keep typing a
part's name while processing runs, rather than having to finalize it before a file can even start being
processed.

**Why unify, reversing the previous two-shape design**: the two paths already shared nearly everything —
the same derived-data shape, the same rejection rules (FR-006), the same thumbnail/estimate requirements,
and (once multi-color already-sliced files are considered) the same need to let the operator narrow one or
more filament requirements after seeing the parsed result. The only genuine difference between them is
*how* the result gets produced — reading embedded metadata vs. invoking an external slicer process — and
that is exactly the kind of difference that belongs behind one interface's implementation, not exposed as
two different API/UI shapes an operator has to learn. Concretely: `Application.Parts.IPartFileProcessor`
(new, in `Application.Parts`) is the **single** dependency `PartProcessingBackgroundService` (§3) calls;
its `Infrastructure.Parts.PartFileProcessor` implementation branches internally — `requiresSlicing = true`
delegates to `ISlicerEngine.SliceAsync` (§4), `requiresSlicing = false` delegates to
`IAlreadySlicedFileMetadataParser.ParseAsync` (§1) — and returns the same `PartProcessingResult` shape
either way. `ISlicerEngine` itself stays its own clean seam underneath (not folded into
`IPartFileProcessor`), since FR-010 requires Phase 7 to be able to depend on it directly and unmodified for
on-demand job-dispatch-time slicing — only *this phase's* pipeline needs the unifying facade.

**What review/confirm does and does not change** (unchanged reasoning from the previous revision, now
applying uniformly): the operator reviewing a "ready for review" result may (a) attach or change the
narrower Filament Profile/color-variant override on any of the resulting filament requirement(s), having
now seen what was actually derived — for an already-sliced multi-color file this can mean narrowing several
requirements at once, not just the single one an in-app slice always produces — and then confirm, which
saves the part immediately from the already-derived data, or (b) discard it, saving nothing. **Nothing
about the derived numbers themselves is editable at this step**, for any file type — nozzle diameter,
material type(s), estimated time/weight stay exactly as derived; spec.md's "no manual numeric entry
anywhere" invariant is unchanged.

**Practical consequence, not a design compromise**: an already-sliced file's job typically reaches "ready
for review" near-instantly (parsing embedded metadata needs no external process), so in practice the
operator experience there is barely distinguishable from the old synchronous path — the unification costs
essentially nothing in perceived latency while removing an entire second code path, a second API response
shape, and a second frontend interaction pattern.

**Alternative considered and rejected**: keeping the two-shape design (the previous revision of this file).
Superseded once it became clear the two paths' actual behavior had already converged everywhere except the
one, purely mechanical parse-vs-slice distinction — maintaining two shapes for that one difference was
strictly more surface area (two response contracts, two frontend flows, two sets of tests) for no remaining
functional benefit.

## 3. A new async processing pipeline: `PartProcessingJob` models Processing → Ready for Review → Confirm/Discard for every file type

**Decision**: introduce `PartProcessingJob` (`OpenFarm.Domain.Parts`, renamed from the earlier
`PartSlicingJob` now that it covers parsing as well as slicing) — a persisted tracking record (`Id`,
`OrganizationId`, `PartId` nullable until an Add is confirmed, `Kind: Add | Replace`,
`Status: Processing | ReadyForReview | Confirmed | Failed | Discarded`, the preview fields once
`ReadyForReview` — `PreviewNozzleDiameterMm`, `PreviewEstimatedPrintTimeMinutes`,
`PreviewEstimatedFilamentWeightGrams`, `PreviewThumbnailBytes`, and one-or-more
`PreviewFilamentRequirement`s (§7) — `ErrorMessage`, `RequestedByUserId`, `CreatedAtUtc`, `ProcessedAtUtc`,
`ResolvedAtUtc`) — distinct from `Part` itself, so "a part is never saved in an incomplete state" (spec.md
Assumptions) stays literally true: nothing under `/parts` ever shows a half-built or not-yet-reviewed row.
A bounded `System.Threading.Channels.Channel<PartProcessingWorkItem>` feeds a new
`PartProcessingBackgroundService` (`Infrastructure.Parts`, registered as a hosted service exactly like
`PrinterConnectionBackgroundService`), whose consumer count is capped by a small configurable
`MaxConcurrentSlicingOperations` (default 2, applying only to the CPU/IO-heavy slicing branch in practice —
parsing an already-sliced file's metadata is cheap enough that this cap is never the bottleneck there). The
background service resolves a tenant-aware `IServiceScope` per work item using the exact
`IServiceScopeFactory` + `IMultiTenantContextSetter` pattern `PrinterConnectionBackgroundService` already
established (`CreateTenantScope()`).

**`Status` lifecycle**: `Processing` (initial, for every file type) → `ReadyForReview` (parse or slice
succeeded; the background service's job here is done — it never touches `Part` itself) *or* `Failed`
(processing itself failed, terminal). From `ReadyForReview`, an operator-initiated `POST .../confirm` moves
it to `Confirmed` (creating/updating the `Part` in the same request) or `DELETE .../confirm` moves it to
`Discarded` — both terminal, both plain synchronous HTTP endpoints, since all the data needed to
create/update the `Part` was already computed and is just sitting on the job row.

**Activity log stays simple, and now uniformly so**: the actual `Part`-mutating action (confirm) is
operator-initiated and completes within an HTTP request for **every** file type now, so the existing
`IActivityLoggable`-on-Request-DTO post-processor covers "PartCreated"/"PartFileReplaced" for free — no
special-cased attribution logic needed there, for either path. The only place this phase still needs a
**direct** `IActivityLogWriter` write from inside `PartProcessingBackgroundService` (mirroring
`PrinterConnectionBackgroundService`'s existing out-of-request logging) is a processing **failure** — a
`Failed` job has no operator-initiated HTTP request at that moment, so its "PartFileProcessingFailed" entry
is written directly by the background service, attributed to `RequestedByUserId` (captured on the job at
submission time) rather than `ActorType.System`.

**Alternative considered and rejected**: Hangfire or a similar durable job-queue library. Rejected — this
phase's queue depth and durability needs are small, and a bounded in-process `Channel` plus one small
tracking table follows the codebase's existing "plain C#/EF Core, no framework we don't already have a
reason for" bias.

## 4. Slicer engine invocation: a new external-process seam, no prior art in `third_party/`

**Decision**: a new `Application.Parts.ISlicerEngine` (`SliceAsync(SlicerEngine engine, Stream modelFile,
string materialType, decimal nozzleDiameterMm, CancellationToken) -> SlicingResult
{ EstimatedPrintTimeMinutes, EstimatedFilamentWeightGrams, ThumbnailPngBytes }`) implemented in
`Infrastructure.Parts` as `CliSlicerEngine`, invoking the configured OrcaSlicer/Bambu Studio headless CLI
binary via `System.Diagnostics.Process` against a temp working directory, parsing the produced G-code's
header-comment estimates and a plate-preview image (re-encoded to PNG, §8), then deleting the temp output —
the actual toolpath is never persisted (FR-005). This interface is consumed by `IPartFileProcessor` (§2) for
this phase's own pipeline, and will later be consumed directly and unmodified by Phase 7 (FR-010).
`SlicerOptions` (CLI binary paths, timeout) is bound from configuration; the container image gains a new
build step installing the OrcaSlicer CLI (the org-configurable default per FR-007).

Confirmed via direct search: `third_party/printers/bambulab` (the existing git submodule) contains only
reverse-engineered *live-connection* protocol docs (MQTT/FTP/camera) — nothing about either slicer's CLI,
project format, or G-code metadata conventions. There is no vendored reference to build on.

**Test discipline**: mirrors the constitution's existing carve-out for `BambuLanConnector` — `ISlicerEngine`
is developed and handler-tested against a `FakeSlicerEngine` first (test-after/fake-based, not TDD); real
CLI validation is manual, never a CI gate, exactly like physical-printer validation today.

## 5. Nozzle diameter auto-detection reuses Phase 2's existing display-field extraction; the mode-selection algorithm itself is TDD

**Decision**: a new `Application.Parts.NozzleDiameterResolver.Resolve(printers, profiles) -> decimal?` is a
small, deterministic, well-specified function — the same bar `FeedUnitKindCatalog.Resolve` and
`PrinterModelCatalog.Resolve` were already held to — so it's TDD, not test-after. It does **not** re-parse
`MachineProfile.RawDataJson` itself: it calls the already-built
`IProfileBundleParser.ExtractMachineDisplayFields(rawDataJson).NozzleSizeMm` (Phase 2) for each linked
profile, then computes the mode among printers whose `MachineProfileId` is set and whose linked profile has
a parseable nozzle size. **Tie-break rule**: the **smallest** diameter wins (a conservative, broadly
compatible default). No qualifying printer → `null` → FR-006(b)'s rejection, checked before a
`PartProcessingJob` is even created for a requires-slicing file.

## 6. `Printer.MachineProfileId`: an enforced nullable FK, not an unenforced bare `Guid?`

**Decision**: unlike the soft references described in §7, `Printer.MachineProfileId` gets a real EF-tracked
foreign key: `builder.HasOne<MachineProfile>().WithMany().HasForeignKey(p => p.MachineProfileId)
.OnDelete(DeleteBehavior.SetNull)` — the same shape `MachineProfile.BaseProfileId`'s self-reference already
uses. **Why the stronger guarantee here specifically**: FR-005a's nozzle-diameter auto-detection is a
farm-wide correctness computation that silently produces a wrong slicing target if a stale/dangling
`MachineProfileId` is allowed to exist — there is no per-part tolerance for "unresolved" the way there is
for a filament requirement's narrower override (§7). Phase 2 has no delete endpoint for `MachineProfile`
yet, so this is currently more a correctness-by-construction choice than a day-one operational necessity —
but it costs nothing and removes a whole class of "what if this Guid doesn't resolve" bugs later.

## 7. A Part's filament requirement is a collection (`PartFilamentRequirement`), and its narrow overrides follow the existing `Filament.FilamentProfileId` "soft reference" precedent

**Decision**: a `Part` holds a collection of `PartFilamentRequirement` child rows, each with its own
`SlotIndex?`, `MaterialType`, and up to one of `NarrowFilamentVariantId`/`NarrowFilamentProfileId` (mutually
exclusive). A real already-sliced multi-color file (Bambu AMS-style slot-to-filament mapping) genuinely
declares more than one material/color requirement; an in-app slice still always produces exactly one
(in-app slicing targets exactly one operator-specified Filament this phase). Since every add/replace now
goes through the same review step (§2), narrow overrides for **any** requirement — however many a given
file produced — are supplied at the same point: the `Confirm` request, as an array keyed by `slotIndex`, not
on the initial submit. This is a further simplification the unification (§2) enables: the earlier design
had already-sliced files supply narrow overrides upfront (no review step existed for them yet) while an
in-app slice's single requirement was narrowed at confirm time — two different moments for conceptually the
same operator action. Now there is exactly one moment, for every case.

**Each requirement's narrow-override fields stay unenforced `Guid?` columns** — no `HasOne`/FK constraint,
exactly matching `Filament.FilamentProfileId`'s existing pattern. **Why**: spec.md's own edge case is
explicit — a narrowing reference that's later deleted becomes unresolved and is surfaced as such, but the
requirement (and the part as a whole) keeps matching on its generic data; the other requirements on the same
part are unaffected. An unenforced reference is what makes that graceful, per-requirement degradation
trivial.

**Both `NarrowFilamentVariantId` and `NarrowFilamentProfileId` are kept, not collapsed into one** — spec.md's
FR-012a explicitly names both as independent options, and a `FilamentProfile` can exist (imported in Phase
2) with no corresponding Phase 3 `Filament`/`FilamentVariant` catalog row ever created for it. `Application`-
layer validation enforces "at most one of the two may be set per requirement" (a FluentValidation gate, not
a DB constraint).

## 8. File storage: new `IPartFileStorage` abstraction (local disk); thumbnails stay in Postgres as `bytea`, always normalized to PNG

**Decision**: confirmed via direct search — **no file-storage abstraction exists anywhere in this
codebase today.** Phase 4 introduces `Application.Parts.IPartFileStorage`
(`SaveAsync`/`OpenReadAsync`/`DeleteAsync`, keyed by `organizationId`/`partId`) implemented in
`Infrastructure.Parts` as `LocalDiskPartFileStorage`, writing under a new bind-mounted volume
(`openfarm_part_files:/data/part-files`, added to `infra/docker-compose.yml`), namespaced by
`organizationId` first.

A part's **thumbnail** is small and bounded, so it's stored as a plain `byte[]`/`bytea` column directly on
`Parts` (and, transiently, on `PartProcessingJobs` while a preview is pending) rather than as a second file
on disk. **No separate content-type/format column is needed**: every thumbnail source (a slicer CLI's
plate-preview export, a G-code comment's base64 preview, a Bambu 3MF's embedded plate PNG) is normalized to
PNG at ingestion time, so `GET /parts/{id}/thumbnail` (and the job-preview equivalent) can always serve
`Content-Type: image/png` unconditionally.

## 9. Tags: a new small many-to-many, org-scoped, get-or-create by name

**Decision**: `Tag` (`Id`, `OrganizationId`, `Name`, unique per `(OrganizationId, Name)`) plus a plain join
table `PartTags` (`PartId`, `TagId`). Tagging a part with a name that doesn't exist yet get-or-creates the
`Tag` row in the same command.

## 10. Organization-level slicing default: one column on `Organization`, not a new settings table

**Decision**: `Organization.DefaultSlicerEngine` (`SlicerEngine` enum: `OrcaSlicer` | `BambuStudio`, default
`OrcaSlicer`) is a single new column on the existing `Organization` entity — not a new `SlicingDefaults`
table, per Principle IV (no speculative structure for one field with no second consumer named).

## 11. Frontend: `web/src/features/parts/` moves from mock data to the real API, with one uniform submit/review flow

**Decision**: `web/src/features/parts/` (currently `data/mockParts.ts` + `data/mockPartsStore.ts`) is
rewired to the generated Orval hooks, following `features/printers`/`features/filament` as the "real
backend" template. Three new pieces are needed:
- **File-upload form field**: no existing RHF+zod pattern manages a `File` — the one precedent
  (`features/profiles/components/ImportProfilesDialog.tsx`) manages the file via plain `useState<File | null>`
  alongside an otherwise-RHF form. This phase's Add/Replace Part dialog follows that same precedent.
- **Static thumbnail display**: no generic image/thumbnail primitive exists in `components/ui/` — this
  phase adds a small, feature-scoped `PartThumbnail` component (an `<img>` with a graceful "no thumbnail"
  fallback state, per FR-014a).
- **A single Add/Replace Part dialog that stays open across the whole submit → process → review → save
  sequence**, not a two-step "submit, then separately go review a job" interaction. Selecting/attaching a
  file (and, if the file needs slicing, picking a Filament) immediately fires the `POST`/`PUT` request and
  keeps the dialog open; the operator continues typing the part's name and adding tags in that same dialog
  while a small in-progress indicator shows the job's `Processing` state (subscribed via `usePartsHub`, not
  polled). Once `PartProcessingJobStatusChanged` reports `ReadyForReview`, the dialog reveals the derived
  nozzle diameter/material(s)/estimates/thumbnail and a per-requirement narrow-override picker in place —
  the same dialog, not a navigation to a separate review screen. The Save button is `disabled` until the job
  is `ReadyForReview` **and** (for an Add) the name field is non-empty; clicking it fires the `Confirm`
  request with the name/tags/narrow overrides accumulated in the form's local state. Cancel at any point
  fires `Discard`. This is one component doing one continuous job for every file type — not a
  slicing-specific panel plus a separate direct-save form for already-sliced files, and not two separate
  dialogs (submit vs. review) an operator has to move between.

**No TanStack Virtual.** SC-005 targets filtering 500+ parts in under 10 seconds — 500 rows is comfortably
within the existing `DataTable` (`web/src/components/data-table/DataTable.tsx`) plus its existing
pagination; introducing virtualization now, with no second consumer yet, would be speculative building.

**SignalR**: job-status push (`PartProcessingJobStatusChanged`, covering `Processing → ReadyForReview →
Confirmed | Discarded` / `Processing → Failed` for every file type uniformly) and catalog-changed push
(`PartUpserted`, `PartDeleted`) reuse the existing single `/hubs/farm` hub and its `IHubContext<FarmHub>` +
`FarmHub.GroupName(orgId)` + `Application`-defined-publisher-interface pattern exactly as
`FarmHubPrinterEventPublisher` already establishes. On the frontend,
`web/src/features/printers/hooks/useFarmHub.ts` is currently the only hub client; this phase extracts the
shared `HubConnectionBuilder` setup into `web/src/lib/signalr/farmHubConnection.ts` used by both
`useFarmHub` and a new `features/parts/hooks/usePartsHub.ts`.

## 12. Test discipline mapping for this phase's new code

- **TDD** (deterministic, well-specified — the constitution's non-negotiable bar): `NozzleDiameterResolver`
  (§5, mode + tie-break), the Add/Replace/Confirm Part FluentValidation gates (Filament-required-when-
  slicing, mutually-exclusive narrow overrides per requirement, file-format acceptance), and
  `PartFileClassifier`'s STL/G-code/3MF dispatch rule itself.
- **Test-after / fixture-replay** (matching `BambuMqttMessageParser` and `ProfileBundleParser`'s existing
  carve-out): `IAlreadySlicedFileMetadataParser`'s actual G-code/3MF-metadata byte parsing (including
  multi-slot mapping extraction), and `ISlicerEngine`'s real CLI invocation.
- **Test-after** (orchestration-only, per the constitution's standing carve-out): `Application` command
  handlers (`AddPartCommandHandler`, `ReplacePartFileCommandHandler`, `ConfirmPartProcessingJobCommandHandler`,
  `DiscardPartProcessingJobCommandHandler`), `PartFileProcessor`'s dispatch (parse vs. slice — orchestration,
  not the parsing/slicing logic itself), `PartProcessingBackgroundService`'s queue-consumption loop, EF Core
  repository/configuration code, and all React components/pages.
- Backend integration: `WebApplicationFactory` + Testcontainers.PostgreSql, covering the full Processing →
  Ready for Review → Confirm/Discard cycle for both an already-sliced file (including a multi-requirement
  fixture) and a requires-slicing file (`FakeSlicerEngine`), every FR-006 rejection case, and the
  printer↔machine-profile link + nozzle-diameter auto-detection round trip.
