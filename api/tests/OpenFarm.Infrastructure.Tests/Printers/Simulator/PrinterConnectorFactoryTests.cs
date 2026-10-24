using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using OpenFarm.Application.Printers;
using OpenFarm.Infrastructure.Printers;
using OpenFarm.Infrastructure.Printers.Bambu;
using OpenFarm.Infrastructure.Printers.Simulator;
using OpenFarm.Infrastructure.Printers.Snapmaker;

namespace OpenFarm.Infrastructure.Tests.Printers.Simulator;

public class PrinterConnectorFactoryTests
{
    private static PrinterConnectorFactory CreateFactory(out SimulatorPrinterConnector simulatorConnector, out SimulatorPrinterCameraRelay simulatorCameraRelay)
    {
        simulatorConnector = new SimulatorPrinterConnector();
        simulatorCameraRelay = new SimulatorPrinterCameraRelay();
        return new PrinterConnectorFactory(simulatorConnector, simulatorCameraRelay, NullLoggerFactory.Instance);
    }

    [Fact]
    public void CreateConnector_SystemSimulationBrandModel_ReturnsSimulatorSingleton()
    {
        var factory = CreateFactory(out var simulatorConnector, out _);
        var details = new PrinterConnectionDetails("System", "Simulation", "10.0.0.1", "SN-1", "12345678");

        var connector = factory.CreateConnector(details);

        connector.Should().BeSameAs(simulatorConnector);
    }

    [Fact]
    public void CreateCameraRelay_SystemSimulationBrandModel_ReturnsSimulatorSingleton()
    {
        var factory = CreateFactory(out _, out var simulatorCameraRelay);
        var details = new PrinterConnectionDetails("System", "Simulation", "10.0.0.1", "SN-1", "12345678");

        var cameraRelay = factory.CreateCameraRelay(details);

        cameraRelay.Should().BeSameAs(simulatorCameraRelay);
    }

    [Fact]
    public void CreateConnector_LowerCaseSystemSimulationBrandModel_StillRoutesToSimulator()
    {
        var factory = CreateFactory(out var simulatorConnector, out _);
        var details = new PrinterConnectionDetails("system", "simulation", "10.0.0.1", "SN-1", "12345678");

        var connector = factory.CreateConnector(details);

        connector.Should().BeSameAs(simulatorConnector);
    }

    [Fact]
    public void CreateConnector_BambuBrand_ReturnsFreshBambuLanConnectorEachCall()
    {
        var factory = CreateFactory(out _, out _);
        var details = new PrinterConnectionDetails("Bambu", "P1S", "10.0.0.50", "SN-2", "12345678");

        var first = factory.CreateConnector(details);
        var second = factory.CreateConnector(details);

        first.Should().BeOfType<BambuLanConnector>();
        second.Should().BeOfType<BambuLanConnector>();
        first.Should().NotBeSameAs(second);
    }

    [Fact]
    public void CreateCameraRelay_BambuBrand_ReturnsFreshBambuCameraRelayEachCall()
    {
        var factory = CreateFactory(out _, out _);
        var details = new PrinterConnectionDetails("Bambu", "P1S", "10.0.0.50", "SN-2", "12345678");

        var first = factory.CreateCameraRelay(details);
        var second = factory.CreateCameraRelay(details);

        first.Should().BeOfType<BambuCameraRelay>();
        second.Should().BeOfType<BambuCameraRelay>();
        first.Should().NotBeSameAs(second);
    }

    [Theory]
    [InlineData("Snapmaker")]
    [InlineData("snapmaker")]
    public void CreateConnector_SnapmakerBrand_ReturnsFreshSnapmakerU1ConnectorEachCall(string brand)
    {
        var factory = CreateFactory(out _, out _);
        var details = new PrinterConnectionDetails(brand, "U1", "10.0.0.60", "SN-4", "");

        var first = factory.CreateConnector(details);
        var second = factory.CreateConnector(details);

        first.Should().BeOfType<SnapmakerU1Connector>();
        second.Should().BeOfType<SnapmakerU1Connector>();
        first.Should().NotBeSameAs(second);
    }

    [Theory]
    [InlineData("", "")]
    [InlineData("Unknown", "Widget9000")]
    [InlineData("System", "P1S")]
    [InlineData("Bambu", "Simulation")]
    public void CreateConnector_UnrecognizedOrMismatchedBrandModel_NeverThrowsAndRoutesToBambu(string brand, string modelCode)
    {
        var factory = CreateFactory(out _, out _);
        var details = new PrinterConnectionDetails(brand, modelCode, "10.0.0.50", "SN-3", "12345678");

        var act = () => factory.CreateConnector(details);

        act.Should().NotThrow();
        act().Should().BeOfType<BambuLanConnector>();
    }
}
