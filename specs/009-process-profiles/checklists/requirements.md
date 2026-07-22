# Specification Quality Checklist: Process Profiles — System & User Profiles for Machine, Process, and Filament

**Purpose**: Validate specification completeness and quality before proceeding to planning
**Created**: 2026-07-21
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

- Six clarifications resolved across two sessions on 2026-07-21 (see spec.md § Clarifications), all folded into the spec:
  - **Q1** — Process profiles are **sourced from the bundled slicer** (built-in per-model presets); no user import, no custom/hand-authored process profiles.
  - **Q2** — A process profile is attached **directly per-printer**, alongside the machine-profile link.
  - **Q3** — System Profile ingestion during provisioning covers **all three kinds** (machine, process, filament), not process alone — grounded against a real Bambu Lab vendor index (`BBL.json`). Used to resolve previously-unresolved base/override references on User-imported profiles too.
  - **Q4** — Every picker offering a profile of a given kind lists **System- and User-sourced entries together, labeled by origin**. Machine + process selection lives on the **printer page**; filament narrowing lives on the **part detail page**. The existing import/browse page (feature 003) stays scoped to **User-imported profiles only**.
  - **Q5** — System Profile ingestion is **scoped to vendors matching OpenFarm's supported machine model catalog** (`PrinterModelCatalog`), tracking it automatically as it grows — not every vendor the bundled slicer ships.
  - **Q6** — The organization-level per-material **filament default is removed too**, for consistency with removing the machine/process defaults. The entire interim "Slicing Profile Defaults" settings page is now gone; an unnarrowed part filament requirement resolves automatically to a representative System filament profile per material type instead.
- All checklist items pass. Spec is ready for `/speckit-plan`.
- Deliberately deferred to planning research (not spec gaps): (1) validating machine⇄process compatibility between a user-imported machine profile and System process presets, including behaviour when the active slicer engine/version changes; (2) re-ingestion idempotency and how a printer's selection is flagged when its previously-selected System Profile disappears from a re-ingested set; (3) mapping a vendor index's human-readable machine names to OpenFarm's internal `PrinterModelCatalog` entries; (4) the tie-breaking rule for which System filament profile is "representative" when a material type has more than one ingested candidate.
