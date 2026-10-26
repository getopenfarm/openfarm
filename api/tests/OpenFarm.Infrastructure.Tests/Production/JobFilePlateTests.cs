using System.IO.Compression;
using System.Text;
using FluentAssertions;
using OpenFarm.Domain.Parts;
using OpenFarm.Infrastructure.Production;

namespace OpenFarm.Infrastructure.Tests.Production;

// Which plate of the file a dispatch prints: decided once, so the plate sent and the plate whose filament is read agree.
public class JobFilePlateTests
{
    [Fact]
    public void SeveralPlatesWithPlateOne_PrintsPlateOne() =>
        JobFileResolver.PlateToPrint(Project(1, 2)).Should().Be(1);

    [Fact]
    public void AnExportedSinglePlate_PrintsItsOwnNumber() =>
        JobFileResolver.PlateToPrint(Project(4)).Should().Be(4);

    [Fact]
    public void SeveralPlatesWithoutPlateOne_BlocksRatherThanGuess()
    {
        var act = () => JobFileResolver.PlateToPrint(Project(2, 3));

        act.Should().Throw<SlicingProfileUnconfiguredException>().WithMessage("The file holds plates 2, 3 and no plate 1.*");
    }

    [Fact]
    public void BareGCode_IsPlateOne() =>
        JobFileResolver.PlateToPrint(Encoding.UTF8.GetBytes("G28\n")).Should().Be(1);

    private static byte[] Project(params int[] plates)
    {
        using var zip = new MemoryStream();
        using (var archive = new ZipArchive(zip, ZipArchiveMode.Create, leaveOpen: true))
            foreach (var plate in plates)
            {
                using var writer = new StreamWriter(archive.CreateEntry($"Metadata/plate_{plate}.gcode").Open());
                writer.Write("G28\n");
            }

        return zip.ToArray();
    }
}
