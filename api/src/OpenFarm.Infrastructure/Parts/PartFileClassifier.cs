using System.IO.Compression;
using System.Buffers.Binary;
using System.Text.RegularExpressions;
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
        if (extension == ".stl") ValidateStl(fileStream);

        return extension switch
        {
            ".stl" => new PartFileClassification(PartFileFormat.Stl, RequiresSlicing: true),
            ".gcode" or ".gco" or ".g" => new PartFileClassification(PartFileFormat.GCode, RequiresSlicing: false),
            ".3mf" => new PartFileClassification(PartFileFormat.ThreeMf, RequiresSlicing: !HasPlateGCode(fileStream)),
            _ => throw new UnsupportedPartFileFormatException(fileName),
        };
    }

    private static void ValidateStl(Stream stream)
    {
        var position = stream.CanSeek ? stream.Position : 0;
        try
        {
            Span<byte> header = stackalloc byte[84];
            var read = stream.ReadAtLeast(header, 84, throwOnEndOfStream: false);
            if (read == 84 && stream.CanSeek)
            {
                var triangles = BinaryPrimitives.ReadUInt32LittleEndian(header[80..]);
                if (triangles > 0 && stream.Length - position == 84L + 50L * triangles) return;
            }
            if (!stream.CanSeek) throw new UnsupportedPartFileFormatException("STL validation requires a seekable upload.");
            stream.Position = position;
            using var reader = new StreamReader(stream, leaveOpen: true);
            var text = reader.ReadToEnd();
            if (!text.TrimStart().StartsWith("solid", StringComparison.OrdinalIgnoreCase)
                || !text.Contains("endsolid", StringComparison.OrdinalIgnoreCase)
                || Regex.Count(text, @"(?im)^\s*vertex\s+[-+0-9.e]+\s+[-+0-9.e]+\s+[-+0-9.e]+\s*$", RegexOptions.IgnoreCase) < 3)
                throw new UnsupportedPartFileFormatException("The STL contains no readable triangles.");
        }
        finally
        {
            if (stream.CanSeek) stream.Position = position;
        }
    }

    private static bool HasPlateGCode(Stream fileStream)
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
            return false;
        }
    }
}
