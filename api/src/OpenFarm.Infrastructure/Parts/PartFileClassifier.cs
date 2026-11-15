using System.IO.Compression;
using System.Xml;
using System.Xml.Linq;
using OpenFarm.Application.Parts;
using OpenFarm.Domain.Parts;

namespace OpenFarm.Infrastructure.Parts;

// ZipArchive-based, patterned after ProfileBundleParser's proven technique (research.md §1) —
// synchronous, cheap I/O only (an extension check, or at most a single zip-entry-presence check).
public sealed class PartFileClassifier : IPartFileClassifier
{


    public PartFileClassification Classify(Stream fileStream, string fileName)
    {
        var extension = Path.GetExtension(fileName).ToLowerInvariant();
        return extension switch
        {
            ".stl" => throw new UnsupportedPartFileFormatException(fileName,
                "STL files carry no slicing settings. Save the model as a project 3MF in OrcaSlicer or Bambu Studio."),
            ".gcode" or ".gco" or ".g" => new PartFileClassification(PartFileFormat.GCode, RequiresSlicing: false),
            ".3mf" => new PartFileClassification(PartFileFormat.ThreeMf, RequiresSlicing: !HasPlateGCode(fileStream, fileName)),
            _ => throw new UnsupportedPartFileFormatException(fileName),
        };
    }

    private static bool HasPlateGCode(Stream fileStream, string fileName)
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
            if (archive.Entries.Any(e => e.FullName.Replace('\\', '/').StartsWith("Metadata/plate_", StringComparison.OrdinalIgnoreCase)
                && e.FullName.EndsWith(".gcode", StringComparison.OrdinalIgnoreCase))) return true;
            var model = archive.GetEntry("3D/3dmodel.model")
                ?? archive.Entries.FirstOrDefault(e => e.FullName.StartsWith("3D/", StringComparison.OrdinalIgnoreCase)
                    && e.FullName.EndsWith(".model", StringComparison.OrdinalIgnoreCase));
            if (model is null) throw new UnsupportedPartFileFormatException("The 3MF contains neither model geometry nor supported plate G-code.");
            try
            {
                using var content = model.Open();
                using var reader = XmlReader.Create(content, new XmlReaderSettings { DtdProcessing = DtdProcessing.Prohibit, XmlResolver = null });
                var document = XDocument.Load(reader);
                if (document.Root?.Name.LocalName != "model" || !document.Descendants().Any(e => e.Name.LocalName == "build"))
                    throw new UnsupportedPartFileFormatException("The 3MF model is missing its model/build structure.");
            }
            catch (XmlException)
            {
                throw new UnsupportedPartFileFormatException("The 3MF model XML could not be read.");
            }
            // A file to slice carries its own machine, process and plate: a geometry-only 3MF has nothing to slice with.
            if (SlicedPlateMetadata.Find(archive, "Metadata/project_settings.config") is null)
                throw new UnsupportedPartFileFormatException(fileName,
                    "This 3MF carries no slicing settings. Save it as a project from OrcaSlicer or Bambu Studio, or upload the sliced file.");
            return false;
        }
    }
}
