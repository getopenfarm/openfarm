using FluentAssertions;
using OpenFarm.Application.Printers;
using OpenFarm.Infrastructure.Printers.Simulator;

namespace OpenFarm.Infrastructure.Tests.Printers.Simulator;

public class SimulatorPrinterCameraRelayTests
{
    [Fact]
    public async Task StreamFramesAsync_YieldsNonEmptyFrameAndKeepsStreamingUntilCancelled()
    {
        var relay = new SimulatorPrinterCameraRelay();
        var details = new PrinterConnectionDetails("System", "Simulation", "10.0.0.1", "SN-1", "12345678");
        using var cts = new CancellationTokenSource();

        var frames = await SimulatorPrinterConnectorTests.TakeAsync(relay.StreamFramesAsync(details, cts.Token), 3);

        frames.Should().HaveCount(3);
        frames.Should().OnlyContain(frame => frame.Length > 0);
    }

    [Fact]
    public async Task StreamFramesAsync_Cancelled_CompletesRatherThanThrowingUnrelatedException()
    {
        var relay = new SimulatorPrinterCameraRelay();
        var details = new PrinterConnectionDetails("System", "Simulation", "10.0.0.1", "SN-2", "12345678");
        using var cts = new CancellationTokenSource();

        var enumerator = relay.StreamFramesAsync(details, cts.Token).GetAsyncEnumerator(cts.Token);
        await enumerator.MoveNextAsync(); // consume the immediate first frame

        await cts.CancelAsync();

        var act = async () => await enumerator.MoveNextAsync();
        await act.Should().ThrowAsync<OperationCanceledException>();
    }
}
