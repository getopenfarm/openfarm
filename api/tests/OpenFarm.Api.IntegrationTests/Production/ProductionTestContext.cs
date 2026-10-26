using System.Runtime.CompilerServices;
using OpenFarm.Domain.FilamentTracking;
using System.Net;
using System.Diagnostics;
using Microsoft.Extensions.Logging;
using OpenFarm.Infrastructure.Production;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using FluentAssertions;
using Microsoft.AspNetCore.Hosting;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using OpenFarm.Api.Endpoints.Auth;
using OpenFarm.Api.Endpoints.PrintJobs;
using OpenFarm.Api.Endpoints.ProductionRuns;
using OpenFarm.Application.Auth;
using OpenFarm.Application.Common;
using OpenFarm.Application.ConnectionSecrets;
using OpenFarm.Application.Parts;
using OpenFarm.Application.Production;
using OpenFarm.Domain.ConnectionSecrets;
using OpenFarm.Domain.Parts;
using OpenFarm.Domain.Printers;
using OpenFarm.Domain.Profiles;
using OpenFarm.Domain.Production;
using OpenFarm.Infrastructure.Persistence;

namespace OpenFarm.Api.IntegrationTests.Production;

// Real HTTP, repositories and PostgreSQL. Tests explicitly start the real dispatcher or
// replay telemetry so connection workers cannot race assertions about intermediate states.
public class ProductionTestFactory : CustomWebApplicationFactory
{
    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        base.ConfigureWebHost(builder);
        builder.ConfigureServices(services => services.RemoveAll<IHostedService>());
    }
}

public abstract class ProductionTestContext(CustomWebApplicationFactory factory)
{
    protected CustomWebApplicationFactory Factory { get; } = factory;

    // One login per factory, not per test: xUnit builds a new context for every test, and a class with more tests than the
    // login rate limit allows per minute would otherwise get 429.
    private static readonly ConditionalWeakTable<CustomWebApplicationFactory, string> Tokens = new();

    protected async Task<HttpClient> ClientAsync()
    {
        var client = Factory.CreateClient();
        if (!Tokens.TryGetValue(Factory, out var token))
        {
            var login = await client.PostAsJsonAsync("/api/v1/auth/login", new LoginCommand("admin", "admin-openfarm"));
            login.EnsureSuccessStatusCode();
            token = (await login.Content.ReadFromJsonAsync<LoginResponse>())!.Token;
            Tokens.AddOrUpdate(Factory, token);
        }
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return client;
    }

    /// <param name="file">The stored file's bytes, instead of <paramref name="contents"/> (a sliced 3MF with a <c>.3mf</c> <paramref name="fileName"/>).</param>
    protected async Task<Part> PartAsync(int units = 1, string contents = "; revision one\n", string fileName = "part.gcode", bool legacy = false,
        decimal? grams = 20, FilamentRequirementSpec? requirement = null, byte[]? file = null)
    {
        using var scope = Factory.Services.CreateScope();
        var storage = scope.ServiceProvider.GetRequiredService<IPartFileStorage>();
        var bytes = file ?? Encoding.UTF8.GetBytes(contents);
        var hash = Convert.ToHexStringLower(SHA256.HashData(bytes));
        using var stream = new MemoryStream(bytes);
        var path = await storage.SaveAsync(legacy ? Guid.NewGuid().ToString("N") : hash, fileName, stream, default);
        if (legacy) hash = new string('a', 32);
        var format = fileName.EndsWith(".3mf", StringComparison.OrdinalIgnoreCase) ? PartFileFormat.ThreeMf : PartFileFormat.GCode;
        var part = Part.Create($"Part {Guid.NewGuid():N}", format,
            false, fileName, path, bytes.Length, 0.4m, null, null, EstimateStatus.Available,
            60, grams, plateNumber: null, units, hash, [requirement ?? new FilamentRequirementSpec("PLA", null, null, null, null)], [], []);
        await scope.ServiceProvider.GetRequiredService<IPartRepository>().AddAsync(part, default);
        return part;
    }

    protected static async Task<ProductionRunDetailResponse> RunAsync(HttpClient client, Guid partId, int? quantity = 1,
        string? name = null, string? revisionMode = null, Guid? pinned = null)
    {
        var response = await client.PostAsJsonAsync("/api/v1/production-runs", new
        {
            name,
            lines = new[] { new { targetKind = "Part", targetId = partId, quantity,
                revisionMode, assignmentMode = pinned is null ? "Unassigned" : "Pinned", pinnedPrinterId = pinned } },
        });
        response.StatusCode.Should().Be(HttpStatusCode.Created, await response.Content.ReadAsStringAsync());
        return (await response.Content.ReadFromJsonAsync<ProductionRunDetailResponse>())!;
    }

    protected static async Task<ProductionRunDetailResponse> DetailAsync(HttpClient client, Guid runId) =>
        (await client.GetFromJsonAsync<ProductionRunDetailResponse>($"/api/v1/production-runs/{runId}"))!;

    protected async Task<List<PrintJob>> JobsAsync(Guid runId)
    {
        using var scope = Factory.Services.CreateScope();
        return (await scope.ServiceProvider.GetRequiredService<IPrintJobRepository>().ListByProductionRunIdAsync(runId, default)).ToList();
    }

    /// <param name="machineProfile">Existing presets to point the printer (and its filament assignment) at; the default is a fresh imported machine and filament profile.</param>
    protected async Task<Printer> PrinterAsync(bool manual = false, string state = "IDLE",
        MachineProfile? machineProfile = null, ProcessProfile? processProfile = null, FilamentProfile? existingFilamentProfile = null,
        string brand = "System", string modelCode = "Simulation")
    {
        using var scope = Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<OpenFarmDbContext>();
        // A vendor's machine preset names its model; the simulator's names none.
        var vendorModel = PrinterModelCatalog.Resolve(brand, modelCode).VendorMachineModelName;
        var profile = machineProfile ?? MachineProfile.Create($"Machine {Guid.NewGuid():N}", ProfileSourceFormat.OrcaSlicer, null,
            vendorModel is null ? "{\"nozzle_diameter\":[\"0.4\"]}" : $$"""{"nozzle_diameter":["0.4"],"printer_model":"{{vendorModel}}"}""");
        if (machineProfile is null) db.MachineProfiles.Add(profile);
        var printer = Printer.Create($"Printer {Guid.NewGuid():N}", brand, modelCode, "127.0.0.1", $"SN{Guid.NewGuid():N}");
        printer.LinkMachineProfile(profile.Id);
        if (processProfile is not null)
        {
            printer.LinkProcessProfile(processProfile.Id);
            printer.SelectBuildPlate(BuildPlateType.HighTemp);
        }

        printer.ObserveNozzle(0.4m, DateTimeOffset.UtcNow);
        var filamentProfile = existingFilamentProfile ?? FilamentProfile.Create($"PLA {Guid.NewGuid():N}", ProfileSourceFormat.OrcaSlicer, null,
            """{"filament_type":["PLA"],"hot_plate_temp":["55"]}""");
        if (existingFilamentProfile is null) db.FilamentProfiles.Add(filamentProfile);
        var vendor = Vendor.Create($"Vendor {Guid.NewGuid():N}");
        db.Vendors.Add(vendor);
        var filament = Filament.Create(vendor.Id, "PLA", "PLA");
        filament.ReplaceProfileAssignments([FilamentProfileAssignment.Create(filament.Id, brand, modelCode, 0.4m, filamentProfile.Id)]);
        var variant = filament.AddVariant("White", "FFFFFF");
        db.Filaments.Add(filament);
        var feedUnit = FeedUnit.CreateConnectorDerived(printer.Id, FeedUnitKindCatalog.ExternalSpool, 0);
        var slot = feedUnit.AddFeedSlot(0);
        db.FeedUnits.Add(feedUnit);
        var spool = Spool.Create(variant.Id, null, 1000, 1000);
        spool.LoadOntoFeedSlot(slot.Id);
        db.Spools.Add(spool);
        printer.MarkReachable(DateTimeOffset.UtcNow);
        printer.RecordTelemetry(null, null, null, null, null, null,
            [new FeedUnitReading(FeedUnitKindCatalog.ExternalSpool, 0, [new FeedSlotReading(0, false, "PLA", "FFFFFF", null, null, false)])]);
        printer.ObserveStatus(state);
        db.Printers.Add(printer);
        // No manual brand is currently registrable. Persist a future/manual brand through EF's
        // mapped field to test the already-defined non-live branch without adding a product brand.
        if (manual) db.Entry(printer).Property(p => p.Brand).CurrentValue = "Manual";
        var protector = scope.ServiceProvider.GetRequiredService<ISecretProtector>();
        db.ConnectionSecrets.Add(ConnectionSecret.Create(printer.SerialNumber, protector.Protect("test-access-code")));
        await db.SaveChangesAsync();
        return printer;
    }

    /// <summary>
    /// Gives a toolchanger its heads, slot n feeding extruder n, with each head's nozzle as reported, and moves the printer's
    /// one loaded spool onto <paramref name="head"/>.
    /// </summary>
    protected async Task<FeedSlot[]> AttachHeadsAsync(Guid printerId, decimal[] nozzlesMm, int head)
    {
        using var scope = Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<OpenFarmDbContext>();
        var heads = FeedUnit.CreateConnectorDerived(printerId, FeedUnitKindCatalog.MultiMaterialUnit, 0);
        var slots = Enumerable.Range(0, nozzlesMm.Length).Select(index => heads.AddFeedSlot(index, extruderIndex: index)).ToArray();
        db.FeedUnits.Add(heads);
        var spool = await db.Spools.SingleAsync(s => s.State == SpoolState.Using && db.FeedUnits.Any(u => u.PrinterId == printerId && u.FeedSlots.Any(f => f.Id == s.FeedSlotId)));
        spool.ReturnToStorage();
        spool.LoadOntoFeedSlot(slots[head].Id);
        (await db.Printers.SingleAsync(p => p.Id == printerId)).ObserveNozzles(nozzlesMm.Select(n => (decimal?)n).ToList(), DateTimeOffset.UtcNow);
        await db.SaveChangesAsync();
        return slots;
    }

    protected async Task AssignAsync(Guid jobId, Guid printerId, bool started = true)
    {
        using var scope = Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<OpenFarmDbContext>();
        var job = await db.PrintJobs.SingleAsync(j => j.Id == jobId);
        job.Dispatch(printerId);
        if (started)
        {
            job.MarkStarted();
            (await db.Printers.SingleAsync(p => p.Id == printerId)).ObserveStatus("RUNNING");
        }
        await db.SaveChangesAsync();
    }

    protected async Task<PrinterStatus> PrinterStatusAsync(Guid printerId)
    {
        using var scope = Factory.Services.CreateScope();
        return (await scope.ServiceProvider.GetRequiredService<OpenFarmDbContext>().Printers.SingleAsync(p => p.Id == printerId)).Status;
    }

    protected async Task ReplayAsync(Guid printerId, string state, DateTimeOffset? unseenSinceUtc = null)
    {
        using var scope = Factory.Services.CreateScope();
        await scope.ServiceProvider.GetRequiredService<IJobTelemetrySync>().ObserveAsync(printerId, state, unseenSinceUtc, default);
    }

    protected ProductionDispatchBackgroundService Worker() => new(Factory.Services.GetRequiredService<IServiceScopeFactory>(),
        Factory.Services.GetRequiredService<ILogger<ProductionDispatchBackgroundService>>());

    protected async Task<PrintJob> WaitForAssignmentAsync(Guid runId, Guid? excludeJobId = null)
    {
        var timer = Stopwatch.StartNew();
        while (timer.Elapsed < TimeSpan.FromSeconds(10))
        {
            var job = (await JobsAsync(runId)).FirstOrDefault(j => j.Id != excludeJobId && j.PrinterId != null);
            if (job is not null) return job;
            await Task.Delay(50);
        }
        throw new TimeoutException("The dispatcher did not assign the expected job.");
    }

    protected static async Task<List<string>> ActivityEventTypesAsync(HttpClient client, Guid entityId) =>
        (await client.GetFromJsonAsync<OpenFarm.Api.Endpoints.ActivityLog.ActivityLogPageResponse>(
            $"/api/v1/activity-log?entityId={entityId}&pageSize=100"))!.Items.Select(i => i.EventType).ToList();

    protected static async Task PostAsync(HttpClient client, string route, HttpStatusCode expected = HttpStatusCode.OK, object? body = null)
    {
        var response = await client.PostAsJsonAsync(route, body ?? new { });
        response.StatusCode.Should().Be(expected, await response.Content.ReadAsStringAsync());
    }
}
