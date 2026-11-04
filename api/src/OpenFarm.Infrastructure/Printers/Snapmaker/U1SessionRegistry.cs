using System.Collections.Concurrent;
using OpenFarm.Application.Printers;

namespace OpenFarm.Infrastructure.Printers.Snapmaker;

/// <summary>Commands borrow the initialized monitoring socket and wait across reconnects. Borrowers never dispose it.</summary>
internal static class U1SessionRegistry
{
    private static readonly ConcurrentDictionary<string, Entry> Entries = new();
    private static readonly ConcurrentDictionary<string, SemaphoreSlim> DispatchGates = new();
    internal static string Key(PrinterConnectionDetails d, MoonrakerTransportOptions o) => $"{d.IpAddress}:{o.Port}/{d.SerialNumber}/{d.AccessCode}";
    internal static SemaphoreSlim DispatchGate(PrinterConnectionDetails d, MoonrakerTransportOptions o) => DispatchGates.GetOrAdd($"{d.IpAddress}:{o.Port}/{d.SerialNumber}", _ => new(1));
    internal static Entry Register(PrinterConnectionDetails d, MoonrakerTransportOptions o)
    {
        var entry = new Entry();
        if (!Entries.TryAdd(Key(d, o), entry)) throw new InvalidOperationException("U1 monitoring is already active.");
        return entry;
    }
    internal static void Remove(PrinterConnectionDetails d, MoonrakerTransportOptions o, Entry entry)
    {
        Entries.TryRemove(new KeyValuePair<string, Entry>(Key(d, o), entry));
        entry.Stop();
    }
    internal static async Task<Lease> BorrowAsync(PrinterConnectionDetails d, MoonrakerTransportOptions o, CancellationToken ct)
    {
        if (Entries.TryGetValue(Key(d, o), out var entry))
        {
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
            timeout.CancelAfter(o.CommandTimeout);
            return new Lease(await entry.ReadyAsync(timeout.Token), false);
        }
        return new Lease(await MoonrakerSocket.ConnectAsync(SnapmakerU1Connector.BaseUri(d, o.Port), d.AccessCode, o, ct), true);
    }
    internal sealed class Entry
    {
        private readonly object _gate = new();
        private TaskCompletionSource<MoonrakerSocket> _ready = NewReady();
        private static TaskCompletionSource<MoonrakerSocket> NewReady() => new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal void SetReady(MoonrakerSocket socket) { lock (_gate) _ready.TrySetResult(socket); }
        internal void Reset() { lock (_gate) { if (_ready.Task.IsCompleted) _ready = NewReady(); } }
        internal void Stop() { lock (_gate) _ready.TrySetCanceled(); }
        internal Task<MoonrakerSocket> ReadyAsync(CancellationToken ct) { lock (_gate) return _ready.Task.WaitAsync(ct); }
    }
    internal sealed class Lease(MoonrakerSocket socket, bool owned) : IAsyncDisposable
    {
        internal MoonrakerSocket Socket => socket;
        internal bool Owned => owned;
        public ValueTask DisposeAsync() => owned ? socket.DisposeAsync() : ValueTask.CompletedTask;
    }
}
