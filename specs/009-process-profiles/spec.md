# Feature Specification: Process Profiles (Fleet-Driven, Imported Like Machine Profiles)

**Feature Branch**: `009-process-profiles`

**Created**: 2026-07-21

**Status**: Draft

**Input**: User description: "previous after implemented the last spec there is still some problem with slicing. So I try to fix it but it's beyond the spec, it still have problem because of missing process profile and now I want to go through proper spec kit flow. The main problem is it introduces process profile in setting page. I want [process] profile to be like machine profile, and I don't want any default machine and process profile, it has to depends on the fleet, totally, just like how machine profile defined in the previous spec. Also, we need to research and see how to import default process profile, I think we need to have predefined process profile in our application for each supported machine model by default, but I'm not sure whether process profile in different slicer is the same or not?"

## Context & Problem *(informative)*

Headless slicing (OrcaSlicer / Bambu Studio driven by OpenFarm) needs three preset inputs to produce toolpaths: a **machine** preset (the printer), a **process** preset (layer height, speeds, supports, infill — "how" the object is printed), and a **filament** preset (the material). Machine and filament profiles are already a first-class imported catalog (feature `003-slicer-profile-import`), linked to the fleet — a printer is tied to its machine profile on the printer's own page (feature `005-printer-fleet-refinements`).

The **process** preset is the missing third leg. An interim fix bolted it on in a way that doesn't match how the rest of the fleet works: it introduced an organization-level **"default machine profile"** and a **paired default process profile**, surfaced and configured on a **Settings** page ("Slicing Profile Defaults"). That is the wrong shape for a print farm:

- A farm has many printers of different models. A single org-wide "default machine profile" cannot be correct for a heterogeneous fleet — the machine (and therefore the compatible process preset) must be **derived from the printer being sliced for**, not from one global setting.
- Process profiles should be acquired, browsed, and corrected the **same way machine and filament profiles are** — imported from slicer bundles into a catalog — not configured as a settings-page default.

This feature makes the process profile a peer of the machine and filament profile: **imported into a catalog**, **attached to the fleet**, and **selected for slicing entirely off the fleet** — with **no organization-level default machine or process profile**.

## User Scenarios & Testing *(mandatory)*

### User Story 1 - Import process profiles from a slicer bundle (Priority: P1)

An operator who already has tuned process presets in their slicer uploads a bundle to OpenFarm. The system extracts every process preset it recognizes and shows them in the same review/selection list it already uses for machine and filament presets. The operator picks which process presets to keep and confirms; only the selected process presets are saved into a Process Profile catalog, preserving any base/override (inheritance) relationship the source bundle declared — exactly as machine and filament profiles are imported today.

**Why this priority**: This is the acquisition path for process data. Without process profiles in a catalog, slicing has no "how to print" input and every downstream story (attaching to the fleet, slicing) has nothing to act on. It is the direct analogue of feature 003's Story 1 for the third preset type.

**Independent Test**: Upload a known-good bundle containing machine, process, and filament presets; select a subset that includes at least one process preset; confirm; verify exactly the selected process presets — and only those — now exist in the Process Profile catalog, with any declared base/override link intact.

**Acceptance Scenarios**:

1. **Given** a valid slicer bundle containing machine, process, and filament presets, **When** the operator uploads it, **Then** the review/selection list presents the process presets alongside the machine and filament presets, each labelled with its type, before anything is saved.
2. **Given** the selection list, **When** the operator selects one process preset and confirms, **Then** exactly that process profile is created in the catalog and unselected process presets leave no trace.
3. **Given** a bundle with a base process preset and a derived process preset that overrides only a few fields, **When** the operator imports both, **Then** the derived process profile is stored with a reference to its base rather than flattened into an independent copy.
4. **Given** a derived process preset whose declared base is a built-in slicer default not present in the bundle, **When** the operator imports it on its own, **Then** it imports successfully with the base recorded as an unresolved reference (an expected outcome, not an error), consistent with machine/filament import behaviour.

---

### User Story 2 - Slice for a printer using fleet-derived profiles, with no org-level defaults (Priority: P1)

When OpenFarm slices for a given printer, it resolves the machine and process presets **from that printer's place in the fleet** — the printer's linked machine profile, and the process profile attached through the fleet — plus the filament for the target material. There is **no organization-level "default machine profile" and no organization-level "default process profile"**: those settings are removed. If a printer that is being sliced for is missing its required machine or process profile, the system reports precisely what is unconfigured for that printer rather than silently substituting a global default.

**Why this priority**: This is the actual bug the operator hit — slicing driven by a single global default machine/process profile is wrong for a multi-model farm. Fixing resolution to be fleet-driven, and removing the misplaced org-level defaults, is the core value of this feature.

**Independent Test**: Configure two printers of different models, each with its own machine + process profile; trigger a slice for each; verify each slice uses that printer's own machine and process profiles. Then remove/omit a printer's process profile and verify the slice for that printer fails with a message naming the missing process profile for that specific printer — with no org-level default ever consulted.

**Acceptance Scenarios**:

1. **Given** two printers of different models, each with its own attached machine and process profile, **When** a slice is requested for each printer, **Then** each slice is driven by that printer's own machine and process profiles, not a shared global default.
2. **Given** a printer whose machine or process profile is not configured, **When** a slice is requested for it, **Then** the system reports exactly which profile is missing for that printer and produces no slice, without falling back to any org-level default.
3. **Given** the interim organization-level "default machine profile" and "default process profile" settings previously existed, **When** this feature is in place, **Then** those organization-level default settings no longer exist anywhere in the product and nothing in slicing reads them.
4. **Given** an estimate-only slice that is not tied to one specific target printer (e.g. deriving a part's print-time/material estimate at upload), **When** it runs, **Then** it selects a representative printer from the current fleet and uses that printer's fleet-attached machine and process profiles — still fleet-derived, never an org-level default.

---

### User Story 3 - Attach a process profile to the fleet (Priority: P2)

An operator ties a process profile to the fleet so that slicing for a printer resolves the correct "how to print" settings — managed on the fleet / profile surfaces the operator already uses for machine profiles, **not** on a Settings page. Because slicers enforce strict machine⇄process compatibility (a process preset is only valid for the exact machine it was authored for), the operator can only attach a process profile that is compatible with the printer's machine.

**Why this priority**: Story 2's fleet-driven resolution needs a place for the operator to declare which process profile a printer uses. It's the connective tissue between the imported catalog (Story 1) and correct slicing (Story 2), mirroring how a machine profile is attached to a printer today.

**Independent Test**: For a printer that has a machine profile attached, attach a compatible process profile through the fleet surface; verify a subsequent slice for that printer uses it. Attempt to attach an incompatible process profile and verify it is prevented with a clear reason.

**Acceptance Scenarios**:

1. **Given** a printer with a machine profile attached, **When** the operator attaches a process profile through the fleet surface, **Then** subsequent slices for that printer use that process profile.
2. **Given** the operator is choosing a process profile to attach, **When** they view the choices, **Then** they are shown/limited to process profiles compatible with the printer's machine, and attaching an incompatible one is prevented with a clear reason.
3. **Given** a process profile attached to the fleet, **When** the operator changes or clears it, **Then** the change takes effect for the next slice and is captured in the activity log like other fleet changes.

---

### User Story 4 - Browse and correct the process profile catalog (Priority: P2)

An operator or admin can see every imported process profile in its own catalog list (peer to the machine and filament catalogs), open a single process profile to view its full raw data, and correct that raw data through the same JSON editor used for machine and filament profiles.

**Why this priority**: Parity with the existing machine/filament catalogs closes the loop — import isn't verifiable or maintainable without a way to review and occasionally correct what landed. It builds on Story 1 but isn't a blocker for the core slicing fix.

**Independent Test**: Import process profiles, open the process profile catalog, confirm each imported process profile is listed with its key identifying fields; open one, edit a field via the JSON editor, save, and confirm the catalog reflects the change; submit malformed JSON and confirm it's rejected with the prior data unchanged.

**Acceptance Scenarios**:

1. **Given** process profiles have been imported, **When** the operator opens the process profile catalog, **Then** every imported process profile is listed with its name and whether it is a base or an override.
2. **Given** a process profile, **When** the operator opens its raw-data editor, edits a field, and saves, **Then** the catalog reflects the updated value.
3. **Given** the operator edits a process profile's raw data, **When** they submit malformed data, **Then** the save is rejected with a clear error and the previously saved data is unchanged.

---

### User Story 5 - Start from predefined process profiles per supported machine model (Priority: P3)

So that an operator isn't forced to hand-tune or hand-import a process preset for every machine before they can slice, OpenFarm offers a set of **predefined process profiles for each machine model it supports** (e.g. the Bambu models in its supported catalog), which the operator can adopt into their own catalog and attach to matching printers.

> ⚠️ **Scope of this story is pending a decision** — see [NEEDS CLARIFICATION: Q1] in Functional Requirements. Whether OpenFarm ships/seeds predefined process profiles at all **reverses a ratified design decision** (`docs/FEATURE_PLAN.md`: "Phase 2 is import-your-own-bundle only, so OpenFarm never bundles or needs to license third-party slicer profile data as defaults"), and depends on whether a process preset is portable across the supported slicers. If the decision is "import-only," this story is dropped from scope.

**Why this priority**: A quality-of-life accelerant, not a blocker — Stories 1–4 already give a complete import-and-slice loop without it. It is called out separately because it is the part of the request with genuine open questions (licensing, cross-slicer portability) that need resolving before it can be committed to.

**Independent Test** *(only if Q1 resolves to "seed")*: On a fresh install with a supported-model printer registered, the operator can adopt a predefined process profile for that model into their catalog and attach it to the printer without importing any external bundle, and a slice for that printer then succeeds.

**Acceptance Scenarios** *(only if Q1 resolves to "seed")*:

1. **Given** a fresh install with a printer of a supported model, **When** the operator opens process-profile choices for that printer, **Then** at least one predefined process profile for that model is offered without any prior bundle import.
2. **Given** a predefined process profile, **When** the operator adopts it, **Then** it appears in the operator's own catalog and behaves like any imported process profile thereafter (attachable, editable, browsable).

---

### Edge Cases

- What happens when a bundle contains process presets but no machine or filament presets (or vice versa)? Each recognized preset type is presented for selection independently; the absence of one type does not block importing the others.
- What happens when the operator tries to slice for a printer that has a machine profile but no process profile attached? The slice is refused with a message naming the missing process profile **for that printer** — never silently substituting anything.
- What happens to installs that already set the interim organization-level default machine/process profile before this feature? Those org-level default settings are removed; any behaviour that depended on them is replaced by fleet-driven resolution, and no slice silently keeps using a stale global default.
- What happens when a process preset's declared base is a built-in slicer default never present in any bundle? It imports with an unresolved base reference, identical to machine/filament behaviour — not an error.
- What happens when an operator tries to attach a process profile that isn't compatible with the printer's machine? It is prevented with a clear reason rather than allowing an unsliceable pairing.
- What happens when the fleet is empty and an estimate-only (no specific target printer) slice is requested? With no fleet member to derive a representative machine/process from, the slice is refused with a clear "no printers configured" reason — consistent with there being no org-level default to fall back on.
- How does the process profile catalog stay usable when a large bundle yields many process presets? The list stays searchable/scrollable, same as the machine/filament catalogs.

## Requirements *(mandatory)*

### Functional Requirements

- **FR-001**: System MUST treat a **Process Profile** as a first-class imported catalog entry, peer to Machine Profile and Filament Profile — extracted from the same supported slicer bundle formats, presented in the same review/selection list, and persisted only when the operator explicitly selects it.
- **FR-002**: System MUST preserve any base/override (parent/child) inheritance a process preset declares in the source bundle by storing the derived process profile with a reference to its declared base, retaining the reference by name even when the base is an unimported built-in slicer default (an unresolved reference is expected, not an error) — identical to Machine/Filament Profile import behaviour.
- **FR-003**: System MUST allow any authenticated user to browse the full Process Profile catalog, view a single process profile's full raw data, and correct that raw data through the same raw-data (JSON) editor used for machine and filament profiles, rejecting malformed input and leaving prior data unchanged on rejection.
- **FR-004**: System MUST resolve the machine and process presets used for a slice **from the fleet** — specifically, from the target printer's fleet-attached machine profile and process profile — for every slice tied to a specific target printer.
- **FR-005**: System MUST NOT provide any **organization-level "default machine profile"** or **organization-level "default process profile"** setting; the interim settings-page defaults for machine and process profiles MUST be removed, and no slicing path may read them.
- **FR-006**: System MUST allow the operator to attach a process profile to the fleet so that slices for a printer resolve the correct process settings, managed on the same fleet/profile surfaces used for machine profiles — **not** on a Settings page. [NEEDS CLARIFICATION: Q2 — is the process profile attached **directly to each printer** (parallel to the machine-profile link, "totally fleet"), or **paired to a machine profile** which printers inherit through their machine link? The interim implementation did the latter; the request ("depends on the fleet, totally, just like how machine profile") suggests the former.]
- **FR-007**: System MUST enforce machine⇄process compatibility when a process profile is attached to the fleet: only a process profile compatible with the printer's machine may be attached, and an incompatible attachment MUST be prevented with a clear reason.
- **FR-008**: System MUST, when a slice tied to a specific printer cannot be completed because that printer's machine or process profile is missing or has an unresolved inheritance chain, report exactly which profile is missing **for that printer** and produce no slice — never substituting an organization-level default.
- **FR-009**: System MUST, for an estimate-only slice not tied to one specific target printer, derive its machine and process presets from a representative member of the current fleet rather than from any organization-level default, and refuse the slice with a clear reason when the fleet has no usable member.
- **FR-010**: System MUST capture attaching, changing, or clearing a printer's process profile in the activity log, consistent with how other state-mutating fleet changes are logged.
- **FR-011**: System MUST keep the per-material **filament** default selection unchanged in scope by this feature — this feature only removes the organization-level *machine* and *process* profile defaults; filament-for-a-material selection is out of scope here. [Assumption — see Assumptions; revisit only if the operator wants filament selection made fleet-driven too.]
- **FR-012**: System MUST offer predefined process profiles for each supported machine model that an operator can adopt into their own catalog without importing an external bundle. [NEEDS CLARIFICATION: Q1 — does OpenFarm ship/seed predefined process profiles at all? This reverses `docs/FEATURE_PLAN.md`'s ratified "import-your-own-bundle only, never bundle default profile data" decision and carries licensing + cross-slicer-portability questions. If the decision is "import-only," this FR and User Story 5 are removed from scope.]

### Key Entities *(include if data involved)*

- **Process Profile**: A named catalog entry describing "how" an object is printed (layer height, speeds, supports, infill, etc.) as extracted from a slicer bundle. Carries the name of the base process profile it overrides, if any (possibly an unresolved built-in reference), and the source format it came from — structurally the same shape as Machine Profile and Filament Profile. A process profile is meaningful only in the context of a specific machine (strict machine⇄process compatibility).
- **Fleet process-profile attachment**: The link that ties a process profile to the fleet so slices for a printer resolve their process settings. Whether this link lives on the printer directly or on the machine profile is pending Q2. Replaces the removed organization-level default process profile.
- **Predefined process profile set** *(conditional on Q1)*: A ready-made collection of process profiles per supported machine model, offered for adoption on a fresh install so slicing works without an external bundle import.

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: 100% of slices tied to a specific printer draw their machine and process presets from that printer's own fleet attachment; 0% consult an organization-level default machine or process profile (there is none).
- **SC-002**: In a fleet of at least two different printer models, each model's slice uses its own model-appropriate process profile — no cross-model process preset is ever silently applied.
- **SC-003**: 100% of slices that cannot resolve a required machine or process profile for the target printer fail with a message that names the specific missing profile and the specific printer, and produce no partial slice.
- **SC-004**: A process profile can be imported from a bundle and appear in its catalog through the same review/confirm flow as machine and filament profiles, in under 2 minutes for a typical bundle (under 50 combined presets).
- **SC-005**: 0 organization-level "default machine profile" or "default process profile" settings remain anywhere in the product after this feature ships.
- **SC-006**: An operator can attach a compatible process profile to a printer and get a successful slice for it in no more than 3 interactions starting from the printer's fleet surface.
- **SC-007** *(conditional on Q1)*: On a fresh install with a supported-model printer, an operator can go from zero to a successful slice using a predefined process profile without importing any external bundle.

## Assumptions

- Supported slicer bundle formats and the review/select/confirm import mechanics are those already established by feature `003-slicer-profile-import`; this feature extends that same pipeline to the process preset type rather than inventing a new import path. Both currently supported slicers (OrcaSlicer and Bambu Studio) share the same bundle structure and inheritance model, so process presets are extracted the same way machine/filament presets already are.
- A printer is already linked to its machine profile via the fleet (feature `005-printer-fleet-refinements`); this feature adds the process leg to that same fleet-driven resolution rather than changing how the machine profile is attached.
- The organization-level *filament-for-a-material* default (also currently on the interim "Slicing Profile Defaults" settings surface) is **left as-is** — the user's request targets removing the *machine* and *process* org-level defaults specifically. Filament selection is inherently about the material loaded rather than the machine model, so it is treated separately and kept out of this feature's scope (FR-011). This is a documented default, not a settled preference — flag if filament selection should also become fleet-driven.
- Strict machine⇄process compatibility is a hard constraint of the underlying slicers (empirically confirmed in `ResolveSlicingProfileSelection`'s notes: no `--no-check`, absent process file, or stripped compatibility list satisfies a mismatched pair). Attaching a process profile therefore must be constrained to the printer's machine, and slicing cannot fall back to a generic/global process.
- Because the real permission catalog isn't fully enforced yet, this feature requires only that a user be authenticated for import, browse, edit, and fleet-attach actions — consistent with the auth posture of features 003–007.
- **Cross-slicer portability of process presets is an open research question** the user explicitly raised. The user's prior experience suggests process presets are largely the same across slicers; the supported slicers (OrcaSlicer, Bambu Studio) do share a common lineage and near-identical process-preset shape, but this must be verified (and the answer differs for other slicers) during planning/research (`/speckit-plan`). This directly gates whether a single predefined set can serve all supported slicers or whether per-slicer sets are needed — and thus feeds Q1.

## Dependencies

- Builds directly on `003-slicer-profile-import` (import pipeline, catalog + raw-data editor, base/override handling) and `005-printer-fleet-refinements` (printer⇄machine-profile fleet link).
- Interacts with `008-orca-slicer-multi-version` / the slicing invocation layer (`ResolveSlicingProfileSelection`), which this feature rewires from org-level defaults to fleet-driven resolution.
