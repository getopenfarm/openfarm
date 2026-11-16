using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using OpenFarm.Api.Endpoints.ActivityLog;
using OpenFarm.Api.Endpoints.Auth;
using OpenFarm.Api.Endpoints.Printers;
using OpenFarm.Application.Auth;
using OpenFarm.Application.Printers;
using OpenFarm.Application.Profiles;
using OpenFarm.Domain.Printers;
using OpenFarm.Domain.Profiles;

namespace OpenFarm.Api.IntegrationTests.Printers;

/// <summary>
/// Requires Docker (Testcontainers.PostgreSql) — not runnable in every sandbox, but exercised by CI.
/// </summary>
public class EditPrinterEndpointTests(CustomWebApplicationFactory factory) : IClassFixture<CustomWebApplicationFactory>
{
    private readonly HttpClient _client = factory.CreateClient();

    // LoginEndpoint's Throttle(hitLimit: 10, durationSeconds: 60) (Program.cs) is per-app-instance —
    // shared across every test in this class via the one CustomWebApplicationFactory IClassFixture
    // provides. This class had exactly 10 tests (the throttle's own limit) before 012's 6 build-plate
    // additions pushed it to 16, so calling /api/v1/auth/login fresh per test started tripping 429s
    // (confirmed via a raw-response diagnostic that briefly lived here: "You are requesting this
    // endpoint too frequently!"). One login per class run, cached and reused, fixes it — the token
    // stays valid for the app instance's whole lifetime regardless of which test requests it.
    private static string? _cachedToken;

    private async Task<HttpClient> AuthenticatedClientAsync()
    {
        if (_cachedToken is null)
        {
            var loginResponse = await _client.PostAsJsonAsync(
                new Uri("/api/v1/auth/login", UriKind.Relative),
                new LoginCommand("admin", "admin-openfarm"));
            var body = await loginResponse.Content.ReadFromJsonAsync<LoginResponse>();
            _cachedToken = body!.Token;
        }

        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", _cachedToken);
        return _client;
    }

    private static async Task<PrinterSummaryResponse> RegisterPrinterAsync(HttpClient client, string ipAddress = "10.0.0.90")
    {
        var serialNumber = $"EP-{Guid.NewGuid():N}";
        var registerResponse = await client.PostAsJsonAsync(
            new Uri("/api/v1/printers", UriKind.Relative),
            new RegisterPrinterRequest
            {
                Name = "Edit Test Printer",
                Brand = "System",
                ModelCode = "Simulation",
                IpAddress = ipAddress,
                SerialNumber = serialNumber,
                AccessCode = "12345678",
            });
        var printer = (await registerResponse.Content.ReadFromJsonAsync<PrinterSummaryResponse>())!;
        await OpenFarm.Api.IntegrationTests.FilamentTracking.FilamentTrackingTestHelpers.WaitForFeedUnitsAsync(client, printer.Id);
        return printer;
    }

    // 012 US1 — a real Bambu-brand registration goes through RegisterPrinterCommandHandler's live
    // connector verification (research.md — only "System"/"Simulation" routes to the no-network
    // simulator), which no CI runner can satisfy against a real printer. Seeding directly through the
    // repository is how this suite gets a printer whose
    // catalog model has a narrower CompatiblePlateTypes set than System/Simulation's "all six".
    private async Task<Guid> RegisterBambuA1PrinterDirectlyAsync()
    {
        using var scope = factory.Services.CreateScope();
        var printerRepo = scope.ServiceProvider.GetRequiredService<IPrinterRepository>();

        var printer = Printer.Create("A1 Test Printer", "Bambu", "A1", "10.0.0.96", $"A1-{Guid.NewGuid():N}");
        await printerRepo.AddAsync(printer, CancellationToken.None);
        await printerRepo.SaveChangesAsync(CancellationToken.None);

        return printer.Id;
    }

    [Fact]
    public async Task Edit_NameOnly_Returns200AndDoesNotRequireVerification()
    {
        var client = await AuthenticatedClientAsync();
        var printer = await RegisterPrinterAsync(client);

        var response = await client.PatchAsJsonAsync(
            new Uri($"/api/v1/printers/{printer.Id}", UriKind.Relative),
            new EditPrinterRequest { Name = "Renamed Printer" });

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadFromJsonAsync<PrinterSummaryResponse>();
        body!.Name.Should().Be("Renamed Printer");
        body.IpAddress.Should().Be(printer.IpAddress);

        var activityLogResponse = await client.GetAsync(new Uri("/api/v1/audit-log?page=1&pageSize=25", UriKind.Relative));
        var page = await activityLogResponse.Content.ReadFromJsonAsync<AuditLogPageResponse>();
        page!.Items.Should().ContainSingle(e =>
            e.EntityId == printer.Id.ToString() && e.EventType == "PrinterEdited");
    }

    [Fact]
    public async Task Edit_ConnectionDetailsWithCorrectCredentials_Returns200AndReconnects()
    {
        var client = await AuthenticatedClientAsync();
        var printer = await RegisterPrinterAsync(client);

        var response = await client.PatchAsJsonAsync(
            new Uri($"/api/v1/printers/{printer.Id}", UriKind.Relative),
            new EditPrinterRequest { Name = printer.Name, IpAddress = "10.0.0.91", AccessCode = "12345678" });

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadFromJsonAsync<PrinterSummaryResponse>();
        body!.IpAddress.Should().Be("10.0.0.91");
    }

    [Fact]
    public async Task Edit_ConnectionDetailsWithWrongAccessCode_Returns422AndLeavesPrinterUnchanged()
    {
        var client = await AuthenticatedClientAsync();
        var printer = await RegisterPrinterAsync(client);

        var response = await client.PatchAsJsonAsync(
            new Uri($"/api/v1/printers/{printer.Id}", UriKind.Relative),
            new EditPrinterRequest { Name = "Should Not Persist", IpAddress = "10.0.0.92", AccessCode = "wrong" });

        response.StatusCode.Should().Be((HttpStatusCode)422);

        var fleetResponse = await client.GetAsync(new Uri("/api/v1/printers", UriKind.Relative));
        var fleet = await fleetResponse.Content.ReadFromJsonAsync<ListFleetResponse>();
        var current = fleet!.Printers.Single(p => p.Id == printer.Id);
        current.Name.Should().Be(printer.Name);
        current.IpAddress.Should().Be(printer.IpAddress);
    }

    [Fact]
    public async Task Edit_MismatchedIpAddressAccessCodePair_Returns400()
    {
        var client = await AuthenticatedClientAsync();
        var printer = await RegisterPrinterAsync(client);

        var response = await client.PatchAsJsonAsync(
            new Uri($"/api/v1/printers/{printer.Id}", UriKind.Relative),
            new EditPrinterRequest { Name = printer.Name, IpAddress = "10.0.0.93" });

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Edit_MismatchedSerialNumber_Returns422AndWritesNoAdditionalActivityLogEntry()
    {
        var client = await AuthenticatedClientAsync();
        var printer = await RegisterPrinterAsync(client);

        var beforeLogResponse = await client.GetAsync(new Uri("/api/v1/audit-log?page=1&pageSize=25", UriKind.Relative));
        var beforeCount = (await beforeLogResponse.Content.ReadFromJsonAsync<AuditLogPageResponse>())!
            .Items.Count(e => e.EntityId == printer.Id.ToString());

        var response = await client.PatchAsJsonAsync(
            new Uri($"/api/v1/printers/{printer.Id}", UriKind.Relative),
            new EditPrinterRequest { Name = printer.Name, SerialNumber = "SOMETHING-ELSE" });

        response.StatusCode.Should().Be((HttpStatusCode)422);
        var problem = await response.Content.ReadFromJsonAsync<Microsoft.AspNetCore.Mvc.ProblemDetails>();
        problem!.Detail.Should().Contain("serialNumber");

        var afterLogResponse = await client.GetAsync(new Uri("/api/v1/audit-log?page=1&pageSize=25", UriKind.Relative));
        var afterCount = (await afterLogResponse.Content.ReadFromJsonAsync<AuditLogPageResponse>())!
            .Items.Count(e => e.EntityId == printer.Id.ToString());

        afterCount.Should().Be(beforeCount);
    }

    // --- 012 US1: build plate selection, now its own slicer-settings endpoint ---

    [Fact]
    public async Task Edit_GetPrinterDetail_CompatibleBuildPlateTypesAndLabelsPresentBeforeAnyPlateIsSelected()
    {
        var client = await AuthenticatedClientAsync();
        var printer = await RegisterPrinterAsync(client);

        var detail = await client.GetFromJsonAsync<PrinterDetailResponse>(new Uri($"/api/v1/printers/{printer.Id}", UriKind.Relative));

        detail!.BuildPlateType.Should().BeNull();
        detail.BuildPlateTypeLabel.Should().BeNull();
        detail.CompatibleBuildPlateTypes.Should().HaveCount(6);
        detail.CompatibleBuildPlateTypes.Should().Contain(o => o.Value == "Cool" && o.Label == "Cool Plate");
    }

    [Fact]
    public async Task SlicerSettings_SelectBuildPlate_Returns200PersistsAndWritesSelectedActivityEvent()
    {
        var client = await AuthenticatedClientAsync();
        var printer = await RegisterPrinterAsync(client);

        var response = await client.PutAsJsonAsync(
            new Uri($"/api/v1/printers/{printer.Id}/slicer-settings", UriKind.Relative),
            new EditPrinterSlicerSettingsRequest { BuildPlateType = "TexturedPei" });

        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var detail = await client.GetFromJsonAsync<PrinterDetailResponse>(new Uri($"/api/v1/printers/{printer.Id}", UriKind.Relative));
        detail!.BuildPlateType.Should().Be("TexturedPei");
        detail.BuildPlateTypeLabel.Should().Be("Textured PEI Plate");

        var activityLogResponse = await client.GetAsync(new Uri("/api/v1/audit-log?page=1&pageSize=25", UriKind.Relative));
        var page = await activityLogResponse.Content.ReadFromJsonAsync<AuditLogPageResponse>();
        page!.Items.Should().Contain(e => e.EntityId == printer.Id.ToString() && e.EventType == "PrinterBuildPlateSelected");
    }

    [Fact]
    public async Task SlicerSettings_ClearBuildPlate_Returns200AndWritesClearedActivityEvent()
    {
        var client = await AuthenticatedClientAsync();
        var printer = await RegisterPrinterAsync(client);
        await client.PutAsJsonAsync(
            new Uri($"/api/v1/printers/{printer.Id}/slicer-settings", UriKind.Relative),
            new EditPrinterSlicerSettingsRequest { BuildPlateType = "TexturedPei" });

        var response = await client.PutAsJsonAsync(
            new Uri($"/api/v1/printers/{printer.Id}/slicer-settings", UriKind.Relative),
            new EditPrinterSlicerSettingsRequest { BuildPlateType = null });

        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var detail = await client.GetFromJsonAsync<PrinterDetailResponse>(new Uri($"/api/v1/printers/{printer.Id}", UriKind.Relative));
        detail!.BuildPlateType.Should().BeNull();

        var activityLogResponse = await client.GetAsync(new Uri("/api/v1/audit-log?page=1&pageSize=25", UriKind.Relative));
        var page = await activityLogResponse.Content.ReadFromJsonAsync<AuditLogPageResponse>();
        page!.Items.Should().Contain(e => e.EntityId == printer.Id.ToString() && e.EventType == "PrinterBuildPlateCleared");
    }

    [Fact]
    public async Task SlicerSettings_BuildPlateNotInModelsCompatibleSet_Returns422()
    {
        var client = await AuthenticatedClientAsync();
        var printerId = await RegisterBambuA1PrinterDirectlyAsync();

        // A1's compatible set is {TexturedPei, HighTemp} — Cool is not in it.
        var response = await client.PutAsJsonAsync(
            new Uri($"/api/v1/printers/{printerId}/slicer-settings", UriKind.Relative),
            new EditPrinterSlicerSettingsRequest { BuildPlateType = "Cool" });

        response.StatusCode.Should().Be((HttpStatusCode)422);
        var problem = await response.Content.ReadFromJsonAsync<Microsoft.AspNetCore.Mvc.ProblemDetails>();
        problem!.Title.Should().Be("Build plate incompatible with printer model");
    }

    [Fact]
    public async Task SlicerSettings_BuildPlate_Returns200OnItsOwn()
    {
        var client = await AuthenticatedClientAsync();
        var printer = await RegisterPrinterAsync(client);

        var response = await client.PutAsJsonAsync(
            new Uri($"/api/v1/printers/{printer.Id}/slicer-settings", UriKind.Relative),
            new EditPrinterSlicerSettingsRequest { BuildPlateType = "HighTemp" });

        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var detail = await client.GetFromJsonAsync<PrinterDetailResponse>(new Uri($"/api/v1/printers/{printer.Id}", UriKind.Relative));
        detail!.BuildPlateType.Should().Be("HighTemp");
    }

    // A real printer cannot take any job without its plate: fleet and detail both say so until the operator selects one.
    [Fact]
    public async Task SetupIssues_NamePrinterWithoutAPlate_AndClearOnceOneIsSelected()
    {
        var client = await AuthenticatedClientAsync();
        var realPrinterId = await RegisterBambuA1PrinterDirectlyAsync();
        var simulator = await RegisterPrinterAsync(client, "10.0.0.97");

        async Task<IReadOnlyList<string>> FleetIssuesAsync(Guid id) =>
            (await client.GetFromJsonAsync<ListFleetResponse>(new Uri("/api/v1/printers", UriKind.Relative)))!.Printers.Single(p => p.Id == id).SetupIssues;

        (await FleetIssuesAsync(realPrinterId)).Should().Equal("Select a build plate");
        (await client.GetFromJsonAsync<PrinterDetailResponse>(new Uri($"/api/v1/printers/{realPrinterId}", UriKind.Relative)))!
            .SetupIssues.Should().Equal("Select a build plate");
        (await FleetIssuesAsync(simulator.Id)).Should().BeEmpty("the simulator stands in for every plate");

        await client.PutAsJsonAsync(
            new Uri($"/api/v1/printers/{realPrinterId}/slicer-settings", UriKind.Relative),
            new EditPrinterSlicerSettingsRequest { BuildPlateType = "TexturedPei" });

        (await FleetIssuesAsync(realPrinterId)).Should().BeEmpty();
        (await client.GetFromJsonAsync<PrinterDetailResponse>(new Uri($"/api/v1/printers/{realPrinterId}", UriKind.Relative)))!
            .SetupIssues.Should().BeEmpty();
    }

    [Fact]
    public async Task SlicerSettings_TwoSameModelPrinters_KeepIndependentBuildPlateSelections()
    {
        var client = await AuthenticatedClientAsync();
        var printerAId = await RegisterBambuA1PrinterDirectlyAsync();
        var printerBId = await RegisterBambuA1PrinterDirectlyAsync();

        await client.PutAsJsonAsync(
            new Uri($"/api/v1/printers/{printerAId}/slicer-settings", UriKind.Relative),
            new EditPrinterSlicerSettingsRequest { BuildPlateType = "TexturedPei" });
        await client.PutAsJsonAsync(
            new Uri($"/api/v1/printers/{printerBId}/slicer-settings", UriKind.Relative),
            new EditPrinterSlicerSettingsRequest { BuildPlateType = "HighTemp" });

        var detailA = await client.GetFromJsonAsync<PrinterDetailResponse>(new Uri($"/api/v1/printers/{printerAId}", UriKind.Relative));
        var detailB = await client.GetFromJsonAsync<PrinterDetailResponse>(new Uri($"/api/v1/printers/{printerBId}", UriKind.Relative));

        detailA!.BuildPlateType.Should().Be("TexturedPei");
        detailB!.BuildPlateType.Should().Be("HighTemp");
    }

    [Fact]
    public async Task Edit_NameOnly_LeavesTheBuildPlateAlone()
    {
        var client = await AuthenticatedClientAsync();
        var printer = await RegisterPrinterAsync(client);
        await client.PutAsJsonAsync(
            new Uri($"/api/v1/printers/{printer.Id}/slicer-settings", UriKind.Relative),
            new EditPrinterSlicerSettingsRequest { BuildPlateType = "HighTemp" });

        var response = await client.PatchAsJsonAsync(
            new Uri($"/api/v1/printers/{printer.Id}", UriKind.Relative),
            new EditPrinterRequest { Name = "Renamed" });

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var detail = await client.GetFromJsonAsync<PrinterDetailResponse>(new Uri($"/api/v1/printers/{printer.Id}", UriKind.Relative));
        detail!.BuildPlateType.Should().Be("HighTemp");
    }

    // The migration moved the plate off "change connection": Operators swap plates, and so can any group that could before.
    [Fact]
    public async Task Migration_GrantsSlicerSettingsToOperatorsAndToEveryGroupThatCouldChangeTheConnection()
    {
        await AuthenticatedClientAsync(); // boots the app and runs the migrations
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<OpenFarm.Infrastructure.Persistence.OpenFarmDbContext>();
        var operators = Guid.Parse("f246d189-b11c-4494-9999-000000000002");
        var viewers = Guid.Parse("f246d189-b11c-4494-9999-000000000003");

        var claims = db.RoleClaims.Where(c => c.ClaimValue == "printers:edit-slicer-settings").Select(c => c.RoleId).ToList();

        claims.Should().Contain(operators);
        claims.Should().NotContain(viewers);
    }
}
