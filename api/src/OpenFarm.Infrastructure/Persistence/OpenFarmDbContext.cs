using Finbuckle.MultiTenant.Abstractions;
using Finbuckle.MultiTenant.EntityFrameworkCore;
using Finbuckle.MultiTenant.EntityFrameworkCore.Extensions;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using OpenFarm.Domain.ActivityLog;
using OpenFarm.Domain.ConnectionSecrets;
using OpenFarm.Domain.FilamentTracking;
using OpenFarm.Domain.Organizations;
using OpenFarm.Domain.Parts;
using OpenFarm.Domain.Printers;
using OpenFarm.Domain.Profiles;
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

    public DbSet<MachineProfile> MachineProfiles => Set<MachineProfile>();

    public DbSet<FilamentProfile> FilamentProfiles => Set<FilamentProfile>();

    public DbSet<ProcessProfile> ProcessProfiles => Set<ProcessProfile>();

    public DbSet<OrganizationFilamentDefault> OrganizationFilamentDefaults => Set<OrganizationFilamentDefault>();

    public DbSet<Vendor> Vendors => Set<Vendor>();

    public DbSet<Filament> Filaments => Set<Filament>();

    public DbSet<FilamentVariant> FilamentVariants => Set<FilamentVariant>();

    public DbSet<FeedUnit> FeedUnits => Set<FeedUnit>();

    public DbSet<FeedSlot> FeedSlots => Set<FeedSlot>();

    public DbSet<Spool> Spools => Set<Spool>();

    public DbSet<SpoolUsageRecord> SpoolUsageRecords => Set<SpoolUsageRecord>();

    public DbSet<Part> Parts => Set<Part>();

    public DbSet<PartFilamentRequirement> PartFilamentRequirements => Set<PartFilamentRequirement>();

    public DbSet<PartThumbnail> PartThumbnails => Set<PartThumbnail>();

    public DbSet<Tag> Tags => Set<Tag>();

    public DbSet<PartProcessingJob> PartProcessingJobs => Set<PartProcessingJob>();

    public DbSet<PartProcessingJobPreviewThumbnail> PartProcessingJobPreviewThumbnails => Set<PartProcessingJobPreviewThumbnail>();

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
