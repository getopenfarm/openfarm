using FluentAssertions;
using OpenFarm.Domain.Production;

namespace OpenFarm.Domain.Tests.Production;

public class PrintFilamentFollowUpTests
{
    private static readonly Guid Spool = Guid.NewGuid();

    /// <summary>A one-tool external print that closes with its line unbound, as when no tray was seen.</summary>
    private static ExternalPrint ClosedUnbound(PrintJobStatus outcome = PrintJobStatus.Completed)
    {
        var print = ExternalPrint.Start(Guid.NewGuid(), "benchy", [new ExternalFilamentSpec(0, "PLA", "000000", 20m, null)]);
        if (outcome == PrintJobStatus.Completed) print.MarkCompleted();
        else print.MarkFailed();
        return print;
    }

    [Fact]
    public void ANewPrint_NeedsNoFollowUp_UntilALineIsSkipped()
    {
        var print = ClosedUnbound();

        print.NeedsFilamentFollowUp.Should().BeFalse();

        print.MarkFilamentSkipped(print.Filaments[0].Id, FilamentSkipReason.NoSpool);

        print.NeedsFilamentFollowUp.Should().BeTrue();
        print.Filaments[0].SkippedReason.Should().Be(FilamentSkipReason.NoSpool);
    }

    [Fact]
    public void RecordingConsumption_ClearsTheSkipReason()
    {
        var print = ClosedUnbound();
        var line = print.Filaments[0];
        print.MarkFilamentSkipped(line.Id, FilamentSkipReason.SpoolNotLoaded);

        print.RecordFilamentConsumption(line.Id, Spool, 20m);

        line.SkippedReason.Should().BeNull();
        print.NeedsFilamentFollowUp.Should().BeFalse();
    }

    [Fact]
    public void Dismissing_HidesTheFollowUp_AndIsIdempotent()
    {
        var print = ClosedUnbound();
        print.MarkFilamentSkipped(print.Filaments[0].Id, FilamentSkipReason.NoSpool);

        print.DismissFilamentFollowUp();
        var dismissedAt = print.FilamentFollowUpDismissedAtUtc;
        print.DismissFilamentFollowUp();

        print.NeedsFilamentFollowUp.Should().BeFalse();
        dismissedAt.Should().NotBeNull();
        print.FilamentFollowUpDismissedAtUtc.Should().Be(dismissedAt);
    }

    [Fact]
    public void BindLineSpool_BindsAnUnrecordedLineOfAClosedPrint()
    {
        var print = ClosedUnbound();

        print.BindLineSpool(print.Filaments[0].Id, Spool);

        print.Filaments[0].SpoolId.Should().Be(Spool);
    }

    [Fact]
    public void BindLineSpool_RejectsAPrintThatIsStillRunning()
    {
        var print = ExternalPrint.Start(Guid.NewGuid(), "benchy", [new ExternalFilamentSpec(0, "PLA", null, 20m, null)]);

        var bind = () => print.BindLineSpool(print.Filaments[0].Id, Spool);

        bind.Should().Throw<PrintFilamentNotAssignableException>();
    }

    [Fact]
    public void BindLineSpool_RejectsALineThatAlreadyHasConsumption()
    {
        var print = ClosedUnbound();
        print.RecordFilamentConsumption(print.Filaments[0].Id, Spool, 20m);

        var bind = () => print.BindLineSpool(print.Filaments[0].Id, Guid.NewGuid());

        bind.Should().Throw<PrintFilamentNotAssignableException>();
    }

    [Fact]
    public void BindLineSpool_RejectsAnUnknownLine()
    {
        var print = ClosedUnbound();

        var bind = () => print.BindLineSpool(Guid.NewGuid(), Spool);

        bind.Should().Throw<FilamentConsumptionSumMismatchException>();
    }

    [Fact]
    public void RecordStopProgress_KeepsWhatThePrinterLastReported()
    {
        var print = ClosedUnbound(PrintJobStatus.Failed);

        print.RecordStopProgress(30, 100, 90);

        print.StopLayer.Should().Be(30);
        print.StopTotalLayers.Should().Be(100);
        print.StopPercent.Should().Be(90);
    }
}
