namespace OpenFarm.Domain.Production;

public sealed class NamedRunCompletionConfirmationRequiredException(Guid runId)
    : Exception($"Confirm production run '{runId}' is complete before marking the printer ready.");
