using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using OpenFarm.Domain.FilamentTracking;
using OpenFarm.Domain.Production;

namespace OpenFarm.Infrastructure.Persistence.Configurations;

public sealed class ExternalPrintConfiguration : IEntityTypeConfiguration<ExternalPrint>
{
    public void Configure(EntityTypeBuilder<ExternalPrint> builder)
    {
        builder.ToTable("ExternalPrints");
        builder.Property(p => p.ObservedSlots)
            .HasColumnType("jsonb")
            .HasConversion(
                slots => JsonSerializer.Serialize(slots, (JsonSerializerOptions?)null),
                json => JsonSerializer.Deserialize<FeedSlotRef[]>(json, (JsonSerializerOptions?)null) ?? Array.Empty<FeedSlotRef>())
            .Metadata.SetValueComparer(new ValueComparer<FeedSlotRef[]>(
                (left, right) => (left ?? Array.Empty<FeedSlotRef>()).SequenceEqual(right ?? Array.Empty<FeedSlotRef>()),
                slots => slots.Aggregate(0, (hash, slot) => HashCode.Combine(hash, slot.GetHashCode())),
                slots => slots.ToArray()));
        builder.Property(p => p.ObservedSpoolIds);
    }
}
