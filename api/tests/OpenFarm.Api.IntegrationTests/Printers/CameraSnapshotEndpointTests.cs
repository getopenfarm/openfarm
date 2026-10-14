using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Finbuckle.MultiTenant.Abstractions;
using Microsoft.Extensions.DependencyInjection;
using OpenFarm.Api.Endpoints.Auth;
using OpenFarm.Application.Auth;
using OpenFarm.Application.Printers;
using OpenFarm.Domain.Organizations;
using OpenFarm.Domain.Printers;
using OpenFarm.Infrastructure.Multitenancy;

namespace OpenFarm.Api.IntegrationTests.Printers;

public sealed class CameraSnapshotEndpointTests(CustomWebApplicationFactory factory) : IClassFixture<CustomWebApplicationFactory>
{
    private async Task<HttpClient> ClientAsync()
    {
        var client = factory.CreateClient();
        var login = await client.PostAsJsonAsync("/api/v1/auth/login", new LoginCommand("admin", "admin-openfarm"));
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer",
            (await login.Content.ReadFromJsonAsync<LoginResponse>())!.Token);
        await factory.EnsureTenantInfoResolvedAsync();
        return client;
    }

    private static Printer NewPrinter(Guid organizationId) => Printer.Create(organizationId,
        "Snapshot test", "Bambu", "P1S", "127.0.0.1", $"CAM-{Guid.NewGuid():N}");
    // Valid relay framing; decoding is independently covered by frontend onLoad/onError tests.
    private static PrinterCameraSnapshot Snapshot() => new(
        Convert.ToBase64String([0xFF, 0xD8, 0xFF, 0xE0, 0xFF, 0xD9]), DateTimeOffset.UtcNow.AddHours(-2));

    [Fact]
    public async Task OfflineSnapshotIsSharedAcrossClientsAndRemovedWithPrinter()
    {
        using var client = await ClientAsync();
        var printer = NewPrinter(Organization.WellKnownId);
        var snapshot = Snapshot();
        using (var scope = factory.CreateTenantScope())
        {
            await scope.ServiceProvider.GetRequiredService<IPrinterRepository>().AddAsync(printer, default);
            await scope.ServiceProvider.GetRequiredService<IPrinterCameraSnapshotStore>().SaveAsync(printer.OrganizationId, printer.Id, snapshot, default);
        }
        using var secondClient = await ClientAsync();
        foreach (var browser in new[] { client, secondClient })
        {
            var response = await browser.GetAsync($"/api/v1/printers/{printer.Id}/camera-snapshot");
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            Assert.True(response.Headers.CacheControl!.NoStore);
            Assert.Equal(snapshot, await response.Content.ReadFromJsonAsync<PrinterCameraSnapshot>());
        }
        Assert.Equal(HttpStatusCode.NoContent, (await client.DeleteAsync($"/api/v1/printers/{printer.Id}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync($"/api/v1/printers/{printer.Id}/camera-snapshot")).StatusCode);
        Assert.Null(await factory.Services.GetRequiredService<IPrinterCameraSnapshotStore>().GetAsync(printer.OrganizationId, printer.Id, default));
    }

    [Fact]
    public async Task MissingSnapshotIs204AndMissingPrinterIs404()
    {
        using var client = await ClientAsync();
        var printer = NewPrinter(Organization.WellKnownId);
        using (var scope = factory.CreateTenantScope())
            await scope.ServiceProvider.GetRequiredService<IPrinterRepository>().AddAsync(printer, default);
        Assert.Equal(HttpStatusCode.NoContent, (await client.GetAsync($"/api/v1/printers/{printer.Id}/camera-snapshot")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync($"/api/v1/printers/{Guid.NewGuid()}/camera-snapshot")).StatusCode);
    }

    [Fact]
    public async Task OtherTenantPrinterIsHiddenAndAuthenticationIsRequired()
    {
        using var client = await ClientAsync();
        var otherOrg = Guid.NewGuid();
        var printer = NewPrinter(otherOrg);
        using (var scope = factory.CreateTenantScope())
        {
            scope.ServiceProvider.GetRequiredService<IMultiTenantContextSetter>().MultiTenantContext =
                new MultiTenantContext<AppTenantInfo>(new AppTenantInfo { Id = otherOrg.ToString(), Identifier = "snapshot-other", Name = "Other tenant" });
            await scope.ServiceProvider.GetRequiredService<IPrinterRepository>().AddAsync(printer, default);
            await scope.ServiceProvider.GetRequiredService<IPrinterCameraSnapshotStore>().SaveAsync(otherOrg, printer.Id, Snapshot(), default);
        }
        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync($"/api/v1/printers/{printer.Id}/camera-snapshot")).StatusCode);
        using var anonymous = factory.CreateClient();
        Assert.Equal(HttpStatusCode.Unauthorized, (await anonymous.GetAsync($"/api/v1/printers/{printer.Id}/camera-snapshot")).StatusCode);
    }
}
