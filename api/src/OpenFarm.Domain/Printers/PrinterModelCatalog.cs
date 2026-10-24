namespace OpenFarm.Domain.Printers;

/// <summary>
/// Compile-time (Brand, ModelCode) -&gt; PrinterModel lookup. Scoped to Bambu X1/P1/A1-series and the
/// Snapmaker U1; adding a brand/model is a new entry here, never a rewrite (data-model.md).
/// </summary>
public static class PrinterModelCatalog
{
    // 012 research.md R2: two-signal method per model (not_support_bed_type when the model's own
    // machine preset declares it, else which *_plate_temp keys the model's own filament presets leave
    // genuinely non-"0") applied directly against the real OrcaSlicer BBL vendor bundle. X1/X1C/X1E/
    // P1P/P1S share the same result: none of their own PLA-basic filament presets declare
    // not_support_bed_type, and each one's own preset (or its unoverridden fdm_filament_pla ancestor)
    // leaves cool_plate_temp/hot_plate_temp/textured_plate_temp/supertack_plate_temp non-zero while
    // eng_plate_temp resolves to "0" — textured_cool_plate_temp has no default anywhere in the Bambu
    // vendor chain for any of them (data-model.md: no catalogued Bambu model uses TexturedCool).
    private static readonly IReadOnlySet<BuildPlateType> BambuXAndP1SeriesPlateTypes =
        new HashSet<BuildPlateType> { BuildPlateType.Cool, BuildPlateType.HighTemp, BuildPlateType.TexturedPei, BuildPlateType.Supertack };

    private static readonly IReadOnlyList<string> OneNozzle = ["Nozzle"];

    private static PrinterModel Bambu(string modelCode, CameraTransport camera, string vendorMachineModelName, IReadOnlySet<BuildPlateType> plates) =>
        new(PrinterBrand.Bambu, modelCode, PrinterDriver.BambuLan, camera, OneNozzle, vendorMachineModelName, plates, FeedUnitStyle.Ams);

    private static readonly Dictionary<(PrinterBrand Brand, string ModelCode), PrinterModel> Entries = new PrinterModel[]
    {
        Bambu("X1", CameraTransport.Rtsp, "Bambu Lab X1", BambuXAndP1SeriesPlateTypes),
        Bambu("X1C", CameraTransport.Rtsp, "Bambu Lab X1 Carbon", BambuXAndP1SeriesPlateTypes),
        Bambu("X1E", CameraTransport.Rtsp, "Bambu Lab X1E", BambuXAndP1SeriesPlateTypes),
        Bambu("P1P", CameraTransport.BinaryImage, "Bambu Lab P1P", BambuXAndP1SeriesPlateTypes),
        Bambu("P1S", CameraTransport.BinaryImage, "Bambu Lab P1S", BambuXAndP1SeriesPlateTypes),
        Bambu("A1", CameraTransport.BinaryImage, "Bambu Lab A1",
            new HashSet<BuildPlateType> { BuildPlateType.TexturedPei, BuildPlateType.HighTemp }),
        // Cross-checked both signals (research.md R2): machine preset's own
        // not_support_bed_type = "Smooth Cool Plate;Engineering Plate" (authoritative) agrees
        // with the filament-temp fallback (eng_plate_temp resolves to "0" through inheritance).
        Bambu("A1MINI", CameraTransport.BinaryImage, "Bambu Lab A1 mini",
            new HashSet<BuildPlateType> { BuildPlateType.TexturedPei, BuildPlateType.HighTemp }),
        new(PrinterBrand.System, "Simulation", PrinterDriver.Simulator, CameraTransport.BinaryImage, OneNozzle,
            CompatiblePlateTypes: new HashSet<BuildPlateType> { BuildPlateType.Cool, BuildPlateType.Engineering, BuildPlateType.HighTemp, BuildPlateType.TexturedPei, BuildPlateType.TexturedCool, BuildPlateType.Supertack }),
        // Plates per 012 research.md R9: the vendor's filament presets leave only hot_plate_temp non-zero.
        new(PrinterBrand.Snapmaker, "U1", PrinterDriver.SnapmakerMoonraker, CameraTransport.HttpSnapshot, ["T1", "T2", "T3", "T4"],
            "Snapmaker U1", new HashSet<BuildPlateType> { BuildPlateType.HighTemp }, FeedUnitStyle.Toolheads),
    }.ToDictionary(m => (m.Brand, m.ModelCode));

    // Bambu SSDP DevModel.bambu.com codes -> catalog ModelCode, for LAN discovery prefill.
    // ponytail: C11/C12/N2S/N1 confirmed via ha-bambulab pybambu/utils.py; the X-series codes are
    // from public docs only — verify against real hardware, an unmapped code just means a manual model pick.
    private static readonly Dictionary<string, string> BambuDeviceModelCodes = new(StringComparer.OrdinalIgnoreCase)
    {
        ["BL-P001"] = "X1C",
        ["BL-P002"] = "X1",
        ["C13"] = "X1E",
        ["C11"] = "P1P",
        ["C12"] = "P1S",
        ["N2S"] = "A1",
        ["N1"] = "A1MINI",
    };

    public static string? TryMapBambuDeviceModel(string deviceModel) =>
        BambuDeviceModelCodes.GetValueOrDefault(deviceModel.Trim());

    /// <summary>
    /// The model a sliced file says it was made for — a vendor machine model name ("Bambu Lab P1S", "Snapmaker U1", the
    /// G-code's <c>printer_model</c>) or a Bambu device code ("C12", from <c>slice_info.config</c>). Null when unknown.
    /// </summary>
    public static PrinterModel? TryResolveSlicedModel(string? declaredModel)
    {
        if (string.IsNullOrWhiteSpace(declaredModel)) return null;
        var name = declaredModel.Trim();
        return Entries.Values.FirstOrDefault(m => string.Equals(m.VendorMachineModelName, name, StringComparison.OrdinalIgnoreCase))
            ?? (TryMapBambuDeviceModel(name) is { } code ? Entries[(PrinterBrand.Bambu, code)] : null);
    }

    /// <summary>
    /// Every catalogued model has a driver, so a catalogued brand has live status/dispatch integration. This is the one
    /// place that check extends, never a restructure of the dispatcher/command-handler branches that already read it
    /// (FR-012/FR-024b).
    /// </summary>
    public static bool SupportsLiveIntegration(string brand) =>
        Enum.TryParse<PrinterBrand>(brand, ignoreCase: true, out var parsedBrand) && Entries.Keys.Any(key => key.Brand == parsedBrand);

    /// <summary><see cref="PrinterModel.UsesAccessCode"/>; an unknown model needs one (validation rejects it anyway).</summary>
    public static bool UsesAccessCode(string brand, string modelCode) =>
        !TryResolve(brand, modelCode, out var model) || model!.UsesAccessCode;

    public static PrinterModel Resolve(string brand, string modelCode)
    {
        if (!TryResolve(brand, modelCode, out var model))
        {
            throw new UnknownPrinterModelException(brand, modelCode);
        }

        return model!;
    }

    /// <summary>The model, or null when (brand, modelCode) isn't catalogued.</summary>
    public static PrinterModel? Find(string brand, string modelCode) => TryResolve(brand, modelCode, out var model) ? model : null;

    public static bool TryResolve(string brand, string modelCode, out PrinterModel? model)
    {
        if (Enum.TryParse<PrinterBrand>(brand, ignoreCase: true, out var parsedBrand)
            && Entries.TryGetValue((parsedBrand, modelCode), out var found))
        {
            model = found;
            return true;
        }

        model = null;
        return false;
    }

    /// <summary>
    /// Every registered model — the read path the catalog endpoint projects, guaranteed to never drift from what
    /// <see cref="Resolve"/>/<see cref="TryResolve"/> would accept since both read the same static dictionary.
    /// </summary>
    public static IReadOnlyCollection<PrinterModel> Models => Entries.Values;

    /// <summary>
    /// Every distinct <see cref="PrinterModel.VendorMachineModelName"/> currently registered — System
    /// Profile ingestion scoping (009, FR-001) cross-references a vendor's own
    /// <c>machine_model_list[].name</c> values against this set rather than a hardcoded vendor name,
    /// so a new vendor's rows appear automatically once a catalog entry maps to it.
    /// </summary>
    public static IReadOnlyList<string> AllVendorMachineModelNames =>
        Models
            .Select(m => m.VendorMachineModelName)
            .Where(name => name is not null)
            .Select(name => name!)
            .Distinct(StringComparer.Ordinal)
            .ToList();
}
