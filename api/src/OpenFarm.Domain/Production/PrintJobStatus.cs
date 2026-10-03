namespace OpenFarm.Domain.Production;

public enum PrintJobStatus
{
    Queued,
    Printing,
    Paused,
    Failed,
    Completed,
    Cancelled,
}
