using FluentAssertions;
using OpenFarm.Application.Parts;
using OpenFarm.Domain.Parts;

namespace OpenFarm.Application.Tests.Parts;

public class ResolveSlicerSelectionTests
{
    private static ResolveSlicerSelection CreateSut(params AvailableSlicer[] available) =>
        new(new FakeAvailableSlicerCatalog(available), new FakeHostPlatformProvider(SlicerPlatform.Linux));

    [Fact]
    public void Resolve_ExplicitPairPresentForHostPlatform_IsAccepted()
    {
        var orca220 = Available(SlicerEngine.OrcaSlicer, "2.2.0");
        var sut = CreateSut(orca220);

        var result = sut.Resolve(SlicerEngine.OrcaSlicer, "2.2.0", farmDefault: null);

        result.Should().Be(new SlicerRef(SlicerEngine.OrcaSlicer, "2.2.0"));
    }

    [Fact]
    public void Resolve_ExplicitPairAbsent_ThrowsSlicerUnavailable()
    {
        var sut = CreateSut(Available(SlicerEngine.OrcaSlicer, "2.2.0"));

        var act = () => sut.Resolve(SlicerEngine.SuperSlicer, "9.9.9", farmDefault: null);

        act.Should().Throw<SlicerUnavailableException>();
    }

    [Fact]
    public void Resolve_EngineOnlyOverride_UsesNewestVersionOfThatEngine()
    {
        var sut = CreateSut(
            Available(SlicerEngine.OrcaSlicer, "2.2.0"),
            Available(SlicerEngine.OrcaSlicer, "2.3.0"));

        var result = sut.Resolve(SlicerEngine.OrcaSlicer, overrideVersion: null, farmDefault: null);

        result.Should().Be(new SlicerRef(SlicerEngine.OrcaSlicer, "2.3.0"));
    }

    [Fact]
    public void Resolve_NoOverride_UsesEffectiveFarmDefault()
    {
        var sut = CreateSut(
            Available(SlicerEngine.OrcaSlicer, "2.2.0"),
            Available(SlicerEngine.SuperSlicer, "2.5.59.13"));

        var result = sut.Resolve(null, null, new SlicerRef(SlicerEngine.SuperSlicer, "2.5.59.13"));

        result.Should().Be(new SlicerRef(SlicerEngine.SuperSlicer, "2.5.59.13"));
    }

    [Fact]
    public void Resolve_NoDefaultAndEmptyCatalog_ThrowsNamingNoSlicerAvailable()
    {
        var sut = CreateSut();

        var act = () => sut.Resolve(null, null, farmDefault: null);

        act.Should().Throw<SlicerUnavailableException>().WithMessage("*no slicer*");
    }

    [Fact]
    public void ResolveEffectiveDefault_ExplicitDefaultPresent_IsUsedAndMarkedExplicit()
    {
        var sut = CreateSut(Available(SlicerEngine.OrcaSlicer, "2.2.0"), Available(SlicerEngine.OrcaSlicer, "2.3.0"));

        var result = sut.ResolveEffectiveDefault(new SlicerRef(SlicerEngine.OrcaSlicer, "2.2.0"));

        result.Default.Should().Be(new SlicerRef(SlicerEngine.OrcaSlicer, "2.2.0"));
        result.IsExplicit.Should().BeTrue();
    }

    [Fact]
    public void ResolveEffectiveDefault_NoExplicitDefault_PrefersNewestOrcaSlicer()
    {
        var sut = CreateSut(
            Available(SlicerEngine.OrcaSlicer, "2.2.0"),
            Available(SlicerEngine.OrcaSlicer, "2.3.0"),
            Available(SlicerEngine.SuperSlicer, "2.5.59.13"));

        var result = sut.ResolveEffectiveDefault(farmDefault: null);

        result.Default.Should().Be(new SlicerRef(SlicerEngine.OrcaSlicer, "2.3.0"));
        result.IsExplicit.Should().BeFalse();
    }

    [Fact]
    public void ResolveEffectiveDefault_NoOrcaSlicerBundled_FallsBackToNewestOfAnyEngine()
    {
        var sut = CreateSut(Available(SlicerEngine.SuperSlicer, "2.5.59.13"));

        var result = sut.ResolveEffectiveDefault(farmDefault: null);

        result.Default.Should().Be(new SlicerRef(SlicerEngine.SuperSlicer, "2.5.59.13"));
        result.IsExplicit.Should().BeFalse();
    }

    [Fact]
    public void ResolveEffectiveDefault_EmptyCatalog_ReturnsNullDefault()
    {
        var sut = CreateSut();

        var result = sut.ResolveEffectiveDefault(farmDefault: null);

        result.Default.Should().BeNull();
        result.IsExplicit.Should().BeFalse();
    }

    [Fact]
    public void EnsureAvailable_NotPresentForHostPlatform_Throws()
    {
        var sut = CreateSut(new AvailableSlicer(SlicerEngine.OrcaSlicer, "2.2.0", SlicerPlatform.Windows, "C:\\slicer.exe"));

        var act = () => sut.EnsureAvailable(new SlicerRef(SlicerEngine.OrcaSlicer, "2.2.0"));

        act.Should().Throw<SlicerUnavailableException>();
    }

    private static AvailableSlicer Available(SlicerEngine engine, string version) =>
        new(engine, version, SlicerPlatform.Linux, $"/bundle/{engine}/{version}/linux/slicer");

    private sealed class FakeAvailableSlicerCatalog(IReadOnlyList<AvailableSlicer> available) : IAvailableSlicerCatalog
    {
        public IReadOnlyList<AvailableSlicer> GetAvailable() => available;

        public AvailableSlicer? Find(SlicerRef slicerRef, SlicerPlatform platform) =>
            available.FirstOrDefault(a =>
                a.Engine == slicerRef.Engine
                && string.Equals(a.Version, slicerRef.Version, StringComparison.Ordinal)
                && a.Platform == platform);
    }

    private sealed class FakeHostPlatformProvider(SlicerPlatform current) : IHostPlatformProvider
    {
        public SlicerPlatform Current { get; } = current;
    }
}
