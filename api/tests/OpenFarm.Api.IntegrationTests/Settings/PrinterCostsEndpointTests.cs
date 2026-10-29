using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using OpenFarm.Api.Endpoints.Auth;
using OpenFarm.Api.Endpoints.Assemblies;
using OpenFarm.Api.Endpoints.Settings;
using OpenFarm.Application.Auth;
using OpenFarm.Application.Parts;
using OpenFarm.Application.Printers;
using OpenFarm.Domain.Parts;
using OpenFarm.Domain.Printers;
using OpenFarm.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace OpenFarm.Api.IntegrationTests.Settings;

public sealed class PrinterCostsEndpointTests(CustomWebApplicationFactory factory) : IClassFixture<CustomWebApplicationFactory>
{
    private async Task<HttpClient> ClientAsync()
    {
        var client = factory.CreateClient();
        var login = await client.PostAsJsonAsync(new Uri("/api/v1/auth/login", UriKind.Relative), new LoginCommand("admin", "admin-openfarm"));
        var body = await login.Content.ReadFromJsonAsync<LoginResponse>();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", body!.Token);
        return client;
    }

    [Fact]
    public async Task ModelSettingsRoundTripAndRetainAfterLastPrinterRemoval()
    {
        var client = await ClientAsync();
        var update = new
        {
            brand = "Bambu",
            modelCode = "P1S",
            printerPurchasePrice = 3000m,
            printerUpfrontCost = 500m,
            lifetimeServiceCost = 1000m,
            lifetimePrintingHours = 5000m,
            averagePrintingWatts = 150m
        };
        (await client.PutAsJsonAsync(new Uri("/api/v1/settings/printer-costs/electricity", UriKind.Relative), new { electricityPricePerKwh = 0.5m })).EnsureSuccessStatusCode();
        (await client.PutAsJsonAsync(new Uri("/api/v1/settings/printer-costs/models", UriKind.Relative), update)).EnsureSuccessStatusCode();
        using (var scope = factory.Services.CreateScope())
        {
            var repo = scope.ServiceProvider.GetRequiredService<IPrinterRepository>();
            var printer = Printer.Create("Cost retention", "Bambu", "P1S", "127.0.0.1", Guid.NewGuid().ToString());
            await repo.AddAsync(printer, CancellationToken.None);
            await repo.RemoveAsync(printer, CancellationToken.None);
        }
        var result = await client.GetFromJsonAsync<PrinterCostsResponse>("/api/v1/settings/printer-costs");
        var model = result!.Models.Single(m => m.Brand == "Bambu" && m.ModelCode == "P1S");
        model.DepreciationPerHour.Should().Be(0.9m);
        model.ElectricityPerHour.Should().Be(0.075m);
        // A repeat save must update one persistent record, never create a duplicate.
        (await client.PutAsJsonAsync(new Uri("/api/v1/settings/printer-costs/models", UriKind.Relative), update)).EnsureSuccessStatusCode();
        using var read = factory.Services.CreateScope();
        (await read.ServiceProvider.GetRequiredService<OpenFarmDbContext>().PrinterModelCosts.CountAsync(m => m.Brand == "Bambu" && m.ModelCode == "P1S")).Should().Be(1);
    }

    [Fact]
    public async Task RegistrationCreatesUnknownCostsAndRemovalRetainsThem()
    {
        using var scope = factory.Services.CreateScope();
        var repo = scope.ServiceProvider.GetRequiredService<IPrinterRepository>();
        var printer = Printer.Create("Automatic model", "Bambu", "A1", "127.0.0.1", Guid.NewGuid().ToString());
        await repo.AddAsync(printer, CancellationToken.None);
        var db = scope.ServiceProvider.GetRequiredService<OpenFarmDbContext>();
        var row = await db.PrinterModelCosts.SingleAsync(m => m.Brand == "Bambu" && m.ModelCode == "A1");
        row.Rates.DepreciationPerHour.Should().BeNull();
        row.Rates.ElectricityPerHour(0.5m).Should().BeNull();
        await repo.RemoveAsync(printer, CancellationToken.None);
        (await db.PrinterModelCosts.AnyAsync(m => m.Brand == "Bambu" && m.ModelCode == "A1")).Should().BeTrue();
    }

    [Fact]
    public async Task InvalidValuesAndUnsupportedModelsAreRejectedAndZeroIsPreserved()
    {
        var client = await ClientAsync();
        foreach (var invalid in new object[] {
            new { brand = "Bambu", modelCode = "P1S", lifetimePrintingHours = 0 },
            new { brand = "Bambu", modelCode = "P1S", printerUpfrontCost = -1 },
            new { brand = "Bambu", modelCode = "P1S", averagePrintingWatts = -1 },
            new { brand = "Bambu", modelCode = "unknown" } })
            (await client.PutAsJsonAsync(new Uri("/api/v1/settings/printer-costs/models", UriKind.Relative), invalid)).IsSuccessStatusCode.Should().BeFalse();
        (await client.PutAsJsonAsync(new Uri("/api/v1/settings/printer-costs/electricity", UriKind.Relative), new { electricityPricePerKwh = -1 })).IsSuccessStatusCode.Should().BeFalse();
        var response = await client.PutAsJsonAsync(new Uri("/api/v1/settings/printer-costs/models", UriKind.Relative),
            new { brand = "Bambu", modelCode = "A1MINI", printerPurchasePrice = 0, printerUpfrontCost = 0, lifetimeServiceCost = 0, lifetimePrintingHours = 100, averagePrintingWatts = 0 });
        response.EnsureSuccessStatusCode();
        (await response.Content.ReadFromJsonAsync<PrinterModelCostResponse>())!.DepreciationPerHour.Should().Be(0m);
    }

    [Fact]
    public async Task NestedMixedModelAssemblyUsesAllocatedHoursAndCurrentRates()
    {
        var client = await ClientAsync();
        (await client.PutAsJsonAsync(new Uri("/api/v1/settings/printer-costs/electricity", UriKind.Relative), new { electricityPricePerKwh = 0.5m })).EnsureSuccessStatusCode();
        foreach (var model in new[] { (Brand: "Bambu", Code: "P1S", Price: 900m, Watts: 150m), (Brand: "Snapmaker", Code: "U1", Price: 1500m, Watts: 200m) })
            (await client.PutAsJsonAsync(new Uri("/api/v1/settings/printer-costs/models", UriKind.Relative), new { brand = model.Brand, modelCode = model.Code, printerPurchasePrice = model.Price, printerUpfrontCost = 0, lifetimeServiceCost = 0, lifetimePrintingHours = 1000, averagePrintingWatts = model.Watts })).EnsureSuccessStatusCode();
        async Task<Guid> PartAsync(string model, int units, int minutes)
        {
            using var scope = factory.Services.CreateScope();
            var part = Part.Create("Machine cost part", PartFileFormat.Stl, true, "part.stl", $"/tmp/{Guid.NewGuid():N}.stl", 1024,
                0.4m, null, null, EstimateStatus.Available, minutes, 20, 1, units, $"{Guid.NewGuid():N}",
                [new FilamentRequirementSpec("PLA", null, null, null, null)], [], []);
            part.SetEstimation(new EstimationDetails("DefaultProfiles", model));
            await scope.ServiceProvider.GetRequiredService<IPartRepository>().AddAsync(part, CancellationToken.None);
            return part.Id;
        }
        async Task<Guid> AssemblyAsync(object[] lines)
        {
            var response = await client.PostAsJsonAsync(new Uri("/api/v1/assemblies", UriKind.Relative), new { name = $"Machine cost {Guid.NewGuid():N}", lines });
            response.EnsureSuccessStatusCode();
            return (await response.Content.ReadFromJsonAsync<AssemblyResponse>())!.Id;
        }
        var p = await PartAsync("Bambu Lab P1S", 2, 120);
        var q = await PartAsync("Snapmaker U1", 1, 60);
        var child = await AssemblyAsync([new { targetKind = "Part", targetId = p, quantity = 2 }]);
        var parent = await AssemblyAsync([new { targetKind = "Assembly", targetId = child, quantity = 3 }, new { targetKind = "Part", targetId = p, quantity = 1 }, new { targetKind = "Part", targetId = q, quantity = 2 }]);
        var result = await client.GetFromJsonAsync<ExplodeAssemblyResponse>($"/api/v1/assemblies/{parent}/explosion?quantity=1");
        result!.MachineCosts.Should().HaveCount(2);
        result.MachineCosts.Sum(r => r.PrinterDepreciation).Should().Be(9.3m);
        result.MachineCosts.Sum(r => r.Electricity).Should().Be(0.725m);
        (await client.PutAsJsonAsync(new Uri("/api/v1/settings/printer-costs/electricity", UriKind.Relative), new { electricityPricePerKwh = (decimal?)null })).EnsureSuccessStatusCode();
        result = await client.GetFromJsonAsync<ExplodeAssemblyResponse>($"/api/v1/assemblies/{parent}/explosion?quantity=1");
        result!.MachineCosts.Should().OnlyContain(r => r.Electricity == null && r.PrinterDepreciation != null);
    }

    [Fact]
    public async Task ModelCostsCannotBeRemovedUntilEveryPrinterOfThatModelLeavesTheFleet()
    {
        var client = await ClientAsync();
        using var scope = factory.Services.CreateScope();
        var printers = scope.ServiceProvider.GetRequiredService<IPrinterRepository>();
        var printer = Printer.Create("Kept after cost removal", "Bambu", "P1S", "127.0.0.1", Guid.NewGuid().ToString());
        await printers.AddAsync(printer, CancellationToken.None);
        var secondPrinter = Printer.Create("Same model", "Bambu", "P1S", "127.0.0.2", Guid.NewGuid().ToString());
        await printers.AddAsync(secondPrinter, CancellationToken.None);
        (await client.PutAsJsonAsync(new Uri("/api/v1/settings/printer-costs/models", UriKind.Relative),
            new { brand = "Bambu", modelCode = "P1S", printerPurchasePrice = 1000, printerUpfrontCost = 0, lifetimeServiceCost = 0, lifetimePrintingHours = 1000, averagePrintingWatts = 100 })).EnsureSuccessStatusCode();
        var part = Part.Create("Removed costs part", PartFileFormat.Stl, true, "part.stl", $"/tmp/{Guid.NewGuid():N}.stl", 1024,
            0.4m, null, null, EstimateStatus.Available, 60, 20, 1, 1, $"{Guid.NewGuid():N}",
            [new FilamentRequirementSpec("PLA", null, null, null, null)], [], []);
        part.SetEstimation(new EstimationDetails("DefaultProfiles", "Bambu Lab P1S"));
        await scope.ServiceProvider.GetRequiredService<IPartRepository>().AddAsync(part, CancellationToken.None);

        var response = await client.DeleteAsync(new Uri("/api/v1/settings/printer-costs/models/Bambu/P1S", UriKind.Relative));
        response.StatusCode.Should().Be(HttpStatusCode.Conflict);
        var initial = await client.GetFromJsonAsync<PrinterCostsResponse>("/api/v1/settings/printer-costs");
        var owned = initial!.Models.Single(m => m.Brand == "Bambu" && m.ModelCode == "P1S");
        owned.CanRemove.Should().BeFalse();
        owned.DepreciationPerHour.Should().Be(1m);
        (await printers.GetByIdAsync(printer.Id, CancellationToken.None)).Should().NotBeNull();
        await printers.RemoveAsync(printer, CancellationToken.None);
        (await client.DeleteAsync(new Uri("/api/v1/settings/printer-costs/models/Bambu/P1S", UriKind.Relative))).StatusCode.Should().Be(HttpStatusCode.Conflict);
        await printers.RemoveAsync(secondPrinter, CancellationToken.None);
        var unused = await client.GetFromJsonAsync<PrinterCostsResponse>("/api/v1/settings/printer-costs");
        unused!.Models.Single(m => m.Brand == "Bambu" && m.ModelCode == "P1S").CanRemove.Should().BeTrue();
        response = await client.DeleteAsync(new Uri("/api/v1/settings/printer-costs/models/Bambu/P1S", UriKind.Relative));
        response.StatusCode.Should().Be(HttpStatusCode.NoContent);
        var settings = await client.GetFromJsonAsync<PrinterCostsResponse>("/api/v1/settings/printer-costs");
        settings!.Models.Should().NotContain(m => m.Brand == "Bambu" && m.ModelCode == "P1S");
        var db = scope.ServiceProvider.GetRequiredService<OpenFarmDbContext>();
        (await db.PrinterModelCosts.AnyAsync(m => m.Brand == "Bambu" && m.ModelCode == "P1S")).Should().BeFalse();
        var estimate = await scope.ServiceProvider.GetRequiredService<OpenFarm.Application.Assemblies.AssemblyMachineCosts>()
            .CalculateAsync([new(part.Id, part.Name, 1)], CancellationToken.None);
        estimate.Single().PrinterDepreciation.Should().BeNull();
        estimate.Single().Electricity.Should().BeNull();

        (await client.DeleteAsync(new Uri("/api/v1/settings/printer-costs/models/Bambu/P1S", UriKind.Relative))).StatusCode.Should().Be(HttpStatusCode.NoContent);
        var reconfigured = await client.PutAsJsonAsync(new Uri("/api/v1/settings/printer-costs/models", UriKind.Relative),
            new { brand = "Bambu", modelCode = "P1S", printerPurchasePrice = 2000, printerUpfrontCost = 0, lifetimeServiceCost = 0, lifetimePrintingHours = 1000 });
        reconfigured.EnsureSuccessStatusCode();
        (await reconfigured.Content.ReadFromJsonAsync<PrinterModelCostResponse>())!.DepreciationPerHour.Should().Be(2m);
    }

    [Fact]
    public async Task RegisteringAnotherPrinterRecreatesRemovedModelWithoutItsOldRates()
    {
        var client = await ClientAsync();
        (await client.DeleteAsync(new Uri("/api/v1/settings/printer-costs/models/Bambu/X1E", UriKind.Relative))).EnsureSuccessStatusCode();
        using var scope = factory.Services.CreateScope();
        var repo = scope.ServiceProvider.GetRequiredService<IPrinterRepository>();
        var printer = Printer.Create("Recreated cost model", "Bambu", "X1E", "127.0.0.1", Guid.NewGuid().ToString());
        await repo.AddAsync(printer, CancellationToken.None);
        var settings = await client.GetFromJsonAsync<PrinterCostsResponse>("/api/v1/settings/printer-costs");
        settings!.Models.Single(m => m.Brand == "Bambu" && m.ModelCode == "X1E").DepreciationPerHour.Should().BeNull();
        await repo.RemoveAsync(printer, CancellationToken.None);
    }

    [Fact]
    public async Task RemovalWaitsForAnInFlightPrinterRegistration()
    {
        var client = await ClientAsync();
        (await client.PutAsJsonAsync(new Uri("/api/v1/settings/printer-costs/models", UriKind.Relative),
            new { brand = "Bambu", modelCode = "X1", printerPurchasePrice = 1000, printerUpfrontCost = 0, lifetimeServiceCost = 0, lifetimePrintingHours = 1000 })).EnsureSuccessStatusCode();
        using var registrationScope = factory.Services.CreateScope();
        var db = registrationScope.ServiceProvider.GetRequiredService<OpenFarmDbContext>();
        await using var registration = await db.Database.BeginTransactionAsync();
        var printers = registrationScope.ServiceProvider.GetRequiredService<IPrinterRepository>();
        var printer = Printer.Create("Concurrent registration", "Bambu", "X1", "127.0.0.1", Guid.NewGuid().ToString());
        await printers.AddAsync(printer, CancellationToken.None);
        // Its fleet row is uncommitted. Removal must wait rather than observe an empty fleet and erase its prices.
        using (var removalScope = factory.Services.CreateScope())
        using (var cancellation = new CancellationTokenSource(TimeSpan.FromSeconds(1)))
        {
            var costs = removalScope.ServiceProvider.GetRequiredService<IPrinterCostRepository>();
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => costs.RemoveAsync("Bambu", "X1", cancellation.Token));
        }
        await registration.CommitAsync();
        (await client.DeleteAsync(new Uri("/api/v1/settings/printer-costs/models/Bambu/X1", UriKind.Relative))).StatusCode.Should().Be(HttpStatusCode.Conflict);
        var settings = await client.GetFromJsonAsync<PrinterCostsResponse>("/api/v1/settings/printer-costs");
        var model = settings!.Models.Single(m => m.Brand == "Bambu" && m.ModelCode == "X1");
        model.CanRemove.Should().BeFalse();
        model.DepreciationPerHour.Should().Be(1m);
        await printers.RemoveAsync(printer, CancellationToken.None);
    }
}
