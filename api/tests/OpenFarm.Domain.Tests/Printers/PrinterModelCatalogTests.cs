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
    }

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
}
