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

    // TDD (write-first) per plan.md's Constitution Check — the IncludedTemplateNames fold-in is the
    // one path with no OrcaSlicer-sourced equivalent to catch a regression (research.md §0a), so it
    // needs its own direct coverage.

    [Fact]
    public void MergeAncestorChainWithIncludes_ResolvedFragment_FoldsItsFieldsIntoTheResult()
    {
        var ancestorsOldestFirst = new List<string>
        {
            """{"type":"machine","inherits":"fdm_bbl_3dp_001_common"}""",
        };
        var includedTemplateNames = new List<string> { "template machine_start_gcode" };

        var merged = ProfileRawDataMerger.MergeAncestorChainWithIncludes(
            ancestorsOldestFirst,
            includedTemplateNames,
            name => name == "template machine_start_gcode" ? """{"machine_start_gcode":"G28\n"}""" : null);

        merged.Should().Contain("\"machine_start_gcode\"");
        merged.Should().Contain("\"inherits\"");
    }

    [Fact]
    public void MergeAncestorChainWithIncludes_UnresolvedIncludeName_IsAGapNotAnError()
    {
        var ancestorsOldestFirst = new List<string> { """{"type":"machine"}""" };
        var includedTemplateNames = new List<string> { "does not exist" };

        string? result = null;
        var act = () => result = ProfileRawDataMerger.MergeAncestorChainWithIncludes(
            ancestorsOldestFirst, includedTemplateNames, _ => null);

        act.Should().NotThrow();
        result.Should().Contain("\"type\"");
    }

    [Fact]
    public void MergeAncestorChainWithIncludes_TargetsOwnKeyWinsOverAnIncludedFragment()
    {
        var ancestorsOldestFirst = new List<string>
        {
            """{"type":"machine","machine_start_gcode":"target's own explicit gcode"}""",
        };
        var includedTemplateNames = new List<string> { "template machine_start_gcode" };

        var merged = ProfileRawDataMerger.MergeAncestorChainWithIncludes(
            ancestorsOldestFirst,
            includedTemplateNames,
            _ => """{"machine_start_gcode":"fragment gcode, should lose"}""");

        merged.Should().Contain("target's own explicit gcode");
        merged.Should().NotContain("should lose");
    }

    [Fact]
    public void MergeAncestorChainWithIncludes_IncludedFragmentWinsOverAnAncestorLayer()
    {
        var ancestorsOldestFirst = new List<string>
        {
            """{"machine_start_gcode":"ancestor gcode, should lose"}""",
            """{"type":"machine"}""",
        };
        var includedTemplateNames = new List<string> { "template machine_start_gcode" };

        var merged = ProfileRawDataMerger.MergeAncestorChainWithIncludes(
            ancestorsOldestFirst,
            includedTemplateNames,
            _ => """{"machine_start_gcode":"fragment gcode, should win"}""");

        merged.Should().Contain("fragment gcode, should win");
        merged.Should().NotContain("ancestor gcode");
    }

    [Fact]
    public void MergeAncestorChainWithIncludes_NoIncludedTemplateNames_BehavesLikePlainMerge()
    {
        var ancestorsOldestFirst = new List<string> { """{"type":"machine","nozzle_diameter":["0.4"]}""" };

        var merged = ProfileRawDataMerger.MergeAncestorChainWithIncludes(ancestorsOldestFirst, [], _ => null);

        merged.Should().Be(ProfileRawDataMerger.MergeAncestorChain(ancestorsOldestFirst));
    }

    [Fact]
    public void MergeAncestorChainWithIncludes_MultipleIncludedFragments_AllFoldIn()
    {
        var ancestorsOldestFirst = new List<string> { """{"type":"machine"}""" };
        var includedTemplateNames = new List<string> { "template start", "template end" };
        var fragments = new Dictionary<string, string>
        {
            ["template start"] = """{"machine_start_gcode":"G28"}""",
            ["template end"] = """{"machine_end_gcode":"M104 S0"}""",
        };

        var merged = ProfileRawDataMerger.MergeAncestorChainWithIncludes(
            ancestorsOldestFirst, includedTemplateNames, name => fragments.GetValueOrDefault(name));

        merged.Should().Contain("\"machine_start_gcode\"");
        merged.Should().Contain("\"machine_end_gcode\"");
    }
}
