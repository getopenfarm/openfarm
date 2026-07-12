using System.Net;
using FluentAssertions;

namespace OpenFarm.Api.IntegrationTests;

public class PingEndpointTests(CustomWebApplicationFactory factory) : IClassFixture<CustomWebApplicationFactory>
{
    private readonly HttpClient _client = factory.CreateClient();

    [Fact]
    public async Task Ping_ReturnsOk()
    {
        var response = await _client.GetAsync(new Uri("/api/v1/ping", UriKind.Relative));

        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }
}
