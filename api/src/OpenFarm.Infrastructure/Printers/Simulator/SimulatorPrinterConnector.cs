using System.Collections.Concurrent;
using System.Runtime.CompilerServices;
using OpenFarm.Application.Printers;
using OpenFarm.Domain.FilamentTracking;
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
    // Illustrative wall-clock length the reported ETA counts down from — decoupled from the (short)
    // simulation cycle so the card shows a realistic-looking "Xh Ym" remaining rather than seconds.
    private const int SimulatedJobTotalMinutes = 120;
    private const string SimulatedJobName = "benchy.3mf";

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
            yield return BuildEvent(state, StatusToGcodeState(state.Status));

            using var timer = new PeriodicTimer(_tickInterval);
            while (await timer.WaitForNextTickAsync(cancellationToken))
            {
                Advance(state);

                // Only re-assert a raw gcode state on a genuine phase transition — re-asserting
                // "FINISH" on every tick once AwaitingClear is reached would flip a manually
                // Mark-Ready'd printer straight back (data-model.md's status-flapping-avoidance note).
                // A pause/resume flip is one such transition even though it never changes Status
                // itself (research.md R12) — PendingGcodeState carries that case; Advance's own
                // Printing->AwaitingClear transition also queues one, consumed here identically.
                var gcodeState = state.PendingGcodeState;
                state.PendingGcodeState = null;

                yield return BuildEvent(state, gcodeState);
            }
        }
        finally
        {
            _activeConnections.TryRemove(details.SerialNumber, out _);
        }
    }

    /// <inheritdoc />
    /// <remarks>No real file transfer — begins a fake Printing → Finish sequence identical to
    /// <see cref="StartSimulatedJob"/>, through the interface real dispatch code calls (research.md R5).</remarks>
    public Task StartJobAsync(PrinterConnectionDetails details, Stream fileContent, string jobName, FilamentFeedMapping? feedMapping, CancellationToken cancellationToken)
    {
        var state = GetOrCreateState(details.SerialNumber);
        state.Status = PrinterStatus.Printing;
        state.IsPaused = false;
        state.JobStartedAtUtc = DateTimeOffset.UtcNow;
        state.PrintProgressPercent = 0;
        state.CurrentJobName = jobName;
        state.RemainingTimeMinutes = SimulatedJobTotalMinutes;
        state.PendingGcodeState = "RUNNING";
        return Task.CompletedTask;
    }

    public Task PauseJobAsync(PrinterConnectionDetails details, CancellationToken cancellationToken)
    {
        if (_states.TryGetValue(details.SerialNumber, out var state) && state.Status == PrinterStatus.Printing)
        {
            state.IsPaused = true;
            state.PendingGcodeState = "PAUSE";
        }

        return Task.CompletedTask;
    }

    public Task ResumeJobAsync(PrinterConnectionDetails details, CancellationToken cancellationToken)
    {
        if (_states.TryGetValue(details.SerialNumber, out var state) && state.IsPaused)
        {
            state.IsPaused = false;
            state.PendingGcodeState = "RUNNING";
        }

        return Task.CompletedTask;
    }

    /// <inheritdoc />
    /// <remarks>Emits a generic terminal "FAILED" gcode — the disambiguation between a requested stop
    /// and a genuine fault is <c>IJobTelemetrySync</c>'s job, keyed off <c>CancelRequestedAtUtc</c>,
    /// not something the connector layer encodes (research.md R12, FR-024c).</remarks>
    public Task StopJobAsync(PrinterConnectionDetails details, CancellationToken cancellationToken)
    {
        if (_states.TryGetValue(details.SerialNumber, out var state))
        {
            state.Status = PrinterStatus.AwaitingClear;
            state.IsPaused = false;
            state.RemainingTimeMinutes = 0;
            state.PendingGcodeState = "FAILED";
        }

        return Task.CompletedTask;
    }

    /// <summary>
    /// Test-only hook standing in for a stop performed directly at the printer, bypassing
    /// <see cref="StopJobAsync"/> entirely (quickstart.md Story 8 step 4) — no <c>CancelRequestedAtUtc</c>
    /// is ever set on the OpenFarm side, exercising the spec Assumptions' documented "no distinguishing
    /// signal" fallback to <c>Failed</c>.
    /// </summary>
    public void SimulateDirectStop(string serialNumber)
    {
        if (_states.TryGetValue(serialNumber, out var state))
        {
            state.Status = PrinterStatus.AwaitingClear;
            state.IsPaused = false;
            state.RemainingTimeMinutes = 0;
            state.PendingGcodeState = "FAILED";
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
        state.IsPaused = false;
        state.JobStartedAtUtc = DateTimeOffset.UtcNow;
        state.PrintProgressPercent = 0;
        state.CurrentJobName = SimulatedJobName;
        state.RemainingTimeMinutes = SimulatedJobTotalMinutes;
        state.PendingGcodeState = "RUNNING";
    }

    private SimulatedPrinterState GetOrCreateState(string serialNumber) =>
        _states.GetOrAdd(serialNumber, static (sn, cycleDuration) => new SimulatedPrinterState
        {
            SerialNumber = sn,
            CycleDuration = cycleDuration,
        }, _cycleDuration);

    private static void Advance(SimulatedPrinterState state)
    {
        if (state.Status != PrinterStatus.Printing || state.JobStartedAtUtc is not { } startedAt || state.IsPaused)
        {
            return;
        }

        var fraction = Math.Clamp((DateTimeOffset.UtcNow - startedAt) / state.CycleDuration, 0.0, 1.0);

        state.PrintProgressPercent = (int)Math.Round(fraction * 100);
        state.NozzleTempC = Lerp(25m, PrintingNozzleTargetC, fraction);
        state.BedTempC = Lerp(25m, PrintingBedTargetC, fraction);
        state.RemainingTimeMinutes = (int)Math.Ceiling((1.0 - fraction) * SimulatedJobTotalMinutes);

        if (fraction >= 1.0)
        {
            state.Status = PrinterStatus.AwaitingClear;
            state.RemainingTimeMinutes = 0;
            state.PendingGcodeState = "FINISH";
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
                state.RemainingTimeMinutes,
                state.CurrentJobName,
                state.FeedReadings,
                ActiveSlots: [new FeedSlotRef(FeedUnitKindCatalog.ExternalSpool, 0, 0)],
                NozzleDiametersMm: [0.4m]));

    private static string StatusToGcodeState(PrinterStatus status) => status switch
    {
        PrinterStatus.Idle => "IDLE",
        PrinterStatus.Printing => "RUNNING",
        PrinterStatus.AwaitingClear => "FINISH",
        _ => "IDLE",
    };
}
