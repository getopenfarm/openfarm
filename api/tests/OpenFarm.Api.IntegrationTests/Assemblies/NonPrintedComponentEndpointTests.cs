using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using OpenFarm.Api.Endpoints.Assemblies;
using OpenFarm.Api.Endpoints.Auth;
using OpenFarm.Api.Endpoints.NonPrintedComponents;
using OpenFarm.Application.Auth;
using OpenFarm.Application.Parts;
using OpenFarm.Domain.Organizations;
using OpenFarm.Domain.Parts;

namespace OpenFarm.Api.IntegrationTests.Assemblies;

// Test-after — create/edit/list round trip, empty name/unit rejected, delete-while-referenced blocked
// then unblocked once the referencing line is removed (contracts/assembly-bom-api.md).
public class NonPrintedComponentEndpointTests(CustomWebApplicationFactory factory) : IClassFixture<CustomWebApplicationFactory>
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

    [Fact]
    public async Task CreateListEditDelete_RoundTrips()
    {
        var client = await AuthenticatedClientAsync();

        var createResponse = await client.PostAsJsonAsync(
            new Uri("/api/v1/non-printed-components", UriKind.Relative),
            new { name = $"Screw {Guid.NewGuid():N}", unit = "each", unitCost = 0.02m });
        createResponse.StatusCode.Should().Be(HttpStatusCode.Created);
        var created = await createResponse.Content.ReadFromJsonAsync<NonPrintedComponentResponse>();

        var listResponse = await client.GetAsync(new Uri("/api/v1/non-printed-components?page=1&pageSize=25", UriKind.Relative));
        var list = await listResponse.Content.ReadFromJsonAsync<ListNonPrintedComponentsResponse>();
        list!.Items.Should().Contain(c => c.Id == created!.Id);

        var editResponse = await client.PutAsJsonAsync(
            new Uri($"/api/v1/non-printed-components/{created!.Id}", UriKind.Relative),
            new { name = "Renamed Screw", unit = "pack", unitCost = (decimal?)null });
        editResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        var edited = await editResponse.Content.ReadFromJsonAsync<NonPrintedComponentResponse>();
        edited!.Name.Should().Be("Renamed Screw");
        edited.UnitCost.Should().BeNull();

        var deleteResponse = await client.DeleteAsync(new Uri($"/api/v1/non-printed-components/{created.Id}", UriKind.Relative));
        deleteResponse.StatusCode.Should().Be(HttpStatusCode.NoContent);
    }

    [Fact]
    public async Task Create_EmptyNameOrUnit_Returns400()
    {
        var client = await AuthenticatedClientAsync();

        var response = await client.PostAsJsonAsync(
            new Uri("/api/v1/non-printed-components", UriKind.Relative), new { name = "", unit = "each", unitCost = (decimal?)null });

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
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

    [Fact]
    public async Task Delete_ReferencedByAssembly_Returns409ThenUnblocksOnceLineRemoved()
    {
        var client = await AuthenticatedClientAsync();
        var partId = await SeedPartAsync($"Holder Part {Guid.NewGuid():N}");

        var componentResponse = await client.PostAsJsonAsync(
            new Uri("/api/v1/non-printed-components", UriKind.Relative),
            new { name = $"Referenced Screw {Guid.NewGuid():N}", unit = "each", unitCost = (decimal?)null });
        var component = await componentResponse.Content.ReadFromJsonAsync<NonPrintedComponentResponse>();

        var assemblyResponse = await client.PostAsJsonAsync(
            new Uri("/api/v1/assemblies", UriKind.Relative),
            new
            {
                name = $"Screw Holder {Guid.NewGuid():N}",
                description = (string?)null,
                lines = new object[]
                {
                    new { targetKind = "Part", targetId = partId, quantity = 1 },
                    new { targetKind = "NonPrintedComponent", targetId = component!.Id, quantity = 4.5 },
                },
            });
        var assembly = await assemblyResponse.Content.ReadFromJsonAsync<AssemblyResponse>();

        var blockedResponse = await client.DeleteAsync(new Uri($"/api/v1/non-printed-components/{component.Id}", UriKind.Relative));
        blockedResponse.StatusCode.Should().Be(HttpStatusCode.Conflict);
        var detail = await blockedResponse.Content.ReadAsStringAsync();
        detail.Should().Contain(assembly!.Name);

        var lineId = assembly!.Lines.Single(l => l.TargetKind == "NonPrintedComponent").Id;
        await client.DeleteAsync(new Uri($"/api/v1/assemblies/{assembly.Id}/lines/{lineId}", UriKind.Relative));

        var unblockedResponse = await client.DeleteAsync(new Uri($"/api/v1/non-printed-components/{component.Id}", UriKind.Relative));
        unblockedResponse.StatusCode.Should().Be(HttpStatusCode.NoContent);
    }
}
