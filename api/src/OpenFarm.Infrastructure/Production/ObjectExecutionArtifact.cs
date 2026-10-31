using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using OpenFarm.Domain.Production;

namespace OpenFarm.Infrastructure.Production;

public static partial class ObjectExecutionArtifact
{
    public static byte[] Prepare(byte[] bytes, int plate, PrintObjectExecution execution)
    {
        var actual = PrintObjectManifestReader.Read(bytes, execution.Manifest.RevisionHash, plate, execution.Manifest.Units.Count);
        if (actual.SkipUnavailableReason is not null) throw new InvalidOperationException(actual.SkipUnavailableReason);
        if (actual.DetectedCount != execution.Manifest.Units.Count || execution.Manifest.Units.Any(u => u.ObjectIds.Count != 1))
            throw new InvalidOperationException("This job has an obsolete object quantity or grouping. Cancel and recreate it after reprocessing the Part.");
        if (actual.Dialect != execution.Manifest.Dialect || !actual.Objects.Select(o => o.Id).Order().SequenceEqual(execution.Manifest.Objects.Select(o => o.Id).Order()))
            throw new InvalidOperationException("Sliced object identities changed. Review the complete-unit mapping before dispatch.");
        if (execution.Manifest.Dialect != "Klipper" || execution.PlannedExclusions.Count == 0) return bytes;
        if (bytes.Length > 1 && bytes[0] == 'P' && bytes[1] == 'K')
        {
            using var output = new MemoryStream();
            output.Write(bytes);
            using (var archive = new ZipArchive(output, ZipArchiveMode.Update, leaveOpen: true))
            {
                var path = $"Metadata/plate_{plate}.gcode";
                var entry = archive.GetEntry(path) ?? throw new InvalidOperationException("The selected plate has no G-code.");
                string code;
                using (var reader = new StreamReader(entry.Open())) code = reader.ReadToEnd();
                entry.Delete();
                using (var writer = new StreamWriter(archive.CreateEntry(path).Open(), new UTF8Encoding(false))) writer.Write(PrepareKlipper(code, execution.PlannedExclusions));
                // A changed entry must never retain its old integrity checksum.
                archive.GetEntry(path + ".md5")?.Delete();
            }
            return output.ToArray();
        }
        return Encoding.UTF8.GetBytes(PrepareKlipper(Encoding.UTF8.GetString(bytes), execution.PlannedExclusions));
    }

    public static string PrepareKlipper(string code, IReadOnlyList<string> ids)
    {
        if (ids.Any(id => !SafeName().IsMatch(id))) throw new InvalidOperationException("Unsafe object identifier.");
        var start = Regex.Match(code, @"(?m)^EXCLUDE_OBJECT_START NAME=[A-Za-z0-9_.-]+");
        var first = start.Success ? start.Index : -1;
        if (first < 0 || Regex.IsMatch(code[first..], @"(?mi)^\s*EXCLUDE_OBJECT(?:_DEFINE)?\b[^\r\n;]*\bRESET\s*=\s*1\b")
            || Regex.IsMatch(code[first..], @"(?mi)^\s*M486\b"))
            throw new InvalidOperationException("Object exclusion initialization cannot be verified for this G-code.");
        // Put exclusions immediately before the first object, after startup macros and file resets.
        return code.Insert(first, string.Join("\n", ids.Select(id => "EXCLUDE_OBJECT NAME=" + id)) + "\n");
    }

    public static string Hash(byte[] bytes) => Convert.ToHexString(SHA256.HashData(bytes));
    [GeneratedRegex(@"^[A-Za-z0-9_.-]+$", RegexOptions.CultureInvariant)]
    private static partial Regex SafeName();
}
