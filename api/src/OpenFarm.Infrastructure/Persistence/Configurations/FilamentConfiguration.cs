using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using OpenFarm.Domain.FilamentTracking;

namespace OpenFarm.Infrastructure.Persistence.Configurations;

public sealed class FilamentConfiguration : IEntityTypeConfiguration<Filament>
{
    public void Configure(EntityTypeBuilder<Filament> builder)
    {
        builder.ToTable("Filaments");

        builder.HasKey(f => f.Id);

        builder.Property(f => f.Material)
            .IsRequired();

        builder.Property(f => f.MaterialType)
            .IsRequired();

        builder.HasMany(f => f.ProfileAssignments).WithOne().HasForeignKey(a => a.FilamentId).OnDelete(DeleteBehavior.Cascade);
        builder.Navigation(f => f.ProfileAssignments).UsePropertyAccessMode(PropertyAccessMode.Field);

        builder.Property(f => f.CreatedAtUtc)
            .IsRequired();

        builder.HasOne<Vendor>()
            .WithMany()
            .HasForeignKey(f => f.VendorId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasMany(f => f.Variants)
            .WithOne()
            .HasForeignKey(v => v.FilamentId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.Navigation(f => f.Variants)
            .UsePropertyAccessMode(PropertyAccessMode.Field);
    }
}
