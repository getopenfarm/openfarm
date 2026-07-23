using Microsoft.Extensions.Options;
using OpenFarm.Application.Parts;

namespace OpenFarm.Infrastructure.Parts;

public sealed class SlicerDefaultsProvider(IOptions<SlicerOptions> options) : ISlicerDefaultsProvider
{
    public string GetFallbackMaterialType() => options.Value.FallbackMaterialType;
}
