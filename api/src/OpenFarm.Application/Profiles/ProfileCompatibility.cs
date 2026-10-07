using System.Globalization;
using System.Text.Json;
using OpenFarm.Application.Printers;
using OpenFarm.Domain.Parts;
using OpenFarm.Domain.Printers;
using OpenFarm.Domain.Profiles;

namespace OpenFarm.Application.Profiles;

/// <summary>Shared effective-preset checks for pickers, assignment writes and production.</summary>
public sealed class ProfileCompatibility(IMachineProfileRepository machines, IFilamentProfileRepository filaments)
{
    private readonly Dictionary<Guid, string> _machineJson = [];
    private readonly Dictionary<Guid, string> _filamentJson = [];

    public async Task<string> MachineJsonAsync(MachineProfile profile, CancellationToken ct)
    {
        if (_machineJson.TryGetValue(profile.Id, out var cached)) return cached;
        var (chain, unresolved) = await ProfileChainWalker.WalkByIdAsync(profile.Id, async (id, token) =>
        {
            var row = await machines.GetByIdAsync(id, token);
            return row is null ? null : new ProfileChainNode(row.RawDataJson, row.BaseProfileId, row.BaseProfileName);
        }, ct);
        if (unresolved) throw new SlicingProfileUnconfiguredException($"Machine profile '{profile.Name}' has an unresolved inheritance chain.");
        return _machineJson[profile.Id] = ProfileRawDataMerger.MergeAncestorChain(chain);
    }

    public async Task<string> FilamentJsonAsync(FilamentProfile profile, CancellationToken ct)
    {
        if (_filamentJson.TryGetValue(profile.Id, out var cached)) return cached;
        var (chain, unresolved) = await ProfileChainWalker.WalkByIdAsync(profile.Id, async (id, token) =>
        {
            var row = await filaments.GetByIdAsync(id, token);
            return row is null ? null : new ProfileChainNode(row.RawDataJson, row.BaseProfileId, row.BaseProfileName);
        }, ct);
        if (unresolved) throw new SlicingProfileUnconfiguredException($"Filament profile '{profile.Name}' has an unresolved inheritance chain.");
        return _filamentJson[profile.Id] = ProfileRawDataMerger.MergeAncestorChain(chain);
    }

    public async Task<bool> MachineMatchesAsync(MachineProfile profile, string brand, string modelCode, decimal nozzleMm, CancellationToken ct)
    {
        if (!profile.IsSelectable || !profile.IsCurrentlyOffered) return false;
        using var json = JsonDocument.Parse(await MachineJsonAsync(profile, ct));
        var root = json.RootElement;
        var nozzles = ReadStrings(root, "nozzle_diameter");
        if (!PrinterModelCatalog.TryResolve(brand, modelCode, out var registered)) return false;
        var model = registered!.VendorMachineModelName;
        return nozzles.Count > 0 && nozzles.All(n => decimal.TryParse(n, NumberStyles.Float, CultureInfo.InvariantCulture, out var value) && value == nozzleMm)
            && (model is null ? brand == "System" : ReadStrings(root, "printer_model").Contains(model, StringComparer.Ordinal));
    }

    public async Task ValidateMachineAsync(Printer printer, MachineProfile profile, CancellationToken ct)
    {
        if (!printer.NozzleConfirmed || printer.ReportedNozzleDiameterMm is not { } nozzle)
            throw new SlicingProfileUnconfiguredException($"Printer '{printer.Name}' is waiting for nozzle telemetry from its current connection.");
        if (!await MachineMatchesAsync(profile, printer.Brand, printer.ModelCode, nozzle, ct))
            throw new SlicingProfileUnconfiguredException($"Machine profile '{profile.Name}' does not match {printer.ModelCode} with its reported {nozzle} mm nozzle. Select a compatible machine profile.");
    }

    public async Task ValidateFilamentAsync(FilamentProfile profile, string materialType, string brand, string modelCode,
        decimal nozzleMm, CancellationToken ct, MachineProfile? targetMachine = null)
    {
        if (!profile.IsSelectable || !profile.IsCurrentlyOffered)
            throw new SlicingProfileUnconfiguredException($"Filament profile '{profile.Name}' is no longer selectable.");
        using var json = JsonDocument.Parse(await FilamentJsonAsync(profile, ct));
        var root = json.RootElement;
        if (!ReadStrings(root, "filament_type").Contains(materialType, StringComparer.Ordinal))
            throw new SlicingProfileUnconfiguredException($"Filament profile '{profile.Name}' does not match material '{materialType}'.");
        var compatible = ReadStrings(root, "compatible_printers");
        var declaredNozzles = ReadStrings(root, "nozzle_diameter");
        if (declaredNozzles.Any(n => !decimal.TryParse(n, NumberStyles.Float, CultureInfo.InvariantCulture, out var value) || value != nozzleMm))
            throw new SlicingProfileUnconfiguredException($"Filament profile '{profile.Name}' does not match the {nozzleMm} mm nozzle.");
        if (ReadStrings(root, "compatible_printers_condition").Any(c => !string.IsNullOrWhiteSpace(c)))
            throw new SlicingProfileUnconfiguredException($"Filament profile '{profile.Name}' uses a compatibility expression that OpenFarm cannot validate. Import a profile with explicit compatible printers.");
        if (compatible.Count == 0) return;
        var candidates = targetMachine is null ? await machines.ListAsync(ct) : [targetMachine];
        foreach (var machine in candidates)
        {
            if (compatible.Contains(machine.Name, StringComparer.Ordinal)
                && await MachineMatchesAsync(machine, brand, modelCode, nozzleMm, ct)) return;
        }
        throw new SlicingProfileUnconfiguredException($"Filament profile '{profile.Name}' is incompatible with {modelCode} {nozzleMm} mm.");
    }

    public static IReadOnlyList<string> ReadStrings(JsonElement root, string key)
    {
        if (!root.TryGetProperty(key, out var value)) return [];
        return value.ValueKind switch
        {
            JsonValueKind.Array => value.EnumerateArray().Select(v => v.ToString()).Where(v => !string.IsNullOrWhiteSpace(v)).ToArray(),
            JsonValueKind.String => string.IsNullOrWhiteSpace(value.GetString()) ? [] : [value.GetString()!],
            JsonValueKind.Number => [value.ToString()],
            _ => [],
        };
    }
}
