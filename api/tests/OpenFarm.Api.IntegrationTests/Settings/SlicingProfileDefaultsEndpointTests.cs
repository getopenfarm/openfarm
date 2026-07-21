using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using OpenFarm.Api.Endpoints.Auth;
using OpenFarm.Api.Endpoints.Profiles;
using OpenFarm.Api.Endpoints.Settings;
using OpenFarm.Api.IntegrationTests.Profiles;
using OpenFarm.Application.Auth;

namespace OpenFarm.Api.IntegrationTests.Settings;

public class SlicingProfileDefaultsEndpointTests(CustomWebApplicationFactory factory) : IClassFixture<CustomWebApplicationFactory>
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

    // Confirms a machine + process + filament preset in one bundle (real import path, so the
    // multi-tenant context is the genuine authenticated-request one, not a raw DbContext write) and
    // returns their created ids.
    private static async Task<(Guid MachineId, Guid ProcessId, Guid FilamentId)> ConfirmProfilesAsync(
        HttpClient client, string suffix, string fileName = "bundle.zip")
    {
        var zipBytes = ProfileBundleTestHelper.BuildZip(
            ($"machine/Test Printer {suffix}.json", $$"""{"name":"Test Printer {{suffix}}","nozzle_diameter":["0.4"]}"""),
            ($"process/Test Process {suffix}.json", $$"""{"name":"Test Process {{suffix}}","layer_height":"0.2"}"""),
            ($"filament/Test PLA {suffix}.json", $$"""{"name":"Test PLA {{suffix}}","filament_type":["PLA"]}"""));

        var selections = JsonSerializer.Serialize(new object[]
        {
            new { type = "Machine", name = $"Test Printer {suffix}", duplicateAction = (string?)null },
            new { type = "Process", name = $"Test Process {suffix}", duplicateAction = (string?)null },
            new { type = "Filament", name = $"Test PLA {suffix}", duplicateAction = (string?)null },
        });

        using var content = ProfileBundleTestHelper.BuildUploadContent(zipBytes, fileName, selections);
        var response = await client.PostAsync(new Uri("/api/v1/profiles/imports/confirm", UriKind.Relative), content);
        response.StatusCode.Should().Be(HttpStatusCode.Created);

        var body = await response.Content.ReadFromJsonAsync<ConfirmProfileImportResponse>();
        return (
            body!.CreatedMachineProfiles.Single().Id,
            body.CreatedProcessProfiles.Single().Id,
            body.CreatedFilamentProfiles.Single().Id);
    }

    [Fact]
    public async Task Put_DefaultMachineProfile_ValidId_Returns200()
    {
        var client = await AuthenticatedClientAsync();
        var (machineId, _, _) = await ConfirmProfilesAsync(client, Guid.NewGuid().ToString("N"));

        var response = await client.PutAsJsonAsync(
            new Uri("/api/v1/settings/default-machine-profile", UriKind.Relative),
            new { machineProfileId = machineId });

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadFromJsonAsync<DefaultMachineProfileResponse>();
        body!.MachineProfileId.Should().Be(machineId);
    }

    [Fact]
    public async Task Put_DefaultMachineProfile_UnknownId_Returns404()
    {
        var client = await AuthenticatedClientAsync();

        var response = await client.PutAsJsonAsync(
            new Uri("/api/v1/settings/default-machine-profile", UriKind.Relative),
            new { machineProfileId = Guid.NewGuid() });

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Put_MachineDefaultProcessProfile_SameSourceFormat_Returns200AndGetReflectsPairing()
    {
        var client = await AuthenticatedClientAsync();
        var (machineId, processId, _) = await ConfirmProfilesAsync(client, Guid.NewGuid().ToString("N"));

        await client.PutAsJsonAsync(
            new Uri("/api/v1/settings/default-machine-profile", UriKind.Relative),
            new { machineProfileId = machineId });

        var pairResponse = await client.PutAsJsonAsync(
            new Uri($"/api/v1/profiles/machine/{machineId}/default-process-profile", UriKind.Relative),
            new { processProfileId = processId });
        pairResponse.StatusCode.Should().Be(HttpStatusCode.OK);

        var getResponse = await client.GetAsync(new Uri("/api/v1/settings/slicing-profile-defaults", UriKind.Relative));
        var defaults = await getResponse.Content.ReadFromJsonAsync<SlicingProfileDefaultsResponse>();

        defaults!.DefaultMachineProfile.Should().NotBeNull();
        defaults.DefaultMachineProfile!.Id.Should().Be(machineId);
        defaults.DefaultMachineProfile.DefaultProcessProfile.Should().NotBeNull();
        defaults.DefaultMachineProfile.DefaultProcessProfile!.Id.Should().Be(processId);
    }

    [Fact]
    public async Task Put_MachineDefaultProcessProfile_CrossSourceFormat_Returns422()
    {
        var client = await AuthenticatedClientAsync();
        var suffix = Guid.NewGuid().ToString("N");
        var (machineId, _, _) = await ConfirmProfilesAsync(client, suffix, "orca-bundle.zip");
        var (_, bambuProcessId, _) = await ConfirmProfilesAsync(client, $"bambu-{suffix}", "bambu-bundle.bbscfg");

        var response = await client.PutAsJsonAsync(
            new Uri($"/api/v1/profiles/machine/{machineId}/default-process-profile", UriKind.Relative),
            new { processProfileId = bambuProcessId });

        response.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
    }

    [Fact]
    public async Task Put_FilamentDefault_ThenReplacesOnSecondCall_GetReflectsLatest()
    {
        var client = await AuthenticatedClientAsync();
        var (_, _, firstFilamentId) = await ConfirmProfilesAsync(client, Guid.NewGuid().ToString("N"));
        var (_, _, secondFilamentId) = await ConfirmProfilesAsync(client, Guid.NewGuid().ToString("N"));
        var materialType = $"PLA-{Guid.NewGuid():N}";

        var firstResponse = await client.PutAsJsonAsync(
            new Uri($"/api/v1/settings/filament-defaults/{materialType}", UriKind.Relative),
            new { filamentProfileId = firstFilamentId });
        firstResponse.StatusCode.Should().Be(HttpStatusCode.OK);

        await client.PutAsJsonAsync(
            new Uri($"/api/v1/settings/filament-defaults/{materialType}", UriKind.Relative),
            new { filamentProfileId = secondFilamentId });

        var getResponse = await client.GetAsync(new Uri("/api/v1/settings/slicing-profile-defaults", UriKind.Relative));
        var defaults = await getResponse.Content.ReadFromJsonAsync<SlicingProfileDefaultsResponse>();

        var entry = defaults!.FilamentDefaults.Single(f => f.MaterialType == materialType);
        entry.FilamentProfileId.Should().Be(secondFilamentId);
    }
}
