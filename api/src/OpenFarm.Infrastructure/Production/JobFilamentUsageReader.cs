using System.Globalization;
using System.IO.Compression;
using System.Text;
using System.Xml.Linq;
using OpenFarm.Application.Production;
using OpenFarm.Infrastructure.Parts;

namespace OpenFarm.Infrastructure.Production;

/// <summary>What the dispatched file says about filament: per-tool grams, and per-tool cumulative extrusion by layer (mm).</summary>
internal sealed record JobFilamentUsageReport(IReadOnlyList<FilamentUsage> Usages, IReadOnlyDictionary<int, double[]> Curves, int FilamentCount = 0)
{
    public static readonly JobFilamentUsageReport Empty = new([], new Dictionary<int, double[]>());
}

/// <summary>
/// Reads the slicer's per-filament grams, and how each filament is spread over the layers, from the file actually sent
/// to the printer — plate 1, the plate <c>BambuPrintFile</c> dispatches. Anything unreadable yields less (no grams, or
/// no curve) rather than an error, so dispatch is never blocked: the job then deducts by an even spread, or is skipped and logged.
/// </summary>
internal static class JobFilamentUsageReader
{
    private const string SliceInfo = "Metadata/slice_info.config";
    private const string PlateGCode = "Metadata/plate_1.gcode";

    public static JobFilamentUsageReport Read(byte[] file)
    {
        try
        {
            if (file.Length >= 2 && file[0] == 'P' && file[1] == 'K')
            {
                using var archive = new ZipArchive(new MemoryStream(file), ZipArchiveMode.Read);
                var gcode = SlicedPlateMetadata.Find(archive, PlateGCode);
                var usages = SlicedPlateMetadata.Find(archive, SliceInfo) is { } entry ? ReadSliceInfo(entry) : [];
                if (usages.Count == 0 && gcode is not null)
                {
                    using var footerReader = new StreamReader(gcode.Open());
                    usages = ReadFooter(footerReader.ReadToEnd());
                }

                return new(usages, gcode is null ? JobFilamentUsageReport.Empty.Curves : ReadCurves(gcode.Open), ProjectFilamentCount(archive));
            }

            var footer = Encoding.UTF8.GetString(file);
            return new(ReadFooter(footer), ReadCurves(() => new MemoryStream(file)),
                GCodeMetadataHelper.ParseFilamentSlots(GCodeMetadataHelper.ParseComments(footer)).Count);
        }
        catch (Exception ex) when (ex is InvalidDataException or System.Xml.XmlException or IOException)
        {
            return JobFilamentUsageReport.Empty;
        }
    }

    /// <summary>How many filaments the project defines (used or not): the length the printer's mapping array must have.</summary>
    private static int ProjectFilamentCount(ZipArchive archive)
    {
        if (SlicedPlateMetadata.Find(archive, "Metadata/project_settings.config") is not { } entry) return 0;
        using var reader = new StreamReader(entry.Open());
        return ProjectSettingsMetadata.Read(reader.ReadToEnd()).Slots.Count;
    }

    private static IReadOnlyDictionary<int, double[]> ReadCurves(Func<Stream> open)
    {
        using var reader = new StreamReader(open());
        return GCodeFilamentCurveReader.Read(reader);
    }

    /// <summary>A slice_info filament <c>id</c> is 1-based; the tool the G-code selects with <c>T&lt;n&gt;</c> is id - 1.</summary>
    private static List<FilamentUsage> ReadSliceInfo(ZipArchiveEntry entry)
    {
        using var input = entry.Open();
        var plate = XDocument.Load(input).Descendants("plate").FirstOrDefault(p => p.Elements("metadata")
            .Any(m => (string?)m.Attribute("key") == "index" && (string?)m.Attribute("value") == "1"));
        if (plate is null) return [];
        return plate.Elements("filament")
            .Select(f => new FilamentUsage((string?)f.Attribute("type"), SlicedPlateMetadata.NormalizeColor((string?)f.Attribute("color")),
                Grams((string?)f.Attribute("used_g")), int.TryParse((string?)f.Attribute("id"), out var id) ? id - 1 : 0))
            .Where(u => u.Grams > 0).ToList();
    }

    /// <summary><c>filament used [g]</c> is parallel to <c>filament_type</c>/<c>filament_colour</c>, one entry per configured filament.</summary>
    private static List<FilamentUsage> ReadFooter(string content)
    {
        var values = GCodeMetadataHelper.ParseComments(content);
        if (!values.TryGetValue("filament used [g]", out var usedRaw)) return [];
        var slots = GCodeMetadataHelper.ParseFilamentSlots(values);
        var used = usedRaw.Split([',', ';'], StringSplitOptions.TrimEntries).Select(Grams).ToList();
        return slots.Zip(used, (slot, grams) => (slot, grams)).Select((p, i) => new FilamentUsage(p.slot.MaterialType, p.slot.DetectedColorHex, p.grams, i))
            .Where(u => u.Grams > 0).ToList();
    }

    private static decimal Grams(string? raw) =>
        decimal.TryParse(raw, NumberStyles.Float, CultureInfo.InvariantCulture, out var n) && n > 0 ? n : 0;
}
