using Finbuckle.MultiTenant.Abstractions;
using Finbuckle.MultiTenant.EntityFrameworkCore;
using Finbuckle.MultiTenant.EntityFrameworkCore.Extensions;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using OpenFarm.Domain.ActivityLog;
using OpenFarm.Domain.ConnectionSecrets;
using OpenFarm.Domain.Organizations;
using OpenFarm.Domain.Printers;
using OpenFarm.Infrastructure.Identity;

namespace OpenFarm.Infrastructure.Persistence;

public sealed class OpenFarmDbContext(
    DbContextOptions<OpenFarmDbContext> options,
    IMultiTenantContextAccessor multiTenantContextAccessor)
    : IdentityDbContext<ApplicationUser, ApplicationRole, Guid>(options), IMultiTenantDbContext
{
    public ITenantInfo? TenantInfo => multiTenantContextAccessor.MultiTenantContext?.TenantInfo;

    public TenantMismatchMode TenantMismatchMode => TenantMismatchMode.Throw;

    public TenantNotSetMode TenantNotSetMode => TenantNotSetMode.Throw;

    public DbSet<Organization> Organizations => Set<Organization>();

    public DbSet<ConnectionSecret> ConnectionSecrets => Set<ConnectionSecret>();

    public DbSet<ActivityLogEntry> ActivityLogEntries => Set<ActivityLogEntry>();

    public DbSet<Printer> Printers => Set<Printer>();

    protected override void OnModelCreating(ModelBuilder builder)
    {
        base.OnModelCreating(builder);

        builder.ApplyConfigurationsFromAssembly(typeof(OpenFarmDbContext).Assembly);
        builder.ConfigureMultiTenant();
    }

    public override int SaveChanges(bool acceptAllChangesOnSuccess)
    {
        this.EnforceMultiTenant();
        return base.SaveChanges(acceptAllChangesOnSuccess);
    }

    public override async Task<int> SaveChangesAsync(bool acceptAllChangesOnSuccess, CancellationToken cancellationToken = default)
    {
        this.EnforceMultiTenant();
        return await base.SaveChangesAsync(acceptAllChangesOnSuccess, cancellationToken);
    }
}
