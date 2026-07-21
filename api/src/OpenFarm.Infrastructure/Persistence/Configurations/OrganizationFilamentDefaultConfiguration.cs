using Finbuckle.MultiTenant.EntityFrameworkCore.Extensions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using OpenFarm.Domain.Profiles;

namespace OpenFarm.Infrastructure.Persistence.Configurations;

public sealed class OrganizationFilamentDefaultConfiguration : IEntityTypeConfiguration<OrganizationFilamentDefault>
{
    public void Configure(EntityTypeBuilder<OrganizationFilamentDefault> builder)
    {
        builder.ToTable("OrganizationFilamentDefaults");

        builder.HasKey(d => d.Id);

        builder.Property(d => d.MaterialType)
            .IsRequired();

        builder.Property(d => d.FilamentProfileId)
            .IsRequired();

        builder.Property(d => d.CreatedAtUtc)
            .IsRequired();

        // Deleting a still-defaulted filament profile is an explicit operator action to reconsider
        // first — no delete endpoint exists for FilamentProfile yet, so this is a defensive default.
        builder.HasOne<FilamentProfile>()
            .WithMany()
            .HasForeignKey(d => d.FilamentProfileId)
            .OnDelete(DeleteBehavior.Restrict);

        // One default per (org, materialType) — a second PUT for the same material replaces it.
        builder.HasIndex(d => new { d.OrganizationId, d.MaterialType })
            .IsUnique();

        builder.IsMultiTenant();
    }
}
