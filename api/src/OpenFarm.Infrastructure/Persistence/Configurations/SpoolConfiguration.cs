using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using OpenFarm.Domain.FilamentTracking;

namespace OpenFarm.Infrastructure.Persistence.Configurations;

public sealed class SpoolConfiguration : IEntityTypeConfiguration<Spool>
{
    public void Configure(EntityTypeBuilder<Spool> builder)
    {
        builder.ToTable("Spools");

        builder.HasKey(s => s.Id);

        builder.Property(s => s.FilamentVariantId)
            .IsRequired();

        builder.Property(s => s.Label);

        builder.Property(s => s.SpoolWeightGrams)
            .IsRequired();

        builder.Property(s => s.RemainingWeightGrams)
            .IsRequired();

        builder.Property(s => s.HasUsageOverrun)
            .IsRequired();

        builder.Property(s => s.State)
            .IsRequired()
            .HasConversion<string>();

        builder.Property(s => s.CreatedAtUtc)
            .IsRequired();

        builder.HasOne<FilamentVariant>()
            .WithMany()
            .HasForeignKey(s => s.FilamentVariantId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne<FeedSlot>()
            .WithMany()
            .HasForeignKey(s => s.FeedSlotId)
            .OnDelete(DeleteBehavior.Restrict);

        // At most one spool is ever Using a given feed slot at a time (data-model.md).
        builder.HasIndex(s => s.FeedSlotId)
            .IsUnique()
            .HasFilter("\"State\" = 'Using'");

        // The inventory list: state filter, in creation order (GetSpoolsQuery).
        builder.HasIndex(s => new { s.State, s.CreatedAtUtc });

        // Postgres system column; the Npgsql provider auto-maps a "xmin"-named uint row-version
        // property to it (research.md/data-model.md's optimistic-concurrency requirement).
        builder.Property<uint>("xmin")
            .IsRowVersion();
    }
}
