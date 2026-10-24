namespace OpenFarm.Domain.Printers;

public enum PrinterBrand
{
    Bambu,
    System,
    Snapmaker,
}

public enum CameraTransport
{
    None,
    Rtsp,
    BinaryImage,

    /// <summary>A JPEG snapshot fetched over HTTP, refreshed by polling (the Snapmaker U1's Moonraker camera).</summary>
    HttpSnapshot,
}

/// <summary>The adapter that talks to a model — the one switch that picks its connector and camera relay.</summary>
public enum PrinterDriver
{
    /// <summary>Bambu LAN mode: MQTT over TLS, FTPS uploads, JPEG-over-TLS camera.</summary>
    BambuLan,

    /// <summary>Snapmaker's Moonraker fork (the U1): JSON-RPC websocket, HTTP uploads, a polled snapshot camera.</summary>
    SnapmakerMoonraker,

    /// <summary>The in-process simulator.</summary>
    Simulator,
}

/// <summary>How a model's connector-derived multi-material unit is named.</summary>
public enum FeedUnitStyle
{
    /// <summary>"Multi-Material Unit n".</summary>
    MultiMaterialUnit,

    /// <summary>Bambu's "AMS n".</summary>
    Ams,

    /// <summary>A toolchanger's heads: one unit whose slot n is the spool on extruder n, named after that extruder.</summary>
    Toolheads,
}

/// <summary>
/// What a printer model is — the single home of hardware facts; how to talk to it stays in its driver's adapter.
/// <paramref name="Extruders"/> names each extruder in index order (one "Nozzle" on a single-nozzle printer, "T1"–"T4" on
/// the U1); a feed slot's extruder index points into it.
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
    PrinterDriver Driver,
    CameraTransport CameraTransport,
    IReadOnlyList<string> Extruders,
    string? VendorMachineModelName = null,
    IReadOnlySet<BuildPlateType>? CompatiblePlateTypes = null,
    FeedUnitStyle FeedUnitStyle = FeedUnitStyle.MultiMaterialUnit)
{
    /// <summary>
    /// Whether registering needs an operator-typed serial and access code. Bambu addresses MQTT by serial and
    /// authenticates with the LAN access code; a Snapmaker reports its own serial and its Moonraker API key is optional.
    /// </summary>
    public bool UsesAccessCode => Driver != PrinterDriver.SnapmakerMoonraker;

    /// <summary>Whether OpenFarm can show this model's camera.</summary>
    // ponytail: no RTSP relay yet (the X1 family), add one when an RTSP model needs a camera
    public bool CameraSupported => CameraTransport is CameraTransport.BinaryImage or CameraTransport.HttpSnapshot;
}
