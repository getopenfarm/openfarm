using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using FluentAssertions;
using OpenFarm.Api.Endpoints.Auth;
using OpenFarm.Api.Endpoints.Filaments;
using OpenFarm.Api.Endpoints.Vendors;
using OpenFarm.Application.Auth;

namespace OpenFarm.Api.IntegrationTests.FilamentTracking;

/// <summary>Requires Docker (Testcontainers.PostgreSql) — exercised by CI.</summary>
public class VendorAndFilamentCatalogTests(CustomWebApplicationFactory factory) : IClassFixture<CustomWebApplicationFactory>
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

    [Fact]
    public async Task CreateVendorThenFilamentWithVariant_BothAppearInTheirCatalogs()
    {
        var client = await AuthenticatedClientAsync();
        var vendorName = $"Bambu Lab {Guid.NewGuid():N}";

        var vendorResponse = await client.PostAsJsonAsync(new Uri("/api/v1/vendors", UriKind.Relative), new CreateVendorRequest { Name = vendorName });
        vendorResponse.StatusCode.Should().Be(HttpStatusCode.Created);
        var vendor = await vendorResponse.Content.ReadFromJsonAsync<VendorResponse>();

        var listVendorsResponse = await client.GetAsync(new Uri("/api/v1/vendors", UriKind.Relative));
        var vendors = await listVendorsResponse.Content.ReadFromJsonAsync<ListVendorsResponse>();
        vendors!.Vendors.Should().Contain(v => v.Id == vendor!.Id && v.Name == vendorName);

        var filamentRequest = new CreateFilamentRequest
        {
            VendorId = vendor!.Id,
            Material = "PLA Basic",
            MaterialType = "PLA",
            Variants = [new CreateFilamentVariantRequest { ColorName = "Black", ColorHex = "#18181b" }],
        };
        var filamentResponse = await client.PostAsJsonAsync(new Uri("/api/v1/filaments", UriKind.Relative), filamentRequest);
        filamentResponse.StatusCode.Should().Be(HttpStatusCode.Created);
        var filament = await filamentResponse.Content.ReadFromJsonAsync<FilamentResponse>();
        filament!.Variants.Should().ContainSingle(v => v.ColorName == "Black" && v.ColorHex == "#18181b");

        var listFilamentsResponse = await client.GetAsync(new Uri("/api/v1/filaments", UriKind.Relative));
        var filaments = await listFilamentsResponse.Content.ReadFromJsonAsync<ListFilamentsResponse>();
        filaments!.Filaments.Should().Contain(f => f.Id == filament.Id && f.VendorName == vendorName);
    }

    [Fact]
    public async Task CreateVendor_WithDuplicateName_Returns409()
    {
        var client = await AuthenticatedClientAsync();
        var vendorName = $"Duplicate Vendor {Guid.NewGuid():N}";

        await client.PostAsJsonAsync(new Uri("/api/v1/vendors", UriKind.Relative), new CreateVendorRequest { Name = vendorName });
        var duplicateResponse = await client.PostAsJsonAsync(new Uri("/api/v1/vendors", UriKind.Relative), new CreateVendorRequest { Name = vendorName });

        duplicateResponse.StatusCode.Should().Be(HttpStatusCode.Conflict);
    }

    [Fact]
    public async Task CreateFilament_WithUnknownVendorId_Returns404()
    {
        var client = await AuthenticatedClientAsync();

        var response = await client.PostAsJsonAsync(new Uri("/api/v1/filaments", UriKind.Relative), new CreateFilamentRequest
        {
            VendorId = Guid.NewGuid(),
            Material = "PLA Basic",
            MaterialType = "PLA",
            Variants = [new CreateFilamentVariantRequest { ColorName = "Black", ColorHex = "#18181b" }],
        });

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task AddFilamentVariant_ToUnknownFilamentId_Returns404()
    {
        var client = await AuthenticatedClientAsync();

        var response = await client.PostAsJsonAsync(
            new Uri($"/api/v1/filaments/{Guid.NewGuid()}/variants", UriKind.Relative),
            new AddFilamentVariantRequest { ColorName = "White", ColorHex = "#f4f4f5" });

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task AddFilamentVariant_WithDuplicateColorName_Returns409()
    {
        var client = await AuthenticatedClientAsync();
        var vendorResponse = await client.PostAsJsonAsync(new Uri("/api/v1/vendors", UriKind.Relative), new CreateVendorRequest { Name = $"Vendor {Guid.NewGuid():N}" });
        var vendor = await vendorResponse.Content.ReadFromJsonAsync<VendorResponse>();

        var filamentResponse = await client.PostAsJsonAsync(new Uri("/api/v1/filaments", UriKind.Relative), new CreateFilamentRequest
        {
            VendorId = vendor!.Id,
            Material = "PETG",
            MaterialType = "PETG",
            Variants = [new CreateFilamentVariantRequest { ColorName = "Black", ColorHex = "#18181b" }],
        });
        var filament = await filamentResponse.Content.ReadFromJsonAsync<FilamentResponse>();

        var duplicateVariantResponse = await client.PostAsJsonAsync(
            new Uri($"/api/v1/filaments/{filament!.Id}/variants", UriKind.Relative),
            new AddFilamentVariantRequest { ColorName = "Black", ColorHex = "#000000" });

        duplicateVariantResponse.StatusCode.Should().Be(HttpStatusCode.Conflict);
    }
}
