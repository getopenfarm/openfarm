using System.Text.Json.Nodes;
using FluentAssertions;
using OpenFarm.Infrastructure.Printers.Snapmaker;

namespace OpenFarm.Infrastructure.Tests.Printers.Snapmaker;

public class U1StateTests
{
    [Fact]
    public void Filament_SparseRfidUpdatesKeepOtherChannelsAndDecodeUidAndColor()
    {
        var state = new U1FilamentState();
        state.Merge(JsonNode.Parse("""{"filament_detect":{"info":[{"MAIN_TYPE":"PETG-HF","RGB_1":16711680,"ALPHA":128,"CARD_UID":[1,2,255]},{"MAIN_TYPE":"PLA"}]},"filament_feed left":{"extruder0":{"filament_detected":true},"extruder1":{"filament_detected":true}}}"""));
        state.Merge(JsonNode.Parse("""{"filament_detect":{"info":[{"SUB_TYPE":"Fast"}]}}"""));
        var slots = state.Read(0).Single().Slots;
        slots[0].MaterialType.Should().Be("PETG");
        slots[0].ColorHex.Should().Be("FF000080");
        slots[0].RfidId.Should().Be("0102FF");
        slots[1].MaterialType.Should().Be("PLA");
        slots[0].RemainPercent.Should().BeNull();
    }

    [Fact]
    public void Filament_TemporaryDescriptionLossIsHeldUntilConfirmedUnload()
    {
        var state = new U1FilamentState();
        state.Merge(JsonNode.Parse("""{"filament_detect":{"info":[{"MAIN_TYPE":"PLA"}]},"filament_feed left":{"extruder0":{"filament_detected":true}}}"""));
        state.Read(null);
        state.Merge(JsonNode.Parse("""{"filament_detect":{"info":[{"MAIN_TYPE":"NONE"}]}}"""));
        state.Read(null).Single().Slots[0].MaterialType.Should().Be("PLA");
        state.Merge(JsonNode.Parse("""{"filament_feed left":{"extruder0":{"filament_detected":false}}}"""));
        state.Read(null).Single().Slots[0].Empty.Should().BeTrue();
        state.Merge(JsonNode.Parse("""{"filament_feed left":{"extruder0":{"filament_detected":true}}}"""));
        state.Read(null).Single().Slots[0].MaterialType.Should().BeNull();
    }

    [Fact]
    public void Filament_UserOverridesSupplyNonRfidDescriptions()
    {
        var state = new U1FilamentState { Overrides = JsonNode.Parse("""{"filament_type":["PC-ABS"],"filament_color_rgba":["aabbccff"]}""") };
        state.Merge(JsonNode.Parse("""{"filament_detect":{"info":[{}]},"filament_feed left":{"extruder0":{"filament_detected":true}}}"""));
        var slot = state.Read(null).Single().Slots[0];
        slot.MaterialType.Should().Be("PC");
        slot.ColorHex.Should().Be("AABBCCFF");
        slot.RfidId.Should().BeNull();
    }

    [Fact]
    public void Exceptions_DeduplicateAndClearOneShotsWithoutClearingActiveAlerts()
    {
        var tracker = new U1ExceptionTracker();
        var alert = JsonNode.Parse("""{"id":525,"index":1,"code":3,"level":2,"message":"Runout"}""");
        tracker.Notify(alert);
        tracker.Notify(alert);
        tracker.Alerts.Should().ContainSingle().Which.Severity.Should().Be("Error");
        tracker.SetActive(new JsonArray(alert!.DeepClone()));
        tracker.Observe("printing");
        tracker.Alerts.Should().ContainSingle().Which.Code.Should().Be("U1-0525-0001-0003");
        tracker.SetActive(new JsonArray());
        tracker.Alerts.Should().BeEmpty();
    }

    [Fact]
    public void Exceptions_KeepTenValidOneShotsAndDefaultToError()
    {
        var tracker = new U1ExceptionTracker();
        tracker.Notify(JsonNode.Parse("""{"id":"NaN","index":0,"code":1}"""));
        for (var i = 0; i < 12; i++) tracker.Notify(new JsonObject { ["id"] = 525, ["index"] = 0, ["code"] = i });
        tracker.Alerts.Should().HaveCount(10).And.OnlyContain(a => a.Severity == "Error");
        tracker.Observe("standby");
        tracker.Alerts.Should().BeEmpty();
    }

    [Fact]
    public async Task Upload_ProgressRenewsDeadlineBeyondItsInitialWindow()
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromMilliseconds(200));
        using var content = new SnapmakerU1Connector.UploadContent(new byte[1024 * 1024], null,
            () => timeout.CancelAfter(TimeSpan.FromMilliseconds(200)));
        await using var stream = new SlowStream();
        await content.CopyToAsync(stream, timeout.Token);
        stream.Length.Should().Be(1024 * 1024);
        stream.Writes.Should().Be(16);
        timeout.IsCancellationRequested.Should().BeFalse();
    }

    private sealed class SlowStream : MemoryStream
    {
        internal int Writes { get; private set; }
        public override async ValueTask WriteAsync(ReadOnlyMemory<byte> buffer, CancellationToken ct = default)
        {
            await Task.Delay(30, ct);
            Writes++;
            await base.WriteAsync(buffer, ct);
        }
    }

    [Fact]
    public void SparseLayerUpdate_PreservesTotalLayerAndOtherPrintFields()
    {
        var status = MoonrakerStatusMapperTests.Fixture("snapmaker-u1-printing.json");
        SnapmakerU1Connector.Merge(status, JsonNode.Parse("""{"print_stats":{"info":{"current_layer":13}}}"""));
        var telemetry = MoonrakerStatusMapper.Telemetry(status, "RUNNING");
        telemetry.CurrentLayer.Should().Be(13);
        telemetry.TotalLayers.Should().Be(80);
        telemetry.PrintProgressPercent.Should().Be(42);
    }

    [Fact]
    public void Metadata_UsesPrintableByteProgressAndSlicerTime()
    {
        var status = MoonrakerStatusMapperTests.Fixture("snapmaker-u1-printing.json");
        status["display_status"]!["progress"] = 0;
        status["virtual_sdcard"]!["file_position"] = 600;
        status["_openfarm_metadata"] = JsonNode.Parse("""{"gcode_start_byte":100,"gcode_end_byte":1100,"estimated_time":600}""");
        status["print_stats"]!["print_duration"] = 120;
        var telemetry = MoonrakerStatusMapper.Telemetry(status, "RUNNING");
        telemetry.PrintProgressPercent.Should().Be(50);
        telemetry.RemainingTimeMinutes.Should().Be(8);
    }
}
