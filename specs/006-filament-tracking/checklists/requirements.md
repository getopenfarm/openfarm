# Specification Quality Checklist: Filament Tracking

**Purpose**: Validate specification completeness and quality before proceeding to planning
**Created**: 2026-07-18
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

- All checklist items pass. The roadmap in `docs/FEATURE_PLAN.md` (Phase 3 — Filament tracking) already resolved every open design question for this feature, so no [NEEDS CLARIFICATION] markers were needed; forward dependencies on later phases (auto-reload feed-slot configuration from Phase 4, formal job/consumption tracking from Phase 8) are captured explicitly in the Assumptions section instead.
- 2026-07-18 clarification session: introduced the Filament (type) / Spool (instance) split plus a separate Vendor entity, per the existing `web/src/features/filament` and `web/src/features/spools` mockups. Re-validated — all items still pass.
- 2026-07-18 follow-up revision: Filament color variants no longer define weight options — starting weight is entered freely per spool at creation time instead. Re-validated — all items still pass.
- 2026-07-18 second follow-up revision: spool creation now captures two fields — Spool Weight (predefined-list or custom) and Remaining Weight (defaults to Spool Weight, lowerable at creation for already-partially-used spools). FR numbering shifted (new FR-002; old FR-002–017 renumbered to FR-003–018). Re-validated — all items still pass.
- 2026-07-18 third follow-up revision: Feed Slots are now strictly derived from a printer's live connection (never operator-created/renamed), covering both Multi-Material Unit slots and an External Spool holder under a generic kind with a brand-computed display label; "auto-reload" was renamed "Automatic Filament Reloader (AFR)" throughout; Multi-Material slot RFID-detected material/color now factors into spool loading as a non-blocking validation warning. Added FR-005a, FR-005b, FR-019, FR-020, FR-021; narrowed the "zero live printer connection required" assumption to cover only catalog/inventory management, not loading a spool onto a Feed Slot. Re-validated — all items still pass; no implementation-detail leakage (RFID, Multi-Material Unit, and External Spool are domain/hardware concepts, not technology choices).
- 2026-07-18 fourth follow-up revision (post real-hardware research — Bambu H2D/H2C, Snapmaker U1): corrected FR-021 — detected-material comparison is no longer categorically excluded for External Spool; real hardware (Snapmaker U1's individually-RFID-capable inputs) disproves that exclusion, so the check now applies uniformly to any Feed Slot kind, gated only on live per-slot data presence. Added FR-023 (Multi-Material Unit automatically, not manually, treated as superseding the External Spool position — the presence is telemetry-observable, unlike the Automatic Filament Reloader case). Added an Assumption explicitly deferring a toolhead/nozzle concept for multi-toolhead printers (confirmed via research that Feed Unit's existing per-unit generalization already covers this without one). Re-validated — all items still pass.
- 2026-07-18 fifth follow-up revision: a superseded External Spool position (by either an Automatic Filament Reloader or a Multi-Material Unit) is now omitted from the default Feed Unit listing entirely, not just flagged advisory-only — mirroring the physical reality of one filament input per printer. Revised FR-022/FR-023 accordingly; added FR-024 (operator can manually reveal a hidden position, for bypass/multi-way connector setups) and FR-025 (a position with an actively-loaded spool always stays visible regardless of hidden/revealed state). Clarified this hide-by-default treatment never extends to multiple Multi-Material Units on the same printer (they form one unified pool, not alternate sources). Added an Assumption confirming a per-print "active source" toggle (mirroring a printer's own AMS enable/disable setting) is out of scope — a job-execution-time concern this phase has no entity for. Re-validated — all items still pass.
- 2026-07-18 sixth follow-up revision: unified declaring/removing an Automatic Filament Reloader and revealing/re-hiding an External Spool position into one pair of operator actions ("add a Feed Unit" / "remove a Feed Unit") rather than two separately-named mechanisms — revised FR-005c and FR-024 accordingly, added FR-026 (rejecting an attempt to reveal an External Spool position the printer has never actually reported). Re-validated — all items still pass.
