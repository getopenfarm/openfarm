using Finbuckle.MultiTenant.EntityFrameworkCore.Extensions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using OpenFarm.Domain.FilamentTracking;
using OpenFarm.Domain.Profiles;

namespace OpenFarm.Infrastructure.Persistence.Configurations;

public sealed class FilamentProfileAssignmentConfiguration : IEntityTypeConfiguration<FilamentProfileAssignment>
{
    public void Configure(EntityTypeBuilder<FilamentProfileAssignment> builder)
    {
        builder.ToTable("FilamentProfileAssignments", table => table.HasCheckConstraint("CK_FilamentAssignment_Nozzle", "\"NozzleDiameterMm\" > 0"));
        builder.HasKey(a => a.Id);
        builder.Property(a => a.OrganizationId).IsRequired();
        builder.Property(a => a.Brand).IsRequired();
        builder.Property(a => a.ModelCode).IsRequired();
        builder.HasOne<FilamentProfile>().WithMany().HasForeignKey(a => a.FilamentProfileId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<FilamentVariant>().WithMany().HasForeignKey(a => a.FilamentVariantId).OnDelete(DeleteBehavior.Cascade);
        builder.HasIndex(a => new { a.OrganizationId, a.FilamentId, a.Brand, a.ModelCode, a.NozzleDiameterMm })
            .IsUnique().HasFilter("\"FilamentVariantId\" IS NULL");
        builder.HasIndex(a => new { a.OrganizationId, a.FilamentId, a.Brand, a.ModelCode, a.NozzleDiameterMm, a.FilamentVariantId })
            .IsUnique().HasFilter("\"FilamentVariantId\" IS NOT NULL");
        builder.IsMultiTenant();
    }
}
