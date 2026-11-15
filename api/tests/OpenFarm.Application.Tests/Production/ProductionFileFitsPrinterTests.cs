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

// The file is the single source of truth for how a part is sliced, and a pre-sliced file runs exactly as it is. Either way
// it only goes to a printer it was made for: the same model and the plate that is on the printer's bed.
public class ProductionFileFitsPrinterTests
{
    // What the check after the gate says: the printer has no confirmed nozzle yet. Reaching it means the gate passed.
    private const string NextStop = "Printer 'Bay 1' is waiting for nozzle telemetry";

    [Theory]
    [InlineData("Bambu", "P1S", "Bambu Lab P1S", BuildPlateType.TexturedPei, "Textured PEI Plate")]
    [InlineData("Bambu", "P1S", "C12", BuildPlateType.Cool, "Cool Plate")]
    [InlineData("Snapmaker", "U1", "Snapmaker U1", BuildPlateType.HighTemp, "High Temp Plate")]
    public async Task File_ForThisModelAndPlate_PassesTheGate(string brand, string modelCode, string model, BuildPlateType plate, string declaredPlate)
    {
        foreach (var requiresSlicing in new[] { false, true })
        {
            var printer = PrinterWith(brand, modelCode, plate);

            (await IssueAsync(printer, requiresSlicing, model, declaredPlate)).Should().StartWith(NextStop);
        }
    }

    [Theory]
    [InlineData("Snapmaker", "U1", "Bambu Lab P1S", "File is for Bambu P1S, not Snapmaker U1.")]
    [InlineData("Bambu", "P1S", "Snapmaker U1", "File is for Snapmaker U1, not Bambu P1S.")]
    [InlineData("Bambu", "X1C", "Bambu Lab P1S", "File is for Bambu P1S, not Bambu X1C.")]
    [InlineData("Bambu", "P1S", "Original Prusa MK4", "File is for Original Prusa MK4, not Bambu P1S.")]
    public async Task File_ForAnotherModel_IsBlocked(string brand, string modelCode, string declared, string expected)
    {
        foreach (var requiresSlicing in new[] { false, true })
        {
            var printer = PrinterWith(brand, modelCode, BuildPlateType.TexturedPei);

            (await IssueAsync(printer, requiresSlicing, declared, "Textured PEI Plate")).Should().Be(expected);
        }
    }

    [Fact]
    public async Task File_NamingNoModel_IsBlocked()
    {
        var printer = PrinterWith("Snapmaker", "U1", BuildPlateType.HighTemp);

        (await IssueAsync(printer, requiresSlicing: false, model: null, "High Temp Plate"))
            .Should().Be("The file names no printer model. Re-save it for Snapmaker U1.");
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task File_ForAnotherPlate_IsBlocked(bool requiresSlicing)
    {
        var printer = PrinterWith("Bambu", "P1S", BuildPlateType.TexturedPei);

        (await IssueAsync(printer, requiresSlicing, "Bambu Lab P1S", "Cool Plate")).Should().Be(
            $"File is for Cool Plate, not the printer's Textured PEI Plate. Change the plate in /printers/{printer.Id}?settings=slicer, or re-save the file for it.");
    }

    [Fact]
    public async Task File_ForAPlateOpenFarmDoesNotKnow_IsBlocked()
    {
        var printer = PrinterWith("Bambu", "P1S", BuildPlateType.TexturedPei);

        (await IssueAsync(printer, requiresSlicing: false, "Bambu Lab P1S", "Glass Plate")).Should().StartWith("File is for Glass Plate, not the printer's Textured PEI Plate.");
    }

    [Fact]
    public async Task File_NamingNoPlate_IsBlocked()
    {
        var printer = PrinterWith("Bambu", "P1S", BuildPlateType.TexturedPei);

        (await IssueAsync(printer, requiresSlicing: false, "Bambu Lab P1S", plate: null))
            .Should().Be("The file names no build plate. Re-save it for Textured PEI Plate.");
    }

    [Fact]
    public async Task PrinterWithNoPlateSelected_BlocksEveryFile()
    {
        var printer = Printer.Create("Bay 1", "Bambu", "P1S", "10.0.0.5", "SN-Bambu");

        (await IssueAsync(printer, requiresSlicing: false, "Bambu Lab P1S", "Textured PEI Plate"))
            .Should().Be($"Select a build plate. Configure in /printers/{printer.Id}?settings=slicer.");
    }

    [Fact]
    public async Task TheSimulator_StandsInForEveryModelAndPlate()
    {
        var printer = Printer.Create("Sim", "System", "Simulation", "10.0.0.5", "SN-SIM");

        (await IssueAsync(printer, requiresSlicing: false, "Bambu Lab P1S", "Cool Plate")).Should().StartWith("Printer 'Sim' is waiting for nozzle telemetry");
    }

    // The printers page warns about a printer with no plate; it must warn exactly where the gate would block every job.
    [Fact]
    public async Task TheSetupWarning_AndTheGate_AgreeOnWhichPrintersNeedAPlate()
    {
        var cases = new[]
        {
            (Printer: Printer.Create("Bay 1", "Bambu", "P1S", "10.0.0.5", "SN-A"), Needs: true),
            (Printer: PrinterWith("Bambu", "P1S", BuildPlateType.TexturedPei), Needs: false),
            (Printer: Printer.Create("Sim", "System", "Simulation", "10.0.0.5", "SN-SIM"), Needs: false),
        };

        foreach (var (printer, needs) in cases)
        {
            printer.NeedsBuildPlate.Should().Be(needs);
            printer.SetupIssues.Should().Equal(needs ? ["Select a build plate"] : []);
            var issue = await IssueAsync(printer, requiresSlicing: false, "Bambu Lab P1S", "Textured PEI Plate");
            (issue?.StartsWith("Select a build plate", StringComparison.Ordinal) ?? false).Should().Be(needs);
        }
    }

    private static Printer PrinterWith(string brand, string modelCode, BuildPlateType plate)
    {
        var printer = Printer.Create("Bay 1", brand, modelCode, "10.0.0.5", $"SN-{brand}");
        printer.SelectBuildPlate(plate);
        return printer;
    }

    private static async Task<string?> IssueAsync(Printer printer, bool requiresSlicing, string? model, string? plate)
    {
        var catalog = new TwoEngineCatalog();
        var printers = new FakePrinterRepository(printer);
        var compatibility = new ProfileCompatibility(catalog.Machines, catalog.Filaments);
        var resolver = new RepresentativeFilamentResolver(catalog.Filaments, new FakeSpoolRepository(), new FakeFilamentRepository(),
            new FakeFeedUnitRepository(), printers, compatibility);
        var declared = new EstimationDetails(requiresSlicing ? "EmbeddedProject" : "EmbeddedSlicedMetadata", model, BuildPlate: plate);
        var readiness = new ProductionProfileReadiness(resolver, printers, null!, compatibility, null!, new StubStorage(),
            new StubClassifier(), null!, new DeclaringSlicedParser(declared), new DeclaringProjectResolver(declared),
            NullLogger<ProductionProfileReadiness>.Instance);
        // The declared model and plate are remembered per revision, so every case needs its own file.
        var revisionHash = Guid.NewGuid().ToString("N") + Guid.NewGuid().ToString("N");
        var part = Part.Create("Part", requiresSlicing ? PartFileFormat.ThreeMf : PartFileFormat.GCode, requiresSlicing,
            "part.gcode", "/part.gcode", 10, 0.4m, null, null, EstimateStatus.Available, 10, 10, 1, 1, revisionHash,
            [new("PLA", "#000000", null, null, null)], [], []);

        return (await readiness.GetIssueAsync(printer.Id, part, [new JobFilamentRequirementSpec("PLA", "#000000")], default))?.Message;
    }

    private sealed class DeclaringSlicedParser(EstimationDetails declared) : IAlreadySlicedFileMetadataParser
    {
        public Task<SlicedFileMetadata> ParseAsync(Stream fileStream, PartFileFormat format, CancellationToken cancellationToken) =>
            Task.FromResult(new SlicedFileMetadata(0.4m, 10, 10, [], [], [], Estimation: declared));
    }

    private sealed class DeclaringProjectResolver(EstimationDetails declared) : IUnslicedProjectMaterialResolver
    {
        public Task<UnslicedProjectMaterial> ResolveAsync(Stream fileStream, CancellationToken cancellationToken) =>
            Task.FromResult(new UnslicedProjectMaterial("PLA", [], HasEmbeddedSettings: true, Estimation: declared));
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
