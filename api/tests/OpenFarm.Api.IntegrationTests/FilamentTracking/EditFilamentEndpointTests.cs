using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.EntityFrameworkCore;
using OpenFarm.Api.Endpoints.Parts;
using OpenFarm.Api.Endpoints.Filaments;
using OpenFarm.Api.Endpoints.Vendors;
using OpenFarm.Api.IntegrationTests.Production;
using OpenFarm.Application.FilamentTracking;
using OpenFarm.Domain.Profiles;
using OpenFarm.Infrastructure.Persistence;

namespace OpenFarm.Api.IntegrationTests.FilamentTracking;

// Test-after: PUT /api/v1/filaments/{id} — details, new variants + profile assignments in one save.
public class EditFilamentEndpointTests(ProductionTestFactory factory) : ProductionTestContext(factory), IClassFixture<ProductionTestFactory>
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
    public async Task PartSummary_ExposesSelectedVariantForAssemblyPricing()
    {
        var client = await ClientAsync();
        var filament = await FilamentAsync(client);
        var part = await PartAsync();
        var variantId = filament.Variants.Single().Id;
        using (var scope = Factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<OpenFarmDbContext>();
            var tracked = await db.Parts.Include(p => p.FilamentRequirements).SingleAsync(p => p.Id == part.Id);
            tracked.NarrowFilamentRequirement(tracked.FilamentRequirements.Single().Id, variantId, null);
            await db.SaveChangesAsync();
        }
        var summary = (await client.GetFromJsonAsync<ListPartsResponse>("/api/v1/parts?page=1&pageSize=200"))!
            .Parts.Single(p => p.Id == part.Id);
        summary.FilamentByColor.Single().NarrowFilamentVariantId.Should().Be(variantId);
    }

    [Fact]
    public async Task Prices_CreateEditClearAndLegacyOmission_RoundTrip()
    {
        var client = await ClientAsync();
        var vendorId = await VendorAsync(client);
        var created = await client.PostAsJsonAsync("/api/v1/filaments", new CreateFilamentRequest
        {
            VendorId = vendorId,
            Material = "Priced PLA",
            MaterialType = "PLA",
            PricePerKg = 20.50m,
            Variants = [new() { ColorName = "Black", ColorHex = "#000000", PricePerKg = 30m }],
        });
        created.StatusCode.Should().Be(HttpStatusCode.Created);
        var filament = (await created.Content.ReadFromJsonAsync<FilamentResponse>())!;
        filament.PricePerKg.Should().Be(20.50m);
        filament.Variants.Single().PricePerKg.Should().Be(30m);
        var legacy = await client.PutAsJsonAsync($"/api/v1/filaments/{filament.Id}", new
        {
            vendorId,
            material = "Renamed",
            materialType = "PLA",
        });
        legacy.EnsureSuccessStatusCode();
        var saved = await ReloadAsync(client, filament.Id);
        saved.PricePerKg.Should().Be(20.50m);
        saved.Variants.Single().PricePerKg.Should().Be(30m);
        var edited = await client.PutAsJsonAsync($"/api/v1/filaments/{filament.Id}", new EditFilamentRequest
        {
            VendorId = vendorId,
            Material = "Renamed",
            MaterialType = "PLA",
            PricePerKg = 0m,
            VariantPrices = [new(saved.Variants.Single().Id, null)],
            NewVariants = [new() { ColorName = "White", ColorHex = "#FFFFFF", PricePerKg = 24m }],
        });
        edited.EnsureSuccessStatusCode();
        saved = await ReloadAsync(client, filament.Id);
        saved.PricePerKg.Should().Be(0m);
        saved.Variants.Single(v => v.ColorName == "Black").PricePerKg.Should().BeNull();
        saved.Variants.Single(v => v.ColorName == "White").PricePerKg.Should().Be(24m);
        var cleared = await client.PutAsJsonAsync($"/api/v1/filaments/{filament.Id}", new
        {
            vendorId,
            material = "Renamed",
            materialType = "PLA",
            pricePerKg = (decimal?)null,
        });
        cleared.EnsureSuccessStatusCode();
        (await ReloadAsync(client, filament.Id)).PricePerKg.Should().BeNull();
    }

    [Fact]
    public async Task Prices_ForeignVariantOrNegativePrice_PersistNothing()
    {
        var client = await ClientAsync();
        var filament = await FilamentAsync(client);
        var other = await FilamentAsync(client);
        foreach (var foreign in new[] { false, true })
        {
            var response = await client.PutAsJsonAsync($"/api/v1/filaments/{filament.Id}", new EditFilamentRequest
            {
                VendorId = filament.VendorId,
                Material = "Should not save",
                MaterialType = "PLA",
                PricePerKg = foreign ? 20m : -1m,
                VariantPrices = [new(other.Variants.Single().Id, 25m)],
                NewVariants = [new() { ColorName = "White", ColorHex = "#FFFFFF" }],
            });
            response.StatusCode.Should().Be(foreign ? HttpStatusCode.NotFound : HttpStatusCode.BadRequest);
            var saved = await ReloadAsync(client, filament.Id);
            saved.Material.Should().Be(filament.Material);
            saved.PricePerKg.Should().BeNull();
            saved.Variants.Should().ContainSingle();
        }
    }

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
