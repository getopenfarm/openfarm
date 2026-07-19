namespace OpenFarm.Application.Parts;

/// <summary>Application-facing seam over the background service's queue — mirrors <c>IPrinterConnectionManager</c>'s relationship to <c>PrinterConnectionBackgroundService</c>.</summary>
public interface IPartProcessingQueue
{
    void Enqueue(PartProcessingWorkItem workItem);
}
