using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using OpenFarm.Infrastructure.Printers.Simulator;
using Testcontainers.PostgreSql;

namespace OpenFarm.Api.IntegrationTests;

public sealed class CustomWebApplicationFactory : WebApplicationFactory<Program>, IAsyncLifetime
{
    // Production defaults to a ~45s job cycle (research.md §9) — far too slow for a test suite to
    // wait out. Overriding the singleton here (the same optional-constructor-parameter pattern
    // PrinterConnectionBackgroundServiceTests already uses) is the one DI seam every integration test
    // needing SimulatorPrinterConnector's job lifecycle shares.
    private static readonly TimeSpan TestCycleDuration = TimeSpan.FromMilliseconds(200);

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.ConfigureServices(services =>
            services.AddSingleton(new SimulatorPrinterConnector(TestCycleDuration)));
    }

    private readonly PostgreSqlContainer _postgres = new PostgreSqlBuilder("postgres:18")
        .WithDatabase("openfarm")
        .WithUsername("openfarm")
        .WithPassword("changeme")
        .Build();

    private readonly string _keysDirectory = Path.Combine(Path.GetTempPath(), "openfarm-test-keys", Guid.NewGuid().ToString());
    private readonly string _slicerBundleDirectory = Path.Combine(Path.GetTempPath(), "openfarm-test-slicers", Guid.NewGuid().ToString());

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

        // A default, non-empty bundle so a requires-slicing upload always gets a job created
        // (CreatePartEndpointTests' own long-standing carve-out: no CI runner has a real slicer CLI
        // installed, so slicing itself still fails at slice time, exactly as it always has — this
        // only keeps the catalog from being empty, which would now reject the upload before a job is
        // ever created, FR-006). SlicingAvailabilityTests overrides this per-test to exercise the
        // genuinely-empty-catalog path.
        SeedDummyBundle();
        Environment.SetEnvironmentVariable("SlicerBundle__RootDirectory", _slicerBundleDirectory);
    }

    private void SeedDummyBundle()
    {
        // Two engines so US2 tests (default vs. explicit engine+version override) have a real
        // second choice to override to, alongside the auto-defaulted OrcaSlicer newest.
        CreateDummyLeaf("orcaslicer", "2.2.0");
        CreateDummyLeaf("superslicer", "2.5.59.13");
    }

    private void CreateDummyLeaf(string engineSlug, string version)
    {
        var leaf = Path.Combine(_slicerBundleDirectory, engineSlug, version, "linux");
        Directory.CreateDirectory(leaf);
        File.WriteAllText(Path.Combine(leaf, "AppRun"), "#!/bin/sh\nexit 1\n");
    }

    public new async Task DisposeAsync()
    {
        await _postgres.StopAsync();
        await base.DisposeAsync();

        try
        {
            Directory.Delete(_slicerBundleDirectory, recursive: true);
        }
        catch (IOException)
        {
        }
    }
}
