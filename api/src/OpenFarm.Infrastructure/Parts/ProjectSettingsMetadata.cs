using System.Text.Json;
using OpenFarm.Application.Parts;
using OpenFarm.Domain.Parts;

namespace OpenFarm.Infrastructure.Parts;

internal sealed record ProjectSettingsMetadata(decimal? Nozzle, IReadOnlyList<DetectedFilamentSlot> Slots,
    EstimationDetails Details, bool MaterialComplete, string? UnavailableReason)
{
    internal static ProjectSettingsMetadata Read(string content)
    {
        try
        {
            using var document = JsonDocument.Parse(content);
            var json = document.RootElement;
            if (json.ValueKind != JsonValueKind.Object) return Invalid();
            string? First(string key) => ResolveEstimationProfiles.ReadFirst(json, key);
            var types = ResolveEstimationProfiles.ReadStrings(json, "filament_type");
            var colors = ResolveEstimationProfiles.ReadStrings(json, "filament_colour");
            var vendors = ResolveEstimationProfiles.ReadStrings(json, "filament_vendor");
            var slots = types.Select((type, i) => new DetectedFilamentSlot(type,
                i < colors.Count ? SlicedPlateMetadata.NormalizeColor(colors[i]) : null, Vendor(vendors, i)))
                .Where(s => !string.IsNullOrWhiteSpace(s.MaterialType)).ToArray();
            var nozzle = ResolveEstimationProfiles.ReadUniformNozzle(json);
            var details = new EstimationDetails("EmbeddedProject", First("printer_model"), First("printer_settings_id"),
                First("print_settings_id"), ResolveEstimationProfiles.ReadStrings(json, "filament_settings_id"), First("curr_bed_type"));
            var complete = types.Count > 0 && types.All(t => !string.IsNullOrWhiteSpace(t));
            var reason = nozzle is null || !complete || string.IsNullOrWhiteSpace(details.MachineProfileName)
                || string.IsNullOrWhiteSpace(details.ProcessProfileName) || string.IsNullOrWhiteSpace(details.BuildPlate)
                ? "Estimation unavailable: embedded project settings are incomplete or contain incompatible nozzle diameters." : null;
            return new(nozzle, slots, details, complete, reason);
        }
        catch (Exception ex) when (ex is JsonException or InvalidOperationException)
        {
            return Invalid();
        }
    }

    /// <summary>Every filament slot of the project in order, a blank one (no type) as null, so a slot's index is its tool.</summary>
    internal static IReadOnlyList<DetectedFilamentSlot?> ReadAllSlots(string content)
    {
        try
        {
            using var document = JsonDocument.Parse(content);
            if (document.RootElement.ValueKind != JsonValueKind.Object) return [];
            var types = ResolveEstimationProfiles.ReadStrings(document.RootElement, "filament_type");
            var colors = ResolveEstimationProfiles.ReadStrings(document.RootElement, "filament_colour");
            var vendors = ResolveEstimationProfiles.ReadStrings(document.RootElement, "filament_vendor");
            return types.Select((type, i) => string.IsNullOrWhiteSpace(type)
                ? null
                : new DetectedFilamentSlot(type, i < colors.Count ? SlicedPlateMetadata.NormalizeColor(colors[i]) : null, Vendor(vendors, i))).ToList();
        }
        catch (Exception ex) when (ex is JsonException or InvalidOperationException)
        {
            return [];
        }
    }

    private static string? Vendor(IReadOnlyList<string> vendors, int i) =>
        i < vendors.Count && !string.IsNullOrWhiteSpace(vendors[i]) ? vendors[i] : null;

    private static ProjectSettingsMetadata Invalid() => new(null, [], new("EmbeddedProject"), false,
        "Estimation unavailable: embedded project settings could not be read.");
}
