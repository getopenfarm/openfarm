using System.Text.Json.Nodes;
using FluentAssertions;
using OpenFarm.Infrastructure.Printers.Snapmaker;

namespace OpenFarm.Infrastructure.Tests.Printers.Snapmaker;

public class MoonrakerStatusMapperTests
{
    internal static JsonObject Fixture(string name) =>
        JsonNode.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Printers", "Fixtures", name)))!.AsObject();

    [Theory]
    [InlineData("standby", "IDLE")]
    [InlineData("printing", "RUNNING")]
    [InlineData("paused", "PAUSE")]
    [InlineData("complete", "FINISH")]
    [InlineData("cancelled", "FAILED")]
    [InlineData("error", "FAILED")]
    [InlineData("calibrating", null)]
    public void State_MapsPrintStatsToOpenFarmStates(string printStatsState, string? expected)
    {
        var status = new JsonObject { ["print_stats"] = new JsonObject { ["state"] = printStatsState } };

        MoonrakerStatusMapper.State(status).Should().Be(expected);
    }

    [Fact]
    public void Telemetry_Idle_ReportsFourToolheadsAsOneUnit()
    {
        var telemetry = MoonrakerStatusMapper.Telemetry(Fixture("snapmaker-u1-idle.json"), "IDLE");

        var unit = telemetry.AmsUnits.Should().ContainSingle().Subject;
        unit.UnitIndex.Should().Be(0);
        var trays = unit.Trays;
        trays.Select(t => t.SlotIndex).Should().Equal(0, 1, 2, 3);
        trays[0].Should().BeEquivalentTo(new { Empty = false, MaterialType = "PLA", ColorHex = "FF0000FF", SubBrand = "PLA Basic", RemainPercent = (int?)null, RfidId = (string?)null });
        trays[1].Should().BeEquivalentTo(new { Empty = false, MaterialType = "PETG", SubBrand = (string?)null });
        trays[2].Should().BeEquivalentTo(new { Empty = true, MaterialType = (string?)null, ColorHex = (string?)null });
        trays[3].SubBrand.Should().Be("PLA Matte");
        telemetry.ExternalSpoolTray.Should().BeNull();
    }

    [Fact]
    public void Telemetry_Idle_ResetsWhatWouldOtherwiseStayFromTheLastPrint()
    {
        var telemetry = MoonrakerStatusMapper.Telemetry(Fixture("snapmaker-u1-idle.json"), "IDLE");

        // No head picked up: the hottest head, no active tray, nothing remaining.
        telemetry.NozzleTempC.Should().Be(31);
        telemetry.ActiveTrayId.Should().Be(MoonrakerStatusMapper.NoActiveHead);
        telemetry.RemainingTimeMinutes.Should().Be(0);
        telemetry.PrintProgressPercent.Should().BeNull();
        telemetry.BedTempC.Should().Be(25);
        telemetry.NozzleDiameterMm.Should().Be(0.4m);
        telemetry.NozzleReported.Should().BeTrue();
        telemetry.CurrentGcodeFile.Should().BeNull();
    }

    [Fact]
    public void Telemetry_Printing_ReadsTheActiveHeadAndM73Progress()
    {
        var telemetry = MoonrakerStatusMapper.Telemetry(Fixture("snapmaker-u1-printing.json"), "RUNNING");

        telemetry.ActiveTrayId.Should().Be(1);
        telemetry.NozzleTempC.Should().Be(220);
        telemetry.PrintProgressPercent.Should().Be(42);
        // 1260 s for 42 %: 1260 × 58 / 42 / 60 = 28.9… → 29 min.
        telemetry.RemainingTimeMinutes.Should().Be(29);
        telemetry.CurrentLayer.Should().Be(12);
        telemetry.TotalLayers.Should().Be(80);
        telemetry.CurrentGcodeFile.Should().Be("openfarm/Bracket_PLA_2h.gcode");
        telemetry.CurrentJobName.Should().Be("Bracket_PLA_2h");
        telemetry.AmsUnits.Single().Trays[1].Active.Should().BeTrue();
    }

    [Fact]
    public void Telemetry_MixedNozzles_ReportsAnUnknownDiameter()
    {
        var status = Fixture("snapmaker-u1-idle.json");
        status["extruder2"]!["nozzle_diameter"] = 0.6;

        var telemetry = MoonrakerStatusMapper.Telemetry(status, "IDLE");

        telemetry.NozzleDiameterMm.Should().BeNull();
        telemetry.NozzleReported.Should().BeTrue("a reported mixed set must clear the confirmed nozzle");
    }

    [Fact]
    public void Telemetry_WithoutPrintTaskConfig_LeavesTheToolheadsUnchanged()
    {
        var status = Fixture("snapmaker-u1-idle.json");
        status.Remove("print_task_config");

        MoonrakerStatusMapper.Telemetry(status, "IDLE").AmsUnits.Should().BeEmpty();
    }

    [Fact]
    public void Alerts_KeyEachExceptionByIdIndexAndCode()
    {
        var exceptions = JsonNode.Parse("""
            [
              {"id": 2, "index": 1, "code": 523, "level": 3, "message": "Filament runout", "timestamp": 1.5},
              {"id": 2, "index": 3, "code": 523, "level": 2, "message": "Filament runout", "timestamp": 1.6},
              {"id": 7, "index": 0, "code": 1, "level": 1, "message": "", "timestamp": 1.7}
            ]
            """);

        var alerts = MoonrakerStatusMapper.Alerts(exceptions);

        alerts.Select(a => (a.Source, a.Code, a.Severity)).Should().Equal(
            ("Exception", "2:1:523", "Error"), ("Exception", "2:3:523", "Warning"), ("Exception", "7:0:1", "Information"));
        alerts[2].Message.Should().Be("Printer exception.");
        MoonrakerStatusMapper.Alerts(null).Should().BeEmpty();
    }

    [Fact]
    public void Merge_ReplacesChangedFieldsAndKeepsTheRest()
    {
        var status = Fixture("snapmaker-u1-printing.json");

        SnapmakerU1Connector.Merge(status, JsonNode.Parse("""{"display_status":{"progress":0.5},"newobject":{"a":1}}"""));

        status["display_status"]!["progress"]!.GetValue<decimal>().Should().Be(0.5m);
        status["print_stats"]!["state"]!.GetValue<string>().Should().Be("printing");
        status["newobject"]!["a"]!.GetValue<int>().Should().Be(1);
    }

    [Fact]
    public void StateTracker_ReportsTheFirstStateThenOnlyChanges()
    {
        var tracker = new SnapmakerU1Connector.StateTracker();
        var status = Fixture("snapmaker-u1-idle.json");

        tracker.Observe(status).Should().Be("IDLE");
        tracker.Observe(status).Should().BeNull();
        status["print_stats"]!["state"] = "complete";
        tracker.Observe(status).Should().Be("FINISH");
        tracker.Observe(status).Should().BeNull("re-sending FINISH would undo an operator's Mark ready");
    }

    [Theory]
    [InlineData(1.0, "FINISH")]
    [InlineData(0.4, "FAILED")]
    public void StateTracker_PrintDroppingStraightToStandby_StillReportsHowItEnded(double lastProgress, string expected)
    {
        var tracker = new SnapmakerU1Connector.StateTracker();
        var status = Fixture("snapmaker-u1-printing.json");
        status["display_status"]!["progress"] = lastProgress;
        tracker.Observe(status).Should().Be("RUNNING");

        status["print_stats"]!["state"] = "standby";
        status["display_status"]!["progress"] = 0.0;

        tracker.Observe(status).Should().Be(expected);
        tracker.Observe(status).Should().Be("IDLE");
    }

    [Fact]
    public void StateTracker_KlippyShutdown_FailsAPrintButNotAnIdlePrinter()
    {
        var printing = new SnapmakerU1Connector.StateTracker();
        var status = Fixture("snapmaker-u1-printing.json");
        printing.Observe(status);
        status["webhooks"] = new JsonObject { ["state"] = "shutdown" };
        printing.Observe(status).Should().Be("FAILED");

        var idle = new SnapmakerU1Connector.StateTracker();
        var idleStatus = Fixture("snapmaker-u1-idle.json");
        idle.Observe(idleStatus);
        idleStatus["webhooks"] = new JsonObject { ["state"] = "shutdown" };
        idle.Observe(idleStatus).Should().BeNull();
    }
}
