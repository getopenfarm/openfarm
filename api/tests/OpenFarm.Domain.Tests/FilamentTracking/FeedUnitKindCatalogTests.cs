using FluentAssertions;
using OpenFarm.Domain.FilamentTracking;

namespace OpenFarm.Domain.Tests.FilamentTracking;

public class FeedUnitKindCatalogTests
{
    [Theory]
    [InlineData(FeedUnitKindCatalog.MultiMaterialUnit, ReloadPolicy.MatchRequired)]
    [InlineData(FeedUnitKindCatalog.AutomaticFilamentReloader, ReloadPolicy.Unconditional)]
    [InlineData(FeedUnitKindCatalog.ExternalSpool, ReloadPolicy.None)]
    public void Resolve_WithKnownKind_ReturnsDocumentedPolicy(string kind, ReloadPolicy expected)
    {
        var result = FeedUnitKindCatalog.Resolve(kind);

        result.Should().Be(expected);
    }

    [Theory]
    [InlineData("SomeFutureHardware")]
    [InlineData("")]
    [InlineData("multimaterialunit")]
    public void Resolve_WithUnrecognizedKind_ReturnsNoneWithoutThrowing(string kind)
    {
        var act = () => FeedUnitKindCatalog.Resolve(kind);

        act.Should().NotThrow().Which.Should().Be(ReloadPolicy.None);
    }
}
