using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using OpenFarm.Domain.Parts;
using OpenFarm.Domain.Production;

namespace OpenFarm.Infrastructure.Persistence.Configurations;

public sealed class PartRequirementConfiguration : IEntityTypeConfiguration<PartRequirement>
{
    public void Configure(EntityTypeBuilder<PartRequirement> builder)
    {
        builder.ToTable("PartRequirements");

        builder.HasKey(r => r.Id);
        builder.Property(r => r.RunLineId).IsRequired();
        builder.Property(r => r.PartId).IsRequired();
        builder.Property(r => r.LibraryPartId);
        builder.Property(r => r.PartName);
        builder.Property(r => r.Quantity).HasColumnType("numeric");

        builder.HasOne<RunLine>().WithMany().HasForeignKey(r => r.RunLineId).OnDelete(DeleteBehavior.Cascade);
        builder.HasOne<Part>().WithMany().HasForeignKey(r => r.LibraryPartId).OnDelete(DeleteBehavior.Restrict);

        // FR-004's grain rule, enforced structurally — exactly one PartRequirement per (RunLine, Part) pair.
        builder.HasIndex(r => new { r.RunLineId, r.PartId }).IsUnique();
    }
}
