using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using OpenFarm.Domain.FilamentTracking;
using OpenFarm.Domain.Printers;
using OpenFarm.Domain.Production;

namespace OpenFarm.Infrastructure.Persistence.Configurations;

/// <summary>
/// Table-per-type root: the lifecycle and filament lines shared by <see cref="PrintJob"/> and <see cref="ExternalPrint"/>.
/// Each subtype keeps only its own columns in its own table. The printer FK is SetNull, never Cascade — a cascade from
/// Printers would delete only this row and orphan the subtype row.
/// </summary>
public sealed class PrintConfiguration : IEntityTypeConfiguration<Print>
{
    public void Configure(EntityTypeBuilder<Print> builder)
    {
        builder.ToTable("Prints");
        builder.UseTptMappingStrategy();

        builder.HasKey(p => p.Id);
        builder.Property(p => p.Name).IsRequired();
        builder.Property(p => p.Source).IsRequired().HasConversion<string>();
        builder.Property(p => p.Status).IsRequired().HasConversion<string>();
        builder.Property(p => p.PrinterId);
        builder.Property(p => p.StartedAtUtc);
        builder.Property(p => p.EndedAtUtc);
        builder.Property(p => p.ActualDurationMinutes);
        builder.Property(p => p.CreatedAtUtc).IsRequired();
        builder.Property(p => p.StopLayer);
        builder.Property(p => p.StopTotalLayers);
        builder.Property(p => p.StopPercent);
        builder.Property(p => p.FilamentFollowUpDismissedAtUtc);

        builder.HasOne<Printer>().WithMany().HasForeignKey(p => p.PrinterId).OnDelete(DeleteBehavior.SetNull);

        builder.HasMany(p => p.Filaments).WithOne().HasForeignKey(f => f.PrintId).OnDelete(DeleteBehavior.Cascade);
        builder.Navigation(p => p.Filaments).UsePropertyAccessMode(PropertyAccessMode.Field);

        builder.HasIndex(p => p.Status);
        builder.HasIndex(p => new { p.PrinterId, p.Status });
        builder.HasIndex(p => new { p.Source, p.StartedAtUtc });

        builder.Property<uint>("xmin").IsRowVersion();
    }
}

public sealed class PrintFilamentConfiguration : IEntityTypeConfiguration<PrintFilament>
{
    public void Configure(EntityTypeBuilder<PrintFilament> builder)
    {
        builder.ToTable("PrintFilaments");

        builder.HasKey(f => f.Id);
        builder.Property(f => f.PrintId).IsRequired();
        builder.Property(f => f.ToolIndex);
        builder.Property(f => f.MaterialType);
        builder.Property(f => f.DetectedColorHex);
        builder.Property(f => f.SpoolId);
        builder.Property(f => f.NarrowFilamentVariantId);
        builder.Property(f => f.NarrowFilamentProfileId);
        builder.Ignore(f => f.Requirement);
        builder.Property(f => f.SkippedReason).HasConversion<string>();
        builder.Property(f => f.GramsConsumed).HasColumnType("numeric");
        builder.Property(f => f.EstimatedGrams).HasColumnType("numeric");
        builder.Property(f => f.UsageCurve).HasConversion(
            curve => curve == null ? null : curve.ToJson(), json => FilamentUsageCurve.FromJson(json));

        // No unique (PrintId, ToolIndex): SetFilamentRequirements inserts the new lines before EF deletes the old ones.
        builder.HasIndex(f => f.PrintId);
        builder.HasIndex(f => f.SpoolId);

        builder.HasOne<Spool>().WithMany().HasForeignKey(f => f.SpoolId).OnDelete(DeleteBehavior.SetNull);
    }
}
