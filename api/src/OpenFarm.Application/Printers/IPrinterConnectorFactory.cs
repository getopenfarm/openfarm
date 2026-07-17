namespace OpenFarm.Application.Printers;

/// <summary>
/// Resolves the <see cref="IPrinterConnector"/>/<see cref="IPrinterCameraRelay"/> that should serve a
/// given printer — the real Bambu LAN implementations for an ordinary printer, or the built-in
/// simulator when the printer's own Brand/ModelCode is the catalog's "System"/"Simulation" entry, a
/// rule the Infrastructure implementation owns. Callers never need to know which one they got
/// (contracts/IPrinterConnectorFactory.md).
/// </summary>
public interface IPrinterConnectorFactory
{
    IPrinterConnector CreateConnector(PrinterConnectionDetails details);

    IPrinterCameraRelay CreateCameraRelay(PrinterConnectionDetails details);
}
