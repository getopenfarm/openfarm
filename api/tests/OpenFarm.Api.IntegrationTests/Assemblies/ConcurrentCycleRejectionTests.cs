using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using OpenFarm.Api.Endpoints.Assemblies;
using OpenFarm.Api.Endpoints.Auth;
using OpenFarm.Application.Auth;
using OpenFarm.Application.Parts;
using OpenFarm.Domain.Organizations;
using OpenFarm.Domain.Parts;

namespace OpenFarm.Api.IntegrationTests.Assemblies;

// FR-007a, quickstart.md Story 3 — two independent assemblies X, Y; two parallel clients fire
// X-includes-Y and Y-includes-X at the same instant. Exactly one must commit; the loser sees the
// ordinary FR-007 cycle rejection (research.md R3), never a generic conflict error.
public class ConcurrentCycleRejectionTests(CustomWebApplicationFactory factory) : IClassFixture<CustomWebApplicationFactory>
{
    private async Task<HttpClient> AuthenticatedClientAsync()
    {
        var client = factory.CreateClient();
        var loginResponse = await client.PostAsJsonAsync(
            new Uri("/api/v1/auth/login", UriKind.Relative), new LoginCommand("admin", "admin-openfarm"));
        var body = await loginResponse.Content.ReadFromJsonAsync<LoginResponse>();

        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", body!.Token);
        return client;
    }

    private async Task<Guid> SeedPartAsync(string name)
    {
        await factory.EnsureTenantInfoResolvedAsync();
        using var scope = factory.CreateTenantScope();
        var partRepository = scope.ServiceProvider.GetRequiredService<IPartRepository>();

        var part = Part.Create(
            Organization.WellKnownId, name, PartFileFormat.Stl, true, "part.stl", $"/tmp/{Guid.NewGuid():N}.stl", 1024, 0.4m,
            null, null, EstimateStatus.Available, 60, 20, 1, [new FilamentRequirementSpec("PLA", null, null, null, null)], [], []);

        await partRepository.AddAsync(part, CancellationToken.None);
        return part.Id;
    }

    private static async Task<Guid> CreateAssemblyAsync(HttpClient client, string name, Guid partId)
    {
        var response = await client.PostAsJsonAsync(
            new Uri("/api/v1/assemblies", UriKind.Relative),
            new { name, description = (string?)null, lines = new[] { new { targetKind = "Part", targetId = partId, quantity = 1 } } });
        var body = await response.Content.ReadFromJsonAsync<AssemblyResponse>();
        return body!.Id;
    }

    [Fact]
    public async Task TwoParallelClientsRaceOppositeEdges_ExactlyOneCommits_LoserSeesCycleRejection()
    {
        var setupClient = await AuthenticatedClientAsync();
        var partId = await SeedPartAsync($"Race Part {Guid.NewGuid():N}");
        var x = await CreateAssemblyAsync(setupClient, $"X {Guid.NewGuid():N}", partId);
        var y = await CreateAssemblyAsync(setupClient, $"Y {Guid.NewGuid():N}", partId);

        var clientA = await AuthenticatedClientAsync();
        var clientB = await AuthenticatedClientAsync();

        var taskA = clientA.PostAsJsonAsync(
            new Uri($"/api/v1/assemblies/{x}/lines", UriKind.Relative), new { targetKind = "Assembly", targetId = y, quantity = 1 });
        var taskB = clientB.PostAsJsonAsync(
            new Uri($"/api/v1/assemblies/{y}/lines", UriKind.Relative), new { targetKind = "Assembly", targetId = x, quantity = 1 });

        var responses = await Task.WhenAll(taskA, taskB);

        var succeeded = responses.Count(r => r.StatusCode == HttpStatusCode.Created);
        var rejected = responses.Count(r => r.StatusCode == HttpStatusCode.UnprocessableEntity);
        succeeded.Should().Be(1, "exactly one of the two racing edges should commit");
        rejected.Should().Be(1, "the loser should see the ordinary cycle-rejection status, not a generic conflict");

        var verifyClient = await AuthenticatedClientAsync();
        var xDetail = await (await verifyClient.GetAsync(new Uri($"/api/v1/assemblies/{x}", UriKind.Relative))).Content.ReadFromJsonAsync<AssemblyResponse>();
        var yDetail = await (await verifyClient.GetAsync(new Uri($"/api/v1/assemblies/{y}", UriKind.Relative))).Content.ReadFromJsonAsync<AssemblyResponse>();

        var xIncludesY = xDetail!.Lines.Any(l => l.TargetId == y);
        var yIncludesX = yDetail!.Lines.Any(l => l.TargetId == x);
        (xIncludesY && yIncludesX).Should().BeFalse("both edges must never coexist — that would be a persisted cycle");
        (xIncludesY || yIncludesX).Should().BeTrue("the winning edge must be persisted");
    }
}
