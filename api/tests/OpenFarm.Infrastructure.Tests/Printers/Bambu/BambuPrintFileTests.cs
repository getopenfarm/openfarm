using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using FluentAssertions;
using OpenFarm.Infrastructure.Printers.Bambu;

namespace OpenFarm.Infrastructure.Tests.Printers.Bambu;

public class BambuPrintFileTests
{
    [Fact]
    public void PrepareProject_BareGCode_PreservesExactToolpathAndAddsMatchingChecksum()
    {
        var bytes = Encoding.UTF8.GetBytes("; nozzle_diameter = 0.4\r\n; 雪\r\nG28\r\nG1 X10 Y10\r\n");
        var result = BambuPrintFile.PrepareProject(bytes, "part.gcode", 1);
        using var archive = new ZipArchive(new MemoryStream(result));
        using var plate = archive.GetEntry(BambuPrintFile.PlatePath(1))!.Open();
        using var copy = new MemoryStream();
        plate.CopyTo(copy);
        copy.ToArray().Should().Equal(bytes);
        using var checksum = new StreamReader(archive.GetEntry($"{BambuPrintFile.PlatePath(1)}.md5")!.Open());
#pragma warning disable CA5351 // Check the protocol's per-plate MD5 against the unmodified G-code.
        checksum.ReadToEnd().Should().Be(Convert.ToHexStringLower(MD5.HashData(bytes)));
#pragma warning restore CA5351
    }

    [Theory]
    [InlineData("part.gcode.3mf")]
    [InlineData("part.gcode")]
    public void PrepareProject_ExistingArchive_PreservesAllEntriesAndBytes(string name)
    {
        using var output = new MemoryStream();
        using (var archive = new ZipArchive(output, ZipArchiveMode.Create, leaveOpen: true))
        {
            using (var plate = new StreamWriter(archive.CreateEntry(BambuPrintFile.PlatePath(1)).Open())) plate.Write("G28\n");
            using (var settings = new StreamWriter(archive.CreateEntry("Metadata/project_settings.config").Open())) settings.Write("{\"bed_type\":\"textured_plate\"}");
            using var thumbnail = archive.CreateEntry("Metadata/plate_1.png").Open();
            thumbnail.Write([1, 2, 3]);
        }
        var bytes = output.ToArray();
        BambuPrintFile.PrepareProject(bytes, name, 1).Should().Equal(bytes);
    }

    [Fact]
    public void PrepareProject_ExportedSinglePlate_IsValidatedAtItsOwnNumber()
    {
        // Bambu Studio / Orca "export plate sliced file" of plate 4 keeps the plate's number.
        using var output = new MemoryStream();
        using (var archive = new ZipArchive(output, ZipArchiveMode.Create, leaveOpen: true))
        using (var plate = new StreamWriter(archive.CreateEntry(BambuPrintFile.PlatePath(4)).Open())) plate.Write("G28\n");
        var bytes = output.ToArray();

        BambuPrintFile.PrepareProject(bytes, "part.gcode.3mf", 4).Should().Equal(bytes);
        var plateOne = () => BambuPrintFile.PrepareProject(bytes, "part.gcode.3mf", 1);
        plateOne.Should().Throw<IOException>().WithMessage("*Metadata/plate_1.gcode*");
    }

    [Fact]
    public void PrepareProject_CorruptThreeMf_DoesNotTreatItAsBareGCode()
    {
        var prepare = () => BambuPrintFile.PrepareProject(Encoding.UTF8.GetBytes("not a zip"), "broken.3mf", 1);
        prepare.Should().Throw<InvalidDataException>();
    }

    [Fact]
    public void PrepareProject_EmptyFile_IsRejected()
    {
        var prepare = () => BambuPrintFile.PrepareProject([], "empty.gcode", 1);
        prepare.Should().Throw<IOException>().WithMessage("*empty*");
    }
}
