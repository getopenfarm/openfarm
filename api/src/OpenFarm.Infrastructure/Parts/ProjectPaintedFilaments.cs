using System.IO.Compression;
using System.Xml;

namespace OpenFarm.Infrastructure.Parts;

/// <summary>Painted slots reachable from a plate's objects, keyed by model path and object ID.</summary>
internal sealed class ProjectPaintedFilaments(ZipArchive archive)
{
    private const string ProductionNamespace = "http://schemas.microsoft.com/3dmanufacturing/production/2015/06";
    private static readonly string[] PaintAttributes = ["paint_color", "mmu_segmentation"];
    private sealed record Component(string Path, string? ObjectId);
    private sealed record ModelObject(HashSet<int> Slots, List<Component> Components)
    {
        public bool Valid { get; set; } = true;
    }

    private readonly Dictionary<string, Dictionary<string, ModelObject>?> _models = new(StringComparer.OrdinalIgnoreCase);

    public HashSet<int>? Read(IReadOnlySet<string> objectIds)
    {
        var slots = new HashSet<int>();
        var visiting = new HashSet<(string Path, string Id)>();
        var visited = new HashSet<(string Path, string Id)>();
        return objectIds.All(id => Visit("3D/3dmodel.model", id, 0)) ? slots : null;

        bool Visit(string path, string? id, int depth)
        {
            // Bound recursion as well as detecting cycles in untrusted component graphs.
            if (id is null || depth > 128) return false;
            var key = (path.ToUpperInvariant(), id);
            if (visited.Contains(key)) return true;
            if (!visiting.Add(key)) return false;
            var objects = Model(path);
            if (objects is null || !objects.TryGetValue(id, out var obj) || !obj.Valid) return false;
            slots.UnionWith(obj.Slots);
            if (!obj.Components.All(c => Visit(c.Path, c.ObjectId, depth + 1))) return false;
            visiting.Remove(key);
            visited.Add(key);
            return true;
        }
    }

    private Dictionary<string, ModelObject>? Model(string path)
    {
        if (_models.TryGetValue(path, out var cached)) return cached;
        Dictionary<string, ModelObject>? objects = null;
        if (SlicedPlateMetadata.Find(archive, path) is { } entry)
        {
            try
            {
                objects = new(StringComparer.Ordinal);
                using var xml = XmlReader.Create(entry.Open(), new XmlReaderSettings { DtdProcessing = DtdProcessing.Prohibit });
                ModelObject? current = null;
                var objectDepth = -1;
                while (xml.Read())
                {
                    if (xml.NodeType == XmlNodeType.EndElement && xml.Depth == objectDepth)
                    {
                        current = null;
                        continue;
                    }
                    if (xml.NodeType != XmlNodeType.Element) continue;
                    if (xml.LocalName == "object")
                    {
                        if (xml.GetAttribute("id") is not { } id || current is not null) { objects = null; break; }
                        var obj = new ModelObject([], []);
                        if (!objects.TryAdd(id, obj)) { objects = null; break; }
                        current = xml.IsEmptyElement ? null : obj;
                        objectDepth = xml.Depth;
                        continue;
                    }
                    if (current is null) continue;
                    if (xml.LocalName == "component")
                    {
                        var target = xml.GetAttribute("path", ProductionNamespace);
                        current.Components.Add(new(target is null ? path : ResolvePath(path, target), xml.GetAttribute("objectid")));
                    }
                    foreach (var attribute in PaintAttributes)
                    {
                        if (xml.GetAttribute(attribute) is not { } value) continue;
                        if (ProjectUsedFilaments.PaintedSlots(value) is { } painted) current.Slots.UnionWith(painted);
                        else current.Valid = false;
                    }
                }
            }
            catch (XmlException) { objects = null; }
        }
        _models[path] = objects;
        return objects;
    }

    private static string ResolvePath(string source, string target)
    {
        target = Uri.UnescapeDataString(target.Replace('\\', '/'));
        var path = target.StartsWith('/') ? target : source[..(source.LastIndexOf('/') + 1)] + target;
        var segments = new List<string>();
        foreach (var segment in path.Split('/', StringSplitOptions.RemoveEmptyEntries))
        {
            if (segment == ".") continue;
            if (segment == "..")
            {
                if (segments.Count == 0) return string.Empty;
                segments.RemoveAt(segments.Count - 1);
            }
            else segments.Add(segment);
        }
        return string.Join('/', segments);
    }
}
