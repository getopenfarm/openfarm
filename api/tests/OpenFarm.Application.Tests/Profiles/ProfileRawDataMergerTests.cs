using FluentAssertions;
using OpenFarm.Application.Profiles;

namespace OpenFarm.Application.Tests.Profiles;

public class ProfileRawDataMergerTests
{
    [Fact]
    public void MergeAncestorChain_WithSingleElement_ReturnsItUnchanged()
    {
        var result = ProfileRawDataMerger.MergeAncestorChain(["""{"a":1,"b":2}"""]);

        System.Text.Json.JsonSerializer.Deserialize<Dictionary<string, int>>(result)
            .Should().BeEquivalentTo(new Dictionary<string, int> { ["a"] = 1, ["b"] = 2 });
    }

    [Fact]
    public void MergeAncestorChain_AncestorFirst_LastElementWinsOnKeyConflict()
    {
        var ancestor = """{"nozzle_temperature":["220"],"bed_temperature":["55"]}""";
        var descendant = """{"nozzle_temperature":["230"]}""";

        var result = ProfileRawDataMerger.MergeAncestorChain([ancestor, descendant]);

        var merged = System.Text.Json.JsonDocument.Parse(result).RootElement;
        merged.GetProperty("nozzle_temperature")[0].GetString().Should().Be("230");
        merged.GetProperty("bed_temperature")[0].GetString().Should().Be("55");
    }

    [Fact]
    public void MergeAncestorChain_ShallowOverwrite_DoesNotDeepMergeNestedValues()
    {
        var ancestor = """{"filament_flow_ratio":["0.98","0.98"]}""";
        var descendant = """{"filament_flow_ratio":["0.95"]}""";

        var result = ProfileRawDataMerger.MergeAncestorChain([ancestor, descendant]);

        var merged = System.Text.Json.JsonDocument.Parse(result).RootElement;
        merged.GetProperty("filament_flow_ratio").GetArrayLength().Should().Be(1);
        merged.GetProperty("filament_flow_ratio")[0].GetString().Should().Be("0.95");
    }

    [Fact]
    public void MergeAncestorChain_ThreeLevelChain_MergesAllNonConflictingKeys()
    {
        var grandparent = """{"a":1,"b":1,"c":1}""";
        var parent = """{"b":2}""";
        var child = """{"c":3}""";

        var result = ProfileRawDataMerger.MergeAncestorChain([grandparent, parent, child]);

        var merged = System.Text.Json.JsonDocument.Parse(result).RootElement;
        merged.GetProperty("a").GetInt32().Should().Be(1);
        merged.GetProperty("b").GetInt32().Should().Be(2);
        merged.GetProperty("c").GetInt32().Should().Be(3);
    }

    [Fact]
    public void MergeAncestorChain_WithUnparseableAncestorBlob_SkipsItRatherThanThrowing()
    {
        var goodAncestor = """{"a":1}""";
        const string malformedAncestor = "{ not valid json";
        var descendant = """{"b":2}""";

        string? result = null;
        var act = () => result = ProfileRawDataMerger.MergeAncestorChain([goodAncestor, malformedAncestor, descendant]);

        act.Should().NotThrow();
        var merged = System.Text.Json.JsonDocument.Parse(result!).RootElement;
        merged.GetProperty("a").GetInt32().Should().Be(1);
        merged.GetProperty("b").GetInt32().Should().Be(2);
    }

    [Fact]
    public void MergeAncestorChain_WithEmptyList_ReturnsEmptyObject()
    {
        var result = ProfileRawDataMerger.MergeAncestorChain([]);

        result.Should().Be("{}");
    }
}
