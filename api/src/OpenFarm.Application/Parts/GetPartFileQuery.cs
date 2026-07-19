namespace OpenFarm.Application.Parts;

public sealed record GetPartFileQuery(Guid PartId);

public sealed record PartFileDownload(Stream Content, string OriginalFileName);
