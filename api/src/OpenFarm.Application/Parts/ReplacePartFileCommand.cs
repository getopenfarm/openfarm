using OpenFarm.Domain.Parts;

namespace OpenFarm.Application.Parts;

public sealed record ReplacePartFileCommand(Guid PartId, Stream File, string FileName, SlicerEngine? SlicerEngineOverride, Guid? RequestedByUserId);
