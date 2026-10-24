using System.Collections.Concurrent;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using OpenFarm.Infrastructure.Printers.Snapmaker;

namespace OpenFarm.Infrastructure.Tests.Printers.Snapmaker;

public class SnapmakerU1CameraRelayTests
{
    private static readonly byte[] FirstFrame = [0xFF, 0xD8, 0xFF, 0xE1, 0x01, 0xFF, 0xD9];
    private static readonly byte[] SecondFrame = [0xFF, 0xD8, 0xFF, 0xDB, 0x02, 0xFF, 0xD9];

    [Fact]
    public async Task StreamFrames_WakesTheCameraAndYieldsOnlyChangedSnapshots()
    {
        await using var harness = new MoonrakerHarness { CameraFrame = FirstFrame };
        await using var pump = new FramePump(harness);

        await pump.WaitForAsync(1);
        await Task.Delay(200);
        pump.Frames.Should().HaveCount(1, "the same snapshot polled again is not a new frame");

        harness.CameraFrame = SecondFrame;
        await pump.WaitForAsync(2);
        pump.Frames.Should().BeEquivalentTo([FirstFrame, SecondFrame], o => o.WithStrictOrdering());
        var wake = await harness.NextRequestAsync("camera.start_monitor");
        wake.Params!["domain"]!.GetValue<string>().Should().Be("lan");
    }

    [Fact]
    public async Task StreamFrames_SkipsAMissingOrHalfWrittenSnapshot()
    {
        await using var harness = new MoonrakerHarness();
        await using var pump = new FramePump(harness);

        await Task.Delay(150);
        harness.CameraFrame = [0xFF, 0xD8, 0xFF, 0xE0, 0x00];
        await Task.Delay(150);
        pump.Frames.Should().BeEmpty();

        harness.CameraFrame = FirstFrame;
        await pump.WaitForAsync(1);
    }

    private sealed class FramePump : IAsyncDisposable
    {
        private readonly CancellationTokenSource _cts = new();
        private readonly Task _task;

        public FramePump(MoonrakerHarness harness)
        {
            var relay = new SnapmakerU1CameraRelay(NullLogger<SnapmakerU1CameraRelay>.Instance,
                harness.Options with { CameraPollInterval = TimeSpan.FromMilliseconds(20) });
            _task = Task.Run(async () =>
            {
                try
                {
                    await foreach (var frame in relay.StreamFramesAsync(harness.Details(), _cts.Token))
                        Frames.Enqueue(frame);
                }
                catch (OperationCanceledException)
                {
                }
            });
        }

        public ConcurrentQueue<byte[]> Frames { get; } = new();

        public async Task WaitForAsync(int count)
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(3));
            while (Frames.Count < count) await Task.Delay(10, timeout.Token);
        }

        public async ValueTask DisposeAsync()
        {
            await _cts.CancelAsync();
            await _task.WaitAsync(TimeSpan.FromSeconds(3));
            _cts.Dispose();
        }
    }
}
