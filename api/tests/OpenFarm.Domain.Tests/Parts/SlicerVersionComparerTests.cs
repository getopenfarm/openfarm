using FluentAssertions;
using OpenFarm.Domain.Parts;

namespace OpenFarm.Domain.Tests.Parts;

public class SlicerVersionComparerTests
{
    private static readonly SlicerVersionComparer Comparer = new();

    [Fact]
    public void Compare_ThreeComponentVersions_OrdersNumerically()
    {
        Comparer.Compare("2.3.0", "2.2.0").Should().BePositive();
        Comparer.Compare("2.2.0", "2.3.0").Should().BeNegative();
        Comparer.Compare("2.2.0", "2.2.0").Should().Be(0);
    }

    [Fact]
    public void Compare_FourComponentAgainstThreeComponent_ZeroPadsTheShorterOne()
    {
        // "2.5.59.13" (4-component) vs "2.2.0" (3-component, zero-padded to "2.2.0.0").
        Comparer.Compare("2.5.59.13", "2.2.0").Should().BePositive();
        Comparer.Compare("2.2.0", "2.5.59.13").Should().BeNegative();
    }

    [Fact]
    public void Compare_BambuFourComponentLeadingZeroForm_OrdersNumerically()
    {
        Comparer.Compare("01.10.00.00", "01.09.00.00").Should().BePositive();
        Comparer.Compare("01.09.00.00", "01.10.00.00").Should().BeNegative();
        Comparer.Compare("01.10.00.00", "01.10.00.00").Should().Be(0);
    }

    [Fact]
    public void Compare_NonNumericSuffixBuild_OrdersBeforeTheCleanRelease()
    {
        // An RC/non-numeric-suffix build orders before its clean release.
        Comparer.Compare("2.2.0-rc1", "2.2.0").Should().BeNegative();
        Comparer.Compare("2.2.0", "2.2.0-rc1").Should().BePositive();
    }

    [Fact]
    public void Compare_SameNumericComponents_TiebreaksLexically()
    {
        Comparer.Compare("2.2.0-alpha", "2.2.0-beta").Should().BeNegative();
        Comparer.Compare("2.2.0-beta", "2.2.0-alpha").Should().BePositive();
    }

    [Fact]
    public void Compare_IsWithinEngineOnly_NoCrossEngineSemanticsAssumed()
    {
        // The comparer takes plain version strings — cross-engine ordering is a caller-level
        // concern (engine preference order), never inferred from the numbers themselves.
        Comparer.Compare("2.5.59.13", "2.5.59.13").Should().Be(0);
    }
}
