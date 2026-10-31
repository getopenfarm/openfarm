using System.Net;
using System.Net.Http.Json;
using System.Net.Http.Headers;
using OpenFarm.Api.Endpoints.Auth;
using OpenFarm.Application.Auth;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.Extensions.DependencyInjection;
using OpenFarm.Domain.Production;
using OpenFarm.Infrastructure.Persistence;
namespace OpenFarm.Api.IntegrationTests.Production;

public class DemandMigrationTests(ProductionTestFactory factory) : ProductionTestContext(factory), IClassFixture<ProductionTestFactory>
{
    [Fact]
    public async Task Upgrade_PreservesExistingRunAndJobIdentity_AndInitializesLegacyModes()
    {
        var client = await ClientAsync();
        var part = await PartAsync(4);
        var finite = await RunAsync(client, part.Id, 8, "Existing order");
        var backfill = await RunAsync(client, part.Id, null, "Existing backfill");
        var before = await JobsAsync(finite.Id);
        using (var scope = Factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<OpenFarmDbContext>();
            var migrator = db.GetService<IMigrator>();
            await migrator.MigrateAsync("20261030071634_AddLibraryFolders");
            await migrator.MigrateAsync();
        }
        // Rolling back beyond the session migration deliberately discards browser sessions.
        (await client.GetAsync($"/api/v1/production-runs/{finite.Id}")).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        var login = await client.PostAsJsonAsync("/api/v1/auth/login", new LoginCommand("admin", "admin-openfarm"));
        login.EnsureSuccessStatusCode();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", (await login.Content.ReadFromJsonAsync<LoginResponse>())!.Token);
        var after = await JobsAsync(finite.Id);
        after.Select(j => new { j.Id, j.PartRequirementId, j.QueuePosition, j.ProductionSnapshotJson, j.Status })
            .Should().BeEquivalentTo(before.Select(j => new { j.Id, j.PartRequirementId, j.QueuePosition, j.ProductionSnapshotJson, j.Status }));
        (await DetailAsync(client, finite.Id)).Name.Should().Be("Existing order");
        using (var scope = Factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<OpenFarmDbContext>();
            var finiteDemand = await db.ProductionDemands.SingleAsync(d => d.ProductionRunId == finite.Id);
            finiteDemand.Mode.Should().Be(DemandMode.Finite);
            finiteDemand.QuantityBasis.Should().Be(QuantityBasis.Units);
            var continuous = await db.ProductionDemands.SingleAsync(d => d.ProductionRunId == backfill.Id);
            continuous.Mode.Should().Be(DemandMode.Backfill);
            continuous.State.Should().Be(DemandState.Enabled);
            var recipe = await db.PartRequirements.SingleAsync(r => r.RunLineId == continuous.Id);
            recipe.RecipeActive.Should().BeTrue(); recipe.UnitsPerSet.Should().Be(1);
        }
    }
}
