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

        // Null on rows written before the ledger kept the weight after each change.
        builder.Property(r => r.RemainingAfterGrams);

        builder.Property(r => r.Reason)
            .HasMaxLength(SpoolUsageRecord.MaxReasonLength);

        builder.HasOne<Spool>()
            .WithMany()
            .HasForeignKey(r => r.SpoolId)
            .OnDelete(DeleteBehavior.Cascade);

        // A spool's history page: newest first.
        builder.HasIndex(r => new { r.SpoolId, r.RecordedAtUtc });
    }
}
