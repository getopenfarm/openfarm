using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using OpenFarm.Domain.FilamentTracking;

namespace OpenFarm.Infrastructure.Persistence.Configurations;

public sealed class SpoolUsageRecordConfiguration : IEntityTypeConfiguration<SpoolUsageRecord>
{
    public void Configure(EntityTypeBuilder<SpoolUsageRecord> builder)
    {
        builder.ToTable("SpoolUsageRecords");

        builder.HasKey(r => r.Id);

        builder.Property(r => r.GramsConsumed)
            .IsRequired();

        builder.Property(r => r.RecordedAtUtc)
            .IsRequired();

        builder.Property(r => r.JobId);

        builder.Property(r => r.Source)
            .IsRequired()
            .HasConversion<string>();

        builder.HasOne<Spool>()
            .WithMany()
            .HasForeignKey(r => r.SpoolId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
