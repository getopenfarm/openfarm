namespace OpenFarm.Application.Production;

public sealed record CloseProductionRunCommand(Guid RunId, Guid? ActorId = null);
