using FluentAssertions;
using OpenFarm.Application.Parts;
using OpenFarm.Application.Tests.Parts.Fakes;
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

    // ---------------------------------------------------------------- production pin: that pair or nothing

    [Fact]
    public void ResolvePinned_CompletePairBundledForTheHost_IsThatPair()
    {
        var sut = CreateSut(Available(SlicerEngine.OrcaSlicer, "2.2.0"), Available(SlicerEngine.OrcaSlicer, "2.3.0"));

        sut.ResolvePinned(SlicerEngine.OrcaSlicer, "2.2.0").Should().Be(new SlicerRef(SlicerEngine.OrcaSlicer, "2.2.0"));
    }

    [Theory]
    [InlineData(null, null)]
    [InlineData(SlicerEngine.OrcaSlicer, null)]
    [InlineData(null, "2.2.0")]
    [InlineData(SlicerEngine.OrcaSlicer, " ")]
    public void ResolvePinned_IncompletePin_IsBlockedNeverFilledFromTheFarmDefaultOrTheNewestVersion(SlicerEngine? engine, string? version)
    {
        var sut = CreateSut(Available(SlicerEngine.OrcaSlicer, "2.2.0"), Available(SlicerEngine.OrcaSlicer, "2.3.0"));

        var act = () => sut.ResolvePinned(engine, version);

        act.Should().Throw<SlicingProfileUnconfiguredException>().Which.Code.Should().Be("NoPin");
    }

    [Fact]
    public void ResolvePinned_PairNotBundled_IsUnavailableNotSubstituted()
    {
        var sut = CreateSut(Available(SlicerEngine.OrcaSlicer, "2.3.0"));

        var act = () => sut.ResolvePinned(SlicerEngine.OrcaSlicer, "2.2.0");

        act.Should().Throw<SlicerUnavailableException>();
    }

    // ---------------------------------------------------------------- replacing or reprocessing a part

    [Fact]
    public void ResolveKeepingPin_NothingRequested_KeepsTheCurrentPinOverTheFarmDefault()
    {
        var sut = CreateSut(Available(SlicerEngine.OrcaSlicer, "2.2.0"), Available(SlicerEngine.BambuStudio, "2.7.1"));

        var result = sut.ResolveKeepingPin(null, null, SlicerEngine.OrcaSlicer, "2.2.0", new SlicerRef(SlicerEngine.BambuStudio, "2.7.1"));

        result.Should().Be(new SlicerRef(SlicerEngine.OrcaSlicer, "2.2.0"));
    }

    [Fact]
    public void ResolveKeepingPin_ExplicitPair_MovesThePart()
    {
        var sut = CreateSut(Available(SlicerEngine.OrcaSlicer, "2.2.0"), Available(SlicerEngine.BambuStudio, "2.7.1"));

        var result = sut.ResolveKeepingPin(SlicerEngine.BambuStudio, "2.7.1", SlicerEngine.OrcaSlicer, "2.2.0", null);

        result.Should().Be(new SlicerRef(SlicerEngine.BambuStudio, "2.7.1"));
    }

    [Fact]
    public void ResolveKeepingPin_EngineOnly_MovesToTheNewestVersionOfThatEngine()
    {
        var sut = CreateSut(Available(SlicerEngine.OrcaSlicer, "2.2.0"), Available(SlicerEngine.OrcaSlicer, "2.3.0"));

        sut.ResolveKeepingPin(SlicerEngine.OrcaSlicer, null, SlicerEngine.OrcaSlicer, "2.2.0", null)
            .Should().Be(new SlicerRef(SlicerEngine.OrcaSlicer, "2.3.0"));
    }

    [Fact]
    public void ResolveKeepingPin_PartWithoutAPin_TakesTheFarmDefault()
    {
        var sut = CreateSut(Available(SlicerEngine.OrcaSlicer, "2.2.0"), Available(SlicerEngine.BambuStudio, "2.7.1"));

        sut.ResolveKeepingPin(null, null, null, null, new SlicerRef(SlicerEngine.BambuStudio, "2.7.1"))
            .Should().Be(new SlicerRef(SlicerEngine.BambuStudio, "2.7.1"));
    }

    [Fact]
    public void ResolveKeepingPin_CurrentPinNoLongerBundled_IsUnavailableNotReplacedByTheDefault()
    {
        var sut = CreateSut(Available(SlicerEngine.OrcaSlicer, "2.3.0"));

        var act = () => sut.ResolveKeepingPin(null, null, SlicerEngine.OrcaSlicer, "2.2.0", new SlicerRef(SlicerEngine.OrcaSlicer, "2.3.0"));

        act.Should().Throw<SlicerUnavailableException>();
    }

    private static AvailableSlicer Available(SlicerEngine engine, string version) =>
        new(engine, version, SlicerPlatform.Linux, $"/bundle/{engine}/{version}/linux/slicer");
}
