using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using OpenFarm.Api.Endpoints.ActivityLog;
using OpenFarm.Api.Endpoints.Auth;
using OpenFarm.Api.Endpoints.Parts;
using OpenFarm.Application.Auth;
using OpenFarm.Application.Parts;
using OpenFarm.Application.Profiles;
using OpenFarm.Domain.Organizations;
using OpenFarm.Domain.Parts;
using OpenFarm.Domain.Profiles;

namespace OpenFarm.Api.IntegrationTests.Parts;

// US4 Acceptance Scenarios 1-3 (contracts/profiles-api.md's PATCH .../filament-requirements/{requirementId}):
// both-origin picker (System profile narrows successfully), narrow/change/clear all activity-logged.
public class UpdatePartFilamentRequirementNarrowingEndpointTests(CustomWebApplicationFactory factory) : IClassFixture<CustomWebApplicationFactory>
{
    private readonly HttpClient _client = factory.CreateClient();

    private async Task<HttpClient> AuthenticatedClientAsync()
    {
        var loginResponse = await _client.PostAsJsonAsync(
            new Uri("/api/v1/auth/login", UriKind.Relative),
            new LoginCommand("admin", "admin-openfarm"));
        var body = await loginResponse.Content.ReadFromJsonAsync<LoginResponse>();

        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", body!.Token);
        return _client;
    }

    private async Task<(Guid PartId, Guid RequirementId)> SeedPartWithFilamentRequirementAsync()
    {
        await factory.EnsureTenantInfoResolvedAsync();
        using var scope = factory.CreateTenantScope();
        var partRepository = scope.ServiceProvider.GetRequiredService<IPartRepository>();

        var part = Part.Create(
            Organization.WellKnownId,
            $"Narrowing Test Part {Guid.NewGuid():N}",
            PartFileFormat.Stl,
            true,
            "part.stl",
            $"/tmp/{Guid.NewGuid():N}.stl",
            1024,
            0.4m,
            null,
            null,
            EstimateStatus.Available,
            60,
            20,
            1,
            1,
            $"{Guid.NewGuid():N}",
            [new FilamentRequirementSpec("PLA", null, null, null, null)],
            [],
            []);

        await partRepository.AddAsync(part, CancellationToken.None);

        return (part.Id, part.FilamentRequirements.Single().Id);
    }

    private async Task<Guid> SeedSystemFilamentProfileAsync()
    {
        await factory.EnsureTenantInfoResolvedAsync();
        using var scope = factory.CreateTenantScope();
        var filamentRepo = scope.ServiceProvider.GetRequiredService<IFilamentProfileRepository>();

        var profile = FilamentProfile.CreateSystemProfile(
            Organization.WellKnownId, $"Narrowing Test PLA {Guid.NewGuid():N}", ProfileSourceFormat.OrcaSlicer, null,
            """{"type":"filament","filament_type":["PLA"]}""",
            "NarrowingTestVendor", Guid.NewGuid().ToString("N"), isSelectable: true, compatibleMachineNames: null, materialType: "PLA", includedTemplateNames: null);
        await filamentRepo.AddAsync(profile, CancellationToken.None);

        return profile.Id;
    }

    [Fact]
    public async Task Patch_NarrowToSystemFilamentProfile_Returns200AndActivityLogsNarrowed()
    {
        var client = await AuthenticatedClientAsync();
        var (partId, requirementId) = await SeedPartWithFilamentRequirementAsync();
        var filamentProfileId = await SeedSystemFilamentProfileAsync();

        var response = await client.PatchAsJsonAsync(
            new Uri($"/api/v1/parts/{partId}/filament-requirements/{requirementId}", UriKind.Relative),
            new { narrowFilamentProfileId = filamentProfileId });

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadFromJsonAsync<PartFilamentRequirementResponse>();
        body!.NarrowFilamentProfileId.Should().Be(filamentProfileId);
        body.NarrowFilamentVariantId.Should().BeNull();

        var activityLogResponse = await client.GetAsync(new Uri("/api/v1/activity-log?page=1&pageSize=25", UriKind.Relative));
        var page = await activityLogResponse.Content.ReadFromJsonAsync<ActivityLogPageResponse>();
        page!.Items.Should().Contain(e => e.EntityId == partId.ToString() && e.EventType == "PartFilamentRequirementNarrowed");
    }

    [Fact]
    public async Task Patch_ChangeThenClear_Returns200AndActivityLogsClearedOnClear()
    {
        var client = await AuthenticatedClientAsync();
        var (partId, requirementId) = await SeedPartWithFilamentRequirementAsync();
        var firstProfileId = await SeedSystemFilamentProfileAsync();
        var secondProfileId = await SeedSystemFilamentProfileAsync();

        await client.PatchAsJsonAsync(
            new Uri($"/api/v1/parts/{partId}/filament-requirements/{requirementId}", UriKind.Relative),
            new { narrowFilamentProfileId = firstProfileId });

        var changeResponse = await client.PatchAsJsonAsync(
            new Uri($"/api/v1/parts/{partId}/filament-requirements/{requirementId}", UriKind.Relative),
            new { narrowFilamentProfileId = secondProfileId });
        changeResponse.StatusCode.Should().Be(HttpStatusCode.OK);

        var clearResponse = await client.PatchAsJsonAsync(
            new Uri($"/api/v1/parts/{partId}/filament-requirements/{requirementId}", UriKind.Relative),
            new { narrowFilamentProfileId = (Guid?)null });
        clearResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        var clearedBody = await clearResponse.Content.ReadFromJsonAsync<PartFilamentRequirementResponse>();
        clearedBody!.NarrowFilamentProfileId.Should().BeNull();

        var activityLogResponse = await client.GetAsync(new Uri("/api/v1/activity-log?page=1&pageSize=50", UriKind.Relative));
        var page = await activityLogResponse.Content.ReadFromJsonAsync<ActivityLogPageResponse>();
        page!.Items.Should().Contain(e => e.EntityId == partId.ToString() && e.EventType == "PartFilamentRequirementCleared");
    }

    [Fact]
    public async Task Patch_BothIdsSet_Returns400()
    {
        var client = await AuthenticatedClientAsync();
        var (partId, requirementId) = await SeedPartWithFilamentRequirementAsync();

        var response = await client.PatchAsJsonAsync(
            new Uri($"/api/v1/parts/{partId}/filament-requirements/{requirementId}", UriKind.Relative),
            new { narrowFilamentVariantId = Guid.NewGuid(), narrowFilamentProfileId = Guid.NewGuid() });

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Patch_UnknownRequirementId_Returns404()
    {
        var client = await AuthenticatedClientAsync();
        var (partId, _) = await SeedPartWithFilamentRequirementAsync();

        var response = await client.PatchAsJsonAsync(
            new Uri($"/api/v1/parts/{partId}/filament-requirements/{Guid.NewGuid()}", UriKind.Relative),
            new { narrowFilamentProfileId = Guid.NewGuid() });

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Patch_UnknownFilamentProfileId_Returns400()
    {
        var client = await AuthenticatedClientAsync();
        var (partId, requirementId) = await SeedPartWithFilamentRequirementAsync();

        var response = await client.PatchAsJsonAsync(
            new Uri($"/api/v1/parts/{partId}/filament-requirements/{requirementId}", UriKind.Relative),
            new { narrowFilamentProfileId = Guid.NewGuid() });

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }
}
