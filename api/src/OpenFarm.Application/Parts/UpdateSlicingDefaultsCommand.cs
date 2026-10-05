using OpenFarm.Domain.Parts;

namespace OpenFarm.Application.Parts;

public sealed record UpdateSlicingDefaultsCommand(SlicerEngine Engine, string Version, EstimationProfileSelection? EstimationProfiles = null, bool UpdateEstimationProfiles = false);
