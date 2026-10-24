using OpenFarm.Domain.Printers;

namespace OpenFarm.Api.Endpoints.Printers;

/// <summary>
/// What the web needs to know about a printer model — everything it renders per model comes from here, so a new
/// catalog entry needs no web change.
/// </summary>
public sealed class PrinterModelResponse
{
    public string ModelCode { get; set; } = string.Empty;

    /// <summary>Display name, e.g. "Bambu Lab P1S".</summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>How OpenFarm talks to it, e.g. "Bambu LAN", "Moonraker".</summary>
    public string Driver { get; set; } = string.Empty;

    /// <summary>One label per extruder, in index order: "Nozzle" on a single-nozzle printer, "T1"–"T4" on the U1.</summary>
    public IReadOnlyList<string> Extruders { get; set; } = [];

    public PrinterConnectionFieldsResponse Connection { get; set; } = new();

    public bool CameraSupported { get; set; }

    public static PrinterModelResponse From(PrinterModel model) => new()
    {
        ModelCode = model.ModelCode,
        Name = model.VendorMachineModelName ?? $"{model.Brand} {model.ModelCode}",
        Driver = model.Driver switch
        {
            PrinterDriver.BambuLan => "Bambu LAN",
            PrinterDriver.SnapmakerMoonraker => "Moonraker",
            _ => "Simulator",
        },
        Extruders = model.Extruders,
        Connection = PrinterConnectionFieldsResponse.For(model),
        CameraSupported = model.CameraSupported,
    };

    public static PrinterModelResponse? Find(string brand, string modelCode) =>
        PrinterModelCatalog.Find(brand, modelCode) is { } model ? From(model) : null;
}

/// <summary>The fields registering or editing a printer asks for, with their copy.</summary>
public sealed class PrinterConnectionFieldsResponse
{
    /// <summary>The operator types the serial number; false when the printer reports its own.</summary>
    public bool SerialRequired { get; set; }

    /// <summary>What the secret is called mid-sentence: "access code", "API key".</summary>
    public string SecretName { get; set; } = string.Empty;

    public bool SecretRequired { get; set; }

    /// <summary>Where to find the secret.</summary>
    public string SecretHint { get; set; } = string.Empty;

    /// <summary>A caveat shown with the connection fields, if any.</summary>
    public string? Note { get; set; }

    public static PrinterConnectionFieldsResponse For(PrinterModel model) => model.Driver switch
    {
        PrinterDriver.SnapmakerMoonraker => new()
        {
            SerialRequired = model.UsesAccessCode,
            SecretName = "API key",
            SecretRequired = model.UsesAccessCode,
            SecretHint = "Moonraker. Blank = none.",
        },
        _ => new()
        {
            SerialRequired = model.UsesAccessCode,
            SecretName = "access code",
            SecretRequired = model.UsesAccessCode,
            SecretHint = "Printer screen → Settings → Network.",
            Note = model.Driver == PrinterDriver.BambuLan ? "LAN mode disconnects Bambu Cloud and Bambu Handy." : null,
        },
    };
}
