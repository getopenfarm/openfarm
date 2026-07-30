using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using FluentAssertions;
using OpenFarm.Api.Endpoints.ActivityLog;
using OpenFarm.Api.Endpoints.Auth;
using OpenFarm.Api.Endpoints.ConnectionSecrets;
using OpenFarm.Application.Auth;

namespace OpenFarm.Api.IntegrationTests.ActivityLog;

public class ActivityLogEndpointTests(CustomWebApplicationFactory factory) : IClassFixture<CustomWebApplicationFactory>
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
    public async Task Create_ThenGetActivityLog_ReturnsRetrievableEntryOrderedNewestFirst()
    {
        var client = await AuthenticatedClientAsync();
        var serialNumber = $"AC-{Guid.NewGuid():N}";

        var createResponse = await client.PostAsJsonAsync(
            new Uri("/api/v1/connection-secrets", UriKind.Relative),
            new CreateConnectionSecretRequest { PrinterSerialNumber = serialNumber, PlainAccessCode = "real-secret-value" });
        var created = await createResponse.Content.ReadFromJsonAsync<ConnectionSecretResponse>();

        var response = await client.GetAsync(new Uri("/api/v1/activity-log?page=1&pageSize=25", UriKind.Relative));

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var page = await response.Content.ReadFromJsonAsync<ActivityLogPageResponse>();
        page.Should().NotBeNull();

        var entry = page!.Items.Should().ContainSingle(e => e.EntityId == created!.Id.ToString()).Subject;
        entry.EntityType.Should().Be("ConnectionSecret");
        entry.EventType.Should().Be("Created");
        entry.ActorType.Should().Be("Operator");
        entry.ActorDisplayName.Should().Be("admin");
        entry.TimestampUtc.Should().BeOnOrAfter(DateTimeOffset.UtcNow.AddMinutes(-1));

        page.Items.Should().BeInDescendingOrder(e => e.TimestampUtc);
    }

    [Fact]
    public async Task GetActivityLog_PaginationParameters_AreRespected()
    {
        var client = await AuthenticatedClientAsync();

        for (var i = 0; i < 3; i++)
        {
            await client.PostAsJsonAsync(
                new Uri("/api/v1/connection-secrets", UriKind.Relative),
                new CreateConnectionSecretRequest { PrinterSerialNumber = $"AC-{Guid.NewGuid():N}", PlainAccessCode = "real-secret-value" });
        }

        var response = await client.GetAsync(new Uri("/api/v1/activity-log?page=1&pageSize=2", UriKind.Relative));
        var page = await response.Content.ReadFromJsonAsync<ActivityLogPageResponse>();

        page.Should().NotBeNull();
        page!.Items.Should().HaveCount(2);
        page.Page.Should().Be(1);
        page.PageSize.Should().Be(2);
        page.TotalCount.Should().BeGreaterThanOrEqualTo(3);
    }

    [Fact]
    public async Task Create_WithFailedValidation_WritesNoActivityLogEntry()
    {
        var client = await AuthenticatedClientAsync();

        var beforeResponse = await client.GetAsync(new Uri("/api/v1/activity-log?page=1&pageSize=1", UriKind.Relative));
        var before = await beforeResponse.Content.ReadFromJsonAsync<ActivityLogPageResponse>();

        var createResponse = await client.PostAsJsonAsync(
            new Uri("/api/v1/connection-secrets", UriKind.Relative),
            new CreateConnectionSecretRequest { PrinterSerialNumber = "AC12345", PlainAccessCode = string.Empty });
        createResponse.StatusCode.Should().Be(HttpStatusCode.BadRequest);

        var afterResponse = await client.GetAsync(new Uri("/api/v1/activity-log?page=1&pageSize=1", UriKind.Relative));
        var after = await afterResponse.Content.ReadFromJsonAsync<ActivityLogPageResponse>();

        after!.TotalCount.Should().Be(before!.TotalCount);
    }
}
