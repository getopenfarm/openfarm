using System.Text.Json;
using Finbuckle.MultiTenant.EntityFrameworkCore.Extensions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using OpenFarm.Domain.Parts;

namespace OpenFarm.Infrastructure.Persistence.Configurations;

public sealed class PartProcessingJobConfiguration : IEntityTypeConfiguration<PartProcessingJob>
{
    public void Configure(EntityTypeBuilder<PartProcessingJob> builder)
    {
        builder.ToTable("PartProcessingJobs");

        builder.HasKey(j => j.Id);

        builder.Property(j => j.OrganizationId).IsRequired();
        builder.Property(j => j.PartId);
        builder.Property(j => j.Kind).IsRequired().HasConversion<string>();
        builder.Property(j => j.Status).IsRequired().HasConversion<string>();

        builder.Property(j => j.FileFormat).IsRequired().HasConversion<string>();
        builder.Property(j => j.OriginalFileName).IsRequired();
        builder.Property(j => j.StoragePath).IsRequired();
        builder.Property(j => j.FileSizeBytes).IsRequired();

        builder.Property(j => j.RequiresSlicing).IsRequired();
        builder.Property(j => j.SlicerEngineUsed).HasConversion<string>();
        builder.Property(j => j.SlicerVersionUsed);
        builder.Property(j => j.TargetNozzleDiameterMm);
        builder.Property(j => j.TargetMaterialType);

        builder.Property(j => j.PreviewNozzleDiameterMm);
        builder.Property(j => j.PreviewEstimateStatus).HasConversion<string>();
        builder.Property(j => j.PreviewEstimatedPrintTimeMinutes);
        builder.Property(j => j.PreviewEstimatedFilamentWeightGrams);
        builder.Property(j => j.PreviewPlateCount);

        // Small, bounded, never independently queried — the same jsonb-value-list pattern
        // Printer.AmsUnits already establishes (research.md §3a), not a child table.
        builder.Property(j => j.PreviewFilamentRequirements)
            .HasColumnType("jsonb")
            .HasConversion(
                requirements => JsonSerializer.Serialize(requirements, (JsonSerializerOptions?)null),
                json => JsonSerializer.Deserialize<IReadOnlyList<PreviewFilamentRequirement>>(json, (JsonSerializerOptions?)null) ?? Array.Empty<PreviewFilamentRequirement>())
            .Metadata.SetValueComparer(new ValueComparer<IReadOnlyList<PreviewFilamentRequirement>>(
                (left, right) => (left ?? Array.Empty<PreviewFilamentRequirement>()).SequenceEqual(right ?? Array.Empty<PreviewFilamentRequirement>()),
                requirements => requirements.Aggregate(0, (hash, r) => HashCode.Combine(hash, r.GetHashCode())),
                requirements => requirements.ToList()));

        builder.Property(j => j.ErrorMessage);
        builder.Property(j => j.RequestedByUserId);
        builder.Property(j => j.CreatedAtUtc).IsRequired();
        builder.Property(j => j.ProcessedAtUtc);
        builder.Property(j => j.ResolvedAtUtc);

        builder.HasMany(j => j.PreviewThumbnails)
            .WithOne()
            .HasForeignKey(t => t.JobId)
            .OnDelete(DeleteBehavior.Cascade);
        builder.Navigation(j => j.PreviewThumbnails).UsePropertyAccessMode(PropertyAccessMode.Field);

        builder.IsMultiTenant();
    }
}
