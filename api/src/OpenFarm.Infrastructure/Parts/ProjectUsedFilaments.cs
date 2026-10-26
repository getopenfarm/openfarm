using System.Globalization;
using System.Xml.Linq;

namespace OpenFarm.Infrastructure.Parts;

/// <summary>
/// Which filament slots an unsliced OrcaSlicer/Bambu Studio project prints with, from <c>Metadata/model_settings.config</c>:
/// the <c>extruder</c> of every printed volume (its own, else its object's, else slot 1) of every object placed on a plate.
/// plus the slots its painted triangles use. A project lists every slot it defines, used or not; this tells the two apart.
/// </summary>
internal static class ProjectUsedFilaments
{
    /// <summary>
    /// Zero-based slot indices, or <c>null</c> when they cannot be told — unreadable settings, painted colours that could not
    /// be decoded (<paramref name="paintedSlots"/> null), no printed object — so the caller keeps every slot rather than hiding
    /// one that prints. <paramref name="paintedSlots"/> are the slots painted triangles pick, empty when nothing is painted.
    /// </summary>
    public static IReadOnlySet<int>? Read(string modelSettingsXml, IReadOnlySet<int>? paintedSlots)
    {
        if (paintedSlots is null) return null;
        XElement root;
        try { root = XDocument.Parse(modelSettingsXml).Root!; }
        catch (System.Xml.XmlException) { return null; }

        var onPlate = root.Elements("plate").Elements("model_instance")
            .Select(instance => Metadata(instance, "object_id")).Where(id => id is not null).ToHashSet();
        // ponytail: a volume's own extruder counts even if every triangle is repainted, so such a slot shows unused-but-listed;
        // check for an unpainted triangle per volume if that ever matters.
        var used = new HashSet<int>();
        foreach (var obj in root.Elements("object"))
        {
            if (onPlate.Count > 0 && !onPlate.Contains((string?)obj.Attribute("id"))) continue;
            var objectSlot = Slot(Metadata(obj, "extruder")) ?? 0;
            var parts = obj.Elements("part").Where(p => (string?)p.Attribute("subtype") is null or "normal_part" or "modifier_part").ToList();
            if (parts.Count == 0) used.Add(objectSlot);
            foreach (var part in parts) used.Add(Slot(Metadata(part, "extruder")) ?? objectSlot);
        }

        if (used.Count == 0) return null;
        used.UnionWith(paintedSlots);
        return used;
    }

    /// <summary>
    /// Slots a <c>paint_color</c>/<c>mmu_segmentation</c> value paints, or <c>null</c> when it is not a valid triangle-selector
    /// string. The hex is read from its end, one nibble per node: the low two bits count split sides (a split node has that
    /// many + 1 children), otherwise it is a leaf whose upper two bits are the state (1-based slot; 3 continues in the next
    /// nibbles, 3 + their sum while they are 0xF); state 0 is unpainted.
    /// </summary>
    public static HashSet<int>? PaintedSlots(string hex)
    {
        var slots = new HashSet<int>();
        var position = hex.Length;
        int Next() => position > 0 && Uri.IsHexDigit(hex[--position]) ? Uri.FromHex(hex[position]) : -1;

        for (var pending = 1; pending > 0; pending--)
        {
            var nibble = Next();
            if (nibble < 0) return null;
            if ((nibble & 3) != 0) { pending += (nibble & 3) + 1; continue; }

            var state = nibble >> 2;
            if (state == 3)
            {
                int more;
                do
                {
                    if ((more = Next()) < 0) return null;
                    state += more;
                } while (more == 0xF);
            }

            if (state > 0) slots.Add(state - 1);
        }

        return position == 0 ? slots : null;
    }

    private static string? Metadata(XElement element, string key) =>
        element.Elements("metadata").FirstOrDefault(m => (string?)m.Attribute("key") == key)?.Attribute("value")?.Value;

    // "0" or blank means "the object's own"; slots are 1-based in the file.
    private static int? Slot(string? value) =>
        int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var slot) && slot > 0 ? slot - 1 : null;
}
