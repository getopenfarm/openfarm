namespace OpenFarm.Domain.Assemblies;

/// <summary>FR-002: an Assembly is never persisted with zero lines.</summary>
public sealed class AssemblyMustHaveAtLeastOneLineException()
    : Exception("An assembly must have at least one BOM line.");
