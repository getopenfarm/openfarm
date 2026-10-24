using FluentAssertions;
using OpenFarm.Domain.FilamentTracking;
using OpenFarm.Domain.Printers;

namespace OpenFarm.Domain.Tests.Printers;

public class PrinterTests
{
    private static Printer CreateValidPrinter() => Printer.Create(
        name: "Printer 1",
        brand: "Bambu",
        modelCode: "P1S",
        ipAddress: "192.168.1.50",
        serialNumber: "01P00A123456789");

    [Fact]
    public void Create_WithValidArguments_SetsAllFieldsAndDefaults()
    {
        var beforeCreate = DateTimeOffset.UtcNow;

        var printer = CreateValidPrinter();

        printer.Id.Should().NotBeEmpty();
        printer.Name.Should().Be("Printer 1");
        printer.Brand.Should().Be("Bambu");
        printer.ModelCode.Should().Be("P1S");
        printer.IpAddress.Should().Be("192.168.1.50");
        printer.SerialNumber.Should().Be("01P00A123456789");
        printer.Status.Should().Be(PrinterStatus.Idle);
        printer.IsReachable.Should().BeFalse();
        printer.LastMessageUtc.Should().BeNull();
        printer.FeedReadings.Should().BeEmpty();
        printer.Extruders.Should().BeEmpty();
        printer.CreatedAtUtc.Should().BeOnOrAfter(beforeCreate);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(null)]
    public void Create_WithEmptyName_Throws(string? name)
    {
        var act = () => Printer.Create(name!, "Bambu", "P1S", "192.168.1.50", "SN1");

        act.Should().Throw<ArgumentException>();
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(null)]
    public void Create_WithEmptyIpAddress_Throws(string? ipAddress)
    {
        var act = () => Printer.Create("Printer 1", "Bambu", "P1S", ipAddress!, "SN1");

        act.Should().Throw<ArgumentException>();
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(null)]
    public void Create_WithEmptySerialNumber_Throws(string? serialNumber)
    {
        var act = () => Printer.Create("Printer 1", "Bambu", "P1S", "192.168.1.50", serialNumber!);

        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void Create_WithUnknownModel_ThrowsUnknownPrinterModelException()
    {
        var act = () => Printer.Create("Printer 1", "Bambu", "NotAModel", "192.168.1.50", "SN1");

        act.Should().Throw<UnknownPrinterModelException>();
    }

    [Fact]
    public void Printer_HasNoSettersOrUpdateMethod()
    {
        var type = typeof(Printer);

        type.GetProperties()
            .Should().OnlyContain(p => p.SetMethod == null || !p.SetMethod.IsPublic);

        type.GetMethods()
            .Should().NotContain(m => m.Name.StartsWith("Update", StringComparison.Ordinal));
    }

    [Fact]
    public void TransitionTo_FromAwaitingClearToIdle_UpdatesStatus()
    {
        var printer = CreateValidPrinter();
        printer.ObserveStatus("FINISH");

        printer.TransitionTo(PrinterStatus.Idle);

        printer.Status.Should().Be(PrinterStatus.Idle);
    }

    [Fact]
    public void TransitionTo_IllegalTransition_ThrowsAndLeavesStatusUnchanged()
    {
        var printer = CreateValidPrinter();

        var act = () => printer.TransitionTo(PrinterStatus.AwaitingClear);

        act.Should().Throw<InvalidPrinterStatusTransitionException>();
        printer.Status.Should().Be(PrinterStatus.Idle);
    }

    [Fact]
    public void ObserveStatus_WithMappedGcodeState_ReconcilesRegardlessOfCurrentStatus()
    {
        var printer = CreateValidPrinter();

        var changed = printer.ObserveStatus("RUNNING");

        changed.Should().BeTrue();
        printer.Status.Should().Be(PrinterStatus.Printing);
    }

    [Fact]
    public void ObserveStatus_WithUnmappedGcodeState_DoesNotChangeStatusOrThrow()
    {
        var printer = CreateValidPrinter();

        var act = () => printer.ObserveStatus("OFFLINE");

        act.Should().NotThrow().Which.Should().BeFalse();
        printer.Status.Should().Be(PrinterStatus.Idle);
    }

    [Fact]
    public void MarkReachable_SetsIsReachableAndLastMessageUtc()
    {
        var printer = CreateValidPrinter();
        var now = DateTimeOffset.UtcNow;

        printer.MarkReachable(now);

        printer.IsReachable.Should().BeTrue();
        printer.LastMessageUtc.Should().Be(now);
    }

    [Fact]
    public void MarkUnreachable_SetsIsReachableFalseWithoutTouchingStatus()
    {
        var printer = CreateValidPrinter();
        printer.MarkReachable(DateTimeOffset.UtcNow);
        printer.ObserveStatus("RUNNING");

        printer.MarkUnreachable();

        printer.IsReachable.Should().BeFalse();
        printer.Status.Should().Be(PrinterStatus.Printing);
    }

    [Fact]
    public void RecordTelemetry_WithFullSnapshot_SetsAllFields()
    {
        var printer = CreateValidPrinter();
        var readings = new List<FeedUnitReading> { new(FeedUnitKindCatalog.MultiMaterialUnit, 0, []) };

        printer.RecordTelemetry(60.5m, 65.0m, 42, "Printing", 72, "benchy.3mf", readings);

        printer.NozzleTempC.Should().Be(60.5m);
        printer.BedTempC.Should().Be(65.0m);
        printer.PrintProgressPercent.Should().Be(42);
        printer.CurrentStageLabel.Should().Be("Printing");
        printer.RemainingTimeMinutes.Should().Be(72);
        printer.CurrentJobName.Should().Be("benchy.3mf");
        printer.FeedReadings.Should().BeEquivalentTo(readings);
    }

    [Fact]
    public void RecordTelemetry_WithSubsequentPartialSnapshot_PreservesFieldsMissingFromDelta()
    {
        var printer = CreateValidPrinter();
        var readings = new List<FeedUnitReading> { new(FeedUnitKindCatalog.MultiMaterialUnit, 0, []) };
        printer.RecordTelemetry(60.5m, 65.0m, 42, "Printing", 72, "benchy.3mf", readings);

        // Bambu's MQTT deltas omit fields that haven't changed since the last message — a
        // subsequent null/empty here must not clear the previously recorded value.
        printer.RecordTelemetry(null, null, null, null, null, null, []);

        printer.NozzleTempC.Should().Be(60.5m);
        printer.BedTempC.Should().Be(65.0m);
        printer.PrintProgressPercent.Should().Be(42);
        printer.CurrentStageLabel.Should().Be("Printing");
        printer.RemainingTimeMinutes.Should().Be(72);
        printer.CurrentJobName.Should().Be("benchy.3mf");
        printer.FeedReadings.Should().BeEquivalentTo(readings);
    }

    [Fact]
    public void RecordTelemetry_ReplacesOnlyTheUnitKindsADeltaReports()
    {
        var printer = CreateValidPrinter();
        printer.RecordTelemetry(null, null, null, null, null, null, [Ams(0), Ams(1)]);

        printer.RecordTelemetry(null, null, null, null, null, null, [External()]);
        printer.RecordTelemetry(null, null, null, null, null, null, [Ams(0)]);

        printer.FeedReadings.Select(u => (u.Kind, u.UnitIndex)).Should().BeEquivalentTo(
            [(FeedUnitKindCatalog.ExternalSpool, 0), (FeedUnitKindCatalog.MultiMaterialUnit, 0)]);
    }

    [Fact]
    public void RecordTelemetry_WithPartialSnapshot_OverwritesOnlyFieldsPresentInDelta()
    {
        var printer = CreateValidPrinter();
        printer.RecordTelemetry(60.5m, 65.0m, 42, "Printing", 72, "benchy.3mf", []);

        printer.RecordTelemetry(61.0m, null, null, null, 60, null, []);

        printer.NozzleTempC.Should().Be(61.0m);
        printer.BedTempC.Should().Be(65.0m);
        printer.PrintProgressPercent.Should().Be(42);
        printer.CurrentStageLabel.Should().Be("Printing");
        printer.RemainingTimeMinutes.Should().Be(60);
        printer.CurrentJobName.Should().Be("benchy.3mf");
    }

    private static FeedSlotReading Slot(int position, int extruder = 0) => new(position, false, "PLA", "#FFFFFF", null, 50, false, ExtruderIndex: extruder);

    private static FeedUnitReading Ams(int index, int slots = 4) =>
        new(FeedUnitKindCatalog.MultiMaterialUnit, index, Enumerable.Range(0, slots).Select(position => Slot(position)).ToList());

    private static FeedUnitReading External(int extruder = 0) =>
        new(FeedUnitKindCatalog.ExternalSpool, extruder, [new FeedSlotReading(0, false, "PLA", "#FFFFFF", null, null, false, ExtruderIndex: extruder)]);

    private static FeedSlotRef AmsSlot(int unit, int slot) => new(FeedUnitKindCatalog.MultiMaterialUnit, unit, slot);

    private static readonly FeedSlotRef ExternalPort = new(FeedUnitKindCatalog.ExternalSpool, 0, 0);

    private static string[] ActiveAmsSlots(Printer printer) => printer.FeedReadings.Where(u => u.Kind == FeedUnitKindCatalog.MultiMaterialUnit)
        .SelectMany(u => u.Slots.Where(s => s.Active).Select(s => $"{u.UnitIndex}:{s.SlotPosition}")).ToArray();

    private static bool ExternalActive(Printer printer, int extruder = 0) =>
        printer.FeedReadings.Single(u => u.Kind == FeedUnitKindCatalog.ExternalSpool && u.UnitIndex == extruder).Slots.Single().Active;

    [Theory]
    [InlineData(0, 0, "0:0")]
    [InlineData(1, 2, "1:2")]
    [InlineData(128, 0, "128:0")]
    public void RecordTelemetry_MarksTheSlotThePrinterReportsFeeding(int unit, int slot, string expected)
    {
        var printer = CreateValidPrinter();

        printer.RecordTelemetry(null, null, null, null, null, null, [Ams(0), Ams(1), Ams(128, 1), External()], activeSlots: [AmsSlot(unit, slot)]);

        ActiveAmsSlots(printer).Should().Equal(expected);
        ExternalActive(printer).Should().BeFalse();
    }

    [Fact]
    public void RecordTelemetry_AppliesAnActiveSlotArrivingAloneToTheStoredUnits()
    {
        var printer = CreateValidPrinter();
        printer.RecordTelemetry(null, null, null, null, null, null, [Ams(0), Ams(1), External()]);
        ActiveAmsSlots(printer).Should().BeEmpty();

        printer.RecordTelemetry(null, null, null, null, null, null, [], activeSlots: [AmsSlot(1, 1)]);
        ActiveAmsSlots(printer).Should().Equal("1:1");

        printer.RecordTelemetry(null, null, null, null, null, null, [], activeSlots: [AmsSlot(0, 2)]);
        ActiveAmsSlots(printer).Should().Equal("0:2");
    }

    [Fact]
    public void RecordTelemetry_AppliesTheStoredActiveSlotToAnAmsArrivingLater()
    {
        var printer = CreateValidPrinter();
        printer.RecordTelemetry(null, null, null, null, null, null, [], activeSlots: [AmsSlot(0, 1)]);

        printer.RecordTelemetry(null, null, null, null, null, null, [Ams(0)]);

        ActiveAmsSlots(printer).Should().Equal("0:1");
    }

    [Fact]
    public void RecordTelemetry_ExternalSpoolIsTheActiveFeedWhenThePrinterSaysSo()
    {
        var printer = CreateValidPrinter();

        printer.RecordTelemetry(null, null, null, null, null, null, [Ams(0), External()], activeSlots: [ExternalPort]);

        ExternalActive(printer).Should().BeTrue();
        ActiveAmsSlots(printer).Should().BeEmpty();
    }

    [Fact]
    public void RecordTelemetry_NothingLoadedMarksNoSlotOnAnAmsPrinter()
    {
        var printer = CreateValidPrinter();
        printer.RecordTelemetry(null, null, null, null, null, null, [Ams(0), External()], activeSlots: [ExternalPort]);

        printer.RecordTelemetry(null, null, null, null, null, null, [], activeSlots: [null]);

        ExternalActive(printer).Should().BeFalse();
        ActiveAmsSlots(printer).Should().BeEmpty();
    }

    public static TheoryData<FeedSlotRef?[]?> AnyActiveSlotReport => new() { null, new FeedSlotRef?[] { ExternalPort }, new FeedSlotRef?[] { null } };

    [Theory]
    [MemberData(nameof(AnyActiveSlotReport))]
    public void RecordTelemetry_ExternalSpoolIsAlwaysActiveWithoutAnAms(FeedSlotRef?[]? activeSlots)
    {
        var printer = CreateValidPrinter();

        printer.RecordTelemetry(null, null, null, null, null, null, [External()], activeSlots: activeSlots);

        ExternalActive(printer).Should().BeTrue();
    }

    [Fact]
    public void RecordTelemetry_MarksNothingOnAnAmsPrinterUntilItSaysWhichSlotFeeds()
    {
        var printer = CreateValidPrinter();
        var reported = new FeedUnitReading(FeedUnitKindCatalog.MultiMaterialUnit, 0, [Slot(0) with { Active = true }, Slot(1)]);

        printer.RecordTelemetry(null, null, null, null, null, null, [reported, External()]);

        ActiveAmsSlots(printer).Should().BeEmpty();
        ExternalActive(printer).Should().BeFalse();
    }

    [Fact]
    public void RecordTelemetry_AnExtruderNoAmsFeedsUsesItsOwnExternalSpool()
    {
        var printer = CreateValidPrinter();

        // The AMS feeds extruder 0; extruder 1 has only its external spool.
        printer.RecordTelemetry(null, null, null, null, null, null, [Ams(0), External(0), External(1)], activeSlots: [AmsSlot(0, 3), null]);

        ActiveAmsSlots(printer).Should().Equal("0:3");
        ExternalActive(printer, 0).Should().BeFalse();
        ExternalActive(printer, 1).Should().BeTrue();
    }

    [Fact]
    public void RecordTelemetry_AToolchangerMarksOnlyTheHeadThatIsFeeding()
    {
        var printer = CreateValidPrinter();
        var heads = new FeedUnitReading(FeedUnitKindCatalog.MultiMaterialUnit, 0, Enumerable.Range(0, 4).Select(head => Slot(head, head)).ToList());

        printer.RecordTelemetry(null, null, null, null, null, null, [heads], activeSlots: [null, null, AmsSlot(0, 2), null]);

        ActiveAmsSlots(printer).Should().Equal("0:2");
    }

    [Fact]
    public void Rename_WithValidName_SetsName()
    {
        var printer = CreateValidPrinter();

        printer.Rename("Printer 1 (renamed)");

        printer.Name.Should().Be("Printer 1 (renamed)");
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(null)]
    public void Rename_WithEmptyName_ThrowsAndLeavesNameUnchanged(string? name)
    {
        var printer = CreateValidPrinter();

        var act = () => printer.Rename(name!);

        act.Should().Throw<ArgumentException>();
        printer.Name.Should().Be("Printer 1");
    }

    [Theory]
    [InlineData(PrinterStatus.Idle)]
    [InlineData(PrinterStatus.Printing)]
    [InlineData(PrinterStatus.AwaitingClear)]
    public void Rename_IsNeverBlockedByStatus(PrinterStatus status)
    {
        var printer = CreateValidPrinter();
        if (status == PrinterStatus.Printing)
        {
            printer.ObserveStatus("RUNNING");
        }
        else if (status == PrinterStatus.AwaitingClear)
        {
            printer.ObserveStatus("FINISH");
        }

        printer.Rename("Renamed While " + status);

        printer.Name.Should().Be("Renamed While " + status);
        printer.Status.Should().Be(status);
    }
}
