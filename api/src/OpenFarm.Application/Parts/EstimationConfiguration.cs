using System.Text.Json;
using OpenFarm.Domain.Parts;
using OpenFarm.Domain.Printers;
using OpenFarm.Domain.Profiles;
using OpenFarm.Application.Profiles;

namespace OpenFarm.Application.Parts;

public sealed record EstimationProfileSelection(Guid MachineProfileId, Guid ProcessProfileId,
    Guid FilamentProfileId, BuildPlateType BuildPlateType);

/// <summary>Immutable resolved inputs stored on a processing job before estimation begins.</summary>
public sealed record EstimationConfiguration(bool EmbeddedProject, decimal? NozzleDiameterMm,
    string? MaterialType, ResolvedSlicingProfiles? Profiles);

public sealed class ResolveEstimationProfiles(IMachineProfileRepository machines, IProcessProfileRepository processes,
    IFilamentProfileRepository filaments, ResolveSlicingProfileSelection resolver)
{
    public async Task<(EstimationConfiguration Configuration, EstimationDetails Details)> ResolveAsync(
        EstimationProfileSelection selection, SlicerEngine engine, CancellationToken cancellationToken)
    {
        try { return await ResolveCoreAsync(selection, engine, cancellationToken); }
        catch (Exception ex) when (ex is JsonException or InvalidOperationException or FormatException or ArgumentException)
        { throw new SlicingProfileUnconfiguredException($"The estimation profiles could not be resolved: {ex.Message}"); }
    }

    private async Task<(EstimationConfiguration Configuration, EstimationDetails Details)> ResolveCoreAsync(
        EstimationProfileSelection selection, SlicerEngine engine, CancellationToken cancellationToken)
    {
        if (engine is not (SlicerEngine.OrcaSlicer or SlicerEngine.BambuStudio))
            throw new SlicingProfileUnconfiguredException("This slicer does not support the available estimation profile formats.");
        var machine = await machines.GetByIdAsync(selection.MachineProfileId, cancellationToken)
            ?? throw new SlicingProfileUnconfiguredException("The estimation machine profile no longer exists.");
        var process = await processes.GetByIdAsync(selection.ProcessProfileId, cancellationToken)
            ?? throw new SlicingProfileUnconfiguredException("The estimation process profile no longer exists.");
        var filament = await filaments.GetByIdAsync(selection.FilamentProfileId, cancellationToken)
            ?? throw new SlicingProfileUnconfiguredException("The estimation filament profile no longer exists.");
        var format = engine == SlicerEngine.OrcaSlicer ? ProfileSourceFormat.OrcaSlicer : ProfileSourceFormat.BambuStudio;
        if (machine.SourceFormat != format || process.SourceFormat != format || filament.SourceFormat != format)
            throw new SlicingProfileUnconfiguredException("Estimation profiles must belong to the selected slicer engine.");
        if (!machine.IsSelectable || !process.IsSelectable || !filament.IsSelectable)
            throw new SlicingProfileUnconfiguredException("Estimation profiles must be selectable profiles.");
        if (process.Origin != ProfileOrigin.System || process.CompatibleMachineNames?.Contains(machine.Name, StringComparer.Ordinal) != true)
            throw new SlicingProfileUnconfiguredException("The estimation process profile is incompatible with the machine profile.");
        if (filament.CompatibleMachineNames is { Count: > 0 } compatible && !compatible.Contains(machine.Name, StringComparer.Ordinal))
            throw new SlicingProfileUnconfiguredException("The estimation filament profile is incompatible with the machine profile.");
        if (!Enum.IsDefined(selection.BuildPlateType))
            throw new SlicingProfileUnconfiguredException("The estimation build plate is invalid.");
        var profiles = await resolver.ResolveProfilesAsync(machine.Id, process.Id, filament.Id, selection.BuildPlateType, cancellationToken);
        using var machineJson = JsonDocument.Parse(profiles.MachineSettingsJson);
        var model = PrinterModelCatalog.All.SelectMany(g => g.Models.Select(code => PrinterModelCatalog.Resolve(g.Brand.ToString(), code)))
            .FirstOrDefault(m => m.VendorMachineModelName == (machine.VendorMachineModelName ?? ReadFirst(machineJson.RootElement, "printer_model")) && m.VendorMachineModelName is not null);
        if (model?.CompatiblePlateTypes is { } plates && !plates.Contains(selection.BuildPlateType))
            throw new SlicingProfileUnconfiguredException("The estimation build plate is incompatible with the machine model.");
        using var filamentJson = JsonDocument.Parse(profiles.FilamentSettingsJson);
        var temperature = ReadFirst(filamentJson.RootElement, BuildPlateTypeTempKeys.Resolve(selection.BuildPlateType));
        if (!decimal.TryParse(temperature, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var temp) || temp <= 0)
            throw new SlicingProfileUnconfiguredException("The estimation filament profile needs a positive temperature for the selected build plate.");
        var nozzle = ReadUniformNozzle(machineJson.RootElement);
        var material = ReadFirst(filamentJson.RootElement, "filament_type");
        if (nozzle is null || string.IsNullOrWhiteSpace(material))
            throw new SlicingProfileUnconfiguredException("Estimation profiles need a known uniform nozzle diameter and filament type.");
        return (new(false, nozzle, material, profiles), new("DefaultProfiles",
            ReadFirst(machineJson.RootElement, "printer_model"), machine.Name, process.Name,
            [filament.Name], BuildPlateTypeVendorNames.Resolve(selection.BuildPlateType)));
    }

    public static string? ReadFirst(JsonElement json, string key)
    {
        var values = ReadStrings(json, key);
        return values.Count > 0 ? values[0] : null;
    }

    public static IReadOnlyList<string> ReadStrings(JsonElement json, string key)
    {
        if (!json.TryGetProperty(key, out var value)) return [];
        return value.ValueKind == JsonValueKind.Array ? value.EnumerateArray().Select(v => v.ToString()).ToArray()
            : value.ValueKind is JsonValueKind.String or JsonValueKind.Number ? [value.ToString()] : [];
    }

    public static decimal? ReadUniformNozzle(JsonElement json)
    {
        var values = ReadStrings(json, "nozzle_diameter").SelectMany(v => v.Split([',', ';'], StringSplitOptions.TrimEntries)).ToArray();
        var nozzles = values.Select(v => decimal.TryParse(v, System.Globalization.NumberStyles.Float,
            System.Globalization.CultureInfo.InvariantCulture, out var n) && n > 0 ? (decimal?)decimal.Round(n, 4) : null).ToArray();
        return nozzles.Length > 0 && nozzles.All(n => n is not null) && nozzles.Distinct().Count() == 1 ? nozzles[0] : null;
    }
}
