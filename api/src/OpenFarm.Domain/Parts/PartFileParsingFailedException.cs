namespace OpenFarm.Domain.Parts;

/// <summary>An already-sliced file's embedded metadata (or an unsliced 3MF's project settings) could not be parsed (FR-006c).</summary>
public sealed class PartFileParsingFailedException(string reason) : Exception(reason);
