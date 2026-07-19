using FluentAssertions;
using OpenFarm.Application.Parts;
using OpenFarm.Application.Profiles;
using OpenFarm.Domain.Printers;
using OpenFarm.Domain.Profiles;

namespace OpenFarm.Application.Tests.Parts;

// TDD (research.md §12) — mode selection + deterministic tie-break, the same bar
// FeedUnitKindCatalog.Resolve/PrinterModelCatalog.Resolve are already held to.
public class NozzleDiameterResolverTests
{
    private static readonly FakeProfileBundleParser Parser = new();

    [Fact]
    public void Resolve_MultipleLinkedPrinters_ReturnsMostCommonNozzleDiameter()
    {
        var profile04 = CreateProfile("0.4");
        var profile06 = CreateProfile("0.6");
        var printers = new[]
        {
            CreatePrinter(profile04.Id),
            CreatePrinter(profile04.Id),
            CreatePrinter(profile06.Id),
        };

        var result = NozzleDiameterResolver.Resolve(printers, [profile04, profile06], Parser);

        result.Should().Be(0.4m);
    }

    [Fact]
    public void Resolve_TiedCounts_SmallestDiameterWins()
    {
        var profile04 = CreateProfile("0.4");
        var profile06 = CreateProfile("0.6");
        var printers = new[]
        {
            CreatePrinter(profile04.Id),
            CreatePrinter(profile06.Id),
        };

        var result = NozzleDiameterResolver.Resolve(printers, [profile04, profile06], Parser);

        result.Should().Be(0.4m);
    }

    [Fact]
    public void Resolve_NoPrinters_ReturnsNull()
    {
        var result = NozzleDiameterResolver.Resolve([], [], Parser);

        result.Should().BeNull();
    }

    [Fact]
    public void Resolve_NoPrinterHasLinkedProfile_ReturnsNull()
    {
        var printers = new[] { CreatePrinter(machineProfileId: null) };

        var result = NozzleDiameterResolver.Resolve(printers, [], Parser);

        result.Should().BeNull();
    }

    [Fact]
    public void Resolve_LinkedProfileDoesNotResolveOrHasNoNozzleSize_ExcludesThatPrinter()
    {
        var profileWithoutNozzle = CreateProfile(nozzleSizeMm: null);
        var danglingProfileId = Guid.NewGuid();
        var printers = new[]
        {
            CreatePrinter(profileWithoutNozzle.Id),
            CreatePrinter(danglingProfileId),
        };

        var result = NozzleDiameterResolver.Resolve(printers, [profileWithoutNozzle], Parser);

        result.Should().BeNull();
    }

    private static MachineProfile CreateProfile(string? nozzleSizeMm) =>
        MachineProfile.Create(Guid.NewGuid(), "Test Profile " + Guid.NewGuid(), ProfileSourceFormat.OrcaSlicer, null, nozzleSizeMm ?? "null");

    private static Printer CreatePrinter(Guid? machineProfileId)
    {
        var printer = Printer.Create(Guid.NewGuid(), "Printer " + Guid.NewGuid(), "Bambu", "X1C", "192.168.1.1", Guid.NewGuid().ToString());
        if (machineProfileId is { } id)
        {
            printer.LinkMachineProfile(id);
        }

        return printer;
    }

    // Encodes the nozzle size directly as RawDataJson ("null" for absent) so tests don't need real preset JSON.
    private sealed class FakeProfileBundleParser : IProfileBundleParser
    {
        public Task<ParsedProfileBundle> ParseAsync(Stream bundleStream, ProfileSourceFormat sourceFormat, CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public MachineProfileDisplayFields ExtractMachineDisplayFields(string rawDataJson, ProfileSourceFormat sourceFormat) =>
            new(
                NozzleSizeMm: rawDataJson == "null" ? null : double.Parse(rawDataJson),
                BedSizeXMm: null, BedSizeYMm: null, MaxHeightMm: null, HasEnclosure: null, SupportedMaterials: []);

        public FilamentProfileDisplayFields ExtractFilamentDisplayFields(string rawDataJson, ProfileSourceFormat sourceFormat, string name, bool hasBaseProfileName) =>
            throw new NotSupportedException();
    }
}
