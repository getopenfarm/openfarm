using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using OpenFarm.Api.Endpoints.Auth;
using OpenFarm.Api.Endpoints.Profiles;
using OpenFarm.Application.Auth;

namespace OpenFarm.Api.IntegrationTests.Profiles;

// FR-012, FR-013 — a valid raw-data save is reflected on the next GET; malformed JSON is rejected
// with a 422 and leaves the previously saved value unchanged (US4 Acceptance Scenarios 1-2).
// Also covers data-model.md's chain-propagation-for-free behavior: editing a base's raw JSON
// changes its descendant's derived display fields on the descendant's very next read, with no
// propagation step anywhere.
public class UpdateProfileRawDataEndpointTests(CustomWebApplicationFactory factory) : IClassFixture<CustomWebApplicationFactory>
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

    // The RawDataJson column is jsonb — Postgres re-serializes it on write (e.g. adding a space
    // after ':'/','), so a stored value is never guaranteed byte-identical to what was submitted.
    // Compare structurally instead of with a raw string equality assertion.
    private static void AssertJsonEquivalent(string? actual, string expected)
    {
        actual.Should().NotBeNull();
        using var actualDoc = JsonDocument.Parse(actual!);
        using var expectedDoc = JsonDocument.Parse(expected);
        JsonSerializer.Serialize(actualDoc.RootElement).Should().Be(JsonSerializer.Serialize(expectedDoc.RootElement));
    }

    [Fact]
    public async Task UpdateMachineProfileRawData_ValidJson_IsReflectedOnNextGet()
    {
        var client = await AuthenticatedClientAsync();
        var suffix = Guid.NewGuid().ToString("N");
        var name = $"Edit Printer {suffix}";
        var bundle = ProfileBundleTestHelper.BuildZip(
            ($"machine/{name}.json", $$"""{"name":"{{name}}","nozzle_diameter":["0.4"]}"""));
        var selections = JsonSerializer.Serialize(new object[] { new { type = "Machine", name, duplicateAction = (string?)null } });
        using var content = ProfileBundleTestHelper.BuildUploadContent(bundle, "bundle.zip", selections);
        var confirmResponse = await client.PostAsync(new Uri("/api/v1/profiles/imports/confirm", UriKind.Relative), content);
        var created = await confirmResponse.Content.ReadFromJsonAsync<ConfirmProfileImportResponse>();
        var id = created!.CreatedMachineProfiles.Single().Id;

        var updatedJson = $$"""{"name":"{{name}}","nozzle_diameter":["0.6"]}""";
        var updateResponse = await client.PutAsJsonAsync(
            new Uri($"/api/v1/profiles/machine/{id}/raw-data", UriKind.Relative),
            new { RawDataJson = updatedJson });

        updateResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        var updated = await updateResponse.Content.ReadFromJsonAsync<MachineProfileDetailResponse>();
        updated!.NozzleSizeMm.Should().Be(0.6);

        var getResponse = await client.GetAsync(new Uri($"/api/v1/profiles/machine/{id}", UriKind.Relative));
        var detail = await getResponse.Content.ReadFromJsonAsync<MachineProfileDetailResponse>();
        AssertJsonEquivalent(detail!.RawDataJson, updatedJson);
        detail.NozzleSizeMm.Should().Be(0.6);
    }

    [Fact]
    public async Task UpdateMachineProfileRawData_MalformedJson_Returns422AndLeavesPreviousValueUnchanged()
    {
        var client = await AuthenticatedClientAsync();
        var suffix = Guid.NewGuid().ToString("N");
        var name = $"Reject Printer {suffix}";
        var originalJson = $$"""{"name":"{{name}}","nozzle_diameter":["0.4"]}""";
        var bundle = ProfileBundleTestHelper.BuildZip(($"machine/{name}.json", originalJson));
        var selections = JsonSerializer.Serialize(new object[] { new { type = "Machine", name, duplicateAction = (string?)null } });
        using var content = ProfileBundleTestHelper.BuildUploadContent(bundle, "bundle.zip", selections);
        var confirmResponse = await client.PostAsync(new Uri("/api/v1/profiles/imports/confirm", UriKind.Relative), content);
        var created = await confirmResponse.Content.ReadFromJsonAsync<ConfirmProfileImportResponse>();
        var id = created!.CreatedMachineProfiles.Single().Id;

        var updateResponse = await client.PutAsJsonAsync(
            new Uri($"/api/v1/profiles/machine/{id}/raw-data", UriKind.Relative),
            new { RawDataJson = "{not valid json" });

        updateResponse.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);

        var getResponse = await client.GetAsync(new Uri($"/api/v1/profiles/machine/{id}", UriKind.Relative));
        var detail = await getResponse.Content.ReadFromJsonAsync<MachineProfileDetailResponse>();
        AssertJsonEquivalent(detail!.RawDataJson, originalJson);
    }

    [Fact]
    public async Task UpdateFilamentProfileRawData_ValidJson_IsReflectedOnNextGet()
    {
        var client = await AuthenticatedClientAsync();
        var suffix = Guid.NewGuid().ToString("N");
        var name = $"Edit PLA {suffix}";
        var bundle = ProfileBundleTestHelper.BuildZip(
            ($"filament/{name}.json", $$"""{"name":"{{name}}","filament_type":["PLA"]}"""));
        var selections = JsonSerializer.Serialize(new object[] { new { type = "Filament", name, duplicateAction = (string?)null } });
        using var content = ProfileBundleTestHelper.BuildUploadContent(bundle, "bundle.zip", selections);
        var confirmResponse = await client.PostAsync(new Uri("/api/v1/profiles/imports/confirm", UriKind.Relative), content);
        var created = await confirmResponse.Content.ReadFromJsonAsync<ConfirmProfileImportResponse>();
        var id = created!.CreatedFilamentProfiles.Single().Id;

        var updatedJson = $$"""{"name":"{{name}}","filament_type":["PETG"]}""";
        var updateResponse = await client.PutAsJsonAsync(
            new Uri($"/api/v1/profiles/filament/{id}/raw-data", UriKind.Relative),
            new { RawDataJson = updatedJson });

        updateResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        var updated = await updateResponse.Content.ReadFromJsonAsync<FilamentProfileDetailResponse>();
        updated!.Material.Should().Be("PETG");

        var getResponse = await client.GetAsync(new Uri($"/api/v1/profiles/filament/{id}", UriKind.Relative));
        var detail = await getResponse.Content.ReadFromJsonAsync<FilamentProfileDetailResponse>();
        AssertJsonEquivalent(detail!.RawDataJson, updatedJson);
        detail.Material.Should().Be("PETG");
    }

    [Fact]
    public async Task UpdateFilamentProfileRawData_MalformedJson_Returns422AndLeavesPreviousValueUnchanged()
    {
        var client = await AuthenticatedClientAsync();
        var suffix = Guid.NewGuid().ToString("N");
        var name = $"Reject PLA {suffix}";
        var originalJson = $$"""{"name":"{{name}}","filament_type":["PLA"]}""";
        var bundle = ProfileBundleTestHelper.BuildZip(($"filament/{name}.json", originalJson));
        var selections = JsonSerializer.Serialize(new object[] { new { type = "Filament", name, duplicateAction = (string?)null } });
        using var content = ProfileBundleTestHelper.BuildUploadContent(bundle, "bundle.zip", selections);
        var confirmResponse = await client.PostAsync(new Uri("/api/v1/profiles/imports/confirm", UriKind.Relative), content);
        var created = await confirmResponse.Content.ReadFromJsonAsync<ConfirmProfileImportResponse>();
        var id = created!.CreatedFilamentProfiles.Single().Id;

        var updateResponse = await client.PutAsJsonAsync(
            new Uri($"/api/v1/profiles/filament/{id}/raw-data", UriKind.Relative),
            new { RawDataJson = "not json at all" });

        updateResponse.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);

        var getResponse = await client.GetAsync(new Uri($"/api/v1/profiles/filament/{id}", UriKind.Relative));
        var detail = await getResponse.Content.ReadFromJsonAsync<FilamentProfileDetailResponse>();
        AssertJsonEquivalent(detail!.RawDataJson, originalJson);
    }

    [Fact]
    public async Task UpdateBaseProfileRawData_ChangesDescendantsDerivedFieldsOnNextRead()
    {
        var client = await AuthenticatedClientAsync();
        var suffix = Guid.NewGuid().ToString("N");
        var baseName = $"Chain Base PLA {suffix}";
        var overrideName = $"Chain Base PLA {suffix} - Red";
        var bundle = ProfileBundleTestHelper.BuildZip(
            ($"filament/{baseName}.json", $$"""{"name":"{{baseName}}","filament_type":["PLA"],"nozzle_temperature":["200"]}"""),
            ($"filament/{overrideName}.json", $$"""{"name":"{{overrideName}}","inherits":"{{baseName}}"}"""));
        var selections = JsonSerializer.Serialize(new object[]
        {
            new { type = "Filament", name = baseName, duplicateAction = (string?)null },
            new { type = "Filament", name = overrideName, duplicateAction = (string?)null },
        });
        using var content = ProfileBundleTestHelper.BuildUploadContent(bundle, "bundle.zip", selections);
        var confirmResponse = await client.PostAsync(new Uri("/api/v1/profiles/imports/confirm", UriKind.Relative), content);
        var created = await confirmResponse.Content.ReadFromJsonAsync<ConfirmProfileImportResponse>();
        var baseId = created!.CreatedFilamentProfiles.Single(p => p.Name == baseName).Id;
        var overrideId = created.CreatedFilamentProfiles.Single(p => p.Name == overrideName).Id;

        var beforeResponse = await client.GetAsync(new Uri($"/api/v1/profiles/filament/{overrideId}", UriKind.Relative));
        var before = await beforeResponse.Content.ReadFromJsonAsync<FilamentProfileDetailResponse>();
        before!.NozzleTempC.Should().Be(200);

        var updatedBaseJson = $$"""{"name":"{{baseName}}","filament_type":["PLA"],"nozzle_temperature":["215"]}""";
        var updateResponse = await client.PutAsJsonAsync(
            new Uri($"/api/v1/profiles/filament/{baseId}/raw-data", UriKind.Relative),
            new { RawDataJson = updatedBaseJson });
        updateResponse.StatusCode.Should().Be(HttpStatusCode.OK);

        var afterResponse = await client.GetAsync(new Uri($"/api/v1/profiles/filament/{overrideId}", UriKind.Relative));
        var after = await afterResponse.Content.ReadFromJsonAsync<FilamentProfileDetailResponse>();
        after!.NozzleTempC.Should().Be(215);
    }
}
