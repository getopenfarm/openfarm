using System.Runtime.CompilerServices;
using OpenFarm.Application.Printers;

namespace OpenFarm.Infrastructure.Printers;

/// <summary>
/// Dev/test default camera relay — never emits a frame, so the frontend's freshness-timeout
/// "unavailable" state (FR-009) is what shows for every printer registered against
/// <see cref="FakePrinterConnector"/>. Stays alive (without polling) until cancelled, mirroring
/// how a real relay's stream completes only when the caller's token is cancelled.
/// </summary>
public sealed class FakePrinterCameraRelay : IPrinterCameraRelay
{
    public async IAsyncEnumerable<byte[]> StreamFramesAsync(
        PrinterConnectionDetails details,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        var completion = new TaskCompletionSource();
        using (cancellationToken.Register(() => completion.TrySetResult()))
        {
            await completion.Task;
        }

        yield break;
    }
}
