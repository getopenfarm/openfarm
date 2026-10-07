using Finbuckle.MultiTenant.Abstractions;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using OpenFarm.Infrastructure.Multitenancy;
using OpenFarm.Application.Parts;
using OpenFarm.Domain.Parts;
using OpenFarm.Infrastructure.Printers.Simulator;
using Testcontainers.PostgreSql;

namespace OpenFarm.Api.IntegrationTests;

public class CustomWebApplicationFactory : WebApplicationFactory<Program>, IAsyncLifetime
{
    // Production defaults to a ~45s job cycle (research.md §9) — far too slow for a test suite to
    // wait out. Overriding the singleton here (the same optional-constructor-parameter pattern
    // PrinterConnectionBackgroundServiceTests already uses) is the one DI seam every integration test
    // needing SimulatorPrinterConnector's job lifecycle shares.
    private static readonly TimeSpan TestCycleDuration = TimeSpan.FromMilliseconds(200);

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.ConfigureServices(services =>
        {
            services.AddSingleton(new SimulatorPrinterConnector(TestCycleDuration));
            // Fixtures deliberately seed Linux AppRun shell scripts, which also execute on macOS.
            // Keep their catalog selection independent of the machine running the test suite.
            services.AddSingleton<IHostPlatformProvider, FixtureHostPlatformProvider>();
            services.PostConfigure<OpenFarm.Infrastructure.Parts.PartFileStorageOptions>(options =>
                options.RootDirectory = _partFilesDirectory);
        });
    }

    private sealed class FixtureHostPlatformProvider : IHostPlatformProvider
    {
        public SlicerPlatform Current => SlicerPlatform.Linux;
    }

    private readonly PostgreSqlContainer _postgres = new PostgreSqlBuilder("postgres:18")
        .WithDatabase("openfarm")
        .WithUsername("openfarm")
        .WithPassword("changeme")
        .Build();

    private readonly string _partFilesDirectory = Path.Combine(Path.GetTempPath(), "openfarm-test-part-files", Guid.NewGuid().ToString());

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
        Environment.SetEnvironmentVariable("Seed__AdminUsername", "admin");
        Environment.SetEnvironmentVariable("Seed__AdminEmail", "admin@openfarm.local");
        Environment.SetEnvironmentVariable("Seed__AdminPassword", "admin-openfarm");
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
        var appRunPath = Path.Combine(leaf, "AppRun");
        File.WriteAllText(appRunPath, "#!/bin/sh\nexit 1\n");

        // File.WriteAllText never sets the execute bit (mode 644) — ProcessStartInfo invokes this
        // path directly (no shell wrapper, see CliSlicerEngine), so without this the process fails to
        // start with "Permission denied" instead of actually running and exiting 1, which is what
        // tests asserting on a real CLI-exit-code error message expect.
        if (!OperatingSystem.IsWindows())
        {
            File.SetUnixFileMode(appRunPath,
                UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute
                | UnixFileMode.GroupRead | UnixFileMode.GroupExecute
                | UnixFileMode.OtherRead | UnixFileMode.OtherExecute);
        }
    }

    private AppTenantInfo? _cachedTenantInfo;

    /// <summary>
    /// Resolves and caches the single organization's tenant info, once per factory instance. Call
    /// this (awaited) before <see cref="CreateTenantScope"/> — split into two members rather than one
    /// combined async method because of a confirmed, reproduced Finbuckle/AsyncLocal footgun: a
    /// <c>IMultiTenantContextSetter.MultiTenantContext</c> assignment made *inside* an async method
    /// that itself awaits something, then returns its scope to an awaiting caller, does not reliably
    /// propagate — the caller ends up with a scope whose ambient tenant context is still null even
    /// though the assignment executed without error. Verified via a standalone repro against a real
    /// Postgres: the combined shape failed 3/3 runs; this split shape (await the resolver, then call
    /// the synchronous scope-creator directly, with no intervening awaited wrapper) succeeded 3/3.
    /// Same underlying bug as <c>PrinterConnectionBackgroundService.CreateTenantScope</c>'s own
    /// documented caveat and 009's <c>ingest-system-profiles</c> CLI entry point.
    /// </summary>
    public async Task EnsureTenantInfoResolvedAsync()
    {
        if (_cachedTenantInfo is not null)
        {
            return;
        }

        using var scope = Services.CreateScope();
        var tenantStore = scope.ServiceProvider.GetRequiredService<IMultiTenantStore<AppTenantInfo>>();
        _cachedTenantInfo = await tenantStore.GetByIdentifierAsync(OrganizationTenantStore.StaticIdentifier)
            ?? throw new InvalidOperationException("No organization found — FirstBootSeeder must run before this test.");
    }

    /// <summary>
    /// A DI scope off <see cref="WebApplicationFactory{TEntryPoint}.Services"/> with the single
    /// organization's tenant context pre-populated. Finbuckle's tenant resolution is HTTP-middleware-
    /// based (<c>UseMultiTenant()</c>), which never runs for a scope a test creates directly this way
    /// — without this, every tenant-scoped EF query/save throws (a <see cref="NullReferenceException"/>
    /// on read, a <c>MultiTenantException</c> on write) against a null <c>TenantInfo</c>. Deliberately
    /// synchronous — see <see cref="EnsureTenantInfoResolvedAsync"/>'s doc comment for why. Callers
    /// must <c>await EnsureTenantInfoResolvedAsync()</c> first, then call this directly (not through
    /// another awaited helper) instead of a bare <c>Services.CreateScope()</c> whenever a test talks
    /// to a tenant-scoped repository/DbContext without going through
    /// <see cref="WebApplicationFactory{TEntryPoint}.CreateClient"/>'s real HTTP pipeline.
    /// </summary>
    public IServiceScope CreateTenantScope()
    {
        var scope = Services.CreateScope();
        var contextSetter = scope.ServiceProvider.GetRequiredService<IMultiTenantContextSetter>();
        contextSetter.MultiTenantContext = new MultiTenantContext<AppTenantInfo>(_cachedTenantInfo!);
        return scope;
    }

    public new async Task DisposeAsync()
    {
        await _postgres.StopAsync();
        await base.DisposeAsync();

        try
        {
            Directory.Delete(_slicerBundleDirectory, recursive: true);
            if (Directory.Exists(_partFilesDirectory)) Directory.Delete(_partFilesDirectory, recursive: true);
        }
        catch (IOException)
        {
        }
    }
}
