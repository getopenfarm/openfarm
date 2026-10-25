using System.Globalization;
using System.Xml.Linq;

namespace OpenFarm.Infrastructure.Parts;

/// <summary>
/// Which filament slots an unsliced OrcaSlicer/Bambu Studio project prints with, from <c>Metadata/model_settings.config</c>:
/// the <c>extruder</c> of every printed volume (its own, else its object's, else slot 1) of every object placed on a plate.
/// A project lists every slot it defines, used or not; this tells the two apart.
/// </summary>
internal static class ProjectUsedFilaments
{
    /// <summary>
    /// Zero-based slot indices, or <c>null</c> when they cannot be told — unreadable settings, painted colours (which pick a
    /// slot per triangle), no printed object — so the caller keeps every slot rather than hiding one that prints.
    /// </summary>
    public static IReadOnlySet<int>? Read(string modelSettingsXml, bool paintedColours)
    {
        if (paintedColours) return null;
        XElement root;
        try { root = XDocument.Parse(modelSettingsXml).Root!; }
        catch (System.Xml.XmlException) { return null; }

        var onPlate = root.Elements("plate").Elements("model_instance")
            .Select(instance => Metadata(instance, "object_id")).Where(id => id is not null).ToHashSet();
        var used = new HashSet<int>();
        foreach (var obj in root.Elements("object"))
        {
            if (onPlate.Count > 0 && !onPlate.Contains((string?)obj.Attribute("id"))) continue;
            var objectSlot = Slot(Metadata(obj, "extruder")) ?? 0;
            var parts = obj.Elements("part").Where(p => (string?)p.Attribute("subtype") is null or "normal_part" or "modifier_part").ToList();
            if (parts.Count == 0) used.Add(objectSlot);
            foreach (var part in parts) used.Add(Slot(Metadata(part, "extruder")) ?? objectSlot);
        }

        return used.Count > 0 ? used : null;
    }

    private static string? Metadata(XElement element, string key) =>
        element.Elements("metadata").FirstOrDefault(m => (string?)m.Attribute("key") == key)?.Attribute("value")?.Value;

    // "0" or blank means "the object's own"; slots are 1-based in the file.
    private static int? Slot(string? value) =>
        int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var slot) && slot > 0 ? slot - 1 : null;
}
