# Phase 0 Research: Fleet- and Inventory-Aware Filament Matching

## R1 — Root cause of "Generic PLA not found" and its fix

**Decision**: Replace `SlicerOptions.GenericFallbackMaterialProfile` (a `Dictionary<SlicerEngine, string>` of
engine-specific literal profile names — `"Generic PLA"`, `"Bambu PLA Basic"`) with a single, engine-independent
fallback **material type** constant (`"PLA"`), and drop the per-engine dictionary entirely.

**Rationale**: Traced the failure end to end:
- `AddPartCommandHandler` (`api/src/OpenFarm.Application/Parts/AddPartCommandHandler.cs:57`) sets an STL job's
  `TargetMaterialType` from `ISlicerDefaultsProvider.GetGenericFallbackMaterialProfile(slicerUsed.Engine)` —
  today that returns `"Generic PLA"` for OrcaSlicer/SuperSlicer or `"Bambu PLA Basic"` for Bambu Studio.
- That value flows into `CliSlicerEngine.SliceCoreAsync` (`api/src/OpenFarm.Infrastructure/Parts/CliSlicerEngine.cs:72-77`)
  as `materialType`, which for OrcaSlicer/Bambu Studio is passed straight into
  `ResolveSlicingProfileSelection.ResolveAsync(printerId, materialType, ...)` →
  `RepresentativeFilamentResolver.ResolveAsync(materialType, ...)`
  (`api/src/OpenFarm.Application/Profiles/RepresentativeFilamentResolver.cs:24`), which filters
  `FilamentProfile`s by `f.MaterialType == materialType`. `FilamentProfile.MaterialType` is populated at
  ingestion time from the vendor data's resolved `filament_type` (research.md of `009-process-profiles`) and
  holds real values like `"PLA"`/`"PETG"`/`"ABS"` — never a profile name. `"Generic PLA"` never equals `"PLA"`,
  so the filter always empties out and `SlicingProfileUnconfiguredException("No System filament profile is
  available for material 'Generic PLA'.")` fires — exactly the reported error, on every STL, regardless of
  how much ingestion has actually landed.
- For SuperSlicer, the same value is *also* passed directly into `SuperSlicerInvocation.BuildInvocation`
  (`api/src/OpenFarm.Infrastructure/Parts/SlicerInvocation/SuperSlicerInvocation.cs:28`) as the CLI's
  `--filament-type` flag — SuperSlicer's classic-PrusaSlicer-CLI fork expects that flag's value to be a
  material type (`PLA`/`PETG`/`ABS`/…), not a profile name, so `"Generic PLA"` happens to work there only by
  accident of substring overlap, not because it's the right kind of value.

  Both consumers already expect the same thing — a material type — confirmed by the parameter itself being
  named `materialType` throughout `ISlicerEngine`/`ISlicerInvocation`. There is exactly one correct fix: stop
  producing a profile-name-shaped value and produce a real material type instead. No signature changes are
  needed anywhere downstream of `AddPartCommandHandler`/`ReplacePartFileCommandHandler` — this is a
  configuration-value defect, not an architectural gap.

**Alternatives considered**:
- *Keep per-engine names, fix `RepresentativeFilamentResolver` to match on name instead of material type* —
  rejected: would silently regress Bambu Studio (whose fallback name, `"Bambu PLA Basic"`, is itself a
  `Name`, and matching by `Name` reintroduces exactly the fragility ingestion's `MaterialType` field was
  built to avoid — a slicer version rename would break the fallback again).
- *Add a translation table from literal profile name → material type* — rejected as needless indirection;
  the fallback's only job is "pick a default material," which a single constant expresses directly.
- *Make the fallback material configurable per organization* — out of scope; nothing in the spec asks for
  operator control over the STL default, and PLA is already the universal 3D-printing default assumed
  elsewhere in the product (research.md of `007-part-library` on the "any material" wildcard).

**Follow-on for FR-003**: once the literal-name mismatch is fixed, the existing
`SlicingProfileUnconfiguredException` message (naming the actual unresolved material) already fires correctly
for the genuine "ingestion hasn't produced anything for this material" case — no new exception type or
message format needed; today's message was just unreachable in its intended case because the STL fallback
never previously supplied a real material type to trigger it correctly.

## R2 — Tiered matching algorithm and tie-breaks

**Decision**: A four-step deterministic search, each step short-circuiting on the first eligible candidate:

1. **Target printer's own loaded spool** (only when a `printerId` is given) — a `Spool` with
   `State = Using` whose `FeedSlotId` resolves (via `IFeedUnitRepository.GetByFeedSlotIdAsync`) to a
   `FeedUnit` with that `PrinterId`, joined to `FilamentVariant → Filament` where `Filament.MaterialType`
   matches and `Filament.FilamentProfileId` resolves to an existing `FilamentProfile`.
2. **Any fleet-loaded spool** (any printer) — same shape, fleet-wide. Tie-break: order by the owning
   printer's `Id`, then the `Spool`'s own `Id`, both ascending — mirrors the existing
   "arbitrary-but-stable-by-Id" tie-break `ResolveForFleetRepresentativeAsync` already uses for machine/process
   selection in the same file, so the codebase has one consistent idiom for "no meaningful preference, pick
   deterministically."
3. **Filament library** (`State = InStorage`) — same material/link eligibility as above, fleet membership
   irrelevant. Tie-break: order by `Spool.Id` ascending (no printer to key off; a plain stable-by-Id order is
   consistent with step 2's idiom rather than inventing a new preference axis like "fullest spool," which
   nothing in the spec asks for).
4. **System catalog** — the existing `RepresentativeFilamentResolver` body, unchanged: `Origin = System`,
   `IsSelectable`, `IsCurrentlyOffered`, `MaterialType` match, ordered by `VendorName` then `ExternalId`.

Each step that finds nothing (not "no candidates exist" vs. "candidates exist but are all ineligible" —
same handling either way, per FR-010) falls through to the next.

**Rationale**: This is exactly the 4-tier breakdown validated with the user (`Loaded Spool [this printer] >
Loaded Spool [fleet] > Filament library > System Profile`), expressed as FR-005/FR-006/FR-007 in the spec.
Reusing the codebase's existing "stable-by-Id" tie-break idiom (already used twice — machine/process fleet
representative pick, and System filament profile pick) avoids introducing a third, different notion of
"best" candidate (e.g., fullest spool by remaining weight) that nothing in the request calls for and that
would need its own justification and edge-case handling (a freshly-registered spool tying on weight, etc.).

**Alternatives considered**:
- *Prefer the spool with the most `RemainingWeightGrams` within a tier* — rejected for this iteration: adds a
  second sort dimension for no requirement in the spec (SC-004 only asks for determinism, not "most
  usable"), and is trivial to add later as a one-line `.ThenBy` if an operator asks for it.
- *Skip the per-printer tier and always search fleet-wide* — rejected: FR-006 (validated with the user) is
  explicit that a specific printer's own loaded filament is the strongest signal when a printer is known,
  which matters most for the job-queuing consumer this capability is built to serve later.

## R3 — Bridging the Filament Tracking domain to the Profiles domain

**Decision**: Reuse the existing, currently-unused `Filament.FilamentProfileId` (`Guid?`, bare reference, no
FK — `api/src/OpenFarm.Domain/FilamentTracking/Filament.cs:35`) as the sole link from a physical
spool/library candidate to the `FilamentProfile` slicing needs. A candidate is eligible only when that link
is set **and** resolves (`IFilamentProfileRepository.GetByIdAsync`) to a real row — both "never linked" and
"linked but dangling" are treated identically as "skip, not an error" (FR-010).

**Rationale**: This is precisely the linkage `006-filament-tracking`'s FR-013 reserved for future use
("allow a Filament catalog entry to exist ... without being linked to any imported slicer/machine profile
... that linkage stays optional and separate from spool tracking") — this feature is its first real
consumer. No new column, table, or migration is needed to make the bridge exist; it already does.

**Alternatives considered**:
- *Match a Spool's `FilamentVariant`/`Filament` to a `FilamentProfile` by name or material+vendor heuristic
  at resolution time* — rejected: fragile (name collisions, no vendor-matching guarantee) and reinvents what
  the explicit `FilamentProfileId` link already exists to avoid; also contradicts `006`'s own design intent
  that the link is operator-declared, not inferred.
- *Require every `Filament` to carry the link retroactively (a data migration)* — rejected; the spec's own
  Assumptions section already treats an unlinked spool as ineligible-not-broken, and forcing a backfill is
  out of scope for a matching feature.

## R4 — Fleet-loaded eligibility does not add printer-availability filtering

**Decision**: The fleet-loaded tiers (steps 1–2 above) do not filter on `Printer.IsReachable`, `Status`, or
any other liveness/availability signal — a loaded spool counts regardless of whether its printer is
currently online.

**Rationale**: `ResolveSlicingProfileSelection.ResolveForFleetRepresentativeAsync`
(`api/src/OpenFarm.Application/Parts/ResolveSlicingProfileSelection.cs:150-160`), the existing
fleet-representative pick this feature sits alongside for the very same slice request, already doesn't filter
on reachability — it only requires `MachineProfileId`/`ProcessProfileId` to be set. Introducing a new
availability concept just for the filament tier, in the same resolution pass that ignores it for
machine/process, would produce an internally inconsistent result (a slice using printer A's process profile
but refusing printer A's loaded filament because A looks offline). The spec's own Assumptions section defers
this to "whatever the fleet management feature already defines" — today, that's nothing, for this purpose.

**Alternatives considered**:
- *Filter out unreachable printers' loaded spools* — rejected for the reason above; also would make an
  estimate flap between two answers purely based on transient connectivity, undermining SC-004's determinism
  goal.

## R5 — Shape of the reusable matching capability

**Decision**: Replace `RepresentativeFilamentResolver` in place (same namespace,
`OpenFarm.Application.Profiles`) with a resolver exposing the same one-method shape but accepting the
optional target `printerId` the caller (`ResolveSlicingProfileSelection`) already has on hand, and returning
a small result wrapper (`FilamentMatchResult`, see `data-model.md`) instead of a bare `FilamentProfile`, so
the source tier travels with the pick.

**Rationale**: `ResolveSlicingProfileSelection.ResolveAsync(printerId, materialType, narrowedFilamentProfileId,
...)` already receives `printerId` and already only calls the representative resolver in the
"not narrowed" branch (`resolveSlicingProfileselection.cs:45-48`) — FR-011 ("never override a manually
narrowed requirement") is therefore already structurally guaranteed by that existing `? :` branch and needs
no new code. Keeping the capability as one Application-layer class with no dependency on `Parts` types
(it depends only on `IFilamentProfileRepository`, `ISpoolRepository`, `IFilamentRepository`,
`IFeedUnitRepository`) is what makes it genuinely reusable for a future job-queuing consumer (FR-004) —
nothing about its shape is specific to adding a part.

**Alternatives considered**:
- *Leave `RepresentativeFilamentResolver` untouched and add a new, separate resolver only for the STL/part
  flow* — rejected: directly contradicts FR-004's "shared, reusable capability... rather than logic specific
  to one flow," and would leave two competing "pick a filament" code paths to keep in sync.

## R6 — Match-source visibility is deferred, not built this iteration

**Decision**: The matching capability internally classifies which tier produced its pick
(`FilamentMatchResult.Source`, `data-model.md`), because the tiering algorithm has to know this to implement
itself — but nothing carries that classification onto `PartProcessingJob`, `Part`, `PartDetail`, or the
frontend in this feature. No migration, no new DTO fields, no UI change.

**Rationale**: The original ask ("if we have that then our estimate will be more realistic") is about the
*estimate value* being grounded in real inventory — satisfied entirely by FR-012 (the estimate already uses
whichever filament's settings the matching capability picked; that's just how the existing
`ResolveSlicingProfileSelection` → slice pipeline works, no new plumbing needed). Making the *pick's origin*
visible to an operator is a separate, additive concern that nobody asked for explicitly, and building it now
means a migration plus three hand-off points plus frontend work for a capability with no confirmed consumer
yet. Deferred until there's a concrete need — most likely an audit trail or a live "why was this filament
chosen" debugging view, both of which are naturally served by this same `FilamentMatchResult.Source`
classification once that need is real, without having to touch the resolver's shape again.

**Alternatives considered**:
- *Build the visibility now, since it's cheap given the resolver already computes the tier* — rejected: the
  computation is cheap, but propagating and rendering it (migration, three hand-off points, frontend) is not
  free, and speculative schema/API surface is exactly what Constitution Principle IV warns against building
  ahead of a real second consumer.
- *Log the match source via structured logging as a lightweight debugging aid, short of full persistence* —
  considered reasonable but also deferred here to keep this iteration to the minimum that resolves the
  reported bug and the core matching behavior; can be added later alongside whichever future
  audit-trail/live-view feature actually needs it.
