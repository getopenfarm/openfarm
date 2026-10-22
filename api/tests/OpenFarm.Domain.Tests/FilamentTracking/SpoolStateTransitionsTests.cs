using FluentAssertions;
using OpenFarm.Domain.FilamentTracking;

namespace OpenFarm.Domain.Tests.FilamentTracking;

public class SpoolStateTransitionsTests
{
    [Theory]
    [InlineData(SpoolState.InStorage, SpoolState.Using)]
    [InlineData(SpoolState.Using, SpoolState.InStorage)]
    [InlineData(SpoolState.Using, SpoolState.Finished)]
    [InlineData(SpoolState.InStorage, SpoolState.Finished)]
    [InlineData(SpoolState.Finished, SpoolState.InStorage)]
    public void EnsureValidTransition_WithLegalTransition_DoesNotThrow(SpoolState from, SpoolState to)
    {
        var act = () => SpoolStateTransitions.EnsureValidTransition(from, to);

        act.Should().NotThrow();
    }

    [Theory]
    [InlineData(SpoolState.Finished, SpoolState.Using)]
    [InlineData(SpoolState.Finished, SpoolState.Finished)]
    [InlineData(SpoolState.InStorage, SpoolState.InStorage)]
    [InlineData(SpoolState.Using, SpoolState.Using)]
    public void EnsureValidTransition_WithIllegalTransition_Throws(SpoolState from, SpoolState to)
    {
        var act = () => SpoolStateTransitions.EnsureValidTransition(from, to);

        act.Should().Throw<InvalidSpoolStateTransitionException>();
    }

    [Fact]
    public void EnsureUsing_WhenUsing_DoesNotThrow()
    {
        var act = () => SpoolStateTransitions.EnsureUsing(SpoolState.Using);

        act.Should().NotThrow();
    }

    [Theory]
    [InlineData(SpoolState.InStorage)]
    [InlineData(SpoolState.Finished)]
    public void EnsureUsing_WhenNotUsing_Throws(SpoolState state)
    {
        var act = () => SpoolStateTransitions.EnsureUsing(state);

        act.Should().Throw<InvalidSpoolStateTransitionException>();
    }
}
