# Phase 1 Data Model: Fleet- and Inventory-Aware Filament Matching

No new tables. One new migration for two nullable columns on the existing `Parts` table (and their
`PartProcessingJob`-side counterparts, which are not persisted independently — see below). Everything else
is a code-level change (config, a resolver, and DTO fields) over data that already exists.

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

## New enum

### `FilamentMatchSource`

`OpenFarm.Domain.Profiles` (alongside `ProfileOrigin`, which it deliberately parallels in spirit —
"where did this come from," but for a resolved filament pick rather than a profile row):

| Value | Meaning |
|---|---|
| `FleetLoaded` | Matched a spool currently `Using` a `FeedSlot` on a fleet printer (steps 1–2 of `research.md` R2). |
| `FilamentLibrary` | Matched a spool `InStorage` (steps 3). |
| `SystemCatalog` | Fell back to a generic System filament profile with no owned spool behind it (step 4 — today's only behavior). |

## New Application-layer type (not persisted)

### `FilamentMatchResult`

`OpenFarm.Application.Profiles`, returned by the matching capability, consumed by
`ResolveSlicingProfileSelection` and (via the estimate pipeline, see below) surfaced to the operator:

```csharp
public sealed record FilamentMatchResult(
    FilamentProfile Profile,
    FilamentMatchSource Source,
    string SourceLabel); // "Fleet: Printer 3" / "Library: eSun PLA+ - Red" / "System: Bambulab"
```

`SourceLabel` is computed once at match time (printer `Name` for `FleetLoaded`; a `"{Filament.Material} -
{FilamentVariant.ColorName}"`-style label, or the spool's own `Label` if set, for `FilamentLibrary`;
`FilamentProfile.VendorName` for `SystemCatalog`) so nothing downstream needs to re-join back through
`FilamentTracking` just to render a label.

## Changed Application-layer contract

### `RepresentativeFilamentResolver` → matching capability (same file/namespace, replaced in place)

`api/src/OpenFarm.Application/Profiles/RepresentativeFilamentResolver.cs`

```csharp
// Before
Task<FilamentProfile> ResolveAsync(string materialType, CancellationToken cancellationToken);

// After
Task<FilamentMatchResult> ResolveAsync(Guid? printerId, string materialType, CancellationToken cancellationToken);
```

New constructor dependencies (all already-existing repository interfaces, no new infrastructure): `ISpoolRepository`,
`IFilamentRepository`, `IFeedUnitRepository`, alongside the existing `IFilamentProfileRepository`.

### `ResolveSlicingProfileSelection.ResolveAsync` (`api/src/OpenFarm.Application/Parts/ResolveSlicingProfileSelection.cs:48`)

Single call-site change: `representativeFilamentResolver.ResolveAsync(materialType, cancellationToken)` →
`representativeFilamentResolver.ResolveAsync(printerId, materialType, cancellationToken)`, and
`ResolvedSlicingProfiles` (or the caller's local, whichever is simplest at implementation time) carries the
result's `Source`/`SourceLabel` forward so `CliSlicerEngine`'s estimate call site can pass it up to
`PartFileProcessor.ResolveEstimateAsync`'s caller. `narrowedFilamentProfileId is { }` branch is unchanged —
this is what already guarantees FR-011 with no new code.

## Changed persisted entities

### `PartProcessingJob` (`api/src/OpenFarm.Domain/Parts/PartProcessingJob.cs`)

Two new nullable properties, set exactly where `PreviewEstimatedPrintTimeMinutes`/
`PreviewEstimatedFilamentWeightGrams` are already set (`ResolveEstimate`, line 192, and the milestone-1
resolution path around line 172):

| Property | Type | Set by |
|---|---|---|
| `PreviewFilamentMatchSource` | `FilamentMatchSource?` | `ResolveEstimate(...)`, alongside the two existing preview-estimate fields — `null` until an estimate has actually been computed (mirrors the existing fields' own nullability). |
| `PreviewFilamentMatchLabel` | `string?` | Same call. |

Not applicable (stays `null`) for a job whose material resolved from an already-sliced file's own embedded
metadata — that path never calls the matching capability at all (`RequiresSlicing = false`), same as
`EstimatedPrintTimeMinutes`/`Weight` are populated a different way for that case already.

### `Part` (`api/src/OpenFarm.Domain/Parts/Part.cs`)

Two new nullable properties, mirroring `EstimatedPrintTimeMinutes`/`EstimatedFilamentWeightGrams` exactly —
same constructor parameters added to `Create`/`ReplaceFile`, same nullability, same migration:

| Column | Type |
|---|---|
| `EstimatedFilamentMatchSource` | `FilamentMatchSource?` (stored as the enum's Postgres representation already used for `ProfileOrigin`, etc. — text or small int per existing convention, decided at implementation time to match sibling enum columns) |
| `EstimatedFilamentMatchLabel` | `string?` |

`ConfirmPartProcessingJobCommandHandler` (`:92-93,117-118`) passes these through from `job.PreviewFilamentMatchSource`/
`PreviewFilamentMatchLabel` at the exact two call sites that already thread the preview estimate fields onto
`Part.Create`/`ReplaceFile`.

**Migration**: one new migration (e.g. `AddFilamentMatchSourceToParts`) adding the two nullable columns to
the existing `Parts` table — no new table, no backfill needed (existing rows simply have `null`, meaning "no
match-source recorded," which the UI renders as absent rather than a third fabricated value).

## Changed read model

### `PartDetail` (`api/src/OpenFarm.Application/Parts/GetPartQuery.cs`)

Two new fields appended to the existing record, populated straight from `Part.EstimatedFilamentMatchSource`/
`EstimatedFilamentMatchLabel` in `GetPartQueryHandler`:

```csharp
public sealed record PartDetail(
    // ...unchanged existing fields...
    string? EstimatedFilamentMatchSource,   // "FleetLoaded" | "FilamentLibrary" | "SystemCatalog" | null
    string? EstimatedFilamentMatchLabel);   // e.g. "Fleet: Printer 3" | null
```

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
