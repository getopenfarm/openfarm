using System.Text.Json;
using System.Text.Json.Serialization;

namespace OpenFarm.Tools.ProvisionSlicers;

public sealed record LockfileEntry(Engine Engine, string Version, Platform Platform, string AssetUrl, string Sha256);

/// <summary>
/// Reader/writer for <c>slicers.lock.json</c> — the resolved asset URL + SHA-256 per
/// <c>(engine, version, platform)</c> entry (research.md §8), giving reproducible integrity
/// verification (FR-014) and the reconciliation basis for idempotency (FR-015).
/// </summary>
public sealed class Lockfile
{
    private readonly Dictionary<(Engine Engine, string Version, Platform Platform), LockfileEntry> _entries;

    private Lockfile(Dictionary<(Engine, string, Platform), LockfileEntry> entries)
    {
        _entries = entries;
    }

    public static Lockfile CreateEmpty() => new(new Dictionary<(Engine, string, Platform), LockfileEntry>());

    public static Lockfile Load(string path)
    {
        if (!File.Exists(path))
        {
            return CreateEmpty();
        }

        var json = File.ReadAllText(path);
        var dto = JsonSerializer.Deserialize<LockfileDto>(json, JsonOptions) ?? new LockfileDto();

        var entries = new Dictionary<(Engine, string, Platform), LockfileEntry>();
        foreach (var entryDto in dto.Entries)
        {
            var engine = Enum.Parse<Engine>(entryDto.Engine);
            var platform = Enum.Parse<Platform>(entryDto.Platform);
            entries[(engine, entryDto.Version, platform)] = new LockfileEntry(engine, entryDto.Version, platform, entryDto.AssetUrl, entryDto.Sha256);
        }

        return new Lockfile(entries);
    }

    public LockfileEntry? Find(Engine engine, string version, Platform platform) =>
        _entries.GetValueOrDefault((engine, version, platform));

    public void Set(LockfileEntry entry) => _entries[(entry.Engine, entry.Version, entry.Platform)] = entry;

    public void Save(string path)
    {
        var dto = new LockfileDto
        {
            Entries = _entries.Values
                .OrderBy(e => e.Engine)
                .ThenBy(e => e.Version, StringComparer.Ordinal)
                .ThenBy(e => e.Platform)
                .Select(e => new LockfileEntryDto
                {
                    Engine = e.Engine.ToString(),
                    Version = e.Version,
                    Platform = e.Platform.ToString(),
                    AssetUrl = e.AssetUrl,
                    Sha256 = e.Sha256,
                })
                .ToList(),
        };

        File.WriteAllText(path, JsonSerializer.Serialize(dto, JsonOptions));
    }

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };

    private sealed class LockfileDto
    {
        [JsonPropertyName("entries")]
        public List<LockfileEntryDto> Entries { get; set; } = [];
    }

    private sealed class LockfileEntryDto
    {
        [JsonPropertyName("engine")]
        public string Engine { get; set; } = string.Empty;

        [JsonPropertyName("version")]
        public string Version { get; set; } = string.Empty;

        [JsonPropertyName("platform")]
        public string Platform { get; set; } = string.Empty;

        [JsonPropertyName("assetUrl")]
        public string AssetUrl { get; set; } = string.Empty;

        [JsonPropertyName("sha256")]
        public string Sha256 { get; set; } = string.Empty;
    }
}
