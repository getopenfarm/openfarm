using OpenFarm.Domain.Parts;

namespace OpenFarm.Application.Parts;

public interface IProfileEngineValidator
{
    /// <summary>Cross-source settings must successfully slice with the exact engine version before production.</summary>
    Task ValidateAsync(SlicerRef slicer, ResolvedSlicingProfiles profiles, string materialType,
        decimal nozzleDiameterMm, CancellationToken cancellationToken);
}
