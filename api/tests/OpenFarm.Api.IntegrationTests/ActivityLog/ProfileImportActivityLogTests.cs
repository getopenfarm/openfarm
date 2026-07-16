using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using OpenFarm.Api.Endpoints.ActivityLog;
using OpenFarm.Api.Endpoints.Auth;
using OpenFarm.Api.Endpoints.Profiles;
using OpenFarm.Api.IntegrationTests.Profiles;
using OpenFarm.Application.Auth;

namespace OpenFarm.Api.IntegrationTests.ActivityLog;

// research.md §7 — the IHasEntityIds extension to ActivityLogPostProcessor: a multi-preset confirm
// call writes one ActivityLogEntry per created profile, not one entry for the whole batch.
public class ProfileImportActivityLogTests(CustomWebApplicationFactory factory) : IClassFixture<CustomWebApplicationFactory>
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

    [Fact]
    public async Task Confirm_WithMultiplePresets_WritesOneActivityLogEntryPerCreatedProfile()
    {
        var client = await AuthenticatedClientAsync();
        var suffix = Guid.NewGuid().ToString("N");
        var zipBytes = ProfileBundleTestHelper.SampleBundle(suffix);

        var selections = JsonSerializer.Serialize(new object[]
        {
            new { type = "Machine", name = $"Test Printer {suffix}", duplicateAction = (string?)null },
            new { type = "Filament", name = $"Test PLA {suffix}", duplicateAction = (string?)null },
            new { type = "Filament", name = $"Test PETG {suffix}", duplicateAction = (string?)null },
        });

        using var content = ProfileBundleTestHelper.BuildUploadContent(zipBytes, "bundle.zip", selections);
        var confirmResponse = await client.PostAsync(new Uri("/api/v1/profiles/imports/confirm", UriKind.Relative), content);
        confirmResponse.StatusCode.Should().Be(HttpStatusCode.Created);
        var created = await confirmResponse.Content.ReadFromJsonAsync<ConfirmProfileImportResponse>();

        var allCreatedIds = created!.CreatedMachineProfiles.Select(p => p.Id.ToString())
            .Concat(created.CreatedFilamentProfiles.Select(p => p.Id.ToString()))
            .ToList();
        allCreatedIds.Should().HaveCount(3);

        var logResponse = await client.GetAsync(new Uri("/api/v1/activity-log?page=1&pageSize=50", UriKind.Relative));
        var page = await logResponse.Content.ReadFromJsonAsync<ActivityLogPageResponse>();

        foreach (var id in allCreatedIds)
        {
            page!.Items.Should().ContainSingle(e => e.EntityId == id && e.EventType == "ProfileImported" && e.EntityType == "Profile");
        }
    }
}
