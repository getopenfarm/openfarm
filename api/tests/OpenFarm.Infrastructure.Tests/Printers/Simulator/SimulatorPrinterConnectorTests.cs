using FluentAssertions;
using OpenFarm.Application.Printers;
using OpenFarm.Domain.Printers;
using OpenFarm.Infrastructure.Printers.Simulator;

namespace OpenFarm.Infrastructure.Tests.Printers.Simulator;

public class SimulatorPrinterConnectorTests
{
    // A 50ms cycle (10ms ticks) used to be tight enough that CI runner contention (thread-pool
    // scheduling, GC pauses) could delay a single PeriodicTimer continuation past the entire
    // cycle, so Advance() jumped straight from Idle to AwaitingClear within one tick and the
    // RUNNING phase was never observed — same class of flake fixed in
    // PrinterConnectionBackgroundServiceTests' telemetry coalescing test. Widened to a 2s cycle
    // (200ms ticks) for comfortable margin while staying well under this file's 5s timeouts.
    private static readonly TimeSpan ShortCycleDuration = TimeSpan.FromSeconds(2);

    [Fact]
    public async Task VerifyAsync_AnyAccessCodeExceptSentinel_ReturnsTrue()
    {
        var connector = new SimulatorPrinterConnector();
        var details = new PrinterConnectionDetails("System", "Simulation", "10.0.0.1", "SN-1", "12345678");

        var verified = await connector.VerifyAsync(details, CancellationToken.None);

        verified.Should().BeTrue();
    }

    [Fact]
    public async Task VerifyAsync_WrongSentinelAccessCode_ReturnsFalse()
    {
        var connector = new SimulatorPrinterConnector();
        var details = new PrinterConnectionDetails("System", "Simulation", "10.0.0.1", "SN-1", "wrong");

        var verified = await connector.VerifyAsync(details, CancellationToken.None);

        verified.Should().BeFalse();
    }

    [Fact]
    public async Task ConnectAsync_OnConnect_YieldsImmediateIdleEventWithBaselineTelemetry()
    {
        var connector = new SimulatorPrinterConnector();
        var details = new PrinterConnectionDetails("System", "Simulation", "10.0.0.1", "SN-2", "12345678");
        using var cts = new CancellationTokenSource();

        var events = await TakeAsync(connector.ConnectAsync(details, cts.Token), 1);
        var firstEvent = events[0];

        firstEvent.GcodeState.Should().Be("IDLE");
        firstEvent.Telemetry.Should().NotBeNull();
        firstEvent.Telemetry!.PrintProgressPercent.Should().Be(0);
        firstEvent.Telemetry.AmsUnits.Should().NotBeEmpty();
    }

    [Fact]
    public async Task ConnectAsync_WithNoStartSimulatedJobCall_StaysIdleAcrossSeveralTicks()
    {
        var connector = new SimulatorPrinterConnector(cycleDuration: ShortCycleDuration);
        var details = new PrinterConnectionDetails("System", "Simulation", "10.0.0.1", "SN-3", "12345678");
        using var cts = new CancellationTokenSource();

        var events = await TakeAsync(connector.ConnectAsync(details, cts.Token), 5);

        events.Should().OnlyContain(e => e.GcodeState == null || e.GcodeState == "IDLE");
    }

    [Fact]
    public async Task ConnectAsync_AfterStartSimulatedJob_EmitsRunningThenFinishExactlyOnceWithMonotonicProgress()
    {
        var connector = new SimulatorPrinterConnector(cycleDuration: ShortCycleDuration);
        var details = new PrinterConnectionDetails("System", "Simulation", "10.0.0.1", "SN-4", "12345678");
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));

        var events = new List<PrinterConnectorEvent>();
        var jobStarted = false;

        await foreach (var connectorEvent in connector.ConnectAsync(details, cts.Token))
        {
            events.Add(connectorEvent);

            if (!jobStarted)
            {
                jobStarted = true;
                connector.StartSimulatedJob(details.SerialNumber);
            }

            // A few ticks past the first FINISH, so we can also assert it isn't re-asserted.
            if (events.Any(e => e.GcodeState == "FINISH") && events.Count >= 15)
            {
                break;
            }

            if (events.Count >= 60)
            {
                break; // safety valve — should never be hit if the cycle completes as designed.
            }
        }

        var gcodeStates = events.Where(e => e.GcodeState is not null).Select(e => e.GcodeState!).ToList();
        gcodeStates.Should().ContainInOrder("IDLE", "RUNNING", "FINISH");
        gcodeStates.Count(s => s == "FINISH").Should().Be(1, "FINISH must not be re-asserted once AwaitingClear is reached");

        events.Select(e => e.Telemetry!.PrintProgressPercent!.Value).Should().BeInAscendingOrder();
        events.Select(e => e.Telemetry!.NozzleTempC!.Value).Should().BeInAscendingOrder();
        events.Select(e => e.Telemetry!.BedTempC!.Value).Should().BeInAscendingOrder();
    }

    [Fact]
    public async Task ConnectAsync_IdleState_ReportsFixedFourTrayAmsFixtureUnchangedAcrossTicks()
    {
        var connector = new SimulatorPrinterConnector(cycleDuration: ShortCycleDuration);
        var details = new PrinterConnectionDetails("System", "Simulation", "10.0.0.1", "SN-5", "12345678");
        using var cts = new CancellationTokenSource();

        var events = await TakeAsync(connector.ConnectAsync(details, cts.Token), 5);

        var expectedTrays = new[]
        {
            new AmsTray(0, Empty: false, MaterialType: "PLA", ColorHex: "#E53935", SubBrand: "Generic", RemainPercent: 82, Active: false,
                RfidId: "SN-5-0", RfidWeightGrams: 1000),
            new AmsTray(1, Empty: false, MaterialType: "PETG", ColorHex: "#1E88E5", SubBrand: "Generic", RemainPercent: 45, Active: false,
                RfidId: "SN-5-1", RfidWeightGrams: 1000),
            new AmsTray(2, Empty: false, MaterialType: "ABS", ColorHex: "#43A047", SubBrand: "Generic", RemainPercent: 90, Active: false),
            new AmsTray(3, Empty: false, MaterialType: "TPU", ColorHex: "#FDD835", SubBrand: "Generic", RemainPercent: 12, Active: false),
        };

        foreach (var connectorEvent in events)
        {
            connectorEvent.Telemetry!.AmsUnits.Should().ContainSingle();
            connectorEvent.Telemetry.AmsUnits[0].UnitIndex.Should().Be(0);
            connectorEvent.Telemetry.AmsUnits[0].Trays.Should().BeEquivalentTo(expectedTrays);
        }
    }

    internal static async Task<List<T>> TakeAsync<T>(IAsyncEnumerable<T> source, int count)
    {
        var results = new List<T>();
        await foreach (var item in source)
        {
            results.Add(item);
            if (results.Count >= count)
            {
                break;
            }
        }

        return results;
    }
}
