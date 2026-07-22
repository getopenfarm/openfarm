# Feature Specification: Process Profiles — System & User Profiles for Machine, Process, and Filament

**Feature Branch**: `009-process-profiles`

**Created**: 2026-07-21

**Status**: Draft

**Input**: User description: "previous after implemented the last spec there is still some problem with slicing. So I try to fix it but it's beyond the spec, it still have problem because of missing process profile and now I want to go through proper spec kit flow. The main problem is it introduces process profile in setting page. I want [process] profile to be like machine profile, and I don't want any default machine and process profile, it has to depends on the fleet, totally, just like how machine profile defined in the previous spec. Also, we need to research and see how to import default process profile, I think we need to have predefined process profile in our application for each supported machine model by default, but I'm not sure whether process profile in different slicer is the same or not?"

## Clarifications

### Session 2026-07-21

- **Q1 — Where do process profiles come from?** → Process profiles are **provided by the bundled slicer itself** (the slicer ships built-in system process presets per machine model). There is **no import feature and no custom/hand-authored process profile** — the operator never imports or defines a process profile; they only *select* one the slicer already provides. "The usage should be the same as machine" refers to the selection experience (chosen on the printer page), not to the acquisition path.
- **Q2 — How is a process profile attached to the fleet?** → **Directly to each printer**, parallel to how a machine profile is attached to a printer. Two printers of the same model can therefore carry different process profiles.
- **Q3 — Should System Profile gathering during provisioning extend beyond process to machine and filament too?** → **Yes, all three kinds.** The bundled slicer's `resources/profiles/<Vendor>.json` vendor index (confirmed against a real Bambu Lab `BBL.json`: `machine_model_list`, `machine_list`, `process_list`, `filament_list`, each entry a `{name, sub_path}` pointer to a delta-profile JSON file with its own `inherits` chain) is ingested for **machine, process, and filament** during provisioning — not process alone. This is used both to source selectable profiles and to resolve `BaseProfileName` references on User-imported Machine/Filament profiles that previously dead-ended as unresolved.
- **Q4 — How are System Profiles surfaced and labeled once ingested?** → Every picker that offers a profile of a given kind lists **both System-sourced and User-imported entries together, clearly labeled by origin** (e.g. `System: Bambu PLA Basic @BBL X1C` vs. `User: My Tuned PLA`). Machine and process profiles are selected on the **printer page**; filament profiles are selected on the **part detail page** (narrowing an existing filament requirement, feature `007-part-library`) — not on the printer page. The existing Machine/Filament Profile import page (feature `003-slicer-profile-import`) is **unchanged in scope**: it continues to list and manage **only User-imported profiles**; System Profiles are never shown or editable there.
- **Q5 — Which vendors' System Profiles get ingested?** → Scoped to the vendor(s) whose declared machine models match OpenFarm's **currently supported machine model catalog** (`PrinterModelCatalog` — today: Bambu Lab only), determined by cross-referencing the vendor index's `machine_model_list` against that catalog rather than hard-coding "Bambu only." Ingestion scope grows automatically as the supported-model catalog gains models, with no ingestion-logic changes needed. User-imported Machine/Filament profiles (feature 003) remain unrestricted to any brand, as today.

## Context & Problem *(informative)*

Headless slicing (OrcaSlicer / Bambu Studio, driven by OpenFarm) needs three preset inputs to produce toolpaths: a **machine** preset (the printer), a **process** preset (layer height, speeds, supports, infill — "how" the object is printed), and a **filament** preset (the material). Machine and filament profiles are already a user-imported catalog (feature `003-slicer-profile-import`); a printer is linked to its machine profile on the printer's own page (feature `005-printer-fleet-refinements`); the bundled slicer engines are discovered on disk per engine/version/platform (feature `008-orca-slicer-multi-version`).

The **process** preset is the missing third leg, and an interim fix bolted it on in the wrong shape:

- It let process presets be **imported from user bundles** and hand-edited as raw data — but process presets are strictly machine-specific slicer artifacts, not something an operator curates like their own machine/filament library.
- It introduced an organization-level **"default machine profile"** and a **paired default process profile**, configured on a **Settings** page. A print farm has many printers of different models, so a single org-wide default machine (and its one paired process) can never be correct for a heterogeneous fleet.

Digging into how the bundled slicers actually ship their presets surfaced a bigger, related gap: each slicer engine bundles a vendor profile index (e.g. Bambu Lab's `BBL.json`) enumerating **every built-in machine, process, and filament preset it ships** — thousands of entries, each a small "delta" preset that inherits from a common base via the same base/override mechanism OpenFarm already models for user-imported profiles (feature 003). OpenFarm has never ingested this data, which is *also* why a User-imported profile's declared base so often shows up as an unresolved reference today — the actual base preset was never anywhere in the system to resolve against.

This feature corrects all of this at once: **System Profiles** for machine, process, and filament are **ingested from the bundled slicer's own vendor data during provisioning**, scoped to the vendors matching OpenFarm's supported machine models. They are offered — clearly labeled alongside User-imported profiles — wherever an operator picks a profile of that kind: machine and process on the **printer page**, filament on the **part detail page**. Process profiles remain exclusively System-sourced (no import, no hand-authoring, per Q1). There is **no organization-level default machine or process profile**; slicing resolves entirely from the fleet.

## User Scenarios & Testing *(mandatory)*

### User Story 1 - System Profiles are ingested from the bundled slicer during provisioning (Priority: P1)

As part of provisioning the bundled slicer (the existing build/dev-time step that fetches each supported engine's binaries), OpenFarm also ingests that slicer's own vendor profile index — its built-in machine, process, and filament presets — scoped to the vendor(s) whose declared machine models match OpenFarm's supported machine model catalog. These become read-only **System Profiles**, each preserving whatever base/override relationship it declares in the vendor data.

**Why this priority**: Everything else in this feature — printer-page selection, part-detail filament narrowing, and fleet-driven slicing — depends on System Profiles existing first. It is also what resolves the user's original open question about "predefined process profiles per supported machine model": the predefined set *is* the slicer's own vendor data, ingested once, not a separately curated or licensed OpenFarm dataset.

**Independent Test**: Run provisioning against a bundled slicer whose vendor data includes a supported machine model (e.g. Bambu Lab); verify System Profiles for machine, process, and filament exist afterward, each attributed to its vendor, with declared base/override relationships intact. Verify no System Profiles were created for a vendor whose machine models aren't in OpenFarm's supported catalog.

**Acceptance Scenarios**:

1. **Given** a bundled slicer whose vendor data covers a supported machine model, **When** provisioning runs, **Then** System Profiles for that vendor's machine, process, and filament presets are ingested and available.
2. **Given** the same vendor data contains a preset that declares a base/override relationship, **When** it is ingested, **Then** the resulting System Profile carries a reference to its declared base among the other ingested System Profiles of the same kind, resolved rather than left dangling when the base was also ingested.
3. **Given** a vendor in the bundled slicer's data whose machine models are not in OpenFarm's supported machine model catalog, **When** provisioning runs, **Then** no System Profiles are ingested for that vendor.
4. **Given** OpenFarm's supported machine model catalog later gains a new model from a vendor already present in the bundled slicer's data, **When** provisioning is re-run, **Then** that vendor's System Profiles become ingested without any change to the ingestion logic itself.
5. **Given** provisioning is re-run against the same vendor data (e.g. a routine re-provision, not a version change), **When** ingestion completes, **Then** no duplicate System Profiles are created for presets already ingested.

---

### User Story 2 - Choose a printer's machine and process profile, from System or User profiles, on the printer page (Priority: P1)

On a printer's page, the operator picks that printer's machine profile and process profile from one combined list per kind — System Profiles and User-imported profiles together, each clearly labeled by origin (e.g. "System: …" / "User: …"). Process profiles are always System-labeled, since no user-import path for process exists. The printer only offers process choices compatible with its selected machine, and only allows machine/process combinations that are themselves compatible. The selections are remembered on the printer and take effect for its next slice; two printers of the same model can carry different selections.

**Why this priority**: This is the primary operator-facing capability that makes fleet-driven slicing possible, and it's where the interim, wrongly-shaped "default machine/process profile" settings page is replaced.

**Independent Test**: Open a printer's page; verify its machine-profile choices include both System- and User-labeled entries; select a System machine profile and a compatible System process profile; save; verify a subsequent slice for that printer uses both. Verify a second printer of the same model can independently select different profiles, including a User-imported machine profile if one exists.

**Acceptance Scenarios**:

1. **Given** a printer's page, **When** the operator views machine-profile choices, **Then** both System-sourced and User-imported machine profiles appear, each clearly labeled by origin.
2. **Given** a printer with a machine profile selected, **When** the operator views process-profile choices, **Then** only System process profiles compatible with that machine appear (never a User-labeled entry, since process has no import path).
3. **Given** the operator selects a machine profile and a compatible process profile and saves, **When** a slice is later requested for that printer, **Then** the slice uses both selections.
4. **Given** two printers of the same model, **When** the operator gives each a different machine and/or process profile (System or User, independently), **Then** each printer keeps and slices with its own selection.
5. **Given** a printer with no machine profile selected yet, **When** the operator opens process-profile choices, **Then** selection is unavailable with a clear prompt to select a machine profile first.
6. **Given** a printer's machine or process selection is changed or cleared, **When** the operator saves, **Then** the change is captured in the activity log like other fleet changes.

---

### User Story 3 - Slice for a printer using fleet-derived profiles, with no org-level defaults (Priority: P1)

When OpenFarm slices for a given printer, it resolves the machine and process presets **from that printer's own selections** — System- or User-sourced, whichever the operator chose — plus the filament for the target material. There is **no organization-level "default machine profile" and no organization-level "default process profile"**: those interim settings are removed. If a printer being sliced for is missing its machine or process selection, the system reports exactly what is unconfigured **for that printer** rather than substituting any global default.

**Why this priority**: This is the actual defect the operator hit — slicing driven by one global default machine/process is wrong for a multi-model farm. Making resolution fleet-driven and removing the misplaced org-level defaults is the core value of this feature.

**Independent Test**: Configure two printers of different models, each with its own machine and process selection; trigger a slice for each; verify each uses its own presets. Then clear one printer's process selection and verify its slice fails with a message naming the missing selection for that specific printer — with no org-level default ever consulted.

**Acceptance Scenarios**:

1. **Given** two printers of different models, each with its own machine and process selection, **When** a slice is requested for each, **Then** each slice is driven by that printer's own selections, not a shared global default.
2. **Given** a printer whose machine or process selection is missing, **When** a slice is requested for it, **Then** the system reports exactly which is missing for that printer and produces no slice, without falling back to any org-level default.
3. **Given** the interim organization-level "default machine profile" and "default process profile" settings previously existed, **When** this feature is in place, **Then** those settings no longer exist anywhere in the product and nothing in slicing reads them.
4. **Given** an estimate-only slice not tied to one specific target printer (e.g. deriving a part's print-time/material estimate at upload), **When** it runs, **Then** it derives its machine and process presets from a representative member of the current fleet's own selections — still fleet-derived, never an org-level default — and refuses with a clear reason when the fleet has no usable member.

---

### User Story 4 - Narrow a part's filament requirement to a System or User filament profile, on the part detail page (Priority: P2)

On a part's detail page, the operator narrows one of the part's filament requirements to a specific filament profile, chosen from one combined list — System-sourced and User-imported filament profiles together, clearly labeled by origin — the same pattern as machine/process selection on the printer page. This extends the part's existing filament-requirement narrowing (feature `007-part-library`) rather than replacing it.

**Why this priority**: Filament is a material choice tied to what's loaded on a printer for a given part/job, not a fleet attachment — it belongs on the part, not the printer page. It's additive to Stories 1–3's core slicing fix, extending the same System/User labeling convention to the third profile kind.

**Independent Test**: Open a part with an unnarrowed filament requirement; verify the narrowing picker offers both System- and User-labeled filament profiles; select a System filament profile; save; verify the requirement reflects the narrowed choice.

**Acceptance Scenarios**:

1. **Given** a part's filament requirement, **When** the operator opens its narrowing picker, **Then** both System-sourced and User-imported filament profiles appear, each clearly labeled by origin.
2. **Given** the operator selects a System filament profile to narrow a requirement, **When** they save, **Then** the requirement reflects that System filament profile the same way it would a User-imported one.
3. **Given** a part's filament requirement is narrowed, changed, or cleared, **When** the operator saves, **Then** the change is captured the same way existing part filament narrowing changes are.

---

### Edge Cases

- What happens when the operator tries to slice for a printer that has a machine profile but no process selection? The slice is refused with a message naming the missing process selection **for that printer** — never silently substituting anything.
- What happens to installs that already set the interim organization-level default machine/process profile, or imported process profiles into the interim catalog? Those org-level default settings and the imported-process path are removed; any behaviour that depended on them is replaced by fleet-driven, System/User-sourced resolution, and no slice silently keeps using a stale global default.
- What happens when the bundled slicer's ingested System Profiles offer no process preset compatible with a printer's selected machine? The printer's process-profile choices are empty and the operator is told no compatible process profile is available, rather than offering an incompatible one.
- What happens when the selected slicer engine/version changes (feature 008) and its built-in process presets differ? A printer's process selection is re-resolved against the currently-ingested System Profiles; if the previously selected one is no longer available, the printer is flagged as needing a new selection rather than silently slicing with a substitute. *(Exact re-resolution/re-ingestion mechanics to be settled in planning.)*
- What happens when the fleet is empty and an estimate-only (no specific target printer) slice is requested? With no fleet member to derive a representative machine/process from, the slice is refused with a clear "no printers configured" reason.
- What happens when a User-imported Machine or Filament profile's declared base name doesn't match any ingested System Profile (e.g. the base belongs to a vendor outside OpenFarm's supported-model scope, per Q5)? The reference remains unresolved, same as before this feature — now the less common case rather than the expected one, but still not an error.
- How do System Profile pickers stay usable at real-world scale (a single vendor's filament index alone can exceed 2,000 entries)? Choices must stay searchable/filterable rather than an unmanageable flat list, on both the printer page and the part detail page.
- What happens when OpenFarm's supported machine model catalog gains a new model whose vendor wasn't previously ingested? That vendor's System Profiles become available only after the next provisioning/ingestion run — not automatically retroactive without one.

## Requirements *(mandatory)*

### Functional Requirements

- **FR-001**: System MUST ingest **System Profiles for machine, process, and filament** from the bundled slicer's own vendor profile data during provisioning, scoped to exactly the vendor(s) whose declared machine models match OpenFarm's currently supported machine model catalog — determined by cross-referencing vendor data against that catalog, not hard-coded to one vendor, so ingestion scope grows automatically as the catalog gains models.
- **FR-002**: System Profiles MUST be read-only: the system MUST NOT provide any way to hand-author, edit, or delete one. They exist only as ingested from the bundled slicer's vendor data and are refreshed by re-running ingestion (e.g. on a subsequent provisioning run), without duplicating already-ingested entries.
- **FR-003**: System MUST preserve each ingested System Profile's declared base/override relationship from the vendor data, resolving it against other ingested System Profiles of the same kind where the declared base was also ingested.
- **FR-004**: System MUST allow the operator to select a printer's **machine profile** and **process profile** on the printer page, each from one combined choice list per kind containing both System-sourced and User-imported entries (machine) — or System-sourced entries only (process, per FR-011) — with every entry clearly labeled by its origin (e.g. "System: …" / "User: …").
- **FR-005**: System MUST persist a printer's machine-profile and process-profile selections **on the printer** (per-printer, not per organization), regardless of whether each selected profile is System- or User-sourced.
- **FR-006**: System MUST offer, as a printer's process-profile choices, only System process profiles compatible with the printer's selected machine profile, and MUST prevent selecting an incompatible machine/process combination.
- **FR-007**: System MUST make process-profile selection unavailable for a printer that has no machine profile selected yet, prompting the operator to select one first.
- **FR-008**: System MUST resolve the machine and process presets used for a slice **from the target printer's own selections** (System- or User-sourced) for every slice tied to a specific target printer — never from any organization-level default.
- **FR-009**: System MUST NOT provide any **organization-level "default machine profile"** or **organization-level "default process profile"** setting; the interim settings-page defaults for machine and process profiles MUST be removed, and no slicing path may read them.
- **FR-010**: System MUST, when a slice tied to a specific printer cannot be completed because that printer's machine or process selection is missing or its resolved presets are unusable, report exactly what is missing **for that printer** and produce no slice.
- **FR-011**: System MUST NOT provide any way to import a process profile from a user bundle, or to hand-author/edit one — process profiles remain exclusively System-sourced.
- **FR-012**: System MUST allow the operator to narrow one of a part's filament requirements, on the part detail page, to a filament profile chosen from one combined list containing both System-sourced and User-imported filament profiles, each clearly labeled by origin — extending the existing part filament-requirement narrowing (feature `007-part-library`) rather than replacing it.
- **FR-013**: System MUST keep the existing Machine/Filament Profile import and browse page (feature `003-slicer-profile-import`) scoped to **User-imported profiles only** — System Profiles MUST NOT appear in, nor be manageable through, that page.
- **FR-014**: System MUST resolve a User-imported Machine or Filament profile's declared base-profile reference against ingested System Profiles of the same kind where a matching one exists, rather than always leaving it unresolved; an unresolved reference remains a valid, non-error outcome when no matching System Profile was ingested (e.g. the base's vendor is outside the supported-model scope).
- **FR-015**: System MUST, for an estimate-only slice not tied to one specific target printer, derive its machine and process presets from a representative member of the current fleet's own selections rather than from any organization-level default, and refuse the slice with a clear reason when the fleet has no usable member.
- **FR-016**: System MUST capture selecting, changing, or clearing a printer's machine or process profile, and narrowing/changing/clearing a part's filament requirement, in the activity log, consistent with how other state-mutating changes are logged.
- **FR-017**: System MUST leave the organization-level per-material **filament default** setting unchanged in this feature's scope — this feature's System/User labeling and selection changes apply to printer-page machine/process selection and part-detail-page filament narrowing only. *(Documented assumption; revisit only if that setting should also surface System filament profiles or become fleet-driven.)*

### Key Entities *(include if data involved)*

- **System Profile**: A named, read-only preset (machine, process, or filament) ingested from the bundled slicer's own vendor data at provisioning time, attributed to its vendor, and scoped to vendors matching OpenFarm's supported machine model catalog. Carries the same base/override relationship the vendor data declares, resolved against other System Profiles of the same kind. Never imported, hand-authored, or edited by the operator.
- **User-imported Profile**: The existing Machine/Filament Profile catalog from feature `003-slicer-profile-import` — unchanged in how it's acquired (bundle upload, review, confirm) and unchanged in that the import/browse page shows only these. Its declared base-profile reference can now resolve against an ingested System Profile where a match exists, in addition to another User-imported profile.
- **Printer machine/process selection**: The per-printer record of which profile (System- or User-sourced, for machine; System-sourced only, for process) the printer uses, selected on the printer page. Replaces the removed organization-level default machine/process profile.
- **Part filament-requirement narrowing**: The existing per-requirement narrowing on a Part (feature `007-part-library`), extended so the referenced filament profile may be System- or User-sourced, selected on the part detail page.

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: 100% of slices tied to a specific printer draw their machine and process presets from that printer's own selections; 0% consult an organization-level default machine or process profile (there is none).
- **SC-002**: In a fleet of at least two different printer models, each model's slice uses a model-compatible process profile — no incompatible or cross-model process preset is ever silently applied.
- **SC-003**: 100% of slices that cannot resolve a required machine or process selection for the target printer fail with a message that names the specific missing item and the specific printer, and produce no partial slice.
- **SC-004**: On a fresh install with a bundled slicer provisioned and a supported-model printer registered, and **zero user-imported profiles**, an operator can select System machine, process, and filament profiles and reach a successful slice — no import required for any of the three.
- **SC-005**: 0 organization-level "default machine profile" or "default process profile" settings, and 0 process-profile import/hand-edit surfaces, remain anywhere in the product after this feature ships.
- **SC-006**: An operator can select compatible machine and process profiles for a printer and obtain a successful slice for it in no more than 3 interactions starting from the printer's page.
- **SC-007**: Two printers of the same model can be given different machine and/or process selections, and each slices with its own — verifiable in a single test.
- **SC-008**: 100% of profile pickers that offer both System- and User-sourced entries clearly label each entry's origin, verifiable by inspection of the printer page and part detail page.
- **SC-009**: System Profile ingestion includes 0 profiles from vendors whose machine models fall outside OpenFarm's supported machine model catalog.
- **SC-010**: A profile picker showing a vendor's full ingested set (thousands of entries for a real vendor's filament list) remains usable — an operator can find a specific named profile via search/filter in a handful of keystrokes rather than scrolling a flat list.

## Assumptions

- The bundled slicer(s) OpenFarm already ships/provisions (feature `008-orca-slicer-multi-version`) expose a vendor profile index (confirmed structure: a top-level JSON per vendor with `machine_model_list`, `machine_list`, `process_list`, `filament_list`, each entry a `{name, sub_path}` pointer to an individual delta-preset JSON file carrying its own `inherits` chain) as part of their bundled resources. Ingesting this is a new step alongside the existing binary-provisioning step, not a replacement for it.
- "Supported machine model catalog" means OpenFarm's existing `PrinterModelCatalog` (or its future equivalent) — the same source of truth already used to recognize live-connectable printer models. Ingestion scoping cross-references a vendor's `machine_model_list` entries against it.
- A User-imported Machine or Filament profile (feature 003) remains unrestricted to any brand, including ones outside the System Profile ingestion scope — only *System Profile ingestion* is scoped to supported vendors; user import is not newly restricted by this feature.
- The organization-level *filament-for-a-material* default (on the interim "Slicing Profile Defaults" settings surface) is **left as-is** (FR-017). This is a documented default, not a settled preference — flag if it should also surface System filament profiles or become fleet-driven.
- Because the real permission catalog isn't fully enforced yet, this feature requires only that a user be authenticated for viewing choices, selecting a printer's machine/process profile, and narrowing a part's filament requirement — consistent with the auth posture of features 003–008.
- Re-ingestion cadence and exact idempotency mechanics (how re-running provisioning avoids duplicating already-ingested System Profiles, and how a printer's now-stale selection is flagged when its previously-selected System Profile disappears from a re-ingested set) are planning-level concerns (`/speckit-plan` research), not resolved further here.
- Matching a vendor index's human-readable `machine_model_list` names (e.g. "Bambu Lab X1 Carbon") to OpenFarm's internal `PrinterModelCatalog` (brand, model code) entries needs a concrete mapping approach, to be established during planning research.

## Dependencies

- Builds on `003-slicer-profile-import` (User-imported Machine/Filament catalog, unchanged in scope per FR-013), `005-printer-fleet-refinements` (printer⇄machine-profile fleet link, extended to also carry the process selection), `007-part-library` (existing part filament-requirement narrowing, extended to accept System-sourced filament profiles), and `008-orca-slicer-multi-version` (bundled-slicer provisioning, extended to also ingest each engine's vendor profile index).
- Rewires the slicing invocation layer (`ResolveSlicingProfileSelection`) from organization-level defaults to per-printer, fleet-driven resolution across System- and User-sourced profiles.
- Removes interim surfaces introduced ad hoc: process-profile bundle import, the process raw-data editor, the machine-profile→process pairing, and the "Slicing Profile Defaults" org-level machine/process defaults.
