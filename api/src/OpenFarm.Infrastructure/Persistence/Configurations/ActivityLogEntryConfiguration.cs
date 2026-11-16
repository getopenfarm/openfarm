using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using OpenFarm.Domain.ActivityLog;

namespace OpenFarm.Infrastructure.Persistence.Configurations;

public sealed class ActivityLogEntryConfiguration : IEntityTypeConfiguration<ActivityLogEntry>
{
    public void Configure(EntityTypeBuilder<ActivityLogEntry> builder)
    {
        builder.ToTable("ActivityLogEntries");

        builder.HasKey(a => a.Id);

        builder.Property(a => a.EntityType)
            .IsRequired();

        builder.Property(a => a.EntityId)
            .IsRequired();

        builder.Property(a => a.EventType)
            .IsRequired();

        builder.Property(a => a.Details)
            .HasColumnType("jsonb")
            .HasConversion(
                details => JsonSerializer.Serialize(details, (JsonSerializerOptions?)null),
                json => JsonSerializer.Deserialize<ActivityLogDetails>(json, (JsonSerializerOptions?)null));

        builder.Property(a => a.ActorId);

        builder.Property(a => a.ActorType)
            .IsRequired()
            .HasConversion<string>();

        builder.Property(a => a.TimestampUtc)
            .IsRequired();

        builder.HasIndex(a => a.TimestampUtc);

        builder.HasIndex(a => new { a.EntityType, a.EntityId });

        builder.Property(a => a.RelatedPrinterIds).HasColumnType("uuid[]").HasDefaultValueSql("'{}'::uuid[]");
        builder.HasIndex(a => a.RelatedPrinterIds).HasMethod("gin");
        builder.Property(a => a.Category).IsRequired().HasDefaultValue("Audit");
        builder.Property(a => a.ChangesJson).HasColumnType("jsonb");
        builder.HasIndex(a => new { a.Category, a.TimestampUtc, a.Id }).IsDescending(false, true, false);
        builder.HasIndex(a => new { a.EntityType, a.EntityId, a.TimestampUtc, a.Id }).IsDescending(false, false, true, false);
        builder.HasIndex(a => new { a.ActorId, a.TimestampUtc, a.Id }).IsDescending(false, true, false);
        builder.HasIndex(a => new { a.Channel, a.TimestampUtc, a.Id }).IsDescending(false, true, false);
        builder.HasIndex(a => a.CorrelationId);
        builder.HasIndex(a => new { a.CredentialId, a.TimestampUtc, a.Id }).IsDescending(false, true, false);

        // Append-only: no update or delete operation is ever mapped for this entity.
    }
}
