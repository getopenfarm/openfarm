using System.IO.Compression;
using OpenFarm.Application.Parts;
using OpenFarm.Domain.Parts;

namespace OpenFarm.Infrastructure.Parts;

// ZipArchive-based, patterned after ProfileBundleParser's proven technique (research.md §1) —
// synchronous, cheap I/O only (an extension check, or at most a single zip-entry-presence check).
public sealed class PartFileClassifier : IPartFileClassifier
{
    private const string SliceInfoConfigEntry = "Metadata/slice_info.config";

    public PartFileClassification Classify(Stream fileStream, string fileName)
    {
        var extension = Path.GetExtension(fileName).ToLowerInvariant();

        return extension switch
        {
            ".stl" => new PartFileClassification(PartFileFormat.Stl, RequiresSlicing: true),
            ".gcode" or ".gco" or ".g" => new PartFileClassification(PartFileFormat.GCode, RequiresSlicing: false),
            ".3mf" => new PartFileClassification(PartFileFormat.ThreeMf, RequiresSlicing: !HasSliceInfoConfig(fileStream)),
            _ => throw new UnsupportedPartFileFormatException(fileName),
        };
    }

    private static bool HasSliceInfoConfig(Stream fileStream)
    {
        ZipArchive archive;
        try
        {
            archive = new ZipArchive(fileStream, ZipArchiveMode.Read, leaveOpen: true);
        }
        catch (Exception ex) when (ex is InvalidDataException or ArgumentOutOfRangeException or IOException or EndOfStreamException)
        {
            throw new UnsupportedPartFileFormatException("The uploaded 3MF could not be read as a valid zip archive.");
        }

        using (archive)
        {
            return archive.GetEntry(SliceInfoConfigEntry) is not null;
        }
    }
}
