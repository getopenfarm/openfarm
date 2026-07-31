namespace OpenFarm.Domain.Printers;

public enum PrinterBrand
{
    Bambu,
    System,
}

public enum CameraTransport
{
    None,
    Rtsp,
    BinaryImage,
}

/// <summary>
/// <paramref name="VendorMachineModelName"/> is the exact <c>machine_model_list[].name</c> string the
/// bundled slicer's own vendor index uses for this model (e.g. "Bambu Lab X1 Carbon") — hand-authored
/// per entry (research.md §0), <c>null</c> only for the non-vendor <c>System/Simulation</c> entry.
/// System Profile ingestion scoping (FR-001) cross-references this against each vendor's own
/// <c>machine_model_list</c>, never a hardcoded vendor name.
/// <paramref name="CompatiblePlateTypes"/> (012) is this exact registered model's own real compatible
/// build-plate set — hand-authored per entry from the model's own vendor machine/filament preset data
/// (research.md R2), gating <c>Printer.SelectBuildPlate</c> independent of whichever machine profile
/// happens to be linked. Nullable only to keep the constructor signature source-compatible; every real
/// catalog entry supplies a concrete, non-null set.
/// </summary>
public sealed record PrinterModel(
    PrinterBrand Brand,
    string ModelCode,
    CameraTransport CameraTransport,
    int FilamentInletCount = 1,
    string? VendorMachineModelName = null,
    IReadOnlySet<BuildPlateType>? CompatiblePlateTypes = null);
