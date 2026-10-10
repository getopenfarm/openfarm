using Microsoft.Extensions.Logging;
using OpenFarm.Application.Printers;
using OpenFarm.Infrastructure.Printers.Bambu;
using OpenFarm.Infrastructure.Printers.Simulator;

namespace OpenFarm.Infrastructure.Printers;

/// <summary>
/// Infrastructure implementation of <see cref="IPrinterConnectorFactory"/> — routes a printer to the
/// shared simulator singleton when its own Brand/ModelCode is the catalog's "System"/"Simulation" entry,
/// otherwise constructs a fresh <see cref="BambuLanConnector"/>/<see cref="BambuCameraRelay"/>
/// (research.md §1-2). A pure function of <paramref name="details"/> — no I/O, never throws for a
/// well-formed <see cref="PrinterConnectionDetails"/>.
/// </summary>
public sealed class PrinterConnectorFactory(
    SimulatorPrinterConnector simulatorConnector,
    SimulatorPrinterCameraRelay simulatorCameraRelay,
    ILoggerFactory loggerFactory) : IPrinterConnectorFactory
{
    public IPrinterConnector CreateConnector(PrinterConnectionDetails details) =>
        IsSimulated(details)
            ? simulatorConnector
            : new BambuLanConnector(loggerFactory.CreateLogger<BambuLanConnector>());

    public IPrinterCameraRelay CreateCameraRelay(PrinterConnectionDetails details) =>
        IsSimulated(details)
            ? simulatorCameraRelay
            : new BambuCameraRelay(loggerFactory.CreateLogger<BambuCameraRelay>());

    private static bool IsSimulated(PrinterConnectionDetails details) =>
        string.Equals(details.Brand, "System", StringComparison.OrdinalIgnoreCase)
        && string.Equals(details.ModelCode, "Simulation", StringComparison.OrdinalIgnoreCase);
}
