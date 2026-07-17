using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using OpenFarm.Api.Endpoints.Auth;
using OpenFarm.Api.Endpoints.Profiles;
using OpenFarm.Application.Auth;

namespace OpenFarm.Api.IntegrationTests.Profiles;

// FR-009 — a preset duplicating an already-imported profile (same name and type) is flagged at
// preview; the operator's Skip vs ImportAsNew choice at confirm produces the correct row count.
public class DuplicateProfileImportTests(CustomWebApplicationFactory factory) : IClassFixture<CustomWebApplicationFactory>
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

    private static byte[] SingleFilamentBundle(string name) => ProfileBundleTestHelper.BuildZip(
        ($"filament/{name}.json", $$"""{"name":"{{name}}","filament_type":["PLA"]}"""));

    [Fact]
    public async Task Preview_OfAlreadyImportedName_FlagsIsDuplicate()
    {
        var client = await AuthenticatedClientAsync();
        var name = $"Dup PLA {Guid.NewGuid():N}";
        var firstBundle = SingleFilamentBundle(name);

        using (var firstConfirmContent = ProfileBundleTestHelper.BuildUploadContent(
            firstBundle, "bundle.zip",
            JsonSerializer.Serialize(new object[] { new { type = "Filament", name, duplicateAction = (string?)null } })))
        {
            var firstConfirm = await client.PostAsync(new Uri("/api/v1/profiles/imports/confirm", UriKind.Relative), firstConfirmContent);
            firstConfirm.StatusCode.Should().Be(HttpStatusCode.Created);
        }

        var secondBundle = SingleFilamentBundle(name);
        using var previewContent = ProfileBundleTestHelper.BuildUploadContent(secondBundle, "bundle2.zip");
        var previewResponse = await client.PostAsync(new Uri("/api/v1/profiles/imports/preview", UriKind.Relative), previewContent);

        var body = await previewResponse.Content.ReadFromJsonAsync<PreviewProfileImportResponse>();
        body!.FilamentPresets.Should().ContainSingle(p => p.Name == name && p.IsDuplicate);
    }

    [Fact]
    public async Task Confirm_DuplicateWithSkip_DoesNotCreateSecondRow()
    {
        var client = await AuthenticatedClientAsync();
        var name = $"Dup PLA {Guid.NewGuid():N}";

        await ConfirmSingleFilamentAsync(client, name, duplicateAction: null);
        await ConfirmSingleFilamentAsync(client, name, duplicateAction: "Skip");

        var filamentList = await client.GetFromJsonAsync<ListFilamentProfilesResponse>(new Uri("/api/v1/profiles/filament", UriKind.Relative));
        filamentList!.Profiles.Count(p => p.Name == name).Should().Be(1);
    }

    [Fact]
    public async Task Confirm_DuplicateWithImportAsNew_CreatesSecondSeparateRow()
    {
        var client = await AuthenticatedClientAsync();
        var name = $"Dup PLA {Guid.NewGuid():N}";

        await ConfirmSingleFilamentAsync(client, name, duplicateAction: null);
        await ConfirmSingleFilamentAsync(client, name, duplicateAction: "ImportAsNew");

        var filamentList = await client.GetFromJsonAsync<ListFilamentProfilesResponse>(new Uri("/api/v1/profiles/filament", UriKind.Relative));
        filamentList!.Profiles.Count(p => p.Name == name).Should().Be(2);
    }

    private static async Task ConfirmSingleFilamentAsync(HttpClient client, string name, string? duplicateAction)
    {
        var bundle = SingleFilamentBundle(name);
        var selections = JsonSerializer.Serialize(new object[] { new { type = "Filament", name, duplicateAction } });
        using var content = ProfileBundleTestHelper.BuildUploadContent(bundle, "bundle.zip", selections);
        var response = await client.PostAsync(new Uri("/api/v1/profiles/imports/confirm", UriKind.Relative), content);
        response.StatusCode.Should().Be(HttpStatusCode.Created);
    }
}
