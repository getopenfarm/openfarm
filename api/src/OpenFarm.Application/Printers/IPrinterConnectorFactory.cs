namespace OpenFarm.Application.Printers;

/// <summary>
/// Resolves the <see cref="IPrinterConnector"/>/<see cref="IPrinterCameraRelay"/> that should serve a
/// given printer — chosen by its catalog model's <c>PrinterDriver</c>, a rule the Infrastructure implementation owns.
/// Callers never need to know which one they got (contracts/IPrinterConnectorFactory.md).
/// </summary>
public interface IPrinterConnectorFactory
{
    IPrinterConnector CreateConnector(PrinterConnectionDetails details);

    IPrinterCameraRelay CreateCameraRelay(PrinterConnectionDetails details);
}
