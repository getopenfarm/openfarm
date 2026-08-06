using OpenFarm.Domain.Assemblies;

namespace OpenFarm.Application.Assemblies;

public sealed record AddBomLineCommand(Guid AssemblyId, BomLineTargetKind TargetKind, Guid TargetId, decimal Quantity);
