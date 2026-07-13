using System.Collections.Concurrent;
using System.Runtime.CompilerServices;
using System.Threading.Channels;
using OpenFarm.Application.Printers;

namespace OpenFarm.Infrastructure.Printers;

/// <summary>
/// Test/dev double for <see cref="IPrinterConnector"/> — the default wired connector outside of
/// real registered Bambu printers (constitution: CI never touches real hardware). Registered as a
/// singleton so the long-lived <c>PrinterConnectionBackgroundService</c> consumers and short-lived
/// callers (tests, a dev-only trigger) share the same in-memory per-serial event channels.
///
/// Verify-connection convention: a connection is treated as unverifiable when
/// <see cref="PrinterConnectionDetails.AccessCode"/> is exactly <c>"wrong"</c> — a fixed sentinel
/// used across quickstart.md's dev scenarios to simulate a bad LAN access code without any real
/// network call.
/// </summary>
public sealed class FakePrinterConnector : IPrinterConnector
{
    private const string UnverifiableAccessCodeSentinel = "wrong";

    private readonly ConcurrentDictionary<string, Channel<PrinterConnectorEvent>> _channels = new();

    public Task<bool> VerifyAsync(PrinterConnectionDetails details, CancellationToken cancellationToken) =>
        Task.FromResult(!string.Equals(details.AccessCode, UnverifiableAccessCodeSentinel, StringComparison.Ordinal));

    public async IAsyncEnumerable<PrinterConnectorEvent> ConnectAsync(
        PrinterConnectionDetails details,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        var channel = GetOrCreateChannel(details.SerialNumber);
        await foreach (var connectorEvent in channel.Reader.ReadAllAsync(cancellationToken))
        {
            yield return connectorEvent;
        }
    }

    /// <summary>
    /// Dev/test hook: push a simulated event as if it had just arrived from the printer's live
    /// connection. Used directly by tests and by a dev-only trigger endpoint.
    /// </summary>
    public void Emit(string serialNumber, PrinterConnectorEvent connectorEvent) =>
        GetOrCreateChannel(serialNumber).Writer.TryWrite(connectorEvent);

    private Channel<PrinterConnectorEvent> GetOrCreateChannel(string serialNumber) =>
        _channels.GetOrAdd(serialNumber, static _ => Channel.CreateUnbounded<PrinterConnectorEvent>());
}
