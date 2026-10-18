using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace OpenFarm.Infrastructure.Persistence;

// Lets `dotnet ef` construct OpenFarmDbContext at design time without booting the full web host.
public sealed class OpenFarmDbContextFactory : IDesignTimeDbContextFactory<OpenFarmDbContext>
{
    public OpenFarmDbContext CreateDbContext(string[] args)
    {
        var connectionString = Environment.GetEnvironmentVariable("ConnectionStrings__Default")
            ?? "Host=localhost;Port=5432;Database=openfarm;Username=openfarm;Password=changeme";

        var optionsBuilder = new DbContextOptionsBuilder<OpenFarmDbContext>();
        optionsBuilder.UseNpgsql(connectionString);

        return new OpenFarmDbContext(optionsBuilder.Options);
    }
}
