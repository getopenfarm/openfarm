# Phase 1 Data Model: Fleet- and Inventory-Aware Filament Matching

No new tables, no migration. Everything is a code-level change (a config value, a resolver's internal
logic and return type) over data that already exists. Per the decision to exclude match-source visibility
from this iteration (see `research.md` R6), nothing here is persisted to `Part`/`PartProcessingJob` or
exposed on any read model — the matching capability's decision is computed and consumed entirely within a
single request, then discarded.

## Changed configuration

### `SlicerOptions` (`api/src/OpenFarm.Infrastructure/Parts/SlicerOptions.cs`)

| Field (today) | Field (this feature) |
|---|---|
| `Dictionary<SlicerEngine, string> GenericFallbackMaterialProfile` (per-engine literal profile names) | `string FallbackMaterialType = "PLA"` (single, engine-independent material type) |

`ISlicerDefaultsProvider.GetGenericFallbackMaterialProfile(SlicerEngine engine)` →
`ISlicerDefaultsProvider.GetFallbackMaterialType()` (no engine parameter — the whole point of R1 is that the
value no longer varies by engine). `SlicerDefaultsProvider` throws nothing anymore (today's
`InvalidOperationException` for a missing dictionary entry can't happen once it's a single required value
with a default).

## New enum (Application-layer, internal)

### `FilamentMatchSource`

`OpenFarm.Application.Profiles` — not a `Domain` type, since nothing persists it this iteration; kept as a
plain internal classification the resolver uses to implement its own tiering and to make a future
audit-trail/debugging feature (deferred, per spec Assumptions) a additive change rather than a redesign:

| Value | Meaning |
|---|---|
| `FleetLoaded` | Matched a spool currently `Using` a `FeedSlot` on a fleet printer (steps 1–2 of `research.md` R2). |
| `FilamentLibrary` | Matched a spool `InStorage` (step 3). |
| `SystemCatalog` | Fell back to a generic System filament profile with no owned spool behind it (step 4 — today's only behavior). |

## New Application-layer type (not persisted, request-scoped only)

### `FilamentMatchResult`

`OpenFarm.Application.Profiles`, returned by the matching capability, consumed only by
`ResolveSlicingProfileSelection` within the same call — never stored, never serialized to a response:

```csharp
public sealed record FilamentMatchResult(FilamentProfile Profile, FilamentMatchSource Source);
```

`ResolveSlicingProfileSelection` uses `.Profile` for the JSON merge exactly as it uses today's bare
`FilamentProfile` return value; `.Source` is currently unused by any caller (available for a future
audit-trail/debugging feature to consume without changing the resolver's shape again).

## Changed Application-layer contract

### `RepresentativeFilamentResolver` → matching capability (same file/namespace, replaced in place)

`api/src/OpenFarm.Application/Profiles/RepresentativeFilamentResolver.cs`

```csharp
// Before
Task<FilamentProfile> ResolveAsync(string materialType, CancellationToken cancellationToken);

// After
Task<FilamentMatchResult> ResolveAsync(Guid? printerId, string materialType, CancellationToken cancellationToken);
```

New constructor dependencies (all already-existing repository interfaces, no new infrastructure):
`ISpoolRepository`, `IFilamentRepository`, `IFeedUnitRepository`, alongside the existing
`IFilamentProfileRepository`.

### `ResolveSlicingProfileSelection.ResolveAsync` (`api/src/OpenFarm.Application/Parts/ResolveSlicingProfileSelection.cs:48`)

Single call-site change: `representativeFilamentResolver.ResolveAsync(materialType, cancellationToken)` →
`representativeFilamentResolver.ResolveAsync(printerId, materialType, cancellationToken).Profile` (the
`.Profile` unwrap happens immediately — nothing about `ResolvedSlicingProfiles`'s own shape changes).
`narrowedFilamentProfileId is { }` branch is unchanged — this is what already guarantees FR-011 with no new
code.

## Entity relationships this feature reads (no schema change — reused as-is)

```text
Spool (State, FeedSlotId?) ──FilamentVariantId──> FilamentVariant ──FilamentId──> Filament
                                                                                     │
Spool.FeedSlotId ──> FeedSlot ──FeedUnitId──> FeedUnit (PrinterId)                  │
                                                                                     │
                                                          FilamentProfileId (bare) ──┘
                                                                     │
                                                                     ▼
                                                            FilamentProfile (existing)
```

`Filament.FilamentProfileId` (`Guid?`, no FK, `api/src/OpenFarm.Domain/FilamentTracking/Filament.cs:35`) is
the one link this feature actually consumes for the first time — everything above it already exists and is
unchanged.

## Explicitly deferred (not built this iteration)

Persisting `FilamentMatchSource`/a human-readable label onto `PartProcessingJob`/`Part`, exposing it on
`PartDetail`, and rendering it in the frontend — i.e. everything that would let an operator see *why* a
given filament was picked — is deferred to a future feature (an audit trail, or a live "matching
explanation" view, per the spec's Assumptions section). `FilamentMatchResult.Source` existing today, even
unused, is what makes that future addition a small extension rather than a resolver rewrite.
