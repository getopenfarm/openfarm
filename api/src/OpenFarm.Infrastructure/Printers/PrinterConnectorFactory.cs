using Microsoft.Extensions.Logging;
using OpenFarm.Application.Printers;
using OpenFarm.Domain.Printers;
using OpenFarm.Infrastructure.Printers.Bambu;
using OpenFarm.Infrastructure.Printers.Simulator;
using OpenFarm.Infrastructure.Printers.Snapmaker;

namespace OpenFarm.Infrastructure.Printers;

/// <summary>
/// Infrastructure implementation of <see cref="IPrinterConnectorFactory"/> — routes a printer by its catalog model's
/// <see cref="PrinterDriver"/>: the shared simulator singleton, a fresh <see cref="SnapmakerU1Connector"/>, or a fresh
/// <see cref="BambuLanConnector"/>/<see cref="BambuCameraRelay"/> (research.md §1-2). A pure function of
/// <paramref name="details"/> — no I/O. A model the catalog doesn't know throws <see cref="UnknownPrinterModelException"/>
/// rather than being guessed at.
/// </summary>
public sealed class PrinterConnectorFactory(
    SimulatorPrinterConnector simulatorConnector,
    SimulatorPrinterCameraRelay simulatorCameraRelay,
    ILoggerFactory loggerFactory) : IPrinterConnectorFactory
{
    public IPrinterConnector CreateConnector(PrinterConnectionDetails details) => DriverOf(details) switch
    {
        PrinterDriver.Simulator => simulatorConnector,
        PrinterDriver.SnapmakerMoonraker => new SnapmakerU1Connector(loggerFactory.CreateLogger<SnapmakerU1Connector>()),
        PrinterDriver.BambuLan => new BambuLanConnector(loggerFactory.CreateLogger<BambuLanConnector>()),
        var driver => throw new NotSupportedException($"No connector for driver {driver}."),
    };

    public IPrinterCameraRelay CreateCameraRelay(PrinterConnectionDetails details) => DriverOf(details) switch
    {
        PrinterDriver.Simulator => simulatorCameraRelay,
        PrinterDriver.SnapmakerMoonraker => new SnapmakerU1CameraRelay(loggerFactory.CreateLogger<SnapmakerU1CameraRelay>()),
        PrinterDriver.BambuLan => new BambuCameraRelay(loggerFactory.CreateLogger<BambuCameraRelay>()),
        var driver => throw new NotSupportedException($"No camera relay for driver {driver}."),
    };

    private static PrinterDriver DriverOf(PrinterConnectionDetails details) =>
        PrinterModelCatalog.Resolve(details.Brand, details.ModelCode).Driver;
}
