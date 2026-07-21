using FluentAssertions;
using OpenFarm.Tools.ProvisionSlicers;

namespace OpenFarm.Tools.ProvisionSlicers.Tests;

public class EngineReleaseSourcesTests
{
    [Theory]
    [InlineData(Engine.OrcaSlicer, "SoftFever/OrcaSlicer")]
    [InlineData(Engine.BambuStudio, "bambulab/BambuStudio")]
    [InlineData(Engine.SuperSlicer, "supermerill/SuperSlicer")]
    public void Resolve_KnownEngineAndPlatform_ResolvesTheOfficialRepo(Engine engine, string expectedRepo)
    {
        var result = EngineReleaseSources.Resolve(engine, "2.2.0", Platform.Linux);

        result.Success.Should().BeTrue();
        result.Source!.Repo.Should().Be(expectedRepo);
    }

    [Fact]
    public void Resolve_OrcaSlicerLinux_ResolvesTheAppImageAssetNamePattern()
    {
        var result = EngineReleaseSources.Resolve(Engine.OrcaSlicer, "2.2.0", Platform.Linux);

        result.Source!.AssetName.Should().Contain("2.2.0").And.EndWith(".AppImage");
    }

    [Fact]
    public void Resolve_UnresolvableCombination_ReturnsATypedFailureNotAnException()
    {
        var act = () => EngineReleaseSources.Resolve(Engine.SuperSlicer, "2.5.59.13", Platform.MacOs);

        act.Should().NotThrow();

        var result = EngineReleaseSources.Resolve(Engine.SuperSlicer, "2.5.59.13", Platform.MacOs);
        result.Success.Should().BeFalse();
        result.Error.Should().NotBeNullOrWhiteSpace();
    }
}
