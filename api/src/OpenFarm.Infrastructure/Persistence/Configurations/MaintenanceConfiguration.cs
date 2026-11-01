using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using OpenFarm.Domain.Maintenance;
using OpenFarm.Domain.Printers;

namespace OpenFarm.Infrastructure.Persistence.Configurations;

public sealed class MaintenanceTaskConfiguration : IEntityTypeConfiguration<MaintenanceTask>
{
    public void Configure(EntityTypeBuilder<MaintenanceTask> builder)
    {
        builder.ToTable("MaintenanceTasks", t => t.HasCheckConstraint("CK_MaintenanceTasks_Intervals",
            "(\"EveryPrintHours\" IS NOT NULL OR \"EveryDays\" IS NOT NULL OR \"EveryPrints\" IS NOT NULL)"
            + " AND coalesce(\"EveryPrintHours\", 1) > 0 AND coalesce(\"EveryDays\", 1) > 0 AND coalesce(\"EveryPrints\", 1) > 0"
            + " AND \"WarnAtPercent\" BETWEEN 1 AND 99"));
        builder.HasKey(t => t.Id);
        builder.Property(t => t.Id).ValueGeneratedNever();
        builder.Property(t => t.Name).IsRequired().HasMaxLength(MaintenanceTask.MaxNameLength);
        builder.Property(t => t.CreatedAtUtc);
        builder.HasIndex(t => t.Name).IsUnique();
    }
}

public sealed class MaintenanceEnrollmentConfiguration : IEntityTypeConfiguration<MaintenanceEnrollment>
{
    public void Configure(EntityTypeBuilder<MaintenanceEnrollment> builder)
    {
        builder.ToTable("MaintenanceEnrollments", t =>
            t.HasCheckConstraint("CK_MaintenanceEnrollments_Carries", "\"CarryHours\" >= 0 AND \"CarryPrints\" >= 0"));
        builder.HasKey(e => e.Id);
        builder.Property(e => e.Id).ValueGeneratedNever();
        builder.HasOne<MaintenanceTask>().WithMany().HasForeignKey(e => e.TaskId).OnDelete(DeleteBehavior.Cascade);
        builder.HasOne<Printer>().WithMany().HasForeignKey(e => e.PrinterId).OnDelete(DeleteBehavior.Cascade);
        builder.HasIndex(e => new { e.TaskId, e.PrinterId }).IsUnique();
        builder.HasIndex(e => e.PrinterId);
    }
}

/// <summary>Append-only; outlives its enrollment, task and printer (SetNull), keeping their names as snapshots.</summary>
public sealed class MaintenanceLogConfiguration : IEntityTypeConfiguration<MaintenanceLog>
{
    public void Configure(EntityTypeBuilder<MaintenanceLog> builder)
    {
        builder.ToTable("MaintenanceLogs", t => t.HasCheckConstraint("CK_MaintenanceLogs_Cost", "\"Cost\" IS NULL OR \"Cost\" >= 0"));
        builder.HasKey(l => l.Id);
        builder.Property(l => l.Id).ValueGeneratedNever();
        builder.Property(l => l.TaskName).IsRequired();
        builder.Property(l => l.PrinterName).IsRequired();
        // Get-only properties are mapped only when named.
        builder.Property(l => l.PerformedAtUtc);
        builder.Property(l => l.PerformedByUserId);
        builder.Property(l => l.PerformedByNote);
        builder.Property(l => l.Notes);
        builder.Property(l => l.Cost);
        builder.Property(l => l.HoursAtCompletion);
        builder.Property(l => l.PrintsAtCompletion);
        builder.HasOne<MaintenanceEnrollment>().WithMany().HasForeignKey(l => l.EnrollmentId).OnDelete(DeleteBehavior.SetNull);
        builder.HasOne<MaintenanceTask>().WithMany().HasForeignKey(l => l.TaskId).OnDelete(DeleteBehavior.SetNull);
        builder.HasOne<Printer>().WithMany().HasForeignKey(l => l.PrinterId).OnDelete(DeleteBehavior.SetNull);
        builder.HasIndex(l => new { l.PrinterId, l.PerformedAtUtc }).IsDescending(false, true);
    }
}
