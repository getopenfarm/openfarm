namespace OpenFarm.Application.Assemblies;

public sealed record CalculateBuildableUnitsQuery(Guid AssemblyId, IReadOnlyDictionary<Guid, decimal> GoodQuantities);
