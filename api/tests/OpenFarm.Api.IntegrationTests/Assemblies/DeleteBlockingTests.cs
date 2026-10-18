using OpenFarm.Api.IntegrationTests.Production;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using OpenFarm.Api.Endpoints.Assemblies;
using OpenFarm.Api.Endpoints.Auth;
using OpenFarm.Application.Auth;
using OpenFarm.Application.Parts;
using OpenFarm.Domain.Parts;

namespace OpenFarm.Api.IntegrationTests.Assemblies;

// FR-011, quickstart.md Story 6 — deletion of a still-referenced Assembly/Part is blocked (409,
// naming the referencing assembly), never cascaded; unblocks once the referencing line is removed.
// Catalog dependency tests do not start printer/discovery workers or snapshot reconciliation.
public class DeleteBlockingTests(ProductionTestFactory factory) : IClassFixture<ProductionTestFactory>
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
        using var scope = factory.Services.CreateScope();
        var partRepository = scope.ServiceProvider.GetRequiredService<IPartRepository>();

        var part = Part.Create(
            name, PartFileFormat.Stl, true, "part.stl", $"/tmp/{Guid.NewGuid():N}.stl", 1024, 0.4m,
            null, null, EstimateStatus.Available, 60, 20, 1, 1, $"{Guid.NewGuid():N}", [new FilamentRequirementSpec("PLA", null, null, null, null)], [], []);

        await partRepository.AddAsync(part, CancellationToken.None);
        return part.Id;
    }

    [Fact]
    public async Task DeleteSubAssembly_StillReferencedByParent_Returns409ThenUnblocksOnceLineRemoved()
    {
        var client = await AuthenticatedClientAsync();
        var partId = await SeedPartAsync($"Handle Part {Guid.NewGuid():N}");
        var otherPartId = await SeedPartAsync($"Enclosure Part {Guid.NewGuid():N}");

        var handleResponse = await client.PostAsJsonAsync(
            new Uri("/api/v1/assemblies", UriKind.Relative),
            new { name = $"Handle {Guid.NewGuid():N}", description = (string?)null, lines = new[] { new { targetKind = "Part", targetId = partId, quantity = 1 } } });
        var handle = await handleResponse.Content.ReadFromJsonAsync<AssemblyResponse>();

        // Enclosure needs a second line besides the sub-assembly one — dropping its only line would
        // itself violate FR-002 ("an assembly must have at least one BOM line"), which isn't what this
        // test is exercising.
        var enclosureResponse = await client.PostAsJsonAsync(
            new Uri("/api/v1/assemblies", UriKind.Relative),
            new
            {
                name = $"Enclosure {Guid.NewGuid():N}",
                description = (string?)null,
                lines = new object[]
                {
                    new { targetKind = "Assembly", targetId = handle!.Id, quantity = 1 },
                    new { targetKind = "Part", targetId = otherPartId, quantity = 1 },
                },
            });
        var enclosure = await enclosureResponse.Content.ReadFromJsonAsync<AssemblyResponse>();

        var blockedResponse = await client.DeleteAsync(new Uri($"/api/v1/assemblies/{handle.Id}", UriKind.Relative));
        blockedResponse.StatusCode.Should().Be(HttpStatusCode.Conflict);
        var blockedDetail = await blockedResponse.Content.ReadAsStringAsync();
        blockedDetail.Should().Contain(enclosure!.Name);

        var keptLine = enclosure.Lines.Single(l => l.TargetId == otherPartId);
        var removeLineResponse = await client.PutAsJsonAsync(
            new Uri($"/api/v1/assemblies/{enclosure.Id}", UriKind.Relative),
            new { name = enclosure.Name, lines = new[] { new { lineId = keptLine.Id, targetKind = "Part", targetId = otherPartId, quantity = 1 } } });
        removeLineResponse.StatusCode.Should().Be(HttpStatusCode.OK);

        var unblockedResponse = await client.DeleteAsync(new Uri($"/api/v1/assemblies/{handle.Id}", UriKind.Relative));
        unblockedResponse.StatusCode.Should().Be(HttpStatusCode.NoContent);
    }

    [Fact]
    public async Task DeletePart_ReferencedByBomLine_Returns409NamingAssembly()
    {
        var client = await AuthenticatedClientAsync();
        var partId = await SeedPartAsync($"Bracket {Guid.NewGuid():N}");

        var assemblyResponse = await client.PostAsJsonAsync(
            new Uri("/api/v1/assemblies", UriKind.Relative),
            new { name = $"Referencing Assembly {Guid.NewGuid():N}", description = (string?)null, lines = new[] { new { targetKind = "Part", targetId = partId, quantity = 1 } } });
        var assembly = await assemblyResponse.Content.ReadFromJsonAsync<AssemblyResponse>();

        var deletePartResponse = await client.DeleteAsync(new Uri($"/api/v1/parts/{partId}", UriKind.Relative));

        deletePartResponse.StatusCode.Should().Be(HttpStatusCode.Conflict);
        var detail = await deletePartResponse.Content.ReadAsStringAsync();
        detail.Should().Contain(assembly!.Name);
    }
}
