using System.Collections.Concurrent;

namespace OpenFarm.Application.Printers;

/// <summary>
/// Which printers' cameras someone is looking at. Every camera-frame request touches its printer; a printer's camera
/// relay runs only while it was touched within <see cref="Window"/>, so an unwatched camera costs no traffic.
/// </summary>
public sealed class CameraDemand(TimeSpan? window = null)
{
    public TimeSpan Window { get; } = window ?? TimeSpan.FromSeconds(15);

    private readonly ConcurrentDictionary<Guid, DateTimeOffset> _lastTouchedUtc = new();

    public void Touch(Guid printerId) => _lastTouchedUtc[printerId] = DateTimeOffset.UtcNow;

    public bool IsWanted(Guid printerId) =>
        _lastTouchedUtc.TryGetValue(printerId, out var touched) && DateTimeOffset.UtcNow - touched < Window;
}
