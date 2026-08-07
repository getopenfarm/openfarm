using System.Net.Http.Headers;
using System.Net.Http.Json;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using OpenFarm.Api.Endpoints.ActivityLog;
using OpenFarm.Api.Endpoints.Assemblies;
using OpenFarm.Api.Endpoints.Auth;
using OpenFarm.Api.Endpoints.NonPrintedComponents;
using OpenFarm.Application.Auth;
using OpenFarm.Application.Parts;
using OpenFarm.Domain.Organizations;
using OpenFarm.Domain.Parts;

namespace OpenFarm.Api.IntegrationTests.ActivityLog;

/// <summary>FR-015 — assembly creation, a line add/change/remove, a blocked and a successful deletion, and non-printed component creation/edit are all recorded with actor and timestamp. Requires Docker (Testcontainers.PostgreSql) — exercised by CI.</summary>
public class AssemblyBomActivityLogTests(CustomWebApplicationFactory factory) : IClassFixture<CustomWebApplicationFactory>
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
            null, null, EstimateStatus.Available, 60, 20, 1, 1, $"{Guid.NewGuid():N}", [new FilamentRequirementSpec("PLA", null, null, null, null)], [], []);

        await partRepository.AddAsync(part, CancellationToken.None);
        return part.Id;
    }

    [Fact]
    public async Task AssemblyAndBomLineLifecycle_EachOperationWritesAnActivityLogEntry()
    {
        var client = await AuthenticatedClientAsync();
        var partOneId = await SeedPartAsync($"Log Part 1 {Guid.NewGuid():N}");
        var partTwoId = await SeedPartAsync($"Log Part 2 {Guid.NewGuid():N}");

        var createResponse = await client.PostAsJsonAsync(
            new Uri("/api/v1/assemblies", UriKind.Relative),
            new { name = $"Log Assembly {Guid.NewGuid():N}", description = (string?)null, lines = new[] { new { targetKind = "Part", targetId = partOneId, quantity = 1 } } });
        var assembly = await createResponse.Content.ReadFromJsonAsync<AssemblyResponse>();

        var addLineResponse = await client.PostAsJsonAsync(
            new Uri($"/api/v1/assemblies/{assembly!.Id}/lines", UriKind.Relative),
            new { targetKind = "Part", targetId = partTwoId, quantity = 2 });
        var addedLine = await addLineResponse.Content.ReadFromJsonAsync<BomLineResponse>();

        await client.PutAsJsonAsync(
            new Uri($"/api/v1/assemblies/{assembly.Id}/lines/{addedLine!.Id}", UriKind.Relative), new { quantity = 5 });

        var blockedDeleteResponse = await client.DeleteAsync(new Uri($"/api/v1/assemblies/{assembly.Id}/lines/{addedLine.Id}", UriKind.Relative));
        blockedDeleteResponse.EnsureSuccessStatusCode();

        await client.DeleteAsync(new Uri($"/api/v1/assemblies/{assembly.Id}", UriKind.Relative));

        var componentResponse = await client.PostAsJsonAsync(
            new Uri("/api/v1/non-printed-components", UriKind.Relative),
            new { name = $"Log Screw {Guid.NewGuid():N}", unit = "each", unitCost = (decimal?)null });
        var component = await componentResponse.Content.ReadFromJsonAsync<NonPrintedComponentResponse>();
        await client.PutAsJsonAsync(
            new Uri($"/api/v1/non-printed-components/{component!.Id}", UriKind.Relative),
            new { name = "Log Screw Edited", unit = "each", unitCost = (decimal?)null });

        var logResponse = await client.GetAsync(new Uri("/api/v1/activity-log?page=1&pageSize=200", UriKind.Relative));
        var page = await logResponse.Content.ReadFromJsonAsync<ActivityLogPageResponse>();

        page!.Items.Should().Contain(e => e.EntityId == assembly.Id.ToString() && e.EventType == "AssemblyCreated");
        page.Items.Should().Contain(e => e.EntityId == assembly.Id.ToString() && e.EventType == "BomLineAdded");
        page.Items.Should().Contain(e => e.EntityId == assembly.Id.ToString() && e.EventType == "BomLineQuantityChanged");
        page.Items.Should().Contain(e => e.EntityId == assembly.Id.ToString() && e.EventType == "BomLineRemoved");
        page.Items.Should().Contain(e => e.EntityId == assembly.Id.ToString() && e.EventType == "AssemblyDeleted");
        page.Items.Should().Contain(e => e.EntityId == component.Id.ToString() && e.EventType == "NonPrintedComponentCreated");
        page.Items.Should().Contain(e => e.EntityId == component.Id.ToString() && e.EventType == "NonPrintedComponentEdited");
        page.Items.Where(e => e.EntityId == assembly.Id.ToString() || e.EntityId == component.Id.ToString())
            .Should().OnlyContain(e => e.ActorType == "Operator" && e.TimestampUtc != default);
    }
}
