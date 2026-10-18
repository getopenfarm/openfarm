using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using OpenFarm.Api.Endpoints.Parts;
using OpenFarm.Api.IntegrationTests.Production;
using OpenFarm.Domain.Profiles;
using OpenFarm.Infrastructure.Persistence;

namespace OpenFarm.Api.IntegrationTests.Parts;

// Test-after: PUT /api/v1/parts/{id} — one all-or-nothing save for name, tags, units and narrowing.
public class EditPartEndpointTests(ProductionTestFactory factory) : ProductionTestContext(factory), IClassFixture<ProductionTestFactory>
{
    private async Task<Guid> FilamentProfileAsync()
    {
        using var scope = Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<OpenFarmDbContext>();
        var profile = FilamentProfile.Create($"PLA {Guid.NewGuid():N}", ProfileSourceFormat.OrcaSlicer, null,
            """{"filament_type":["PLA"],"nozzle_diameter":["0.4"]}""");
        db.FilamentProfiles.Add(profile);
        await db.SaveChangesAsync();
        return profile.Id;
    }

    [Fact]
    public async Task Edit_RoundTripsEverySection_AndLogsPartEdited()
    {
        var client = await ClientAsync();
        var part = await PartAsync();
        var requirementId = part.FilamentRequirements.Single().Id;
        var profileId = await FilamentProfileAsync();
        var tag = $"tag-{Guid.NewGuid():N}";

        var response = await client.PutAsJsonAsync($"/api/v1/parts/{part.Id}", new
        {
            name = "Renamed bracket",
            tags = new[] { tag, "shared" },
            unitsPerPrint = 3,
            filamentRequirements = new[] { new { requirementId, narrowFilamentProfileId = profileId } },
        });

        response.StatusCode.Should().Be(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
        var body = (await response.Content.ReadFromJsonAsync<PartResponse>())!;
        body.Name.Should().Be("Renamed bracket");
        body.Tags.Should().BeEquivalentTo(tag, "shared");
        body.UnitsPerPrint.Should().Be(3);
        body.FilamentRequirements.Single().NarrowFilamentProfileId.Should().Be(profileId);

        // Second save drops one tag and clears the narrowing.
        var second = await client.PutAsJsonAsync($"/api/v1/parts/{part.Id}", new
        {
            name = "Renamed bracket",
            tags = new List<string> { "shared" },
            unitsPerPrint = 3,
            filamentRequirements = new[] { new { requirementId } },
        });
        var secondBody = (await second.Content.ReadFromJsonAsync<PartResponse>())!;
        secondBody.Tags.Should().Equal("shared");
        secondBody.FilamentRequirements.Single().NarrowFilamentProfileId.Should().BeNull();

        (await ActivityEventTypesAsync(client, part.Id)).Should().Contain("PartEdited");
    }

    [Fact]
    public async Task Edit_InvalidUnitsPerPrint_Returns400()
    {
        var client = await ClientAsync();
        var part = await PartAsync();

        var response = await client.PutAsJsonAsync($"/api/v1/parts/{part.Id}", new { name = "x", tags = Array.Empty<string>(), unitsPerPrint = 0 });

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Edit_UnknownNarrowingProfile_PersistsNothing()
    {
        var client = await ClientAsync();
        var part = await PartAsync();
        var tag = $"orphan-{Guid.NewGuid():N}";

        var response = await client.PutAsJsonAsync($"/api/v1/parts/{part.Id}", new
        {
            name = "Should not stick",
            tags = new[] { tag },
            unitsPerPrint = 5,
            filamentRequirements = new[] { new { requirementId = part.FilamentRequirements.Single().Id, narrowFilamentProfileId = Guid.NewGuid() } },
        });

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        var after = (await client.GetFromJsonAsync<PartResponse>($"/api/v1/parts/{part.Id}"))!;
        after.Name.Should().Be(part.Name);
        after.Tags.Should().BeEmpty();
        after.UnitsPerPrint.Should().Be(1);
        using var scope = Factory.Services.CreateScope();
        (await scope.ServiceProvider.GetRequiredService<OpenFarmDbContext>().Tags.AnyAsync(t => t.Name == tag)).Should().BeFalse();
    }

    [Fact]
    public async Task Edit_BothNarrowingIdsSet_Returns400()
    {
        var client = await ClientAsync();
        var part = await PartAsync();

        var response = await client.PutAsJsonAsync($"/api/v1/parts/{part.Id}", new
        {
            name = part.Name,
            tags = Array.Empty<string>(),
            unitsPerPrint = 1,
            filamentRequirements = new[] { new { requirementId = part.FilamentRequirements.Single().Id, narrowFilamentVariantId = Guid.NewGuid(), narrowFilamentProfileId = Guid.NewGuid() } },
        });

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Edit_UnknownRequirementId_Returns404()
    {
        var client = await ClientAsync();
        var part = await PartAsync();

        var response = await client.PutAsJsonAsync($"/api/v1/parts/{part.Id}", new
        {
            name = part.Name,
            tags = Array.Empty<string>(),
            unitsPerPrint = 1,
            filamentRequirements = new[] { new { requirementId = Guid.NewGuid(), narrowFilamentProfileId = Guid.NewGuid() } },
        });

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }
}
