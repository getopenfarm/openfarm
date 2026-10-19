using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using OpenFarm.Api.Endpoints.Assemblies;
using OpenFarm.Api.Endpoints.ActivityLog;
using OpenFarm.Api.Endpoints.Auth;
using OpenFarm.Api.Endpoints.CostCatalog;
using OpenFarm.Api.Endpoints.NonPrintedComponents;
using OpenFarm.Application.Auth;
using OpenFarm.Application.Parts;
using OpenFarm.Domain.Assemblies;
using OpenFarm.Domain.Parts;

namespace OpenFarm.Api.IntegrationTests.Assemblies;

public class CostCatalogEndpointTests(CustomWebApplicationFactory factory) : IClassFixture<CustomWebApplicationFactory>
{
    private async Task<HttpClient> ClientAsync()
    {
        var client = factory.CreateClient();
        var response = await client.PostAsJsonAsync(new Uri("/api/v1/auth/login", UriKind.Relative), new LoginCommand("admin", "admin-openfarm"));
        var login = await response.Content.ReadFromJsonAsync<LoginResponse>();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", login!.Token);
        return client;
    }
    private async Task<Guid> PartAsync()
    {
        using var scope = factory.Services.CreateScope();
        var part = Part.Create("Cost test part", PartFileFormat.Stl, true, "part.stl", $"/tmp/{Guid.NewGuid():N}.stl", 1024,
            0.4m, null, null, EstimateStatus.Available, 60, 20, 1, 1, $"{Guid.NewGuid():N}",
            [new FilamentRequirementSpec("PLA", null, null, null, null)], [], []);
        await scope.ServiceProvider.GetRequiredService<IPartRepository>().AddAsync(part, CancellationToken.None);
        return part.Id;
    }
    private static async Task<CostItemResponse> ItemAsync(HttpClient client, CatalogPricing pricing)
    {
        var response = await client.PostAsJsonAsync(new Uri("/api/v1/cost-items", UriKind.Relative), new { name = $"Cost item {Guid.NewGuid():N}", pricing });
        response.StatusCode.Should().Be(HttpStatusCode.Created);
        return (await response.Content.ReadFromJsonAsync<CostItemResponse>())!;
    }
    private static async Task<AssemblyResponse> AssemblyAsync(HttpClient client, Guid target, string kind, params AssemblyCostInput[] costs)
    {
        var response = await client.PostAsJsonAsync(new Uri("/api/v1/assemblies", UriKind.Relative), new
        {
            name = $"Cost assembly {Guid.NewGuid():N}",
            lines = new[] { new { targetKind = kind, targetId = target, quantity = 2 } },
            additionalCosts = costs,
        });
        response.StatusCode.Should().Be(HttpStatusCode.Created);
        return (await response.Content.ReadFromJsonAsync<AssemblyResponse>())!;
    }

    [Fact]
    public async Task PackagePriceWithoutLabelRoundTrips()
    {
        var client = await ClientAsync();
        var item = await ItemAsync(client, new CatalogPricing(Unit: "Piece", PricingMode: "Package", PackagePrice: 8m, PackageQuantity: 200));
        item.EffectiveRate.Should().Be(0.04m);
        item.Pricing.PackageLabel.Should().BeNull();
        item.Pricing.PackageQuantity.Should().Be(200m);
    }

    [Fact]
    public async Task RollPricingAndDimensionsRoundTripAndReprice()
    {
        var client = await ClientAsync();
        var pricing = new CatalogPricing(Category: "Packaging", Unit: "SquareMetre", PricingMode: "Package", PackageLabel: "roll",
            PackagePrice: 40m, Length: 100, LengthUnit: "m", Width: 100, WidthUnit: "cm");
        var item = await ItemAsync(client, pricing);
        item.EffectiveRate.Should().Be(0.4m);
        item.Pricing.WidthUnit.Should().Be("cm");
        var cut = new AssemblyCostInput(item.Id, "FinalProduct", InputMode: "Dimensions", Length: 50, Width: 20);
        var assembly = await AssemblyAsync(client, await PartAsync(), "Part", cut);
        assembly.AdditionalCosts.Should().ContainSingle().Which.Should().Be(cut);
        var explosion = await client.GetFromJsonAsync<ExplodeAssemblyResponse>($"/api/v1/assemblies/{assembly.Id}/explosion?quantity=1");
        explosion!.FinalProductCosts.Should().ContainSingle().Which.Cost.Should().Be(0.04m);
        var edit = await client.PutAsJsonAsync(new Uri($"/api/v1/cost-items/{item.Id}", UriKind.Relative), new { name = item.Name, pricing = pricing with { PackagePrice = 80 } });
        edit.StatusCode.Should().Be(HttpStatusCode.OK);
        explosion = await client.GetFromJsonAsync<ExplodeAssemblyResponse>($"/api/v1/assemblies/{assembly.Id}/explosion?quantity=1");
        explosion!.FinalProductCosts[0].Cost.Should().Be(0.08m);
    }

    [Fact]
    public async Task NestingInheritsProductionButExcludesChildFinalCostsAndPreservesLegacyEdits()
    {
        var client = await ClientAsync();
        var labour = await ItemAsync(client, new CatalogPricing("Service", "Labour", "Hour", DirectRate: 20));
        var shipping = await ItemAsync(client, new CatalogPricing("Service", "Shipping", "Application", DirectRate: 8));
        var child = await AssemblyAsync(client, await PartAsync(), "Part", new(labour.Id, Quantity: 6), new(shipping.Id, "FinalProduct"));
        var parent = await AssemblyAsync(client, child.Id, "Assembly");
        var explosion = await client.GetFromJsonAsync<ExplodeAssemblyResponse>($"/api/v1/assemblies/{parent.Id}/explosion?quantity=3");
        explosion!.ProductionCosts.Sum(c => c.Cost).Should().Be(12m);
        explosion.FinalProductCosts.Should().BeEmpty();
        var edit = await client.PutAsJsonAsync(new Uri($"/api/v1/assemblies/{child.Id}", UriKind.Relative), new { name = "Renamed", description = "Legacy edit" });
        edit.StatusCode.Should().Be(HttpStatusCode.OK);
        (await edit.Content.ReadFromJsonAsync<AssemblyResponse>())!.AdditionalCosts.Should().HaveCount(2);
        // Updating an existing cost line must preserve its row and avoid a unique-key collision.
        edit = await client.PutAsJsonAsync(new Uri($"/api/v1/assemblies/{child.Id}", UriKind.Relative), new { name = "Renamed", additionalCosts = new[] { new AssemblyCostInput(labour.Id, Quantity: 12) } });
        edit.StatusCode.Should().Be(HttpStatusCode.OK);
        (await edit.Content.ReadFromJsonAsync<AssemblyResponse>())!.AdditionalCosts.Should().ContainSingle().Which.Quantity.Should().Be(12);
    }

    [Fact]
    public async Task ReferencesProtectUnitDeletionAndLegacyWrites()
    {
        var client = await ClientAsync();
        var item = await ItemAsync(client, new CatalogPricing(Unit: "Metre", DirectRate: 2));
        var assembly = await AssemblyAsync(client, await PartAsync(), "Part", new AssemblyCostInput(item.Id, Quantity: 0.5m));
        var edit = await client.PutAsJsonAsync(new Uri($"/api/v1/cost-items/{item.Id}", UriKind.Relative), new { name = item.Name, pricing = new CatalogPricing(DirectRate: 2) });
        edit.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
        var remove = await client.DeleteAsync(new Uri($"/api/v1/cost-items/{item.Id}", UriKind.Relative));
        remove.StatusCode.Should().Be(HttpStatusCode.Conflict);
        var legacy = await client.PutAsJsonAsync(new Uri($"/api/v1/non-printed-components/{item.Id}", UriKind.Relative), new { name = "Legacy edit", unitCost = 99 });
        legacy.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
        var saved = await client.GetFromJsonAsync<CostItemResponse>($"/api/v1/cost-items/{item.Id}");
        saved!.EffectiveRate.Should().Be(2m);
        var list = await client.GetFromJsonAsync<ListNonPrintedComponentsResponse>("/api/v1/non-printed-components?pageSize=200");
        list!.Items.Should().NotContain(i => i.Id == item.Id);
        var clear = await client.PutAsJsonAsync(new Uri($"/api/v1/assemblies/{assembly.Id}", UriKind.Relative), new { name = "Cleared", additionalCosts = Array.Empty<AssemblyCostInput>() });
        clear.StatusCode.Should().Be(HttpStatusCode.OK);
        (await client.DeleteAsync(new Uri($"/api/v1/cost-items/{item.Id}", UriKind.Relative))).StatusCode.Should().Be(HttpStatusCode.NoContent);
        var activity = await client.GetFromJsonAsync<ActivityLogPageResponse>("/api/v1/activity-log?pageSize=200");
        activity!.Items.Should().Contain(e => e.EntityId == item.Id.ToString() && e.EventType == "NonPrintedComponentCreated");
        activity.Items.Should().Contain(e => e.EntityId == item.Id.ToString() && e.EventType == "NonPrintedComponentDeleted");
    }

    [Fact]
    public async Task InvalidCostUsageRollsBackNameAndBomAndServicesCannotEnterBom()
    {
        var client = await ClientAsync();
        var item = await ItemAsync(client, new CatalogPricing("Service", "Labour", "Application", DirectRate: 2));
        var part = await PartAsync();
        var assembly = await AssemblyAsync(client, part, "Part");
        var edit = await client.PutAsJsonAsync(new Uri($"/api/v1/assemblies/{assembly.Id}", UriKind.Relative), new
        {
            name = "Must not persist",
            lines = new[] { new { targetKind = "Part", targetId = part, quantity = 99 } },
            additionalCosts = new[] { new AssemblyCostInput(item.Id, Quantity: -1) },
        });
        edit.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
        var saved = await client.GetFromJsonAsync<AssemblyResponse>($"/api/v1/assemblies/{assembly.Id}");
        saved!.Name.Should().Be(assembly.Name);
        saved.Lines[0].Quantity.Should().Be(2);
        var response = await client.PostAsJsonAsync(new Uri("/api/v1/assemblies", UriKind.Relative), new { name = "Invalid", lines = new[] { new { targetKind = "NonPrintedComponent", targetId = item.Id, quantity = 1 } } });
        response.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
    }

    [Fact]
    public async Task ExistingComponentsRemainVisibleAndMeasuredBomQuantitiesUseItemUnit()
    {
        var client = await ClientAsync();
        var legacy = await client.PostAsJsonAsync(new Uri("/api/v1/non-printed-components", UriKind.Relative), new { name = "Legacy screw", unitCost = 0.05 });
        var piece = (await legacy.Content.ReadFromJsonAsync<NonPrintedComponentResponse>())!;
        var catalog = await client.GetFromJsonAsync<CostItemResponse>($"/api/v1/cost-items/{piece.Id}");
        catalog!.Pricing.Unit.Should().Be("Piece");
        catalog.EffectiveRate.Should().Be(0.05m);
        var invalid = await client.PostAsJsonAsync(new Uri("/api/v1/assemblies", UriKind.Relative), new { name = "Fractional screw", lines = new[] { new { targetKind = "NonPrintedComponent", targetId = piece.Id, quantity = 0.5 } } });
        invalid.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
        var material = await ItemAsync(client, new CatalogPricing(Unit: "Metre", DirectRate: 2));
        var valid = await client.PostAsJsonAsync(new Uri("/api/v1/assemblies", UriKind.Relative), new { name = "Measured tape", lines = new[] { new { targetKind = "NonPrintedComponent", targetId = material.Id, quantity = 0.5 } } });
        valid.StatusCode.Should().Be(HttpStatusCode.Created);
        var assembly = (await valid.Content.ReadFromJsonAsync<AssemblyResponse>())!;
        var duplicate = await client.PutAsJsonAsync(new Uri($"/api/v1/assemblies/{assembly.Id}", UriKind.Relative), new { name = "Duplicate", additionalCosts = new[] { new AssemblyCostInput(material.Id) } });
        duplicate.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
    }
}
