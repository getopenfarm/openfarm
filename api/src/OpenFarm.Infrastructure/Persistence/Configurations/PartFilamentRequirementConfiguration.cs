using Finbuckle.MultiTenant.EntityFrameworkCore.Extensions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using OpenFarm.Domain.Parts;

namespace OpenFarm.Infrastructure.Persistence.Configurations;

public sealed class PartFilamentRequirementConfiguration : IEntityTypeConfiguration<PartFilamentRequirement>
{
    public void Configure(EntityTypeBuilder<PartFilamentRequirement> builder)
    {
        builder.ToTable("PartFilamentRequirements");

        builder.HasKey(r => r.Id);

        builder.Property(r => r.OrganizationId).IsRequired();
        builder.Property(r => r.PartId).IsRequired();

        builder.Property(r => r.MaterialType);
        builder.Property(r => r.DetectedColorHex);
        builder.Property(r => r.DetectedSubBrand);

        // Bare, unenforced references (research.md §7) — a deleted FilamentVariant/FilamentProfile
        // degrades this requirement's narrow override gracefully rather than cascading.
        builder.Property(r => r.NarrowFilamentVariantId);
        builder.Property(r => r.NarrowFilamentProfileId);

        builder.Property(r => r.CreatedAtUtc).IsRequired();

        // Deduplication is guaranteed by FilamentRequirementGrouper before any row is written, not a
        // DB constraint — Postgres treats every NULL-containing row as distinct by default, which
        // can't usefully express "no duplicate pairs" once MaterialType itself can be null
        // (data-model.md).
        builder.HasIndex(r => new { r.PartId, r.MaterialType, r.DetectedColorHex });

        builder.IsMultiTenant();
    }
}
