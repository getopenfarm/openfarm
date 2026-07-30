using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using OpenFarm.Api.Endpoints.Auth;
using OpenFarm.Api.Endpoints.ConnectionSecrets;
using OpenFarm.Application.Auth;
using OpenFarm.Domain.Organizations;
using OpenFarm.Infrastructure.Persistence;

namespace OpenFarm.Api.IntegrationTests.ConnectionSecrets;

public class ConnectionSecretsEndpointTests(CustomWebApplicationFactory factory) : IClassFixture<CustomWebApplicationFactory>
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
    public async Task Create_WithValidBody_ReturnsMaskedResponseAndStoresCiphertext()
    {
        var client = await AuthenticatedClientAsync();
        var serialNumber = $"AC-{Guid.NewGuid():N}";

        var response = await client.PostAsJsonAsync(
            new Uri("/api/v1/connection-secrets", UriKind.Relative),
            new CreateConnectionSecretRequest { PrinterSerialNumber = serialNumber, PlainAccessCode = "real-secret-value" });

        response.StatusCode.Should().Be(HttpStatusCode.Created);

        var body = await response.Content.ReadFromJsonAsync<ConnectionSecretResponse>();
        body.Should().NotBeNull();
        body!.PrinterSerialNumber.Should().Be(serialNumber);
        body.MaskedAccessCode.Should().Be("***");

        using var scope = factory.Services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<OpenFarmDbContext>();
        var stored = await dbContext.ConnectionSecrets
            .IgnoreQueryFilters()
            .AsNoTracking()
            .SingleAsync(s => s.Id == body.Id);

        stored.EncryptedAccessCode.Should().NotBe("real-secret-value");
        stored.OrganizationId.Should().Be(Organization.WellKnownId);
    }

    [Fact]
    public async Task List_ReturnsMaskedEntries()
    {
        var client = await AuthenticatedClientAsync();
        var serialNumber = $"AC-{Guid.NewGuid():N}";
        await client.PostAsJsonAsync(
            new Uri("/api/v1/connection-secrets", UriKind.Relative),
            new CreateConnectionSecretRequest { PrinterSerialNumber = serialNumber, PlainAccessCode = "real-secret-value" });

        var response = await client.GetAsync(new Uri("/api/v1/connection-secrets", UriKind.Relative));

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadFromJsonAsync<List<ConnectionSecretResponse>>();
        body.Should().NotBeNull();
        body!.Should().Contain(s => s.PrinterSerialNumber == serialNumber && s.MaskedAccessCode == "***");
    }

    [Fact]
    public async Task Create_WithEmptyAccessCode_Returns400AndWritesNoActivityLogEntry()
    {
        var client = await AuthenticatedClientAsync();

        using var scope = factory.Services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<OpenFarmDbContext>();
        var countBefore = await dbContext.ActivityLogEntries.IgnoreQueryFilters().CountAsync();

        var response = await client.PostAsJsonAsync(
            new Uri("/api/v1/connection-secrets", UriKind.Relative),
            new CreateConnectionSecretRequest { PrinterSerialNumber = "AC12345", PlainAccessCode = string.Empty });

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);

        var countAfter = await dbContext.ActivityLogEntries.IgnoreQueryFilters().CountAsync();
        countAfter.Should().Be(countBefore);
    }

    [Theory]
    [InlineData("PUT")]
    [InlineData("PATCH")]
    [InlineData("DELETE")]
    public async Task MutatingVerbsOtherThanCreate_AreNotAllowed(string method)
    {
        var client = await AuthenticatedClientAsync();

        using var request = new HttpRequestMessage(new HttpMethod(method), new Uri("/api/v1/connection-secrets", UriKind.Relative));
        var response = await client.SendAsync(request);

        response.StatusCode.Should().BeOneOf(HttpStatusCode.NotFound, HttpStatusCode.MethodNotAllowed);
    }
}
