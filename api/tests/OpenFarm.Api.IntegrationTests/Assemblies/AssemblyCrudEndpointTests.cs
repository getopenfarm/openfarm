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

// Test-after (constitution Principle II — orchestration-only endpoints). Covers quickstart.md Story 1's
// create/list/get/rename/delete round trip and the FR-002/FR-002/FR-013 rejection cases.
public class AssemblyCrudEndpointTests(CustomWebApplicationFactory factory) : IClassFixture<CustomWebApplicationFactory>
{
    private readonly HttpClient _client = factory.CreateClient();

    private async Task<HttpClient> AuthenticatedClientAsync()
    {
        var loginResponse = await _client.PostAsJsonAsync(
            new Uri("/api/v1/auth/login", UriKind.Relative), new LoginCommand("admin", "admin-openfarm"));
        var body = await loginResponse.Content.ReadFromJsonAsync<LoginResponse>();

        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", body!.Token);
        return _client;
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
    public async Task CreateListGetEditDelete_RoundTrips()
    {
        var client = await AuthenticatedClientAsync();
        var bracketId = await SeedPartAsync($"Bracket {Guid.NewGuid():N}");

        var createResponse = await client.PostAsJsonAsync(
            new Uri("/api/v1/assemblies", UriKind.Relative),
            new { name = "Crud Test Assembly", description = "d", lines = new[] { new { targetKind = "Part", targetId = bracketId, quantity = 2 } } });
        createResponse.StatusCode.Should().Be(HttpStatusCode.Created);
        var created = await createResponse.Content.ReadFromJsonAsync<AssemblyResponse>();
        created!.Lines.Should().ContainSingle(l => l.TargetId == bracketId && l.Quantity == 2);

        var listResponse = await client.GetAsync(new Uri("/api/v1/assemblies?page=1&pageSize=25", UriKind.Relative));
        var list = await listResponse.Content.ReadFromJsonAsync<ListAssembliesResponse>();
        list!.Items.Should().Contain(a => a.Id == created.Id);

        var getResponse = await client.GetAsync(new Uri($"/api/v1/assemblies/{created.Id}", UriKind.Relative));
        getResponse.StatusCode.Should().Be(HttpStatusCode.OK);

        var editResponse = await client.PutAsJsonAsync(
            new Uri($"/api/v1/assemblies/{created.Id}", UriKind.Relative), new { name = "Renamed", description = (string?)null });
        editResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        var edited = await editResponse.Content.ReadFromJsonAsync<AssemblyResponse>();
        edited!.Name.Should().Be("Renamed");

        var deleteResponse = await client.DeleteAsync(new Uri($"/api/v1/assemblies/{created.Id}", UriKind.Relative));
        deleteResponse.StatusCode.Should().Be(HttpStatusCode.NoContent);

        var getAfterDelete = await client.GetAsync(new Uri($"/api/v1/assemblies/{created.Id}", UriKind.Relative));
        getAfterDelete.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Create_EmptyLines_Returns400()
    {
        var client = await AuthenticatedClientAsync();

        var response = await client.PostAsJsonAsync(
            new Uri("/api/v1/assemblies", UriKind.Relative), new { name = "Empty", description = (string?)null, lines = Array.Empty<object>() });

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task EditLines_DuplicateTarget_Returns409()
    {
        var client = await AuthenticatedClientAsync();
        var partId = await SeedPartAsync($"Dup Part {Guid.NewGuid():N}");

        var createResponse = await client.PostAsJsonAsync(
            new Uri("/api/v1/assemblies", UriKind.Relative),
            new { name = "Dup Target", description = (string?)null, lines = new[] { new { targetKind = "Part", targetId = partId, quantity = 1 } } });
        var created = await createResponse.Content.ReadFromJsonAsync<AssemblyResponse>();

        var addResponse = await client.PutAsJsonAsync(
            new Uri($"/api/v1/assemblies/{created!.Id}", UriKind.Relative),
            new
            {
                name = created.Name,
                lines = new object[]
                {
                    new { lineId = created.Lines.Single().Id, targetKind = "Part", targetId = partId, quantity = 1 },
                    new { targetKind = "Part", targetId = partId, quantity = 3 },
                },
            });

        addResponse.StatusCode.Should().Be(HttpStatusCode.Conflict);
    }

    [Fact]
    public async Task Create_UnresolvableTargetId_Returns404()
    {
        var client = await AuthenticatedClientAsync();

        var response = await client.PostAsJsonAsync(
            new Uri("/api/v1/assemblies", UriKind.Relative),
            new { name = "Unresolvable", description = (string?)null, lines = new[] { new { targetKind = "Part", targetId = Guid.NewGuid(), quantity = 1 } } });

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }
}
