namespace OpenFarm.Infrastructure.Printers;

/// <summary>
/// Reports on the reporting thread. <see cref="Progress{T}"/> posts each report to the thread pool when there is no
/// synchronization context (as in a background service), so upload percents could arrive out of order.
/// </summary>
internal sealed class InlineProgress<T>(Action<T> report) : IProgress<T>
{
    public void Report(T value) => report(value);
}
