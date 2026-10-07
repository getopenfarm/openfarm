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
    [Theory]
    [InlineData("[{\"diameter\":\"0.4\"}]", "0.4")]
    [InlineData("[{\"diameter\":0.4},{\"diameter\":0.6}]", null)]
    [InlineData("[{\"diameter\":0.4},{}]", null)]
    [InlineData("[]", null)]
    public void NestedNozzleReportsRequireKnownUniformDiameters(string info, string? expected)
    {
        var telemetry = BambuMqttMessageParser.TryParseTelemetry($"{{\"print\":{{\"device\":{{\"nozzle\":{{\"info\":{info}}}}}}}}}");
        telemetry!.NozzleReported.Should().BeTrue();
        telemetry.NozzleDiameterMm.Should().Be(expected is null ? null : decimal.Parse(expected, System.Globalization.CultureInfo.InvariantCulture));
    }

    [Theory]
    [InlineData("0.4", "0.4")]
    [InlineData("\"0.6\"", "0.6")]
    [InlineData("0", null)]
    [InlineData("\"unknown\"", null)]
    [InlineData("null", null)]
    public void NozzleDiameterAcceptsNumericAndStringTelemetryOnly(string value, string? expected)
    {
        var telemetry = BambuMqttMessageParser.TryParseTelemetry($"{{\"print\":{{\"nozzle_diameter\":{value}}}}}");
        telemetry!.NozzleDiameterMm.Should().Be(expected is null ? null : decimal.Parse(expected, System.Globalization.CultureInfo.InvariantCulture));
    }

    private static readonly string FixturesDirectory = Path.Combine(AppContext.BaseDirectory, "Printers", "Fixtures");

    [Theory]
    [InlineData("null")]
    [InlineData("[]")]
    [InlineData("42")]
    [InlineData("{\"print\":null,\"info\":[]}")]
    [InlineData("{\"print\":[],\"info\":42}")]
    public void WrongJsonShape_IsIgnoredWithoutThrowing(string payload)
    {
        BambuMqttMessageParser.IsPrintReport(payload).Should().BeFalse();
        BambuMqttMessageParser.IsGetVersionResponse(payload).Should().BeFalse();
        BambuMqttMessageParser.TryParseGcodeState(payload, out _).Should().BeFalse();
        BambuMqttMessageParser.TryParseTelemetry(payload).Should().BeNull();
    }

    [Theory]
    [InlineData("{\"print\":{\"nozzle_temper\":220,\"ams\":null}}")]
    [InlineData("{\"print\":{\"nozzle_temper\":220,\"ams\":{\"ams\":[null,42,[]]}}}")]
    [InlineData("{\"print\":{\"nozzle_temper\":220,\"ams\":{\"ams\":[{\"id\":\"0\",\"tray\":[null,42,[]]}]}}}")]
    public void MalformedAmsEntries_DoNotDiscardValidTelemetry(string payload)
    {
        var telemetry = BambuMqttMessageParser.TryParseTelemetry(payload);

        telemetry.Should().NotBeNull();
        telemetry!.NozzleTempC.Should().Be(220);
        telemetry.AmsUnits.SelectMany(unit => unit.Trays).Should().BeEmpty();
    }

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

    [Fact]
    public void TryParseTelemetry_WithMultiAmsFixture_ExtractsTemperaturesProgressAndStage()
    {
        var reportPayload = LoadSection("awaiting-clear-x1c-multi-ams.json", "pushall");

        var telemetry = BambuMqttMessageParser.TryParseTelemetry(reportPayload);

        telemetry.Should().NotBeNull();
        telemetry!.NozzleTempC.Should().Be(26.0m);
        telemetry.BedTempC.Should().Be(23.0m);
        telemetry.PrintProgressPercent.Should().Be(100);
        telemetry.CurrentStageLabel.Should().Be("idle"); // stg_cur -1 => idle sub-stage label
    }

    [Fact]
    public void TryParseTelemetry_WithMultiAmsFixture_ExtractsRemainingTimeAndPrinterReportedJobName()
    {
        // The printer reports its own current job (subtask_name) and remaining time regardless of
        // how the print was started — this fixture is a real device capture, not an OpenFarm-dispatched
        // job, which is exactly the "not started from OpenFarm" case the card must still show.
        var reportPayload = LoadSection("awaiting-clear-x1c-multi-ams.json", "pushall");

        var telemetry = BambuMqttMessageParser.TryParseTelemetry(reportPayload);

        telemetry.Should().NotBeNull();
        telemetry!.RemainingTimeMinutes.Should().Be(0);
        telemetry.CurrentJobName.Should().Be("Garden Hose Healer: Male fitting");
    }

    [Fact]
    public void TryParseTelemetry_WithEmptySubtaskName_ReturnsNullJobName()
    {
        // An empty subtask_name means "no job to name" — treated as null so the delta-merge keeps the
        // last known value rather than clobbering it with a blank.
        var reportPayload = LoadSection("printing-a1.json", "pushall");

        var telemetry = BambuMqttMessageParser.TryParseTelemetry(reportPayload);

        telemetry.Should().NotBeNull();
        telemetry!.CurrentJobName.Should().BeNull();
    }

    [Fact]
    public void TryParseTelemetry_WithFractionalSensorReadings_RoundsToWholeDegrees()
    {
        // This fixture's raw nozzle_temper/bed_temper are 25.90625/23.125 — real Bambu ADC noise
        // that jitters on nearly every message. A print-farm operator only cares about whole
        // degrees, so the parser rounds rather than surfacing that sensor-level churn.
        var reportPayload = LoadSection("awaiting-clear-p1p-no-ams.json", "pushall");

        var telemetry = BambuMqttMessageParser.TryParseTelemetry(reportPayload);

        telemetry.Should().NotBeNull();
        telemetry!.NozzleTempC.Should().Be(26m);
        telemetry.BedTempC.Should().Be(23m);
    }

    [Fact]
    public void TryParseTelemetry_WithIdleA1Fixture_CorrectsBootQuirkStageZeroToIdle()
    {
        var reportPayload = LoadSection("idle-a1.json", "pushall");

        var telemetry = BambuMqttMessageParser.TryParseTelemetry(reportPayload);

        telemetry.Should().NotBeNull();
        // Firmware boot quirk: an idle A1/P1 reports stg_cur == 0 instead of the real idle
        // sentinel 255 right after boot. print_type == "idle" is the signal to correct for it.
        telemetry!.CurrentStageLabel.Should().Be("idle");
    }

    [Fact]
    public void TryParseTelemetry_WithMultiAmsFixture_ExtractsAllFourAmsUnitsWithCorrectTrayCounts()
    {
        var reportPayload = LoadSection("awaiting-clear-x1c-multi-ams.json", "pushall");

        var telemetry = BambuMqttMessageParser.TryParseTelemetry(reportPayload);

        telemetry.Should().NotBeNull();
        telemetry!.AmsUnits.Should().HaveCount(4);
        telemetry.AmsUnits.Select(u => u.UnitIndex).Should().BeEquivalentTo([0, 1, 2, 128]);
        telemetry.AmsUnits.Single(u => u.UnitIndex == 0).Trays.Should().HaveCount(4);
        telemetry.AmsUnits.Single(u => u.UnitIndex == 128).Trays.Should().HaveCount(1); // external spool holder
    }

    [Fact]
    public void TryParseTelemetry_WithMultiAmsFixture_ParsesAPopulatedTrayCorrectly()
    {
        var reportPayload = LoadSection("awaiting-clear-x1c-multi-ams.json", "pushall");

        var telemetry = BambuMqttMessageParser.TryParseTelemetry(reportPayload);

        var tray = telemetry!.AmsUnits.Single(u => u.UnitIndex == 0).Trays.Single(t => t.SlotIndex == 0);
        tray.Empty.Should().BeFalse();
        tray.MaterialType.Should().Be("PLA");
        tray.ColorHex.Should().Be("000000FF");
        tray.RemainPercent.Should().Be(93);
    }

    [Fact]
    public void TryParseTelemetry_WithMultiAmsFixture_ParsesAnEmptyTrayAsEmpty()
    {
        var reportPayload = LoadSection("awaiting-clear-x1c-multi-ams.json", "pushall");

        var telemetry = BambuMqttMessageParser.TryParseTelemetry(reportPayload);

        // Unit 2, tray 0 has no tray_type/tray_color in the real fixture — an unloaded slot.
        var tray = telemetry!.AmsUnits.Single(u => u.UnitIndex == 2).Trays.Single(t => t.SlotIndex == 0);
        tray.Empty.Should().BeTrue();
        tray.MaterialType.Should().BeNull();
        tray.ColorHex.Should().BeNull();
    }

    [Fact]
    public void TryParseTelemetry_WithNoAmsFixture_ReturnsEmptyAmsUnits()
    {
        var reportPayload = LoadSection("awaiting-clear-p1p-no-ams.json", "pushall");

        var telemetry = BambuMqttMessageParser.TryParseTelemetry(reportPayload);

        telemetry.Should().NotBeNull();
        telemetry!.AmsUnits.Should().BeEmpty();
    }

    [Theory]
    [InlineData("")]
    [InlineData("not json")]
    public void TryParseTelemetry_WithMalformedPayload_ReturnsNullWithoutThrowing(string payload)
    {
        var act = () => BambuMqttMessageParser.TryParseTelemetry(payload);

        act.Should().NotThrow().Which.Should().BeNull();
    }

    [Fact]
    public void TryParseTelemetry_WithIdleA1Fixture_ParsesExternalSpoolTrayAsLoaded()
    {
        var reportPayload = LoadSection("idle-a1.json", "pushall");

        var telemetry = BambuMqttMessageParser.TryParseTelemetry(reportPayload);

        telemetry.Should().NotBeNull();
        telemetry!.ExternalSpoolTray.Should().NotBeNull();
        telemetry.ExternalSpoolTray!.Empty.Should().BeFalse();
        telemetry.ExternalSpoolTray.MaterialType.Should().Be("PLA");
        telemetry.ExternalSpoolTray.ColorHex.Should().Be("FFFFFFFF");
    }

    [Fact]
    public void TryParseTelemetry_WithNoAmsFixture_ParsesExternalSpoolTrayWithDifferentMaterial()
    {
        var reportPayload = LoadSection("awaiting-clear-p1p-no-ams.json", "pushall");

        var telemetry = BambuMqttMessageParser.TryParseTelemetry(reportPayload);

        telemetry.Should().NotBeNull();
        telemetry!.ExternalSpoolTray.Should().NotBeNull();
        telemetry.ExternalSpoolTray!.Empty.Should().BeFalse();
        telemetry.ExternalSpoolTray.MaterialType.Should().Be("PETG");
        telemetry.ExternalSpoolTray.ColorHex.Should().Be("BCBCBCFF");
    }

    [Fact]
    public void TryParseTelemetry_WithMultiAmsFixture_ParsesEmptyExternalSpoolTrayAsEmpty()
    {
        var reportPayload = LoadSection("awaiting-clear-x1c-multi-ams.json", "pushall");

        var telemetry = BambuMqttMessageParser.TryParseTelemetry(reportPayload);

        telemetry.Should().NotBeNull();
        telemetry!.ExternalSpoolTray.Should().NotBeNull();
        telemetry.ExternalSpoolTray!.Empty.Should().BeTrue();
        telemetry.ExternalSpoolTray.MaterialType.Should().BeNull();
    }

    private static string LoadSection(string fixtureFile, string sectionName)
    {
        var path = Path.Combine(FixturesDirectory, fixtureFile);
        using var document = JsonDocument.Parse(File.ReadAllText(path));
        return document.RootElement.GetProperty(sectionName).GetRawText();
    }
}
