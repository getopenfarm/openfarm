using Finbuckle.MultiTenant.EntityFrameworkCore.Extensions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using OpenFarm.Domain.Printers;
using OpenFarm.Domain.Production;

namespace OpenFarm.Infrastructure.Persistence.Configurations;

/// <summary>
/// data-model.md's full table shape. <see cref="RunLine.TargetId"/> has no database FK — it is
/// polymorphic across two tables (Part or Assembly) and the discriminator (<see cref="RunLine.TargetKind"/>)
/// is already required, so existence is validated at the Application layer instead (data-model.md's
/// rationale: a RunLine's PartRequirements are already snapshotted and independent of the source
/// Part/Assembly's continued existence, unlike BomLine's FR-011 delete-blocking needs).
/// </summary>
public sealed class RunLineConfiguration : IEntityTypeConfiguration<RunLine>
{
    public void Configure(EntityTypeBuilder<RunLine> builder)
    {
        builder.ToTable("RunLines", t =>
        {
            t.HasCheckConstraint("CK_RunLines_QuantityAtLeastOne", "\"Quantity\" IS NULL OR \"Quantity\" >= 1");
            t.HasCheckConstraint(
                "CK_RunLines_PinnedRequiresPrinter",
                "\"DefaultAssignmentMode\" = 'Unassigned' OR \"DefaultPinnedPrinterId\" IS NOT NULL");
        });

        builder.HasKey(l => l.Id);

        builder.Property(l => l.OrganizationId).IsRequired();
        builder.Property(l => l.ProductionRunId).IsRequired();
        builder.Property(l => l.TargetKind).IsRequired().HasConversion<string>();
        builder.Property(l => l.TargetId).IsRequired();
        builder.Property(l => l.Quantity);
        builder.Property(l => l.TargetName);
        builder.Property(l => l.RevisionMode).IsRequired().HasConversion<string>();
        builder.Property(l => l.DefaultAssignmentMode).IsRequired().HasConversion<string>();
        builder.Property(l => l.DefaultPinnedPrinterId);
        builder.Property(l => l.BackfillPrinterCap).IsRequired();
        builder.Property(l => l.BackfillCumulativeUnitCeiling);

        builder.HasOne<Printer>()
            .WithMany()
            .HasForeignKey(l => l.DefaultPinnedPrinterId)
            .OnDelete(DeleteBehavior.SetNull);

        builder.IsMultiTenant();
    }
}
