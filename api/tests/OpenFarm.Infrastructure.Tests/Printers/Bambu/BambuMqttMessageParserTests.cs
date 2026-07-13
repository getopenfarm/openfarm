using System.Text.Json;
using FluentAssertions;
using OpenFarm.Infrastructure.Printers.Bambu;

namespace OpenFarm.Infrastructure.Tests.Printers.Bambu;

/// <summary>
/// Fixture-replay tests (test-after, per the constitution's named exemption for connector code) —
/// never TDD against a live printer. Fixtures are real pybambu test-corpus payloads, see
/// <c>Printers/Fixtures/README.md</c> for provenance.
/// </summary>
public class BambuMqttMessageParserTests
{
    private static readonly string FixturesDirectory = Path.Combine(AppContext.BaseDirectory, "Printers", "Fixtures");

    [Theory]
    [InlineData("idle-a1.json", "IDLE")]
    [InlineData("printing-a1.json", "RUNNING")]
    [InlineData("awaiting-clear-p1p-no-ams.json", "FINISH")]
    [InlineData("awaiting-clear-x1c-multi-ams.json", "FINISH")]
    public void TryParseGcodeState_WithRealReportFixture_ExtractsExpectedState(string fixtureFile, string expectedGcodeState)
    {
        var reportPayload = LoadSection(fixtureFile, "pushall");

        var parsed = BambuMqttMessageParser.TryParseGcodeState(reportPayload, out var gcodeState);

        parsed.Should().BeTrue();
        gcodeState.Should().Be(expectedGcodeState);
    }

    [Theory]
    [InlineData("idle-a1.json")]
    [InlineData("awaiting-clear-p1p-no-ams.json")]
    public void IsPrintReport_WithRealReportFixture_ReturnsTrue(string fixtureFile)
    {
        var reportPayload = LoadSection(fixtureFile, "pushall");

        BambuMqttMessageParser.IsPrintReport(reportPayload).Should().BeTrue();
    }

    [Theory]
    [InlineData("idle-a1.json")]
    [InlineData("awaiting-clear-p1p-no-ams.json")]
    public void IsGetVersionResponse_WithRealGetVersionFixture_ReturnsTrue(string fixtureFile)
    {
        var getVersionPayload = LoadSection(fixtureFile, "get_version");

        BambuMqttMessageParser.IsGetVersionResponse(getVersionPayload).Should().BeTrue();
    }

    [Fact]
    public void IsGetVersionResponse_WithReportPayload_ReturnsFalse()
    {
        var reportPayload = LoadSection("idle-a1.json", "pushall");

        BambuMqttMessageParser.IsGetVersionResponse(reportPayload).Should().BeFalse();
    }

    [Theory]
    [InlineData("")]
    [InlineData("not json")]
    [InlineData("{\"unrelated\": true}")]
    public void TryParseGcodeState_WithMalformedOrUnrelatedPayload_ReturnsFalseWithoutThrowing(string payload)
    {
        var act = () => BambuMqttMessageParser.TryParseGcodeState(payload, out _);

        act.Should().NotThrow().Which.Should().BeFalse();
    }

    private static string LoadSection(string fixtureFile, string sectionName)
    {
        var path = Path.Combine(FixturesDirectory, fixtureFile);
        using var document = JsonDocument.Parse(File.ReadAllText(path));
        return document.RootElement.GetProperty(sectionName).GetRawText();
    }
}
