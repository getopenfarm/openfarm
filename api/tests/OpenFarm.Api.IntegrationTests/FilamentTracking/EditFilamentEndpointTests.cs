using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using OpenFarm.Api.Endpoints.Filaments;
using OpenFarm.Api.Endpoints.Vendors;
using OpenFarm.Api.IntegrationTests.Production;
using OpenFarm.Application.FilamentTracking;
using OpenFarm.Domain.Organizations;
using OpenFarm.Domain.Profiles;
using OpenFarm.Infrastructure.Persistence;

namespace OpenFarm.Api.IntegrationTests.FilamentTracking;

// Test-after: PUT /api/v1/filaments/{id} — details, new variants + profile assignments in one save.
public class EditFilamentEndpointTests(ProductionTestFactory factory) : ProductionTestContext(factory), IClassFixture<ProductionTestFactory>
{
    private async Task<Guid> FilamentProfileAsync()
    {
        using var scope = Factory.CreateTenantScope();
        var db = scope.ServiceProvider.GetRequiredService<OpenFarmDbContext>();
        var profile = FilamentProfile.Create(Organization.WellKnownId, $"PLA {Guid.NewGuid():N}", ProfileSourceFormat.OrcaSlicer, null,
            """{"filament_type":["PLA"],"nozzle_diameter":["0.4"]}""");
        db.FilamentProfiles.Add(profile);
        await db.SaveChangesAsync();
        return profile.Id;
    }

    private static async Task<FilamentResponse> FilamentAsync(HttpClient client)
    {
        var vendorResponse = await client.PostAsJsonAsync("/api/v1/vendors", new CreateVendorRequest { Name = Guid.NewGuid().ToString() });
        var vendor = (await vendorResponse.Content.ReadFromJsonAsync<VendorResponse>())!;
        var created = await client.PostAsJsonAsync("/api/v1/filaments", new CreateFilamentRequest
        {
            VendorId = vendor.Id,
            Material = "PLA Basic",
            MaterialType = "PLA",
            Variants = [new() { ColorName = "Black", ColorHex = "#000000" }],
        });
        return (await created.Content.ReadFromJsonAsync<FilamentResponse>())!;
    }

    private static async Task<Guid> VendorAsync(HttpClient client)
    {
        var response = await client.PostAsJsonAsync("/api/v1/vendors", new CreateVendorRequest { Name = Guid.NewGuid().ToString() });
        return (await response.Content.ReadFromJsonAsync<VendorResponse>())!.Id;
    }

    private static async Task<FilamentResponse> ReloadAsync(HttpClient client, Guid id) =>
        (await client.GetFromJsonAsync<ListFilamentsResponse>("/api/v1/filaments"))!.Filaments.Single(f => f.Id == id);

    [Fact]
    public async Task Edit_AddsVariantAndAssignmentReferencingIt_AndLogsFilamentEdited()
    {
        var client = await ClientAsync();
        var filament = await FilamentAsync(client);
        var profileId = await FilamentProfileAsync();

        var response = await client.PutAsJsonAsync($"/api/v1/filaments/{filament.Id}", new EditFilamentRequest
        {
            VendorId = filament.VendorId,
            Material = filament.Material,
            MaterialType = filament.MaterialType,
            NewVariants = [new() { ColorName = "Red", ColorHex = "#ff0000" }],
            ProfileAssignments = [new FilamentProfileAssignmentInput("Bambu", "P1S", 0.4m, profileId, "#FF0000")],
        });

        response.StatusCode.Should().Be(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
        var body = (await response.Content.ReadFromJsonAsync<FilamentResponse>())!;
        body.Variants.Select(v => v.ColorName).Should().BeEquivalentTo("Black", "Red");
        body.ProfileAssignments.Should().ContainSingle(a => a.FilamentProfileId == profileId && a.ColorHex == "#ff0000");

        var reloaded = await ReloadAsync(client, filament.Id);
        reloaded.Variants.Should().HaveCount(2);
        reloaded.ProfileAssignments.Should().ContainSingle();
        (await ActivityEventTypesAsync(client, filament.Id)).Should().Contain("FilamentEdited");
    }

    [Fact]
    public async Task Edit_BlankVariantColour_Returns400()
    {
        var client = await ClientAsync();
        var filament = await FilamentAsync(client);

        var response = await client.PutAsJsonAsync($"/api/v1/filaments/{filament.Id}", new EditFilamentRequest
        {
            VendorId = filament.VendorId,
            Material = filament.Material,
            MaterialType = filament.MaterialType,
            NewVariants = [new() { ColorName = "", ColorHex = "#ff0000" }],
        });

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Edit_DuplicateVariantColour_Returns409AndPersistsNothing()
    {
        var client = await ClientAsync();
        var filament = await FilamentAsync(client);

        var response = await client.PutAsJsonAsync($"/api/v1/filaments/{filament.Id}", new EditFilamentRequest
        {
            VendorId = filament.VendorId,
            Material = filament.Material,
            MaterialType = filament.MaterialType,
            NewVariants = [new() { ColorName = "Blue", ColorHex = "#0000ff" }, new() { ColorName = "Blue", ColorHex = "#0000fe" }],
        });

        response.StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await ReloadAsync(client, filament.Id)).Variants.Should().ContainSingle();
    }

    [Fact]
    public async Task Edit_UnknownAssignmentProfile_RollsBackNewVariants()
    {
        var client = await ClientAsync();
        var filament = await FilamentAsync(client);

        var response = await client.PutAsJsonAsync($"/api/v1/filaments/{filament.Id}", new EditFilamentRequest
        {
            VendorId = filament.VendorId,
            Material = filament.Material,
            MaterialType = filament.MaterialType,
            NewVariants = [new() { ColorName = "Green", ColorHex = "#00ff00" }],
            ProfileAssignments = [new FilamentProfileAssignmentInput("Bambu", "P1S", 0.4m, Guid.NewGuid())],
        });

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
        var reloaded = await ReloadAsync(client, filament.Id);
        reloaded.Variants.Should().ContainSingle();
        reloaded.ProfileAssignments.Should().BeEmpty();
    }

    [Fact]
    public async Task Edit_UpdatesVendorMaterialAndType()
    {
        var client = await ClientAsync();
        var filament = await FilamentAsync(client);
        var vendorId = await VendorAsync(client);

        var response = await client.PutAsJsonAsync($"/api/v1/filaments/{filament.Id}", new EditFilamentRequest
        {
            VendorId = vendorId,
            Material = "PETG HF",
            MaterialType = "PETG",
        });

        response.StatusCode.Should().Be(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
        var reloaded = await ReloadAsync(client, filament.Id);
        reloaded.VendorId.Should().Be(vendorId);
        reloaded.Material.Should().Be("PETG HF");
        reloaded.MaterialType.Should().Be("PETG");
        reloaded.Variants.Should().ContainSingle();
    }

    [Fact]
    public async Task Edit_UnknownVendor_Returns404()
    {
        var client = await ClientAsync();
        var filament = await FilamentAsync(client);

        var response = await client.PutAsJsonAsync($"/api/v1/filaments/{filament.Id}", new EditFilamentRequest
        {
            VendorId = Guid.NewGuid(),
            Material = filament.Material,
            MaterialType = filament.MaterialType,
        });

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await ReloadAsync(client, filament.Id)).VendorId.Should().Be(filament.VendorId);
    }

    [Fact]
    public async Task Edit_TypeChangeWithIncompatibleAssignment_IsRejectedAndPersistsNothing()
    {
        var client = await ClientAsync();
        var filament = await FilamentAsync(client);
        var plaProfileId = await FilamentProfileAsync();

        var response = await client.PutAsJsonAsync($"/api/v1/filaments/{filament.Id}", new EditFilamentRequest
        {
            VendorId = filament.VendorId,
            Material = filament.Material,
            MaterialType = "PETG",
            ProfileAssignments = [new FilamentProfileAssignmentInput("Bambu", "P1S", 0.4m, plaProfileId)],
        });

        response.IsSuccessStatusCode.Should().BeFalse();
        (await ReloadAsync(client, filament.Id)).MaterialType.Should().Be("PLA");
    }
}
