# Phase 3 Research: Filament Tracking

Supersedes the previous version of this file in full. That version modeled `FeedSlot` as a flat entity
(`Kind`: `MultiMaterialSlot` | `ExternalSpool`) with a per-slot `IsAutomaticFilamentReloaderEnabled` flag and
a `StandbySpoolId` assignment. A further design conversation (post-`/speckit-clarify`) replaced that with a
two-level `FeedUnit` → `FeedSlot` hierarchy, an open/extensible `Kind` classification, and a
kind-driven reload policy that removes the per-slot flag and standby assignment entirely. This file resolves
the engineering questions that redesign raises.

## 1. Two-level hierarchy: `FeedUnit` (the physical accessory) → `FeedSlot` (a position within it)

**Decision**: split what was previously a flat `FeedSlot` entity into `FeedUnit` (the physical
accessory — a Multi-Material Unit, an External Spool port, or an Automatic Filament Reloader) and
`FeedSlot` (a single loading position within a `FeedUnit`). An External Spool is simply a `FeedUnit` with
exactly one child `FeedSlot` — not a special-cased type, the same modeling primitive as a 4-tray
Multi-Material Unit with four child slots. This directly reflects how the underlying hardware actually
works: on a single-nozzle printer, every physical source (each AMS tray, the external port) feeds the same
one nozzle through one selectable path — from the printer's own perspective, "which source is active" is
one selection across all of them, not two unrelated systems. Splitting the entity in two mirrors that
reality instead of flattening it.

**Why this matters for extensibility** (the concern that prompted this redesign): before this split, adding
a new physical accessory type meant deciding whether it fit the existing `Kind` enum's two hardcoded values.
After the split, a new accessory type is just a new `FeedUnit.Kind` value with however many child
`FeedSlot`s it reports — no schema change, no new `FeedSlot` variant, since `FeedSlot` itself carries no
kind-specific fields at all (see §3).

## 2. `FeedUnit.Kind` is an open, extensible tag — not a fixed Postgres enum

**Decision**: `FeedUnit.Kind` is a plain `string` column, not a Postgres enum. Known values today:
`"MultiMaterialUnit"`, `"ExternalSpool"`, `"AutomaticFilamentReloader"`. A small in-code registry —
`FeedUnitKindCatalog` (`Domain/FilamentTracking/`) — validates known kinds and resolves each to its
`ReloadPolicy` (§4) and (for `MultiMaterialUnit`) its brand-terminology mapping (§5). This directly mirrors
`OpenFarm.Domain.Printers.PrinterModelCatalog`'s existing shape: a small static lookup resolving
per-key behavior, not a database-enforced closed set. Adding a real future accessory type (a different
brand's splicer, a hardware type not yet invented) is a code change — one new catalog entry — not a
migration. An unrecognized `Kind` string defaults to `ReloadPolicy.None` (§4) rather than throwing, so the
connector-derived sync (§6) never fails outright against a kind it doesn't yet have a policy for — it just
never auto-continues for that kind until a future PR teaches the catalog about it.

**Alternative considered and rejected**: a Postgres enum (`CREATE TYPE feed_unit_kind AS ENUM (...)`),
matching `Spool.State`'s existing pattern. Rejected specifically for this field, and only this field — every
other enum in this feature (`SpoolState`) stays a Postgres enum, since its value set is genuinely fixed by
this feature's own domain logic. `FeedUnit.Kind`'s value set is explicitly meant to grow as new hardware
becomes supported, and a Postgres enum requires a schema migration (`ALTER TYPE ... ADD VALUE`) to add a
value — the one property this design explicitly wants to avoid needing.

## 3. `FeedSlot` carries no kind-specific fields — `FeedUnitId`, `SlotPosition`, `DeactivatedAtUtc`, nothing else

**Decision**: `FeedSlot` is now genuinely minimal: `Id`, `OrganizationId`, `FeedUnitId` (FK), `SlotPosition`
(int), `DeactivatedAtUtc` (nullable). No `Kind` (lives on the parent `FeedUnit` now, not duplicated per
slot — avoiding the same-value-on-every-sibling-row redundancy the earlier flat design had). No
`IsAutomaticFilamentReloaderEnabled`, no `StandbySpoolId` — both removed entirely (§4 explains what replaces
them). Loading a spool onto *any* `FeedSlot` — whether it belongs to a Multi-Material Unit, an External
Spool, or a manually-declared Automatic Filament Reloader — is the identical `LoadSpoolOntoFeedSlotCommand`
call; there is no separate "assign standby" step for any kind.

## 4. Reload policy replaces the per-slot AFR flag and standby assignment entirely

**Decision**: whether and how a `FeedUnit` auto-continues when one of its slots depletes is now purely a
function of its `Kind`, resolved via `FeedUnitKindCatalog`'s `ReloadPolicy`:

| `Kind` | `ReloadPolicy` | Behavior |
|---|---|---|
| `MultiMaterialUnit` | `MatchRequired` | Scans sibling `FeedSlot`s within the same `FeedUnit` for a `Using` spool whose `FilamentVariantId` exactly matches the depleted spool's. Continues only if one exists; otherwise no auto-continuation (the manual path applies, indistinguishable from an ordinary non-automatic depletion) |
| `AutomaticFilamentReloader` | `Unconditional` | Continues onto the next `Using` sibling slot in ascending `SlotPosition` order, with **no material check at all** — real splicer hardware mechanically joins whatever's physically next, it has no way to inspect material. A mismatch still completes the continuation but raises a warning (FR-011) |
| `ExternalSpool` | `None` | Never auto-continues — by construction it only ever has one `FeedSlot`, so there is never a sibling to fall back to |

This directly reflects a real difference in hardware capability the earlier flat design's uniform "standby
assignment" glossed over: a Multi-Material Unit can identify what's physically in each of its own trays
(color/material sensing or RFID, §8) and so can condition continuation on a match; a splicer-style
Automatic Filament Reloader has no such sensing — it just joins the next spool in its physical sequence,
mechanically, regardless of what it is. Modeling both as "a standby spool you assign" erased that
distinction; modeling them as different `ReloadPolicy` values driven by `Kind` keeps it, and keeps the whole
mechanism config-free — no toggle to remember to enable, no assignment step to forget.

**Which spool counts as the "auto-continuation target"**: for `MatchRequired`, any `Using` sibling
`FeedSlot` in the same `FeedUnit` whose spool's `FilamentVariantId` equals the depleted spool's; if more than
one qualifies, the lowest `SlotPosition` wins (a simple, deterministic tie-break — no FR distinguishes
between multiple equally-valid candidates, so any consistent rule satisfies it). For `Unconditional`, the
lowest-`SlotPosition` `Using` sibling regardless of variant, since there's no matching step to filter by.

**Alternative considered and rejected**: keep a per-slot boolean plus an explicit `StandbySpoolId`, and layer
a `Kind`-driven "does this kind require a match" rule on top of it. Rejected as strictly more moving parts
for the same outcome — an explicit standby assignment is redundant once the rule is "whichever
already-loaded sibling qualifies" (match-based or positional), and it reintroduces exactly the
configuration-step-to-forget problem the simplification was meant to remove. It would also fail to represent
the real, common way operators actually use a Multi-Material Unit for continuation: pre-loading two matching
spools into two trays before a long print, with neither ever explicitly "assigned" as anyone's backup — the
system should recognize that setup on its own, not require a separate configuration action describing what's
already physically true.

## 5. Brand terminology stays scoped to `Kind = MultiMaterialUnit` only

**Decision**: `FeedUnitKindCatalog`'s brand-terminology resolution (`"AMS"` for Bambu, `"MMU"` for a future
Prusa connector, default `"Multi-Material Unit"`) applies only when `Kind = "MultiMaterialUnit"` — computed
at read time from `Printer.Brand`, never stored (unchanged reasoning from the prior version of this file:
`Printer.Brand` is the one source of truth, a cached label could drift from it). `ExternalSpool` always
displays as `"External Spool"` regardless of brand — it isn't a branded concept manufacturers name
differently. `AutomaticFilamentReloader` displays using the operator's own declared name (§7) — there's no
brand to resolve against at all, since it's third-party hardware the printer's own connector never reports.

## 6. Multi-Material Unit and External Spool stay strictly connector-derived — unchanged from the prior design

**Decision**: unchanged from the previous version of this file. `FeedUnit`/`FeedSlot` rows of `Kind =
MultiMaterialUnit` or `ExternalSpool` are synced automatically from a printer's live connection via
`IFeedSlotTelemetrySync`, called from `PrinterConnectionBackgroundService.HandleEventAsync`'s existing
flush-gated telemetry block, reading `Printer.AmsUnits`/`Printer.ExternalSpoolTray` (merged state, not raw
per-message deltas). `Printer.ExternalSpoolTray` (a nullable, reused `AmsTray` value) is still populated by
extending `BambuMqttMessageParser` to read `print.vt_tray` (confirmed present in real payloads, previously
unparsed) — this piece of the design is unaffected by the `FeedUnit`/`FeedSlot` split; it just now produces
one `FeedUnit` row (`Kind = ExternalSpool`, one child `FeedSlot`) instead of a single flat `FeedSlot` row.
Soft-deactivation (`FeedSlot.DeactivatedAtUtc`, never a hard delete) is unchanged in rationale — a `Spool`
may still reference a slot the printer's live connection has stopped reporting.

## 7. `AutomaticFilamentReloader` is the one Feed Unit manually created (and removed) by the operator

**Decision**: since real splicer-style hardware (e.g. an "Infinite Flow"-style device) is invisible to a
printer's own firmware/telemetry — the printer just sees whatever material is currently coming through
its external port, with no signal that a splicer is transparently managing multiple spools behind it — an
operator manually adds an `AutomaticFilamentReloader` `FeedUnit` for a printer: a name and a spool-position
count (via `AddFeedUnitCommand`, §13 — unified with revealing an `ExternalSpool` position), which
immediately provisions that many child `FeedSlot` rows (`SlotPosition` 0..N-1) — there's no telemetry event
to discover them from, so they're provisioned upfront rather than synced incrementally. This is the one
deliberate, narrow exception to "Feed Units are strictly connector-derived" (§6), scoped to exactly one
`Kind`.

**Removal is also manual, for the same reason**: there's no telemetry signal for "the operator unplugged
their splicer," so removal is an explicit operator action (via `RemoveFeedUnitCommand`, §13) rather than
something the sync process could ever detect on its own. This is the intentional asymmetry between this
`Kind` and the other two — sync-managed end-to-end vs. operator-managed end-to-end, never a mix of both for
the same `Kind`.

**Physical wiring note, deliberately not modeled**: an Automatic Filament Reloader typically feeds *through*
a printer's External Spool port (sometimes via a Y-connector so the operator can choose between the AFR and
a direct spool). OpenFarm does not attempt to model this physical relationship — the `AutomaticFilamentReloader`
`FeedUnit` is tracked as fully independent of any `ExternalSpool` `FeedUnit` on the same printer. This is a
deliberate scope limit, not an oversight: nothing in spec.md asks the system to reconcile "what the printer's
own telemetry reports through its external port" against "what the operator has separately declared their
splicer is doing" — modeling that relationship would require assumptions about wiring this design has no way
to verify, and no FR depends on it.

## 8. RFID-detected material — gated purely on live per-slot data presence, no `Kind` restriction at all

**Revised** (supersedes this section's original version, which categorically excluded `ExternalSpool`).
External research into real hardware — prompted by a design question about whether the `FeedUnit` model
needed to change for multi-toolhead printers — surfaced a confirmed counter-example: the **Snapmaker U1**
is a 4-independent-toolhead printer where each individual, single-slot filament input reads an RFID tag on
load (Mifare 1K tags, per Snapmaker's own documentation and community firmware pages). Structurally, each
U1 input is exactly what this design calls an `ExternalSpool`-kind unit (one `FeedUnit`, one child
`FeedSlot`) — so "an `ExternalSpool` never carries RFID-detected data" was not merely an untested
assumption, it is now a *confirmed-false* claim for at least one real product category.

**Decision**: the mechanism itself is unchanged — a non-blocking load-time validation only (FR-021), never
auto-selecting or replacing the operator's spool choice. What changes is the gating: there is no `Kind`-level
restriction anywhere anymore. The handler checks exactly one thing, uniformly across every `FeedUnit.Kind`:
does *this specific `FeedSlot`'s current telemetry* report non-empty detected fields at the moment of the
load call? If yes, compare; if no (nothing loaded, or this specific hardware/product never reads tags at
all), skip — identical treatment regardless of which kind the slot's `FeedUnit` is. `AutomaticFilamentReloader`
slots still never trigger the comparison, but only because they genuinely have no telemetry backing them at
all (§7) — a consequence of how that `Kind` is created, not a rule about RFID specifically.

**Why this is the right generalization, not overfitting to one product**: the corrected rule is *simpler*
than the one it replaces — "check live data presence, full stop" rather than "check live data presence, but
first apply a `Kind`-based carve-out." The carve-out added a rule that turned out to be both unnecessary (the
per-instance check already handled every case correctly on its own) and wrong (contradicted by real
hardware) — removing it is a straightforward simplification, not a new special case bolted on for
Snapmaker specifically. No schema change was needed to make this correction — `FeedUnitKindCatalog` never
encoded RFID capability by `Kind` to begin with; only the `Application`-layer validation's own gating logic
(`LoadSpoolOntoFeedSlotCommandHandler`) and its documentation needed correcting.

## 9. `ReplacesFeedUnitId` — an optional relationship for a port-sharing Automatic Filament Reloader

**Decision**: `FeedUnit` gains one more optional field, `ReplacesFeedUnitId: Guid?` (self-referencing FK),
settable only when adding an `AutomaticFilamentReloader` (`AddFeedUnitCommand`, `Kind =
AutomaticFilamentReloader`, §13), never on a connector-derived unit. Must reference an `ExternalSpool`-kind
`FeedUnit` on the same printer when provided.

**Why this is needed**: an Automatic Filament Reloader physically wires into a printer in one of two ways —
alongside the External Spool port via a bypassable (Y-)connector, where both remain independently usable, or
as a full replacement via a multi-way splitter with no direct-feed bypass, where the External Spool port is
no longer really available for a direct spool at all. Without distinguishing these, every AFR installation
would show an always-visible, seemingly-unrelated `ExternalSpool` unit alongside it — confusing specifically
for the population of users who bought this hardware. §12 covers what this relationship actually *does*
(hide the superseded unit by default) — this section is only about how the relationship itself is captured.

**Scope limit, deliberate**: only `ExternalSpool` is a valid target — an AFR replacing a specific
`MultiMaterialUnit` tray isn't a realistic wiring pattern (AMS trays are enclosed, not exposed for external
rerouting), so this isn't generalized into a broader Feed-Unit-to-Feed-Unit wiring graph with no other
stated need behind it (Constitution IV). Fixed at creation, not independently editable afterward — a rare,
low-frequency operator action (rewiring hardware) not worth its own update endpoint; changing it means
deactivating and re-declaring the Automatic Filament Reloader.

## 10. Multi-toolhead printers (Bambu H2D/H2C) — confirmed the existing model generalizes without change

Prompted by a design question about whether `FeedUnit` needed remodeling for multi-toolhead hardware.
Bambu's H2D/H2C (dual-nozzle) printers have a filament buffer with two base inlet ports, each expandable via
a "4-in-1 PTFE Adapter," supporting up to 4× AMS 2 Pro units + 8× AMS HT units + 1 external spool
concurrently (per Bambu's own product page and wiki) — materially more complex than either "one input" or
"two inputs" as a first approximation. **Confirmed**: this does not require any change to the
`FeedUnit`/`FeedSlot` model. `FeedUnit.UnitIndex` already generalizes to "however many physical units of a
given `Kind` a printer reports" (designed from the very first version of this research to cover an "AMS 1 +
AMS 2 + AMS 3 + AMS 4" scenario) — an H2D reporting several `MultiMaterialUnit` units plus an `ExternalSpool`
is the same shape, just with more `UnitIndex` values populated. **Explicitly deferred, not modeled**: which
nozzle/toolhead a given `FeedUnit` ultimately routes to. This is a print-job routing concern — `Printer` has
no toolhead/nozzle concept anywhere in this codebase today, and no FR in this phase (pure inventory
tracking, no `Job` entity yet) needs one. Revisit only once a later phase (Phase 8+) needs to assign a job
to a specific toolhead — at that point `FeedUnit` gaining an optional `ToolheadIndex`-shaped field is a
small, additive change, not a redesign, since nothing about the current shape assumes a single nozzle.

## 11. Multi-Material Unit ↔ External Spool relationship — auto-derived, not declared (unlike Automatic Filament Reloader)

**Decision**: unlike `ReplacesFeedUnitId` (§9, necessarily a stored, operator-declared field since an
Automatic Filament Reloader is invisible to any printer's telemetry), the relationship between a
`MultiMaterialUnit` and a printer's `ExternalSpool` position is derived entirely at read time, with **no new
persisted field**. Confirmed by research: on single-nozzle Bambu printers, a firmware-level "Enable/Disable
AMS" toggle switches between the two as alternate sources for the same one physical path — but that toggle
is per-print/job-level state this phase doesn't track (no `Job` entity), and *whether an AMS is attached at
all* is directly, always observable from live telemetry (`Printer.AmsUnits` non-empty). So:
`GetFeedUnitsQuery` computes an `ExternalSpool` unit's `supersededByFeedUnitId` from **either** source —
an operator-declared `AutomaticFilamentReloader.ReplacesFeedUnitId` pointing at it (§9, explicit), **or**,
absent that, whether the same printer currently has at least one non-deactivated `MultiMaterialUnit` unit
(implicit, auto-derived, FR-023) — preferring the explicit declaration if both happen to apply.

**Why no stored field here, unlike §9**: storing an equivalent of `ReplacesFeedUnitId` for this relationship
would duplicate a fact already fully derivable from existing, always-current data (`FeedUnit` rows of `Kind
= MultiMaterialUnit` that aren't deactivated) — a second, redundant source of truth with its own staleness
risk for zero benefit, the same reasoning this codebase already applies everywhere else a value can be
computed at read time from data that's already the actual source of truth (Profiles' display fields, this
feature's own display-label computation, §5).

**Scope limit, confirmed deliberately, not by default**: this relationship never applies between two
`MultiMaterialUnit` rows on the same printer, however many exist. Multiple Multi-Material Units connected
together (Bambu's own multi-AMS chaining, §10) form one unified, simultaneously-selectable pool of
positions by design — unlike a Multi-Material Unit and an External Spool, which really are alternate sources
for the same one physical path (§0/§1's own framing). Hiding additional Multi-Material Units the way an
`ExternalSpool` gets hidden would misrepresent real, independently-usable capacity as redundant.

**Confirms no separate "active source" concept is needed**: the real "Enable/Disable AMS" toggle mentioned
above actually conflates two different facts — (a) whether the operator's physical wiring makes the
External Spool concurrently usable at all (a slow-changing, wiring-level fact — exactly what
`supersededByFeedUnitId`/`isRevealed`, §12, capture), and (b) which source a *specific print job* is
actively drawing from right now (a per-print, job-execution-time fact). Only (a) is in this phase's scope.
`Spool.State = Using` has only ever meant "physically loaded onto this slot," not "currently feeding the
nozzle" — the auto-continuation design (§4) already relies on two sibling spools both being `Using`
simultaneously with only one actually printing from at a time, so a spool `Using` a revealed `ExternalSpool`
slot alongside spools `Using` `MultiMaterialUnit` trays is already representable with zero new state. (b)
remains entirely out of scope until a `Job` entity exists to ask "which source did this print draw from."

## 12. Hide-by-default, not just advisory — a superseded External Spool position is omitted from the listing entirely

**Revised** (supersedes §9's and §11's original "advisory-only, always shown" framing for how a superseded
`ExternalSpool` position is *presented*, though not how the relationship itself is captured). On reflection,
a purely advisory flag (shown but annotated) undersells the real physical situation: a printer normally has
exactly one filament input, and once a Multi-Material Unit or Automatic Filament Reloader has taken it over,
the raw external port genuinely isn't a separate, independently-usable thing from the operator's point of
view — showing it as a distinct (if flagged) entry misrepresents the hardware, not just the UI.

**Decision**: `GetFeedUnitsQuery` omits an `ExternalSpool` unit from its response entirely when **all** of
the following hold:
1. `supersededByFeedUnitId` is non-null (declared via an AFR's `ReplacesFeedUnitId`, §9, or auto-derived
   from an active `MultiMaterialUnit`, §11);
2. `FeedUnit.IsRevealed` is `false` (new field, default `false` — an operator flips this on to indicate a
   bypass/multi-way connector genuinely makes the port usable alongside whatever supersedes it, FR-024);
3. no `Spool` is currently `Using` any of its `FeedSlot`s (FR-025's safety net — an actively-tracked spool
   must never silently disappear from the listing, regardless of the hidden/revealed state, e.g. leftover
   from before the superseding accessory was attached, or loaded via a connector the operator never got
   around to formally revealing).

`IsRevealed` is set via `AddFeedUnitCommand`/cleared via `RemoveFeedUnitCommand` (§13) — the one narrow,
explicit exception to FR-005a's "operators MUST NOT manually create/rename/delete a connector-derived Feed
Unit": neither call creates, renames, or deletes the underlying `FeedUnit` row for `ExternalSpool` (it
already exists, sync-owned); they only flip a visibility flag. `Kind ∈ {MultiMaterialUnit,
AutomaticFilamentReloader}` units and a not-superseded `ExternalSpool` are never filtered — this omission
logic applies to exactly the one case §9/§11 describe.

**Alternative considered and rejected (advisory-only, unconditionally shown)**: this was the original design
— rejected once it became clear "mimic the physical view" is the more honest default: an operator with a
plain single-AMS setup and no connector has no real, independently-usable external port to see, so showing
one (even flagged) implies a capability that doesn't exist for their hardware. `supersededByFeedUnitId`
itself is unchanged and still computed the same way (§9/§11) — only whether the unit appears in the default
listing changed.

## 13. Unifying "declare/remove an Automatic Filament Reloader" and "reveal/re-hide an External Spool" into one add/remove action

**Revised** (supersedes §7's and §12's original framing of these as two differently-named mechanisms —
`CreateAutomaticFilamentReloaderCommand`/`DeactivateAutomaticFilamentReloaderCommand` alongside a separate
`RevealFeedUnitCommand`/`PUT .../reveal`). On reflection, both pairs represent the exact same underlying
fact from the operator's perspective: hardware or wiring the printer's own connection can't fully account
for on its own — an Automatic Filament Reloader is entirely invisible to telemetry, and a bypass/multi-way
connector making the External Spool concurrently usable is *equally* invisible to telemetry (telemetry can
only ever tell us an AMS is attached, never whether the operator has also wired in a way to use the spool
port alongside it). Presenting them as two different actions with two different names was an unforced
inconsistency, not a reflection of any real underlying difference.

**Decision**: one unified `AddFeedUnitCommand` (`Kind`-discriminated request) and one unified
`RemoveFeedUnitCommand` (dispatches on the target row's own `Kind`) replace all three of the previous
commands:
- `AddFeedUnitCommand` with `Kind = AutomaticFilamentReloader`: requires `Name` + slot count (+ optional
  `ReplacesFeedUnitId`) — creates brand-new `FeedUnit`/`FeedSlot` rows, exactly as `CreateAutomaticFilamentReloaderCommand` did.
- `AddFeedUnitCommand` with `Kind = ExternalSpool`: no `Name`/slot count needed (the row and its one slot
  already exist, sync-owned) — finds the printer's existing `ExternalSpool` `FeedUnit` and sets
  `IsRevealed = true`. Idempotent (no error if already revealed). Rejects (FR-026) if the printer has never
  actually reported an `ExternalSpool` at all — nothing exists to reveal.
- `RemoveFeedUnitCommand`, keyed by the target `FeedUnit`'s id: if `Kind = AutomaticFilamentReloader`,
  deactivates it (unchanged behavior). If `Kind = ExternalSpool`, sets `IsRevealed = false` (idempotent
  re-hiding) — still subject to FR-025's loaded-spool safety net, so it doesn't vanish while occupied.

**This also resolves §12's discoverability gap, more cleanly than the `includeHidden` parameter did**:
revealing no longer requires the client to already know the hidden `ExternalSpool` unit's id — the operator
just calls `AddFeedUnitCommand` with `Kind = ExternalSpool` for a given printer, and the handler finds the
(singleton) row itself. The `includeHidden` query parameter this file's previous version introduced
specifically to solve "how does the client discover a hidden unit's id to reveal it" is no longer needed for
that purpose and is removed — keeping it around would be exactly the kind of unused surface Constitution
Principle IV warns against once the problem it solved no longer exists. (A "manage Feed Units" UI can still
choose to show hidden units for *informational* purposes, but that's a product decision for the
implementation phase, not something this design needs to support via a dedicated query parameter.)

## Summary of resolved decisions

| Item | Resolution |
|---|---|
| Entity shape | Two-level `FeedUnit` (physical accessory) → `FeedSlot` (position within it); External Spool is a one-slot `FeedUnit`, not a special case (§1) |
| `Kind` representation | Open string, validated/resolved via `FeedUnitKindCatalog` (mirrors `PrinterModelCatalog`), not a Postgres enum — extensible without a migration (§2) |
| `FeedSlot` schema | `FeedUnitId`, `SlotPosition`, `DeactivatedAtUtc` only — no kind, no AFR flag, no standby field (§3) |
| Auto-continuation mechanism | Kind-driven `ReloadPolicy` (`MatchRequired` / `Unconditional` / `None`), fully replacing the per-slot flag + `StandbySpoolId` (§4) |
| Brand terminology | Scoped to `MultiMaterialUnit` only; `ExternalSpool` always "External Spool"; `AutomaticFilamentReloader` uses its operator-given name (§5) |
| MMU/ExternalSpool creation | Unchanged — strictly connector-derived, soft-deactivated not deleted (§6) |
| `AutomaticFilamentReloader` creation/removal | Manually added/removed (`AddFeedUnitCommand`/`RemoveFeedUnitCommand`, unified with revealing/re-hiding External Spool, §13), provisioned with a fixed slot count upfront (§7) |
| RFID validation | No `Kind` restriction at all — gated purely on live per-slot data presence; corrected after Snapmaker U1 disproved the earlier categorical `ExternalSpool` exclusion (§8) |
| Port-sharing Automatic Filament Reloader | Optional `ReplacesFeedUnitId` (self-ref, AFR → `ExternalSpool` only), fixed at creation (§9) |
| Multi-toolhead printers (H2D/H2C) | Confirmed existing `UnitIndex` generalization already covers this; toolhead/nozzle routing explicitly deferred, not modeled (§10) |
| Multi-Material Unit ↔ External Spool relationship | Auto-derived at read time (no stored field), never extended to multiple Multi-Material Units, confirms no separate "active source" concept needed (§11, FR-023) |
| Superseded External Spool presentation | Omitted from the default Feed Unit listing entirely (not just flagged), with an operator-settable `IsRevealed` escape hatch and a loaded-spool safety net (§12, FR-022/FR-024/FR-025) |
| Add/remove Feed Unit unification | One `AddFeedUnitCommand`/`RemoveFeedUnitCommand` pair, `Kind`-discriminated, covers both declaring/removing an Automatic Filament Reloader and revealing/re-hiding an External Spool — resolves discoverability without a separate `includeHidden` parameter (§13, FR-005c/FR-024/FR-026) |
