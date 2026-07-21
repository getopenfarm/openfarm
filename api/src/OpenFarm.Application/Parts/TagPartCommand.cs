namespace OpenFarm.Application.Parts;

public sealed record TagPartCommand(Guid PartId, string TagName);

public sealed record UntagPartCommand(Guid PartId, Guid TagId);

public sealed record PartTagsResult(IReadOnlyList<string> Tags);
