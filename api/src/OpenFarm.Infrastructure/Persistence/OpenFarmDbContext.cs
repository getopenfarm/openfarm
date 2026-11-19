using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using OpenFarm.Domain.ActivityLog;
using OpenFarm.Application.AuditLog;
using OpenFarm.Infrastructure.ActivityLog;
using OpenFarm.Domain.Assemblies;
using OpenFarm.Domain.ConnectionSecrets;
using OpenFarm.Domain.FilamentTracking;
using OpenFarm.Domain.Maintenance;
using OpenFarm.Domain.Parts;
using OpenFarm.Domain.Printers;
using OpenFarm.Domain.Production;
using OpenFarm.Domain.Profiles;
using OpenFarm.Domain.Settings;
using OpenFarm.Infrastructure.Identity;

namespace OpenFarm.Infrastructure.Persistence;

public sealed class OpenFarmDbContext(DbContextOptions<OpenFarmDbContext> options, IAuditContextAccessor? auditContext = null, IAuditChangeNotifier? auditNotifier = null)
    : IdentityDbContext<ApplicationUser, ApplicationRole, Guid>(options)
{
    public DbSet<OpenFarm.Domain.Library.LibraryFolder> LibraryFolders => Set<OpenFarm.Domain.Library.LibraryFolder>();

    public DbSet<PersonalAccessToken> PersonalAccessTokens => Set<PersonalAccessToken>();

    public DbSet<BrowserSession> BrowserSessions => Set<BrowserSession>();

    public DbSet<AuthenticationSettingsRecord> AuthenticationSettings => Set<AuthenticationSettingsRecord>();
    public DbSet<OidcLogoutReceipt> OidcLogoutReceipts => Set<OidcLogoutReceipt>();

    public DbSet<SessionCredential> SessionCredentials => Set<SessionCredential>();

    public DbSet<OAuthClient> OAuthClients => Set<OAuthClient>();

    public DbSet<FarmSettings> FarmSettings => Set<FarmSettings>();

    public DbSet<ServerSettings> ServerSettings => Set<ServerSettings>();

    public DbSet<ConnectionSecret> ConnectionSecrets => Set<ConnectionSecret>();

    public DbSet<ActivityLogEntry> ActivityLogEntries => Set<ActivityLogEntry>();

    public DbSet<PrinterModelCost> PrinterModelCosts => Set<PrinterModelCost>();

    public DbSet<FeeProfile> FeeProfiles => Set<FeeProfile>();

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

    public DbSet<ProductionDemand> ProductionDemands => Set<ProductionDemand>();

    public DbSet<PartRequirement> PartRequirements => Set<PartRequirement>();

    public DbSet<PrintJob> PrintJobs => Set<PrintJob>();

    public DbSet<ExternalPrint> ExternalPrints => Set<ExternalPrint>();

    public DbSet<Print> Prints => Set<Print>();

    public DbSet<PrintFilament> PrintFilaments => Set<PrintFilament>();

    public DbSet<MaintenanceTask> MaintenanceTasks => Set<MaintenanceTask>();

    public DbSet<MaintenanceEnrollment> MaintenanceEnrollments => Set<MaintenanceEnrollment>();

    public DbSet<MaintenanceLog> MaintenanceLogs => Set<MaintenanceLog>();

    private readonly string _auditCorrelationId = Guid.NewGuid().ToString();

    private bool _pendingAuditNotification;
    private Guid? _auditTransactionId;
    private void TrackAuditNotification()
    {
        var transactionId = Database.CurrentTransaction?.TransactionId;
        // Disposing an uncommitted outer transaction also discards its pending notification.
        if (_auditTransactionId is { } previous && previous != transactionId) _pendingAuditNotification = false;
        _auditTransactionId = transactionId;
        _pendingAuditNotification |= ChangeTracker.Entries<ActivityLogEntry>().Any(e => e.State == EntityState.Added);
    }
    public bool TakeAuditNotification()
    {
        var pending = _pendingAuditNotification;
        _pendingAuditNotification = false;
        return pending;
    }

    public override int SaveChanges(bool acceptAllChangesOnSuccess)
    {
        var entries = AuditCapture.Prepare(this, auditContext?.Current ?? AuditContextScope.Current, _auditCorrelationId);
        TrackAuditNotification();
        try
        {
            var result = base.SaveChanges(acceptAllChangesOnSuccess);
            if (Database.CurrentTransaction is null && TakeAuditNotification() && auditNotifier is not null)
                auditNotifier.NotifyAsync().GetAwaiter().GetResult();
            return result;
        }
        catch
        {
            _pendingAuditNotification = false;
            foreach (var entry in entries) Entry(entry).State = EntityState.Detached;
            throw;
        }
    }

    public override async Task<int> SaveChangesAsync(bool acceptAllChangesOnSuccess, CancellationToken cancellationToken = default)
    {
        var entries = AuditCapture.Prepare(this, auditContext?.Current ?? AuditContextScope.Current, _auditCorrelationId);
        TrackAuditNotification();
        try
        {
            var result = await base.SaveChangesAsync(acceptAllChangesOnSuccess, cancellationToken);
            if (Database.CurrentTransaction is null && TakeAuditNotification() && auditNotifier is not null)
                await auditNotifier.NotifyAsync();
            return result;
        }
        catch
        {
            _pendingAuditNotification = false;
            foreach (var entry in entries) Entry(entry).State = EntityState.Detached;
            throw;
        }
    }

    protected override void OnModelCreating(ModelBuilder builder)
    {
        base.OnModelCreating(builder);

        builder.ApplyConfigurationsFromAssembly(typeof(OpenFarmDbContext).Assembly);
    }
}
