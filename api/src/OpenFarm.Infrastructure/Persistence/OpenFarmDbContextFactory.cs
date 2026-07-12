using Finbuckle.MultiTenant.Abstractions;
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

        return new OpenFarmDbContext(optionsBuilder.Options, new DesignTimeMultiTenantContextAccessor());
    }

    private sealed class DesignTimeMultiTenantContextAccessor : IMultiTenantContextAccessor
    {
        public IMultiTenantContext MultiTenantContext { get; } = new EmptyMultiTenantContext();

        private sealed class EmptyMultiTenantContext : IMultiTenantContext
        {
            public ITenantInfo? TenantInfo { get; init; }

            public bool IsResolved => false;

            public StrategyInfo? StrategyInfo { get; init; }
        }
    }
}
