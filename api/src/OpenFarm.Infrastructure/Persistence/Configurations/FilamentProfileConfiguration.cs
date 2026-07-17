using Finbuckle.MultiTenant.EntityFrameworkCore.Extensions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using OpenFarm.Domain.Profiles;

namespace OpenFarm.Infrastructure.Persistence.Configurations;

public sealed class FilamentProfileConfiguration : IEntityTypeConfiguration<FilamentProfile>
{
    public void Configure(EntityTypeBuilder<FilamentProfile> builder)
    {
        builder.ToTable("FilamentProfiles");

        builder.HasKey(p => p.Id);

        builder.Property(p => p.Name)
            .IsRequired();

        builder.Property(p => p.SourceFormat)
            .IsRequired()
            .HasConversion<string>();

        builder.Property(p => p.BaseProfileName);

        builder.Property(p => p.RawDataJson)
            .IsRequired()
            .HasColumnType("jsonb");

        builder.Property(p => p.CreatedAtUtc)
            .IsRequired();

        // No delete endpoint exists yet (FR-001–015 name no deletion capability) — ON DELETE SET
        // NULL is a defensive default for whenever one is added (data-model.md).
        builder.HasOne<FilamentProfile>()
            .WithMany()
            .HasForeignKey(p => p.BaseProfileId)
            .OnDelete(DeleteBehavior.SetNull);

        // No uniqueness constraint on Name — FR-009 permits duplicate-named imports as separate
        // rows (data-model.md's Validation rules).
        builder.IsMultiTenant();
    }
}
