# Feature Specification: Process Profiles (Fleet-Selected, Sourced From the Bundled Slicer)

**Feature Branch**: `009-process-profiles`

**Created**: 2026-07-21

**Status**: Draft

**Input**: User description: "previous after implemented the last spec there is still some problem with slicing. So I try to fix it but it's beyond the spec, it still have problem because of missing process profile and now I want to go through proper spec kit flow. The main problem is it introduces process profile in setting page. I want [process] profile to be like machine profile, and I don't want any default machine and process profile, it has to depends on the fleet, totally, just like how machine profile defined in the previous spec. Also, we need to research and see how to import default process profile, I think we need to have predefined process profile in our application for each supported machine model by default, but I'm not sure whether process profile in different slicer is the same or not?"

## Clarifications

### Session 2026-07-21

- **Q1 — Where do process profiles come from?** → Process profiles are **provided by the bundled slicer itself** (the slicer ships built-in system process presets per machine model). There is **no import feature and no custom/hand-authored process profile** — the operator never imports or defines a process profile; they only *select* one the slicer already provides. "The usage should be the same as machine" refers to the selection experience (chosen on the printer page), not to the acquisition path. This resolves the original "predefined process profile per supported machine model" idea: the predefined set *is* the bundled slicer's own per-model presets, so no separate seeding or third-party licensing of profile data is introduced.
- **Q2 — How is a process profile attached to the fleet?** → **Directly to each printer** (Option A), parallel to how a machine profile is attached to a printer. Two printers of the same model can therefore carry different process profiles.

## Context & Problem *(informative)*

Headless slicing (OrcaSlicer / Bambu Studio, driven by OpenFarm) needs three preset inputs to produce toolpaths: a **machine** preset (the printer), a **process** preset (layer height, speeds, supports, infill — "how" the object is printed), and a **filament** preset (the material). Machine and filament profiles are already a user-imported catalog (feature `003-slicer-profile-import`); a printer is linked to its machine profile on the printer's own page (feature `005-printer-fleet-refinements`); the bundled slicer engines are discovered on disk per engine/version/platform (feature `008-orca-slicer-multi-version`).

The **process** preset is the missing third leg, and an interim fix bolted it on in the wrong shape:

- It let process presets be **imported from user bundles** and hand-edited as raw data — but process presets are strictly machine-specific slicer artifacts, not something an operator curates like their own machine/filament library.
- It introduced an organization-level **"default machine profile"** and a **paired default process profile**, configured on a **Settings** page. A print farm has many printers of different models, so a single org-wide default machine (and its one paired process) can never be correct for a heterogeneous fleet.

This feature corrects both: a process profile is **sourced from the bundled slicer** (per supported machine model, never imported or hand-defined), is **selected per printer on the printer page** (exactly where a machine profile is selected), and slicing resolves its machine + process presets **entirely from the fleet** — with **no organization-level default machine or process profile**.

## User Scenarios & Testing *(mandatory)*

### User Story 1 - Choose a printer's process profile on the printer page (Priority: P1)

On a printer's page — the same place the operator already picks that printer's machine profile — the operator chooses which process profile the printer uses. The choices offered are the process profiles the bundled slicer provides for that printer's machine model, and only ones compatible with the printer's machine. The choice is remembered on the printer and takes effect for that printer's next slice. Two printers of the same model can carry different process profiles.

**Why this priority**: This is the primary operator-facing capability of the feature and the connective tissue that makes fleet-driven slicing (Story 2) work — without a per-printer process selection, a slice has no valid "how to print" input. It mirrors the existing machine-profile selection on the printer page.

**Independent Test**: For a printer that has a machine profile attached, open its page, select a process profile from the offered choices, save, and verify a subsequent slice for that printer uses it. Verify a second printer of the same model can be given a different process profile independently.

**Acceptance Scenarios**:

1. **Given** a printer with a machine profile attached, **When** the operator opens its page, **Then** the process-profile choices offered are those the bundled slicer provides for that printer's machine model, and none incompatible with it.
2. **Given** the operator selects a process profile and saves, **When** a slice is later requested for that printer, **Then** the slice uses the selected process profile.
3. **Given** two printers of the same model, **When** the operator gives each a different process profile, **Then** each printer keeps its own selection and slices with it independently.
4. **Given** a printer that has no machine profile attached yet, **When** the operator opens its page, **Then** process-profile selection is unavailable (nothing to determine compatibility against) with a clear prompt to attach a machine profile first.
5. **Given** a printer's process profile is selected, changed, or cleared, **When** the operator saves, **Then** the change is captured in the activity log like other fleet changes.

---

### User Story 2 - Slice for a printer using fleet-derived profiles, with no org-level defaults (Priority: P1)

When OpenFarm slices for a given printer, it resolves the machine and process presets **from that printer** — the printer's attached machine profile and the printer's selected process profile — plus the filament for the target material. There is **no organization-level "default machine profile" and no organization-level "default process profile"**: those interim settings are removed. If a printer being sliced for is missing its machine profile or its process selection, the system reports exactly what is unconfigured **for that printer** rather than substituting any global default.

**Why this priority**: This is the actual defect the operator hit — slicing driven by one global default machine/process is wrong for a multi-model farm. Making resolution fleet-driven and removing the misplaced org-level defaults is the core value of this feature.

**Independent Test**: Configure two printers of different models, each with its own machine profile and a compatible process profile; trigger a slice for each; verify each uses its own machine and process presets. Then clear one printer's process selection and verify its slice fails with a message naming the missing process profile for that specific printer — with no org-level default ever consulted.

**Acceptance Scenarios**:

1. **Given** two printers of different models, each with its own machine profile and selected process profile, **When** a slice is requested for each, **Then** each slice is driven by that printer's own machine and process presets, not a shared global default.
2. **Given** a printer whose machine profile or process selection is missing, **When** a slice is requested for it, **Then** the system reports exactly which is missing for that printer and produces no slice, without falling back to any org-level default.
3. **Given** the interim organization-level "default machine profile" and "default process profile" settings previously existed, **When** this feature is in place, **Then** those settings no longer exist anywhere in the product and nothing in slicing reads them.
4. **Given** an estimate-only slice not tied to one specific target printer (e.g. deriving a part's print-time/material estimate at upload), **When** it runs, **Then** it derives its machine and process presets from a representative member of the current fleet — still fleet-derived, never an org-level default — and refuses with a clear reason when the fleet has no usable member.

---

### User Story 3 - Process profiles come from the bundled slicer, not from imports (Priority: P1)

The set of process profiles an operator can choose from is provided by the slicer OpenFarm already bundles — its built-in system process presets, organized per supported machine model. The operator never imports a process profile from their own bundle and never hand-authors or edits one; they only ever select from what the slicer supplies. The interim process-profile import path, the raw-data editor for process profiles, and the settings-page process default are all removed.

**Why this priority**: This defines what the choices in Stories 1–2 actually are and removes the mis-modeled interim surfaces. It is foundational: the selection and slicing stories are only coherent once the source of process profiles is fixed as "from the slicer, read-only."

**Independent Test**: On an install with a bundled slicer present, verify the process-profile choices available for a supported-model printer come from the slicer's own presets with no prior user import; verify there is no way to import or hand-create a process profile, and no raw-data editor for one.

**Acceptance Scenarios**:

1. **Given** a bundled slicer is present and a printer of a supported model exists, **When** the operator views that printer's process-profile choices, **Then** at least the slicer's built-in process presets for that model are offered, with no bundle import having occurred.
2. **Given** the product surfaces for adding profiles, **When** the operator looks for a way to import or hand-define a process profile, **Then** none exists — process profiles are read-only, slicer-sourced entries.
3. **Given** a bundle upload that also contains process presets, **When** the operator imports it, **Then** machine and filament presets import as before and process presets are ignored (not added as user process profiles), consistent with process profiles being slicer-sourced only.
4. **Given** no bundled slicer is present, **When** the operator opens a printer's process-profile choices, **Then** the system clearly reports that no process profiles are available until a slicer is bundled/provisioned, rather than showing an empty silent state.

---

### Edge Cases

- What happens when the operator tries to slice for a printer that has a machine profile but no process profile selected? The slice is refused with a message naming the missing process selection **for that printer** — never silently substituting anything.
- What happens to installs that already set the interim organization-level default machine/process profile, or imported process profiles into the interim catalog? Those org-level default settings and the imported-process path are removed; any behaviour that depended on them is replaced by fleet-driven, slicer-sourced resolution, and no slice silently keeps using a stale global default.
- What happens when the bundled slicer offers no process preset compatible with a printer's machine model (e.g. an unusual imported machine profile that doesn't map to any bundled model)? The printer's process-profile choices are empty and the operator is told no compatible process profile is available for that machine, rather than offering an incompatible one.
- What happens when the selected slicer engine/version changes (feature 008) and its built-in process presets differ? A printer's process selection is re-resolved against the active slicer; if the previously selected process profile is no longer available, the printer is flagged as needing a new selection rather than silently slicing with a substitute. *(Exact re-resolution behaviour to be settled in planning — see Assumptions.)*
- What happens when the fleet is empty and an estimate-only (no specific target printer) slice is requested? With no fleet member to derive a representative machine/process from, the slice is refused with a clear "no printers configured" reason — consistent with there being no org-level default to fall back on.
- How does the printer page stay usable when a machine model has many built-in process presets? The choices stay searchable/scannable rather than an unmanageable list.

## Requirements *(mandatory)*

### Functional Requirements

- **FR-001**: System MUST source the available process profiles from the **bundled slicer's built-in system process presets**, organized per supported machine model. Process profiles MUST be read-only, slicer-supplied entries — the system MUST NOT provide any way to import a process profile from a user bundle or to hand-author/edit one.
- **FR-002**: System MUST allow the operator to select a printer's process profile **on the printer page**, in the same manner a machine profile is selected, and MUST persist that selection **on the printer** (per-printer, not per machine profile and not per organization).
- **FR-003**: System MUST offer, as a printer's process-profile choices, only process profiles the bundled slicer provides that are compatible with that printer's attached machine, and MUST prevent selecting one incompatible with the printer's machine.
- **FR-004**: System MUST make process-profile selection unavailable for a printer that has no machine profile attached (there is nothing to determine compatibility against), prompting the operator to attach a machine profile first.
- **FR-005**: System MUST resolve the machine and process presets for a slice **from the fleet** — from the target printer's attached machine profile and selected process profile — for every slice tied to a specific target printer.
- **FR-006**: System MUST NOT provide any **organization-level "default machine profile"** or **organization-level "default process profile"** setting; the interim settings-page defaults for machine and process profiles MUST be removed, and no slicing path may read them.
- **FR-007**: System MUST, when a slice tied to a specific printer cannot be completed because that printer's machine profile or process selection is missing (or its resolved presets are unusable), report exactly what is missing **for that printer** and produce no slice — never substituting an organization-level default.
- **FR-008**: System MUST, for an estimate-only slice not tied to one specific target printer, derive its machine and process presets from a representative member of the current fleet rather than from any organization-level default, and refuse the slice with a clear reason when the fleet has no usable member.
- **FR-009**: System MUST capture selecting, changing, or clearing a printer's process profile in the activity log, consistent with how other state-mutating fleet changes are logged.
- **FR-010**: System MUST remove the interim process-profile import behaviour so that importing a slicer bundle continues to import machine and filament presets but does not create user-owned/editable process profiles from it.
- **FR-011**: System MUST report clearly, when no bundled slicer (or no slicer providing process presets for a printer's machine model) is available, that no process profiles can be offered — rather than presenting an empty, unexplained selection.
- **FR-012**: System MUST leave the per-material **filament** default selection unchanged — this feature removes only the organization-level *machine* and *process* profile defaults; filament-for-a-material selection is out of scope here. *(Documented assumption; revisit only if filament selection should also become fleet-driven.)*

### Key Entities *(include if data involved)*

- **Process Profile (slicer-sourced)**: A named, read-only "how to print" preset (layer height, speeds, supports, infill, etc.) supplied by the bundled slicer, associated with one or more supported machine models it is compatible with. Not imported, not hand-authored, not edited by the operator. Identified stably enough to be re-resolved against the active slicer engine/version at slice time.
- **Printer process selection**: The per-printer record of which slicer-sourced process profile a printer uses, sitting alongside the printer's machine-profile link on the same fleet surface. Replaces the removed organization-level default process profile. Must reference a process profile compatible with the printer's attached machine.

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: 100% of slices tied to a specific printer draw their machine and process presets from that printer's own fleet selection; 0% consult an organization-level default machine or process profile (there is none).
- **SC-002**: In a fleet of at least two different printer models, each model's slice uses a model-compatible process profile — no incompatible or cross-model process preset is ever silently applied.
- **SC-003**: 100% of slices that cannot resolve a required machine profile or process selection for the target printer fail with a message that names the specific missing item and the specific printer, and produce no partial slice.
- **SC-004**: An operator can go from a fresh install with a bundled slicer and a supported-model printer to a successful slice by selecting a slicer-provided process profile on the printer page — **without importing any process profile**.
- **SC-005**: 0 organization-level "default machine profile" or "default process profile" settings, and 0 process-profile import/hand-edit surfaces, remain anywhere in the product after this feature ships.
- **SC-006**: An operator can select a compatible process profile for a printer and obtain a successful slice for it in no more than 3 interactions starting from the printer's page.
- **SC-007**: Two printers of the same model can be given different process profiles, and each slices with its own — verifiable in a single test.

## Assumptions

- The bundled slicer(s) OpenFarm already ships/provisions (feature `008-orca-slicer-multi-version`) include built-in system process presets per machine model in their bundled resources; discovering these extends the existing bundled-slicer scanning rather than adding a new acquisition path. Because these presets travel with the slicer OpenFarm already bundles, this introduces no separate seeding or third-party profile-data licensing concern (consistent with `docs/FEATURE_PLAN.md`'s "never bundle default profile data as our own" intent — we surface the slicer's own presets, we don't license or ship a separate profile dataset).
- A machine profile remains **user-imported** (feature `003-slicer-profile-import`) and is attached to a printer via the fleet (feature `005-printer-fleet-refinements`); only the **process** leg changes to slicer-sourced. Because a user's imported machine profile derives from the slicer's own built-in machine preset for that model, the slicer's built-in process presets for that model are expected to be compatible with it — but this machine⇄process compatibility (built-in process against user-imported machine, including across slicer versions) MUST be validated during planning (`/speckit-plan` research), since strict machine⇄process compatibility is a hard slicer constraint and the single most likely failure point.
- **Cross-slicer portability** of process presets (the user's open question) is not a blocker under this model: process profiles are always read from whichever slicer engine/version is active, so each slicer supplies its own. Whether a printer's process *selection* should remain stable when the active slicer engine/version changes — and how to re-resolve or re-prompt when it can't — is a planning concern (see Edge Cases), not a scope decision needing further input now.
- The organization-level *filament-for-a-material* default (also on the interim "Slicing Profile Defaults" settings surface) is **left as-is** (FR-012). Filament selection is about the material loaded rather than the machine model, so it is treated separately and kept out of this feature's scope. This is a documented default, not a settled preference.
- Because the real permission catalog isn't fully enforced yet, this feature requires only that a user be authenticated for viewing choices and selecting a printer's process profile — consistent with the auth posture of features 003–008.
- "Supported machine model" means a model in OpenFarm's existing supported catalog (e.g. the Bambu X1C/P1S/A1/A1MINI entries in `PrinterModelCatalog`); the process presets offered for a printer are keyed off its machine model as recognized there.

## Dependencies

- Builds on `005-printer-fleet-refinements` (printer⇄machine-profile fleet link, which the per-printer process selection sits alongside) and `008-orca-slicer-multi-version` (bundled-slicer discovery, which is extended to surface built-in process presets).
- Rewires the slicing invocation layer (`ResolveSlicingProfileSelection`) from organization-level defaults to per-printer, fleet-driven, slicer-sourced resolution.
- Removes interim surfaces introduced ad hoc: process-profile bundle import, the process raw-data editor, the machine-profile→process pairing, and the "Slicing Profile Defaults" org-level machine/process defaults.
