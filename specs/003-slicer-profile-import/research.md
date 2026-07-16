# Phase 2 Research: Profiles (Machine + Filament, Imported from Slicer Bundles)

Resolves the open technical questions behind spec.md's Assumptions section (which already locked in
the supported-format list and the general bundle shape) and the FastEndpoints/EF/Constitution
mechanics needed to implement it. No `NEEDS CLARIFICATION` markers remain in Technical Context — this
file either confirms a decision with a cited source or explicitly scopes an item as "verify against a
real sample bundle during implementation," the same way `specs/002-printer-integration/research.md`
§4 scoped the camera-protocol question rather than guessing at a precise byte-level spec.

## 1. OrcaSlicer/Bambu Studio bundle shape (confirms spec.md Assumptions, doesn't relitigate them)

**Decision**: Trust spec.md's Assumptions as written — every supported format (`.zip` profile bundle,
`.orca_printer`, `.orca_filament`, `.bbscfg`, `.bbsflmt`) is a zip-format archive containing one or
more preset JSON files, organized under type-named subfolders (`filament/`, `machine/` — `process/`
print-settings presets are out of scope per spec's Key Entities, which only names Machine and Filament
profiles). A parent/base preset reference is carried by an `"inherits"` field inside the preset's own
JSON, naming the parent preset. This is independently confirmed by multiple sources: OrcaSlicer's own
wiki and community docs describe "Export Preset Bundle" producing a `.zip` with `filament`/`machine`/
`process` subfolders of individual JSON files ([OrcaSlicer Wiki: How to create
profiles](https://github.com/OrcaSlicer/OrcaSlicer/wiki/How-to-create-profiles),
[Obico: OrcaSlicer Profile Management](https://www.obico.io/blog/orcaslicer-comprehensive-profile-management-guide/)),
system presets in the shipped profile library use `"inherits": "fdm_process_qidi_x3_common"`-shaped
references ([OrcaSlicer issue #12223](https://github.com/OrcaSlicer/OrcaSlicer/issues/12223)), and
Bambu Studio's `.bbscfg`/`.bbsflmt` exports are documented as "the printer preset, user filament
presets associated with the printer, and user process presets associated with the printer" bundled
together ([Bambu Lab Wiki: Exporting printer, filament, and process
presets](https://wiki.bambulab.com/en/bambu-studio/export-filament)) — the same shared shape spec.md
already asserts, since Bambu Studio and OrcaSlicer share a common lineage (OrcaSlicer is a Bambu
Studio/PrusaSlicer fork).

**Genuinely open item, scoped rather than guessed at**: the *exact* field names/schema of the
`bundle_structure.json` manifest spec.md's Assumptions names are not published anywhere reachable
during this research pass — GitHub code search for that filename requires an authenticated session
this environment doesn't have, and the one PR that introduces a same-named `bundle.json` concept
([OrcaSlicer PR #8858](https://github.com/SoftFever/OrcaSlicer/pull/8858)) is about the *vendor's
shipped system-profile* bundle (an internal app-startup optimization), not the *user-facing* "Export
Preset Bundle" output this feature parses — these are two different files that happen to share a
naming pattern, and conflating them would be a real mistake. **Decision**: the parser (see §2) does
not hard-depend on `bundle_structure.json` for correctness. It classifies presets primarily by their
subfolder (`machine/` vs `filament/`), which every source above agrees on, and treats a
`bundle_structure.json` manifest as an optional cross-check/name-source when present, never a required
input — this is deliberately resilient to the one part of the format this research pass could not
independently verify. **Follow-up task for the implementation phase** (not a plan blocker): obtain 2-3
real exported bundles (one OrcaSlicer, one Bambu Studio, one single-preset `.orca_printer`) and add
them as fixtures, exactly as M0.1's hardware spike captured real MQTT fixtures for Phase 1 — see §2's
testing strategy.

## 2. Where the bundle parser lives and how it's tested

**Decision**: `IProfileBundleParser` is an `Application`-defined contract (`Application/Profiles/`,
mirroring `IPrinterConnector`'s placement), implemented by `ProfileBundleParser` in
`Infrastructure/Profiles/` using the .NET BCL's `System.IO.Compression.ZipArchive` — no new NuGet
package needed, this is a well-understood zip-plus-JSON format, not a binary protocol like Bambu's MQTT
stream. Per-preset field extraction (nozzle size, bed volume, material, color, etc. — the "key
identifying fields" spec.md's US3 requires) is format-aware but table-driven, not one giant
if/else: a small per-`SourceFormat` field-mapping table translates known JSON key names into the
"derived display field" shape (§9) — `nozzleSizeMm`, `bedSizeXMm`, `material`, `colorHex`, etc. —
tolerating missing keys (the field is simply omitted/null in the response) rather than failing the
import. Per §9, none of these fields are persisted columns; this same extraction logic is invoked both
at preview parse time and at every catalog read against a stored row's `RawDataJson`.

**Testing classification**: fixture-replay, test-after — **not** TDD. This follows the exact precedent
set by `BambuMqttMessageParser` in `specs/002-printer-integration/plan.md` (raw external-format bytes
→ domain shape, tested against real captured payloads, per the constitution's Principle II carve-out
for "the real Bambu LAN client"). Parsing an externally-defined, imperfectly-documented archive format
is the same class of problem — the correctness bar is "does it parse what a real slicer actually
produces," which only real fixture files can prove, not up-front red-green-refactor against an assumed
schema. `Domain`-side logic this feature *does* need TDD for is narrower: the deterministic
base/override resolution algorithm (§3) and duplicate-name detection, both of which are pure,
well-specified functions over already-parsed data — no external format uncertainty.

## 3. Base/override resolution algorithm

**Decision**: resolution happens in two passes inside `ConfirmProfileImportCommandHandler`'s
transaction, not the parser (the parser's job is only to extract each preset's own declared
`BaseProfileName`, never to resolve it).

**Pass 1 — insert and index.** Insert every selected, non-skipped preset as a new
`MachineProfile`/`FilamentProfile` row. While inserting, build an in-memory per-profile-type map from
`(SourceFormat, Name)` to "zero, exactly one, or more than one `Id`" for rows created in *this* confirm
call — a plain `Dictionary<(ProfileSourceFormat, string), Guid>` cannot represent this, since a second
insert under the same key would silently overwrite the first with no documented behavior. Concretely: a
`Dictionary<(ProfileSourceFormat, string), Guid?>` works — set `map[(sourceFormat, name)] = id` on the
first insert of that key; on any subsequent insert of the same key, set the entry to `null` as an
ambiguous sentinel and leave it null for all further inserts of that key. This is possible because
FR-009 explicitly permits importing a duplicate-named preset as a separate additional row, and neither
table enforces a DB uniqueness constraint on `Name` (`data-model.md`), so a same-batch name collision is
a real, expected input shape, not a bug. Keying by `(SourceFormat, Name)` rather than `Name` alone (see
§10) is a correctness requirement, not just extra caution: an `"inherits"` reference always names a
preset within that same slicer's own library, so a Bambu Studio preset can never legitimately declare an
OrcaSlicer-named preset as its base.

**Pass 2 — resolve.** For every inserted row with a non-null `BaseProfileName`, resolve `BaseProfileId`
by matching on `(SourceFormat, BaseProfileName)` — the row's own `SourceFormat`, never a different one:
1. Look up `(SourceFormat, BaseProfileName)` in the in-batch map (the base was imported alongside it,
   US2 Scenario 1).
   - Entry present with a non-null `Id` → resolved; `BaseProfileId` = that `Id`. **Stop — do not also
     query the database.**
   - Entry present but `null` (ambiguous — 2+ same-`(SourceFormat, Name)` rows in this batch) →
     **unresolved**. **Stop — do not fall through to a database lookup**: a prior-import row of the same
     name wouldn't disambiguate which in-batch preset was meant, it would only add a third, equally
     arbitrary candidate.
   - No entry (zero in-batch candidates) → continue to step 2.
2. Query already-imported catalog rows for the same organization/type/`SourceFormat` by exact `Name`
   match (the base was imported in an *earlier* import).
   - Exactly one matching row → resolved; `BaseProfileId` = that row's `Id`.
   - Two or more matching rows → **unresolved** — the organization already has 2+ existing profiles
     sharing that `(SourceFormat, Name)`, a legitimate state since FR-009 allows duplicate-name imports,
     and there is no principled rule for picking one over another.
   - Zero matching rows → **unresolved** (US2 Scenario 3 — a built-in slicer default, the expected
     common case per spec.md's Assumptions).

**Cycle reachability**: `BaseProfileId` is immutable once set (no endpoint ever revisits it — FR-012's
raw-JSON editor only overwrites `RawDataJson`) and can only be set to reference a row that already
exists at the moment of resolution, so a cycle spanning two different imports is structurally
impossible — the reference graph only grows forward in time. A same-*batch* cycle (preset A declares
base name B, preset B in the same bundle declares base name A) is the one case not structurally
prevented here: Pass 1 inserts every selected preset before Pass 2 resolves any of them, so if both
names are unambiguous within the batch, Pass 2 would happily resolve A→B and B→A. This is an
adversarial/malformed input shape no real slicer export produces (a slicer's own `"inherits"` handling
enforces a DAG when saving), so Pass 2 does not add extra bookkeeping to reject it — §10's read-side
chain-walk guard is where this is neutralized, cheaply and for every read path at once, rather than
duplicating the check at write time too.

In every unresolved outcome above — no match, or an ambiguous match, from either source — `BaseProfileId`
stays null and `BaseProfileName` is still stored verbatim, exactly the same "unresolved reference is a
normal, expected outcome" behavior FR-005 already establishes for the no-match case. This uniform
treatment is deliberate: from the operator's perspective, an ambiguous match and no match are equally
"the system cannot say with certainty which row was meant," and silently guessing (e.g. picking the
most-recently-created same-named row) would produce a *wrong* link — a failure mode strictly worse than
a missing one, since a wrong link looks correct in the UI and is much harder to notice and correct later.

**Alternative considered and rejected**: an arbitrary but deterministic tie-break (most-recently-created
row, or lexicographically-smallest `Id`, among same-named candidates) was considered, since it resolves
*something* rather than leaving `BaseProfileId` null. Rejected for the reason above — there is no
principled way to infer which same-named row a preset's declared base actually refers to, and a silent
wrong guess is worse than the already-established, visible-by-its-absence "unresolved" state.

**Distinguishing "ambiguous" from "no match at all"**: both collapse to the identical persisted shape
(`BaseProfileName` set, `BaseProfileId` null) — no separate column is added to distinguish them, since
no FR asks for that distinction to be queryable or user-visible, consistent with this feature's general
avoidance of schema surface without a stated requirement (`data-model.md`'s "genuinely small" schema
note). If this distinction is ever needed for debugging, the recommended approach is a structured log
line in `ConfirmProfileImportCommandHandler` at the point an ambiguous match is discarded (candidate
count, profile type, preset name), not a persisted column.

This two-pass approach is still a plain, deterministic algorithm — a good TDD candidate, tested against
synthetic in-batch/prior-import/unresolved/**ambiguous-in-batch/ambiguous-prior-import** fixtures rather
than needing real slicer files.

## 4. Preview/confirm flow — stateless, no server-side staging entity, no client-trusted payload

**Decision**: the review-before-persist step (FR-003/FR-004) is implemented as two independent HTTP
calls with **no server-side staging table** in between, and **confirm never trusts client-supplied
preset content** — only the operator's *selection decision*. `POST /profiles/imports/preview` parses
the uploaded archive and returns the full extracted preset list (all fields, all presets, for the
review-list UI to render) without writing anything to the database. `POST /profiles/imports/confirm`
re-uploads the **same file** alongside a compact list of `(type, name)` keys identifying which presets
the operator selected, plus a duplicate-resolution choice for any flagged as duplicates at preview
time. The confirm handler re-parses the re-uploaded file with the exact same `IProfileBundleParser`
used at preview and persists only the presets whose keys appear in the selection list — every typed
field and every byte of `RawDataJson` written to the database comes from the server's own fresh parse
of the file, never from a client-echoed value.

**Revision note**: an earlier version of this design had confirm accept a full client-echoed copy of
each selected preset's data (every typed field plus `RawDataJson`) instead of re-uploading the file.
That was rejected on review: it meant the server would persist whatever preset content a client
claimed was in the bundle, with no guarantee it actually came from the uploaded archive — a real
integrity gap, not just a style preference, even though the blast radius is limited (an authenticated
org member can already write arbitrary profile JSON via the FR-012 raw-JSON editor after import, so
this wasn't a new *capability*, just an unnecessary loophole for the confirm step specifically). Making
confirm re-parse from the source file instead closes that gap for free and, as a side effect, shrinks
the confirm request payload — the client only ever needs to say *which* presets it wants, never repeat
their content back (see `data-model.md`'s `SelectedProfileKey` shape).

**Alternatives considered and rejected**: a persisted `PendingImportBatch` entity (parse once, store the
extracted-but-unconfirmed presets server-side, reference by an opaque batch id from the confirm call)
was considered. Rejected per the constitution's Principle IV ("Build for Expansion, Not Speculation" —
"designed extension points are required, preemptive abstraction is not") and directly supported by
spec.md's own edge case: "if the operator's session or upload is interrupted partway through parsing a
large bundle, no partial set of profiles should be left in the catalog." A stateless round-trip
satisfies that by construction — there's no server-side state to leak partially — while a staging table
would need its own cleanup/TTL story (abandoned batches, multi-tab conflicts) that nothing else in this
phase needs. Re-parsing the file twice (once at preview, once at confirm) is a second, smaller way to
get the same "no leaked partial state" property without a staging table, and it's cheap: bundles are
capped well under any real size concern (SC-001, under 50 presets), so a second parse pass costs
nothing worth optimizing away.

**Consequence for FR-006** (warn when a selected override's base is present in the bundle but not
selected): this check needs zero backend round-trip — the frontend already holds the full preview
array (every preset's name, type, and declared base name) and the operator's live selection state, so
the warning is computed and rendered client-side as checkboxes change, confirmed in `contracts/` and
`quickstart.md`.

**Consistency check at confirm time**: if the operator re-selects a different file than the one they
previewed (or the same file's content somehow parses differently — shouldn't happen for a static
upload, but the check is cheap insurance), some selected `(type, name)` keys from the browser's
retained preview state may not appear in the freshly re-parsed result. The confirm handler treats this
as a client-input error (422), not a partial best-effort import — silently dropping unmatched
selections would be a surprising, hard-to-notice partial import, worse than a clear rejection asking
the operator to re-preview.

## 5. Duplicate detection (FR-009)

**Decision**: computed server-side during the *preview* call (not confirm), since it requires a
database lookup the frontend can't do on its own — for every parsed preset, the preview handler queries
existing `MachineProfile`/`FilamentProfile` rows in the organization by exact `(Name)` match within the
same profile type, and marks `IsDuplicate: true` on the preview item if found. The operator's
skip/import-as-new *choice* per duplicate is the one piece of preset-level information the confirm
payload carries beyond the bare selection key (§4) — an explicit `DuplicateAction` enum attached to
that preset's key, not re-derived at confirm time. This is a deliberate exception to "confirm never
carries preset content, only selection" (§4): a decision the operator already made isn't content to
re-derive, and re-checking duplicate status fresh at confirm time would risk a different verdict than
what the operator actually saw and decided against (e.g. someone else on the team imported the same
name in between).

## 6. File upload mechanics (FastEndpoints)

**Decision**: both `POST /api/v1/profiles/imports/preview` **and** `POST
/api/v1/profiles/imports/confirm` (per §4's revised design) are FastEndpoints endpoints with
`AllowFileUploads()` in `Configure()` and a request DTO exposing a single `IFormFile File` property —
FastEndpoints binds `multipart/form-data` uploads to `IFormFile`-typed request properties directly,
confirmed against the framework's own file-handling docs and demo repo
([FastEndpoints: File Handling](https://fast-endpoints.com/docs/file-handling),
[FastEndpoints/File-Handling-Demo](https://github.com/FastEndpoints/File-Handling-Demo)). Confirm's
request DTO adds one more bound field alongside `File`: a `Selections` property (a JSON-encoded string
form field, deserialized manually in the handler — FastEndpoints multipart binding covers the file
part, not nested JSON structures, so the selection list travels as a plain string field rather than a
second exotic binding). A bundle is a small file (SC-001's "typical bundle" scope, dozens of presets,
not gigabytes), so the simple auto-buffered `IFormFile` binding is sufficient for both endpoints — no
need for the streaming `AllowFileUploads(dontAutoBindFormData: true)` + `FormFileSectionsAsync()` path
FastEndpoints offers for large uploads. `MaxRequestBodySize()` is set to a conservative cap (e.g. 20
MB) generous for any realistic slicer bundle while still bounding worst-case memory use from an
oversized/malicious upload — applied to both endpoints identically, since confirm receives the same
file preview did.

## 7. Extension to the activity-log post-processor for bulk-created entities

**Decision**: `ActivityLogPostProcessor` (`api/src/OpenFarm.Api/Processors/ActivityLogPostProcessor.cs`)
currently assumes exactly one entity is created per state-mutating request — it reflects a single `Id`
property off the response DTO. The confirm-import endpoint is the first case in this codebase where one
HTTP call legitimately creates *many* entities (every selected preset in one request). Rather than
having `ConfirmProfileImportCommandHandler` call `IActivityLogWriter` directly and bypass the
post-processor (which would quietly reopen the exact gap the constitution's Security section calls out
— "structural, not a per-feature convention that can be forgotten"), the post-processor gains one small,
generic extension: a response DTO may instead implement a new `IHasEntityIds { IReadOnlyList<string>
EntityIds }` marker; when present, the post-processor writes one `ActivityLogEntry` per id (same
`EntityType`/`EventType` from `IActivityLoggable`, same actor-extraction logic already there) instead of
its existing single-`Id`-reflection path. This keeps every profile creation logged structurally, exactly
like every other state-mutating command, without special-casing this one endpoint's logging outside the
shared pipeline. `RegisterPrinterEndpoint`, `MarkPrinterReadyEndpoint`, etc. are entirely unaffected —
the existing single-`Id` path stays the default and is checked first.

**Performance note (accepted, non-blocking)**: `EfActivityLogWriter.WriteAsync` (the existing
implementation of `IActivityLogWriter`) does one `Add` + one `SaveChangesAsync` per call, so this
extension means confirm-import performs one activity-log DB round-trip per created profile, on top of
the profile inserts themselves. Accepted at this feature's scale (SC-001 caps a single import at under
50 presets); worth a code comment at the loop site for anyone revisiting this if the scale assumption
ever changes, not a design blocker now.

## 8. Machine profile ↔ existing `PrinterModelCatalog` cross-reference — considered, cut for this phase

**Decision**: `MachineProfile` does **not** carry a `SupportsLiveIntegration` field (or equivalent) in
this phase — the idea was considered and explicitly cut, not deferred-by-omission.

spec.md's Key Entities section names "whether the model is one OpenFarm can live-connect to" as an
attribute of a Machine Profile. The design originally explored here derived it, not manually set, by
matching the imported preset's declared printer name/model text against
`OpenFarm.Domain.Printers.PrinterModelCatalog` (built in `specs/002-printer-integration/`) — reusing
Phase 1's existing catalog instead of duplicating model knowledge, per the constitution's Principle IV.
That match would have been explicitly best-effort: a slicer's machine-preset JSON names the printer in
free text (e.g. `"Bambu Lab X1 Carbon 0.4 nozzle"`), not `PrinterModelCatalog`'s exact `(Brand,
ModelCode)` pair (e.g. `("Bambu", "X1C")`), so resolution would have needed a normalized
substring/alias match rather than an exact key hit.

**Why cut rather than built**: unlike every other field this design kept, this one is backed by no
numbered FR or SC — only by Key Entities' descriptive text — and its source data was never verified: the
real preset JSON field(s) that carry a printer's model string are part of the same
"confirm against actual exported JSON" unknown named in §1's real-sample-fixture follow-up task, i.e.
this feature would have been building fuzzy-matching logic against a field whose existence in real
bundles hadn't been confirmed. Given this phase's design elsewhere aggressively cuts anything not backed
by a stated FR/SC (§9's derived-fields decision, the `SourceFileName` cut in `data-model.md`), keeping
this one field as an exception — the single field with no FR/SC behind it *and* an unverified source —
would have been inconsistent with that standard. It can be reintroduced in a later phase once real
sample bundles confirm the relevant preset field actually exists and is reliable enough to match on;
nothing in this phase's schema blocks adding it then (the same "derived at read time, not persisted"
pattern §9 establishes would still apply, so adding it later needs no migration).

## 9. Descriptive fields (nozzle size, bed volume, material, color, tuned settings, ...) are computed at read time, not stored — and from a chain, not one row alone

**Decision**: none of the descriptive/spec fields spec.md's Key Entities names for either profile
type — nozzle size, bed volume dimensions, enclosure, supported materials (machine); material, brand,
color, nozzle/bed temp, max volumetric speed, fan percent (filament) — are persisted as their own typed
table columns. (Key Entities also names a live-integration-capability attribute for machine profiles;
that one is cut entirely rather than derived — see §8.) `RawDataJson` is the only persisted form of this
data; the `MachineProfile`/`FilamentProfile` tables carry only identity/reference/provenance columns
(`Id`, `OrganizationId`, `Name`, `SourceFormat`, `BaseProfileName`, `BaseProfileId`, `RawDataJson`,
`CreatedAtUtc` — see `data-model.md`).

**Revised (§10 supersedes this section's original single-row framing)**: computing these fields from
*only the target row's own* `RawDataJson` is **not sufficient**, and was a real design gap, not just an
approximation. Real research against OrcaSlicer/Bambu Studio's actual export behavior (source code, a
real multi-level fixture chain of exported presets, and multiple GitHub issue reports — see §10)
confirmed that exported presets are **delta-only**: a preset with a declared base carries only the
fields it overrides, not the full effective settings. A color-variant filament override like "Bambu PLA
Basic – Red" typically has only its color (and maybe flow ratio) in its own `RawDataJson` — no nozzle
temp, bed temp, max volumetric speed, or fan percent, because those live only in whatever it inherits
from. Reading one row's `RawDataJson` alone would show these fields blank for that profile **even when
its base preset is imported right alongside it in the same bundle** — spec.md's own canonical example
(US2 Scenario 1). §10 replaces single-row extraction with a **chain-walk merge**: derived fields are
computed from the *resolved ancestor chain's* merged `RawDataJson`, not one row.

`IProfileBundleParser`'s per-`SourceFormat` field-extraction (`ExtractMachineDisplayFields`/
`ExtractFilamentDisplayFields`) is unchanged in shape and still the single implementation used
everywhere fields are derived — what changes is what gets fed into it: a merged JSON string (§10's
`ProfileRawDataMerger`) instead of one row's raw `RawDataJson` directly. `SourceFormat` is still not
derivable from `RawDataJson` alone (`data-model.md`) — the merged chain is guaranteed single-format
throughout by §3's `(SourceFormat, Name)`-scoped resolution, so the target row's own `SourceFormat`
column remains the one value ever passed to extraction, at every call site.

**Revision note**: the original design (this file's first draft, and `data-model.md`'s first draft)
promoted these fields to typed columns, mirroring `Printer`'s inline-telemetry-column precedent
(`specs/002-printer-integration/data-model.md`). On review, that precedent doesn't actually transfer:
`Printer`'s inline telemetry columns cache values that arrive continuously from a live MQTT connection
with no other durable record of their own — the column *is* the only copy. Here, every one of these
fields already has a durable, always-current source: `RawDataJson` (now understood to mean the resolved
chain's `RawDataJson`, not necessarily the target row's alone). Caching a second copy in typed columns
would only ever be a snapshot that a raw-JSON edit (FR-012) could silently invalidate — the
`UpdateMachineProfileRawDataCommand`/`UpdateFilamentProfileRawDataCommand` handlers would need an
explicit "re-derive and write back the cached columns" step, and getting that step wrong (or a future
change forgetting it) would leave the catalog list showing values that don't match what the JSON editor
just saved. Not persisting them removes the failure mode entirely rather than managing it carefully. The
chain-walk design in §10 sharpens this further, not weakens it: because nothing in the chain is ever
cached, editing a *base* profile's raw JSON via FR-012 takes effect in every descendant's displayed
fields on the very next read, with no propagation logic anywhere — "never cache" now pays off across a
whole chain, not just one row.

**Alternative considered and rejected**: keep the typed columns as a read-performance optimization
(avoid parsing `RawDataJson` on every list request). Rejected because no functional requirement asks
for server-side filtering or sorting by any of these fields — US3 only requires them to be *displayed*
— and re-running a small-JSON field extraction over, at most, a few hundred catalog rows (this feature
has no bulk-scale story; SC-001 scopes a single import to under 50 presets) is not a real performance
concern worth the drift risk. If a later phase adds a real query requirement against one of these
fields (e.g. Phase 7's sliced files matching by nozzle size), promoting that specific field back to an
indexed column at that point is a small, contained migration — exactly the extension point the
constitution's Principle IV asks for, not something to pre-build now.

## 10. Chain-walk merge — closing the delta-export gap without seeding default profiles

**The problem** (see §9's revision): OrcaSlicer/Bambu Studio export presets as deltas. Confirmed via
convergent evidence: OrcaSlicer's `Preset::save()` (`libslic3r/Preset.cpp`) only writes
`config.diff(*parent_config)` when a parent exists (marked `//BBS: only save difference if it has
parent` — this mechanism originates in Bambu Studio's own codebase, which OrcaSlicer forked, so it
applies to `.bbscfg`/`.bbsflmt` too, not just OrcaSlicer's own formats); a real public fixture repo of
exported presets (`DRIgnazGortngschirl/bambulab-studio-orca-slicer-presets`) shows a 3-level chain of
actual filament presets where none of the levels carry nozzle temp, bed temp, or max volumetric speed —
those values exist only in the vendor's shipped `fdm_filament_pla.json`/`fdm_filament_common.json`
resource files, which are bundled with the *application*, never exported by a user; and multiple
OrcaSlicer GitHub issues (#7462, #12223) report hard failures ("can not find parent for config...")
whose only coherent explanation is that shared/exported bundles are themselves delta-based.

**The decision (not seeding default profiles)**: OpenFarm still does not seed or vendor any built-in
slicer profile data. spec.md's Assumptions already commit to "import-your-own-bundle only," and vendoring
even a narrow "common bases" subset of OrcaSlicer's shipped resources would reopen that position for an
ongoing upstream-sync maintenance burden this phase currently has none of — the same reasoning §8
already applied to cut `SupportsLiveIntegration`. Instead, the fix is entirely on the *read* side:
compute derived display fields from the full resolved ancestor chain, not one row, so the dominant real
case (base imported alongside its override, spec's own US2 Scenario 1) resolves completely without ever
touching a vendor default. Only a genuinely unresolved built-in root (never imported by anyone) leaves
the merge incomplete for whatever that true root would have supplied — a narrower, more honestly
signalled residual limitation (`hasUnresolvedBaseInChain`, below) than silently showing blank fields with
no explanation.

**Merge utility**: `Application/Profiles/ProfileRawDataMerger.cs` —
```csharp
public static class ProfileRawDataMerger
{
    // rawDataJsonAncestorFirst: oldest resolved ancestor first, target profile's own RawDataJson last.
    // Each element's top-level keys overwrite the accumulator's (shallow overwrite, matching the flat
    // key shape real preset JSON actually uses) — the last element always wins on any conflict.
    public static string MergeAncestorChain(IReadOnlyList<string> rawDataJsonAncestorFirst);
}
```
Kept deliberately separate from `ExtractMachineDisplayFields`/`ExtractFilamentDisplayFields`, which stay
unchanged and are simply called once on the merge's output:
`parser.ExtractMachineDisplayFields(ProfileRawDataMerger.MergeAncestorChain(chain), row.SourceFormat)`.
Merging is format-agnostic (pure key-overwrite); mapping known keys to typed fields is format-specific —
conflating them would force a per-ancestor `SourceFormat` parameter that §3's `(SourceFormat, Name)`
resolution tightening makes unnecessary (every row in a resolved chain is guaranteed one format). Placed
in `Application`, not `Domain` — matches this codebase's existing precedent of putting deterministic,
well-specified algorithms in `Application` (§3's two-pass resolution is the same class of logic, also in
`Application`) rather than introducing this feature's first `Domain` static-service class. **TDD**, same
bar as §3: pure synthetic JSON fixtures, no real slicer files needed, since the merge itself doesn't
know or care about format-specific key names. Defensive: if an ancestor's `RawDataJson` somehow fails to
parse (shouldn't happen — `data-model.md` guarantees well-formed JSON — but this is a shared read path
hit by every catalog view), skip that level with a structured log rather than failing the whole request,
matching the parser's existing "tolerate, don't fail the whole operation" philosophy (§2).

**Where the chain is fetched from — no N+1, no recursive CTE**:
- **List** (`GetMachineProfilesQuery`/`GetFilamentProfilesQuery`): both already call
  `repository.ListAsync(ct)` to build the list response. Build one `Dictionary<Guid, Row>` from that
  *same* already-fetched set and walk `BaseProfileId` purely in memory per row — zero additional DB
  round trips. This works because every possible ancestor is, by `data-model.md`'s own FK scoping rule,
  another row of the same table/org — already inside the one `ListAsync` result set.
- **Detail** (`GetMachineProfileQuery`/`GetFilamentProfileQuery`): sequential bounded fetch-by-id up the
  chain via the existing `GetByIdAsync(Guid, ct)`, reversed to ancestor-first before merging. Not an
  N+1 pattern — one request resolving one bounded chain, not per-row-of-a-list; loading an entire
  catalog just to resolve one detail view would be wasteful in the other direction.
- **Not a recursive CTE**: the constitution's Principle V mandates one for BOM decomposition
  specifically because of its stated *unlimited-depth, DB-layer* scale story. Profiles has no such story
  — SC-001 caps an import at under 50 presets, real preset hierarchies are 2–4 levels deep, and the
  list endpoint's full row set is already loaded regardless of how the walk is done. Reaching for `WITH
  RECURSIVE` here would be over-engineering relative to Principle V's actual purpose (avoiding repeated
  round trips / unbounded per-row DB cost), not required by it — a deliberate scoping call, not an
  oversight next to the BOM example. Cumulative catalog scale (many imports over an org's lifetime, as
  opposed to SC-001's per-import cap) stays trivial for an in-memory `O(n × depth)` walk over small JSON
  blobs at any realistic size; true unbounded-catalog scale, if it ever became a real concern, is a
  distinct future question this change doesn't need to anticipate.

**Cycle/depth safety**: per §3's cycle-reachability note, a same-batch mutual reference is the one
adversarial input shape not structurally prevented at write time. Guarded at the **read** side instead
(both walk implementations above): a `HashSet<Guid>` of visited ids, `visited.Add(current.Id)` as the
loop continuation check, plus a generous `MaxChainDepth` constant (e.g. 32) as cheap belt-and-suspenders.
On cutoff, merge whatever ancestors were collected before the cutoff (still strictly better than the
pre-fix zero-ancestor baseline) and set `hasUnresolvedBaseInChain` to `true` (correctness beyond the
cutoff is unknown). Proportionate, not paranoid: a few lines in a loop the walk needs anyway, versus a
shared read path that could otherwise hang on adversarial input; not duplicated at write time too, since
the read-side guard alone already makes every read safe.

**`hasUnresolvedBaseInChain: boolean`** — added to every catalog response (machine/filament, list and
detail) and to preview response items (below). Computed during the same chain-walk: `true` iff the walk
terminates *at any level* (not just the profile's own immediate base — a resolved immediate base whose
own ancestor chain terminates unresolved still counts) at a row whose declared base name is non-null but
didn't resolve, or at the cycle/depth cutoff. `false` only when the walk reaches a row that genuinely
declares no base at all — the merge is complete by construction, nothing is missing. No new persisted
column — computed read-time-only, the same principle as every other derived field.

**Preview needs the identical fix, name-matched instead of `Id`-matched**: preview computes
`ParsedMachinePreset`/`ParsedFilamentPreset`'s derived fields from one just-parsed preset in isolation —
the same flaw. Fixing only the confirmed-catalog side would leave the review list (FR-003) showing
incomplete data right up until the moment of confirm, undermining the honesty this whole fix is for.
Preview has no persisted `Id`s to walk via `BaseProfileId`, so it reuses the same "check same-batch, then
check prior imports" shape §5's duplicate detection already establishes at preview time — not new
machinery, the same pattern applied to base-chain resolution instead of duplicate-name detection:
1. Look up the declared base name, scoped by `(SourceFormat, Name)`, among sibling presets already
   parsed from the *same* bundle. Present and unambiguous → use that sibling's own `RawDataJson` as the
   next link (and recurse — that sibling may itself have a resolved or unresolved base).
2. Not found in-batch → query prior-imported catalog rows by `(SourceFormat, Name)`. Exactly one match →
   fetch that row's real ancestor chain (the detail-endpoint's fetch-by-id walk, above) and merge it in.
3. Neither → unresolved; `hasUnresolvedBaseInChain: true` on the preview item.

Preview's preset extraction gains the same `ProfileRawDataMerger.MergeAncestorChain` call, fed by a
name-matched chain instead of an `Id`-matched one. The same cycle guard applies — a same-batch mutual
name reference is exactly as reachable here as at confirm time, so the same visited-set/depth-cap
approach is reused rather than a new one invented for preview specifically.

**Chain topology is always the persisted columns, never `RawDataJson`'s own embedded reference**: a
row's `RawDataJson` may itself contain a format-native `inherits`-shaped key — that's literally the field
the parser extracts `BaseProfileName` from at import time. After a raw-JSON edit (FR-012), that embedded
value and the persisted `BaseProfileId`/`BaseProfileName` columns could disagree, since the update
handlers never revisit them (`data-model.md`). The walk must **always** use the persisted columns (or,
at preview time, the name-matching above) — never re-parse `RawDataJson`'s own embedded reference to
decide topology. Worth stating explicitly now that a real chain-walk exists to get this wrong in.

## 11. Filament preset → compatible printer(s) — a second declared relationship (FR-016)

**Confirmed real, not inferred**: OrcaSlicer/Bambu Studio filament (and process) presets carry an actual
`compatible_printers: string[]` field in their own JSON — an explicit list of printer preset *names* the
preset declares itself usable with. An empty array (or the key absent entirely) means the preset is
compatible with every printer, per the source format's own convention — this is confirmed product
behavior, not a gap to fill in: Bambu Studio's own AMS-mapping/nozzle-compatibility gate reads exactly
this kind of declared data before allowing a print
([Bambu Lab Wiki: Use AMS on Bambu Studio](https://wiki.bambulab.com/en/software/bambu-studio/use-ams-on-bambu-studio)).
A real, documented OrcaSlicer bug writes the key as `compatible_prints` (missing the trailing "er")
instead of `compatible_printers` in some versions, which silently hides the affected preset from the UI
([OrcaSlicer issue #12193](https://github.com/OrcaSlicer/OrcaSlicer/issues/12193)) — the parser accepts
**both** key spellings defensively, matching §2's "tolerate the real format, don't assume a clean schema"
philosophy.

**Name-suffix fallback, precisely scoped**: when a preset's `compatible_printers`/`compatible_prints` is
empty *and* the preset has no declared base (`BaseProfileName` is null — a "root" preset in OrcaSlicer's
own terminology), OrcaSlicer itself infers compatibility from the preset's own name — the convention
`<filament> @ <printer name>` (e.g. `"Bambu PLA Basic @ Bambu X1 Carbon"`) exists specifically so this
inference works, confirmed both by OrcaSlicer's own preset-naming documentation
([OrcaSlicer Wiki: How to create profiles](https://github.com/OrcaSlicer/OrcaSlicer/wiki/How-to-create-profiles))
and by a description of Bambu Studio's own matching logic checking "whether the full printer preset name
... appears inside the preset's name field." This fallback is conditioned on the *target row's own*
`BaseProfileName` being null — a preset's `Name` (data-model.md) is always its own real column, never
merged/inherited, so parsing the "@" suffix out of it is unambiguous regardless of chain resolution. When
`BaseProfileName` *is* set (a non-root/derived preset), no fallback applies — an empty
`compatible_printers` at that level is left to resolve through the ordinary chain merge below, same as
any other field.

**No new merge machinery — this reuses §10's chain-walk exactly**: `compatible_printers` is just one more
key in a preset's JSON, subject to the same delta-export behavior every other field already gets (§9's
revision, §10) — a derived preset's own JSON may omit `compatible_printers` entirely, inheriting whatever
its base declared. `ProfileRawDataMerger.MergeAncestorChain`'s existing shallow key-overwrite merge
already produces the correct effective value with zero changes; the per-`SourceFormat` field-mapping
table (§2) just gains one more extraction target (`compatiblePrinterNames`, tolerant of both key
spellings, defaulting to `[]` when absent at every level, applying the name-suffix fallback only for the
target row's own `BaseProfileName == null` case described above) — computed via the exact call sites §9/
§10 already established (preview and every catalog read), nothing new to fetch or wire up.

**Deliberately not resolved to a `MachineProfile.Id` this feature**: unlike `BaseProfileName`/
`BaseProfileId`, `compatiblePrinterNames` is stored and shown as plain declared text only — no resolution
step against the `MachineProfile` catalog, no `machineProfileId`-shaped field anywhere in the response.
This was explicitly considered and cut, not an oversight: the resolution design (matching each name
against the catalog by `(SourceFormat, Name)`, same tri-state as base resolution) would have worked
mechanically, but it introduces a real asymmetry worth avoiding for this phase — a `machineProfileId`
would need to be a real, already-persisted `MachineProfile.Id`, so it could only ever resolve against
*prior* imports, never against a same-bundle sibling machine preset still unconfirmed in the same preview
response — even though that's the common case spec.md's own Acceptance Scenario 6 describes (one printer
preset bundled with several compatible filament presets). A resolution field that's usually null right
where the operator would expect it to work is worse than not having the field at all. Extracting and
showing the declared names (§ decision above) already satisfies FR-016 and US3's acceptance scenario in
full; resolving those names to actual catalog records is deferred, not required now.

**API shape**: filament profile responses (preview and catalog, list and detail) gain
`compatiblePrinterNames: string[]` — the raw declared/inferred names, post-merge, shown as plain text. An
empty array means "compatible with all printers," per the source format's own convention — surfaced
as-is, not specially flagged, since it's an explicit, meaningful declaration rather than a gap.

**Machine profiles don't get a reciprocal field this phase**: `compatible_printers` is a
filament/process-preset concept in the source format — a machine/printer preset has no equivalent
"compatible filaments" declaration of its own to extract (a machine preset already *is* the description
of one printer model; there's nothing on that side needing a separate compatibility list). A "which
filament profiles declare compatibility with this machine profile" reverse lookup is a reasonable future
UI convenience but isn't required by FR-016 or any acceptance scenario — not built now, consistent with
this feature's standing rule of not building past what's actually asked for (Principle IV).

## Summary of resolved unknowns

| Item | Resolution |
|---|---|
| Bundle container format | zip archive, all 5 supported extensions (confirmed via multiple independent sources, §1) |
| Base/override reference field | `"inherits"`-shaped field in preset JSON (confirmed, §1) |
| `bundle_structure.json` exact schema | Not required for correctness — subfolder-based classification is primary, manifest is optional cross-check (§1); real-sample fixture verification deferred to implementation, not a plan blocker |
| Parser placement/testing strategy | `Infrastructure`, fixture-replay/test-after, mirrors `BambuMqttMessageParser` precedent (§2) |
| Base/override resolution algorithm | Two-pass, TDD, in `ConfirmProfileImportCommandHandler`; matches scoped by `(SourceFormat, Name)`; ambiguous matches (in-batch or prior-import) treated as unresolved, same as no match (§3) |
| Preview/confirm state management | Stateless round-trip, no staging entity; confirm re-uploads the file and re-parses rather than trusting echoed content (§4) |
| Duplicate detection timing | Server-side at preview; only the skip/import-as-new *decision* (not preset content) is carried through the confirm payload (§5) |
| File upload binding | FastEndpoints `AllowFileUploads()` + `IFormFile`, used by both preview and confirm (§6) |
| Bulk activity-log coverage | `IHasEntityIds` extension to the existing post-processor (§7) |
| Live-integration flag | Cut entirely for this phase — no FR/SC backs it and its source field is unverified (§8) |
| Persisting descriptive/spec fields | Not stored — computed at read time (§9), and, since real exported presets are delta-only, from the *resolved ancestor chain's* merged `RawDataJson` rather than one row alone, both at preview and post-confirm (§10) |
| Delta-export gap (custom profile with "nothing to reference") | Confirmed real via source code + a real fixture chain + GitHub issues: exports are delta-only. Fixed by a read-time chain-walk merge, not by seeding default slicer profiles — the latter was considered and rejected (§10) |
| Filament preset → compatible printer(s) (FR-016) | Real `compatible_printers`/buggy `compatible_prints` field, plus a name-suffix fallback for root presets with neither a base nor a declared list; reuses §10's chain-walk merge as-is; stored/shown as declared text only — deliberately not resolved against the machine-profile catalog this feature (§11) |
