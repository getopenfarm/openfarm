using Finbuckle.MultiTenant.EntityFrameworkCore.Extensions;
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

        builder.HasKey(j => j.Id);

        builder.Property(j => j.OrganizationId).IsRequired();
        builder.Property(j => j.PartRequirementId).IsRequired();
        builder.Property(j => j.QueuePosition).IsRequired();
        builder.Property(j => j.PlateQuantity).IsRequired();
        builder.Property(j => j.AssignmentMode).IsRequired().HasConversion<string>();
        builder.Property(j => j.PinnedPrinterId);
        builder.Property(j => j.DispatchedPrinterId);
        builder.Property(j => j.Status).IsRequired().HasConversion<string>();
        builder.Property(j => j.FileRevisionHash).IsRequired();
        builder.Property(j => j.FileRevisionNumber).IsRequired();
        builder.Property(j => j.DispatchedAtUtc);
        builder.Property(j => j.StartedAtUtc);
        builder.Property(j => j.EndedAtUtc);
        builder.Property(j => j.ActualDurationMinutes);
        builder.Property(j => j.CancelRequestedAtUtc);
        builder.Property(j => j.RequeuedAsJobId);
        builder.Property(j => j.CreatedAtUtc).IsRequired();

        builder.HasOne<PartRequirement>().WithMany().HasForeignKey(j => j.PartRequirementId).OnDelete(DeleteBehavior.Cascade);

        // Two independently-configured FKs to Printers — no navigation on either side, so EF treats
        // them as distinct relationships (mirrors BomLineConfiguration's three target FKs).
        builder.HasOne<Printer>().WithMany().HasForeignKey(j => j.PinnedPrinterId).OnDelete(DeleteBehavior.SetNull);
        builder.HasOne<Printer>().WithMany().HasForeignKey(j => j.DispatchedPrinterId).OnDelete(DeleteBehavior.SetNull);

        // Self-referencing — the lost job -> its one replacement (research.md R13).
        builder.HasOne<PrintJob>().WithMany().HasForeignKey(j => j.RequeuedAsJobId).OnDelete(DeleteBehavior.SetNull);

        builder.HasMany(j => j.FilamentConsumption)
            .WithOne()
            .HasForeignKey(c => c.PrintJobId)
            .OnDelete(DeleteBehavior.Cascade);
        builder.Navigation(j => j.FilamentConsumption).UsePropertyAccessMode(PropertyAccessMode.Field);

        // The dispatcher's own hot-path query (research.md R4).
        builder.HasIndex(j => new { j.OrganizationId, j.Status, j.AssignmentMode, j.QueuePosition });
        builder.HasIndex(j => new { j.OrganizationId, j.PinnedPrinterId }).HasFilter("\"PinnedPrinterId\" IS NOT NULL");

        builder.Property<uint>("xmin").IsRowVersion();

        builder.IsMultiTenant();
    }
}
