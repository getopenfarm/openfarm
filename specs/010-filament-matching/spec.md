# Feature Specification: Fleet- and Inventory-Aware Filament Matching

**Feature Branch**: `010-filament-matching`

**Created**: 2026-07-22

**Status**: Draft

**Input**: User description: "after we have fix the slicer provision and implemented profile ingestion we're almost there, but when I add STL part it still failed saying Generic PLA not found, due to the fall back. I think we have 2 problem here, since we implemented ingestion we should already have Generic PLA profile. 2nd problem it should choose filament based on what we have, in the machine or filament library (I don't have the idea yet. What I am thinking is something like intelligent matching logic I think we should have it sooner or later because when we implement job queuing we will need that too. If we have that then our estimate will be more realistic as it's based on the fleet and filament repository."

## Context & Problem *(informative)*

Adding a part file that structurally carries no material data (an STL) requires the system to pick a
stand-in filament so it can still be sliced and estimated. Today that pick is a hardcoded, per-slicer-engine
literal name (e.g. `"Generic PLA"`, `"Bambu PLA Basic"`) baked into configuration, carried through the part's
processing job as if it were a material type, and handed to the same System-filament-profile resolution path
that machine/process/filament ingestion (the prior "slicer provision"/"profile ingestion" work) built for
real material types like `"PLA"` or `"PETG"`. Because a literal profile name is not a material type, that
resolution path never finds a match — even though ingestion has, in fact, already landed real System
filament profiles for PLA — and the operator sees a confusing "Generic PLA not found" failure on every STL
add. This is a defect in how the fallback value is produced and threaded through, not a gap in ingestion
itself.

Separately, even once that lookup is repaired, the existing fallback-resolution logic (`RepresentativeFilamentResolver`,
delivered by the process-profiles ingestion work) picks *any* System filament profile matching the needed
material type, using an arbitrary-but-stable tie-break (vendor name, then a vendor-assigned ID) that has no
relationship to what the print farm actually owns. OpenFarm already tracks a real filament inventory — spools
loaded onto printers right now, and spools sitting in storage as a filament library — but none of that is
consulted when picking a stand-in filament. The result is technically correct (a PLA profile is chosen for a
PLA part) but not realistic: the choice can name a filament the farm has never bought, and the resulting
material/time estimate reflects a generic catalog entry rather than the fleet's actual capability. The same
"pick a filament from what's realistically available" problem will resurface unchanged when print job queuing
is built (assigning a queued job to a printer needs the same kind of answer), so this feature treats the
matching logic as a shared capability rather than something specific to adding a part.

## User Scenarios & Testing *(mandatory)*

### User Story 1 - Adding an STL part no longer fails on filament resolution (Priority: P1)

An operator uploads an STL file (no embedded material/profile data) to add it as a part. Today this fails
outright with a "Generic PLA not found" error. Instead, the system must successfully resolve a stand-in
filament for the part's default material and continue with slicing and estimation, using the real System
filament profiles that ingestion already produced.

**Why this priority**: This is a hard failure blocking the most basic path (add any STL part) — nothing else
in this feature matters if this doesn't work first.

**Independent Test**: Upload an STL with no printer targeted, on an organization that has completed System
Profile ingestion for at least one machine model. Verify the part is added successfully, a filament is
resolved for its default material, and no "not found" error occurs.

**Acceptance Scenarios**:

1. **Given** an organization with System filament profiles already ingested for PLA, **When** an operator
   adds an STL part, **Then** the part is added successfully and a PLA filament is resolved for it — no
   "Generic PLA not found" or equivalent failure occurs.
2. **Given** an organization whose bundled slicer engine differs (e.g. Bambu Studio vs. OrcaSlicer vs.
   SuperSlicer), **When** an operator adds an STL part, **Then** resolution succeeds the same way regardless
   of which engine is in use — the default material an STL resolves to is expressed the same way (a material
   type, not an engine-specific literal profile name) for every engine.
3. **Given** an organization that has genuinely never ingested any System filament profile for the default
   material (e.g. ingestion hasn't run yet), **When** an operator adds an STL part, **Then** the system
   reports clearly that no filament is available for that material — distinct from today's misleading
   "not found" message, and naming the actual material that's unresolved.

---

### User Story 2 - Filament resolution prefers what the fleet already has loaded (Priority: P1)

When the system must pick a stand-in filament for a part whose material requirement isn't narrowed to a
specific filament (the common case for an STL, and for a 3MF plate the operator hasn't manually pinned to a
particular spool or profile), it should prefer a filament that's physically loaded onto a printer in the
fleet right now, over an arbitrary catalog entry. This makes the automatic choice reflect what could actually
be printed today, without operator input.

**Why this priority**: This is the core "intelligent matching" the operator asked for, and it's what makes
downstream estimates trustworthy — a choice grounded in what's loaded is the strongest, most actionable
signal a farm has.

**Independent Test**: With at least one printer in the fleet having a spool of a given material loaded, and
at least one other System filament profile of that same material also available, request resolution for
that material with no specific printer targeted. Verify the loaded spool's associated filament is chosen
over the unrelated catalog entry.

**Acceptance Scenarios**:

1. **Given** a fleet with one printer currently loaded with a PLA spool, and separately several unrelated
   System PLA filament profiles available, **When** the system resolves a stand-in filament for PLA with no
   specific printer targeted, **Then** the loaded spool's associated filament is chosen.
2. **Given** two different printers each loaded with a different PLA spool, **When** resolution runs for PLA
   with no specific printer targeted, **Then** exactly one is chosen, and repeating the resolution against
   the same unchanged fleet state always yields the same choice.
3. **Given** a specific target printer is known (e.g. resolving for that printer's own queued work), **When**
   that printer itself has a matching-material spool loaded, **Then** that printer's own loaded spool is
   preferred over a spool loaded on a different printer.
4. **Given** a loaded spool exists for the wrong material (e.g. PETG loaded, but the part needs PLA), **When**
   resolution runs for PLA, **Then** that mismatched spool is never chosen.

---

### User Story 3 - Filament resolution falls back to the filament library, then the catalog (Priority: P2)

When nothing in the fleet is currently loaded with a matching material, the system should next prefer a
filament from the organization's filament library (spools in storage, not yet loaded onto any printer) before
falling back to a generic System catalog profile. This keeps the choice grounded in filament the farm actually
owns even when nothing is loaded yet, and only reaches for a generic catalog pick as a last resort.

**Why this priority**: Extends the same realism goal of User Story 2 to the next-best signal available;
lower priority because User Story 1's guaranteed-not-to-fail catalog fallback already exists once US1 is
fixed, so this is a quality improvement on top of a working baseline, not a blocker.

**Independent Test**: With no printer in the fleet loaded with a matching-material spool, but at least one
matching-material spool sitting in storage, request resolution for that material. Verify the stored spool's
associated filament is chosen over a generic System catalog profile. Then remove that spool from storage
entirely (or use a material with none in storage) and verify resolution falls back to a System catalog
profile as it does today.

**Acceptance Scenarios**:

1. **Given** no printer in the fleet has a matching-material spool loaded, but a matching-material spool
   exists in storage, **When** resolution runs for that material, **Then** the stored spool's associated
   filament is chosen over an unrelated System catalog profile.
2. **Given** neither a loaded nor a stored spool exists for the needed material anywhere in the organization,
   **When** resolution runs, **Then** a System catalog profile for that material is chosen, exactly as today,
   and the part is still successfully added/estimated.
3. **Given** a matching library spool exists in storage but it has no linkage to any usable slicing profile
   data, **When** resolution runs, **Then** that spool is skipped in favor of the next-best candidate (another
   qualifying spool, or the System catalog fallback) rather than causing a failure.

---

### User Story 4 - Estimates reflect the matched filament (Priority: P2)

Once a stand-in filament has been intelligently matched (fleet-loaded, library, or catalog), the part's
material/time estimate is computed using that specific filament's own settings, not a generic placeholder —
so an operator reviewing an estimate is seeing a number grounded in what the farm can actually do, and can
tell (via the same origin labeling already used elsewhere in the system) whether the estimate assumed
fleet-loaded filament, library stock, or a generic catalog guess.

**Why this priority**: Turns User Stories 2–3's improved matching into the operator-visible value the request
was actually motivated by ("our estimate will be more realistic"); depends on those stories existing first.

**Independent Test**: Add the same STL part twice — once when a matching fleet-loaded spool exists, once
when only a generic catalog profile is available (no matching spool anywhere) — and verify the two resulting
estimates can differ (reflecting each filament's own settings) and each estimate is traceable to which kind
of filament source produced it.

**Acceptance Scenarios**:

1. **Given** a part's stand-in filament was matched from a fleet-loaded spool, **When** its estimate is
   computed, **Then** the estimate uses that filament's own settings and the part indicates its filament
   source was a loaded fleet spool.
2. **Given** a part's stand-in filament was matched from filament library stock, **When** its estimate is
   computed, **Then** the part indicates its filament source was library stock rather than a loaded spool or
   a generic catalog pick.
3. **Given** a part's stand-in filament fell back to a generic System catalog profile, **When** its estimate
   is computed, **Then** the part indicates its filament source was a generic catalog pick, distinguishing it
   from the two more specific sources above.

### Edge Cases

- What happens when a fleet-loaded spool's material matches but the printer it's loaded on is offline,
  disabled, or otherwise unavailable for work — should it still count as "loaded" for matching purposes?
- How does the system handle a tie between two equally-eligible candidates at the same priority tier (e.g.
  two printers loaded with equivalent PLA spools, both otherwise identical) — the choice must still be
  deterministic and stable across repeated resolutions of an unchanged state.
- What happens when a part's filament requirement has already been manually narrowed (by an operator, to a
  specific profile or a specific library filament) — intelligent matching must never override an explicit
  operator choice.
- What happens when the organization has no printers in its fleet at all yet, and no filament library
  entries either — resolution must still reach the existing System catalog fallback rather than failing
  differently than it does today.
- What happens when a matched fleet-loaded spool runs out or is swapped between the time a part's estimate
  is computed and the time it's actually queued for printing — this feature covers matching at
  estimate/add time only; keeping a prior match in sync with later inventory changes is out of scope here.

## Requirements *(mandatory)*

### Functional Requirements

- **FR-001**: When a part file carries no embedded material data (an STL), the system MUST resolve its
  default material as an actual material type (e.g. "PLA"), never as an engine-specific literal profile
  name, so it can be matched against real material data regardless of which bundled slicer engine is in use.
- **FR-002**: The system MUST NOT fail to add or estimate an STL part due to a mismatch between the form of
  its default-material value and the form filament resolution expects — the two must always be compatible.
- **FR-003**: When no System filament profile exists at all for the resolved default material (ingestion
  genuinely has none), the system MUST report clearly which material is unresolved, distinguishable from a
  generic "not found" message.
- **FR-004**: For any part whose filament requirement is not manually narrowed to a specific profile or
  library filament, the system MUST resolve a stand-in filament using a shared, reusable matching capability
  — usable both for adding/estimating a part and, in the future, for assigning filament during job queuing —
  rather than logic specific to one flow.
- **FR-005**: The matching capability MUST prefer, in order: (1) a filament currently loaded on a printer in
  the fleet whose material matches, (2) a filament available in the organization's filament library (spool
  in storage, not loaded) whose material matches, (3) a generic System catalog profile for that material —
  falling through to the next tier whenever the current tier has no eligible candidate.
- **FR-006**: When a specific printer is the resolution target, the matching capability MUST prefer that
  printer's own loaded filament over a filament loaded on a different printer, before falling through to
  fleet-wide loaded filament, library, and catalog tiers in that order.
- **FR-007**: When no specific printer is targeted (the estimate-only case), the matching capability MUST
  consider loaded filament across the whole fleet, not any single printer.
- **FR-008**: The matching capability MUST never select a candidate whose material does not match the
  requirement, regardless of tier.
- **FR-009**: Within any tier, when more than one candidate is otherwise equally eligible, the matching
  capability MUST select deterministically and stably, so that repeating resolution against an unchanged
  fleet and filament library always yields the same choice.
- **FR-010**: A filament library entry (spool) that matches on material but has no usable link to slicing
  profile data MUST be skipped in favor of the next eligible candidate, rather than causing a failure.
- **FR-011**: Intelligent matching MUST never override a part's filament requirement that has already been
  manually narrowed to a specific profile or library filament by an operator.
- **FR-012**: A part's computed material/time estimate MUST use the specific filament settings of whichever
  filament the matching capability actually selected, not a placeholder distinct from that selection.
- **FR-013**: The system MUST make it visible, wherever a resolved filament's estimate is shown, which kind
  of source produced it — fleet-loaded, filament library, or generic catalog — consistent with how other
  profile origins are already labeled elsewhere in the system.
- **FR-014**: When the fleet has no printers and the filament library has no entries at all, the matching
  capability MUST still reach the generic System catalog fallback rather than failing differently than the
  system does today in that situation.

### Key Entities

- **Material Type**: The real, engine-independent classification (e.g. "PLA", "PETG", "ABS") that a part's
  filament requirement is expressed in and that filament resolution matches against. Distinct from any
  slicer-specific literal profile name.
- **Fleet-Loaded Filament**: A spool currently loaded onto a feed slot on a printer that is part of the
  organization's fleet — the strongest signal of "what the farm can print with right now."
- **Filament Library Entry**: A spool held in storage, not currently loaded onto any printer, but owned by
  the organization and available to be loaded — the next-best signal after a live load.
- **System Filament Profile**: A generic, ingested slicing profile for a material, not tied to any specific
  owned spool — the existing, always-available fallback.
- **Filament Match Result**: The outcome of the matching capability for a given material (and, when
  applicable, a given target printer) — which filament was selected and which of the three source kinds
  (fleet-loaded, library, catalog) it came from, carried through to the estimate that consumes it.

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: Adding an STL part succeeds without a filament-resolution failure in 100% of cases where at
  least one System filament profile exists for its default material — eliminating the current "Generic PLA
  not found" failure entirely.
- **SC-002**: When a matching filament is loaded somewhere in the fleet, the estimate produced for a newly
  added part reflects that specific filament's own settings rather than a generic placeholder, verifiable by
  comparing estimates for the same file under fleet-loaded-available vs. no-match-available conditions.
- **SC-003**: An operator reviewing any part's estimate can tell, without leaving the page, whether the
  filament behind it came from the fleet, the filament library, or a generic catalog guess.
- **SC-004**: Repeating filament resolution against an unchanged fleet and filament library always produces
  the identical choice, with no observed flapping between equally-eligible candidates across repeated runs.

## Assumptions

- The immediate defect (STL add failing with "Generic PLA not found") is caused by threading an
  engine-specific literal profile name through a resolution path that expects a material type; fixing FR-001
  resolves it without requiring any change to the ingestion pipeline itself, which already produces usable
  System filament profiles.
- "The machine" in the user's request corresponds to filament currently loaded on a fleet printer's feed
  slot (tracked by the existing filament-tracking feature), and "the filament library" corresponds to spools
  held in storage under that same existing feature — this feature reuses that existing inventory data rather
  than introducing a new inventory concept.
- Job queuing itself (assigning queued jobs to specific printers/filament) is explicitly out of scope for
  this feature; only the shared matching capability it will depend on is delivered here.
- A filament library spool's linkage to usable slicing profile data is optional and may be absent for some
  spools (consistent with the existing filament-tracking design); this feature treats an unlinked spool as
  ineligible for matching rather than requiring every spool to carry that link retroactively.
- "Available for work" filtering of fleet printers (e.g. excluding offline/disabled printers from the
  fleet-loaded tier) follows whatever notion of an active/usable printer the fleet management feature already
  defines; this feature does not introduce a new availability concept.
- Keeping a part's matched filament in sync with later inventory changes (a spool depleting or being
  swapped after the match was made) is out of scope; matching happens at add/estimate time only.
