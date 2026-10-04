namespace OpenFarm.Application.Production;

public sealed record CancelProductionRunCommand(Guid RunId, Guid? ActorId = null);
