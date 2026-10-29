using System.Net;
using FluentAssertions;

namespace OpenFarm.Api.IntegrationTests.ConnectionSecrets;

public class ConnectionSecretsEndpointTests(CustomWebApplicationFactory factory) : IClassFixture<CustomWebApplicationFactory>
{
    [Theory]
    [InlineData("GET")]
    [InlineData("POST")]
    public async Task StandaloneCredentialEndpoint_IsRemoved(string method)
    {
        using var client = factory.CreateClient();
        using var request = new HttpRequestMessage(new HttpMethod(method), "/api/v1/connection-secrets");
        var response = await client.SendAsync(request);
        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }
}
