# Data Model: Build Plate Selection

**Feature**: `012-build-plate-selection` | **Date**: 2026-07-30

## Entities

### `BuildPlateType` (new enum, `OpenFarm.Domain.Printers`)

Fixed, non-extensible-by-users enumeration — not a database-backed catalog (FR-004, FR-010).

```csharp
public enum BuildPlateType
{
    Cool,
    Engineering,
    HighTemp,
    TexturedPei,
    TexturedCool,
    Supertack,
}
```

| Value | OrcaSlicer `curr_bed_type` string | Filament-preset temp key |
|---|---|---|
| `Cool` | `Cool Plate` | `cool_plate_temp` |
| `Engineering` | `Engineering Plate` | `eng_plate_temp` |
| `HighTemp` | `High Temp Plate` | `hot_plate_temp` |
| `TexturedPei` | `Textured PEI Plate` | `textured_plate_temp` |
| `TexturedCool` | `Textured Cool Plate` | `textured_cool_plate_temp` |
| `Supertack` | `Supertack Plate` | `supertack_plate_temp` |

Confirmed vendor-agnostic (`research.md` R3/R9): every OrcaSlicer-bundled vendor inspected (Bambu,
Prusa, Creality, Qidi, Anycubic, Elegoo, Artillery, FLSun, Snapmaker, Flashforge) declares the same six
key names in its own `fdm_filament_common.json` — this is an OrcaSlicer-engine schema convention, not
something each vendor defines independently, so the mapping table above never needs a per-vendor
variant. No currently-catalogued Bambu model (`X1`/`X1C`/`X1E`/`P1P`/`P1S`/`A1`/`A1MINI`) actually uses
`TexturedCool` — it's included for schema completeness and because it's the factory-default plate on
other real OrcaSlicer-supported machines (e.g. Snapmaker U1, Flashforge AD5X) that could be catalogued
in the future without another enum change.

The C#-name ↔ vendor-string mapping lives in one place (a small static lookup alongside the enum,
e.g. `BuildPlateTypeVendorNames`) so the compatibility gate (validates against `PrinterModelCatalog`),
the slicing resolver (writes `curr_bed_type`, reads the temp key), **and** `GetPrinterDetailQuery`
(resolves the friendly label served to the frontend, FR-012) share a single source of truth — no
duplicated string literals, and critically, no second copy of this mapping in `web/`. The same vendor
string doubles as the operator-facing label (e.g. `TexturedPei` → `"Textured PEI Plate"`) — there is no
separate "display name" table to keep in sync with it.

### `Printer` (existing entity, extended — `Domain/Printers/Printer.cs`)

New property, parallel to `MachineProfileId`/`ProcessProfileId`:

```csharp
public BuildPlateType? BuildPlateType { get; private set; }
```

New domain methods, mirroring `LinkMachineProfile`/`UnlinkMachineProfile`:

```csharp
public void SelectBuildPlate(BuildPlateType buildPlateType) => BuildPlateType = buildPlateType;

public void ClearBuildPlate() => BuildPlateType = null;
```

- **Default**: `null` for every printer, including ones that existed before this feature ships (spec
  Edge Cases — no backfill, matches how `MachineProfileId`/`ProcessProfileId` started `null` for
  pre-009 printers).
- **Cardinality**: per-printer, not per-organization, not per-model (FR-003) — two printers of the
  same model may carry different selections; enforced simply by this being an instance property, not
  a shared/static value.
- **No state machine**: unlike `Status` (`PrinterStatusTransitions`), there is no invalid-transition
  concept here — any compatible value can replace any other compatible value freely, and `null` is
  always a valid target (clearing). No `EnsureValidTransition`-style guard is needed.
- **No re-validation trigger from machine profile changes** (FR-008): `LinkMachineProfile`/
  `UnlinkMachineProfile` are untouched by this feature and never read/write `BuildPlateType` — the two
  fields are fully independent on the entity.

### `PrinterModel` (existing record, extended — `Domain/Printers/PrinterModel.cs`)

New field:

```csharp
public sealed record PrinterModel(
    PrinterBrand Brand,
    string ModelCode,
    CameraTransport CameraTransport,
    int FilamentInletCount = 1,
    string? VendorMachineModelName = null,
    IReadOnlySet<BuildPlateType>? CompatiblePlateTypes = null);
```

Every entry in `PrinterModelCatalog.Entries` supplies a concrete, non-null set (nullable only to keep
the constructor signature backward-source-compatible during the edit; every actual catalog row is
populated). Two entries are vendor-confirmed already (`research.md` R2):

```csharp
[(PrinterBrand.Bambu, "A1")] = new PrinterModel(PrinterBrand.Bambu, "A1", CameraTransport.BinaryImage,
    VendorMachineModelName: "Bambu Lab A1",
    CompatiblePlateTypes: new HashSet<BuildPlateType> { BuildPlateType.TexturedPei, BuildPlateType.HighTemp }),

[(PrinterBrand.Bambu, "A1MINI")] = new PrinterModel(PrinterBrand.Bambu, "A1MINI", CameraTransport.BinaryImage,
    VendorMachineModelName: "Bambu Lab A1 mini",
    // Cross-checked both signals (research.md R2): machine preset's own
    // not_support_bed_type = "Smooth Cool Plate;Engineering Plate" (authoritative) agrees with the
    // filament-temp fallback (eng_plate_temp resolves to "0" through inheritance).
    CompatiblePlateTypes: new HashSet<BuildPlateType> { BuildPlateType.TexturedPei, BuildPlateType.HighTemp }),

[(PrinterBrand.Bambu, "X1C")] = new PrinterModel(PrinterBrand.Bambu, "X1C", CameraTransport.Rtsp,
    VendorMachineModelName: "Bambu Lab X1 Carbon",
    CompatiblePlateTypes: new HashSet<BuildPlateType> { /* at least TexturedPei (default_bed_type) — remaining set: tasks.md data task */ }),
```

`X1`, `X1E`, `P1P`, `P1S` need the same one-time vendor-bundle inspection (`research.md` R2) as a
`tasks.md` data-population item — not resolved further at plan time. Per the spec's Clarifications
(session 2026-07-30), that inspection reads two signals in priority order per model: the model's own
machine preset's `not_support_bed_type` field when declared (authoritative — subtract those types from
the full six), falling back to which of the six `*_plate_temp` keys that model's own filament presets
leave non-`"0"` when `not_support_bed_type` isn't declared at all (confirmed necessary — `X1C` has no
`not_support_bed_type` field in its real machine preset). `System`/`Simulation` gets every
`BuildPlateType` value (no real hardware constraint).

**Validation rule**: `EditPrinterCommandHandler` rejects a requested `BuildPlateType` not present in
`PrinterModelCatalog.Resolve(printer.Brand, printer.ModelCode).CompatiblePlateTypes` with
`IncompatibleBuildPlateException` — never validated against the linked `MachineProfile` (FR-002,
clarification Q4).

### `IncompatibleBuildPlateException` (new, `Domain/Printers/`)

```csharp
public sealed class IncompatibleBuildPlateException(BuildPlateType requested, string brand, string modelCode)
    : Exception($"'{requested}' is not a compatible build plate for {brand} {modelCode}.")
{
    public BuildPlateType Requested { get; } = requested;
}
```

Mapped in `DomainExceptionHandler` to `422 Unprocessable Entity`, title "Build plate incompatible with
printer model" — same shape as every other typed exception in that switch.

### `FilamentProfile` (existing entity, no schema change — read path extended only)

No new column. `RawDataJson` already carries all five `*_plate_temp` keys (confirmed via vendor
fixtures, `research.md` R3) for every ingested/imported filament preset. This feature is the first
consumer to read them structurally — via the plate-type → key mapping table above, inside
`ResolveSlicingProfileSelection`, not via a new entity property.

## Persistence

### New migration: `AddPrinterBuildPlate`

Structurally mirrors `20260719150100_AddPrinterMachineProfileLink.cs`, but simpler — a plain nullable
`text` column, no FK, no index (there's no lookup-table row to join against, and this column is never
filtered/joined on in a hot path):

```csharp
protected override void Up(MigrationBuilder migrationBuilder)
{
    migrationBuilder.AddColumn<string>(
        name: "BuildPlateType",
        table: "Printers",
        type: "text",
        nullable: true);
}

protected override void Down(MigrationBuilder migrationBuilder)
{
    migrationBuilder.DropColumn(
        name: "BuildPlateType",
        table: "Printers");
}
```

### `PrinterConfiguration` (EF config, extended)

```csharp
builder.Property(p => p.BuildPlateType)
    .HasConversion<string>();
```

Same line shape as `builder.Property(p => p.Status).IsRequired().HasConversion<string>();` minus
`.IsRequired()` (nullable, unlike `Status`).

## Application-layer shapes

### `EditPrinterCommand` (extended)

```csharp
public sealed record EditPrinterCommand(
    Guid PrinterId, string Name, string? IpAddress, string? AccessCode,
    string? Brand, string? ModelCode, string? SerialNumber,
    Guid? MachineProfileId, Guid? ProcessProfileId,
    BuildPlateType? BuildPlateType,
    Guid? ActorId, ActorType ActorType);
```

`BuildPlateType` follows the exact "applied as sent, `null` clears" contract `MachineProfileId`/
`ProcessProfileId` already document — the caller submits the printer's full current state on every
save.

### `GetPrinterDetailQuery` → `PrinterDetail` (extended)

```csharp
public sealed record PrinterDetail(
    Guid Id, string Name, string Brand, string ModelCode, string IpAddress, string SerialNumber,
    PrinterStatus Status, bool IsReachable,
    decimal? NozzleTempC, decimal? BedTempC, int? PrintProgressPercent, string? CurrentStageLabel,
    int? RemainingTimeMinutes, string? CurrentJobName, IReadOnlyList<AmsUnit> AmsUnits,
    Guid? MachineProfileId, string? MachineProfileName,
    Guid? ProcessProfileId, string? ProcessProfileName,
    BuildPlateType? BuildPlateType, string? BuildPlateTypeLabel,
    IReadOnlyList<BuildPlateTypeOption> CompatibleBuildPlateTypes);

public sealed record BuildPlateTypeOption(BuildPlateType Value, string Label);
```

`CompatibleBuildPlateTypes` is always populated (non-empty list) — a pure, non-throwing
`PrinterModelCatalog.Resolve(...)` lookup, since every registered printer's Brand/ModelCode already
passed catalog resolution at registration and can never change afterward. Each entry's `Label` and the
top-level `BuildPlateTypeLabel` (`null` iff `BuildPlateType` is `null`) are both resolved from the same
`BuildPlateTypeVendorNames` lookup the slicing resolver already uses (FR-012) — `GetPrinterDetailQueryHandler`
is the only place that reads it for display purposes; nothing in `web/` re-derives a label from the
enum code.

## API response/request shapes (backend DTOs, mirrored by orval on the frontend)

See `contracts/printers-api.md` for the full endpoint-level contract.

## State / validity summary

| State | Meaning | Slice behavior |
|---|---|---|
| `Printer.BuildPlateType == null` | No selection made yet (default for every printer, old and new) | Printer-tied slice refused: `SlicingProfileUnconfiguredException` naming the printer (FR-006) |
| `Printer.BuildPlateType == X`, filament preset has a real (non-`"0"`) value for `X`'s key | Normal case | `curr_bed_type` injected as `X`'s vendor string; slice proceeds |
| `Printer.BuildPlateType == X`, filament preset's key for `X` is missing or `"0"` | Filament preset doesn't support this plate for this material | Slice refused: `SlicingProfileUnconfiguredException` naming the filament preset and plate type (FR-006) |
| Estimate-only slice (`printerId == null`), no fleet printer has machine+process+build-plate all set | No usable representative | Slice refused: existing "no printers configured" message, extended to require build plate too (FR-007) |
