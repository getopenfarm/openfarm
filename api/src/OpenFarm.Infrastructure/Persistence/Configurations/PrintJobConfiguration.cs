using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using OpenFarm.Domain.Printers;
using OpenFarm.Domain.Production;

namespace OpenFarm.Infrastructure.Persistence.Configurations;

public sealed class PrintJobConfiguration : IEntityTypeConfiguration<PrintJob>
{
    public void Configure(EntityTypeBuilder<PrintJob> builder)
    {
        builder.ToTable("PrintJobs");

        builder.Property(j => j.PartRequirementId).IsRequired();
        builder.Property(j => j.QueuePosition).IsRequired();
        builder.Property(j => j.PlateQuantity).IsRequired();
        builder.Property(j => j.TargetUnits);
        builder.Property(j => j.ObjectExecutionJson).HasColumnType("jsonb");
        builder.Ignore(j => j.ObjectExecution);
        builder.Ignore(j => j.EffectiveTargetUnits);
        builder.Ignore(j => j.ProducedUnits);
        builder.Ignore(j => j.MissingUnits);
        builder.Ignore(j => j.WrittenOffUnits);
        builder.Property(j => j.AssignmentMode).IsRequired().HasConversion<string>();
        builder.Property(j => j.PinnedPrinterId);
        builder.Property(j => j.FileRevisionHash).IsRequired();
        builder.Property(j => j.FileRevisionNumber).IsRequired();
        builder.Property(j => j.DispatchedAtUtc);
        builder.Property(j => j.CancelRequestedAtUtc);
        builder.Property(j => j.RequeuedAsJobId);
        builder.Property(j => j.ProductionSnapshotJson).HasColumnType("jsonb");
        builder.Property(j => j.SnapshotRecoveryReason);
        builder.Property(j => j.SnapshotUnavailableAtUtc);
        builder.Property(j => j.HasFilamentRequirementOverride).HasDefaultValue(false);
        builder.Property(j => j.DispatchProvenanceJson).HasColumnType("jsonb");
        builder.Property(j => j.LastBlockReason);
        builder.Property(j => j.LastBlockedAtUtc);
        builder.Property(j => j.BlockFailureCount).HasDefaultValue(0);
        builder.Property(j => j.WrittenOffAtUtc);

        builder.HasOne<PartRequirement>().WithMany().HasForeignKey(j => j.PartRequirementId).OnDelete(DeleteBehavior.Cascade);

        builder.HasOne<Printer>().WithMany().HasForeignKey(j => j.PinnedPrinterId).OnDelete(DeleteBehavior.SetNull);

        // Self-referencing — the lost job -> its one replacement (research.md R13).
        builder.HasOne<PrintJob>().WithMany().HasForeignKey(j => j.RequeuedAsJobId).OnDelete(DeleteBehavior.SetNull);

        // The dispatcher's hot-path query (research.md R4); Status lives on the Prints root.
        builder.HasIndex(j => new { j.AssignmentMode, j.QueuePosition });
        builder.HasIndex(j => j.PinnedPrinterId).HasFilter("\"PinnedPrinterId\" IS NOT NULL");

        // The row version is on the root (Prints); this one guards the columns that live only in PrintJobs
        // (reorder, pin, unpin touch no Prints column).
        builder.Property<uint>("PrintJobXmin").HasColumnName("xmin").IsRowVersion();
    }
}
