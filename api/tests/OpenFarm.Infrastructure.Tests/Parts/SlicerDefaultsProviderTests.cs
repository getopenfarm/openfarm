using FluentAssertions;
using Microsoft.Extensions.Options;
using OpenFarm.Infrastructure.Parts;

namespace OpenFarm.Infrastructure.Tests.Parts;

public class SlicerDefaultsProviderTests
{
    [Fact]
    public void GetFallbackMaterialType_DefaultOptions_ReturnsPla()
    {
        var provider = new SlicerDefaultsProvider(Options.Create(new SlicerOptions()));

        provider.GetFallbackMaterialType().Should().Be("PLA");
    }

    [Fact]
    public void GetFallbackMaterialType_ConfiguredOverride_ReturnsConfiguredValue()
    {
        var provider = new SlicerDefaultsProvider(Options.Create(new SlicerOptions { FallbackMaterialType = "PETG" }));

        provider.GetFallbackMaterialType().Should().Be("PETG");
    }
}
