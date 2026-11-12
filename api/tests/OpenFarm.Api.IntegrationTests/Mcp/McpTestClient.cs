using System.Net.Http.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using ModelContextProtocol.Client;
using OpenFarm.Api.Endpoints.Auth;
using OpenFarm.Application.Auth;
using OpenFarm.Infrastructure.Auth;

namespace OpenFarm.Api.IntegrationTests.Mcp;

public static class McpTestClient
{
    public static async Task<HttpClient> BrowserAsync(WebApplicationFactory<Program> factory)
    {
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-Forwarded-For", $"10.9.{Random.Shared.Next(255)}.{Random.Shared.Next(255)}");
        var response = await client.PostAsJsonAsync("/api/v1/auth/login", new LoginCommand("admin", "admin-openfarm"));
        response.EnsureSuccessStatusCode();
        client.DefaultRequestHeaders.Authorization = new("Bearer", (await response.Content.ReadFromJsonAsync<LoginResponse>())!.Token);
        return client;
    }

    public static async Task<AccessTokenCreated> TokenAsync(HttpClient browser, params string[] permissions)
    {
        var response = await browser.PostAsJsonAsync("/api/v1/auth/tokens", new AccessTokenInput(Guid.NewGuid().ToString(), permissions, null));
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<AccessTokenCreated>())!;
    }

    /// <summary>The official MCP client over the in-memory test server.</summary>
    public static async Task<McpClient> ConnectAsync(CustomWebApplicationFactory factory, string bearer)
    {
        var http = factory.CreateClient();
        http.DefaultRequestHeaders.Authorization = new("Bearer", bearer);
        var transport = new HttpClientTransport(new HttpClientTransportOptions
        {
            Endpoint = new Uri(http.BaseAddress!, "/mcp"),
            TransportMode = HttpTransportMode.StreamableHttp,
        }, http, ownsHttpClient: true);
        return await McpClient.CreateAsync(transport);
    }
}
