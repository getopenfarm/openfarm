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
catalog's typed columns, tolerating missing keys (nullable columns) rather than failing the import.

**Testing classification**: fixture-replay, test-after — **not** TDD. This follows the exact precedent
set by `BambuMqttMessageParser` in `specs/002-printer-integration/plan.md` (raw external-format bytes
→ domain shape, tested against real captured payloads, per the constitution's Principle II carve-out
for "the real Bambu LAN client"). Parsing an externally-defined, imperfectly-documented archive format
is the same class of problem — the correctness bar is "does it parse what a real slicer actually
produces," which only real fixture files can prove, not up-front red-green-refactor against an assumed
schema. `Domain`-side logic this feature *does* need TDD for for is narrower: the deterministic
base/override resolution algorithm (§3) and duplicate-name detection, both of which are pure,
well-specified functions over already-parsed data — no external format uncertainty.

## 3. Base/override resolution algorithm

**Decision**: resolution happens in two passes inside `ConfirmProfileImportCommandHandler`'s
transaction, not the parser (the parser's job is only to extract each preset's own declared
`BaseProfileName`, never to resolve it): (1) insert every selected, non-skipped preset as a new
`MachineProfile`/`FilamentProfile` row, keeping an in-memory `Name → Id` map per profile type for rows
created in *this* confirm call; (2) for every inserted row with a non-null `BaseProfileName`, resolve
`BaseProfileId` by checking the in-batch map first (the base was imported alongside it, US2 Scenario 1),
then falling back to a lookup against already-imported catalog rows for the same organization/type
(the base was imported in an *earlier* import), leaving `BaseProfileId` null if neither resolves (US2
Scenario 3 — a built-in slicer default, the expected common case per spec.md's Assumptions).
`BaseProfileName` itself is always stored verbatim regardless of whether resolution succeeds — it's
the durable reference spec.md's FR-005 requires, `BaseProfileId` is just an optimization for the
resolved case. This two-pass approach is a plain, deterministic algorithm — a good TDD candidate,
tested against synthetic in-batch/prior-import/unresolved fixtures rather than needing real slicer
files.

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

## 8. Machine profile ↔ existing `PrinterModelCatalog` cross-reference (best-effort)

**Decision**: `MachineProfile.SupportsLiveIntegration` (spec's Key Entities: "whether the model is one
OpenFarm can live-connect to") is derived, not manually set, by attempting to match the imported
preset's declared printer name/model text against `OpenFarm.Domain.Printers.PrinterModelCatalog`
(built in `specs/002-printer-integration/`) — reusing Phase 1's existing catalog instead of duplicating
model knowledge, per the constitution's Principle IV. **This match is explicitly best-effort**: a
slicer's machine-preset JSON names the printer in free text (e.g. `"Bambu Lab X1 Carbon 0.4 nozzle"`),
not `PrinterModelCatalog`'s exact `(Brand, ModelCode)` pair (e.g. `("Bambu", "X1C")`), so resolution
uses a small normalized substring/alias match rather than requiring an exact key hit; when nothing
matches, `SupportsLiveIntegration` defaults to `false` rather than blocking the import or guessing — a
machine profile importing successfully never depends on this field resolving (FR-002's extraction
requirement doesn't gate on it). Verifying the real preset field name(s) that carry the printer's model
string is part of the same real-sample-fixture task named in §1, since it's the same "confirm against
actual exported JSON" category of unknown. Per §9, this field is computed at read time rather than
cached — so a `PrinterModelCatalog` change (a new supported model added in a later phase) automatically
changes what existing imported machine profiles report, with no backfill needed.

## 9. Descriptive fields (nozzle size, bed volume, material, color, tuned settings, ...) are computed at read time, not stored

**Decision**: none of the descriptive/spec fields spec.md's Key Entities names for either profile
type — nozzle size, bed volume dimensions, enclosure, supported materials, live-integration capability
(machine); material, brand, color, nozzle/bed temp, max volumetric speed, fan percent (filament) — are
persisted as their own typed table columns. `RawDataJson` is the only persisted form of this data; the
`MachineProfile`/`FilamentProfile` tables carry only identity/reference/provenance columns (`Id`,
`OrganizationId`, `Name`, `SourceFormat`, `BaseProfileName`, `BaseProfileId`, `RawDataJson`,
`SourceFileName`, `CreatedAtUtc` — see `data-model.md`). `IProfileBundleParser` exposes the same
per-`SourceFormat` field-extraction logic it needs internally at two call sites: once against a freshly
parsed archive entry (preview, §2), and once against a stored row's `RawDataJson` string (every catalog
list/detail read) — one implementation, reused, never forked into a "parse-time version" and a
"read-time version" that could drift from each other.

**Revision note**: the original design (this file's first draft, and `data-model.md`'s first draft)
promoted these fields to typed columns, mirroring `Printer`'s inline-telemetry-column precedent
(`specs/002-printer-integration/data-model.md`). On review, that precedent doesn't actually transfer:
`Printer`'s inline telemetry columns cache values that arrive continuously from a live MQTT connection
with no other durable record of their own — the column *is* the only copy. Here, every one of these
fields already has a durable, always-current source: `RawDataJson`. Caching a second copy in typed
columns would only ever be a snapshot that a raw-JSON edit (FR-012) could silently invalidate — the
`UpdateMachineProfileRawDataCommand`/`UpdateFilamentProfileRawDataCommand` handlers would need an
explicit "re-derive and write back the cached columns" step, and getting that step wrong (or a future
change forgetting it) would leave the catalog list showing values that don't match what the JSON editor
just saved. Not persisting them removes the failure mode entirely rather than managing it carefully.

**Alternative considered and rejected**: keep the typed columns as a read-performance optimization
(avoid parsing `RawDataJson` on every list request). Rejected because no functional requirement asks
for server-side filtering or sorting by any of these fields — US3 only requires them to be *displayed*
— and re-running a small-JSON field extraction over, at most, a few hundred catalog rows (this feature
has no bulk-scale story; SC-001 scopes a single import to under 50 presets) is not a real performance
concern worth the drift risk. If a later phase adds a real query requirement against one of these
fields (e.g. Phase 7's sliced files matching by nozzle size), promoting that specific field back to an
indexed column at that point is a small, contained migration — exactly the extension point the
constitution's Principle IV asks for, not something to pre-build now.

## Summary of resolved unknowns

| Item | Resolution |
|---|---|
| Bundle container format | zip archive, all 5 supported extensions (confirmed via multiple independent sources, §1) |
| Base/override reference field | `"inherits"`-shaped field in preset JSON (confirmed, §1) |
| `bundle_structure.json` exact schema | Not required for correctness — subfolder-based classification is primary, manifest is optional cross-check (§1); real-sample fixture verification deferred to implementation, not a plan blocker |
| Parser placement/testing strategy | `Infrastructure`, fixture-replay/test-after, mirrors `BambuMqttMessageParser` precedent (§2) |
| Base/override resolution algorithm | Two-pass, TDD, in `ConfirmProfileImportCommandHandler` (§3) |
| Preview/confirm state management | Stateless round-trip, no staging entity; confirm re-uploads the file and re-parses rather than trusting echoed content (§4) |
| Duplicate detection timing | Server-side at preview; only the skip/import-as-new *decision* (not preset content) is carried through the confirm payload (§5) |
| File upload binding | FastEndpoints `AllowFileUploads()` + `IFormFile`, used by both preview and confirm (§6) |
| Bulk activity-log coverage | `IHasEntityIds` extension to the existing post-processor (§7) |
| Live-integration flag | Best-effort match against Phase 1's `PrinterModelCatalog` (§8) |
| Persisting descriptive/spec fields | Not stored — computed at read time from `RawDataJson`, one extraction implementation shared between preview and catalog reads (§9) |
