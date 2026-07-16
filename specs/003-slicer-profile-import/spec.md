# Feature Specification: Profiles (Machine + Filament, Imported from Slicer Bundles)

**Feature Branch**: `003-slicer-profile-import`

**Created**: 2026-07-15

**Status**: Draft

**Input**: User description: "Phase 2 in @docs/FEATURE_PLAN.md"

## User Scenarios & Testing *(mandatory)*

### User Story 1 - Import machine and filament profiles from a slicer bundle (Priority: P1)

An operator who already has tuned machine and filament presets in their slicer (OrcaSlicer or Bambu
Studio) uploads that bundle to OpenFarm. The system extracts every machine and filament preset it
recognizes and shows them as a selection list. The operator picks which presets are actually worth
keeping (a bundle often has presets they don't use) and confirms. Only the selected presets are saved
into OpenFarm's profile catalogs.

**Why this priority**: This is the entire acquisition path for profile data in this phase — without it,
neither catalog has any real data, and no other story in this feature has anything to act on.

**Independent Test**: Can be fully tested by uploading a known-good OrcaSlicer `.zip` bundle (or a Bambu
Studio `.3mf`/preset bundle) containing multiple machine and filament presets, selecting a subset in the
review list, confirming, and verifying that exactly the selected presets — and only those — now exist in
the machine profile and filament profile catalogs.

**Acceptance Scenarios**:

1. **Given** a valid OrcaSlicer profile-bundle `.zip` containing 2 machine presets and 5 filament
   presets, **When** the operator uploads it, **Then** the system presents all 7 presets in a selection
   list before anything is saved.
2. **Given** the presented selection list, **When** the operator selects 1 machine preset and 3 filament
   presets and confirms, **Then** exactly those 4 profiles are created in the catalogs and the other 3
   are discarded without a trace in the database.
3. **Given** the selection list is presented, **When** the operator closes/cancels the import before
   confirming, **Then** no profiles are created.
4. **Given** a valid Bambu Studio printer config bundle (`.bbscfg`) or filament bundle (`.bbsflmt`)
   containing embedded printer and filament presets, **When** the operator uploads it, **Then** the
   system extracts and presents those presets the same way it does for an OrcaSlicer bundle.
5. **Given** a single-preset OrcaSlicer export such as `Bambu Lab P1S 0.4 nozzle.orca_printer` (containing
   the printer preset plus its one directly compatible filament preset) or
   `Aliz PETG Basic.orca_filament` (containing the filament preset plus the printer presets it's
   compatible with), **When** the operator uploads it, **Then** the system presents the small set of
   presets found inside — even when that set is just one or two entries — the same way it presents a
   larger multi-preset bundle.
6. **Given** a Bambu Studio printer config bundle containing one printer preset and six compatible
   filament presets, **When** the operator uploads it, **Then** the system presents all seven as a
   selection list the same way it would for the OrcaSlicer equivalent.

---

### User Story 2 - Preserve base/override relationships from the source bundle (Priority: P2)

A bundle contains a shared base filament preset (e.g. a brand's generic PLA) and several color-specific
presets that override just a few fields on top of it (e.g. "Brand PLA – Red"). The source slicer records
this relationship by having the override preset name its base (OrcaSlicer's own preset files carry an
explicit "derived from" reference to the parent preset's name; a preset with no parent leaves that
reference empty). When the operator imports both the base and its overrides, OpenFarm keeps that
relationship intact rather than flattening every preset into an independent copy that duplicates the
base's full settings.

**Why this priority**: Losing this relationship on import silently throws away information the source
slicer curated — overrides would look like unrelated full presets, making later maintenance (e.g.
updating a shared base) impossible to do consistently. It matters immediately but only once Story 1's
import mechanism exists.

**Independent Test**: Can be fully tested by importing a bundle containing one base filament preset and
one override preset that depends on it, selecting both, and verifying the resulting catalog records show
the override linked to its base rather than as a flattened standalone copy.

**Acceptance Scenarios**:

1. **Given** a bundle with a base machine preset and a derived preset that overrides only its bed-volume
   field, **When** the operator selects and imports both, **Then** the derived profile is stored with a
   reference to its base profile and only the overridden field(s) diverge.
2. **Given** the same bundle, **When** the operator selects only the derived preset and not its base,
   **Then** the system clearly indicates the derived preset's base is not being imported alongside it
   before the operator confirms.
3. **Given** a preset whose declared base is a built-in preset that ships with the slicer itself (e.g. a
   stock Bambu Lab filament preset) rather than anything the user has ever exported or will ever import,
   **When** the operator imports the derived preset on its own, **Then** the system still imports it
   successfully, recording the declared base's name for reference without requiring a matching profile to
   exist in the catalog — a dangling base reference is an expected, normal outcome, not an error.

---

### User Story 3 - Browse imported profile catalogs (Priority: P2)

An operator or admin wants to see what's already been imported — all machine profiles in one list, all
filament profiles in another — to check what's available before doing anything else with them (in a later
phase, assigning them to a printer or a sliced file).

**Why this priority**: Import without a way to review what landed isn't verifiable or useful day-to-day;
this closes the loop on Story 1 for a real user.

**Independent Test**: Can be fully tested by importing a bundle with known presets and then confirming
each imported preset appears, with its key identifying fields (name, source format, whether it's a base
or override, and — for filament profiles — its declared compatible printer(s)), in the corresponding
catalog list.

**Acceptance Scenarios**:

1. **Given** profiles have been imported, **When** the operator opens the machine profile catalog,
   **Then** every imported machine profile is listed with its name and key spec fields.
2. **Given** profiles have been imported, **When** the operator opens the filament profile catalog,
   **Then** every imported filament profile is listed with its name, material, and color.
3. **Given** a filament profile that declares which printer(s) it's compatible with, **When** the
   operator views that filament profile, **Then** the declared compatible printer name(s) are shown.

---

### User Story 4 - Correct an imported profile's raw data (Priority: P3)

After importing a profile, an operator notices a field is wrong or wants to tweak a value (e.g. a nozzle
diameter that didn't parse as expected). Rather than a form field, they open a raw JSON editor/viewer for
that specific profile, make the change, and save.

**Why this priority**: Useful once real imported data exists and inevitably needs occasional correction,
but it's a refinement on top of Stories 1–3, not a blocker for the acquisition path itself. Structured
per-field forms are explicitly out of scope for this phase.

**Independent Test**: Can be fully tested by opening an imported profile's JSON editor, changing a field
value, saving, and confirming the catalog now reflects the new value.

**Acceptance Scenarios**:

1. **Given** an imported filament profile, **When** the operator opens its JSON editor, edits a field,
   and saves, **Then** the catalog reflects the updated value.
2. **Given** an operator editing a profile's JSON, **When** they submit malformed JSON, **Then** the
   system rejects the save with a clear error and the previously saved data is left unchanged.

---

### Edge Cases

- What happens when the uploaded file isn't a recognizable OrcaSlicer or Bambu Studio bundle (wrong file
  type, corrupted archive, or a bundle format from an unsupported slicer)? The system must reject the
  upload with a clear reason and create no catalog entries.
- What happens when a bundle contains zero recognizable machine or filament presets (e.g. an empty
  archive or one containing only unrelated files)? The system must report that nothing importable was
  found rather than silently succeeding with an empty result.
- What happens when a preset in the bundle duplicates one already sitting in the catalog (same name and
  type from a prior import)? The system must flag the duplicate to the operator and let them choose to
  skip it or import it as a separate additional entry, rather than silently overwriting existing data.
- What happens when an override preset's declared base is a built-in slicer default that was never part
  of any bundle (the common case, not a rare one)? The system must import the override on its own and
  keep the base's name as an unresolved reference rather than blocking the import or erroring.
- How does the system handle a very large bundle (many dozens of presets) in the review/selection list?
  The list must stay usable (searchable/scrollable) rather than becoming an unmanageable wall of entries.
- What happens if the operator's session or upload is interrupted partway through parsing a large bundle?
  No partial set of profiles should be left in the catalog — the import either completes on explicit
  confirmation or leaves no trace.

## Requirements *(mandatory)*

### Functional Requirements

- **FR-001**: System MUST allow an authenticated user to upload a slicer bundle file in one of the
  supported source formats: an OrcaSlicer multi-preset profile-bundle `.zip`; an OrcaSlicer single-preset
  export (`.orca_filament` or `.orca_printer`); or the Bambu Studio equivalents (`.bbsflmt` filament
  bundle or `.bbscfg` printer config bundle). Each single-preset export is a self-contained archive that
  may include the preset's directly compatible companion presets alongside it (e.g. a printer's config
  bundle commonly includes several compatible filament presets, not just the printer preset itself).
- **FR-002**: System MUST parse an uploaded bundle and extract every machine preset and filament preset
  it recognizes within it.
- **FR-003**: System MUST present all extracted presets to the user as a selection list — showing at
  least each preset's name, type (machine or filament), and whether it is a base or an override — before
  persisting anything.
- **FR-004**: System MUST persist only the presets the user explicitly selects to keep; presets left
  unselected MUST NOT be created in either catalog.
- **FR-005**: System MUST preserve any base/override (parent/child) relationship a preset declares in the
  source bundle by storing the derived profile with a reference to its declared base, rather than
  flattening it into an independent full copy. The reference MUST be retained by base name even when no
  matching base profile exists in the catalog — a base preset is commonly a built-in slicer default that
  is never itself exported or imported, so an unresolved reference is expected, not an error.
- **FR-006**: System MUST warn the user, before import is confirmed, when a selected override's base
  preset is present elsewhere in the same bundle but not also selected for import.
- **FR-007**: System MUST reject bundles that are not a recognizable OrcaSlicer or Bambu Studio format
  (unsupported type or corrupted archive) with a clear, actionable error, and MUST NOT create any catalog
  entries from a rejected bundle.
- **FR-008**: System MUST report clearly when a successfully-opened bundle contains zero recognizable
  machine or filament presets, without creating any catalog entries.
- **FR-009**: System MUST detect when a preset being imported duplicates an already-imported profile
  (same name and type) and let the user choose to skip it or import it as a separate additional entry.
- **FR-010**: System MUST allow any authenticated user to view the full list of imported machine profiles
  and the full list of imported filament profiles as two separate catalogs.
- **FR-011**: System MUST allow any authenticated user to view a single imported profile's full raw data.
- **FR-012**: System MUST allow any authenticated user to edit an imported profile's raw data through a
  JSON editor and save the change.
- **FR-013**: System MUST validate that edited profile JSON is well-formed before saving; on invalid
  JSON, the system MUST reject the save and leave the previously saved profile data unchanged.
- **FR-014**: System MUST NOT provide or require structured per-field create/edit forms for machine or
  filament profiles in this feature — manual creation is out of scope; profiles only ever come from
  import.
- **FR-015**: System MUST NOT link an imported profile to any specific fleet printer record or to any
  print job — profiles remain a standalone catalog in this feature.
- **FR-016**: System MUST extract and preserve a filament preset's declared printer compatibility from
  the source bundle — the preset's own list of compatible printer preset names — and, for a preset that
  declares neither a base preset nor an explicit compatible-printer list, fall back to the printer name
  embedded in the preset's own name. These names are stored and shown as declared text; resolving them
  against a specific Machine Profile record is explicitly out of scope for this feature (see Assumptions)
  — FR-015 is unaffected regardless. An empty or entirely absent compatible-printer declaration means the
  preset is compatible with all printers, consistent with the source format's own convention.

### Key Entities *(include if feature involves data)*

- **Machine Profile**: A named catalog entry describing a printer model's capabilities as extracted from
  a slicer bundle — nozzle size, bed volume, whether it has an enclosure, supported materials, and
  whether the model is one OpenFarm can live-connect to. Carries the name of the base machine profile it
  overrides, if any; that reference may point to a profile that exists in the catalog, or may be left
  unresolved when the base is a built-in slicer default that was never imported.
- **Filament Profile**: A named catalog entry describing a filament preset as extracted from a slicer
  bundle — material type, brand, color, and tuned slicer settings. Schema is kept close to Spoolman's
  filament/vendor shape for future compatibility. Carries the name of the base filament profile it
  overrides, if any, with the same possibly-unresolved reference behavior as Machine Profile. Also
  carries the printer name(s) it declares itself compatible with, stored as declared text (from the
  source preset's own compatible-printer list, or inferred from its own name when it declares neither a
  base nor an explicit list) — not resolved to a specific Machine Profile record this feature (FR-016).
- **Import Batch** (conceptual, not necessarily a user-facing entity): The set of presets extracted from
  one uploaded bundle in one import action, used to group the review/selection step before any profile is
  persisted.

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: An operator can go from uploading a typical slicer bundle (under 50 combined presets) to
  seeing their selected profiles appear in the catalog in under 2 minutes.
- **SC-002**: 100% of base/override relationships present in a source bundle and selected for import are
  preserved as a parent/child link afterward — none are silently flattened into independent copies.
- **SC-003**: 100% of unselected presets from a reviewed bundle are absent from the catalog after import
  completes.
- **SC-004**: 100% of cancelled or unconfirmed imports leave zero new profiles in the catalog.
- **SC-005**: 100% of malformed, corrupted, or unsupported-format bundle uploads are rejected with an
  actionable error message and create no partial catalog data.
- **SC-006**: An operator can locate and open any single imported profile's raw data for review in no
  more than 3 interactions starting from the catalog list.

## Assumptions

- Supported source formats are OrcaSlicer exports (the multi-preset profile-bundle `.zip`, and the
  single-preset `.orca_filament`/`.orca_printer` files) and their Bambu Studio equivalents (`.bbsflmt`
  filament bundle, `.bbscfg` printer config bundle). Both slicers share the same underlying bundle
  structure — an archive containing a `bundle_structure.json` manifest plus one or more preset JSON
  files — differing only in file extension and naming, and both carry a "derived from" reference to a
  base preset's name when a preset is an override. Other slicers (Cura, PrusaSlicer, SuperSlicer, etc.)
  are out of scope until a later phase, if ever.
- A preset's declared base is frequently a built-in preset shipped with the slicer itself (e.g. a stock
  vendor filament preset), which is never exported or imported on its own since OpenFarm never seeds
  default slicer profiles (consistent with `docs/FEATURE_PLAN.md`'s "import-your-own-bundle only"
  design). Storing an override whose base reference never resolves to a catalog record is therefore the
  common case, not an exceptional one.
- A filament preset's declared printer compatibility is stored and shown as plain declared text (FR-016)
  — preserved verbatim from the source bundle, the same way `BaseProfileName` is, but explicitly **not**
  resolved to a specific Machine Profile record in this feature. Linking a filament profile's declared
  compatibility to an actual catalog Machine Profile is deferred, consistent with this feature's general
  "catalog data only, no cross-record linking beyond base/override" scope for its first phase.
- Structured per-field create/edit forms are explicitly out of scope for this phase; the raw JSON
  editor/viewer is the only way to inspect or correct an imported profile, consistent with
  `docs/FEATURE_PLAN.md` Phase 2 deferring per-attribute forms until real imported data has settled the
  schema.
- No live printer connection or printer/job linkage is needed or built in this feature — imported
  profiles are a standalone library; wiring a machine profile onto a fleet `Printer` row or a filament
  profile onto a job happens in later phases (Sliced files, Jobs) that already depend on this catalog
  existing.
- Because the real permission catalog isn't seeded until a later fleet-hardening phase, this feature
  requires only that a user be authenticated — it does not yet gate import, view, or edit actions behind
  a specific role or permission.
- Duplicate detection matches on preset name and type against existing catalog entries; it is a
  soft warning the user can override by choosing to import a duplicate as a new separate entry, not a
  hard block.
- The Filament Profile schema intentionally mirrors Spoolman's material/brand/vendor shape for future
  compatibility, but implementing a Spoolman-compatible API surface itself is a separate, later concern
  and is not part of this feature.
