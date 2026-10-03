using OpenFarm.Domain.Production;

namespace OpenFarm.Application.Production;

public sealed record SetRunLineRevisionModeCommand(Guid RunLineId, RevisionMode RevisionMode);
