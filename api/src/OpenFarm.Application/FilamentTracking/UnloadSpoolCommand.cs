namespace OpenFarm.Application.FilamentTracking;

public enum UnloadSpoolAction
{
    ReturnToStorage,
    MarkFinished,
}

/// <param name="Reason">Why the spool was finished; kept with the write-off, and ignored when returning to storage.</param>
public sealed record UnloadSpoolCommand(Guid SpoolId, UnloadSpoolAction Action, string? Reason = null);
