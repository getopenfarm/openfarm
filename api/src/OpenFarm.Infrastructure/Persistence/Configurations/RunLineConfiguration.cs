using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using OpenFarm.Domain.Printers;
using OpenFarm.Domain.Production;

namespace OpenFarm.Infrastructure.Persistence.Configurations;

/// <summary>
/// data-model.md's full table shape. <see cref="ProductionDemand.TargetId"/> has no database FK — it is
/// polymorphic across two tables (Part or Assembly) and the discriminator (<see cref="ProductionDemand.TargetKind"/>)
/// is already required, so existence is validated at the Application layer instead (data-model.md's
/// rationale: a ProductionDemand's PartRequirements are already snapshotted and independent of the source
/// Part/Assembly's continued existence, unlike BomLine's FR-011 delete-blocking needs).
/// </summary>
public sealed class RunLineConfiguration : IEntityTypeConfiguration<ProductionDemand>
{
    public void Configure(EntityTypeBuilder<ProductionDemand> builder)
    {
        builder.ToTable("RunLines", t =>
        {
            t.HasCheckConstraint("CK_RunLines_QuantityAtLeastOne", "\"Quantity\" IS NULL OR \"Quantity\" >= 1");
            t.HasCheckConstraint(
                "CK_RunLines_PinnedRequiresPrinter",
                "\"DefaultAssignmentMode\" = 'Unassigned' OR \"DefaultPinnedPrinterId\" IS NOT NULL");
        });

        builder.HasKey(l => l.Id);
        builder.Property(l => l.ProductionRunId);
        builder.Property(l => l.Mode).HasConversion<string>();
        builder.Property(l => l.QuantityBasis).HasConversion<string>();
        builder.Property(l => l.State).HasConversion<string>();
        builder.Property(l => l.CreatedAtUtc);
        builder.Property(l => l.BalanceSinceUtc);
        builder.HasIndex(l => l.SubmissionId).IsUnique().HasFilter("\"SubmissionId\" IS NOT NULL");
        builder.Property<uint>("xmin").IsRowVersion();
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
    }
}
