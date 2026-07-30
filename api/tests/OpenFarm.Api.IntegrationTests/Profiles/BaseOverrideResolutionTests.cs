using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using OpenFarm.Api.Endpoints.Auth;
using OpenFarm.Api.Endpoints.Profiles;
using OpenFarm.Application.Auth;

namespace OpenFarm.Api.IntegrationTests.Profiles;

// FR-005, SC-002 — base/override relationships are preserved as a real parent/child link, not
// flattened into an independent copy; an unresolved (or ambiguous) base reference is a normal
// outcome, not an error (US2 Acceptance Scenarios 1 and 3).
public class BaseOverrideResolutionTests(CustomWebApplicationFactory factory) : IClassFixture<CustomWebApplicationFactory>
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

    private static byte[] BaseAndOverrideBundle(string baseName, string overrideName) =>
        ProfileBundleTestHelper.BuildZip(
            ($"filament/{baseName}.json", $$"""{"name":"{{baseName}}","filament_type":["PLA"]}"""),
            ($"filament/{overrideName}.json",
                $$"""{"name":"{{overrideName}}","inherits":"{{baseName}}","default_filament_colour":["#C12E1FFF"]}"""));

    [Fact]
    public async Task Confirm_SelectingBaseAndOverride_LinksOverrideBaseProfileIdToBaseId()
    {
        var client = await AuthenticatedClientAsync();
        var suffix = Guid.NewGuid().ToString("N");
        var baseName = $"Base PLA {suffix}";
        var overrideName = $"Base PLA {suffix} - Red";
        var bundle = BaseAndOverrideBundle(baseName, overrideName);

        var selections = JsonSerializer.Serialize(new object[]
        {
            new { type = "Filament", name = baseName, duplicateAction = (string?)null },
            new { type = "Filament", name = overrideName, duplicateAction = (string?)null },
        });

        using var content = ProfileBundleTestHelper.BuildUploadContent(bundle, "bundle.zip", selections);
        var response = await client.PostAsync(new Uri("/api/v1/profiles/imports/confirm", UriKind.Relative), content);
        response.StatusCode.Should().Be(HttpStatusCode.Created);

        var filamentList = await client.GetFromJsonAsync<ListFilamentProfilesResponse>(new Uri("/api/v1/profiles/filament", UriKind.Relative));
        var basePresetId = filamentList!.Items.Single(p => p.Name == baseName).Id;
        var overridePreset = filamentList.Items.Single(p => p.Name == overrideName);

        overridePreset.BaseProfileId.Should().Be(basePresetId);
        overridePreset.BaseProfileName.Should().Be(baseName);
    }

    [Fact]
    public async Task Confirm_OverrideAloneWithoutItsBase_KeepsBaseProfileNamePopulatedWithNullId()
    {
        var client = await AuthenticatedClientAsync();
        var suffix = Guid.NewGuid().ToString("N");
        var baseName = $"Stock Vendor PLA {suffix}";
        var overrideName = $"Stock Vendor PLA {suffix} - Blue";
        var bundle = BaseAndOverrideBundle(baseName, overrideName);

        // Only the override is selected — its declared base is a built-in default that was never
        // imported (US2 Acceptance Scenario 3, the common real-world case).
        var selections = JsonSerializer.Serialize(new object[]
        {
            new { type = "Filament", name = overrideName, duplicateAction = (string?)null },
        });

        using var content = ProfileBundleTestHelper.BuildUploadContent(bundle, "bundle.zip", selections);
        var response = await client.PostAsync(new Uri("/api/v1/profiles/imports/confirm", UriKind.Relative), content);
        response.StatusCode.Should().Be(HttpStatusCode.Created);

        var filamentList = await client.GetFromJsonAsync<ListFilamentProfilesResponse>(new Uri("/api/v1/profiles/filament", UriKind.Relative));
        var overridePreset = filamentList!.Items.Single(p => p.Name == overrideName);

        overridePreset.BaseProfileId.Should().BeNull();
        overridePreset.BaseProfileName.Should().Be(baseName);
    }

    [Fact]
    public async Task Confirm_DuplicateNamedBaseAlreadyInCatalog_ProducesSameUnresolvedShapeAsNoMatch()
    {
        var client = await AuthenticatedClientAsync();
        var suffix = Guid.NewGuid().ToString("N");
        var baseName = $"Generic PLA {suffix}";

        // Import the same base name twice as separate rows (FR-009's duplicate-as-new path), so the
        // catalog has two rows sharing (SourceFormat, Name) before the override ever gets confirmed.
        var firstBaseBundle = ProfileBundleTestHelper.BuildZip(
            ($"filament/{baseName}.json", $$"""{"name":"{{baseName}}","filament_type":["PLA"]}"""));
        var secondBaseBundle = ProfileBundleTestHelper.BuildZip(
            ($"filament/{baseName}.json", $$"""{"name":"{{baseName}}","filament_type":["PLA"]}"""));

        foreach (var bundle in new[] { firstBaseBundle, secondBaseBundle })
        {
            var selections = JsonSerializer.Serialize(new object[]
            {
                new { type = "Filament", name = baseName, duplicateAction = "ImportAsNew" },
            });
            using var content = ProfileBundleTestHelper.BuildUploadContent(bundle, "bundle.zip", selections);
            var response = await client.PostAsync(new Uri("/api/v1/profiles/imports/confirm", UriKind.Relative), content);
            response.StatusCode.Should().Be(HttpStatusCode.Created);
        }

        var beforeList = await client.GetFromJsonAsync<ListFilamentProfilesResponse>(new Uri("/api/v1/profiles/filament", UriKind.Relative));
        beforeList!.Items.Count(p => p.Name == baseName).Should().Be(2);

        var overrideName = $"Generic PLA {suffix} - Third";
        var thirdBundle = ProfileBundleTestHelper.BuildZip(
            ($"filament/{overrideName}.json", $$"""{"name":"{{overrideName}}","inherits":"{{baseName}}"}"""));
        var thirdSelections = JsonSerializer.Serialize(new object[]
        {
            new { type = "Filament", name = overrideName, duplicateAction = (string?)null },
        });
        using var thirdContent = ProfileBundleTestHelper.BuildUploadContent(thirdBundle, "bundle.zip", thirdSelections);
        var thirdResponse = await client.PostAsync(new Uri("/api/v1/profiles/imports/confirm", UriKind.Relative), thirdContent);
        thirdResponse.StatusCode.Should().Be(HttpStatusCode.Created);

        var afterList = await client.GetFromJsonAsync<ListFilamentProfilesResponse>(new Uri("/api/v1/profiles/filament", UriKind.Relative));
        var overridePreset = afterList!.Items.Single(p => p.Name == overrideName);

        // Ambiguous (2 same-named candidates) collapses to the identical shape as no match at all —
        // resolving to one arbitrarily would be a silent wrong guess (research.md §3).
        overridePreset.BaseProfileId.Should().BeNull();
        overridePreset.BaseProfileName.Should().Be(baseName);
    }
}
