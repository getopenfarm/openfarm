namespace OpenFarm.Domain.Printers;

/// <summary>
/// Fixed, non-extensible-by-users enumeration of physical build plates (FR-004/FR-010) — not a
/// database-backed catalog like <see cref="PrinterModel"/>'s ingested profile kinds. A printer's own
/// installed plate is configured directly on <see cref="Printer.BuildPlateType"/>, gated by
/// <see cref="PrinterModel.CompatiblePlateTypes"/> (data-model.md).
/// </summary>
public enum BuildPlateType
{
    Cool,
    Engineering,
    HighTemp,
    TexturedPei,
    TexturedCool,
    Supertack,
}

/// <summary>
/// The exact OrcaSlicer/Bambu Studio <c>curr_bed_type</c> display string for each
/// <see cref="BuildPlateType"/> — doubles as the operator-facing label served by
/// <c>GetPrinterDetailQuery</c> (FR-012, data-model.md: "there is no separate 'display name' table
/// to keep in sync with it"). Single source of truth shared by the compatibility gate
/// (<c>EditPrinterCommandHandler</c>), the slicing resolver (<c>ResolveSlicingProfileSelection</c>),
/// and the read side.
/// </summary>
public static class BuildPlateTypeVendorNames
{
    private static readonly IReadOnlyDictionary<BuildPlateType, string> Names = new Dictionary<BuildPlateType, string>
    {
        [BuildPlateType.Cool] = "Cool Plate",
        [BuildPlateType.Engineering] = "Engineering Plate",
        [BuildPlateType.HighTemp] = "High Temp Plate",
        [BuildPlateType.TexturedPei] = "Textured PEI Plate",
        [BuildPlateType.TexturedCool] = "Textured Cool Plate",
        [BuildPlateType.Supertack] = "Supertack Plate",
    };

    public static string Resolve(BuildPlateType buildPlateType) => Names[buildPlateType];
}

/// <summary>
/// The filament preset JSON key (research.md R3) each <see cref="BuildPlateType"/> resolves its
/// bed temperature from — read by <c>ResolveSlicingProfileSelection</c> to pre-flight-validate a
/// filament preset actually declares a real (non-<c>"0"</c>) temperature for the printer's selected
/// plate before injecting <see cref="BuildPlateTypeVendorNames"/>'s <c>curr_bed_type</c> string.
/// Confirmed vendor-agnostic across ten real OrcaSlicer-bundled vendor profile sets — never needs a
/// per-vendor variant.
/// </summary>
public static class BuildPlateTypeTempKeys
{
    private static readonly IReadOnlyDictionary<BuildPlateType, string> Keys = new Dictionary<BuildPlateType, string>
    {
        [BuildPlateType.Cool] = "cool_plate_temp",
        [BuildPlateType.Engineering] = "eng_plate_temp",
        [BuildPlateType.HighTemp] = "hot_plate_temp",
        [BuildPlateType.TexturedPei] = "textured_plate_temp",
        [BuildPlateType.TexturedCool] = "textured_cool_plate_temp",
        [BuildPlateType.Supertack] = "supertack_plate_temp",
    };

    public static string Resolve(BuildPlateType buildPlateType) => Keys[buildPlateType];
}
