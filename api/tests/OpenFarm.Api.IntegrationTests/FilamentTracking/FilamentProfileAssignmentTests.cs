using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using OpenFarm.Api.Endpoints.Filaments;
using OpenFarm.Api.Endpoints.Vendors;
using OpenFarm.Api.IntegrationTests.Production;
using OpenFarm.Application.FilamentTracking;
using OpenFarm.Application.Parts;
using OpenFarm.Application.Production;
using OpenFarm.Domain.Printers;
using OpenFarm.Domain.Profiles;
using OpenFarm.Domain.Parts;
using OpenFarm.Infrastructure.Persistence;

namespace OpenFarm.Api.IntegrationTests.FilamentTracking;

public class FilamentProfileAssignmentTests(ProductionTestFactory factory) : ProductionTestContext(factory), IClassFixture<ProductionTestFactory>
{
    [Fact]
    public async Task FleetGroupsReportedHardwareAndRetainsOfflineNozzlesWithoutUsingProfiles()
    {
        var client = await ClientAsync();
        using (var scope = Factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<OpenFarmDbContext>();
            foreach (var diameter in new[] { 0.4m, 0.4m, 0.6m })
            {
                var printer = Printer.Create("P1S", "Bambu", "P1S", "127.0.0.1", Guid.NewGuid().ToString());
                printer.ObserveNozzle(diameter, DateTimeOffset.UtcNow);
                printer.MarkUnreachable();
                db.Printers.Add(printer);
            }
            var different = Printer.Create("X1C", "Bambu", "X1C", "127.0.0.1", Guid.NewGuid().ToString());
            different.ObserveNozzle(0.6m, DateTimeOffset.UtcNow);
            db.Printers.Add(different);
            db.Printers.Add(Printer.Create("Unknown nozzle", "Bambu", "A1", "127.0.0.1", Guid.NewGuid().ToString()));
            await db.SaveChangesAsync();
        }
        var fleet = await client.GetFromJsonAsync<FilamentAssignmentFleet>("/api/v1/filaments/profile-assignment-fleet");
        fleet!.Variants.Should().Contain(v => v.ModelCode == "P1S" && v.NozzleDiameterMm == 0.4m && v.PrinterCount == 2);
        fleet.Variants.Should().Contain(v => v.ModelCode == "P1S" && v.NozzleDiameterMm == 0.6m && v.PrinterCount == 1);
        fleet.Variants.Should().Contain(v => v.ModelCode == "X1C" && v.NozzleDiameterMm == 0.6m && v.PrinterCount == 1);
        fleet.PrintersAwaitingNozzle.Should().Contain("Unknown nozzle");
    }

    [Fact]
    public async Task IncompleteInventorySaves_AssignmentsReplaceAtomicallyAndRetainIdentity()
    {
        var client = await ClientAsync();
        Guid firstId, secondId;
        using (var scope = Factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<OpenFarmDbContext>();
            var first = FilamentProfile.Create("PLA 0.4", ProfileSourceFormat.OrcaSlicer, null,
                """{"filament_type":["PLA"],"nozzle_diameter":["0.4"]}""");
            var second = FilamentProfile.Create("Black PLA 0.4", ProfileSourceFormat.OrcaSlicer, null,
                """{"filament_type":["PLA"],"nozzle_diameter":["0.4"]}""");
            db.FilamentProfiles.AddRange(first, second);
            await db.SaveChangesAsync();
            firstId = first.Id; secondId = second.Id;
        }
        var vendorResponse = await client.PostAsJsonAsync("/api/v1/vendors", new CreateVendorRequest { Name = Guid.NewGuid().ToString() });
        var vendor = (await vendorResponse.Content.ReadFromJsonAsync<VendorResponse>())!;
        var created = await client.PostAsJsonAsync("/api/v1/filaments", new CreateFilamentRequest
        {
            VendorId = vendor.Id,
            Material = "PLA Basic",
            MaterialType = "PLA",
            Variants = [new() { ColorName = "Black", ColorHex = "#000000" }],
        });
        created.StatusCode.Should().Be(HttpStatusCode.Created);
        var filament = (await created.Content.ReadFromJsonAsync<FilamentResponse>())!;
        filament.ProfileAssignments.Should().BeEmpty();
        var route = $"/api/v1/filaments/{filament.Id}/profile-assignments";
        var editRoute = $"/api/v1/filaments/{filament.Id}";
        var saved = await client.PutAsJsonAsync(editRoute, new EditFilamentRequest
        {
            VendorId = filament.VendorId,
            Material = filament.Material,
            MaterialType = filament.MaterialType,
            ProfileAssignments = [new("Bambu", "P1S", 0.4m, firstId), new("Bambu", "P1S", 0.4m, secondId, "#000000")],
        });
        saved.StatusCode.Should().Be(HttpStatusCode.OK, await saved.Content.ReadAsStringAsync());
        Guid originalId;
        using (var scope = Factory.Services.CreateScope())
        {
            var row = await scope.ServiceProvider.GetRequiredService<IFilamentRepository>().GetByIdAsync(filament.Id, default);
            originalId = row!.ProfileAssignments.Single(a => a.FilamentVariantId is null).Id;
        }
        saved = await client.PutAsJsonAsync(editRoute, new EditFilamentRequest { VendorId = filament.VendorId, Material = filament.Material, MaterialType = filament.MaterialType, ProfileAssignments = [new("Bambu", "P1S", 0.4m, secondId)] });
        saved.StatusCode.Should().Be(HttpStatusCode.OK, await saved.Content.ReadAsStringAsync());
        using (var scope = Factory.Services.CreateScope())
        {
            var row = await scope.ServiceProvider.GetRequiredService<IFilamentRepository>().GetByIdAsync(filament.Id, default);
            row!.ProfileAssignments.Should().ContainSingle().Which.Id.Should().Be(originalId);
            row.ResolveProfileId("Bambu", "P1S", 0.4m, filament.Variants[0].Id).Should().Be(secondId);
        }
        var rejected = await client.PutAsJsonAsync(editRoute, new EditFilamentRequest
        { VendorId = filament.VendorId, Material = filament.Material, MaterialType = filament.MaterialType, ProfileAssignments = [new("Bambu", "P1S", 0.4m, firstId), new("Bambu", "P1S", 0.6m, secondId)] });
        rejected.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
        var retained = (await client.GetFromJsonAsync<FilamentProfileAssignmentsResponse>(route))!;
        retained.Assignments.Should().ContainSingle().Which.FilamentProfileId.Should().Be(secondId);
        retained.Issues.Should().BeEmpty();
        using (var scope = Factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<OpenFarmDbContext>();
            (await db.FilamentProfiles.SingleAsync(p => p.Id == secondId)).MarkStale();
            await db.SaveChangesAsync();
        }
        (await client.GetFromJsonAsync<FilamentProfileAssignmentsResponse>(route))!.Issues.Should().ContainSingle();
    }

    // Production only ever slices with the pinned slicer's own presets, so a preset of the other engine is not an
    // assignment the farm could use: it is refused when saved, with a stable code, and nothing is stored.
    [Fact]
    public async Task AssignmentOfAnotherEnginesPresetIsRejectedAndNothingIsSaved()
    {
        var client = await ClientAsync();
        Guid profileId;
        using (var scope = Factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<OpenFarmDbContext>();
            var imported = FilamentProfile.Create("Imported Bambu PLA", ProfileSourceFormat.BambuStudio, null,
                """{"filament_type":["PLA"],"nozzle_diameter":["0.4"]}""");
            db.FilamentProfiles.Add(imported);
            await db.SaveChangesAsync();
            profileId = imported.Id;
        }
        var vendor = (await (await client.PostAsJsonAsync("/api/v1/vendors", new CreateVendorRequest { Name = Guid.NewGuid().ToString() }))
            .Content.ReadFromJsonAsync<VendorResponse>())!;
        var created = await client.PostAsJsonAsync("/api/v1/filaments", new CreateFilamentRequest
        {
            VendorId = vendor.Id,
            Material = "PLA Basic",
            MaterialType = "PLA",
            Variants = [new() { ColorName = "Black", ColorHex = "#000000" }],
        });
        var filament = (await created.Content.ReadFromJsonAsync<FilamentResponse>())!;

        var save = await client.PutAsJsonAsync($"/api/v1/filaments/{filament.Id}", new EditFilamentRequest
        {
            VendorId = filament.VendorId,
            Material = filament.Material,
            MaterialType = filament.MaterialType,
            ProfileAssignments = [new("Bambu", "P1S", 0.4m, profileId)],
        });

        save.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
        var problem = await save.Content.ReadFromJsonAsync<System.Text.Json.JsonElement>();
        problem.GetProperty("code").GetString().Should().Be("UnsupportedOrigin");
        problem.GetProperty("detail").GetString().Should().Contain("its own profiles only");
        (await client.GetFromJsonAsync<FilamentProfileAssignmentsResponse>($"/api/v1/filaments/{filament.Id}/profile-assignments"))!
            .Assignments.Should().BeEmpty();
    }

    [Fact]
    public async Task PreslicedProductionRequiresTrackedAssignmentsAndFreshNozzleConfirmation()
    {
        var client = await ClientAsync();
        var part = await PartAsync();
        var printer = await PrinterAsync();
        var run = await RunAsync(client, part.Id, pinned: printer.Id);
        var job = (await JobsAsync(run.Id)).Single();
        (await client.GetAsync($"/api/v1/print-jobs/{job.Id}/file")).StatusCode.Should().Be(HttpStatusCode.OK);
        using (var scope = Factory.Services.CreateScope())
        {
            var content = await scope.ServiceProvider.GetRequiredService<IJobFileResolver>().ResolveAsync(part, job, printer.Id, 0.4m, default);
            content.Bytes.Should().NotBeEmpty();
            var db = scope.ServiceProvider.GetRequiredService<OpenFarmDbContext>();
            var filament = await db.Filaments.Include(f => f.ProfileAssignments).SingleAsync(f => f.ProfileAssignments.Any(a => a.Brand == "System"));
            filament.ReplaceProfileAssignments([]);
            await db.SaveChangesAsync();
        }
        using (var scope = Factory.Services.CreateScope())
        {
            var act = () => scope.ServiceProvider.GetRequiredService<IJobFileResolver>().ResolveAsync(part, job, printer.Id, 0.4m, default);
            await act.Should().ThrowAsync<SlicingProfileUnconfiguredException>().WithMessage("*Assign a profile*");
        }
        (await client.GetAsync($"/api/v1/print-jobs/{job.Id}/file")).StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
        var queue = await client.GetFromJsonAsync<OpenFarm.Api.Endpoints.PrintJobs.ListQueueJobsResponse>("/api/v1/print-jobs");
        queue!.Items.Should().Contain(j => j.JobId == job.Id && j.IsBlocked && j.BlockingReason!.Contains("/filament/", StringComparison.Ordinal));
        using (var scope = Factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<OpenFarmDbContext>();
            (await db.Printers.SingleAsync(p => p.Id == printer.Id)).BeginConnection();
            await db.SaveChangesAsync();
        }
        using (var scope = Factory.Services.CreateScope())
        {
            var act = () => scope.ServiceProvider.GetRequiredService<IJobFileResolver>().ResolveAsync(part, job, printer.Id, 0.4m, default);
            await act.Should().ThrowAsync<SlicingProfileUnconfiguredException>().WithMessage("*current telemetry*");
        }
    }
}
