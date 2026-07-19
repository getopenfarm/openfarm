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
non-empty name. A fourth follow-up simplifies §3: a job's preview filament requirement(s) are now a `jsonb`
column on `PartProcessingJob` (§3a) rather than a separate child table, since this data is small, transient,
and never independently queried — mirroring `Printer.AmsUnits`'s existing pattern. §7 was also revised
(distinct-(material,color)-pair grouping across every plate, not one row per detected slot) per a worked
example the user supplied directly.

**A sixth, larger round of follow-up clarifications (post-implementation-review) supersedes §1/§3/§7a/§8
further, in three linked ways:**

1. **`filamentId` is removed from the request entirely (§7a rewritten, §13 new)** — an operator never
   selects a material at upload time, for any file type. Material now always resolves from the file itself:
   an unsliced 3MF's own project-level filament assignment (§13, new — Orca Slicer/Bambu Studio require
   picking one to save any project), or, only for an STL (which structurally has none), a fixed internal
   fallback (§7a, narrowed — the *only* remaining case that produces a fully unconstrained "any material"
   requirement). An unsliced 3MF whose project material can't be parsed now rejects outright, exactly like
   an already-sliced file's unparseable metadata — it does **not** fall back to "any material."
2. **Per-plate thumbnails, not one per file (§8 rewritten)** — both an already-sliced and a requires-slicing
   multi-plate file now get one thumbnail derived per plate, not only the first. `Part`/`PartProcessingJob`
   each gain a small per-plate thumbnail child table instead of a single `bytea` column.
3. **Confirm no longer waits on the estimate (§3 rewritten, §3b new)** — nozzle diameter and material now
   resolve, and reach `ReadyForReview`, independently of and well before estimated print time/weight and the
   per-plate thumbnails for a requires-slicing file (they're inputs to the slice, not outputs of it, once
   `filamentId` is gone — see §13). An operator can confirm as soon as `ReadyForReview` is reached even
   though the estimate/thumbnails may still be resolving; a new background continuation (§3b) then writes
   the result onto the job's preview (if still unconfirmed) or directly onto the saved `Part` (if already
   confirmed) — the first case in this phase where a `Part` can carry a permanent gap in its derived data,
   if that continuation itself fails post-confirm.

Everything else from the fifth follow-up (`filamentId` optionality is superseded by full removal, above) and
everything not called out above carries forward unchanged.

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
  exists for. Since `filamentId` no longer exists on the request (§13), this result now only decides which
  downstream processor a job needs (§2) — it no longer has any "any material" implication itself; that's
  entirely a §7a/§13 concern, resolved later in the pipeline, not here.
- `IAlreadySlicedFileMetadataParser.ParseAsync(Stream, PartFileFormat) -> SlicedFileMetadata` — nozzle
  diameter, a summed estimated time/weight across every plate (§7), a normalized-to-PNG thumbnail **per
  plate** (§8, reopened from "first plate only"), and one or more grouped filament-requirement entries (§7 —
  a single entry for an ordinary single-material file, one per **distinct detected (material type, color)
  pair** found across every plate and every AMS slot within each plate), or a typed failure. For a G-code
  file this reads its header comments (`filament_type`/`filament_colour`, standard Bambu/OrcaSlicer
  convention — one value per AMS slot used); for a `.gcode.3mf`, this now walks **every plate's own embedded
  G-code/metadata block** (not just the first), reading the same per-slot fields from each, plus each
  plate's own embedded preview image (§8). Unparseable → the add/replace is rejected outright (FR-006c).
- `IUnslicedProjectMaterialResolver.ResolveAsync(Stream) -> UnslicedProjectMaterial` (new, §13) — reads an
  unsliced 3MF project's own filament-preset assignment and plate count directly from its project settings
  (present because Orca Slicer/Bambu Studio require selecting a filament to save any project, sliced or
  not), independent of `IAlreadySlicedFileMetadataParser` above (which only ever runs against an *already*-
  sliced file). Only ever invoked when `requiresSlicing = true` **and** `PartFileFormat = ThreeMf` — an STL
  never has this called against it at all, since it always takes the fixed-fallback path instead (§7a).
  Unparseable → rejected outright (FR-006), exactly like an already-sliced file's unparseable metadata.

**Alternative considered and rejected**: a dedicated 3MF-parsing NuGet package. Rejected — none is currently
a dependency anywhere in this solution, the actual parsing need is narrow, and the codebase's own precedent
(`ProfileBundleParser`) already proves hand-rolled `ZipArchive` + `JsonDocument` is sufficient without a new
external dependency.

## 2. Every add/replace — regardless of file type — goes through the identical Processing → Ready for Review → Confirm/Discard pipeline

**Decision**: `POST /api/v1/parts` and `PUT /api/v1/parts/{id}/file` are each a single FastEndpoints class
with **one** completion shape, not two. Their request body is deliberately minimal — `file` only, **no
`filamentId`, `name`, or `tags`** — since the sole purpose of this call is to start processing as early as
possible; the handler calls `IPartFileClassifier` synchronously (§1) to determine which downstream processor
the job will need. `filamentId` is gone from the request entirely as of the sixth follow-up (§13) — material
is now always resolved from the file itself, never operator-supplied at this step. The actual work of
turning the file into derived data (nozzle diameter, filament requirement(s), estimated time/weight, per-
plate thumbnails) happens asynchronously via a new `PartProcessingJob`, and now reaches "ready for review" in
**two effective stages** rather than one (§3/§3b) — nozzle diameter/material first, estimate/thumbnails
potentially trailing for a requires-slicing file. There is no longer a synchronous, direct-save branch for an
already-sliced file. `name`/`tags` (and any narrow filament overrides) travel instead with the `Confirm`
request (§7) — a third, later clarification (see this file's header) moved them there specifically so the
operator can keep typing a part's name while processing runs, rather than having to finalize it before a
file can even start being processed.

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
`ReadyForReview` — `PreviewNozzleDiameterMm`, `PreviewFilamentRequirements` (§3a),
`PreviewEstimateStatus: Resolving | Available` (new, §3b), `PreviewEstimatedPrintTimeMinutes`,
`PreviewEstimatedFilamentWeightGrams`, `PreviewThumbnails` (new, §8, replaces the old single
`PreviewThumbnailBytes`) — `ErrorMessage`, `RequestedByUserId`, `CreatedAtUtc`, `ProcessedAtUtc`,
`ResolvedAtUtc`) — distinct from `Part` itself, so "a part is never saved without a resolved nozzle diameter
and material" (spec.md Assumptions) stays literally true: nothing under `/parts` ever shows a row with an
unresolved nozzle diameter or material. A bounded `System.Threading.Channels.Channel<PartProcessingWorkItem>`
feeds a new `PartProcessingBackgroundService` (`Infrastructure.Parts`, registered as a hosted service exactly
like `PrinterConnectionBackgroundService`), whose consumer count is capped by a small configurable
`MaxConcurrentSlicingOperations` (default 2, applying only to the CPU/IO-heavy slicing branch in practice —
parsing an already-sliced file's metadata, or an unsliced 3MF's project material (§13), is cheap enough that
this cap is never the bottleneck there). The background service resolves a tenant-aware `IServiceScope` per
work item using the exact `IServiceScopeFactory` + `IMultiTenantContextSetter` pattern
`PrinterConnectionBackgroundService` already established (`CreateTenantScope()`).

**`Status` lifecycle, revised for the two-milestone flow (§3b)**: `Processing` (initial, for every file
type) → `ReadyForReview` once nozzle diameter and material resolve — for an already-sliced file this is the
same moment its estimate/thumbnails also resolve (one atomic parse, `PreviewEstimateStatus = Available`
immediately); for a requires-slicing file, this now happens **before** the slicer CLI necessarily finishes,
since nozzle diameter (farm-wide, resolved at submit) and material (§13 — parsed from an unsliced 3MF's
project settings, or STL's fixed fallback, §7a) no longer depend on the slice itself
(`PreviewEstimateStatus = Resolving`, the actual `ISlicerEngine` invocation continuing in the background past
this transition). `Processing` can *also* still land on `Failed` (terminal) if nozzle diameter/material
themselves can't be resolved. **New transition**: `ReadyForReview → Failed` — if a requires-slicing job's
still-running slice subsequently fails while the job is still unconfirmed, the whole job (including its
already-shown nozzle diameter/material) is retroactively rejected, matching spec.md's rule that a failure
**before** confirmation still rejects the operation outright (FR-006/SC-007). From `ReadyForReview`, an
operator-initiated `POST .../confirm` moves it to `Confirmed` (creating/updating the `Part` in the same
request, from whatever has been derived so far — `PreviewEstimateStatus` may still be `Resolving` at that
moment) or `DELETE .../confirm` moves it to `Discarded` — both terminal, both plain synchronous HTTP
endpoints. `Confirmed` is no longer necessarily the end of this job's background activity, though — see §3b.

## 3a. A job's preview filament requirement(s) are a `jsonb` column on `PartProcessingJob`, not a separate child table

**Decision**: `PartProcessingJob.PreviewFilamentRequirements` is stored as `jsonb` (a small
`IReadOnlyList<PreviewFilamentRequirement>` value list, `HasConversion` + a custom `ValueComparer`) directly
on the job row, rather than a separate `PartProcessingJobPreviewFilamentRequirement` child table (an earlier
revision of this design). **Why**: this data is small (1–4 entries, bounded by real AMS hardware), never
independently queried/filtered/joined by anything (it's read exactly once, by the `Confirm` handler, then
superseded by the real `PartFilamentRequirement` rows it produces), and this codebase already has an
established, working pattern for exactly this shape — `Printer.AmsUnits`/`Printer.ExternalSpoolTray`
(`api/src/OpenFarm.Domain/Printers/Printer.cs`) are stored the same way for the same reason. A dedicated
child table would mean a fourth migration-relevant entity, its own EF configuration class, and a cascade-
delete relationship for data whose entire lifetime is "written once, read once, then irrelevant." This is
the *opposite* asymmetry from `Part.PartFilamentRequirement` (§7), which stays a real child table —
deliberately, since that one's narrow-override fields need to be resolved against `FilamentVariant`/
`FilamentProfile` at read time for the rest of the part's life, and a future Phase 7 will plausibly query/
join it for job-dispatch matching. The preview copy on a job has neither of those needs.

**This does not change what the `Confirm` request accepts.** The client still never submits nozzle
diameter, material type, or estimated time/weight — those remain exclusively server-derived and
server-stored (now in this `jsonb` column instead of a child table), read back by the `Confirm` handler
itself, not echoed back by the client. `Confirm`'s request body is unchanged: `name`/`tags` (Add only) plus
an optional array of narrow-override choices keyed by `requirementIndex` — the only things a client ever
legitimately supplies. Letting the client instead round-trip the full derived record (submit it back as
part of `Confirm`) was considered and rejected: it would reopen exactly the "no manual numeric entry
anywhere" hole spec.md's FR-006/FR-013 close, since nothing would then stop a client from submitting a
different nozzle diameter or estimate than what was actually derived.

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

## 3b. Confirm no longer waits on the estimate: a trailing background continuation targets the job's preview or the confirmed `Part`, whichever currently holds the truth

**Decision**: for a requires-slicing job, the `ISlicerEngine` invocation that produces estimated print
time/weight and per-plate thumbnails (§4/§8) is no longer a prerequisite for reaching `ReadyForReview` — it
now runs as a continuation of the *same* background work item, spanning across whatever the job's `Status`
happens to be by the time it completes. On completion, the continuation checks the job's current `Status`:

- **Still `ReadyForReview` (operator hasn't confirmed yet)**: on success, update the job's own
  `PreviewEstimatedPrintTimeMinutes`/`PreviewEstimatedFilamentWeightGrams`/`PreviewThumbnails` in place,
  flip `PreviewEstimateStatus: Resolving → Available`, and push an updated `PartProcessingJobStatusChanged`
  event (`status` unchanged at `"ReadyForReview"`, `preview` refreshed) so an open review dialog updates
  live. On failure, transition the whole job `ReadyForReview → Failed` (§3) — this is not a partial-Part
  case, since nothing has been saved yet.
- **Already `Confirmed` (operator saved early, FR-009c/FR-009d)**: the job itself stays `Confirmed` and
  untouched (it remains a clean audit record of what was reviewed at confirm time) — instead, the
  continuation writes directly onto the linked `Part` (`job.PartId`, always set by the time a job reaches
  `Confirmed`): on success, `Part.EstimatedPrintTimeMinutes`/`EstimatedFilamentWeightGrams`/`PartThumbnail`
  rows populate and `Part.EstimateStatus: Resolving → Available`; on failure, `Part.EstimateStatus:
  Resolving → Unavailable` (permanent — spec.md's one legitimate case of a `Part` carrying incomplete
  derived data, FR-009d). Either way, this write is a direct `Part` mutation from inside
  `PartProcessingBackgroundService`, not a second HTTP-triggered command — mirroring exactly how a
  processing *failure*'s direct `IActivityLogWriter` write already works today (§3a) for the same underlying
  reason (no HTTP request is in flight at that moment). A `PartEstimateResolved`/`PartEstimateUnavailable`
  SignalR event (§8/`contracts/part-library-api.md`) notifies any open catalog/detail view, reusing the
  existing `IPartEventPublisher` seam — no polling, no manual refresh (FR-009e).
- **Already `Discarded`**: the continuation's result is simply dropped — best-effort cancellation is
  attempted on `Discarded` (§this file, unchanged from the previous revision), but even if the CLI process
  had already run to completion by the time the discard request lands, there's nothing left to write it to.

**Why not model this as a second job**: a separate `PartEstimateRefinementJob` entity was considered and
rejected — it would duplicate `PartProcessingJob`'s tracking shape (status, error message, timestamps) for
what is, mechanically, the exact same background work item that produced the job's own nozzle-diameter/
material result a moment earlier; the only new thing needed is a target that can shift from "job preview" to
"the `Part` itself" depending on timing, which a plain status check inside the existing continuation handles
without a second entity, a second table, or a second background-service registration.

**Why this doesn't reopen "no manual numeric entry anywhere"**: the trailing continuation writes exactly the
same server-derived values the job's own preview would otherwise have shown, at the same fidelity, produced
by the same `ISlicerEngine`/`IAlreadySlicedFileMetadataParser` call — the *only* thing FR-009c/FR-009d
changes is *when* the operator is allowed to stop waiting for it, not *what* eventually gets stored or *who*
supplies it.

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

## 7. A Part's filament requirement is a collection (`PartFilamentRequirement`), grouped by distinct (material, color) pair across every plate/slot — not one row per slot or plate

Supersedes this section's previous revision — per a fourth follow-up clarification the user raised directly
(after supplying a concrete worked example: a 4-plate file with plates 1/3 sharing black PETG, plate 2 white
PLA, plate 4 red PETG, expecting **3** requirement rows, not 4 and not 1). Two changes from the prior
design: (a) detection now scans **every plate**, not just the first — partially reopening spec.md's earlier
"multi-plate 3MF is an accepted, unaddressed limitation" decision, specifically for filament-requirement
purposes; (b) requirements are identified by their **detected (material type, color) pair**, not by a
`SlotIndex` — `SlotIndex` is dropped entirely from the model.

**Decision**: a `Part` holds a collection of `PartFilamentRequirement` child rows, each with `MaterialType`,
an optional best-effort `DetectedColorHex` (and `DetectedSubBrand`, purely informational — e.g. "Bambu
PETG-CF" — mirroring `AmsTray`'s existing `SubBrand`/`ColorHex` shape from Phase 1/3 telemetry), and up to
one of `NarrowFilamentVariantId`/`NarrowFilamentProfileId` (mutually exclusive). The parsing/grouping
algorithm: scan every plate in the file, and every AMS slot within each plate, for its detected
`(MaterialType, DetectedColorHex)` pair; **group** the results by distinct pairs (a small, deterministic,
well-specified function — `Application.Parts.FilamentRequirementGrouper`, the same TDD bar as
`NozzleDiameterResolver`/`FeedUnitKindCatalog.Resolve`, §12); emit exactly one `PartFilamentRequirement` per
distinct group, in first-seen order. A file that's uniformly one material+color (however many plates/slots
it spans) produces exactly one requirement; a file with genuinely different pairs produces one per pair,
regardless of how many plates/slots repeat a given pair. **`DetectedColorHex` groups/distinguishes
requirements from each other — it is never itself a matching criterion** (matching still runs purely on
`MaterialType`, or the narrow override if the operator sets one); it exists so the operator reviewing "3
filament requirements" can actually tell them apart (e.g. "PETG — black" vs. "PETG — red") rather than
guessing from an opaque slot number, which is the practical problem an earlier revision of this design (one
row per `SlotIndex`, no color at all) didn't solve.

**Narrowing one requirement applies to every plate/slot sharing its pair, not per-plate independently** —
this falls directly out of the grouping: since a requirement already represents *every* plate/slot with that
exact material+color combination, there is no finer-grained "narrow plate 3 differently from plate 1"
concept to build, and the user's own framing of the decision confirms this is the intended behavior, not a
limitation. An in-app slice still always produces exactly one requirement regardless (in-app slicing targets
exactly one operator-specified Filament this phase — true multi-material *slicing* is out of scope), so
none of this grouping machinery is exercised on that path at all.

Since every add/replace goes through the same review step (§2), narrow overrides for **any** requirement —
however many a given file produced — are supplied at the same point: the `Confirm` request, as an array. The
preview copy of each requirement (§3a) carries a stable `RequirementIndex` (0-based, assigned in the same
first-seen grouping order) that the `Confirm` request's override entries key against, replacing `SlotIndex`'s
old role as the "which requirement does this override apply to" reference.

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

**Print time/weight aggregation**: since a multi-plate `.gcode.3mf` executes all of its plates as one
continuous physical print, `Part.EstimatedPrintTimeMinutes`/`EstimatedFilamentWeightGrams` are the **sum**
across every plate's own parsed estimate (FR-013) — a single-plate file is trivially "sum of one." The
thumbnail stays first-plate-only, unchanged (FR-014) — reopening multi-plate detection for filament
requirements doesn't extend to picking a "best" plate for the thumbnail; that stays exactly as already
ratified.

## 7a. "Any material" is now STL-only: `MaterialType` stays nullable, driven by a fixed internal fallback slicer profile — never an organization setting, never valid for a 3MF (sliced or not) or a G-code

**Supersedes this section's fifth-follow-up revision** — a sixth follow-up removes `filamentId` from the
request entirely (§13), which also narrows exactly when "any material" can legitimately occur. Previously,
omitting an optional `filamentId` on a requires-slicing file was the trigger; now, since there's no operator
input to omit, "any material" instead falls directly out of the **file format**: an STL structurally can
never carry material data (it's geometry only), so it is the only format that ever produces this case. An
unsliced 3MF, by contrast, is now expected to always resolve a real material from its own project settings
(§13) — if it can't, that's a rejection (FR-006), not a fallback to this wildcard.

**Decision**: `PartFileProcessor`'s `ISlicerEngine` branch, when the classified file is an STL, runs the
in-app slice against a **fixed, hardcoded fallback material profile** — a constant on `SlicerOptions` (e.g.
`GenericFallbackMaterialProfile = "Generic PLA"`), one entry per `SlicerEngine`, since OrcaSlicer and Bambu
Studio each ship their own bundled generic system presets under slightly different names. This is **not** an
organization-configurable setting — it never appears in `Organization`/`SlicingDefaults`, and there is no
Settings UI for it — it exists purely so the CLI invocation this phase already requires has *something*
concrete to slice against; a slicer cannot run without a material's temperature/retraction/flow settings as
input. For every other format — unsliced 3MF (§13), sliced 3MF/G-code (§1) — a real material is always
resolved from the file itself; there is no "no material was supplied" branch left to handle for them, since
nothing is ever supplied by the operator in the first place.

**`PartFilamentRequirement.MaterialType` becomes `string?`** (was a required `string`) specifically to
represent this — `null` means "fully unconstrained, matches any spool of any material," a strictly broader
claim than a real generic type like `"PLA"` (which still requires PLA specifically). This can now only ever
arise from an STL; every other format always has a real, non-null `MaterialType` by the time it reaches
`ReadyForReview` — an already-sliced file's, since FR-006c rejects an unparseable one outright, and an
unsliced 3MF's, since its own project-level assignment is now required and parsed directly (§13, also
rejecting outright if unparseable) rather than ever falling back to this wildcard.

**No new stored "is this approximate" flag** — whether a part's estimate is a best-effort approximation
(SC-003's named exception) is fully derivable at read time by checking whether any of its filament
requirements has `MaterialType = null`; since an in-app slice always produces exactly one requirement, this
is a single, cheap check, not a query. Storing a redundant boolean alongside data that already implies it
would be exactly the kind of "don't cache what you can derive" case Phase 2's own display-field extraction
already established as this codebase's convention.

**Narrowing still works identically** on an "any material" requirement — the operator can attach a specific
Filament Profile/color-variant to it at Confirm, exactly like any other requirement (FR-012a) — but doing so
does **not** trigger a re-slice; the estimate stays whatever the fallback-material slice originally produced,
approximate regardless of any subsequent narrowing. A fresh, accurate slice for the part's real, eventual
material is Phase 7's job at job-dispatch time (FR-010), not something this phase re-triggers.

**Why an already-sliced file can never use this wildcard**: once a file is sliced, its G-code toolpath has
one material's process parameters (nozzle/bed temperature, retraction, cooling-fan speed, flow rate)
physically baked in — printing a different actual material through it isn't a compatibility nuance, it's a
likely-bad print (poor adhesion, stringing, or a clog) from wrong temperatures. A file requiring slicing has
no such commitment yet — it gets a genuinely fresh slice whenever its real material is eventually known
(now, with a placeholder, or later at Phase 7 dispatch, with the real one) — so only that path can honestly
support deferring the material commitment at all.

## 8. File storage: new `IPartFileStorage` abstraction (local disk); thumbnails stay in Postgres as `bytea`, one row per plate, always normalized to PNG

**Decision**: confirmed via direct search — **no file-storage abstraction exists anywhere in this
codebase today.** Phase 4 introduces `Application.Parts.IPartFileStorage`
(`SaveAsync`/`OpenReadAsync`/`DeleteAsync`, keyed by `organizationId`/`partId`) implemented in
`Infrastructure.Parts` as `LocalDiskPartFileStorage`, writing under a new bind-mounted volume
(`openfarm_part_files:/data/part-files`, added to `infra/docker-compose.yml`), namespaced by
`organizationId` first.

**Thumbnails are now per-plate, not one per file (supersedes this section's previous "first plate only"
decision)**: a sixth follow-up clarification reopened this specifically — both an already-sliced and a
requires-slicing multi-plate file now get one thumbnail derived per plate. Each is still small and bounded,
so `PartThumbnail` (new child table: `PartId`, `PlateIndex`, `Bytes`) and, transiently,
`PartProcessingJobPreviewThumbnail` (`JobId`, `PlateIndex`, `Bytes`) hold one row per plate that successfully
produced a thumbnail — **not** one row per plate that *exists* (a plate with no derivable preview simply has
no row; `Part.PlateCount`/`PartProcessingJob.PreviewPlateCount` — known independently of thumbnail
derivation, from the same metadata/project-settings scan that already produces plate count for
filament-requirement grouping, §7/§13 — tells the frontend how many slots to render, filling in from
whichever `PlateIndex` rows actually exist). A child table, not a `jsonb` array of byte arrays, is the right
shape here specifically **because** the payload is binary — `jsonb` would force a second base64 encoding on
top of the one already used for API transport (§ below), for no benefit, unlike `PreviewFilamentRequirements`
(§3a), which stays `jsonb` precisely because *that* data is small structured text, not binary. The Part's
single catalog/list-view thumbnail is `PlateIndex = 0`'s row (a plain filtered read, not a denormalized
duplicate column — at the pagination-bounded row counts SC-005 already established as fine for this
feature, "cache what you can derive" would be premature here the same way it was rejected for the "is this
approximate" flag in §7a).

**No separate content-type/format column is needed**: every thumbnail source (a slicer CLI's per-plate
preview export, a G-code comment's base64 preview, a Bambu 3MF's per-plate embedded PNG) is normalized to
PNG at ingestion time, so any response that includes one can always treat it as PNG unconditionally.

**Thumbnails are served as inline base64 in JSON/SignalR payloads (`thumbnailBase64` per plate entry), never
as a bare authenticated byte-stream URL.** This app authenticates purely via JWT Bearer tokens (no cookies,
confirmed by the SignalR client's `withCredentials: false`) — a plain `<img src="...">` cannot attach an
`Authorization` header, so a naive `GET /parts/{id}/thumbnail`-style endpoint returning raw bytes would not
actually be renderable by the browser without extra client-side plumbing. This codebase already has exactly
one precedent for displaying an authenticated image, and it solves this the same way:
`features/printers/components/CameraView.tsx` renders a live camera frame via
`<img src={`data:image/jpeg;base64,${data.frameJpegBase64}`}>`, with the frame's bytes arriving inside an
already-authenticated SignalR payload rather than a second bare-URL fetch. Phase 4 follows the identical
pattern: a `thumbnails: [{ plateIndex, thumbnailBase64 }]` array is embedded directly in `GET /parts` (list,
bounded by `pageSize` — never the full catalog; the list row itself keeps a single `thumbnailBase64` for
just `PlateIndex = 0`, to avoid inlining every plate's image into a paginated list unnecessarily), `GET
/parts/{id}`, `GET /part-processing-jobs/{id}`, and the `PartProcessingJobStatusChanged`/trailing-update
SignalR events themselves (§3b, `contracts/part-library-api.md`) — there is no separate `GET .../thumbnail`
endpoint. An earlier revision of this contract *did* include one; it would not have actually worked as
designed, since nothing about it addressed the Bearer-auth-vs-`<img>` problem — this was caught and
corrected directly by the user reviewing the plan, not discovered independently.

**The part's actual file** (`GET /parts/{id}/file` — potentially large, STL/3MF/G-code) stays a genuine
byte-stream endpoint, unaffected by this — it's never rendered inline by the browser the way a thumbnail
is; a "download" action in the frontend fetches it through the authenticated Orval client as a blob and
triggers a browser download from that blob, the standard pattern for authenticated binary downloads,
rather than a bare hyperlink.

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
  phase adds a small, feature-scoped `PartThumbnail` component. It takes the `thumbnailBase64` string
  already present on whatever payload rendered it (list row, detail, job preview, or the SignalR push
  itself, §8) and renders `<img src={`data:image/png;base64,${thumbnailBase64}`}>` directly — no separate
  fetch, no loading state of its own — with a graceful "no thumbnail" fallback when it's `null` (FR-014a).
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

## 13. An unsliced 3MF's target material is parsed from its own project settings, not supplied by the operator

**Decision**: a new `Application.Parts.IUnslicedProjectMaterialResolver` (§1), implemented in
`Infrastructure.Parts` as `UnslicedProjectMaterialResolver`, opens an unsliced 3MF's project file with the
same `ZipArchive` technique already established (§1) and reads its filament-preset assignment from
`Metadata/project_settings.config` (the OrcaSlicer/Bambu Studio project-settings JSON entry every saved
project contains — distinct from `Metadata/slice_info.config`, which only exists in an *already-sliced*
`.gcode.3mf` and is what `IPartFileClassifier` checks for instead, §1) plus the project's own plate count.
This resolves the generic material type (e.g. `"PETG"`) used as `ISlicerEngine`'s target for that file's
in-app slice (§4) — the operator never supplies one (§2/§7a, `filamentId` removed entirely).

**Why a project file reliably has this**: unlike an STL (pure geometry, no capacity to carry any material
concept), a `.3mf` saved by OrcaSlicer or Bambu Studio always has *some* filament preset assigned to at least
one AMS/extruder slot before it can be saved at all — that's a property of how those tools' save flow works,
not something a user can skip. Treating a missing/unparseable assignment as a genuine failure (FR-006),
rather than silently falling back to the STL-only wildcard (§7a), follows directly from that: an unsliced
3MF missing this data is anomalous (a hand-crafted or corrupted file, not a normal tool export), not the
expected case the STL wildcard exists for.

**Multi-material projects**: if a project's own settings assign more than one distinct material/color across
its slots (rare pre-slice, but the project format permits it the same way a *sliced* file's AMS mapping does,
§7), `IUnslicedProjectMaterialResolver` still resolves and reports only a single target material for the
in-app slice itself — true multi-material *slicing* stays out of scope for this phase (§7/FR-012, unchanged)
— but surfaces the full detected set for informational display alongside the single slicing target, so an
operator isn't confused about why only one material appears in the resulting single filament requirement.

**Relationship to `IAlreadySlicedFileMetadataParser` (§1)**: a deliberately separate interface, not a shared
one with a flag — the two parse structurally different metadata (project settings vs. embedded G-code/
slice-result data) for a different purpose (choosing a slicing target vs. reporting an already-fixed
result), and only one of them is ever invoked for a given file, decided by `IPartFileClassifier`'s
`requiresSlicing` result. Collapsing them into one interface with an internal branch was considered and
rejected — it would obscure that these read genuinely different parts of a 3MF's zip structure for
genuinely different reasons, the same "don't force an artificial shared abstraction" reasoning already
applied to keeping `IPartFileClassifier` and `IAlreadySlicedFileMetadataParser` themselves separate (§1).
