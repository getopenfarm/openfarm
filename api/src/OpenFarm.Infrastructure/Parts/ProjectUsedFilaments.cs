using System.Globalization;
using System.IO.Compression;
using System.Text.RegularExpressions;
using System.Xml;
using System.Xml.Linq;

namespace OpenFarm.Infrastructure.Parts;

/// <summary>
/// Which filament slots an unsliced OrcaSlicer/Bambu Studio project prints with, from <c>Metadata/model_settings.config</c>:
/// the <c>extruder</c> of every printed volume (its own, else its object's, else slot 1) of every object placed on a plate.
/// plus the slots its painted triangles use. A project lists every slot it defines, used or not; this tells the two apart.
/// </summary>
internal static partial class ProjectUsedFilaments
{
    /// <summary>One plate of a project: its number (<c>plater_id</c>, else its position), name and the objects placed on it.</summary>
    public sealed record ProjectPlate(int Number, string? Name, IReadOnlySet<string> ObjectIds);

    /// <summary>Every <c>&lt;plate&gt;</c> of <c>model_settings.config</c> in file order; empty when it cannot be read.</summary>
    public static IReadOnlyList<ProjectPlate> Plates(string modelSettingsXml)
    {
        XElement root;
        try { root = XDocument.Parse(modelSettingsXml).Root!; }
        catch (XmlException) { return []; }

        return root.Elements("plate").Select((plate, i) => new ProjectPlate(
            int.TryParse(Metadata(plate, "plater_id"), NumberStyles.Integer, CultureInfo.InvariantCulture, out var id) && id > 0 ? id : i + 1,
            Metadata(plate, "plater_name") is { Length: > 0 } name ? name : null,
            plate.Elements("model_instance").Select(instance => Metadata(instance, "object_id")).OfType<string>().ToHashSet())).ToList();
    }

    /// <summary>
    /// Zero-based slot indices, or <c>null</c> when they cannot be told — unreadable settings, painted colours that could not
    /// be decoded (<paramref name="paintedSlots"/> null), no printed object — so the caller keeps every slot rather than hiding
    /// one that prints. <paramref name="paintedSlots"/> are the slots painted triangles pick, empty when nothing is painted.
    /// <paramref name="objectIds"/> narrows it to one plate's objects; by default every object placed on any plate counts.
    /// </summary>
    public static IReadOnlySet<int>? Read(string modelSettingsXml, IReadOnlySet<int>? paintedSlots, IReadOnlySet<string>? objectIds = null)
    {
        if (paintedSlots is null) return null;
        XElement root;
        try { root = XDocument.Parse(modelSettingsXml).Root!; }
        catch (XmlException) { return null; }

        // ponytail: painted slots count for every plate, as they are read per model file, not per object; split them by
        // object if a plate ever lists a colour only another plate paints.
        var onPlate = objectIds ?? root.Elements("plate").Elements("model_instance")
            .Select(instance => Metadata(instance, "object_id")).OfType<string>().ToHashSet();
        // ponytail: a volume's own extruder counts even if every triangle is repainted, so such a slot shows unused-but-listed;
        // check for an unpainted triangle per volume if that ever matters.
        var used = new HashSet<int>();
        foreach (var obj in root.Elements("object"))
        {
            if (onPlate.Count > 0 && ((string?)obj.Attribute("id") is not { } id || !onPlate.Contains(id))) continue;
            var objectSlot = Slot(Metadata(obj, "extruder")) ?? 0;
            var parts = obj.Elements("part").Where(p => (string?)p.Attribute("subtype") is null or "normal_part" or "modifier_part").ToList();
            if (parts.Count == 0) used.Add(objectSlot);
            foreach (var part in parts) used.Add(Slot(Metadata(part, "extruder")) ?? objectSlot);
        }

        if (used.Count == 0) return null;
        used.UnionWith(paintedSlots);
        return used;
    }

    /// <summary>The slots a project archive prints with (see <see cref="Read"/>): plate <paramref name="plate"/>'s, or every placed object's.</summary>
    public static IReadOnlySet<int>? Read(ZipArchive archive, int? plate)
    {
        if (SlicedPlateMetadata.Find(archive, "Metadata/model_settings.config") is not { } entry) return null;
        using var reader = new StreamReader(entry.Open());
        var xml = reader.ReadToEnd();
        if (plate is null) return Read(xml, PaintedSlots(archive));
        return Plates(xml).FirstOrDefault(p => p.Number == plate) is { ObjectIds.Count: > 0 } own ? Read(xml, PaintedSlots(archive), own.ObjectIds) : null;
    }

    /// <summary>
    /// The slots object volumes paint per triangle (<c>paint_color</c>, <c>mmu_segmentation</c>), empty when nothing is painted;
    /// <c>null</c> when a painted value cannot be decoded, so the caller keeps every slot.
    /// </summary>
    public static HashSet<int>? PaintedSlots(ZipArchive archive)
    {
        var slots = new HashSet<int>();
        var settings = new XmlReaderSettings { DtdProcessing = DtdProcessing.Prohibit };
        foreach (var entry in archive.Entries.Where(e => PaintableModelRegex().IsMatch(e.FullName.Replace('\\', '/'))))
        {
            try
            {
                using var xml = XmlReader.Create(entry.Open(), settings);
                while (xml.Read())
                {
                    if (xml.NodeType != XmlNodeType.Element || !xml.HasAttributes) continue;
                    foreach (var attribute in PaintAttributes)
                        if (xml.GetAttribute(attribute) is { } value)
                        {
                            if (PaintedSlots(value) is not { } painted) return null;
                            slots.UnionWith(painted);
                        }
                }
            }
            catch (XmlException)
            {
                return null;
            }
        }

        return slots;
    }

    private static readonly string[] PaintAttributes = ["paint_color", "mmu_segmentation"];

    [GeneratedRegex(@"^3D/(?:Objects/.+|3dmodel)\.model$", RegexOptions.IgnoreCase)]
    private static partial Regex PaintableModelRegex();

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
