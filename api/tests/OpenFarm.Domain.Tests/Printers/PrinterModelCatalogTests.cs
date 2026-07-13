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
}
