using System.Text.Json;
using FluentAssertions;
using Microsoft.AspNetCore.SignalR;
using OpenFarm.Api.Hubs;
using OpenFarm.Domain.Parts;

namespace OpenFarm.Api.IntegrationTests.Parts;

// The review dialog replaces its cached preview with this push, so it must carry the selections the GET returns —
// otherwise an auto-matched or carried-over filament shows as "Use file material and color".
public class FarmHubPartEventPublisherTests
{
    [Fact]
    public async Task PartProcessingJobStatusChanged_ReadyForReview_CarriesTheNarrowedFilamentAndGrams()
    {
        var variantId = Guid.NewGuid();
        var job = PartProcessingJob.CreateAdd(PartFileFormat.GCode, "a.gcode", "a.gcode", 1, false, null, null, null, null, null);
        job.MarkReadyForReview(0.4m, [new PreviewFilamentRequirement(0, "PETG", "#000000", "Aliz", variantId, null, 1.2m)], 1,
            EstimateStatus.Available, 10, 1.2m, []);
        var proxy = new CapturingProxy();

        await new FarmHubPartEventPublisher(new FakeHubContext(proxy)).PartProcessingJobStatusChangedAsync(job, CancellationToken.None);

        var requirement = JsonSerializer.SerializeToElement(proxy.Args![0]).GetProperty("preview").GetProperty("filamentRequirements")[0];
        requirement.GetProperty("narrowFilamentVariantId").GetGuid().Should().Be(variantId);
        requirement.GetProperty("estimatedGrams").GetDecimal().Should().Be(1.2m);
    }

    private sealed class CapturingProxy : IClientProxy
    {
        public object?[]? Args { get; private set; }

        public Task SendCoreAsync(string method, object?[] args, CancellationToken cancellationToken = default)
        {
            Args = args;
            return Task.CompletedTask;
        }
    }

    private sealed class FakeHubContext(IClientProxy proxy) : IHubContext<FarmHub>
    {
        public IHubClients Clients { get; } = new FakeClients(proxy);

        public IGroupManager Groups => throw new NotSupportedException();
    }

    private sealed class FakeClients(IClientProxy proxy) : IHubClients
    {
        public IClientProxy All => proxy;

        public IClientProxy AllExcept(IReadOnlyList<string> excludedConnectionIds) => proxy;

        public IClientProxy Client(string connectionId) => proxy;

        public IClientProxy Clients(IReadOnlyList<string> connectionIds) => proxy;

        public IClientProxy Group(string groupName) => proxy;

        public IClientProxy GroupExcept(string groupName, IReadOnlyList<string> excludedConnectionIds) => proxy;

        public IClientProxy Groups(IReadOnlyList<string> groupNames) => proxy;

        public IClientProxy User(string userId) => proxy;

        public IClientProxy Users(IReadOnlyList<string> userIds) => proxy;
    }
}
