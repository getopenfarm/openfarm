using System.Text.Json;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace OpenFarm.Application.Profiles;

// Format-agnostic shallow key-overwrite merge over a resolved ancestor chain's RawDataJson
// (research.md §10) — real OrcaSlicer/Bambu Studio exports are delta-only, so a single row's own
// RawDataJson is often missing settings only its base actually supplies. Kept deliberately separate
// from IProfileBundleParser's ExtractXDisplayFields, which run once on this method's output.
public static partial class ProfileRawDataMerger
{
    [LoggerMessage(Level = LogLevel.Warning, Message = "Skipping an unparseable ancestor RawDataJson blob during profile chain merge.")]
    private static partial void LogUnparseableAncestor(ILogger logger, Exception ex);

    // rawDataJsonAncestorFirst: oldest resolved ancestor first, target profile's own RawDataJson
    // last. Each element's top-level keys overwrite the accumulator's (shallow overwrite, matching
    // the flat key shape real preset JSON actually uses) — the last element always wins on conflict.
    public static string MergeAncestorChain(IReadOnlyList<string> rawDataJsonAncestorFirst, ILogger? logger = null)
    {
        logger ??= NullLogger.Instance;

        var merged = new Dictionary<string, JsonElement>();

        foreach (var rawDataJson in rawDataJsonAncestorFirst)
        {
            JsonDocument document;
            try
            {
                document = JsonDocument.Parse(rawDataJson);
            }
            catch (JsonException ex)
            {
                // Shouldn't happen — data-model.md guarantees well-formed JSON — but this is a
                // shared read path hit by every catalog view, so tolerate rather than fail the
                // whole request (matches the parser's existing "tolerate, don't fail" philosophy).
                LogUnparseableAncestor(logger, ex);
                continue;
            }

            using (document)
            {
                foreach (var property in document.RootElement.EnumerateObject())
                {
                    merged[property.Name] = property.Value.Clone();
                }
            }
        }

        using var stream = new MemoryStream();
        using (var writer = new Utf8JsonWriter(stream))
        {
            writer.WriteStartObject();
            foreach (var (key, value) in merged)
            {
                writer.WritePropertyName(key);
                value.WriteTo(writer);
            }

            writer.WriteEndObject();
        }

        return System.Text.Encoding.UTF8.GetString(stream.ToArray());
    }
}
