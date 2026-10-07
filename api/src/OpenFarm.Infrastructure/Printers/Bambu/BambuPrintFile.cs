using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;

namespace OpenFarm.Infrastructure.Printers.Bambu;

/// <summary>Builds the project archive required by Bambu's print.project_file command.</summary>
internal static class BambuPrintFile
{
    internal const string PlatePath = "Metadata/plate_1.gcode";

    public static byte[] PrepareProject(byte[] content, string fileName)
    {
        if (content.Length == 0) throw new IOException("The print file is empty.");
        // Inspect bytes as well as the name: production output may have an older .gcode label.
        if (fileName.EndsWith(".3mf", StringComparison.OrdinalIgnoreCase)
            || content.Length >= 2 && content[0] == 'P' && content[1] == 'K')
        {
            using var input = new MemoryStream(content);
            using var archive = new ZipArchive(input, ZipArchiveMode.Read);
            if (archive.GetEntry(PlatePath) is not { Length: > 0 })
                throw new IOException($"The print project must contain {PlatePath}. Export a sliced plate from Bambu Studio or OrcaSlicer.");
            return content;
        }

        // Keep bare G-code bytes intact, but package them rather than merely renaming them .3mf.
        // This follows bambulabs_api's documented Start a Print example.
        using var output = new MemoryStream();
        using (var archive = new ZipArchive(output, ZipArchiveMode.Create, leaveOpen: true))
        {
            using (var entry = archive.CreateEntry(PlatePath).Open()) entry.Write(content);
#pragma warning disable CA5351 // Bambu's per-plate integrity checksum, not a security primitive.
            var md5 = Convert.ToHexStringLower(MD5.HashData(content));
#pragma warning restore CA5351
            using var checksum = archive.CreateEntry($"{PlatePath}.md5").Open();
            checksum.Write(Encoding.ASCII.GetBytes(md5));
        }
        return output.ToArray();
    }
}
