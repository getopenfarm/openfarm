using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using OpenFarm.Api.Endpoints.ActivityLog;
using OpenFarm.Api.Endpoints.Auth;
using OpenFarm.Api.Endpoints.Printers;
using OpenFarm.Application.Auth;
using OpenFarm.Application.Printers;
using OpenFarm.Application.Profiles;
using OpenFarm.Domain.Organizations;
using OpenFarm.Domain.Printers;
using OpenFarm.Domain.Profiles;

namespace OpenFarm.Api.IntegrationTests.Printers;

/// <summary>
/// Requires Docker (Testcontainers.PostgreSql) — not runnable in every sandbox, but exercised by CI.
/// </summary>
public class EditPrinterEndpointTests(CustomWebApplicationFactory factory) : IClassFixture<CustomWebApplicationFactory>
{
    private static readonly JsonSerializerOptions DiagnosticJsonOptions = new(JsonSerializerDefaults.Web);

    private readonly HttpClient _client = factory.CreateClient();

    private async Task<HttpClient> AuthenticatedClientAsync()
    {
        var loginResponse = await _client.PostAsJsonAsync(
            new Uri("/api/v1/auth/login", UriKind.Relative),
            new LoginCommand("admin", "admin-openfarm"));

        // TEMP DIAGNOSTIC (remove once the intermittent CI JsonException here is root-caused): read as
        // a string first so a malformed body's actual content — not just System.Text.Json's opaque
        // "'X' is an invalid start of a value" — shows up in the CI log.
        var rawBody = await loginResponse.Content.ReadAsStringAsync();
        LoginResponse? body;
        try
        {
            body = JsonSerializer.Deserialize<LoginResponse>(rawBody, DiagnosticJsonOptions);
        }
        catch (JsonException ex)
        {
            throw new InvalidOperationException(
                $"Login response failed to parse as JSON. Status: {(int)loginResponse.StatusCode} " +
                $"{loginResponse.StatusCode}. Content-Type: {loginResponse.Content.Headers.ContentType}. " +
                $"Content-Length header: {loginResponse.Content.Headers.ContentLength}. " +
                $"Raw body ({rawBody.Length} chars): {rawBody[..Math.Min(rawBody.Length, 2000)]}", ex);
        }

        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", body!.Token);
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
        return (await registerResponse.Content.ReadFromJsonAsync<PrinterSummaryResponse>())!;
    }

    // Seeds a compatible Machine + Process System profile pair directly through the repositories
    // (contracts/profiles-api.md's US2 scenarios need `Origin = System`, which only the ingestion
    // pipeline's `CreateSystemProfile` factories can produce — cheaper here than staging a full
    // vendor bundle and running ingestion just to get one compatible pair).
    private async Task<(Guid MachineProfileId, Guid ProcessProfileId, string MachineProfileName)> SeedCompatibleSystemProfilePairAsync()
    {
        await factory.EnsureTenantInfoResolvedAsync();
        using var scope = factory.CreateTenantScope();
        var machineRepo = scope.ServiceProvider.GetRequiredService<IMachineProfileRepository>();
        var processRepo = scope.ServiceProvider.GetRequiredService<IProcessProfileRepository>();

        var suffix = Guid.NewGuid().ToString("N");
        var machineName = $"Edit Test Machine {suffix}";
        var machine = MachineProfile.CreateSystemProfile(
            Organization.WellKnownId, machineName, ProfileSourceFormat.OrcaSlicer, null,
            """{"name":"placeholder"}""", "EditPrinterEndpointTests", suffix, true, null, null);
        await machineRepo.AddAsync(machine, CancellationToken.None);

        var process = ProcessProfile.CreateSystemProfile(
            Organization.WellKnownId, $"Edit Test Process {suffix}", ProfileSourceFormat.OrcaSlicer, null,
            """{"name":"placeholder"}""", "EditPrinterEndpointTests", $"proc-{suffix}", true, [machineName]);
        await processRepo.AddAsync(process, CancellationToken.None);

        return (machine.Id, process.Id, machineName);
    }

    // 012 US1 — a real Bambu-brand registration goes through RegisterPrinterCommandHandler's live
    // connector verification (research.md — only "System"/"Simulation" routes to the no-network
    // simulator), which no CI runner can satisfy against a real printer. Seeding directly through the
    // repository (the same "cheaper than staging a full pipeline" reasoning
    // SeedCompatibleSystemProfilePairAsync already uses) is how this suite gets a printer whose
    // catalog model has a narrower CompatiblePlateTypes set than System/Simulation's "all six".
    private async Task<Guid> RegisterBambuA1PrinterDirectlyAsync()
    {
        await factory.EnsureTenantInfoResolvedAsync();
        using var scope = factory.CreateTenantScope();
        var printerRepo = scope.ServiceProvider.GetRequiredService<IPrinterRepository>();

        var printer = Printer.Create(Organization.WellKnownId, "A1 Test Printer", "Bambu", "A1", "10.0.0.96", $"A1-{Guid.NewGuid():N}");
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

        var activityLogResponse = await client.GetAsync(new Uri("/api/v1/activity-log?page=1&pageSize=25", UriKind.Relative));
        var page = await activityLogResponse.Content.ReadFromJsonAsync<ActivityLogPageResponse>();
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

        var beforeLogResponse = await client.GetAsync(new Uri("/api/v1/activity-log?page=1&pageSize=25", UriKind.Relative));
        var beforeCount = (await beforeLogResponse.Content.ReadFromJsonAsync<ActivityLogPageResponse>())!
            .Items.Count(e => e.EntityId == printer.Id.ToString());

        var response = await client.PatchAsJsonAsync(
            new Uri($"/api/v1/printers/{printer.Id}", UriKind.Relative),
            new EditPrinterRequest { Name = printer.Name, SerialNumber = "SOMETHING-ELSE" });

        response.StatusCode.Should().Be((HttpStatusCode)422);
        var problem = await response.Content.ReadFromJsonAsync<Microsoft.AspNetCore.Mvc.ProblemDetails>();
        problem!.Detail.Should().Contain("serialNumber");

        var afterLogResponse = await client.GetAsync(new Uri("/api/v1/activity-log?page=1&pageSize=25", UriKind.Relative));
        var afterCount = (await afterLogResponse.Content.ReadFromJsonAsync<ActivityLogPageResponse>())!
            .Items.Count(e => e.EntityId == printer.Id.ToString());

        afterCount.Should().Be(beforeCount);
    }

    [Fact]
    public async Task Edit_LinkMachineAndCompatibleProcessProfile_Returns200AndWritesLinkedActivityEvents()
    {
        var client = await AuthenticatedClientAsync();
        var printer = await RegisterPrinterAsync(client);
        var (machineProfileId, processProfileId, _) = await SeedCompatibleSystemProfilePairAsync();

        var response = await client.PatchAsJsonAsync(
            new Uri($"/api/v1/printers/{printer.Id}", UriKind.Relative),
            new EditPrinterRequest { Name = printer.Name, MachineProfileId = machineProfileId, ProcessProfileId = processProfileId });

        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var activityLogResponse = await client.GetAsync(new Uri("/api/v1/activity-log?page=1&pageSize=25", UriKind.Relative));
        var page = await activityLogResponse.Content.ReadFromJsonAsync<ActivityLogPageResponse>();
        page!.Items.Should().Contain(e => e.EntityId == printer.Id.ToString() && e.EventType == "PrinterMachineProfileLinked");
        page.Items.Should().Contain(e => e.EntityId == printer.Id.ToString() && e.EventType == "PrinterProcessProfileLinked");
    }

    [Fact]
    public async Task Edit_ProcessProfileWithoutMachineProfile_Returns422()
    {
        var client = await AuthenticatedClientAsync();
        var printer = await RegisterPrinterAsync(client);
        var (_, processProfileId, _) = await SeedCompatibleSystemProfilePairAsync();

        var response = await client.PatchAsJsonAsync(
            new Uri($"/api/v1/printers/{printer.Id}", UriKind.Relative),
            new EditPrinterRequest { Name = printer.Name, ProcessProfileId = processProfileId });

        response.StatusCode.Should().Be((HttpStatusCode)422);
    }

    [Fact]
    public async Task Edit_ProcessProfileIncompatibleWithSelectedMachine_Returns422()
    {
        var client = await AuthenticatedClientAsync();
        var printer = await RegisterPrinterAsync(client);
        var (_, processProfileId, _) = await SeedCompatibleSystemProfilePairAsync();
        var (otherMachineProfileId, _, _) = await SeedCompatibleSystemProfilePairAsync();

        var response = await client.PatchAsJsonAsync(
            new Uri($"/api/v1/printers/{printer.Id}", UriKind.Relative),
            new EditPrinterRequest { Name = printer.Name, MachineProfileId = otherMachineProfileId, ProcessProfileId = processProfileId });

        response.StatusCode.Should().Be((HttpStatusCode)422);
    }

    [Fact]
    public async Task Edit_TwoPrintersLinkedIndependently_EachKeepsItsOwnSelection()
    {
        var client = await AuthenticatedClientAsync();
        var printerA = await RegisterPrinterAsync(client, "10.0.0.94");
        var printerB = await RegisterPrinterAsync(client, "10.0.0.95");
        var (machineProfileIdA, processProfileIdA, _) = await SeedCompatibleSystemProfilePairAsync();
        var (machineProfileIdB, processProfileIdB, _) = await SeedCompatibleSystemProfilePairAsync();

        await client.PatchAsJsonAsync(
            new Uri($"/api/v1/printers/{printerA.Id}", UriKind.Relative),
            new EditPrinterRequest { Name = printerA.Name, MachineProfileId = machineProfileIdA, ProcessProfileId = processProfileIdA });
        await client.PatchAsJsonAsync(
            new Uri($"/api/v1/printers/{printerB.Id}", UriKind.Relative),
            new EditPrinterRequest { Name = printerB.Name, MachineProfileId = machineProfileIdB, ProcessProfileId = processProfileIdB });

        var detailA = await client.GetFromJsonAsync<PrinterDetailResponse>(new Uri($"/api/v1/printers/{printerA.Id}", UriKind.Relative));
        var detailB = await client.GetFromJsonAsync<PrinterDetailResponse>(new Uri($"/api/v1/printers/{printerB.Id}", UriKind.Relative));

        detailA!.MachineProfileId.Should().Be(machineProfileIdA);
        detailA.ProcessProfileId.Should().Be(processProfileIdA);
        detailB!.MachineProfileId.Should().Be(machineProfileIdB);
        detailB.ProcessProfileId.Should().Be(processProfileIdB);
    }

    [Fact]
    public async Task Edit_ClearMachineProfile_AlsoClearsProcessProfileLinkAndWritesUnlinkedEvents()
    {
        var client = await AuthenticatedClientAsync();
        var printer = await RegisterPrinterAsync(client);
        var (machineProfileId, processProfileId, _) = await SeedCompatibleSystemProfilePairAsync();

        await client.PatchAsJsonAsync(
            new Uri($"/api/v1/printers/{printer.Id}", UriKind.Relative),
            new EditPrinterRequest { Name = printer.Name, MachineProfileId = machineProfileId, ProcessProfileId = processProfileId });

        var response = await client.PatchAsJsonAsync(
            new Uri($"/api/v1/printers/{printer.Id}", UriKind.Relative),
            new EditPrinterRequest { Name = printer.Name });

        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var detail = await client.GetFromJsonAsync<PrinterDetailResponse>(new Uri($"/api/v1/printers/{printer.Id}", UriKind.Relative));
        detail!.MachineProfileId.Should().BeNull();
        detail.ProcessProfileId.Should().BeNull();

        var activityLogResponse = await client.GetAsync(new Uri("/api/v1/activity-log?page=1&pageSize=50", UriKind.Relative));
        var page = await activityLogResponse.Content.ReadFromJsonAsync<ActivityLogPageResponse>();
        page!.Items.Should().Contain(e => e.EntityId == printer.Id.ToString() && e.EventType == "PrinterMachineProfileUnlinked");
        page.Items.Should().Contain(e => e.EntityId == printer.Id.ToString() && e.EventType == "PrinterProcessProfileUnlinked");
    }

    // --- 012 US1: build plate selection (test-after, per plan.md's TDD scoping) ---

    [Fact]
    public async Task Edit_GetPrinterDetail_CompatibleBuildPlateTypesAndLabelsPresentEvenWithNoMachineProfileLinked()
    {
        var client = await AuthenticatedClientAsync();
        var printer = await RegisterPrinterAsync(client);

        var detail = await client.GetFromJsonAsync<PrinterDetailResponse>(new Uri($"/api/v1/printers/{printer.Id}", UriKind.Relative));

        detail!.MachineProfileId.Should().BeNull();
        detail.BuildPlateType.Should().BeNull();
        detail.BuildPlateTypeLabel.Should().BeNull();
        detail.CompatibleBuildPlateTypes.Should().HaveCount(6);
        detail.CompatibleBuildPlateTypes.Should().Contain(o => o.Value == "Cool" && o.Label == "Cool Plate");
    }

    [Fact]
    public async Task Edit_SelectBuildPlate_Returns200PersistsAndWritesSelectedActivityEvent()
    {
        var client = await AuthenticatedClientAsync();
        var printer = await RegisterPrinterAsync(client);

        var response = await client.PatchAsJsonAsync(
            new Uri($"/api/v1/printers/{printer.Id}", UriKind.Relative),
            new EditPrinterRequest { Name = printer.Name, BuildPlateType = "TexturedPei" });

        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var detail = await client.GetFromJsonAsync<PrinterDetailResponse>(new Uri($"/api/v1/printers/{printer.Id}", UriKind.Relative));
        detail!.BuildPlateType.Should().Be("TexturedPei");
        detail.BuildPlateTypeLabel.Should().Be("Textured PEI Plate");

        var activityLogResponse = await client.GetAsync(new Uri("/api/v1/activity-log?page=1&pageSize=25", UriKind.Relative));
        var page = await activityLogResponse.Content.ReadFromJsonAsync<ActivityLogPageResponse>();
        page!.Items.Should().Contain(e => e.EntityId == printer.Id.ToString() && e.EventType == "PrinterBuildPlateSelected");
    }

    [Fact]
    public async Task Edit_ClearBuildPlate_Returns200AndWritesClearedActivityEvent()
    {
        var client = await AuthenticatedClientAsync();
        var printer = await RegisterPrinterAsync(client);
        await client.PatchAsJsonAsync(
            new Uri($"/api/v1/printers/{printer.Id}", UriKind.Relative),
            new EditPrinterRequest { Name = printer.Name, BuildPlateType = "TexturedPei" });

        var response = await client.PatchAsJsonAsync(
            new Uri($"/api/v1/printers/{printer.Id}", UriKind.Relative),
            new EditPrinterRequest { Name = printer.Name });

        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var detail = await client.GetFromJsonAsync<PrinterDetailResponse>(new Uri($"/api/v1/printers/{printer.Id}", UriKind.Relative));
        detail!.BuildPlateType.Should().BeNull();

        var activityLogResponse = await client.GetAsync(new Uri("/api/v1/activity-log?page=1&pageSize=25", UriKind.Relative));
        var page = await activityLogResponse.Content.ReadFromJsonAsync<ActivityLogPageResponse>();
        page!.Items.Should().Contain(e => e.EntityId == printer.Id.ToString() && e.EventType == "PrinterBuildPlateCleared");
    }

    [Fact]
    public async Task Edit_BuildPlateNotInModelsCompatibleSet_Returns422()
    {
        var client = await AuthenticatedClientAsync();
        var printerId = await RegisterBambuA1PrinterDirectlyAsync();

        // A1's compatible set is {TexturedPei, HighTemp} — Cool is not in it.
        var response = await client.PatchAsJsonAsync(
            new Uri($"/api/v1/printers/{printerId}", UriKind.Relative),
            new EditPrinterRequest { Name = "A1 Test Printer", BuildPlateType = "Cool" });

        response.StatusCode.Should().Be((HttpStatusCode)422);
        var problem = await response.Content.ReadFromJsonAsync<Microsoft.AspNetCore.Mvc.ProblemDetails>();
        problem!.Title.Should().Be("Build plate incompatible with printer model");
    }

    [Fact]
    public async Task Edit_BuildPlateOnPrinterWithNoMachineProfile_Returns200_NoPrerequisite()
    {
        var client = await AuthenticatedClientAsync();
        var printer = await RegisterPrinterAsync(client);

        var response = await client.PatchAsJsonAsync(
            new Uri($"/api/v1/printers/{printer.Id}", UriKind.Relative),
            new EditPrinterRequest { Name = printer.Name, BuildPlateType = "HighTemp" });

        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var detail = await client.GetFromJsonAsync<PrinterDetailResponse>(new Uri($"/api/v1/printers/{printer.Id}", UriKind.Relative));
        detail!.MachineProfileId.Should().BeNull();
        detail.BuildPlateType.Should().Be("HighTemp");
    }

    [Fact]
    public async Task Edit_TwoSameModelPrinters_KeepIndependentBuildPlateSelections()
    {
        var client = await AuthenticatedClientAsync();
        var printerAId = await RegisterBambuA1PrinterDirectlyAsync();
        var printerBId = await RegisterBambuA1PrinterDirectlyAsync();

        await client.PatchAsJsonAsync(
            new Uri($"/api/v1/printers/{printerAId}", UriKind.Relative),
            new EditPrinterRequest { Name = "A1 Test Printer", BuildPlateType = "TexturedPei" });
        await client.PatchAsJsonAsync(
            new Uri($"/api/v1/printers/{printerBId}", UriKind.Relative),
            new EditPrinterRequest { Name = "A1 Test Printer", BuildPlateType = "HighTemp" });

        var detailA = await client.GetFromJsonAsync<PrinterDetailResponse>(new Uri($"/api/v1/printers/{printerAId}", UriKind.Relative));
        var detailB = await client.GetFromJsonAsync<PrinterDetailResponse>(new Uri($"/api/v1/printers/{printerBId}", UriKind.Relative));

        detailA!.BuildPlateType.Should().Be("TexturedPei");
        detailB!.BuildPlateType.Should().Be("HighTemp");
    }
}
