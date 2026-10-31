using System.Text.Json;
using FluentAssertions;
using Microsoft.AspNetCore.SignalR;
using OpenFarm.Api.Hubs;
using OpenFarm.Application.Parts;
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
        job.MarkReadyForReview(0.4m, [new PreviewFilamentRequirement(0, "PETG", "#000000", "Aliz", variantId, null, 1.2m)], [new PreviewPlate(1, null, [0], 10, 1.2m)],
            EstimateStatus.Available, 10, 1.2m, []);
        var proxy = new CapturingProxy();

        await new FarmHubPartEventPublisher(proxy, new CountStorage()).PartProcessingJobStatusChangedAsync(job, CancellationToken.None);

        var preview = JsonSerializer.SerializeToElement(proxy.Args![0]).GetProperty("preview");
        preview.GetProperty("unitsPerPrint").GetInt32().Should().Be(4);
        preview.GetProperty("plates")[0].GetProperty("unitsPerPrint").GetInt32().Should().Be(4);
        var requirement = preview.GetProperty("filamentRequirements")[0];
        requirement.GetProperty("narrowFilamentVariantId").GetGuid().Should().Be(variantId);
        requirement.GetProperty("estimatedGrams").GetDecimal().Should().Be(1.2m);
    }

    private sealed class CountStorage : IPartFileStorage
    {
        public Task<Stream> OpenReadAsync(string storagePath, CancellationToken cancellationToken) =>
            Task.FromResult<Stream>(new MemoryStream(System.Text.Encoding.UTF8.GetBytes("; object_count = 4\n")));
        public Task<string> SaveAsync(string contentHash, string originalFileName, Stream content, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task DeleteAsync(string storagePath, CancellationToken cancellationToken) => throw new NotSupportedException();
    }

    private sealed class CapturingProxy : IFarmEventClients
    {
        public object?[]? Args { get; private set; }

        public Task SendCoreAsync(string method, object?[] args, CancellationToken cancellationToken = default)
        {
            Args = args;
            return Task.CompletedTask;
        }
    }

}
