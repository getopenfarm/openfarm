using FluentAssertions;
using OpenFarm.Domain.FilamentTracking;

namespace OpenFarm.Domain.Tests.FilamentTracking;

public class SpoolReadingTests
{
    [Theory]
    [InlineData(87, 1000, 870)]
    [InlineData(42, 250, 105)]
    [InlineData(100, 1000, 1000)]
    [InlineData(1, 1000, 10)]
    public void Grams_AMeasuredPercent_IsThatShareOfTheFullWeight(int percent, int full, int expected)
    {
        SpoolReading.Grams(percent, full).Should().Be(expected);
    }

    [Theory]
    [InlineData(null)]
    [InlineData(-1)]
    [InlineData(0)]
    [InlineData(101)]
    public void Grams_AnUnmeasuredReading_IsNull(int? percent)
    {
        // The AMS reports -1 or 0 until it has measured a spool, and nothing when remaining tracking is off.
        SpoolReading.Grams(percent, 1000).Should().BeNull();
    }

    [Theory]
    [InlineData(640, 420, 1000, true)]
    [InlineData(420, 640, 1000, true)]
    [InlineData(520, 420, 1000, false)] // exactly a tenth apart is still in line
    [InlineData(500, 480, 1000, false)]
    public void Drifted_OnlyPastATenthOfTheFullWeight_EitherWay(int inventory, int read, int full, bool drifted)
    {
        SpoolReading.Drifted(inventory, read, full).Should().Be(drifted);
    }
}
