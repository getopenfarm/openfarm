namespace OpenFarm.Domain.Production;

public sealed class InvalidPrintJobStatusTransitionException(PrintJobStatus from, PrintJobStatus to)
    : Exception($"Cannot transition a print job from '{from}' to '{to}'.")
{
    public PrintJobStatus From { get; } = from;

    public PrintJobStatus To { get; } = to;
}
