using System.IO.Compression;
using System.Text.RegularExpressions;
using System.Xml;
using System.Xml.Linq;

namespace OpenFarm.Application.Parts;

/// <summary>Best-effort suggestion; ambiguous or unequal plate counts retain the default of one.</summary>
public static partial class SlicedFileUnitsPerPrint
{
    public static async Task<int> SuggestAsync(IPartFileStorage storage, string path, CancellationToken ct)
    {
        await using var stream = await storage.OpenReadAsync(path, ct);
        return Suggest(stream, path);
    }

    public static int Suggest(Stream stream, string fileName)
    {
        try
        {
            if (fileName.EndsWith(".3mf", StringComparison.OrdinalIgnoreCase))
            {
                using var zip = new ZipArchive(stream, ZipArchiveMode.Read, leaveOpen: true);
                var entry = zip.GetEntry("Metadata/model_settings.config") ?? zip.GetEntry("Metadata/slice_info.config");
                if (entry is null) return 1;
                using var content = entry.Open();
                using var reader = XmlReader.Create(content, new XmlReaderSettings { DtdProcessing = DtdProcessing.Prohibit, XmlResolver = null });
                var counts = XDocument.Load(reader).Descendants("plate")
                    .Select(p => p.Elements("model_instance").Count() is var n && n > 0 ? n : p.Elements("object").Count())
                    .Where(n => n > 0).Distinct().ToList();
                return counts.Count == 1 ? counts[0] : 1;
            }
            if (!fileName.EndsWith(".gcode", StringComparison.OrdinalIgnoreCase)) return 1;
            using var text = new StreamReader(stream, leaveOpen: true);
            var objectNames = new HashSet<string>(StringComparer.Ordinal);
            int? declaredCount = null;
            while (text.ReadLine() is { } line)
            {
                var count = ObjectCount().Match(line);
                if (count.Success && int.TryParse(count.Groups[1].Value, out var parsed) && parsed > 0) declaredCount = parsed;
                var name = ObjectName().Match(line);
                if (name.Success) objectNames.Add((name.Groups[1].Success ? name.Groups[1].Value : name.Groups[2].Value).Trim());
            }
            return declaredCount ?? (objectNames.Count > 0 ? objectNames.Count : 1);
        }
        catch (Exception ex) when (ex is InvalidDataException or XmlException)
        {
            return 1;
        }
    }

    [GeneratedRegex(@"^;\s*(?:object_count|objects_count)\s*=\s*(\d+)", RegexOptions.IgnoreCase)]
    private static partial Regex ObjectCount();

    [GeneratedRegex(@"^(?:EXCLUDE_OBJECT_DEFINE\s+NAME=([^\s]+)|;\s*printing object\s+(.+))", RegexOptions.IgnoreCase)]
    private static partial Regex ObjectName();
}
