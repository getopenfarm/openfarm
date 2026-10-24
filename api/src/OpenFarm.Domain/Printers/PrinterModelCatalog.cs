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

    private static readonly Dictionary<(PrinterBrand Brand, string ModelCode), PrinterModel> Entries =
        new Dictionary<(PrinterBrand, string), PrinterModel>
        {
            [(PrinterBrand.Bambu, "X1")] = new PrinterModel(PrinterBrand.Bambu, "X1", CameraTransport.Rtsp, VendorMachineModelName: "Bambu Lab X1", CompatiblePlateTypes: BambuXAndP1SeriesPlateTypes),
            [(PrinterBrand.Bambu, "X1C")] = new PrinterModel(PrinterBrand.Bambu, "X1C", CameraTransport.Rtsp, VendorMachineModelName: "Bambu Lab X1 Carbon", CompatiblePlateTypes: BambuXAndP1SeriesPlateTypes),
            [(PrinterBrand.Bambu, "X1E")] = new PrinterModel(PrinterBrand.Bambu, "X1E", CameraTransport.Rtsp, VendorMachineModelName: "Bambu Lab X1E", CompatiblePlateTypes: BambuXAndP1SeriesPlateTypes),
            [(PrinterBrand.Bambu, "P1P")] = new PrinterModel(PrinterBrand.Bambu, "P1P", CameraTransport.BinaryImage, VendorMachineModelName: "Bambu Lab P1P", CompatiblePlateTypes: BambuXAndP1SeriesPlateTypes),
            [(PrinterBrand.Bambu, "P1S")] = new PrinterModel(PrinterBrand.Bambu, "P1S", CameraTransport.BinaryImage, VendorMachineModelName: "Bambu Lab P1S", CompatiblePlateTypes: BambuXAndP1SeriesPlateTypes),
            [(PrinterBrand.Bambu, "A1")] = new PrinterModel(PrinterBrand.Bambu, "A1", CameraTransport.BinaryImage,
                VendorMachineModelName: "Bambu Lab A1",
                CompatiblePlateTypes: new HashSet<BuildPlateType> { BuildPlateType.TexturedPei, BuildPlateType.HighTemp }),
            [(PrinterBrand.Bambu, "A1MINI")] = new PrinterModel(PrinterBrand.Bambu, "A1MINI", CameraTransport.BinaryImage,
                VendorMachineModelName: "Bambu Lab A1 mini",
                // Cross-checked both signals (research.md R2): machine preset's own
                // not_support_bed_type = "Smooth Cool Plate;Engineering Plate" (authoritative) agrees
                // with the filament-temp fallback (eng_plate_temp resolves to "0" through inheritance).
                CompatiblePlateTypes: new HashSet<BuildPlateType> { BuildPlateType.TexturedPei, BuildPlateType.HighTemp }),
            [(PrinterBrand.System, "Simulation")] = new PrinterModel(PrinterBrand.System, "Simulation", CameraTransport.BinaryImage,
                CompatiblePlateTypes: new HashSet<BuildPlateType> { BuildPlateType.Cool, BuildPlateType.Engineering, BuildPlateType.HighTemp, BuildPlateType.TexturedPei, BuildPlateType.TexturedCool, BuildPlateType.Supertack }),
            // Plates per 012 research.md R9: the vendor's filament presets leave only hot_plate_temp non-zero.
            [(PrinterBrand.Snapmaker, "U1")] = new PrinterModel(PrinterBrand.Snapmaker, "U1", CameraTransport.HttpSnapshot,
                VendorMachineModelName: "Snapmaker U1",
                CompatiblePlateTypes: new HashSet<BuildPlateType> { BuildPlateType.HighTemp }),
        };

    // Every current brand (Bambu and the Snapmaker U1, the real integrations; System, the Simulator stand-in) has live
    // status/dispatch integration — there is no non-live brand registrable today. This is the one place that check
    // extends, never a restructure of the dispatcher/command-handler branches that already read it (FR-012/FR-024b).
    private static readonly HashSet<PrinterBrand> LiveIntegratedBrands = new() { PrinterBrand.Bambu, PrinterBrand.System, PrinterBrand.Snapmaker };

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

    public static bool SupportsLiveIntegration(string brand) =>
        Enum.TryParse<PrinterBrand>(brand, ignoreCase: true, out var parsedBrand) && LiveIntegratedBrands.Contains(parsedBrand);

    /// <summary>
    /// Whether registering needs an operator-typed serial and access code. Bambu addresses MQTT by serial and
    /// authenticates with the LAN access code; a Snapmaker reports its own serial and its Moonraker API key is optional.
    /// </summary>
    public static bool UsesAccessCode(string brand) =>
        !Enum.TryParse<PrinterBrand>(brand, ignoreCase: true, out var parsedBrand) || parsedBrand != PrinterBrand.Snapmaker;

    public static PrinterModel Resolve(string brand, string modelCode)
    {
        if (!TryResolve(brand, modelCode, out var model))
        {
            throw new UnknownPrinterModelException(brand, modelCode);
        }

        return model!;
    }

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
    /// Every (Brand, ModelCode) entry currently registered, grouped by brand — the read path a
    /// backend-exposed catalog endpoint (US3) projects into a response, guaranteed to never drift
    /// from what <see cref="Resolve"/>/<see cref="TryResolve"/> would accept since both read the
    /// same static dictionary.
    /// </summary>
    public static IReadOnlyList<PrinterCatalogBrandGroup> All =>
        Entries.Keys
            .GroupBy(key => key.Brand)
            .Select(group => new PrinterCatalogBrandGroup(
                group.Key,
                group.Select(key => key.ModelCode).ToList()))
            .ToList();

    /// <summary>
    /// Every distinct <see cref="PrinterModel.VendorMachineModelName"/> currently registered — System
    /// Profile ingestion scoping (009, FR-001) cross-references a vendor's own
    /// <c>machine_model_list[].name</c> values against this set rather than a hardcoded vendor name,
    /// so a new vendor's rows appear automatically once a catalog entry maps to it.
    /// </summary>
    public static IReadOnlyList<string> AllVendorMachineModelNames =>
        Entries.Values
            .Select(m => m.VendorMachineModelName)
            .Where(name => name is not null)
            .Select(name => name!)
            .Distinct(StringComparer.Ordinal)
            .ToList();
}

public sealed record PrinterCatalogBrandGroup(PrinterBrand Brand, IReadOnlyList<string> Models);
