# Feature Specification: Build Plate Selection — Per-Printer Physical Plate, Configured Alongside Machine/Process Profile

**Feature Branch**: `012-build-plate-selection`

**Created**: 2026-07-30

**Status**: Draft

**Input**: User description: "There is one missing feature for printer, previously we implemented printer profile and process profile, but we forgot to implement build plate, research and see how it fits in, i prefer to add it at configure printer page, same with process profiles etc.. Also research see if there are any plate profile thingy."

## Context & Problem *(informative)*

Feature `009-process-profiles` gave each printer its own machine profile and process profile selection, both chosen on the printer's own page and both required before a slice can be resolved for that printer. A third input every slice also depends on — **which physical build plate is currently installed on the printer** — was never added anywhere, and nothing in the product lets an operator declare it today.

This gap is not cosmetic. Every ingested/imported filament preset already carries a *distinct bed-temperature value per plate type* (the same filament reaches a different target bed temperature on a Cool Plate than on a Textured PEI Plate or an Engineering Plate, per the bundled slicer's own vendor data). Slicing today silently reads one single fixed plate-type's temperature value for every filament and every printer, regardless of which plate is actually on the machine. On a print farm where different printers of the same model routinely carry different plates (a Cool Plate for PETG/ABS jobs, a Textured PEI Plate for PLA, an Engineering Plate for ABS/PC), this produces a bed temperature that silently doesn't match the plate that's physically installed.

Research into the bundled slicers' own profile data confirms there is **no separate "build plate profile" catalog** analogous to the machine/process/filament profiles ingested by `009-process-profiles` — vendors don't ship an importable "plate profile" bundle. Instead, "build plate" resolves to two much smaller things already latent in existing data:

1. A small, fixed set of named plate types (Cool Plate, Engineering Plate, High Temp Plate, Textured PEI Plate, Textured Cool Plate, Supertack Plate) drawn from the same set the bundled slicer's own UI offers, not something ingested or imported per organization — but whose relevant subset differs by exact machine model, not just brand: the vendor data confirms, for example, that a Bambu Lab A1's own "PLA Basic" preset only declares Textured PEI/High Temp Plate temperatures, an A2L's only declares an Engineering Plate temperature, and an H2D/P2S/X2D's declare Engineering **and** Supertack Plate temperatures (and explicitly exclude Textured Cool Plate via their own machine preset's `not_support_bed_type`) — for the exact same nominal filament. Textured Cool Plate itself is real and genuinely used today, just not by any currently-catalogued Bambu model — it's the factory default plate on non-Bambu OrcaSlicer-supported machines such as the Snapmaker U1 and Flashforge AD5X/G4-series.
2. A machine profile's own declared default plate type (present today in every ingested machine preset, e.g. a Bambu Lab X1 Carbon profile declares "Textured PEI Plate" as its factory default) and a filament profile's own per-plate-type bed-temperature values (already present in every ingested/imported filament preset's raw data, and already varying by exact machine model as shown above — just never read structurally by anything in the product today).

This feature adds the missing piece: a **per-printer build plate selection**, configured on the printer's page the same way machine and process profile already are, gated by the printer's own registered brand/model (set once at registration and immutable thereafter, per `005-printer-fleet-refinements`), and used at slice time to pick the bed-temperature value matching the plate that's actually installed — rather than the single fixed value the system silently assumes today.

## Clarifications

### Session 2026-07-30

- Q: Bambu Lab printers can report which build plate is physically inserted via their own live MQTT status, but OpenFarm's telemetry parser doesn't read it today. Should this feature rely purely on manual operator selection, or also auto-detect the plate from live telemetry? → A: Manual selection only — no live-telemetry parsing is added by this feature; the printer connector/telemetry layer is untouched.
- Q: The build plate choices are a small fixed named set (Cool Plate, Engineering Plate, High Temp Plate, Textured PEI Plate, Supertack Plate). Should a custom/other option with a manually-entered bed temperature be supported for third-party plates, or should selection stay strictly limited to the fixed named list? → A: Fixed list only — no custom/manual-temperature entry option; an operator with a third-party plate picks the closest matching named type.
- Q: Vendor data confirms different exact machine models (not just different brands) already declare a different relevant subset of plate types and different bed temperatures for the same filament (e.g. Bambu Lab A1's PLA Basic preset only defines Textured PEI/High Temp Plate temps, A2L's only defines Engineering Plate, H2D/P2S/X2D's define Engineering + Supertack Plate). Should build plate compatibility (FR-002) be required to reflect each exact machine model's own real compatible/relevant plate set, rather than one shared list applied to every model? → A: Yes — compatibility is required per exact machine model, sourced from that model's own vendor/machine data; bed-temperature resolution (FR-005) naturally inherits whatever value that specific model's own filament preset declares, with no separate per-model temperature logic needed.
- Q: `Printer.Brand`/`Printer.ModelCode` (set at registration, immutable per `005-printer-fleet-refinements`) is the printer's real physical identity — separate from `Printer.MachineProfileId`, a slicer-preset selection that today has no validation tying it back to the printer's own registered model. Since build plate is a real hardware fact, should its compatible choices be gated by the printer's own registered Brand/ModelCode directly, rather than by whichever machine profile happens to be linked? → A: Yes — gate by the printer's own registered Brand/ModelCode (via `PrinterModelCatalog`), independent of the linked machine profile. Since ModelCode is immutable and known from registration onward, build plate selection no longer needs to wait on a machine profile being selected first.
- Q: Direct inspection of the bundled slicer's real vendor profile bundle (not just the repo's narrow test fixtures) confirms a sixth canonical plate type, "Textured Cool Plate," genuinely used by other OrcaSlicer-supported vendors (e.g. Snapmaker U1, Flashforge AD5X/G4-series) — and explicitly named (only to be excluded) in current Bambu H2D/H2D Pro/H2S/P2S machine presets' own `not_support_bed_type` field — which the fixed list above doesn't include at all. Should the fixed named set be expanded from five to six plate types now, even though no currently-catalogued Bambu model uses it? → A: Yes — expand the fixed set to six: Cool Plate, Engineering Plate, High Temp Plate, Textured PEI Plate, Textured Cool Plate, Supertack Plate; this both completes the enum against the real slicer schema and is a prerequisite for any future non-Bambu `PrinterModelCatalog` entry (005/future work) whose factory-default plate is Textured Cool Plate.
- Q: The same inspection found that some Bambu machine presets (e.g. A1 mini, H2D, H2S, P2S) declare an explicit `not_support_bed_type` denylist in their own raw preset data — a more directly vendor-declared signal than inferring compatibility purely from which plate-type bed-temperature keys a filament preset leaves non-zero. When both signals are available for a model, which should take precedence when sourcing that model's compatible plate set? → A: `not_support_bed_type` is authoritative when a model's own machine preset declares it (subtract those types from the full fixed set); the filament-preset bed-temperature-key signal remains the sourcing method for any model whose machine preset doesn't declare `not_support_bed_type` at all.

### Session 2026-07-31

- Q: Machine profile and process profile are both maintained solely in the backend (ingested vendor data) and expose their display names to the frontend as plain resolved strings (`machineProfileName`, `processProfileName`) — the frontend never maintains its own copy of the list or a name lookup. Build plate is a fixed backend enum rather than an ingested catalog; should its human-readable label follow the same rule — resolved and served by the backend alongside each enum code — so the frontend never hand-maintains its own enum-to-label list? → A: Yes — the backend resolves and serves the friendly label for both the printer's current selection and every compatible choice; the frontend renders whatever label string it's given and holds no enum-to-label table of its own, matching the machine/process profile precedent exactly.

## User Scenarios & Testing *(mandatory)*

### User Story 1 - Select a printer's build plate on its configure page (Priority: P1)

On a printer's page — the same page where the operator already selects its machine profile and process profile — the operator selects which physical build plate is currently installed. Choices are limited to plate types compatible with the printer's own registered brand/model (the same identity fixed at registration and used to validate the printer against `PrinterModelCatalog`), independent of whichever machine profile happens to be linked for slicing purposes. Because that identity is known and immutable from registration onward, build plate selection is available immediately — it does not need to wait on a machine profile being selected first, unlike process profile.

**Why this priority**: This is the entire missing capability — without a place to declare which plate is installed, nothing downstream (slicing) can ever be correct for a printer whose plate differs from its machine's factory default.

**Independent Test**: Open any registered printer; verify build plate choices appear immediately, limited to types compatible with that printer's own registered brand/model, regardless of whether a machine profile has been linked yet; select one and save; verify the printer's page reflects the choice.

**Acceptance Scenarios**:

1. **Given** a registered printer, **When** the operator opens its build plate choices, **Then** only plate types compatible with that printer's own registered brand/model are offered, regardless of whether a machine profile is currently linked.
2. **Given** a printer with no machine profile linked at all, **When** the operator opens build plate choices, **Then** selection is still available, driven entirely by the printer's own registered brand/model.
3. **Given** the operator selects a build plate and saves, **When** the page reloads, **Then** the selection persists and is shown alongside the printer's machine and process profile.
4. **Given** two printers of the same model, **When** the operator gives each a different build plate, **Then** each printer keeps its own selection independently.
5. **Given** a printer's build plate selection is changed or cleared, **When** the operator saves, **Then** the change is captured in the activity log, consistent with how machine/process profile changes already are.

---

### User Story 2 - Slice using the bed temperature for the printer's actual installed plate (Priority: P1)

When OpenFarm slices for a given printer, the bed-temperature value it resolves for the chosen filament preset matches the printer's own selected build plate — not a single fixed plate type applied to every printer regardless of what's installed. Two printers of the same model and same filament, but different build plates, produce different bed temperatures in their slice output, matching what each plate actually requires.

**Why this priority**: This is the actual defect motivating the feature — a slice is only correct if the bed temperature matches the plate physically on the machine; without this, User Story 1's selection would be cosmetic.

**Independent Test**: Configure two printers of the same model with the same machine, process, and filament selections but different build plates; trigger a slice for each; verify the resolved bed temperature differs between them and matches each one's selected plate type.

**Acceptance Scenarios**:

1. **Given** a printer with a build plate selected, **When** a slice is requested for it, **Then** the resolved bed temperature matches the value the chosen filament preset declares for that specific plate type.
2. **Given** two printers of the same model with different build plates selected, **When** each is sliced with the same filament, **Then** each slice's resolved bed temperature differs to match its own plate.
3. **Given** a printer with no build plate selected, **When** a slice is requested for it, **Then** the system reports exactly that the build plate is unconfigured for that printer and produces no slice, without silently substituting any plate type.
4. **Given** an estimate-only slice not tied to one specific target printer, **When** it runs, **Then** it derives its build plate the same way it already derives machine and process — from a representative member of the current fleet's own selections — and refuses with a clear reason when the fleet has no usable member.

---

### Edge Cases

- What happens if a printer's linked machine profile belongs to a different model than the printer's own registered brand/model (an existing inconsistency the product doesn't prevent today, and which stays out of scope for this feature)? It has no effect on build plate — build plate compatibility is driven solely by the printer's own registered brand/model, never by whichever machine profile is linked. This also means build plate compatibility never needs re-flagging: a printer's registered brand/model is immutable after registration (`005-printer-fleet-refinements`), so nothing about it ever changes post-registration.
- What happens to printers that already exist when this feature ships, with a machine and process profile already selected but obviously no build plate (since the field didn't exist)? They start with no build plate selected and are treated the same as any printer missing a required selection today — slicing for them is refused with a clear message naming the printer, until the operator selects one.
- What happens when a filament preset has no bed-temperature value recorded for the printer's selected plate type? The slice is refused with a clear reason naming both the filament preset and the plate type, rather than falling back to a different plate's value.
- What happens when the fleet is empty and an estimate-only slice needs a representative build plate? Same as the existing "no printers configured" refusal for machine/process.
- What happens if a printer's live connection telemetry ever reports a build plate different from the operator's manual selection? Not reconciled by this feature — live telemetry is not read for build plate at all; the manual selection is the sole source of truth.
- What happens when an operator has a third-party/generic plate that doesn't exactly match any named type in the fixed list? They pick the closest matching named type; no custom/other option or manual bed-temperature entry exists.

## Requirements *(mandatory)*

### Functional Requirements

- **FR-001**: System MUST let the operator select a printer's build plate on that printer's own page, in the same place and manner machine and process profile are already selected (feature `009-process-profiles`).
- **FR-002**: System MUST offer, as a printer's build plate choices, only plate types compatible with that printer's own **registered brand/model** (fixed at registration, per `PrinterModelCatalog`) — determined per exact model, not a single list shared across every model, and independent of whichever machine profile is (or isn't) currently linked to the printer. Two printers of different registered models MUST NOT be offered identical build plate choices when their models' underlying data declares different compatible/relevant plate sets.
- **FR-003**: System MUST persist a printer's build plate selection per-printer (not per organization, not per machine model), so two printers of the same model may carry different selections.
- **FR-004**: System MUST NOT ingest, import, or otherwise treat build plate as a vendor-shipped profile catalog — it is a small, fixed set of named plate types, not a fourth profile kind alongside machine/process/filament.
- **FR-005**: System MUST resolve the bed-temperature value used in a slice from the combination of the target printer's selected build plate and the resolved filament preset's own per-plate-type bed-temperature data, for every slice tied to a specific printer.
- **FR-006**: System MUST, when a slice tied to a specific printer cannot be completed because that printer's build plate selection is missing, or the resolved filament preset has no bed-temperature value for the selected plate type, report exactly what is missing for that printer/filament and produce no slice.
- **FR-007**: System MUST, for an estimate-only slice not tied to one specific target printer, derive its build plate from a representative member of the current fleet's own selections, consistent with how machine and process are already derived for estimate-only slices, and refuse with a clear reason when the fleet has no usable member.
- **FR-008**: System MUST NOT gate, invalidate, or re-flag a printer's build plate selection based on its machine profile selection or any change to it — build plate compatibility depends solely on the printer's own registered brand/model (FR-002), which is immutable after registration, so no re-resolution trigger is needed.
- **FR-009**: System MUST capture selecting, changing, or clearing a printer's build plate in the activity log, consistent with how machine/process profile changes are already logged.
- **FR-010**: System MUST NOT provide a custom/other build plate option or a manually-entered bed-temperature override — selection is limited to the fixed named set (FR-002) with no free-form alternative.
- **FR-011**: System MUST NOT read or reconcile a printer's live connection telemetry against its build plate selection — the manual selection (FR-001) is the sole source of truth, with no auto-detection added by this feature.
- **FR-012**: System MUST resolve and serve the human-readable label for a printer's selected build plate and for each of its compatible choices from the backend, alongside the underlying enum code, so the frontend never maintains its own copy of the plate-type-to-label mapping — consistent with how machine and process profile display names are already served (`machineProfileName`, `processProfileName`).

### Key Entities *(include if data involved)*

- **Build Plate Type**: A named physical print-surface type (Cool Plate, Engineering Plate, High Temp Plate, Textured PEI Plate, Textured Cool Plate, Supertack Plate) drawn from a small, fixed set — not an operator-imported or vendor-ingested profile catalog, and with no custom/other entry (FR-010). Compatibility is a fixed property of the plate type / **printer's own registered brand-model** combination (`PrinterModelCatalog`; confirmed to differ model-by-model, not just brand-by-brand, in the underlying vendor data) — never of whichever machine profile happens to be linked — not something the operator curates. Sourcing that per-model compatible set reads two signals from the model's own ingested vendor data, in priority order: the model's own machine preset's `not_support_bed_type` field when declared (authoritative — subtract those types from the full six), falling back to which plate-type bed-temperature keys that model's own filament presets leave non-zero when `not_support_bed_type` isn't declared at all. Each type's human-readable label (e.g. `TexturedPei` → "Textured PEI Plate") is resolved by the backend from one canonical mapping and served alongside the code (FR-012) — the frontend has no label list of its own.
- **Printer build plate selection**: The per-printer record of which build plate type is currently installed, selected on the printer's page alongside its existing machine and process selections. Unset by default for every printer, including ones that existed before this feature.
- **Filament Profile** (existing, extended usage): Already carries a distinct bed-temperature value per build plate type in its underlying preset data (ingested/imported by feature `003-slicer-profile-import` / `009-process-profiles`); this feature is the first to read that per-plate-type data structurally rather than assuming one fixed plate type.

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: 100% of slices tied to a specific printer resolve their bed temperature from that printer's own selected build plate; 0% use a bed-temperature value for a plate type other than the one selected.
- **SC-002**: An operator can select a build plate for a printer, from model-compatible choices, in no more interactions than selecting that printer's process profile already takes today — available immediately on a freshly registered printer, with no machine profile prerequisite.
- **SC-003**: Two printers of the same model with different build plate selections produce different resolved bed temperatures for the same filament, verifiable in a single test.
- **SC-004**: 100% of slices that cannot resolve a required build plate selection, or a matching bed-temperature value, for the target printer fail with a message naming the specific missing item and the specific printer, and produce no partial slice.
- **SC-005**: 100% of build plate selection/change/clear actions are captured in the activity log.
- **SC-006**: 0 build-plate "profiles" are ever imported, hand-authored, or ingested from a vendor bundle — build plate remains a fixed enumeration, never a fourth importable profile catalog.
- **SC-007**: Two printers of different registered models whose underlying model data declares different compatible/relevant plate sets are never offered the same build plate choices, regardless of what machine profile (if any) is linked to either — verifiable by comparing choices across at least two different supported models.
- **SC-008**: 0 occurrences of a build-plate-type-to-label mapping hand-maintained in frontend code — every label the operator sees for a build plate comes from the backend response, verifiable by inspecting the frontend diff for this feature.

## Assumptions

- Build plate selection affects only bed-temperature resolution for a slice; it does not gate which filament or process profiles are otherwise offered for a printer (matching how the bundled slicers themselves treat plate choice as an informational/temperature concern, not a compatibility filter on filament/process selection).
- Consistent with `009-process-profiles`'s established "no silent defaults, fleet-derived, explicit per-printer selection" precedent — build plate has no organization-level default and no automatic fallback for a printer with an unset selection; a slice tied to that printer is refused until the operator makes an explicit choice, exactly like a missing process-profile selection is refused today.
- Build plate compatibility is required at the exact registered-model level (confirmed by vendor data to genuinely differ model-by-model, not just brand-by-brand — see Clarifications), sourced from the printer's own `PrinterModelCatalog` entry — never from whichever machine profile happens to be linked (today's `LinkMachineProfile` has no check tying the linked profile back to the printer's own registered model, a pre-existing gap this feature deliberately routes around rather than fixes). The sourcing signal priority is settled (Clarifications): a model's own machine preset's `not_support_bed_type` field, when declared, is authoritative; the filament-preset bed-temperature-key signal is the fallback for any model that doesn't declare it. How that resolved data is attached to `PrinterModelCatalog` entries (e.g. hand-authored per entry vs. derived at read time from ingested preset data) remains a planning-level concern.
- Printers that existed before this feature ships start with no build plate selected (same treatment as any newly-required selection with no historical data to backfill), consistent with how `009-process-profiles` handled pre-existing printers' process-profile selection.
- Because the real permission catalog isn't fully enforced yet, this feature requires only that a user be authenticated to view or change a printer's build plate selection, consistent with the auth posture of features 003–011.
- Build plate is manual-selection-only; this feature does not add live-telemetry-based auto-detection (e.g., parsing a Bambu MQTT bed-type field), even though the printer connector layer could theoretically report it in the future — that remains a candidate for a separate, later feature.
- Build plate choices are limited to the fixed named set with no custom/other/manual-temperature-entry option; an operator with a third-party plate approximates it with the closest matching named type.

## Dependencies

- Builds on `005-printer-fleet-refinements` (the printer detail page is the configuration surface this feature extends, and the immutable registered Brand/ModelCode this feature gates build plate compatibility on) and `009-process-profiles` (establishes the printer-page selection pattern, activity-log capture, and fleet-derived estimate-only resolution that this feature mirrors — though unlike process profile, build plate compatibility is gated by the printer's own registered model rather than by a linked machine profile, since it needs no machine-profile prerequisite).
- Extends the slicing resolution path (`ResolveSlicingProfileSelection` and its consumers) to additionally read per-plate-type bed-temperature data already present in ingested/imported `FilamentProfile` raw data, replacing today's single fixed plate-type assumption.
