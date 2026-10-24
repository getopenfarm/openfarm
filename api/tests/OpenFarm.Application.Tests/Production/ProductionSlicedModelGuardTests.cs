using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using OpenFarm.Application.Parts;
using OpenFarm.Application.Production;
using OpenFarm.Application.Profiles;
using OpenFarm.Application.Tests.Profiles.Fakes;
using OpenFarm.Domain.Parts;
using OpenFarm.Domain.Printers;
using OpenFarm.Domain.Production;

namespace OpenFarm.Application.Tests.Production;

// A pre-sliced file runs exactly as it is, so it only goes to the model it was sliced for. A file that still needs
// slicing is sliced for whichever printer takes it.
public class ProductionSlicedModelGuardTests
{
    [Theory]
    [InlineData("Bambu", "P1S", "Bambu Lab P1S")]
    [InlineData("Bambu", "P1S", "C12")]
    [InlineData("Snapmaker", "U1", "Snapmaker U1")]
    public async Task PreSlicedFile_ForThisExactModel_PassesTheGuard(string brand, string modelCode, string declared)
    {
        var printer = Printer.Create("Bay 1", brand, modelCode, "10.0.0.5", $"SN-{brand}");

        var issue = await IssueAsync(printer, requiresSlicing: false, declared);

        // The next check stops it: this printer has no machine profile.
        issue.Should().StartWith("Printer 'Bay 1' needs a compatible machine profile.");
    }

    [Theory]
    [InlineData("Snapmaker", "U1", "Bambu Lab P1S", "Sliced for Bambu P1S, not Snapmaker U1.")]
    [InlineData("Bambu", "P1S", "Snapmaker U1", "Sliced for Snapmaker U1, not Bambu P1S.")]
    [InlineData("Bambu", "X1C", "Bambu Lab P1S", "Sliced for Bambu P1S, not Bambu X1C.")]
    [InlineData("Bambu", "P1S", "Original Prusa MK4", "Sliced for Original Prusa MK4, not Bambu P1S.")]
    public async Task PreSlicedFile_ForAnotherModel_IsBlocked(string brand, string modelCode, string declared, string expected)
    {
        var printer = Printer.Create("Bay 1", brand, modelCode, "10.0.0.5", $"SN-{brand}");

        (await IssueAsync(printer, requiresSlicing: false, declared)).Should().Be(expected);
    }

    [Fact]
    public async Task PreSlicedFile_NamingNoModel_IsBlocked()
    {
        var printer = Printer.Create("Bay 1", "Snapmaker", "U1", "10.0.0.5", "SN-U1");

        (await IssueAsync(printer, requiresSlicing: false, declared: null))
            .Should().Be("The file names no printer model. Slice it for Snapmaker U1.");
    }

    [Fact]
    public async Task FileThatNeedsSlicing_GoesToAnyModel()
    {
        var printer = Printer.Create("Bay 1", "Snapmaker", "U1", "10.0.0.5", "SN-U1");

        (await IssueAsync(printer, requiresSlicing: true, declared: "Bambu Lab P1S"))
            .Should().StartWith("Printer 'Bay 1' needs a compatible machine profile.");
    }

    [Fact]
    public async Task TheSimulator_StandsInForEveryModel()
    {
        var printer = Printer.Create("Sim", "System", "Simulation", "10.0.0.5", "SN-SIM");

        (await IssueAsync(printer, requiresSlicing: false, declared: "Bambu Lab P1S"))
            .Should().StartWith("Printer 'Sim' needs a compatible machine profile.");
    }

    private static async Task<string?> IssueAsync(Printer printer, bool requiresSlicing, string? declared)
    {
        var catalog = new TwoEngineCatalog();
        var printers = new FakePrinterRepository(printer);
        var compatibility = new ProfileCompatibility(catalog.Machines, catalog.Filaments);
        var resolver = new RepresentativeFilamentResolver(catalog.Filaments, new FakeSpoolRepository(), new FakeFilamentRepository(),
            new FakeFeedUnitRepository(), printers, catalog.Machines, compatibility);
        var readiness = new ProductionProfileReadiness(resolver, printers, null!, compatibility, null!, new StubStorage(),
            new StubClassifier(), null!, new DeclaringParser(declared), NullLogger<ProductionProfileReadiness>.Instance);
        // The declared model is remembered per revision, so every case needs its own file.
        var revisionHash = Guid.NewGuid().ToString("N") + Guid.NewGuid().ToString("N");
        var part = Part.Create("Part", requiresSlicing ? PartFileFormat.ThreeMf : PartFileFormat.GCode, requiresSlicing,
            "part.gcode", "/part.gcode", 10, 0.4m, null, null, EstimateStatus.Available, 10, 10, 1, 1, revisionHash,
            [new("PLA", "#000000", null, null, null)], [], []);

        return (await readiness.GetIssueAsync(printer.Id, part, [new JobFilamentRequirementSpec("PLA", "#000000")], default))?.Message;
    }

    private sealed class DeclaringParser(string? declared) : IAlreadySlicedFileMetadataParser
    {
        public Task<SlicedFileMetadata> ParseAsync(Stream fileStream, PartFileFormat format, CancellationToken cancellationToken) =>
            Task.FromResult(new SlicedFileMetadata(0.4m, 10, 10, 1, [], [], Estimation: new EstimationDetails("EmbeddedSlicedMetadata", declared)));
    }

    private sealed class StubClassifier : IPartFileClassifier
    {
        public PartFileClassification Classify(Stream fileStream, string fileName) => new(PartFileFormat.GCode, RequiresSlicing: false);
    }

    private sealed class StubStorage : IPartFileStorage
    {
        public Task<string> SaveAsync(string contentHash, string originalFileName, Stream content, CancellationToken cancellationToken) =>
            Task.FromResult(originalFileName);

        public Task<Stream> OpenReadAsync(string storagePath, CancellationToken cancellationToken) => Task.FromResult<Stream>(new MemoryStream([1]));

        public Task DeleteAsync(string storagePath, CancellationToken cancellationToken) => Task.CompletedTask;
    }
}
