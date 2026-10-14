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
using OpenFarm.Domain.Organizations;
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
        using (var scope = Factory.CreateTenantScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<OpenFarmDbContext>();
            foreach (var diameter in new[] { 0.4m, 0.4m, 0.6m })
            {
                var printer = Printer.Create(Organization.WellKnownId, "P1S", "Bambu", "P1S", "127.0.0.1", Guid.NewGuid().ToString());
                printer.ObserveNozzle(diameter, DateTimeOffset.UtcNow);
                printer.MarkUnreachable();
                db.Printers.Add(printer);
            }
            var different = Printer.Create(Organization.WellKnownId, "X1C", "Bambu", "X1C", "127.0.0.1", Guid.NewGuid().ToString());
            different.ObserveNozzle(0.6m, DateTimeOffset.UtcNow);
            db.Printers.Add(different);
            db.Printers.Add(Printer.Create(Organization.WellKnownId, "Unknown nozzle", "Bambu", "A1", "127.0.0.1", Guid.NewGuid().ToString()));
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
        using (var scope = Factory.CreateTenantScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<OpenFarmDbContext>();
            var first = FilamentProfile.Create(Organization.WellKnownId, "PLA 0.4", ProfileSourceFormat.OrcaSlicer, null,
                """{"filament_type":["PLA"],"nozzle_diameter":["0.4"]}""");
            var second = FilamentProfile.Create(Organization.WellKnownId, "Black PLA 0.4", ProfileSourceFormat.OrcaSlicer, null,
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
            ProfileAssignments = [new("Bambu", "P1S", 0.4m, firstId), new("Bambu", "P1S", 0.4m, secondId, "#000000")],
        });
        saved.StatusCode.Should().Be(HttpStatusCode.OK, await saved.Content.ReadAsStringAsync());
        Guid originalId;
        using (var scope = Factory.CreateTenantScope())
        {
            var row = await scope.ServiceProvider.GetRequiredService<IFilamentRepository>().GetByIdAsync(filament.Id, default);
            originalId = row!.ProfileAssignments.Single(a => a.FilamentVariantId is null).Id;
        }
        saved = await client.PutAsJsonAsync(editRoute, new EditFilamentRequest { ProfileAssignments = [new("Bambu", "P1S", 0.4m, secondId)] });
        saved.StatusCode.Should().Be(HttpStatusCode.OK, await saved.Content.ReadAsStringAsync());
        using (var scope = Factory.CreateTenantScope())
        {
            var row = await scope.ServiceProvider.GetRequiredService<IFilamentRepository>().GetByIdAsync(filament.Id, default);
            row!.ProfileAssignments.Should().ContainSingle().Which.Id.Should().Be(originalId);
            row.ResolveProfileId("Bambu", "P1S", 0.4m, filament.Variants[0].Id).Should().Be(secondId);
        }
        var rejected = await client.PutAsJsonAsync(editRoute, new EditFilamentRequest
        { ProfileAssignments = [new("Bambu", "P1S", 0.4m, firstId), new("Bambu", "P1S", 0.6m, secondId)] });
        rejected.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
        var retained = (await client.GetFromJsonAsync<FilamentProfileAssignmentsResponse>(route))!;
        retained.Assignments.Should().ContainSingle().Which.FilamentProfileId.Should().Be(secondId);
        retained.Issues.Should().BeEmpty();
        using (var scope = Factory.CreateTenantScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<OpenFarmDbContext>();
            (await db.FilamentProfiles.SingleAsync(p => p.Id == secondId)).MarkStale();
            await db.SaveChangesAsync();
        }
        (await client.GetFromJsonAsync<FilamentProfileAssignmentsResponse>(route))!.Issues.Should().ContainSingle();
    }

    [Fact]
    public async Task BambuOriginAssignmentIsAcceptedForOrcaButFailedEngineValidationBlocksTheQueue()
    {
        var client = await ClientAsync();
        var printer = await PrinterAsync();
        var stored = await PartAsync(fileName: "profile-validation.stl");
        Part part;
        Guid filamentId, profileId;
        using (var scope = Factory.CreateTenantScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<OpenFarmDbContext>();
            var trackedPrinter = await db.Printers.SingleAsync(p => p.Id == printer.Id);
            var process = ProcessProfile.CreateSystemProfile(Organization.WellKnownId, Guid.NewGuid().ToString(),
                ProfileSourceFormat.OrcaSlicer, null, "{}", "test", Guid.NewGuid().ToString(), true,
                [(await db.MachineProfiles.SingleAsync(p => p.Id == trackedPrinter.MachineProfileId)).Name]);
            db.ProcessProfiles.Add(process);
            trackedPrinter.LinkProcessProfile(process.Id);
            trackedPrinter.SelectBuildPlate(BuildPlateType.HighTemp);
            var profile = FilamentProfile.Create(Organization.WellKnownId, "Imported Bambu PLA", ProfileSourceFormat.BambuStudio, null,
                """{"filament_type":["PLA"],"hot_plate_temp":["55"]}""");
            db.FilamentProfiles.Add(profile);
            // Identify the filament on this exact printer's loaded tracked spool.
            var slots = await db.FeedUnits.Where(u => u.PrinterId == printer.Id).SelectMany(u => u.FeedSlots).Select(s => s.Id).ToListAsync();
            var variantId = (await db.Spools.SingleAsync(s => s.FeedSlotId != null && slots.Contains(s.FeedSlotId.Value))).FilamentVariantId;
            filamentId = await db.Filaments.Where(f => f.Variants.Any(v => v.Id == variantId)).Select(f => f.Id).SingleAsync();
            profileId = profile.Id;
            part = Part.Create(Organization.WellKnownId, Guid.NewGuid().ToString(), PartFileFormat.Stl, true,
                stored.OriginalFileName, stored.StoragePath, stored.FileSizeBytes, 0.4m, null, null,
                EstimateStatus.Available, 1, 1, 1, 1, stored.RevisionHash,
                [new FilamentRequirementSpec("PLA", null, null, null, null)], [], []);
            db.Parts.Add(part);
            await db.SaveChangesAsync();
        }
        var save = await client.PutAsJsonAsync($"/api/v1/filaments/{filamentId}",
            new EditFilamentRequest { ProfileAssignments = [new("System", "Simulation", 0.4m, profileId)] });
        save.StatusCode.Should().Be(HttpStatusCode.OK, await save.Content.ReadAsStringAsync());
        using (var scope = Factory.CreateTenantScope())
        {
            var issue = await scope.ServiceProvider.GetRequiredService<ProductionProfileReadiness>()
                .GetIssueAsync(printer.Id, part, [("PLA", null)], default);
            issue.Should().Contain("Profile compatibility validation failed with OrcaSlicer 2.2.0")
                .And.Contain($"/filament/{filamentId}/edit");
        }
        var run = await RunAsync(client, part.Id, pinned: printer.Id);
        var jobs = await client.GetFromJsonAsync<OpenFarm.Api.Endpoints.PrintJobs.ListQueueJobsResponse>("/api/v1/print-jobs?view=queue&pageSize=1000");
        jobs!.Items.Should().Contain(j => j.ProductionRunId == run.Id && j.IsBlocked
            && j.BlockingReason!.Contains("Profile compatibility validation failed"));
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
        using (var scope = Factory.CreateTenantScope())
        {
            var content = await scope.ServiceProvider.GetRequiredService<IJobFileResolver>().ResolveAsync(part, job, printer.Id, 0.4m, default);
            content.Bytes.Should().NotBeEmpty();
            var db = scope.ServiceProvider.GetRequiredService<OpenFarmDbContext>();
            var filament = await db.Filaments.Include(f => f.ProfileAssignments).SingleAsync(f => f.ProfileAssignments.Any(a => a.Brand == "System"));
            filament.ReplaceProfileAssignments([]);
            await db.SaveChangesAsync();
        }
        using (var scope = Factory.CreateTenantScope())
        {
            var act = () => scope.ServiceProvider.GetRequiredService<IJobFileResolver>().ResolveAsync(part, job, printer.Id, 0.4m, default);
            await act.Should().ThrowAsync<SlicingProfileUnconfiguredException>().WithMessage("*Assign a profile*");
        }
        (await client.GetAsync($"/api/v1/print-jobs/{job.Id}/file")).StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
        var queue = await client.GetFromJsonAsync<OpenFarm.Api.Endpoints.PrintJobs.ListQueueJobsResponse>("/api/v1/print-jobs");
        queue!.Items.Should().Contain(j => j.JobId == job.Id && j.IsBlocked && j.BlockingReason!.Contains("/filament/", StringComparison.Ordinal));
        using (var scope = Factory.CreateTenantScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<OpenFarmDbContext>();
            (await db.Printers.SingleAsync(p => p.Id == printer.Id)).BeginConnection();
            await db.SaveChangesAsync();
        }
        using (var scope = Factory.CreateTenantScope())
        {
            var act = () => scope.ServiceProvider.GetRequiredService<IJobFileResolver>().ResolveAsync(part, job, printer.Id, 0.4m, default);
            await act.Should().ThrowAsync<SlicingProfileUnconfiguredException>().WithMessage("*current telemetry*");
        }
    }
}
