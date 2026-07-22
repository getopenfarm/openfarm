using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using OpenFarm.Api.Endpoints.ActivityLog;
using OpenFarm.Api.Endpoints.Auth;
using OpenFarm.Api.Endpoints.Printers;
using OpenFarm.Application.Auth;
using OpenFarm.Application.Profiles;
using OpenFarm.Domain.Organizations;
using OpenFarm.Domain.Profiles;

namespace OpenFarm.Api.IntegrationTests.Printers;

/// <summary>
/// Requires Docker (Testcontainers.PostgreSql) — not runnable in every sandbox, but exercised by CI.
/// </summary>
public class EditPrinterEndpointTests(CustomWebApplicationFactory factory) : IClassFixture<CustomWebApplicationFactory>
{
    private readonly HttpClient _client = factory.CreateClient();

    private async Task<HttpClient> AuthenticatedClientAsync()
    {
        var loginResponse = await _client.PostAsJsonAsync(
            new Uri("/api/v1/auth/login", UriKind.Relative),
            new LoginCommand("admin@openfarm.local", "ChangeMe123!"));
        var body = await loginResponse.Content.ReadFromJsonAsync<LoginResponse>();

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
}
