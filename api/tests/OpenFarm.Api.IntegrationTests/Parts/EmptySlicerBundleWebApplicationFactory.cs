using Microsoft.AspNetCore.Mvc.Testing;
using Testcontainers.PostgreSql;

namespace OpenFarm.Api.IntegrationTests.Parts;

/// <summary>
/// A dedicated factory whose bundled-slicer directory exists but is genuinely empty — exercises the
/// "no slicer available at all" path (FR-006/FR-011/SC-002), which <see cref="CustomWebApplicationFactory"/>
/// deliberately seeds a dummy entry to avoid (so the rest of the suite still gets a job created).
/// </summary>
public sealed class EmptySlicerBundleWebApplicationFactory : WebApplicationFactory<Program>, IAsyncLifetime
{
    private readonly PostgreSqlContainer _postgres = new PostgreSqlBuilder("postgres:18")
        .WithDatabase("openfarm")
        .WithUsername("openfarm")
        .WithPassword("changeme")
        .Build();

    private readonly string _keysDirectory = Path.Combine(Path.GetTempPath(), "openfarm-test-keys", Guid.NewGuid().ToString());
    private readonly string _emptySlicerBundleDirectory = Path.Combine(Path.GetTempPath(), "openfarm-test-slicers-empty", Guid.NewGuid().ToString());

    public async Task InitializeAsync()
    {
        await _postgres.StartAsync();

        Directory.CreateDirectory(_emptySlicerBundleDirectory);

        Environment.SetEnvironmentVariable("ConnectionStrings__Default", _postgres.GetConnectionString());
        Environment.SetEnvironmentVariable("Jwt__Issuer", "OpenFarm.Tests");
        Environment.SetEnvironmentVariable("Jwt__Audience", "OpenFarm.Tests");
        Environment.SetEnvironmentVariable("Jwt__SigningKey", "integration-test-signing-key-0123456789ABCDEF");
        Environment.SetEnvironmentVariable("Seed__AdminUsername", "admin");
        Environment.SetEnvironmentVariable("Seed__AdminEmail", "admin@openfarm.local");
        Environment.SetEnvironmentVariable("Seed__AdminPassword", "admin-openfarm");
        Environment.SetEnvironmentVariable("DataProtection__KeysDirectory", _keysDirectory);
        Environment.SetEnvironmentVariable("SlicerBundle__RootDirectory", _emptySlicerBundleDirectory);
    }

    public new async Task DisposeAsync()
    {
        await _postgres.StopAsync();
        await base.DisposeAsync();

        try
        {
            Directory.Delete(_emptySlicerBundleDirectory, recursive: true);
        }
        catch (IOException)
        {
        }
    }
}
