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

    /// <summary>
    /// Extends <see cref="MergeAncestorChain"/> with Bambu Studio's <c>include</c> gcode-template
    /// fold-in (research.md §0a) — a real fork divergence from OrcaSlicer, present on every current
    /// Bambu Studio machine preset and 68% of its filament presets, absent from OrcaSlicer's data
    /// entirely. Each name in <paramref name="includedTemplateNames"/> is resolved via
    /// <paramref name="resolveIncludedRawJson"/> (scoped by the caller to the same
    /// VendorName+Kind+Origin=System as the target — the same lookup shape as existing
    /// base-name resolution) and folded in the same shallow-overwrite way <see cref="MergeAncestorChain"/>
    /// already uses, applied <b>before</b> the target's own final layer in
    /// <paramref name="rawDataJsonAncestorFirst"/> so the target's own explicit keys still win on a
    /// genuine collision. A name that doesn't resolve (<paramref name="resolveIncludedRawJson"/>
    /// returns <c>null</c>) is treated as a normal resolve-time gap — not a hard error — mirroring how
    /// an unresolved <c>BaseProfileName</c> is already handled elsewhere in this codebase.
    /// </summary>
    public static string MergeAncestorChainWithIncludes(
        IReadOnlyList<string> rawDataJsonAncestorFirst,
        IReadOnlyList<string> includedTemplateNames,
        Func<string, string?> resolveIncludedRawJson,
        ILogger? logger = null)
    {
        if (includedTemplateNames.Count == 0 || rawDataJsonAncestorFirst.Count == 0)
        {
            return MergeAncestorChain(rawDataJsonAncestorFirst, logger);
        }

        var resolvedFragments = new List<string>();
        foreach (var name in includedTemplateNames)
        {
            if (resolveIncludedRawJson(name) is { } rawJson)
            {
                resolvedFragments.Add(rawJson);
            }
        }

        var combined = new List<string>(rawDataJsonAncestorFirst.Count - 1 + resolvedFragments.Count + 1);
        combined.AddRange(rawDataJsonAncestorFirst.Take(rawDataJsonAncestorFirst.Count - 1));
        combined.AddRange(resolvedFragments);
        combined.Add(rawDataJsonAncestorFirst[^1]);

        return MergeAncestorChain(combined, logger);
    }
}
