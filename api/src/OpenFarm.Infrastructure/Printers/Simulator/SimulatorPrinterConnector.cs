using System.Collections.Concurrent;
using System.Runtime.CompilerServices;
using OpenFarm.Application.Printers;
using OpenFarm.Domain.Printers;

namespace OpenFarm.Infrastructure.Printers.Simulator;

/// <summary>
/// Built-in <see cref="IPrinterConnector"/> for printers registered with Brand "System" / ModelCode
/// "Simulation" — the dev/demo/test double that replaces <c>FakePrinterConnector</c>. Registered as a
/// singleton (<c>Program.cs</c>) so its in-memory
/// per-serial state is shared across the long-lived <c>PrinterConnectionBackgroundService</c> loop and
/// short-lived callers (tests, a future job-dispatch handler).
///
/// Verify-connection convention: a connection is unverifiable when
/// <see cref="PrinterConnectionDetails.AccessCode"/> is exactly <c>"wrong"</c>, mirroring
/// <c>FakePrinterConnector</c>'s existing sentinel exactly (research.md §7).
/// </summary>
public sealed class SimulatorPrinterConnector : IPrinterConnector
{
    private const string UnverifiableAccessCodeSentinel = "wrong";
    private static readonly TimeSpan DefaultCycleDuration = TimeSpan.FromSeconds(45);
    private static readonly TimeSpan MinTickInterval = TimeSpan.FromMilliseconds(10);
    private const decimal PrintingNozzleTargetC = 220m;
    private const decimal PrintingBedTargetC = 60m;

    private readonly TimeSpan _cycleDuration;
    private readonly TimeSpan _tickInterval;
    private readonly ConcurrentDictionary<string, SimulatedPrinterState> _states = new();
    private readonly ConcurrentDictionary<string, bool> _activeConnections = new();

    public SimulatorPrinterConnector(TimeSpan? cycleDuration = null)
    {
        _cycleDuration = cycleDuration ?? DefaultCycleDuration;
        var proportionalTick = TimeSpan.FromMilliseconds(_cycleDuration.TotalMilliseconds / 10);
        _tickInterval = proportionalTick > MinTickInterval ? proportionalTick : MinTickInterval;
    }

    public Task<bool> VerifyAsync(PrinterConnectionDetails details, CancellationToken cancellationToken) =>
        Task.FromResult(!string.Equals(details.AccessCode, UnverifiableAccessCodeSentinel, StringComparison.Ordinal));

    public async IAsyncEnumerable<PrinterConnectorEvent> ConnectAsync(
        PrinterConnectionDetails details,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        var state = GetOrCreateState(details.SerialNumber);
        _activeConnections[details.SerialNumber] = true;

        try
        {
            var lastEmittedStatus = state.Status;
            yield return BuildEvent(state, StatusToGcodeState(lastEmittedStatus));

            using var timer = new PeriodicTimer(_tickInterval);
            while (await timer.WaitForNextTickAsync(cancellationToken))
            {
                Advance(state);

                // Only re-assert a status string on a genuine phase transition — re-asserting
                // "FINISH" on every tick once AwaitingClear is reached would flip a manually
                // Mark-Ready'd printer straight back (data-model.md's status-flapping-avoidance note).
                string? gcodeState = null;
                if (state.Status != lastEmittedStatus)
                {
                    gcodeState = StatusToGcodeState(state.Status);
                    lastEmittedStatus = state.Status;
                }

                yield return BuildEvent(state, gcodeState);
            }
        }
        finally
        {
            _activeConnections.TryRemove(details.SerialNumber, out _);
        }
    }

    /// <summary>
    /// Test-only entry point standing in for a future real job-dispatch command
    /// (contracts/IPrinterConnectorFactory.md) — deliberately kept off the shared
    /// <see cref="IPrinterConnector"/> interface (research.md §3). Unconditionally resets the serial's
    /// state to Printing regardless of whatever was last reported, so a fresh cycle always starts
    /// clean. No-op if the serial has no active <see cref="ConnectAsync"/> connection.
    /// </summary>
    public void StartSimulatedJob(string serialNumber)
    {
        if (!_activeConnections.ContainsKey(serialNumber) || !_states.TryGetValue(serialNumber, out var state))
        {
            return;
        }

        state.Status = PrinterStatus.Printing;
        state.JobStartedAtUtc = DateTimeOffset.UtcNow;
        state.PrintProgressPercent = 0;
    }

    private SimulatedPrinterState GetOrCreateState(string serialNumber) =>
        _states.GetOrAdd(serialNumber, static (sn, cycleDuration) => new SimulatedPrinterState
        {
            SerialNumber = sn,
            CycleDuration = cycleDuration,
        }, _cycleDuration);

    private static void Advance(SimulatedPrinterState state)
    {
        if (state.Status != PrinterStatus.Printing || state.JobStartedAtUtc is not { } startedAt)
        {
            return;
        }

        var fraction = Math.Clamp((DateTimeOffset.UtcNow - startedAt) / state.CycleDuration, 0.0, 1.0);

        state.PrintProgressPercent = (int)Math.Round(fraction * 100);
        state.NozzleTempC = Lerp(25m, PrintingNozzleTargetC, fraction);
        state.BedTempC = Lerp(25m, PrintingBedTargetC, fraction);

        if (fraction >= 1.0)
        {
            state.Status = PrinterStatus.AwaitingClear;
        }
    }

    private static decimal Lerp(decimal from, decimal to, double fraction) =>
        from + ((to - from) * (decimal)fraction);

    private static PrinterConnectorEvent BuildEvent(SimulatedPrinterState state, string? gcodeState) =>
        new(
            DateTimeOffset.UtcNow,
            GcodeState: gcodeState,
            Telemetry: new PrinterTelemetrySnapshot(
                state.NozzleTempC,
                state.BedTempC,
                state.PrintProgressPercent,
                CurrentStageLabel: null,
                state.AmsUnits));

    private static string StatusToGcodeState(PrinterStatus status) => status switch
    {
        PrinterStatus.Idle => "IDLE",
        PrinterStatus.Printing => "RUNNING",
        PrinterStatus.AwaitingClear => "FINISH",
        _ => "IDLE",
    };
}
