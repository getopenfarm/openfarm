using System.Text.Json;
using System.Text.Json.Serialization;
using OpenFarm.Application.Parts;
using OpenFarm.Application.Profiles;
using OpenFarm.Domain.Parts;

namespace OpenFarm.Application.Production;

/// <summary>What a file sent to a printer was sliced with: the slicer pin and, per kind, the preset selected and the preset it resolved to.</summary>
public sealed record DispatchProvenance(SlicerRef Pin, IReadOnlyList<ResolvedPreset> Presets, string ConfigurationStamp, DateTimeOffset AtUtc)
{
    private static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web) { Converters = { new JsonStringEnumConverter() } };

    public static DispatchProvenance From(ResolvedSlicingProfiles profiles, string configurationStamp, DateTimeOffset atUtc) =>
        new(profiles.Pin, [profiles.Machine, profiles.Process, profiles.Filament], configurationStamp, atUtc);

    public string ToJson() => JsonSerializer.Serialize(this, Options);

    public static DispatchProvenance? FromJson(string? json) =>
        string.IsNullOrWhiteSpace(json) ? null : JsonSerializer.Deserialize<DispatchProvenance>(json, Options);
}
