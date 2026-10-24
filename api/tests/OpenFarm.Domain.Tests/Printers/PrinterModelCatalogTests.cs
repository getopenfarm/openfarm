using FluentAssertions;
using OpenFarm.Domain.Printers;

namespace OpenFarm.Domain.Tests.Printers;

public class PrinterModelCatalogTests
{
    [Theory]
    [InlineData("X1", CameraTransport.Rtsp)]
    [InlineData("X1C", CameraTransport.Rtsp)]
    [InlineData("X1E", CameraTransport.Rtsp)]
    [InlineData("P1P", CameraTransport.BinaryImage)]
    [InlineData("P1S", CameraTransport.BinaryImage)]
    [InlineData("A1", CameraTransport.BinaryImage)]
    [InlineData("A1MINI", CameraTransport.BinaryImage)]
    public void Resolve_WithKnownBambuModel_ReturnsExpectedCameraTransport(string modelCode, CameraTransport expected)
    {
        var model = PrinterModelCatalog.Resolve("Bambu", modelCode);

        model.Brand.Should().Be(PrinterBrand.Bambu);
        model.ModelCode.Should().Be(modelCode);
        model.CameraTransport.Should().Be(expected);
    }

    [Theory]
    [InlineData("Bambu", "X2D")]
    [InlineData("Bambu", "H2D")]
    [InlineData("Prusa", "MK4")]
    [InlineData("Bambu", "NotARealModel")]
    public void Resolve_WithUnrecognizedCombination_Throws(string brand, string modelCode)
    {
        var act = () => PrinterModelCatalog.Resolve(brand, modelCode);

        act.Should().Throw<UnknownPrinterModelException>()
            .Which.Message.Should().Contain(modelCode);
    }

    [Fact]
    public void TryResolve_WithKnownModel_ReturnsTrueAndModel()
    {
        var resolved = PrinterModelCatalog.TryResolve("Bambu", "P1S", out var model);

        resolved.Should().BeTrue();
        model.Should().NotBeNull();
        model!.CameraTransport.Should().Be(CameraTransport.BinaryImage);
    }

    [Fact]
    public void TryResolve_WithUnknownModel_ReturnsFalse()
    {
        var resolved = PrinterModelCatalog.TryResolve("Bambu", "H2D", out var model);

        resolved.Should().BeFalse();
        model.Should().BeNull();
    }

    [Fact]
    public void All_ReturnsEveryEntryGroupedByBrand()
    {
        var all = PrinterModelCatalog.All;

        all.Should().ContainSingle(g => g.Brand == PrinterBrand.Bambu)
            .Which.Models.Should().BeEquivalentTo(["X1", "X1C", "X1E", "P1P", "P1S", "A1", "A1MINI"]);
        all.Should().ContainSingle(g => g.Brand == PrinterBrand.System)
            .Which.Models.Should().BeEquivalentTo(["Simulation"]);
        all.Should().ContainSingle(g => g.Brand == PrinterBrand.Snapmaker)
            .Which.Models.Should().BeEquivalentTo(["U1"]);
    }

    [Fact]
    public void Resolve_SnapmakerU1_IsLiveOnTheHighTempPlateWithItsOrcaMachineModel()
    {
        var model = PrinterModelCatalog.Resolve("Snapmaker", "U1");

        model.Brand.Should().Be(PrinterBrand.Snapmaker);
        model.CameraTransport.Should().Be(CameraTransport.HttpSnapshot);
        model.CompatiblePlateTypes.Should().BeEquivalentTo([BuildPlateType.HighTemp]);
        // OrcaSlicer's Snapmaker vendor index names the model this way; ingestion pulls the vendor in through it.
        model.VendorMachineModelName.Should().Be("Snapmaker U1");
        PrinterModelCatalog.AllVendorMachineModelNames.Should().Contain("Snapmaker U1");
        PrinterModelCatalog.SupportsLiveIntegration("Snapmaker").Should().BeTrue();
    }

    [Theory]
    [InlineData("Bambu Lab P1S", "Bambu", "P1S")]
    [InlineData("bambu lab x1 carbon", "Bambu", "X1C")]
    [InlineData("C12", "Bambu", "P1S")]
    [InlineData("Snapmaker U1", "Snapmaker", "U1")]
    [InlineData(" Snapmaker U1 ", "Snapmaker", "U1")]
    public void TryResolveSlicedModel_MapsWhatASlicedFileDeclares(string declared, string brand, string modelCode)
    {
        var model = PrinterModelCatalog.TryResolveSlicedModel(declared);

        model.Should().NotBeNull();
        model!.Brand.ToString().Should().Be(brand);
        model.ModelCode.Should().Be(modelCode);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("Original Prusa MK4")]
    [InlineData("MK4")]
    public void TryResolveSlicedModel_UnknownOrMissing_IsNull(string? declared) =>
        PrinterModelCatalog.TryResolveSlicedModel(declared).Should().BeNull();

    [Theory]
    [InlineData("Bambu", true)]
    [InlineData("System", true)]
    [InlineData("Unknown", true)]
    [InlineData("Snapmaker", false)]
    [InlineData("snapmaker", false)]
    public void UsesAccessCode_IsFalseOnlyForSnapmaker(string brand, bool expected) =>
        PrinterModelCatalog.UsesAccessCode(brand).Should().Be(expected);

    [Fact]
    public void All_EveryEntryResolvesViaResolve()
    {
        var all = PrinterModelCatalog.All;

        foreach (var group in all)
        {
            foreach (var modelCode in group.Models)
            {
                var act = () => PrinterModelCatalog.Resolve(group.Brand.ToString(), modelCode);
                act.Should().NotThrow();
            }
        }
    }

    // --- 012 US1: build plate compatibility (TDD, plan.md's Constitution Check / research.md R8) ---

    [Theory]
    [InlineData("A1")]
    [InlineData("A1MINI")]
    public void Resolve_A1Series_CompatiblePlateTypesIsExactlyTexturedPeiAndHighTemp(string modelCode)
    {
        var model = PrinterModelCatalog.Resolve("Bambu", modelCode);

        model.CompatiblePlateTypes.Should().BeEquivalentTo([BuildPlateType.TexturedPei, BuildPlateType.HighTemp]);
        model.CompatiblePlateTypes.Should().NotContain(BuildPlateType.Cool);
        model.CompatiblePlateTypes.Should().NotContain(BuildPlateType.Engineering);
        model.CompatiblePlateTypes.Should().NotContain(BuildPlateType.Supertack);
        model.CompatiblePlateTypes.Should().NotContain(BuildPlateType.TexturedCool);
    }

    [Theory]
    [InlineData("X1")]
    [InlineData("X1C")]
    [InlineData("X1E")]
    [InlineData("P1P")]
    [InlineData("P1S")]
    public void Resolve_XAndP1Series_CompatiblePlateTypesIsCoolHighTempTexturedPeiAndSupertack(string modelCode)
    {
        var model = PrinterModelCatalog.Resolve("Bambu", modelCode);

        model.CompatiblePlateTypes.Should().BeEquivalentTo(
            [BuildPlateType.Cool, BuildPlateType.HighTemp, BuildPlateType.TexturedPei, BuildPlateType.Supertack]);
        model.CompatiblePlateTypes.Should().NotContain(BuildPlateType.Engineering);
        model.CompatiblePlateTypes.Should().NotContain(BuildPlateType.TexturedCool);
    }

    [Fact]
    public void Resolve_SystemSimulation_CompatiblePlateTypesContainsAllSixValues()
    {
        var model = PrinterModelCatalog.Resolve("System", "Simulation");

        model.CompatiblePlateTypes.Should().BeEquivalentTo(Enum.GetValues<BuildPlateType>());
    }

    [Fact]
    public void All_EveryEntryHasANonNullNonEmptyCompatiblePlateTypesSet()
    {
        var all = PrinterModelCatalog.All;

        foreach (var group in all)
        {
            foreach (var modelCode in group.Models)
            {
                var model = PrinterModelCatalog.Resolve(group.Brand.ToString(), modelCode);
                model.CompatiblePlateTypes.Should().NotBeNull().And.NotBeEmpty();
            }
        }
    }

    [Theory]
    [InlineData("C12", "P1S")]
    [InlineData("c11", "P1P")]
    [InlineData("N2S", "A1")]
    [InlineData("N1", "A1MINI")]
    [InlineData("BL-P001", "X1C")]
    [InlineData("ZZZ", null)]
    public void TryMapBambuDeviceModel_MapsSsdpCodesToCatalogCodes(string deviceModel, string? expected)
    {
        var mapped = PrinterModelCatalog.TryMapBambuDeviceModel(deviceModel);

        mapped.Should().Be(expected);
        if (mapped is not null)
        {
            PrinterModelCatalog.TryResolve("Bambu", mapped, out _).Should().BeTrue();
        }
    }
}
