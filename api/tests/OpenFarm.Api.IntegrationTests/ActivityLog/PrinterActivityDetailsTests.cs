using System.Net.Http.Json;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.EntityFrameworkCore;
using OpenFarm.Api.Endpoints.ActivityLog;
using OpenFarm.Api.Endpoints.Printers;
using OpenFarm.Domain.ActivityLog;
using OpenFarm.Domain.Printers;
using OpenFarm.Infrastructure.Persistence;
using static OpenFarm.Api.IntegrationTests.FilamentTracking.FilamentTrackingTestHelpers;

namespace OpenFarm.Api.IntegrationTests.ActivityLog;

public class PrinterActivityDetailsTests(CustomWebApplicationFactory factory) : IClassFixture<CustomWebApplicationFactory>
{
    [Fact]
    public async Task FiltersBeforePagination_AndRoundTripsStateAndAlerts()
    {
        var client = await AuthenticatedClientAsync(factory.CreateClient());
        Guid printerId;
        var warning = new PrinterAlert("Hms", "0300_0100_0002_0001", "Warning", "Needs attention");
        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<OpenFarmDbContext>();
            var printer = Printer.Create("Reported status", "Bambu", "P1S", "10.0.0.1", $"ACT-{Guid.NewGuid():N}");
            printerId = printer.Id;
            printer.ObserveStatus("PAUSE");
            printer.ObserveReportedState("PAUSE", DateTimeOffset.UtcNow);
            printer.ObserveAlerts([warning], null);
            db.Printers.Add(printer);
            db.ActivityLogEntries.Add(ActivityLogEntry.Create("Printer", printer.Id.ToString(), "PrinterStatusObserved", null, ActorType.System,
                new ActivityLogDetails("RUNNING", "PAUSE", DateTimeOffset.UtcNow)));
            db.ActivityLogEntries.Add(ActivityLogEntry.Create("Printer", printer.Id.ToString(), "PrinterAlertsChanged", null, ActorType.System,
                new ActivityLogDetails(PreviousAlerts: [], ActiveAlerts: [warning])));
            for (var i = 0; i < 25; i++)
                db.ActivityLogEntries.Add(ActivityLogEntry.Create("Printer", Guid.NewGuid().ToString(), "PrinterStatusObserved", null, ActorType.System));
            await db.SaveChangesAsync();
        }
        var page = await client.GetFromJsonAsync<ActivityLogPageResponse>($"/api/v1/activity-log?entityType=Printer&entityId={printerId}&page=1&pageSize=1");
        page!.TotalCount.Should().Be(2);
        page.Items.Should().ContainSingle().Which.Details!.ActiveAlerts.Should().Equal(warning);
        var second = await client.GetFromJsonAsync<ActivityLogPageResponse>($"/api/v1/activity-log?entityType=Printer&entityId={printerId}&page=2&pageSize=1");
        second!.Items.Should().ContainSingle().Which.Details!.ReportedState.Should().Be("PAUSE");
        var detail = await client.GetFromJsonAsync<PrinterDetailResponse>($"/api/v1/printers/{printerId}");
        detail!.Status.Should().Be("Printing");
        detail.Telemetry.ReportedState.Should().Be("PAUSE");
        detail.Telemetry.StateObservedAtUtc.Should().NotBeNull();
        detail.Telemetry.ActiveAlerts.Should().Equal(warning);
    }
}
