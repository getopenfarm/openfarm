using System.Text.Json;

namespace OpenFarm.Domain.Production;

public sealed record PrintObject(string Id, string Name, IReadOnlyList<decimal[]>? Polygon = null);
public sealed record PrintUnit(string Id, IReadOnlyList<string> ObjectIds);

/// <summary>Object IDs belong to this exact source revision and plate, never to a catalog name.</summary>
public sealed record PrintObjectManifest(string RevisionHash, int Plate, string Dialect,
    IReadOnlyList<PrintObject> Objects, IReadOnlyList<PrintUnit> Units,
    int? DetectedCount = null, string? CountSource = null, string? SkipUnavailableReason = null)
{
    public void Validate(int capacity)
    {
        if (SkipUnavailableReason is not null) throw new InvalidOperationException(SkipUnavailableReason);
        if (Objects is null || Units is null || Objects.Any(o => o is null) || Units.Any(u => u is null || u.ObjectIds is null))
            throw new InvalidOperationException("Every printable object needs a complete-unit mapping.");
        var ids = Objects.Select(o => o.Id).ToHashSet(StringComparer.Ordinal);
        var assigned = Units.SelectMany(u => u.ObjectIds).ToArray();
        if (Objects.Count == 0 || Objects.Any(o => string.IsNullOrWhiteSpace(o.Id)) || ids.Count != Objects.Count || Units.Count != capacity
            || Units.Select(u => u.Id).Distinct(StringComparer.Ordinal).Count() != Units.Count
            || Units.Any(u => string.IsNullOrWhiteSpace(u.Id) || u.ObjectIds.Count == 0)
            || assigned.Length != ids.Count || assigned.Distinct(StringComparer.Ordinal).Count() != assigned.Length
            || assigned.Any(id => !ids.Contains(id)))
            throw new InvalidOperationException("Map every printable object to exactly one complete unit, matching units per plate.");
    }
}

/// <summary>Durable selection and request/observation ledger. Planned surplus never becomes a production loss.</summary>
public sealed record PrintObjectExecution(PrintObjectManifest Manifest, IReadOnlyList<string> SelectedUnitIds,
    IReadOnlyList<string> RequestedObjectIds, IReadOnlyList<string> ConfirmedObjectIds,
    bool Observed = false, string? ArtifactPath = null, string? ArtifactHash = null, string? ArtifactName = null,
    string? RequestError = null, IReadOnlyDictionary<string, ProgressMark>? ExcludedAt = null, bool StartRequested = false, bool? CanSkipObjects = null, string? SkipUnavailableReason = null)
{
    public IReadOnlyList<string> PlannedExclusions => Manifest.Units.Where(u => !SelectedUnitIds.Contains(u.Id))
        .SelectMany(u => u.ObjectIds).Order(StringComparer.Ordinal).ToArray();
    public int GoodUnits => Manifest.Units.Count(u => SelectedUnitIds.Contains(u.Id)
        && !u.ObjectIds.Any(id => ConfirmedObjectIds.Contains(id) || RequestedObjectIds.Contains(id)));
    public string ToJson() => JsonSerializer.Serialize(this);
    public static PrintObjectExecution? Read(string? json) => json is null ? null : JsonSerializer.Deserialize<PrintObjectExecution>(json);
}
