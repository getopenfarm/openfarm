using Microsoft.AspNetCore.Mvc.Testing;
using Testcontainers.PostgreSql;

namespace OpenFarm.Api.IntegrationTests;

public sealed class CustomWebApplicationFactory : WebApplicationFactory<Program>, IAsyncLifetime
{
    private readonly PostgreSqlContainer _postgres = new PostgreSqlBuilder("postgres:18")
        .WithDatabase("openfarm")
        .WithUsername("openfarm")
        .WithPassword("changeme")
        .Build();

    private readonly string _keysDirectory = Path.Combine(Path.GetTempPath(), "openfarm-test-keys", Guid.NewGuid().ToString());

    public async Task InitializeAsync()
    {
        await _postgres.StartAsync();

        // Set before the first CreateClient()/Services access so WebApplication.CreateBuilder(args)
        // picks these up while building configuration for the top-level Program.cs.
        Environment.SetEnvironmentVariable("ConnectionStrings__Default", _postgres.GetConnectionString());
        Environment.SetEnvironmentVariable("Jwt__Issuer", "OpenFarm.Tests");
        Environment.SetEnvironmentVariable("Jwt__Audience", "OpenFarm.Tests");
        Environment.SetEnvironmentVariable("Jwt__SigningKey", "integration-test-signing-key-0123456789ABCDEF");
        Environment.SetEnvironmentVariable("Seed__AdminEmail", "admin@openfarm.local");
        Environment.SetEnvironmentVariable("Seed__AdminPassword", "ChangeMe123!");
        Environment.SetEnvironmentVariable("DataProtection__KeysDirectory", _keysDirectory);
    }

    public new async Task DisposeAsync()
    {
        await _postgres.StopAsync();
        await base.DisposeAsync();
    }
}
