using System.Net;
using FluentAssertions;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using OpenFarm.Application.Production;
using OpenFarm.Domain.Parts;
using OpenFarm.Domain.Production;
using OpenFarm.Infrastructure.Production;

namespace OpenFarm.Api.IntegrationTests.Production;

public sealed class SnapshotDispatchGate
{
    private int _armed;
    public TaskCompletionSource Entered { get; private set; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
    public TaskCompletionSource Release { get; private set; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

    public void Arm()
    {
        Entered = new(TaskCreationOptions.RunContinuationsAsynchronously);
        Release = new(TaskCreationOptions.RunContinuationsAsynchronously);
        Interlocked.Exchange(ref _armed, 1);
    }

    public async Task PauseAsync(CancellationToken ct)
    {
        if (Interlocked.Exchange(ref _armed, 0) == 0) return;
        Entered.TrySetResult();
        await Release.Task.WaitAsync(ct);
    }
}

public sealed class SnapshotDispatchRaceFactory : ProductionTestFactory
{
    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        base.ConfigureWebHost(builder);
        builder.ConfigureServices(services =>
        {
            services.AddSingleton<SnapshotDispatchGate>();
            services.RemoveAll<IJobFileResolver>();
            services.AddScoped<IJobFileResolver>(provider => new PausingResolver(
                ActivatorUtilities.CreateInstance<JobFileResolver>(provider), provider.GetRequiredService<SnapshotDispatchGate>()));
        });
    }

    private sealed class PausingResolver(JobFileResolver inner, SnapshotDispatchGate gate) : IJobFileResolver
    {
        public async Task<JobFileContent> ResolveAsync(Part part, PrintJob job, Guid printerId, decimal nozzleDiameterMm, CancellationToken ct)
        {
            var file = await inner.ResolveAsync(part, job, printerId, nozzleDiameterMm, ct);
            await gate.PauseAsync(ct);
            return file;
        }
    }
}

public class SnapshotDispatchRaceTests(SnapshotDispatchRaceFactory factory) : ProductionTestContext(factory), IClassFixture<SnapshotDispatchRaceFactory>
{
    [Fact]
    public async Task FiniteDeletionWhilePreparingFile_DoesNotPreventDispatch()
    {
        var client = await ClientAsync();
        var part = await PartAsync();
        var printer = await PrinterAsync();
        var run = await RunAsync(client, part.Id, pinned: printer.Id);
        var gate = Factory.Services.GetRequiredService<SnapshotDispatchGate>();
        gate.Arm();
        using var worker = Worker();
        await worker.StartAsync(default);
        try
        {
            await gate.Entered.Task.WaitAsync(TimeSpan.FromSeconds(10));
            (await client.DeleteAsync($"/api/v1/parts/{part.Id}")).StatusCode.Should().Be(HttpStatusCode.NoContent);
            gate.Release.TrySetResult();
            var dispatched = await WaitForAssignmentAsync(run.Id);
            dispatched.GetProductionPart()!.StoragePath.Should().Be(part.StoragePath);
            await ReplayAsync(printer.Id, "RUNNING");
        }
        finally { gate.Release.TrySetResult(); await worker.StopAsync(default); }
    }

    [Fact]
    public async Task UnlimitedCancellationWhilePreparingFile_PreventsSendingAndReleasesLibrary()
    {
        var client = await ClientAsync();
        var part = await PartAsync();
        var printer = await PrinterAsync();
        var run = await RunAsync(client, part.Id, quantity: null, pinned: printer.Id);
        var gate = Factory.Services.GetRequiredService<SnapshotDispatchGate>();
        gate.Arm();
        using var worker = Worker();
        await worker.StartAsync(default);
        try
        {
            await gate.Entered.Task.WaitAsync(TimeSpan.FromSeconds(10));
            await PostAsync(client, $"/api/v1/production-runs/{run.Id}/cancel");
            (await client.DeleteAsync($"/api/v1/parts/{part.Id}")).StatusCode.Should().Be(HttpStatusCode.NoContent);
            gate.Release.TrySetResult();
            await Task.Delay(200);
            var job = (await JobsAsync(run.Id)).Single();
            job.Status.Should().Be(PrintJobStatus.Cancelled);
            job.DispatchedAtUtc.Should().BeNull();
            job.GetProductionPart().Should().NotBeNull();
        }
        finally { gate.Release.TrySetResult(); await worker.StopAsync(default); }
    }
}
