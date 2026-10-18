using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using OpenFarm.Domain.FilamentTracking;

namespace OpenFarm.Infrastructure.Persistence.Configurations;

public sealed class FeedSlotConfiguration : IEntityTypeConfiguration<FeedSlot>
{
    public void Configure(EntityTypeBuilder<FeedSlot> builder)
    {
        builder.ToTable("FeedSlots");

        builder.HasKey(s => s.Id);

        builder.Property(s => s.SlotPosition)
            .IsRequired();

        builder.Property(s => s.CreatedAtUtc)
            .IsRequired();

        builder.HasIndex(s => new { s.FeedUnitId, s.SlotPosition })
            .IsUnique();
    }
}
