using System.Net.Http.Headers;
using System.Net.Http.Json;
using OpenFarm.Api.Endpoints.Auth;
using OpenFarm.Api.Endpoints.Filaments;
using OpenFarm.Api.Endpoints.FeedUnits;
using OpenFarm.Api.Endpoints.Printers;
using OpenFarm.Api.Endpoints.Spools;
using OpenFarm.Api.Endpoints.Vendors;
using OpenFarm.Application.Auth;

namespace OpenFarm.Api.IntegrationTests.FilamentTracking;

/// <summary>Shared setup helpers for filament-tracking integration tests — a simulator printer's Feed Slots populate within a couple of seconds of registration.</summary>
internal static class FilamentTrackingTestHelpers
{
    public static async Task<HttpClient> AuthenticatedClientAsync(HttpClient client)
    {
        var loginResponse = await client.PostAsJsonAsync(
            new Uri("/api/v1/auth/login", UriKind.Relative),
            new LoginCommand("admin", "admin-openfarm"));
        var body = await loginResponse.Content.ReadFromJsonAsync<LoginResponse>();

        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", body!.Token);
        return client;
    }

    public static async Task<Guid> CreateFilamentVariantAsync(HttpClient client, string colorName = "Black", string colorHex = "#18181b")
    {
        var vendorResponse = await client.PostAsJsonAsync(new Uri("/api/v1/vendors", UriKind.Relative), new CreateVendorRequest { Name = $"Vendor {Guid.NewGuid():N}" });
        var vendor = await vendorResponse.Content.ReadFromJsonAsync<VendorResponse>();

        var filamentResponse = await client.PostAsJsonAsync(new Uri("/api/v1/filaments", UriKind.Relative), new CreateFilamentRequest
        {
            VendorId = vendor!.Id,
            Material = "PLA Basic",
            MaterialType = "PLA",
            Variants = [new CreateFilamentVariantRequest { ColorName = colorName, ColorHex = colorHex }],
        });
        var filament = await filamentResponse.Content.ReadFromJsonAsync<FilamentResponse>();

        return filament!.Variants[0].Id;
    }

    public static async Task<Guid> RegisterSimulatorPrinterAsync(HttpClient client, string? name = null)
    {
        var response = await client.PostAsJsonAsync(new Uri("/api/v1/printers", UriKind.Relative), new RegisterPrinterRequest
        {
            Name = name ?? $"Sim {Guid.NewGuid():N}",
            Brand = "System",
            ModelCode = "Simulation",
            IpAddress = "10.0.0.60",
            SerialNumber = $"SN-{Guid.NewGuid():N}",
            AccessCode = "12345678",
        });
        var printer = await response.Content.ReadFromJsonAsync<PrinterSummaryResponse>();
        return printer!.Id;
    }

    public static async Task<ListFeedUnitsResponse> WaitForFeedUnitsAsync(HttpClient client, Guid printerId, Func<ListFeedUnitsResponse, bool>? until = null)
    {
        until ??= r => r.FeedUnits.Count > 0;

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        while (!cts.IsCancellationRequested)
        {
            var response = await client.GetAsync(new Uri($"/api/v1/printers/{printerId}/feed-units", UriKind.Relative));
            var body = await response.Content.ReadFromJsonAsync<ListFeedUnitsResponse>();
            if (body is not null && until(body))
            {
                return body;
            }

            await Task.Delay(200, CancellationToken.None);
        }

        throw new TimeoutException("Feed units did not reach the expected state in time.");
    }

    public static async Task<Guid> RegisterSpoolAsync(HttpClient client, Guid filamentVariantId, decimal spoolWeightGrams = 1000)
    {
        var response = await client.PostAsJsonAsync(new Uri("/api/v1/spools", UriKind.Relative), new RegisterSpoolRequest
        {
            FilamentVariantId = filamentVariantId,
            SpoolWeightGrams = spoolWeightGrams,
        });
        var spool = await response.Content.ReadFromJsonAsync<SpoolResponse>();
        return spool!.Id;
    }
}
