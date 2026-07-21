using Microsoft.Extensions.Options;
using OpenFarm.Application.Parts;
using OpenFarm.Domain.Parts;

namespace OpenFarm.Infrastructure.Parts;

public sealed class SlicerDefaultsProvider(IOptions<SlicerOptions> options) : ISlicerDefaultsProvider
{
    public string GetGenericFallbackMaterialProfile(SlicerEngine engine) =>
        options.Value.GenericFallbackMaterialProfile.TryGetValue(engine, out var profile)
            ? profile
            : throw new InvalidOperationException($"No GenericFallbackMaterialProfile is configured for {engine}.");
}
