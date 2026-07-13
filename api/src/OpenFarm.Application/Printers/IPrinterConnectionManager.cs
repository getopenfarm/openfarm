namespace OpenFarm.Application.Printers;

/// <summary>
/// Application-facing handle onto the Infrastructure-owned connection lifecycle
/// (<c>PrinterConnectionBackgroundService</c>) — lets command handlers start/stop a printer's live
/// connection without depending on the Infrastructure type directly.
/// </summary>
public interface IPrinterConnectionManager
{
    void StartConnection(Guid printerId, CancellationToken cancellationToken);

    void StopConnection(Guid printerId);
}
