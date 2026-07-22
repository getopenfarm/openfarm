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
/// </summary>
public sealed record PrinterModel(
    PrinterBrand Brand,
    string ModelCode,
    CameraTransport CameraTransport,
    int FilamentInletCount = 1,
    string? VendorMachineModelName = null);
