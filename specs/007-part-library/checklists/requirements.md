# Specification Quality Checklist: Phase 4 — Part Library (with Sliced Files & In-App Slicing)

**Purpose**: Validate specification completeness and quality before proceeding to planning
**Created**: 2026-07-19
**Feature**: [spec.md](../spec.md)

## Content Quality

- [x] No implementation details (languages, frameworks, APIs)
- [x] Focused on user value and business needs
- [x] Written for non-technical stakeholders
- [x] All mandatory sections completed

## Requirement Completeness

- [x] No [NEEDS CLARIFICATION] markers remain
- [x] Requirements are testable and unambiguous
- [x] Success criteria are measurable
- [x] Success criteria are technology-agnostic (no implementation details)
- [x] All acceptance scenarios are defined
- [x] Edge cases are identified
- [x] Scope is clearly bounded
- [x] Dependencies and assumptions identified

## Feature Readiness

- [x] All functional requirements have clear acceptance criteria
- [x] User scenarios cover primary flows
- [x] Feature meets measurable outcomes defined in Success Criteria
- [x] No implementation details leak into specification

## Notes

- Updated after a first `/speckit-clarify` pass (2026-07-19, 3 formal questions + 1 direct-instruction
  descope) — Sliced File matching became generic-material/nozzle-diameter driven (parsed from the
  file itself) with optional narrower catalog overrides, rather than requiring a direct link to one
  specific Machine Profile. Sliced-file staleness tracking on source-model revision was descoped
  from this phase entirely per explicit user instruction.
- Updated after a second `/speckit-clarify` pass (2026-07-19, 3 more formal questions) that merged
  the former Phase 23 — In-app slicing into this phase, per explicit user instruction, and updated
  `docs/FEATURE_PLAN.md` accordingly (Phase 23 merged into Phase 4; former Phase 24 renumbered to
  Phase 23). Added: automatic in-app slicing at source-model upload (organization-configurable
  slicer engine + defaults, per-part override, async execution, failure fallback), manual sliced-file
  upload retained alongside it, and a forward-compatibility note that Phase 7 (Jobs) will reuse the
  same in-app-slicing capability on-demand at job-dispatch time.
- Naming specific slicer engines (OrcaSlicer, Bambu Studio) is treated as a domain-level fact — which
  slicer software an operator's automatic slicing runs, directly affecting supported printer
  brands/output compatibility — not an internal implementation/tech-stack detail, consistent with how
  the spec already names G-code/3MF as domain file formats.
- Follow-up clarification (same day, conversational, not a formal `/speckit-clarify` invocation):
  confirmed source-model replacement is direct/destructive (no versioned file history) and that a
  queued/printing job (Phase 7) is protected from a source-model revision by Phase 7's own
  sliced-file locking design, not by anything this phase builds — documented explicitly in
  Assumptions and Clarifications rather than left implicit.
- Follow-up clarification (same day): an intermediate answer treated "source model" as an optional
  field distinct from "Sliced File." That framing was itself superseded: **there is no "source model"
  concept at all.** A Part simply holds a File, determined at upload to either require slicing or not
  (STL always requires it, G-code never does, 3MF is content-inspected). The spec was rewritten around
  this single unified File model — every FR, User Story, edge case, entity, and success criterion
  previously phrased around "source model" vs. "Sliced File" was reworded to "a file that requires
  slicing" vs. "an already-sliced file."
- Follow-up clarification (same day, final on this topic): that pass initially allowed a Part to hold
  a flat *collection* of Files (e.g. multiple already-sliced variants, one per nozzle/diameter
  pairing). That was superseded once more: **a Part holds strictly one file, period** — since Assembly
  (Phase 6) already exists to combine multiple parts, a Part itself stays atomic. Uploading a new file
  always replaces the current one; running the same design against a different nozzle/filament-diameter
  target requires a separate Part record, not a second file on the same one. A 3MF bundling multiple
  plates internally is an accepted, unaddressed limitation, not solved in this phase. The entire spec
  and the relevant parts of `docs/FEATURE_PLAN.md` (Phase 4 and Phase 7's job-locking description) were
  rewritten to reflect single-file cardinality — FR count dropped from 25 to 23 as the
  multiple-files-per-part machinery (old FR-016/018/019) was removed.
- Follow-up clarification (same day, final): confirmed that automatic in-app slicing of an STL/3MF
  **never persists a sliced-output file** — it runs purely to derive nozzle diameter, filament
  diameter, generic material type, and estimated print time/weight, stored directly on the Part, then
  discards the actual slice result; the part's file stays exactly the STL/3MF as uploaded. A sliced
  file is only ever stored when the operator directly uploads one (G-code, or an already-sliced 3MF).
  This also redefined "job-ready" to mean "derived compatibility/estimate data exists," not "an
  actual sliceable file is in storage" — an STL/3MF-derived part is job-ready once auto-sliced, even
  though Phase 7 will still need to invoke in-app slicing again on-demand at job-dispatch time to get
  real toolpath data for it. FR-013/FR-014 (parse nozzle/diameter/material) now apply only to
  already-sliced uploads; the old cache/reuse FR was dropped (nothing persists to reuse within this
  phase). `docs/FEATURE_PLAN.md`'s Phase 4 and Phase 7 sections were updated to match — Phase 7's
  job-locking description now distinguishes "job-ready" from "has an actual sliceable file in hand."
- Follow-up clarification (same day, final round — 4 direct corrections, no formal questions asked
  since each was already decisive): (1) **dropped the optional Machine Profile link entirely** — a
  part's compatibility is expressed purely through derived nozzle/filament diameter; the narrower
  Filament Profile/color-variant override is unaffected and stays. (2) **removed all manual-estimate
  fallback** — an unparseable already-sliced file, or a file requiring slicing with no material
  available, now rejects the add/replace outright rather than saving a part with placeholder data.
  (3) **dropped the "job-ready" concept entirely** — every saved part is complete and usable by
  construction (nothing incomplete is ever saved), so there's nothing left to flag as not-ready; the
  only remaining distinction is whether a part's file still requires slicing (Phase 7 re-slices it at
  job-dispatch time) or was already sliced on upload. Also introduced **automatic thumbnail
  derivation** (new FR-014/FR-014a) — rendered from the model for a file requiring slicing, or
  extracted from the file's own embedded preview image for an already-sliced one (a standard
  slicer/firmware convention); best-effort only, never blocks saving. (4) **removed all manual numeric
  input** for nozzle diameter, filament diameter, print time, or filament weight — every value must be
  derived from the file or the operation is rejected; the only remaining manual numeric input in this
  phase is the organization's default nozzle/filament-diameter Settings (FR-019). FR count is now 20
  (was 22); `docs/FEATURE_PLAN.md`'s Phase 4 and Phase 7 sections were updated to match throughout.
- Follow-up clarification (same day, final round — 1 formal question, since the resolution needed a
  concrete mechanism decision): (1) **removed the organization-configured default material** — a
  Filament (generic, e.g. "Any PETG," or a specific variant) is now a hard per-part requirement
  whenever a file needs slicing, supplied by the operator in the same request, never defaulted at the
  org level. (2) **removed the organization-configured default nozzle/filament-diameter settings, and
  dropped filament-diameter matching from this phase entirely** (virtually uniform 1.75mm across the
  target market; printer telemetry doesn't expose it either). The only remaining organization-level
  Slicing Default is the slicer **engine**. (3) **Nozzle diameter for automatic slicing is now
  auto-detected from the farm's registered printers** (whichever value is most common), with no
  operator input and no Settings field — new FR-005a, and a new explicit dependency of this phase on
  the printer catalog (Phase 1). FR count is now 18 top-level (+4 lettered sub-items);
  `docs/FEATURE_PLAN.md`'s Phase 4 and Phase 7 sections were updated to match throughout.
- Follow-up clarification (same day, final — 1 formal question, resolving the cross-phase dependency
  the previous round flagged but left open): confirmed this phase **wires the Printer ↔ Machine
  Profile link** that `docs/FEATURE_PLAN.md`'s own Phase 1 dependency notes already earmarked for
  "whichever later phase actually consumes it" — nozzle diameter is read from a printer's linked
  Machine Profile (Phase 2, which already models nozzle size), not a new duplicate field on `Printer`.
  The link is optional at printer-creation time (a matching Machine Profile might not be imported
  yet); only a printer with one linked counts as a "real fleet member" for FR-005a's nozzle-diameter
  determination. New FR-005b, new Key Entity ("Printer ↔ Machine Profile link"), and updated wording
  throughout the spec and `docs/FEATURE_PLAN.md`'s Phase 4 section to reflect this resolved mechanism
  rather than leaving it as an open planning-level decision.
- No [NEEDS CLARIFICATION] markers remain — remaining minor ambiguities (precise thumbnail-rendering
  mechanism, the nozzle-diameter tiebreak rule when the fleet is evenly split) are resolved with
  documented reasonable defaults/deferrals in the Assumptions and Edge Cases sections instead, since
  none blocks scope or introduces a security/privacy concern significant enough to warrant blocking on
  user input before planning.
- All items pass after this clarification pass.
- Follow-up clarification during `/speckit-plan` (2026-07-19, raised directly by the user reviewing the
  generated plan rather than through `/speckit-clarify`, 2 decisions): (1) **a requires-slicing add/replace
  now passes through a "ready for review" state before saving** — the operator sees the derived
  nozzle/material/estimate/thumbnail once a background slice succeeds, may attach/change a narrower
  Filament override, then explicitly confirms (or discards) rather than the part auto-saving the moment
  slicing succeeds; this narrows, not reverses, the earlier "no operator review step" answer, since the
  derived numbers themselves are still never hand-editable. An already-sliced file's add/replace is
  unaffected and still saves in one step. New FR-009a, updated FR-005/FR-017, updated User Story 2's
  Acceptance Scenarios. (2) **a Part's filament requirement became a collection** (`Filament Requirement`,
  a new Key Entity) rather than a single value, so a real multi-color/AMS part's per-slot material/color
  mapping is representable; in-app slicing itself still only ever produces exactly one requirement. Updated
  FR-002/FR-012/FR-012a, User Story 3's Acceptance Scenario 3, and `docs/FEATURE_PLAN.md`'s Phase 4 section
  accordingly. Both items are recorded in the Clarifications section above and in Assumptions; no scope
  change beyond these two decisions.
- Second follow-up clarification during `/speckit-plan` (2026-07-19, again raised directly by the user, 1
  further decision): **the review/confirm step above is broadened from "requires-slicing path only" to
  every add/replace, regardless of file type.** An already-sliced file's add/replace no longer completes
  in one synchronous step — it goes through the identical submit → processing → ready-for-review →
  confirm/discard shape a requires-slicing file does, since parsing its embedded metadata is still real
  processing work, just one with no external process and a typically near-instant result. Rationale: the
  two paths already shared nearly everything (derived-data shape, rejection rules, thumbnail/estimate
  requirements, per-requirement narrow-override mechanics for a multi-color file) — the only genuine
  difference (parse vs. slice) belongs behind one interface's implementation, not exposed as two different
  operator-facing flows. Updated FR-009/FR-009a, User Stories 1 and 3's Acceptance Scenarios/Independent
  Tests, and relevant Assumptions/Edge Cases entries; `docs/FEATURE_PLAN.md`'s Phase 4 section updated to
  match. No scope change beyond unifying the two previously-separate completion shapes.
- Third follow-up clarification during `/speckit-plan` (2026-07-19, again raised directly by the user, 1
  further UX-flow decision): **name/tags are no longer required at the initial file-attach step — they're
  entered/edited concurrently while processing runs, and only actually needed at confirm/save time.**
  Attaching a file (plus a Filament pick, if the file needs slicing) starts background processing
  immediately as one continuous add-part dialog; Save stays disabled until processing reaches "ready for
  review" and (for a new part) a name has been entered, then submits name/tags/narrow overrides together as
  the confirm action. New FR-009b, updated FR-001, updated User Story 1's Acceptance Scenarios/Independent
  Test, new Edge Case. `docs/FEATURE_PLAN.md`'s Phase 4 section updated to match. No scope change — this is
  a request/response-shape and UX-sequencing refinement of the already-uniform review/confirm pipeline, not
  a new capability.
