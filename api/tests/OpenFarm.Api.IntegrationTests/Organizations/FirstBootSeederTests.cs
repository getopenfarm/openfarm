using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using OpenFarm.Infrastructure.Persistence;
using OpenFarm.Infrastructure.Seed;

namespace OpenFarm.Api.IntegrationTests.Organizations;

public class FirstBootSeederTests(CustomWebApplicationFactory factory) : IClassFixture<CustomWebApplicationFactory>
{
    [Fact]
    public async Task SeedAsync_RunTwice_LeavesExactlyOneOrganization()
    {
        using var scope = factory.Services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<OpenFarmDbContext>();
        var seeder = scope.ServiceProvider.GetRequiredService<FirstBootSeeder>();

        await seeder.SeedAsync();

        var count = await dbContext.Organizations.CountAsync();
        count.Should().Be(1);
    }

    [Fact]
    public async Task SeedAsync_AcrossThreeSimulatedRestarts_ConvergesOnExactlyOneOrganization()
    {
        using var scope = factory.Services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<OpenFarmDbContext>();
        var seeder = scope.ServiceProvider.GetRequiredService<FirstBootSeeder>();

        for (var i = 0; i < 3; i++)
        {
            await seeder.SeedAsync();
        }

        var count = await dbContext.Organizations.CountAsync();
        count.Should().Be(1);
    }
}
