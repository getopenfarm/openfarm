using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using OpenFarm.Domain.FilamentTracking;

namespace OpenFarm.Infrastructure.Persistence.Configurations;

// No OrganizationId column — reached exclusively through its parent Filament (data-model.md's EF
// Core configuration notes), so no IsMultiTenant() call here.
public sealed class FilamentVariantConfiguration : IEntityTypeConfiguration<FilamentVariant>
{
    public void Configure(EntityTypeBuilder<FilamentVariant> builder)
    {
        builder.ToTable("FilamentVariants");

        builder.HasKey(v => v.Id);

        builder.Property(v => v.ColorName)
            .IsRequired();

        builder.Property(v => v.ColorHex)
            .IsRequired();

        builder.Property(v => v.CreatedAtUtc)
            .IsRequired();

        builder.HasIndex(v => new { v.FilamentId, v.ColorName })
            .IsUnique();
    }
}
