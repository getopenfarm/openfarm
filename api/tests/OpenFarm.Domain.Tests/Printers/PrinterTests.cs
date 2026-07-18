using FluentAssertions;
using OpenFarm.Domain.Printers;

namespace OpenFarm.Domain.Tests.Printers;

public class PrinterTests
{
    private static readonly Guid OrganizationId = Guid.NewGuid();

    private static Printer CreateValidPrinter() => Printer.Create(
        OrganizationId,
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
        printer.OrganizationId.Should().Be(OrganizationId);
        printer.Name.Should().Be("Printer 1");
        printer.Brand.Should().Be("Bambu");
        printer.ModelCode.Should().Be("P1S");
        printer.IpAddress.Should().Be("192.168.1.50");
        printer.SerialNumber.Should().Be("01P00A123456789");
        printer.Status.Should().Be(PrinterStatus.Idle);
        printer.IsReachable.Should().BeFalse();
        printer.LastMessageUtc.Should().BeNull();
        printer.AmsUnits.Should().BeEmpty();
        printer.CreatedAtUtc.Should().BeOnOrAfter(beforeCreate);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(null)]
    public void Create_WithEmptyName_Throws(string? name)
    {
        var act = () => Printer.Create(OrganizationId, name!, "Bambu", "P1S", "192.168.1.50", "SN1");

        act.Should().Throw<ArgumentException>();
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(null)]
    public void Create_WithEmptyIpAddress_Throws(string? ipAddress)
    {
        var act = () => Printer.Create(OrganizationId, "Printer 1", "Bambu", "P1S", ipAddress!, "SN1");

        act.Should().Throw<ArgumentException>();
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(null)]
    public void Create_WithEmptySerialNumber_Throws(string? serialNumber)
    {
        var act = () => Printer.Create(OrganizationId, "Printer 1", "Bambu", "P1S", "192.168.1.50", serialNumber!);

        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void Create_WithUnknownModel_ThrowsUnknownPrinterModelException()
    {
        var act = () => Printer.Create(OrganizationId, "Printer 1", "Bambu", "NotAModel", "192.168.1.50", "SN1");

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
        var amsUnits = new List<AmsUnit> { new(0, []) };

        printer.RecordTelemetry(60.5m, 65.0m, 42, "Printing", 72, "benchy.3mf", amsUnits);

        printer.NozzleTempC.Should().Be(60.5m);
        printer.BedTempC.Should().Be(65.0m);
        printer.PrintProgressPercent.Should().Be(42);
        printer.CurrentStageLabel.Should().Be("Printing");
        printer.RemainingTimeMinutes.Should().Be(72);
        printer.CurrentJobName.Should().Be("benchy.3mf");
        printer.AmsUnits.Should().BeEquivalentTo(amsUnits);
    }

    [Fact]
    public void RecordTelemetry_WithSubsequentPartialSnapshot_PreservesFieldsMissingFromDelta()
    {
        var printer = CreateValidPrinter();
        var amsUnits = new List<AmsUnit> { new(0, []) };
        printer.RecordTelemetry(60.5m, 65.0m, 42, "Printing", 72, "benchy.3mf", amsUnits);

        // Bambu's MQTT deltas omit fields that haven't changed since the last message — a
        // subsequent null/empty here must not clear the previously recorded value.
        printer.RecordTelemetry(null, null, null, null, null, null, []);

        printer.NozzleTempC.Should().Be(60.5m);
        printer.BedTempC.Should().Be(65.0m);
        printer.PrintProgressPercent.Should().Be(42);
        printer.CurrentStageLabel.Should().Be("Printing");
        printer.RemainingTimeMinutes.Should().Be(72);
        printer.CurrentJobName.Should().Be("benchy.3mf");
        printer.AmsUnits.Should().BeEquivalentTo(amsUnits);
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
