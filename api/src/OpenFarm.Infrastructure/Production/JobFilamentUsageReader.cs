using System.Globalization;
using System.IO.Compression;
using System.Text;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using OpenFarm.Application.Production;
using OpenFarm.Domain.Parts;
using OpenFarm.Infrastructure.Parts;

namespace OpenFarm.Infrastructure.Production;

/// <summary>
/// What the dispatched file says about filament: per-tool grams (the slicer's, plus the purge it leaves out) and per-tool
/// extrusion by layer (mm).
/// </summary>
internal sealed record JobFilamentUsageReport(IReadOnlyList<FilamentUsage> Usages, GCodeExtrusion Extrusion, int FilamentCount = 0)
{
    public static readonly JobFilamentUsageReport Empty = new([], GCodeExtrusion.Empty);
}

/// <summary>
/// Reads the slicer's per-filament grams, and how each filament is spread over the layers, from the file actually sent
/// to the printer — plate 1, the plate <c>BambuPrintFile</c> dispatches. Anything unreadable yields less (no grams, or
/// no curve) rather than an error, so dispatch is never blocked: the job then deducts by an even spread, or is skipped and logged.
/// </summary>
internal static partial class JobFilamentUsageReader
{
    private const string SliceInfo = "Metadata/slice_info.config";

    [GeneratedRegex(@"(?:^|/)Metadata/plate_(\d+)\.gcode$", RegexOptions.IgnoreCase)]
    private static partial Regex PlateGCodeEntry();

    /// <summary>Plate numbers that have sliced G-code in a 3mf, in ascending order; empty for anything else.</summary>
    public static IReadOnlyList<int> PlatesWithGCode(byte[] file)
    {
        if (file.Length < 2 || file[0] != 'P' || file[1] != 'K') return [];
        try
        {
            using var archive = new ZipArchive(new MemoryStream(file), ZipArchiveMode.Read);
            return archive.Entries
                .Select(e => PlateGCodeEntry().Match(e.FullName))
                .Where(m => m.Success)
                .Select(m => int.Parse(m.Groups[1].Value, CultureInfo.InvariantCulture))
                .Distinct().Order().ToList();
        }
        catch (InvalidDataException)
        {
            return [];
        }
    }

    /// <summary>
    /// Which plate a file read back from a printer was printed from. A file exported for one plate keeps that plate's number
    /// (<c>Metadata/plate_4.gcode</c>) and the printer's <c>gcode_file</c> does not always name it, so the file is asked:
    /// a plate the printer names wins if the file has it, else the only plate with G-code. Null when the file holds several
    /// plates and the printed one cannot be told, so no one plate's filament is guessed.
    /// </summary>
    public static int? ChoosePlate(byte[] file, int? namedPlate)
    {
        var plates = PlatesWithGCode(file);
        if (plates.Count == 0) return namedPlate ?? 1;
        if (namedPlate is { } named && plates.Contains(named)) return named;
        return plates.Count == 1 ? plates[0] : null;
    }

    /// <summary>Dispatch always sends plate 1; an external print names its own plate.</summary>
    public static JobFilamentUsageReport Read(byte[] file, int plate = 1)
    {
        try
        {
            if (file.Length >= 2 && file[0] == 'P' && file[1] == 'K')
            {
                using var archive = new ZipArchive(new MemoryStream(file), ZipArchiveMode.Read);
                return Read(archive, plate);
            }

            var footer = Encoding.UTF8.GetString(file);
            var rawExtrusion = ReadExtrusion(() => new MemoryStream(file));
            return new(WithPurge(ReadFooter(footer), rawExtrusion), rawExtrusion,
                GCodeMetadataHelper.ParseFilamentSlots(GCodeMetadataHelper.ParseComments(footer)).Count);
        }
        catch (Exception ex) when (ex is InvalidDataException or System.Xml.XmlException or IOException)
        {
            return JobFilamentUsageReport.Empty;
        }
    }

    /// <summary><see cref="Read(byte[], int)"/> for a 3mf that is already open.</summary>
    public static JobFilamentUsageReport Read(ZipArchive archive, int plate)
    {
        var gcode = SlicedPlateMetadata.Find(archive, $"Metadata/plate_{plate}.gcode");
        var usages = SlicedPlateMetadata.Find(archive, SliceInfo) is { } entry ? ReadSliceInfo(entry, plate) : [];
        if (usages.Count == 0 && gcode is not null)
        {
            using var footerReader = new StreamReader(gcode.Open());
            usages = ReadFooter(footerReader.ReadToEnd());
        }

        var extrusion = gcode is null ? GCodeExtrusion.Empty : ReadExtrusion(gcode.Open);
        return new(WithPurge(usages, extrusion), extrusion, ProjectFilamentCount(archive));
    }

    /// <summary>
    /// Per-filament grams of every sliced plate, purge included, summed by (material, colour): what one print of the
    /// whole file (one plate set) draws from the spools. Empty when the file says nothing about filament usage.
    /// </summary>
    public static List<FilamentGrams> ReadAllPlates(byte[] file)
    {
        IEnumerable<JobFilamentUsageReport> reports;
        if (file.Length >= 2 && file[0] == 'P' && file[1] == 'K')
        {
            try
            {
                using var archive = new ZipArchive(new MemoryStream(file), ZipArchiveMode.Read);
                reports = PlatesWithGCode(file).Select(plate => Read(archive, plate)).ToList();
            }
            catch (Exception ex) when (ex is InvalidDataException or System.Xml.XmlException or IOException) { return []; }
        }
        else reports = [Read(file)];

        return Sum(reports);
    }

    /// <summary>Sums per-plate reports by (material, colour).</summary>
    public static List<FilamentGrams> Sum(IEnumerable<JobFilamentUsageReport> reports)
    {
        return reports.SelectMany(r => r.Usages).GroupBy(u => (u.MaterialType?.ToUpperInvariant(), u.ColorHex?.ToUpperInvariant()))
            .Select(g => new FilamentGrams(g.First().MaterialType, g.First().ColorHex, g.Sum(u => u.Grams))).ToList();
    }

    /// <summary>How many filaments the project defines (used or not): the length the printer's mapping array must have.</summary>
    private static int ProjectFilamentCount(ZipArchive archive)
    {
        if (SlicedPlateMetadata.Find(archive, "Metadata/project_settings.config") is not { } entry) return 0;
        using var reader = new StreamReader(entry.Open());
        return ProjectSettingsMetadata.Read(reader.ReadToEnd()).Slots.Count;
    }

    private static GCodeExtrusion ReadExtrusion(Func<Stream> open)
    {
        using var reader = new StreamReader(open());
        return GCodeFilamentCurveReader.Read(reader);
    }

    /// <summary>
    /// The slicer's grams leave out the purge its machine start G-code extrudes before layer 1, but that filament leaves
    /// the spool too. Price it at the slicer's own grams per mm, which carries the file's density, diameter and flow ratio.
    /// </summary>
    private static List<FilamentUsage> WithPurge(IReadOnlyList<FilamentUsage> usages, GCodeExtrusion extrusion) =>
        usages.Select(u => extrusion.Tools.TryGetValue(u.ToolIndex, out var tool) && tool.CumulativeMm[^1] > 0
            ? u with { Grams = u.Grams + u.Grams * (decimal)(tool.PrimeMm / tool.CumulativeMm[^1]) }
            : u).ToList();

    /// <summary>A slice_info filament <c>id</c> is 1-based; the tool the G-code selects with <c>T&lt;n&gt;</c> is id - 1.</summary>
    private static List<FilamentUsage> ReadSliceInfo(ZipArchiveEntry entry, int plateIndex)
    {
        using var input = entry.Open();
        var plate = XDocument.Load(input).Descendants("plate").FirstOrDefault(p => p.Elements("metadata")
            .Any(m => (string?)m.Attribute("key") == "index" && (string?)m.Attribute("value") == plateIndex.ToString(CultureInfo.InvariantCulture)));
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
