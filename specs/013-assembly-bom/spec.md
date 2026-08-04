# Feature Specification: Phase 6 — Assembly (BOM)

**Feature Branch**: `013-assembly-bom`

**Created**: 2026-08-04

**Status**: Draft

**Input**: User description: "Phase 6"

## User Scenarios & Testing *(mandatory)*

### User Story 1 - Define an assembly from parts, quantities, and sub-assemblies (Priority: P1)

As a print farm operator, I want to describe a finished product as a list of components with quantities — printed parts from the catalog (Phase 4) and other assemblies nested inside it — so the system holds a truthful description of what the product is actually made of, at whatever depth the product's real structure requires.

**Why this priority**: This is the entire definition layer every other story in this phase reads from — there is nothing to explode, cost, or count buildable sets for until an assembly exists. It is independently valuable on its own: an operator gets a real, browsable product structure where today they have only a flat list of unrelated parts.

**Independent Test**: Can be fully tested by creating an assembly with several part lines and quantities, nesting a second assembly inside it as a sub-assembly, saving, reopening it, and confirming the structure and quantities persist exactly as entered — with no printer, job, run, or file involved anywhere in the flow.

**Acceptance Scenarios**:

1. **Given** several parts exist in the catalog (Phase 4), **When** an operator creates an assembly with a name and one or more lines each targeting a part with a quantity, **Then** the assembly is saved and its structure is browsable exactly as entered.
2. **Given** an assembly "Handle" already exists, **When** an operator adds a line to assembly "Enclosure" targeting "Handle" with a quantity of 2, **Then** "Enclosure" is saved with "Handle" as a sub-assembly, and "Handle" remains independently usable as its own assembly.
3. **Given** an operator is building an assembly, **When** they add a line targeting an assembly that already transitively contains the assembly being edited, **Then** the save is rejected and the offending containment path is named — the assembly is never stored in a cyclic state.
4. **Given** an operator is building an assembly whose nesting would produce a path deeper than the maximum nesting depth, **When** they save, **Then** the save is rejected and the depth limit is stated — no assembly is ever stored past the limit.
5. **Given** an install with no printers registered, no files uploaded, and no jobs or runs of any kind, **When** an operator creates and edits assemblies, **Then** every operation succeeds — nothing in this phase requires printer knowledge, a live connection, or file handling.

---

### User Story 2 - Explode an assembly into aggregated part demand (Priority: P1)

As a print farm operator, I want to ask "if I want 5 of this product, exactly which parts do I need and how many of each," and get one flat, aggregated answer per part — plus a readable explanation of where each number came from — so I can see real demand rather than mentally multiplying a nested tree by hand.

**Why this priority**: This is the phase's headline capability and the reason Phase 6 sits ahead of Phase 7 at all — Phase 7's entire demand model is built on consuming this aggregate. It is independently valuable before Phase 7 exists: an operator with a nested product can immediately see the true part count for any target quantity.

**Independent Test**: Can be fully tested by exploding a hand-computed nested assembly at several quantities and confirming the flat per-part totals match the hand-computed values, including a part that appears at more than one depth, and confirming the same inputs produce the same result every time with nothing persisted as a side effect.

**Acceptance Scenarios**:

1. **Given** an assembly containing 3 of part A and 2 of part B, **When** an operator explodes it at quantity 4, **Then** the result is 12 of part A and 8 of part B.
2. **Given** an assembly containing 2 knobs directly plus 2 of sub-assembly "Handle", where "Handle" contains 2 knobs, **When** an operator explodes it at quantity 1, **Then** the result reports **6 knobs** as a single aggregated line, with a display-only breakdown reading "2 direct + 4 via Handle".
3. **Given** the same assembly, **When** an operator explodes it repeatedly at the same quantity, **Then** every explosion returns an identical result, and no assembly, line, or quantity anywhere in the system is changed by having exploded it.
4. **Given** an assembly nested several levels deep where the same part appears at three different depths, **When** it is exploded, **Then** that part appears exactly once in the aggregated result with the summed total, and its breakdown lists all three contributing paths.
5. **Given** an assembly with non-printed component lines (User Story 4) anywhere in its tree, **When** it is exploded, **Then** the aggregated non-printed component totals are reported **separately** from the printed-part totals, so no consumer can mistake one set for the other.

---

### User Story 3 - See how many complete sets are buildable (Priority: P2)

As a print farm operator, I want to know how many complete, boxable sets of a product I can make from the good units I have on hand, so I get the one number that matters at shipping time — not a component-progress percentage that hides a single missing bracket.

**Why this priority**: This is the calculation that answers "how many complete sets can I box today," and it is specifically what incumbent farm software cannot answer. It is ranked below Stories 1–2 because its real-world input — how many good units of each part actually exist — is produced by Phase 8 (Progress, outcomes & yield); this phase defines, exposes, and proves the calculation itself, against good quantities supplied by the caller.

**Independent Test**: Can be fully tested by supplying a set of good quantities per part for a known assembly and confirming the returned buildable-units number matches the hand-computed `floor(min over printed components of (good quantity ÷ quantity per assembly))`, including the case where one part's good quantity is zero.

**Acceptance Scenarios**:

1. **Given** an assembly requiring 2 of part A and 3 of part B per unit, **When** good quantities of 10 of A and 12 of B are supplied, **Then** buildable units is 4 — `min(floor(10÷2)=5, floor(12÷3)=4)`.
2. **Given** the same assembly, **When** good quantities of 90 of A and 0 of B are supplied, **Then** buildable units is 0 — near-complete progress on every other component does not produce a single shippable set.
3. **Given** a nested assembly where a part appears at multiple depths, **When** buildable units is calculated, **Then** the per-unit requirement used for that part is its **aggregated** exploded quantity for one unit of the assembly, not any single line's quantity.
4. **Given** an assembly that also contains non-printed component lines with no stock of any kind, **When** buildable units is calculated, **Then** those lines are excluded entirely and the result is driven only by printed parts.

---

### User Story 4 - Record non-printed components on a BOM (Priority: P3)

As a print farm operator, I want to list the screws, heat-set inserts, magnets, bearings, and packaging that go into a product alongside its printed parts, so the BOM is a truthful description of the product rather than a filament-only view — and so cost per assembly later (Phase 18) isn't filament-only either.

**Why this priority**: Purely informational — these lines carry no stock, gate nothing, and never affect buildable units or demand. Ranked last because the assembly is fully usable without them. Modeled now rather than later specifically because making the BOM line polymorphic afterwards would mean migrating every existing BOM row.

**Independent Test**: Can be tested by adding non-printed component lines to an existing assembly, confirming they appear in the assembly's structure and in the explosion's separate non-printed totals, and confirming buildable units for that assembly is unchanged from before they were added.

**Acceptance Scenarios**:

1. **Given** an assembly of printed parts, **When** an operator adds a line targeting a non-printed component ("M3×8 screw", unit "each") with a quantity of 6 per assembly, **Then** the line is saved and appears in the assembly's structure alongside its printed-part lines.
2. **Given** a non-printed component with an optional unit cost recorded, **When** an operator views it, **Then** the cost is stored and available for later consumption (Phase 18) — and the component is still saveable and usable with no unit cost at all.
3. **Given** an assembly containing non-printed component lines, **When** buildable units is calculated, **Then** the result is identical to the result for the same assembly with those lines removed.
4. **Given** an operator looks for stock, on-hand counts, or a reorder point for a non-printed component, **When** they view it, **Then** none exists — the system tracks no quantity on hand for non-printed components anywhere.

---

### Edge Cases

- **What happens when an operator saves an assembly that transitively contains itself?** The save is rejected at that moment, naming the containment path that would have closed the loop. Cycle detection happens **at save time, never at explosion time** — explosion is allowed to assume it is operating on an acyclic structure.
- **What happens when an operator adds a line that makes an assembly directly contain itself?** Identical treatment — a direct self-reference is just the shortest cycle and is rejected at save time by the same rule.
- **What happens when nesting would exceed the maximum depth?** The save is rejected, stating the limit. The initial limit is **10 levels of nested assemblies**: the assembly being saved counts as level 1, an assembly it directly includes is level 2, and so on. Part and non-printed-component lines are leaves and do not consume a level.
- **What happens when an operator saves a change to a *deep* assembly that is itself used inside other assemblies, and the change pushes one of those parents past the depth limit?** The save is rejected. Both the cycle rule and the depth rule are evaluated across every path the saved assembly participates in — downward through its own components **and** upward through the assemblies that already include it — not only its own subtree.
- **What happens when a part appears at several different depths in the same tree?** The explosion reports it **once**, as a single aggregated quantity summed across every path it appears on. The per-path breakdown ("6 knobs: 2 direct + 4 via the handle sub-assembly") is retained as **display-only metadata** — it is shown to operators and never used to match, allocate, or consume anything.
- **What happens when an operator tries to save an assembly with no lines at all?** The save is rejected — an assembly must have at least one BOM line to exist, for the same reason a cycle is rejected at save time: an empty assembly is not a truthful description of a product, and explosion of one has no meaningful answer.
- **What happens when an operator enters a line quantity of zero or a negative number?** The save is rejected. A part or sub-assembly line quantity must be a whole number of at least 1; a non-printed component line quantity must be greater than zero (and may be fractional, since it carries a unit).
- **What happens when an operator tries to explode an assembly at quantity zero or a negative quantity?** The explosion is rejected rather than returning an all-zero result — the requested quantity must be a whole number of at least 1.
- **What happens when the same target appears twice on the same assembly?** The save is rejected — a given part, sub-assembly, or non-printed component may appear at most once per assembly; the operator adjusts that one line's quantity instead. Multi-path aggregation still occurs naturally through nesting, where the same target legitimately appears on different assemblies.
- **What happens when an operator deletes an assembly that another assembly includes as a sub-assembly?** The deletion is blocked, naming every assembly that references it. There is nothing in flight to protect (no runs or jobs exist yet — Phase 7), but silently orphaning or cascade-deleting a parent's structure would destroy a truthful product description the operator never asked to change.
- **What happens when an operator deletes a part (Phase 4) that a BOM line targets?** The deletion is blocked, naming the referencing assemblies. This narrows Phase 4's own "deleting a part deletes its data with it" edge case, which held only because nothing referenced a part independently of itself — as of this phase, BOM lines do.
- **What happens when an operator deletes a non-printed component referenced by a BOM line?** Blocked on the same basis as a sub-assembly or part.
- **What happens when an operator edits an assembly that is used as a sub-assembly elsewhere?** The edit is allowed and takes effect immediately for every parent's explosion — this is a definition layer with no snapshots and no in-flight quantities. Phase 7 is what freezes a BOM explosion, by snapshotting it at run creation; nothing in this phase needs to.
- **What happens when a non-printed component has no unit cost recorded?** It is saved and used normally. Cost per assembly (Phase 18) simply has no contribution from that component; the line is never blocked, flagged as incomplete, or excluded from the BOM because of it.
- **What happens when a printed part's good quantity is zero?** Buildable units is 0. This is the intended headline behavior: 90% component progress with one missing bracket is zero shippable sets.
- **What happens when an assembly contains *no printed parts anywhere in its tree* — only non-printed components?** Buildable units is reported as **not applicable**, with the reason stated. It is deliberately not 0 (which would falsely claim nothing can be shipped) and not an unbounded/infinite value (the mathematical result of a minimum over an empty set, which would be equally untruthful).
- **What happens when explosion encounters unexpectedly deep or repeating structure despite save-time validation?** Explosion terminates deterministically at the maximum depth and reports that it did, rather than recursing without bound. Explosion still performs no cycle *validation* — that stays a save-time concern — it merely refuses to run forever.
- **How is progress attributed to a nested sub-assembly (Phase 8)?** It is **prorated from its parent line**, not measured, because produced units are not tracked per path — the per-path breakdown is display-only and never a matching input. This is an accepted approximation: accurate at the line level, approximate one level down.
- **What happens when an operator explodes a sub-assembly directly rather than its parent?** It works identically — any assembly can be the root of an explosion; "sub-assembly" describes a relationship, not a different kind of entity.

## Requirements *(mandatory)*

### Functional Requirements

- **FR-001**: System MUST allow an operator to create, view, edit, and delete an Assembly identified by a name, with an optional description.
- **FR-002**: System MUST require every Assembly to have at least one BOM line, rejecting the save of an Assembly with no lines.
- **FR-003**: System MUST support a polymorphic BOM line that targets **exactly one** of: a Part (Phase 4), another Assembly (a sub-assembly), or a non-printed component. A single line MUST NOT target more than one of these, and MUST NOT exist with no target.
- **FR-004**: System MUST record a quantity per assembly on every BOM line.
  - **FR-004a**: A line targeting a Part or a sub-Assembly MUST have a quantity that is a whole number of at least 1.
  - **FR-004b**: A line targeting a non-printed component MUST have a quantity greater than zero, expressed in that component's own unit, and MAY be fractional.
- **FR-005**: System MUST allow a non-printed component to be defined with a name, a unit, and an **optional** unit cost, and MUST allow it to be referenced by BOM lines across multiple assemblies.
  - **FR-005a**: System MUST NOT track stock, on-hand quantity, reorder point, or any other inventory state for a non-printed component.
  - **FR-005b**: A non-printed component MUST NOT gate, block, or reduce any calculation, availability decision, or readiness state anywhere in this phase — it is informational only, existing so a BOM is a truthful description of the product and so cost per assembly (Phase 18) is not filament-only.
  - **FR-005c**: System MUST allow a non-printed component with no unit cost recorded to be created, referenced, and used exactly like one that has a cost.
- **FR-006**: System MUST support nested assemblies to a spec-defined **maximum nesting depth of 10 levels**, where the assembly being evaluated is level 1 and each further assembly on a containment path adds one level; Part and non-printed-component lines are leaves and consume no level.
  - **FR-006a**: System MUST reject, **at save time**, any create or edit that would produce a containment path exceeding the maximum nesting depth, and MUST state the limit in the rejection.
- **FR-007**: System MUST reject, **at save time**, any create or edit that would cause an Assembly to directly or transitively contain itself, and MUST name the containment path that would have closed the cycle. The system MUST NOT persist a cyclic structure under any circumstance.
- **FR-008**: System MUST evaluate both FR-006a's depth rule and FR-007's cycle rule across every containment path the saved Assembly participates in — downward through its own components and upward through the assemblies that already include it — not only its own subtree.
- **FR-009**: System MUST provide BOM explosion as a **pure, deterministic function** taking an Assembly and a requested quantity and producing flat component quantities. Repeated invocations with identical inputs MUST produce identical results, and an explosion MUST NOT modify, persist, or otherwise have any side effect on any assembly, line, quantity, or other stored data.
  - **FR-009a**: Explosion MUST multiply quantities down the tree — each nested level's quantity multiplied by the quantity of every line above it on that path, and by the requested quantity at the root.
  - **FR-009b**: Explosion MUST **aggregate by part**, reporting each distinct Part exactly once with the sum of its quantities across every path it appears on.
  - **FR-009c**: Explosion MUST retain a per-path breakdown for each aggregated part (e.g. "6 knobs: 2 direct + 4 via the handle sub-assembly") as **display-only metadata**. The breakdown MUST NOT be used for matching, allocation, consumption, or any other decision — it is only ever shown.
  - **FR-009d**: Explosion MUST report aggregated non-printed component quantities **separately** from printed-part quantities, applying the same multiplication and aggregation rules, so that a consumer of printed-part demand can never accidentally read a non-printed total as printable demand.
  - **FR-009e**: Explosion MUST reject a requested quantity that is not a whole number of at least 1.
  - **FR-009f**: Explosion MUST NOT perform cycle validation (that is FR-007's save-time responsibility), but MUST terminate deterministically at the maximum nesting depth rather than recursing without bound, reporting that it did so.
- **FR-010**: System MUST calculate **buildable units** for an Assembly as `floor(min over printed components of (good quantity ÷ quantity per assembly))`.
  - **FR-010a**: The "quantity per assembly" used for each printed component MUST be that part's **aggregated exploded quantity for one unit** of the assembly (FR-009b), not any individual BOM line's quantity.
  - **FR-010b**: System MUST exclude non-printed components from the buildable-units calculation entirely — they carry no stock (FR-005a), so including them would pin every result at zero.
  - **FR-010c**: System MUST return 0 when any printed component's good quantity is zero.
  - **FR-010d**: System MUST report buildable units as **not applicable**, with the reason stated, for an Assembly whose exploded tree contains no printed components at all — never 0, and never an unbounded value.
  - **FR-010e**: System MUST accept good quantities as supplied inputs to this calculation. This phase MUST NOT itself track, derive, or store good quantities — Phase 8 (Progress, outcomes & yield) is the first consumer that supplies real ones.
- **FR-011**: System MUST block deleting an Assembly, a Part (Phase 4), or a non-printed component that is referenced by any BOM line, and MUST name the referencing assemblies in the rejection. Deletion MUST NOT cascade into, or silently orphan, a referencing assembly's structure.
- **FR-012**: System MUST apply an edit to an Assembly immediately to every explosion that reads it, including explosions of assemblies that include it as a sub-assembly. System MUST NOT snapshot, version, or freeze a BOM in this phase — snapshotting a BOM explosion at run creation is Phase 7's responsibility.
- **FR-013**: System MUST reject a BOM line whose target already appears on the same Assembly — a given Part, sub-Assembly, or non-printed component MAY appear at most once per Assembly.
- **FR-014**: System MUST allow assemblies to be browsed as a list and viewed as an expandable structure showing each line's target, quantity, and — for sub-assemblies — their own contents.
- **FR-015**: System MUST record assembly creation, edits (including line add/change/remove), deletions, and non-printed component creation/edits in the activity log, including who made the change and when.
- **FR-016**: System MUST allow every operation in this phase — creating, editing, viewing, exploding an assembly, and calculating buildable units — to succeed with **no printer registered, no live printer connection, no machine/filament/process profile, and no file handling or slicing of any kind**, matching the same "zero printer knowledge required" property Phase 4 has.
- **FR-017**: System MUST NOT model, in this phase, any production run, run line, part requirement, job, print queue, dispatch, in-flight quantity, or produced quantity — those are Phase 7's entities entirely. This phase is a definition layer only: it describes what a finished product is made of, never how one gets produced.

### Key Entities *(include if feature involves data)*

- **Assembly**: A named description of a finished product, made up of one or more BOM lines. Key attributes: name, optional description. An assembly may be used as a component of other assemblies to arbitrary depth up to the maximum nesting depth, and may never directly or transitively contain itself. It has no stock, no status, no readiness state, and no relationship to any printer, job, run, or file. It is what Phase 4 deliberately kept out of a Part: a Part is atomic (exactly one file), and an Assembly is the mechanism for combining multiple distinct parts.
- **BOM Line**: One component entry on an Assembly, carrying a quantity per assembly and targeting **exactly one** of: a Part (Phase 4), another Assembly (making it a sub-assembly), or a non-printed component. The line's target kind is what makes the BOM polymorphic; the quantity is per single unit of the owning assembly, multiplied down the tree at explosion time. A target may appear at most once per assembly.
- **Non-Printed Component**: A reusable, informational catalog entry for something a product needs that no printer makes — screws, heat-set inserts, magnets, bearings, packaging. Key attributes: name, unit, optional unit cost. It carries **no stock and no on-hand quantity of any kind**, never gates anything, and is excluded from buildable units entirely. It exists so a BOM is a truthful description of the product, and so cost per assembly (Phase 18) is not filament-only.
- **BOM Explosion Result**: The output of exploding an assembly at a requested quantity — a flat set of aggregated printed-part quantities (each part appearing exactly once, summed across every path), a separately-reported set of aggregated non-printed component quantities, and, per aggregated entry, a display-only per-path breakdown explaining how the total was reached. Nothing about the result is stored: it is recomputed on demand and is identical for identical inputs. Phase 7 consumes the aggregate; the breakdown is only ever shown.
- **Buildable Units**: A derived value, not stored — the number of complete sets of an assembly that could be assembled from a supplied set of good quantities per printed part, defined as `floor(min over printed components of (good quantity ÷ quantity per assembly))`. Non-printed components never participate. It is defined and proven in this phase and consumed by Phase 8, which supplies the first real good quantities.

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: 100% of explosions of hand-computed reference assemblies — including one at the full 10-level maximum nesting depth, and one where the same part appears at three different depths — return per-part totals exactly matching the hand-computed values, with each part appearing exactly once.
- **SC-002**: Explosion is verifiably pure: 100 repeated explosions of the same assembly at the same quantity return identical results, and 0 rows of stored data anywhere in the system differ before versus after any explosion.
- **SC-003**: 100% of save attempts that would create a direct or transitive cycle, or exceed the maximum nesting depth, are rejected at save time with the offending path or limit named; 0 cyclic or over-depth structures are ever persisted, and 0 explosions fail to terminate.
- **SC-004**: 100% of buildable-units calculations across a reference set of scenarios — flat, nested, part-appearing-at-multiple-depths, one component at zero good quantity, and an assembly containing non-printed components — match their hand-computed values, with the zero-stock case returning exactly 0.
- **SC-005**: 0 non-printed component lines affect a buildable-units result: for every reference assembly, the value calculated with its non-printed lines present is identical to the value calculated with them removed.
- **SC-006**: An operator can create, edit, explode, and calculate buildable units for an assembly on an install with 0 printers registered, 0 files uploaded, 0 profiles imported, and 0 jobs or runs in existence — verifiable in a single end-to-end pass.
- **SC-007**: An assembly of 500 total BOM lines spread across the full 10 levels of nesting explodes in under 1 second, and the result remains a single aggregated entry per distinct part regardless of how many paths contributed to it.

## Assumptions

- **Maximum nesting depth is 10 levels.** The source material establishes that a maximum exists but does not give a number; 10 is chosen as the initial default because real printed products rarely exceed three or four levels of genuine sub-assembly, leaving generous headroom while still bounding recursion to a testable constant. It is asserted here so the limit is directly testable rather than left as an implementation choice.
- **An assembly with no BOM lines is rejected at save time**, by the same reasoning as a cycle: an empty assembly is not a truthful product description, and exploding one has no meaningful answer. The alternative considered — allowing an empty draft assembly to exist and be filled in later — was rejected because it would introduce a "not yet valid" state that nothing else in this phase has, and because Phase 4 already established the precedent that a catalog entity is never saved incomplete.
- **Non-printed components are reusable catalog entries, not data inlined on a single BOM line.** The source material says a line *targets* a non-printed component, so the same "M3×8 screw" is defined once and referenced by many assemblies, and updating its unit cost updates it everywhere for Phase 18. Inlining name/unit/cost on each line was rejected as producing duplicate, drifting cost data across assemblies.
- **A non-printed component's unit is an operator-supplied short label** ("each", "ml", "m", "pack"), not a fixed enumeration. Because these components never gate anything and carry no stock, nothing in the system needs to interpret the unit — it is displayed, and it qualifies the line quantity.
- **Quantity types differ by target kind**: part and sub-assembly quantities are whole numbers (you cannot print 1.5 brackets), while non-printed component quantities may be fractional, since they carry a unit that legitimately supports fractions (0.2 m of foam tape). This is safe specifically because non-printed quantities are excluded from buildable units and never gate anything.
- **Cycle and depth validation are evaluated over the whole containment graph the saved assembly participates in, including its ancestors** — not just its own subtree. The source material specifies save-time cycle rejection and a maximum depth but not the direction of evaluation; validating only downward would let an edit to a deep child silently push an unrelated parent past the limit.
- **The same target may appear at most once per assembly.** The source material is silent; rejecting duplicates keeps each line's per-path breakdown unambiguous (two identical direct paths would be indistinguishable in the display metadata) and loses nothing, since the operator adjusts the single line's quantity instead. Genuine multi-path aggregation still arises through nesting, which is where the source material's own example ("2 direct + 4 via the handle sub-assembly") comes from.
- **Buildable units is computed against the quantity-1 explosion**, so a part appearing at multiple depths contributes its aggregated per-unit total rather than any single line's quantity. The source material's formula says "quantity per assembly," which is only unambiguous once read as the exploded per-unit aggregate.
- **Good quantities are inputs to buildable units, not something this phase tracks.** No stock, on-hand, produced, or in-flight quantity of any kind exists in this phase — Phase 8 supplies the first real good quantities. The calculation is therefore defined and unit-tested here against supplied values, exactly as the source material describes.
- **Buildable units for an assembly with no printed components anywhere is reported as "not applicable"**, with the reason stated. The source material's formula is a minimum over printed components only, which is mathematically undefined (or unbounded) over an empty set; reporting 0 would falsely claim nothing is shippable and reporting an unbounded value would falsely claim everything is.
- **Deletion is blocked rather than cascaded**, for assemblies, parts, and non-printed components alike, whenever a BOM line references them. This narrows Phase 4's own "deleting a part deletes its data with it" edge case, which held only while nothing referenced a part independently of itself.
- **BOMs are not snapshotted or versioned in this phase.** An edit takes effect immediately everywhere the assembly is read. There is nothing in flight to protect because no runs, jobs, or produced quantities exist yet — Phase 7 owns snapshotting a BOM explosion at run creation, and that is where the freeze belongs.
- **Progress attributed to a nested sub-assembly will be prorated from its parent line rather than measured** (Phase 8), because produced units are not tracked per path and the per-path breakdown is display-only. This is an accepted approximation stated here for traceability — accurate at the line level, approximate one level down — not a requirement this phase implements.
- **This phase computes no cost or margin.** It records an optional unit cost per non-printed component and exposes aggregated non-printed quantities from explosion; rolling those into cost per assembly, and any selling price or margin, is Phase 18's work.
- **Assemblies and non-printed components are organization-scoped**, consistent with every other tenant-scoped entity since Phase 0.
- **Whether non-printed components earn their place long-term remains an open product question**, per the plan's own "Open decisions" note — but the decision for this phase is to build them now, because making the BOM line polymorphic later would mean migrating every existing BOM row, whereas scoping them to informational-only keeps the cost near zero today. This is noted as a future-removability question, not an unresolved ambiguity in this spec.
