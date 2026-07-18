# Implementation Plan: Filament Tracking

**Branch**: `006-filament-tracking` | **Date**: 2026-07-18 | **Spec**: [spec.md](./spec.md)

**Input**: Feature specification from `/specs/006-filament-tracking/spec.md`

**Note**: This template is filled in by the `/speckit-plan` command. See `.specify/templates/plan-template.md` for the execution workflow.

## Summary

Add a real filament-tracking capability replacing the two existing mock-data prototypes
(`web/src/features/filament/`, `web/src/features/spools/`): a reusable `Vendor` → `Filament` →
`FilamentVariant` catalog, and a `Spool` entity tracked through an explicit `InStorage` / `Using` /
`Finished` state machine mirroring `Printer.Status`'s existing pattern. A spool's remaining weight is
estimated automatically from an append-only usage ledger (`SpoolUsageRecord`) and can be manually
corrected. `Vendor` management ships as a lightweight tab embedded in the Filament feature page rather than
its own nav item.

This phase's central entity is a two-level `FeedUnit` (the physical accessory) → `FeedSlot` (a loading
position within it) hierarchy — arrived at after two rounds of design revision once it became clear the
domain needed to reflect real hardware more faithfully than an initial flat model did. A `FeedUnit`'s
`Kind` is an open, extensible tag (`MultiMaterialUnit`, `ExternalSpool`, `AutomaticFilamentReloader`, and
any future kind), resolved via a small static `FeedUnitKindCatalog` — the same shape
`OpenFarm.Domain.Printers.PrinterModelCatalog` already establishes for per-key behavior lookups — rather
than a fixed Postgres enum, so a new hardware type is a code change, not a migration. `MultiMaterialUnit`
and `ExternalSpool` units are strictly connector-derived: synced automatically from a printer's live
connection (a new `IFeedSlotTelemetrySync`, called from the existing `PrinterConnectionBackgroundService`)
because a printer's real feed topology — including RFID-detected material on some Multi-Material trays —
is only knowable once OpenFarm has connected to it. `AutomaticFilamentReloader` is the one deliberate
exception: real splicer-style hardware (e.g. an "Infinite Flow"-style device) is invisible to any printer's
telemetry, so its `FeedUnit` is manually declared (and manually removed) by the operator instead.

There is no per-slot "enable auto-reload" flag and no standby-spool assignment anywhere — automatic
continuation is determined entirely by a `FeedUnit`'s `Kind` via its resolved `ReloadPolicy`:
`MultiMaterialUnit` requires a same-Filament/color spool already loaded in a sibling slot
(`MatchRequired`, matching how a real AMS auto-refill can identify what's in each of its own trays);
`AutomaticFilamentReloader` continues onto the next loaded sibling regardless of material
(`Unconditional`, matching how a real splicer mechanically joins filament without inspecting it — still
raising a mismatch warning, never blocking); `ExternalSpool` never auto-continues (`None`, it only ever has
one slot). This phase also extends the already-shipped `BambuMqttMessageParser` to read `print.vt_tray`
(confirmed present in real payloads, previously unparsed) so an `ExternalSpool` unit syncs the same way a
`MultiMaterialUnit` does, and adds a non-blocking detected-material validation at spool-load time (FR-021).
This check is gated purely on live per-slot data presence — **not** by `Kind` at all: external research
into real multi-toolhead/individual-input hardware (Bambu H2D/H2C, Snapmaker U1) confirmed a genuine
counter-example to an earlier categorical "External Spool never carries RFID data" rule (Snapmaker U1 reads
RFID on individually-addressable, single-slot inputs structurally identical to this feature's
`ExternalSpool` kind), so the check now runs identically for any kind whose telemetry currently reports
detected material. The operator still always picks which physical `Spool` is loaded; detection only checks
the pick. That same research confirmed the `FeedUnit`/`FeedSlot` model needs no structural change for
multi-toolhead printers — `UnitIndex` already generalizes to however many units a printer reports (Bambu
H2D can report several Multi-Material Units plus an External Spool); which nozzle a unit routes to is
explicitly deferred to a later, job-execution-aware phase. Since an Automatic Filament Reloader typically
wires through a printer's External Spool port — alongside it via a bypassable connector, or fully replacing
it via a multi-way splitter — an operator may optionally declare that relationship
(`FeedUnit.ReplacesFeedUnitId`, FR-022). A Multi-Material Unit's own relationship to the External Spool port
is handled differently: since its presence is directly telemetry-observable (unlike an Automatic Filament
Reloader), it's auto-derived at read time with no stored field at all (FR-023) — never extended to multiple
Multi-Material Units on the same printer, which form one unified pool rather than alternate sources for one
path. Either way, a superseded External Spool position is **omitted from the default Feed Unit listing
entirely** — mirroring the physical reality of one filament input per printer, not merely flagged — unless
an operator reveals it via a new `FeedUnit.IsRevealed` flag (FR-024, for bypass/multi-way connector setups)
or a spool is currently loaded onto it (FR-025's safety net, so an actively-tracked spool never silently
disappears). Revealing/re-hiding an External Spool position and adding/removing an Automatic Filament
Reloader are the same single pair of operator actions — one unified `POST`/`DELETE /feed-units` — so the
operator never needs to discover a hidden unit's id ahead of time to reveal it: the "add" call finds the
printer's own singleton `ExternalSpool` row itself. This never blocks loading a spool directly onto a
superseded position either way — that stays a non-blocking `portSharingNote`. Two SignalR-pushed real-time
channels (`IFilamentTrackingEventPublisher`,
reusing the existing `/hubs/farm` hub) keep spool state and Feed Unit topology in sync across every open
view.

## Technical Context

**Language/Version**: C# 13 / .NET 10 (`api/`), TypeScript 5 / React 19 (`web/`) — unchanged from
Phase 0–2.

**Primary Dependencies**: No new NuGet packages. The state machine, usage ledger, `FeedUnitKindCatalog`,
and auto-continuation orchestration are plain C#/EF Core, following the exact shape `Printer`'s status
machine, `PrinterModelCatalog`, and `Application`-layer command handlers already established. The Feed Unit
telemetry sync reuses `System.Text.Json` parsing already in `BambuMqttMessageParser` — one new private
method (`ParseExternalSpool`), no new library. No new frontend dependencies.

**Storage**: Existing Postgres via `OpenFarmDbContext`. Two migrations in this feature's PR:
`AddFilamentTracking` (seven new tables — `Vendors`, `Filaments`, `FilamentVariants`, `FeedUnits`,
`FeedSlots`, `Spools`, `SpoolUsageRecords`, `data-model.md`) and `AddExternalSpoolTelemetry` (one new
nullable `jsonb` column, `Printer.ExternalSpoolTray`, on the existing `Printers` table — kept separate since
it's a schema-coherent change to a different table). This is the only change this feature makes to
`Printers`' schema. `FeedUnit.Kind` is a plain `string` column, deliberately **not** a Postgres enum
(`research.md` §2) — the one field in this feature designed to grow without a migration. `Spool.State` is a
Postgres enum with `Domain`-enforced transitions, the same pattern the constitution already names for
`printers.status`/`jobs.status`. `Spool` additionally uses Postgres's `xmin` system column as an EF Core
optimistic-concurrency token and a partial unique index guaranteeing at most one spool is ever `Using` a
given feed slot. `FeedUnit`/`FeedSlot` rows of `Kind ∈ {MultiMaterialUnit, ExternalSpool}` are written
exclusively by `IFeedSlotTelemetrySync`; `Kind = AutomaticFilamentReloader` rows are written exclusively by
a small pair of operator-facing commands (create/deactivate) — no code path crosses that ownership
boundary.

**Testing**: TDD (xUnit + FluentAssertions, `OpenFarm.Domain.Tests`) for `SpoolStateTransitions`, the
usage-decrement/floor/overrun math (the constitution's own named TDD-non-negotiable "filament
weight-tracking math" example), the `RegisterSpoolCommandValidator`/weight-relationship FluentValidation
gates, and `FeedUnitKindCatalog.Resolve` (a small, deterministic, well-specified lookup — same bar as
`PrinterModelCatalog.Resolve`'s existing TDD coverage). `BambuMqttMessageParser.ParseExternalSpool` is
test-after/fixture-replay — **not** TDD, matching the rest of that parser's existing classification.
Test-after for orchestration-only `Application` handlers (`RegisterSpoolCommandHandler`,
`IFeedSlotTelemetrySync`'s diff/upsert logic, `SpoolUsageRecordedHandler`'s policy-driven continuation
branch, catalog CRUD handlers, the RFID-comparison logic), EF Core repository/configuration code, and React
components/pages. `WebApplicationFactory` + Testcontainers.PostgreSql for API integration tests (catalog →
spool → load-with-RFID-check → usage → both `MatchRequired` and `Unconditional` continuation paths → manual
Automatic Filament Reloader create/deactivate round trip, plus the concurrency-conflict and
one-spool-per-slot cases). Vitest + RTL for `web/src/features/spools`, `web/src/features/filament` (with
the embedded Vendor tab) and the extended `web/src/features/printers` Feed Units card.

**Target Platform**: Linux containers (Docker Compose), ASP.NET Core host, Vite-built SPA — unchanged.

**Project Type**: Web application — existing `api/` + `web/` monorepo, extended in place.

**Performance Goals**: SC-001 (register a spool in under 30 seconds) — no new latency source. SC-004
(auto-continuation succeeds in ≥95% of qualifying cases) is a correctness property of the deterministic
policy resolution, not a load/throughput target. The Feed Unit telemetry sync must not add per-MQTT-message
overhead: it runs only inside the existing 1-second coalesced flush window, and only writes when a diff
against currently-stored units/slots is non-empty.

**Constraints**: Every spool list/detail read must show current state and remaining weight without a
second round-trip (SC-002) — denormalized joined-read response shapes, all computed at read time via joins,
never cached columns. Real-time updates for spool state and Feed Unit topology changes must go through
`/hubs/farm`, not a new polling path.

**Scale/Scope**: Single self-hosted organization scope, unchanged from prior phases. Feed Unit/Slot count
per printer is small and bounded by real hardware — no virtualization concern.

## Constitution Check

*GATE: Must pass before Phase 0 research. Re-check after Phase 1 design.*

- **I. Clean Architecture**: `Domain/FilamentTracking/` holds `Vendor`, `Filament`, `FilamentVariant`,
  `FeedUnit`, `FeedSlot`, `FeedUnitKindCatalog`, `ReloadPolicy`, `Spool`, `SpoolState`,
  `SpoolStateTransitions`, `SpoolUsageRecord` — framework-agnostic, no `FastEndpoints`/EF references.
  Cross-aggregate orchestration (the policy-driven continuation touching both `Spool` and `FeedUnit`/
  `FeedSlot`; the telemetry-sync diff touching many rows at once) lives in `Application`
  (`SpoolUsageRecordedHandler`, `FeedSlotTelemetrySyncHandler`), not bolted onto either `Domain` entity.
  `BambuMqttMessageParser.ParseExternalSpool` stays in `Infrastructure.Printers.Bambu`, alongside its
  sibling `ParseTray`. `Api` endpoints stay thin FastEndpoints adapters. **Pass.**
- **II. Test Discipline**: `SpoolStateTransitions`, the usage-decrement/floor/overrun math, and
  `FeedUnitKindCatalog.Resolve` all get TDD — deterministic, well-specified, and (for the catalog) the exact
  same classification `PrinterModelCatalog.Resolve` already carries. `ParseExternalSpool` is test-after/
  fixture-replay, matching its sibling `ParseTray`. Everything else (`Application` orchestration including
  the telemetry-sync diff, the policy-driven continuation branch, and the RFID comparison; EF repositories;
  React components) is test-after, per the constitution's existing carve-outs. **Pass.**
- **III. Consistent UX**: spool state changes, Feed Unit auto-continuations, and Feed Unit topology changes
  all push through the existing `/hubs/farm` hub via a new `IFilamentTrackingEventPublisher` — no view falls
  back to polling. A Feed Unit's display label (brand-resolved for `MultiMaterialUnit`, operator-named for
  `AutomaticFilamentReloader`, fixed for `ExternalSpool`) is computed once and reused everywhere it's shown.
  Forms use react-hook-form + zod, primitives from `src/components/ui/`. All errors surface as
  `ProblemDetails`. **Pass.**
- **IV. Build for Expansion, Not Speculation**: `organization_id` present on every new tenant-scoped table.
  `SpoolUsageRecord.JobId` is reserved-but-unused this phase, the same "schema now, consumer later" shape
  already sanctioned for `role_scopes`. `FeedUnit.Kind` being an open string rather than a Postgres enum is
  this principle applied directly to a real, stated need (a future hardware type) rather than spread
  speculatively across fields with no such need — every other enum in this feature (`SpoolState`) stays a
  closed Postgres enum precisely because nothing asks it to grow. The whole `FeedUnit`/`FeedSlot`/
  `ReloadPolicy` shape is itself the outcome of this principle cutting twice in the same direction: an
  operator-created flat `FeedSlot` (this feature's first pass) was reconsidered because a printer's real
  topology can't be hand-declared accurately; a per-slot AFR flag + standby assignment (the second pass) was
  further reconsidered because it modeled two materially different hardware behaviors (material-aware
  switching vs. blind mechanical splicing) as one undifferentiated toggle. Both revisions removed
  complexity that wasn't earning its keep rather than adding speculative flexibility. A third, smaller
  correction cuts the same way: external research into real multi-toolhead/individual-input hardware
  disproved an assumption this design had asserted (RFID being categorically impossible for `ExternalSpool`)
  — the fix was to *remove* a `Kind`-based special case, not add one, landing on a simpler, more uniform
  rule than what it replaced. The research also confirmed no structural change is needed for multi-toolhead
  printers (Bambu H2D/H2C) — `FeedUnit.UnitIndex` already generalizes, and toolhead/nozzle routing is
  deliberately deferred rather than spread across this phase's schema with no consumer for it yet. A fourth
  revision — hiding a superseded External Spool by default rather than merely flagging it — is this
  principle applied to *presentation* rather than schema: an always-visible, always-annotated entry would
  have been the speculative-flexibility direction (showing a capability most single-AMS operators don't
  actually have), while hiding it (with `IsRevealed`/a loaded-spool safety net as the only two escape
  hatches, both driven by real, stated need) matches what the hardware actually offers by default. **Pass.**
- **V. Performance and Real-Time Responsiveness**: no polling anywhere new. No unbounded-list
  virtualization concern. No recursive-CTE concern. `RemainingWeightGrams` stays a stored,
  incrementally-updated column. The telemetry sync's performance constraint (diff-before-write, piggyback on
  the existing coalesced flush) is this principle applied to the one new always-on background work this
  feature adds. **Pass.**
- **Security & Multi-Tenancy Isolation**: every new tenant-scoped table uses `IsMultiTenant()` exclusively.
  No secrets involved. Every operator-facing state-mutating command (including the new unified
  `AddFeedUnitCommand`/`RemoveFeedUnitCommand` pair) returns a single entity
  id, so the existing single-`Id`-reflection `ActivityLogPostProcessor` path covers all of them with zero
  pipeline changes. `AddFeedUnitEndpoint`/`RemoveFeedUnitEndpoint` carry their own real
  `[Permission("feed-units:add")]`/`[Permission("feed-units:remove")]` attributes despite one branch of each
  being a narrow, single-field toggle (revealing/re-hiding External Spool) — no endpoint in this feature
  skips the permission gate regardless of how small a given branch's effect is.
  `IFeedSlotTelemetrySync`'s own writes run inside `PrinterConnectionBackgroundService`, outside any HTTP
  request — logged directly via `IActivityLogWriter` with `ActorType.System`, the same pattern that
  background service already uses for its own `PrinterStatusObserved` entries. Auth is the existing real JWT
  flow; every new endpoint carries a real `[Permission("...")]` attribute. **Pass.**

No Constitution Check violations. `research.md` records the real design calls this feature makes — most
notably the two-round revision from a flat, operator-created `FeedSlot`, through a connector-derived flat
`FeedSlot` with a per-slot AFR flag, to the final `FeedUnit`/`FeedSlot` split with a kind-driven
`ReloadPolicy` — for visibility in Complexity Tracking below, same convention
`specs/003-slicer-profile-import/plan.md` used.

**Post-Phase-1-design re-check** (per `research.md`/`data-model.md`/`contracts/`): confirms all of the
above with no new violations. `PrinterConnectionBackgroundService` (in `Printers`) taking a direct
dependency on `Application.FilamentTracking.IFeedSlotTelemetrySync` remains the first cross-feature edge of
that shape in this codebase — still `Infrastructure → Application`, not a layering violation.

## Project Structure

### Documentation (this feature)

```text
specs/006-filament-tracking/
├── plan.md              # This file (/speckit-plan command output)
├── research.md          # Phase 0 output
├── data-model.md        # Phase 1 output
├── quickstart.md         # Phase 1 output
├── contracts/            # Phase 1 output
│   └── filament-tracking-api.md
└── tasks.md              # Phase 2 output (/speckit-tasks command - NOT created by /speckit-plan)
```

### Source Code (repository root)

```text
api/src/OpenFarm.Domain/FilamentTracking/
  Vendor.cs                           # entity
  Filament.cs                         # entity — VendorId FK, optional bare-Guid? FilamentProfileId
  FilamentVariant.cs                  # entity — child of Filament
  FeedUnit.cs                         # entity — PrinterId (bare FK), Kind (string), UnitIndex, Name?,
                                        # ReplacesFeedUnitId? (self-FK, AFR -> ExternalSpool only, research.md §9),
                                        # IsRevealed (bool, ExternalSpool only, research.md §12)
  FeedUnitKindCatalog.cs               # static Resolve(kind) -> ReloadPolicy; brand-terminology lookup for
                                        # MultiMaterialUnit — mirrors PrinterModelCatalog (research.md §2)
  ReloadPolicy.cs                      # enum: None, MatchRequired, Unconditional (research.md §4)
  FeedSlot.cs                         # entity — FeedUnitId, SlotPosition, DeactivatedAtUtc — no Kind, no
                                       # AFR flag, no standby field (research.md §3)
  Spool.cs                            # entity — LoadOntoFeedSlot/ReturnToStorage/MarkFinished/RecordUsage/CorrectRemainingWeight
  SpoolState.cs                       # enum: InStorage, Using, Finished
  SpoolStateTransitions.cs            # mirrors PrinterStatusTransitions
  SpoolUsageRecord.cs                 # append-only ledger entity
  VendorNotFoundException.cs / FilamentNotFoundException.cs / FilamentVariantNotFoundException.cs
  DuplicateVendorNameException.cs / DuplicateAutomaticFilamentReloaderNameException.cs
  InvalidSpoolStateTransitionException.cs / SpoolConcurrencyConflictException.cs
  FeedSlotOccupiedException.cs / FeedSlotDeactivatedException.cs / FeedSlotNotFoundException.cs
  FeedUnitNotFoundException.cs / NotAnAutomaticFilamentReloaderException.cs

api/src/OpenFarm.Domain/Printers/
  AmsTray.cs                          # unchanged — reused as Printer.ExternalSpoolTray's type
  Printer.cs                          # + ExternalSpoolTray property; RecordTelemetry gains one parameter

api/src/OpenFarm.Application/FilamentTracking/
  IVendorRepository.cs / IFilamentRepository.cs / IFeedUnitRepository.cs / ISpoolRepository.cs
  IFilamentTrackingEventPublisher.cs   # Application-defined contract; Api implements
  IFeedSlotTelemetrySync.cs            # SyncAsync(printerId, organizationId, brand, amsUnits, externalSpoolTray, ct)
  CreateVendorCommand.cs / Handler / Validator
  GetVendorsQuery.cs / Handler
  CreateFilamentCommand.cs / Handler / Validator
  AddFilamentVariantCommand.cs / Handler / Validator
  GetFilamentsQuery.cs / Handler
  GetFeedUnitsQuery.cs / Handler                        # per-printer; nested feedSlots; derived displayLabel,
                                                          # currentSpool, detected fields (any kind with
                                                          # telemetry); hide-by-default filter + safety net
                                                          # for a superseded ExternalSpool (research.md §12)
  AddFeedUnitCommand.cs / Handler / Validator           # Kind-discriminated: AutomaticFilamentReloader
                                                          # (name + slot count + optional replacesFeedUnitId,
                                                          # creates new rows) | ExternalSpool (no fields,
                                                          # finds the printer's own row, sets IsRevealed=true;
                                                          # 404 if none exists — FR-026) — research.md §13
  RemoveFeedUnitCommand.cs / Handler / Validator        # dispatches on the target row's own Kind:
                                                          # AutomaticFilamentReloader -> deactivate;
                                                          # ExternalSpool -> IsRevealed=false; 422 for
                                                          # MultiMaterialUnit (connector-derived, FR-005a)
  RegisterSpoolCommand.cs / Handler / Validator
  GetSpoolsQuery.cs / Handler
  LoadSpoolOntoFeedSlotCommand.cs / Handler             # pre-check ahead of the partial unique index; RFID
                                                          # comparison, MultiMaterialUnit slots only (FR-021)
  UnloadSpoolCommand.cs / Handler                       # ReturnToStorage | MarkFinished
  RecordSpoolUsageCommand.cs / Handler                  # decrements RemainingWeightGrams; triggers continuation
  CorrectRemainingWeightCommand.cs / Handler / Validator
  SpoolUsageRecordedHandler.cs          # resolves ReloadPolicy via FeedUnitKindCatalog; MatchRequired /
                                         # Unconditional / None branches — data-model.md's algorithm
  FeedSlotTelemetrySyncHandler.cs        # implements IFeedSlotTelemetrySync; diffs + upserts/deactivates
                                          # MultiMaterialUnit/ExternalSpool rows only

api/src/OpenFarm.Infrastructure/FilamentTracking/
  VendorRepository.cs / FilamentRepository.cs / FeedUnitRepository.cs / SpoolRepository.cs
  Persistence/Configurations/VendorConfiguration.cs / FilamentConfiguration.cs /
    FilamentVariantConfiguration.cs / FeedUnitConfiguration.cs / FeedSlotConfiguration.cs /
    SpoolConfiguration.cs / SpoolUsageRecordConfiguration.cs
    # FeedUnitConfiguration: Kind as plain string (no HasConversion needed), unique index on
    # (PrinterId, Kind, UnitIndex). FeedSlotConfiguration: unique index on (FeedUnitId, SlotPosition).
    # SpoolConfiguration: HasConversion<string>() on State, partial unique index on (FeedSlotId) WHERE
    # State = 'Using', UseXminAsConcurrencyToken().
  Persistence/Migrations/<timestamp>_AddFilamentTracking.cs         # seven new tables
  Persistence/Migrations/<timestamp>_AddExternalSpoolTelemetry.cs   # one new Printers column

api/src/OpenFarm.Infrastructure/Printers/Bambu/
  BambuMqttMessageParser.cs           # + ParseExternalSpool(print) reading print.vt_tray, mirrors ParseTray
                                       # — existing file, test-after/fixture-replay unchanged
api/src/OpenFarm.Infrastructure/Printers/
  PrinterConnectionBackgroundService.cs   # HandleEventAsync's existing telemetry-flush block gains one call
                                            # to IFeedSlotTelemetrySync — existing file
api/src/OpenFarm.Infrastructure/Printers/Simulator/
  SimulatedPrinterState.cs            # + a static ExternalSpoolTray fixture — existing file

api/src/OpenFarm.Api/
  Endpoints/Vendors/CreateVendorEndpoint.cs / ListVendorsEndpoint.cs
  Endpoints/Filaments/CreateFilamentEndpoint.cs / AddFilamentVariantEndpoint.cs / ListFilamentsEndpoint.cs
  Endpoints/FeedUnits/ListFeedUnitsEndpoint.cs / AddFeedUnitEndpoint.cs / RemoveFeedUnitEndpoint.cs
  Endpoints/Spools/RegisterSpoolEndpoint.cs / ListSpoolsEndpoint.cs / LoadSpoolEndpoint.cs /
    UnloadSpoolEndpoint.cs / CorrectRemainingWeightEndpoint.cs / RecordSpoolUsageEndpoint.cs
  Hubs/FarmHubFilamentTrackingEventPublisher.cs   # implements IFilamentTrackingEventPublisher
  ExceptionHandling/DomainExceptionHandler.cs      # extended with this feature's exception → status mappings

web/src/features/filament/                # replaces the mock-data prototype; gains an embedded Vendor tab
  FilamentPage.tsx / FilamentFormPage.tsx   # existing files, rewired to real API
  components/VariantsCard.tsx / VariantDialog.tsx / FilamentCatalogTable.tsx
  components/VendorTable.tsx / CreateVendorDialog.tsx
  data/ (Orval-generated hooks; mockFilament.ts / mockFilamentStore.ts deleted)

web/src/features/spools/                  # replaces the mock-data prototype
  SpoolsPage.tsx / components/SpoolsTable.tsx
  components/AddSpoolDialog.tsx             # Filament/variant Select sourced from GET /filaments
  components/LoadSpoolDialog.tsx            # Feed Slot Select sourced from GET /printers/{id}/feed-units
                                             # (flattened from the nested feedUnits[].feedSlots[] shape);
                                             # shows rfidMismatchWarning after load
  components/UnloadSpoolDialog.tsx / RecordUsageDialog.tsx
  data/ (Orval-generated hooks; mockSpools.ts / mockSpoolsStore.ts deleted)

web/src/features/printers/                # existing feature, extended (no new nav item)
  components/FeedUnitsCard.tsx              # printer-detail page card: grouped Feed Unit -> Feed Slot list
                                             # (displayLabel, detected material, deactivated state, currentSpool);
                                             # a single "Add a Feed Unit" action (kind picker: Automatic
                                             # Filament Reloader with name/slot count, or External Spool with
                                             # no extra fields) and "Remove" per added/revealed unit
                                             # (research.md §13); subscribes to FeedUnitsChanged
```

**Structure Decision**: Existing four-project Clean Architecture backend + single-SPA frontend, extended in
place — no new top-level project. `web/src/features/filament/` and `web/src/features/spools/` replace their
existing mock-data prototypes. Vendor management is a tab embedded in `FilamentPage.tsx`, not a new
top-level feature. Feed Unit topology and the manual Automatic Filament Reloader actions live on the
existing printer-detail page, not a new nav item.

## Complexity Tracking

> **Fill ONLY if Constitution Check has violations that must be justified**

No Constitution Check violations. The design decisions below are recorded for visibility, not because any
is a deviation:

| Decision | Why Needed | Simpler Alternative Rejected Because |
|-----------|------------|-------------------------------------|
| `FeedUnit`/`FeedSlot` two-level split, replacing a flat `FeedSlot` entity | A physical accessory (Multi-Material Unit, External Spool, Automatic Filament Reloader) and a loading position within it are genuinely different things — a flat entity either duplicated `Kind` per sibling slot or couldn't cleanly represent "a Reload Unit" as its own addressable concept | A single flat `FeedSlot` carrying `Kind`/`UnitIndex`/`SlotPosition` directly (this feature's second design pass) was simpler on paper but redundantly repeated the same `Kind` value across every sibling row and had no natural home for unit-level concerns (e.g. an operator-given `Name` for a manually-declared unit) |
| `FeedUnit.Kind` is an open `string`, resolved via `FeedUnitKindCatalog`, not a Postgres enum | A new material-reload hardware type should be supportable without a schema migration | A Postgres enum (matching `Spool.State`'s pattern) was considered — rejected specifically for this one field, since its value set is explicitly meant to grow, unlike every other enum in this feature whose value set is genuinely fixed by domain logic |
| Auto-continuation replaced entirely by a kind-driven `ReloadPolicy` — no per-slot flag, no `StandbySpoolId` | A Multi-Material Unit (material-aware, can check what's in its own trays) and an Automatic Filament Reloader (blind mechanical splicer, can't) behave differently enough that one uniform "standby assignment" toggle either over- or under-constrained one of them | An explicit per-slot flag + standby-spool assignment (this feature's second design pass) was reconsidered because it required an operator to explicitly designate a backup even when the correct backup is simply "whichever matching spool is already loaded" — redundant configuration for a fact already true, and it couldn't express the real difference between match-gated and unconditional continuation without an extra field on top |
| `AutomaticFilamentReloader` is the one `FeedUnit` `Kind` manually created/removed by the operator; the other two stay strictly connector-derived | Real third-party splicer hardware is invisible to a printer's own telemetry — there is no signal to sync from | Extending the connector-derived model to cover this kind too was considered and is simply not possible — no printer firmware reports third-party splicer hardware; the manual path is not a design preference but the only available one |
| Detected-material load-time validation (FR-021) gated purely on live per-slot data presence, with **no** `Kind` restriction at all | External research (Bambu H2D/H2C, Snapmaker U1) confirmed a real product (Snapmaker U1) reads detection data on individually-addressable, single-slot inputs structurally identical to this feature's `ExternalSpool` kind — disproving an earlier categorical exclusion | Two prior framings were tried and superseded in sequence: first, "scoped to `MultiMaterialUnit` slots" (read as if every Multi-Material Unit supports detection); then "categorically excludes `ExternalSpool`, per-instance within `MultiMaterialUnit`" (an improvement, but still asserted a `Kind`-level rule that real hardware then disproved). The final rule — gate purely on live data presence, no `Kind` involved at all — is strictly simpler than both, not an added special case |
| Multi-Material Unit ↔ External Spool relationship (FR-023) is auto-derived at read time, not a stored field like the Automatic Filament Reloader's `ReplacesFeedUnitId` | A Multi-Material Unit's presence is directly observable from live telemetry (`Printer.AmsUnits`), unlike an Automatic Filament Reloader which has no telemetry signal at all | A symmetric stored field (mirroring `ReplacesFeedUnitId`) was considered for consistency — rejected because it would duplicate a fact already fully derivable from existing, always-current data, the same "don't cache what you can derive" reasoning already applied to Feed Unit display labels and Profiles' descriptive fields |
| Multi-toolhead printers (Bambu H2D/H2C) get no new toolhead/nozzle field on `FeedUnit` this phase | Confirmed via research that `FeedUnit.UnitIndex` already generalizes to however many physical units a printer reports, regardless of how many nozzles route to them | Adding a toolhead/nozzle identifier now, anticipating H2D-class hardware, was considered and rejected — no FR in this phase (pure inventory tracking, no `Job` entity) consumes it; `Printer` has no toolhead concept anywhere today, so this would be schema surface with no current requirement behind it |
| `FeedUnit.ReplacesFeedUnitId` — an optional relationship from an `AutomaticFilamentReloader` to the `ExternalSpool` unit it may physically supersede | Some AFR installations wire in via a bypassable Y-connector (both units genuinely independent); others fully replace the port via a multi-way splitter with no bypass, where showing both as unrelated accessories is actively confusing | A structured, enforced relationship (e.g. blocking a direct load onto a "superseded" slot) was considered and rejected — this design can't verify actual physical wiring, only what the operator declares, so hard-blocking risks being wrong the moment someone bypasses their splicer without updating the declaration; loading a spool directly onto a superseded position always still succeeds. Generalizing to a broader Feed-Unit-to-Feed-Unit wiring graph (e.g. covering a `MultiMaterialUnit` target too) was also considered and rejected as speculative — no realistic wiring pattern needs it |
| A superseded External Spool position is omitted from `GET /feed-units` entirely by default, not just flagged — with `IsRevealed` as an operator escape hatch and a loaded-spool safety net | A printer normally has one filament input; once superseded, the raw port isn't a separate, independently-usable thing from the operator's point of view, so showing it (even flagged) misrepresents the hardware, not just the UI | The original design (advisory-only, always shown) was reconsidered on reflection, not because it was wrong, but because it undersold the physical reality. Hiding it outright, with no escape hatch, was also considered and rejected — it would make an actively-loaded spool (bypass connector) invisible |
| Declaring/removing an Automatic Filament Reloader and revealing/re-hiding a superseded External Spool are unified into one `AddFeedUnitCommand`/`RemoveFeedUnitCommand` pair, `Kind`-discriminated, rather than three separate commands/endpoints | Both are the same underlying fact from the operator's perspective — hardware/wiring the printer's connection can't fully account for on its own — and unifying them also resolves the hide-by-default design's discoverability gap (revealing no longer needs the client to already know the hidden unit's id) more cleanly than a dedicated query parameter would | Three separate commands (`CreateAutomaticFilamentReloaderCommand`, `DeactivateAutomaticFilamentReloaderCommand`, a standalone `RevealFeedUnitCommand`) plus a `GET .../feed-units?includeHidden=true` parameter (this design's first attempt at solving discoverability) were considered and rejected once the unification made the query parameter unnecessary — keeping it around after that would have been unused surface with no purpose left to serve |
| Partial unique index (`Spools.FeedSlotId WHERE State = 'Using'`) in addition to an `Application`-layer pre-check | Makes "at most one spool per feed slot" provably true under concurrency | An `Application`-layer check alone was considered sufficient — rejected because the same edge case motivating `xmin` concurrency (two operators racing) applies equally here |
| `Spool` uses `xmin` as an EF Core concurrency token; no other entity in this feature does | spec.md's own edge case explicitly names concurrent spool-state edits | An application-managed version column was considered — rejected in favor of Postgres's built-in `xmin` |
| `RemainingWeightGrams` is a stored, incrementally-updated column, not a live `SUM()` over `SpoolUsageRecords` | Every spool read must stay O(1) regardless of ledger size (SC-002) | Live-aggregating was considered — rejected per Principle V: no FR asks to query/filter by historical usage |
