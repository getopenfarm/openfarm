using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using OpenFarm.Domain.Profiles;
using OpenFarm.Application.Profiles;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using OpenFarm.Infrastructure.Persistence;
using OpenFarm.Api.Endpoints.Auth;
using OpenFarm.Api.Endpoints.Profiles;
using OpenFarm.Application.Auth;

namespace OpenFarm.Api.IntegrationTests.Profiles;

// FR-010, US3 Acceptance Scenarios 1-2 — every imported machine/filament profile is listed with its
// name, source format, and derived spec fields.
public class ListProfilesEndpointTests(CustomWebApplicationFactory factory) : IClassFixture<CustomWebApplicationFactory>
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
    public async Task ListMachineAndFilamentProfiles_ReturnsImportedProfilesWithKeyFields()
    {
        var client = await AuthenticatedClientAsync();
        var suffix = Guid.NewGuid().ToString("N");
        var machineName = $"Test Printer {suffix}";
        var filamentName = $"Test PLA {suffix}";
        var bundle = ProfileBundleTestHelper.BuildZip(
            ($"machine/{machineName}.json", $$"""{"name":"{{machineName}}","nozzle_diameter":["0.4"]}"""),
            ($"filament/{filamentName}.json", $$"""{"name":"{{filamentName}}","filament_type":["PLA"]}"""));

        var selections = JsonSerializer.Serialize(new object[]
        {
            new { type = "Machine", name = machineName, duplicateAction = (string?)null },
            new { type = "Filament", name = filamentName, duplicateAction = (string?)null },
        });
        using var content = ProfileBundleTestHelper.BuildUploadContent(bundle, "bundle.zip", selections);
        var confirmResponse = await client.PostAsync(new Uri("/api/v1/profiles/imports/confirm", UriKind.Relative), content);
        confirmResponse.EnsureSuccessStatusCode();

        var machineList = await client.GetFromJsonAsync<ListMachineProfilesResponse>(new Uri("/api/v1/profiles/machine", UriKind.Relative));
        var machineEntry = machineList!.Items.Should().ContainSingle(p => p.Name == machineName).Subject;
        machineEntry.SourceFormat.Should().Be("OrcaSlicer");
        machineEntry.NozzleSizeMm.Should().Be(0.4);

        var filamentList = await client.GetFromJsonAsync<ListFilamentProfilesResponse>(new Uri("/api/v1/profiles/filament", UriKind.Relative));
        var filamentEntry = filamentList!.Items.Should().ContainSingle(p => p.Name == filamentName).Subject;
        filamentEntry.SourceFormat.Should().Be("OrcaSlicer");
        filamentEntry.Material.Should().Be("PLA");
    }

    // Per-version catalogs: once a System preset exists in two ingested versions, every picker lists only the farm
    // default's version — the factory bundles OrcaSlicer 2.2.0 (the auto default), so 2.2.0 wins over 2.1.0.
    // Runs the scoped predicate against real Postgres, which the in-memory fakes cannot prove translates.
    [Fact]
    public async Task Pickers_ListOnlyTheFarmDefaultsCatalogVersion()
    {
        var suffix = Guid.NewGuid().ToString("N");
        var machineName = $"Bambu Lab P1S {suffix}";
        var processName = $"0.20mm Standard {suffix}";
        var filamentName = $"Bambu PLA Basic {suffix}";
        using (var scope = factory.Services.CreateScope())
        {
            var machines = scope.ServiceProvider.GetRequiredService<IMachineProfileRepository>();
            var processes = scope.ServiceProvider.GetRequiredService<IProcessProfileRepository>();
            var filaments = scope.ServiceProvider.GetRequiredService<IFilamentProfileRepository>();
            foreach (var version in new[] { "2.1.0", "2.2.0" })
            {
                await machines.AddAsync(MachineProfile.CreateSystemProfile(
                    machineName, ProfileSourceFormat.OrcaSlicer, null, "{}", "Bambulab", "GM014",
                    isSelectable: true, vendorMachineModelName: null, includedTemplateNames: null, sourceVersion: version), default);
                await processes.AddAsync(ProcessProfile.CreateSystemProfile(
                    processName, ProfileSourceFormat.OrcaSlicer, null, "{}", "Bambulab", "GP004",
                    isSelectable: true, compatibleMachineNames: null, sourceVersion: version), default);
                await filaments.AddAsync(FilamentProfile.CreateSystemProfile(
                    filamentName, ProfileSourceFormat.OrcaSlicer, null, "{}", "Bambulab", "GFA00",
                    isSelectable: true, compatibleMachineNames: null, materialType: "PLA", includedTemplateNames: null, sourceVersion: version), default);
            }
        }

        var client = await AuthenticatedClientAsync();

        var machineList = await client.GetFromJsonAsync<ListMachineProfilesResponse>(new Uri($"/api/v1/profiles/machine?origin=All&q={machineName}", UriKind.Relative));
        machineList!.Items.Should().ContainSingle(p => p.Name == machineName).Which.SourceVersion.Should().Be("2.2.0");

        var processList = await client.GetFromJsonAsync<ListProcessProfilesResponse>(new Uri($"/api/v1/profiles/process?origin=All&q={processName}", UriKind.Relative));
        processList!.Items.Should().ContainSingle(p => p.Name == processName).Which.SourceVersion.Should().Be("2.2.0");

        var filamentList = await client.GetFromJsonAsync<ListFilamentProfilesResponse>(new Uri($"/api/v1/profiles/filament?origin=All&q={filamentName}", UriKind.Relative));
        filamentList!.Items.Should().ContainSingle(p => p.Name == filamentName).Which.SourceVersion.Should().Be("2.2.0");
    }

    // Production slices with the pinned slicer's own presets only, so the farm default engine's pickers offer only its
    // own format — System and imported alike. The page that manages imports still lists every format (real Postgres).
    [Fact]
    public async Task Pickers_ListOnlyTheDefaultEnginesFormat_ButTheImportsPageListsEveryFormat()
    {
        var suffix = Guid.NewGuid().ToString("N");
        var orcaUserName = $"My Orca P1S {suffix}";
        var bambuUserName = $"My Bambu P1S {suffix}";
        var bambuSystemName = $"Bambu Lab H2D {suffix}";
        using (var scope = factory.Services.CreateScope())
        {
            var machines = scope.ServiceProvider.GetRequiredService<IMachineProfileRepository>();
            await machines.AddAsync(MachineProfile.Create(orcaUserName, ProfileSourceFormat.OrcaSlicer, null, "{}"), default);
            await machines.AddAsync(MachineProfile.Create(bambuUserName, ProfileSourceFormat.BambuStudio, null, "{}"), default);
            await machines.AddAsync(MachineProfile.CreateSystemProfile(
                bambuSystemName, ProfileSourceFormat.BambuStudio, null, "{}", "Bambulab", "GM099",
                isSelectable: true, vendorMachineModelName: null, includedTemplateNames: null, sourceVersion: "2.7.1"), default);
        }

        var client = await AuthenticatedClientAsync();

        var picker = await client.GetFromJsonAsync<ListMachineProfilesResponse>(new Uri($"/api/v1/profiles/machine?origin=All&q={suffix}", UriKind.Relative));
        var imports = await client.GetFromJsonAsync<ListMachineProfilesResponse>(new Uri("/api/v1/profiles/machine", UriKind.Relative));

        picker!.Items.Select(i => i.Name).Should().BeEquivalentTo([orcaUserName], "the farm default is OrcaSlicer: neither Bambu Studio preset is offered");
        imports!.Items.Select(i => i.Name).Should().Contain([orcaUserName, bambuUserName]);
    }

    // A System row always has a version, so the identity key never contains NULL: it is unique per catalog version,
    // and the same identity in another version is another row.
    [Fact]
    public async Task UniqueKey_IsPerCatalogVersion()
    {
        var suffix = Guid.NewGuid().ToString("N");
        MachineProfile Row(string version) => MachineProfile.CreateSystemProfile(
            $"Bambu Lab A1 {suffix}", ProfileSourceFormat.OrcaSlicer, null, "{}", $"Bambulab-{suffix}", "GM020",
            isSelectable: true, vendorMachineModelName: null, includedTemplateNames: null, sourceVersion: version);

        async Task AddAsync(MachineProfile row)
        {
            using var scope = factory.Services.CreateScope();
            await scope.ServiceProvider.GetRequiredService<IMachineProfileRepository>().AddAsync(row, default);
        }

        await AddAsync(Row("2.2.0"));
        await AddAsync(Row("2.3.1"));

        await ((Func<Task>)(() => AddAsync(Row("2.2.0")))).Should().ThrowAsync<DbUpdateException>();
    }

    [Fact]
    public async Task Database_RejectsASystemRowWithoutAVersionAndAUserRowWithOne()
    {
        var suffix = Guid.NewGuid().ToString("N");
        var systemWithoutVersion = MachineProfile.CreateSystemProfile(
            $"Bambu Lab A1 {suffix}", ProfileSourceFormat.OrcaSlicer, null, "{}", $"Bambulab-{suffix}", "GM021",
            isSelectable: true, vendorMachineModelName: null, includedTemplateNames: null, sourceVersion: "2.2.0");
        SetSourceVersion(systemWithoutVersion, null);
        var userWithVersion = MachineProfile.Create($"My printer {suffix}", ProfileSourceFormat.OrcaSlicer, null, "{}");
        SetSourceVersion(userWithVersion, "2.2.0");

        foreach (var row in new[] { systemWithoutVersion, userWithVersion })
        {
            using var scope = factory.Services.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<OpenFarmDbContext>();
            db.MachineProfiles.Add(row);

            var failure = await ((Func<Task>)(() => db.SaveChangesAsync())).Should().ThrowAsync<DbUpdateException>();
            failure.Which.InnerException.Should().BeOfType<PostgresException>()
                .Which.ConstraintName.Should().Be("CK_MachineProfiles_SystemHasVersion");
        }
    }

    // The domain factories refuse these states, so the database rule is exercised by writing past them.
    private static void SetSourceVersion(MachineProfile row, string? version) =>
        typeof(MachineProfile).GetProperty(nameof(MachineProfile.SourceVersion))!.SetValue(row, version);
}
