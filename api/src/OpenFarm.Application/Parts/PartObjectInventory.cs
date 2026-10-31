using System.IO.Compression;
using System.Text.RegularExpressions;
using System.Xml;
using System.Xml.Linq;

namespace OpenFarm.Application.Parts;

public sealed record DetectedPrintObject(string Id, string Name);

/// <summary>One printable instance is one unit. Counts and native skip identities are separate evidence.</summary>
public sealed record PartObjectInventory(int? Count, string? Source, string Dialect,
    IReadOnlyList<DetectedPrintObject> Objects, string? SkipUnavailableReason = null)
{
    public static async Task<PartObjectInventory> ReadAsync(IPartFileStorage storage, string path, int? plate, CancellationToken ct)
    {
        await using var stream = await storage.OpenReadAsync(path, ct);
        return Read(stream, path, plate);
    }

    public static PartObjectInventory Read(Stream stream, string fileName, int? plate = null)
    {
        try
        {
            if (fileName.EndsWith(".3mf", StringComparison.OrdinalIgnoreCase))
            {
                using var archive = new ZipArchive(stream, ZipArchiveMode.Read, leaveOpen: true);
                return ReadArchive(archive, plate);
            }
            if (fileName.EndsWith(".gcode", StringComparison.OrdinalIgnoreCase))
            {
                using var reader = new StreamReader(stream, leaveOpen: true);
                return ReadCode(reader.ReadToEnd(), null, false);
            }
            return Unknown();
        }
        catch (Exception ex) when (ex is InvalidDataException or XmlException)
        {
            return Unknown("Object metadata could not be read. Re-export the file to enable object skipping.");
        }
    }

    private static PartObjectInventory ReadArchive(ZipArchive archive, int? selected)
    {
        var settings = Xml(archive, "Metadata/model_settings.config");
        var slice = Xml(archive, "Metadata/slice_info.config");
        var plates = (settings?.Descendants("plate") ?? []).Select((p, i) => PlateNumber(p, i))
            .Concat((slice?.Descendants("plate") ?? []).Select((p, i) => PlateNumber(p, i)))
            .Concat(archive.Entries.Select(e => Regex.Match(e.FullName, @"^Metadata/plate_(\d+)\.gcode$"))
                .Where(m => m.Success).Select(m => int.TryParse(m.Groups[1].Value, out var n) ? n : 0).Where(n => n > 0))
            .Distinct().Order().ToArray();
        if (selected is null && plates.Length > 1)
        {
            var all = plates.Select(n => ReadArchive(archive, n)).ToArray();
            var counts = all.Select(p => p.Count).Distinct().ToArray();
            return counts.Length == 1 && counts[0] is > 0
                ? new(counts[0], "UniformPlateCounts", "Bambu", [], "Choose a plate to identify skippable objects.") : Unknown();
        }
        var number = selected ?? plates.FirstOrDefault(1);
        var own = settings?.Descendants("plate").Select((p, i) => (p, n: PlateNumber(p, i))).FirstOrDefault(p => p.n == number).p;
        var ownSlice = slice?.Descendants("plate").Select((p, i) => (p, n: PlateNumber(p, i))).FirstOrDefault(p => p.n == number).p;
        var slicedObjects = ownSlice?.Elements("object").ToArray() ?? [];
        var buildItems = BuildItems(archive);
        var build = buildItems.Where(e => e.Attribute("objectid") is not null)
            .GroupBy(e => e.Attribute("objectid")!.Value).ToDictionary(g => g.Key, g => g.ToArray());
        var instances = own?.Elements("model_instance").Where(Printable).Where(i =>
        {
            var items = build?.GetValueOrDefault(Value(i, "object_id") ?? "");
            var index = int.TryParse(Value(i, "instance_id"), out var n) ? n : 0;
            return items is null || index < 0 || index >= items.Length || Printable(items[index]);
        }).ToArray() ?? [];
        if (archive.GetEntry($"Metadata/plate_{number}.gcode") is { } code)
        {
            var result = ReadCode(Text(code), slicedObjects, true);
            if (result.Count is null && instances.Length > 0)
                return result with { Count = instances.Length, Source = "ModelInstances" };
            if (instances.Length > 0 && result.Count != instances.Length)
                return result with { Objects = [], SkipUnavailableReason = "Source instances and G-code quantities disagree. Re-export the file." };
            return result;
        }

        // An empty model_settings is common in sliced exports; it must not hide slice_info.
        if (instances.Length > 0)
        {
            var sourceNames = settings?.Root?.Elements("object").Where(o => o.Attribute("id") is not null)
                .GroupBy(o => o.Attribute("id")!.Value).ToDictionary(g => g.Key, g => Value(g.First(), "name"));
            var objects = instances.Select(i => new DetectedPrintObject(Value(i, "identify_id") ?? "",
                sourceNames?.GetValueOrDefault(Value(i, "object_id") ?? "") ?? Value(i, "identify_id") ?? "Object")).ToArray();
            var valid = objects.All(o => int.TryParse(o.Id, out var id) && id > 0) && objects.Select(o => o.Id).Distinct().Count() == objects.Length;
            return new(instances.Length, "ModelInstances", "Bambu", valid ? objects : [],
                valid ? null : "The source objects have no unique native skip identities. Re-export with object labels.");
        }
        if (slicedObjects.Length > 0)
            return new(slicedObjects.Length, "SliceObjects", "Bambu", [], "No labelled G-code is available to verify object skipping.");
        var legacyObjects = own?.Elements("object").Count() ?? 0;
        if (legacyObjects > 0) return new(legacyObjects, "PlateObjects", "Bambu", [], "Native object identities are unavailable.");

        // Geometry-only 3MF: build items are instances; resource meshes/components are not units.
        if (number == 1 && plates.Length <= 1)
        {
            var count = buildItems.Count(Printable);
            if (count > 0) return new(count, "BuildItems", "Bambu", [], "The source objects have no native skip identities.");
        }
        return Unknown();
    }

    private static PartObjectInventory ReadCode(string code, XElement[]? metadata, bool packaged)
    {
        var klipperStarts = Regex.Matches(code, @"(?mi)^\s*EXCLUDE_OBJECT_START\s+NAME=([A-Za-z0-9_.-]+)\s*$");
        var klipper = klipperStarts.Select(m => m.Groups[1].Value.ToUpperInvariant()).Distinct().ToArray();
        var bambuStarts = Regex.Matches(code, @"(?m)^; start printing object, unique label id: (\d+)\r?\nM624 [^\r\n]+");
        var bambu = bambuStarts.Select(m => m.Groups[1].Value).Distinct().ToArray();
        if (klipper.Length > 0 && bambu.Length > 0) return Unknown("Mixed object-exclusion dialects are unsupported.");
        var dialect = klipper.Length > 0 ? "Klipper" : "Bambu";
        var ids = klipper.Length > 0 ? klipper : bambu;
        if (Regex.Count(code, @"(?mi)^\s*EXCLUDE_OBJECT_START\b") != klipperStarts.Count
            || Regex.Count(code, @"(?m)^; start printing object, unique label id:") != bambuStarts.Count)
            return Unknown("This file contains unsupported object identifiers.");
        var declared = Regex.Matches(code, @"(?mi)^;\s*(?:object_count|objects_count)\s*=\s*(\d+)")
            .Select(m => int.TryParse(m.Groups[1].Value, out var n) ? n : 0).Where(n => n > 0).Distinct().ToArray();
        var definitions = Regex.Matches(code, @"(?mi)^EXCLUDE_OBJECT_DEFINE\s+NAME=([A-Za-z0-9_.-]+)")
            .Select(m => m.Groups[1].Value.ToUpperInvariant()).Distinct().ToArray();
        var count = ids.Length > 0 ? ids.Length : metadata is { Length: > 0 } ? metadata.Length
            : definitions.Length > 0 ? definitions.Length : declared.Length == 1 ? declared[0] : (int?)null;
        string? reason = null;
        if (declared.Length > 1 || (declared.Length == 1 && count != declared[0])) reason = "Declared quantity disagrees with object labels. Re-export the file.";
        var metadataIds = metadata?.Select(o => (string?)o.Attribute("identify_id") ?? "").ToArray() ?? [];
        if (metadataIds.Length > 0 && (metadataIds.Distinct().Count() != metadataIds.Length || !metadataIds.Order().SequenceEqual(ids.Order())))
            reason = "Sliced metadata and G-code object identities disagree. Re-export the file.";
        if (dialect == "Klipper" && definitions.Length > 0 && !definitions.Order().SequenceEqual(ids.Order()))
            reason = "Defined and printed object identities disagree. Re-export the file.";
        var names = (metadata ?? []).Where(o => o.Attribute("identify_id") is not null).GroupBy(o => o.Attribute("identify_id")!.Value)
            .ToDictionary(g => g.Key, g => (string?)g.First().Attribute("name") ?? g.Key);
        var objects = ids.Select(id => new DetectedPrintObject(id, names.GetValueOrDefault(id) ?? id)).ToArray();
        reason ??= ids.Length == 0 ? "Export labelled G-code to enable object skipping."
            : dialect == "Bambu" && (!packaged || metadataIds.Length == 0)
                ? "Bambu object identity metadata is unavailable. Use a labelled sliced 3MF for object skipping." : null;
        return new(count, ids.Length > 0 ? "GcodeLabels" : metadata is { Length: > 0 } ? "SliceObjects"
            : definitions.Length > 0 ? "ObjectDefinitions" : count is not null ? "DeclaredCount" : null, dialect, objects, reason);
    }

    private static bool Printable(XElement element) => (string?)element.Attribute("printable") is not ("0" or "false") && Value(element, "printable") is not ("0" or "false");
    private static int PlateNumber(XElement p, int index) => int.TryParse(Value(p, "plater_id") ?? Value(p, "index"), out var n) ? n : index + 1;
    private static string? Value(XElement e, string key) => e.Elements("metadata").FirstOrDefault(m => (string?)m.Attribute("key") == key)?.Attribute("value")?.Value;
    private static XElement[] BuildItems(ZipArchive archive)
    {
        if (archive.GetEntry("3D/3dmodel.model") is not { } entry) return [];
        using var content = entry.Open();
        using var reader = XmlReader.Create(content, new XmlReaderSettings { DtdProcessing = DtdProcessing.Prohibit, XmlResolver = null });
        while (!reader.EOF)
        {
            if (reader.NodeType == XmlNodeType.Element && reader.LocalName == "resources")
            {
                reader.Skip(); // Mesh vertices/components never contribute to the instance count.
                continue;
            }
            if (reader.NodeType == XmlNodeType.Element && reader.LocalName == "build")
                return ((XElement)XNode.ReadFrom(reader)).Elements().Where(e => e.Name.LocalName == "item").ToArray();
            reader.Read();
        }
        return [];
    }
    private static XDocument? Xml(ZipArchive archive, string path)
    {
        if (archive.GetEntry(path) is not { } entry) return null;
        using var content = entry.Open();
        using var reader = XmlReader.Create(content, new XmlReaderSettings { DtdProcessing = DtdProcessing.Prohibit, XmlResolver = null });
        return XDocument.Load(reader);
    }
    private static string Text(ZipArchiveEntry entry) { using var reader = new StreamReader(entry.Open()); return reader.ReadToEnd(); }
    private static PartObjectInventory Unknown(string? reason = null) => new(null, null, "Bambu", [], reason ?? "The printable object count could not be detected.");
}
