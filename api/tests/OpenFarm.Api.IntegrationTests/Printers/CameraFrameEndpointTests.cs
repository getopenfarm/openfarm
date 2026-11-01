using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Microsoft.Extensions.DependencyInjection;
using OpenFarm.Api.Endpoints.Auth;
using OpenFarm.Application.Auth;
using OpenFarm.Application.Printers;
using OpenFarm.Domain.Printers;

namespace OpenFarm.Api.IntegrationTests.Printers;

public sealed class CameraFrameEndpointTests(CustomWebApplicationFactory factory) : IClassFixture<CustomWebApplicationFactory>
{
    private async Task<HttpClient> ClientAsync()
    {
        var client = factory.CreateClient();
        var login = await client.PostAsJsonAsync("/api/v1/auth/login", new LoginCommand("admin", "admin-openfarm"));
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer",
            (await login.Content.ReadFromJsonAsync<LoginResponse>())!.Token);
        return client;
    }

    private static Printer NewPrinter() => Printer.Create(
        "Camera test", "Bambu", "P1S", "127.0.0.1", $"CAM-{Guid.NewGuid():N}");
    // Valid relay framing; decoding is independently covered by frontend onLoad/onError tests.
    private static PrinterCameraSnapshot Snapshot() => new([0xFF, 0xD8, 0xFF, 0xE0, 0xFF, 0xD9], DateTimeOffset.UtcNow.AddHours(-2));

    private async Task<Printer> AddPrinterAsync(PrinterCameraSnapshot? snapshot = null)
    {
        var printer = NewPrinter();
        using var scope = factory.Services.CreateScope();
        await scope.ServiceProvider.GetRequiredService<IPrinterRepository>().AddAsync(printer, default);
        if (snapshot is not null)
            await scope.ServiceProvider.GetRequiredService<IPrinterCameraSnapshotStore>().SaveAsync(printer.Id, snapshot, default);
        return printer;
    }

    [Fact]
    public async Task FrameIsAJpegWithAnETag_UnchangedFrameIs304_AndPrinterRemovalDeletesIt()
    {
        using var client = await ClientAsync();
        var snapshot = Snapshot();
        var printer = await AddPrinterAsync(snapshot);
        var url = $"/api/v1/printers/{printer.Id}/camera.jpg";

        var response = await client.GetAsync(url);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("image/jpeg", response.Content.Headers.ContentType!.MediaType);
        Assert.True(response.Headers.CacheControl!.NoCache);
        Assert.True(response.Headers.CacheControl.Private);
        Assert.Equal(snapshot.FrameJpeg, await response.Content.ReadAsByteArrayAsync());
        Assert.Equal(snapshot.ReceivedAtUtc, DateTimeOffset.Parse(response.Headers.GetValues("X-Frame-Received-At").Single(),
            System.Globalization.CultureInfo.InvariantCulture));
        var etag = response.Headers.ETag!;

        using var revalidate = new HttpRequestMessage(HttpMethod.Get, url);
        revalidate.Headers.IfNoneMatch.Add(etag);
        var notModified = await client.SendAsync(revalidate);
        Assert.Equal(HttpStatusCode.NotModified, notModified.StatusCode);
        Assert.Empty(await notModified.Content.ReadAsByteArrayAsync());

        Assert.Equal(HttpStatusCode.NoContent, (await client.DeleteAsync($"/api/v1/printers/{printer.Id}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync(url)).StatusCode);
        Assert.Null(await factory.Services.GetRequiredService<IPrinterCameraSnapshotStore>().GetAsync(printer.Id, default));
    }

    [Fact]
    public async Task ARequestMarksTheCameraWatched()
    {
        using var client = await ClientAsync();
        var printer = await AddPrinterAsync();
        var demand = factory.Services.GetRequiredService<CameraDemand>();
        Assert.False(demand.IsWanted(printer.Id));

        await client.GetAsync($"/api/v1/printers/{printer.Id}/camera.jpg");

        Assert.True(demand.IsWanted(printer.Id));
    }

    [Fact]
    public async Task MissingFrameIs204AndMissingPrinterIs404()
    {
        using var client = await ClientAsync();
        var printer = await AddPrinterAsync();
        Assert.Equal(HttpStatusCode.NoContent, (await client.GetAsync($"/api/v1/printers/{printer.Id}/camera.jpg")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync($"/api/v1/printers/{Guid.NewGuid()}/camera.jpg")).StatusCode);
    }

    [Fact]
    public async Task AuthenticationIsRequired()
    {
        var printer = await AddPrinterAsync(Snapshot());
        using var anonymous = factory.CreateClient();
        Assert.Equal(HttpStatusCode.Unauthorized, (await anonymous.GetAsync($"/api/v1/printers/{printer.Id}/camera.jpg")).StatusCode);
    }
}
