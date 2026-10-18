using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using OpenFarm.Domain.Parts;

namespace OpenFarm.Infrastructure.Persistence.Configurations;

public sealed class PartThumbnailConfiguration : IEntityTypeConfiguration<PartThumbnail>
{
    public void Configure(EntityTypeBuilder<PartThumbnail> builder)
    {
        builder.ToTable("PartThumbnails");

        builder.HasKey(t => new { t.PartId, t.PlateIndex });

        builder.Property(t => t.Bytes).IsRequired();
    }
}
