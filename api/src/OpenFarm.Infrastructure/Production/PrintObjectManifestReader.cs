using System.Globalization;
using System.IO.Compression;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using OpenFarm.Application.Parts;
using OpenFarm.Application.Production;
using OpenFarm.Domain.Parts;
using OpenFarm.Domain.Production;

namespace OpenFarm.Infrastructure.Production;

public sealed partial class PrintObjectManifestReader(IPartFileStorage storage) : IPrintObjectManifestReader
{
    public async Task<PrintObjectManifest> ReadAsync(Part part, CancellationToken cancellationToken)
    {
        await using var stream = await storage.OpenReadAsync(part.StoragePath, cancellationToken);
        using var buffer = new MemoryStream();
        await stream.CopyToAsync(buffer, cancellationToken);
        var detected = Read(buffer.ToArray(), part.RevisionHash, part.PlateNumber ?? 1, part.UnitsPerPrint);
        return detected;
    }

    public static PrintObjectManifest Read(byte[] bytes, string revision, int plate, int capacity)
    {
        string gcode;
        if (bytes.Length > 1 && bytes[0] == 'P' && bytes[1] == 'K')
        {
            using var archive = new ZipArchive(new MemoryStream(bytes));
            var entry = archive.GetEntry($"Metadata/plate_{plate}.gcode");
            gcode = entry is null ? "" : Text(entry);
        }
        else gcode = Encoding.UTF8.GetString(bytes);

        using var source = new MemoryStream(bytes);
        var inventory = PartObjectInventory.Read(source, bytes.Length > 1 && bytes[0] == 'P' && bytes[1] == 'K' ? "plate.3mf" : "plate.gcode", plate);
        var dialect = inventory.Dialect;
        var outlines = new Dictionary<string, decimal[][]>(StringComparer.Ordinal);
        foreach (Match match in KlipperOutline().Matches(gcode))
        {
            try
            {
                var polygon = JsonSerializer.Deserialize<decimal[][]>(match.Groups[2].Value);
                if (polygon is { Length: >= 3 and <= 1000 } && polygon.All(p => p.Length == 2)) outlines[match.Groups[1].Value.ToUpperInvariant()] = polygon;
            }
            catch (JsonException) { /* Optional presentation metadata never determines object identity. */ }
        }
        var objects = inventory.Objects.OrderBy(o => o.Id, StringComparer.Ordinal)
            .Select(o => new PrintObject(o.Id, o.Name, outlines.GetValueOrDefault(o.Id))).ToArray();
        IReadOnlyList<PrintUnit> units = inventory.SkipUnavailableReason is null && objects.Length == inventory.Count
            ? objects.Select((o, i) => new PrintUnit((i + 1).ToString(CultureInfo.InvariantCulture), [o.Id])).ToArray() : [];
        return new(revision, plate, dialect, objects, units, inventory.Count, inventory.Source, inventory.SkipUnavailableReason);
    }

    private static string Text(ZipArchiveEntry entry) { using var reader = new StreamReader(entry.Open()); return reader.ReadToEnd(); }
    [GeneratedRegex(@"(?m)^EXCLUDE_OBJECT_DEFINE\s+NAME=([A-Za-z0-9_.-]+)[^\r\n]*?POLYGON=(\[\[[^\r\n]+\]\])", RegexOptions.CultureInvariant)]
    private static partial Regex KlipperOutline();
}
