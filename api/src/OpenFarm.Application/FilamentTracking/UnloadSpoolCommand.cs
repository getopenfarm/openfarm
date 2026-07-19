namespace OpenFarm.Application.FilamentTracking;

public enum UnloadSpoolAction
{
    ReturnToStorage,
    MarkFinished,
}

public sealed record UnloadSpoolCommand(Guid SpoolId, UnloadSpoolAction Action);
