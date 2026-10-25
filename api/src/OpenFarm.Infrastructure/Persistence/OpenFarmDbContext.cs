using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using OpenFarm.Domain.ActivityLog;
using OpenFarm.Domain.Assemblies;
using OpenFarm.Domain.ConnectionSecrets;
using OpenFarm.Domain.FilamentTracking;
using OpenFarm.Domain.Parts;
using OpenFarm.Domain.Printers;
using OpenFarm.Domain.Production;
using OpenFarm.Domain.Profiles;
using OpenFarm.Domain.Settings;
using OpenFarm.Infrastructure.Identity;

namespace OpenFarm.Infrastructure.Persistence;

public sealed class OpenFarmDbContext(DbContextOptions<OpenFarmDbContext> options)
    : IdentityDbContext<ApplicationUser, ApplicationRole, Guid>(options)
{
    public DbSet<FarmSettings> FarmSettings => Set<FarmSettings>();

    public DbSet<ConnectionSecret> ConnectionSecrets => Set<ConnectionSecret>();

    public DbSet<ActivityLogEntry> ActivityLogEntries => Set<ActivityLogEntry>();

    public DbSet<Printer> Printers => Set<Printer>();

    public DbSet<PrinterActivitySpan> PrinterActivitySpans => Set<PrinterActivitySpan>();

    public DbSet<MachineProfile> MachineProfiles => Set<MachineProfile>();

    public DbSet<FilamentProfile> FilamentProfiles => Set<FilamentProfile>();

    public DbSet<ProcessProfile> ProcessProfiles => Set<ProcessProfile>();

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

    public DbSet<OpenFarm.Domain.Assemblies.Assembly> Assemblies => Set<OpenFarm.Domain.Assemblies.Assembly>();

    public DbSet<BomLine> BomLines => Set<BomLine>();

    public DbSet<NonPrintedComponent> NonPrintedComponents => Set<NonPrintedComponent>();

    public DbSet<ProductionRun> ProductionRuns => Set<ProductionRun>();

    public DbSet<RunLine> RunLines => Set<RunLine>();

    public DbSet<PartRequirement> PartRequirements => Set<PartRequirement>();

    public DbSet<PrintJob> PrintJobs => Set<PrintJob>();

    public DbSet<ExternalPrint> ExternalPrints => Set<ExternalPrint>();

    public DbSet<Print> Prints => Set<Print>();

    public DbSet<PrintFilament> PrintFilaments => Set<PrintFilament>();

    protected override void OnModelCreating(ModelBuilder builder)
    {
        base.OnModelCreating(builder);

        builder.ApplyConfigurationsFromAssembly(typeof(OpenFarmDbContext).Assembly);
    }
}
