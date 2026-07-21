namespace OpenFarm.Domain.Parts;

/// <summary>An unsliced 3MF's own project-level filament assignment could not be parsed (research.md §13, FR-006).</summary>
public sealed class UnslicedProjectMaterialParsingFailedException(string reason) : Exception(reason);
