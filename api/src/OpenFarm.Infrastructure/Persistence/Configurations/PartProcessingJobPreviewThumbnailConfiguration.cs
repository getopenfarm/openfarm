using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using OpenFarm.Domain.Parts;

namespace OpenFarm.Infrastructure.Persistence.Configurations;

public sealed class PartProcessingJobPreviewThumbnailConfiguration : IEntityTypeConfiguration<PartProcessingJobPreviewThumbnail>
{
    public void Configure(EntityTypeBuilder<PartProcessingJobPreviewThumbnail> builder)
    {
        builder.ToTable("PartProcessingJobPreviewThumbnails");

        builder.HasKey(t => new { t.JobId, t.PlateIndex });

        builder.Property(t => t.Bytes).IsRequired();
    }
}
