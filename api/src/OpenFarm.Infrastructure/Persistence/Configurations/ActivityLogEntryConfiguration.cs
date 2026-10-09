using System.Text.Json;
using Finbuckle.MultiTenant.EntityFrameworkCore.Extensions;
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

        builder.HasIndex(a => new { a.OrganizationId, a.TimestampUtc });

        builder.HasIndex(a => new { a.EntityType, a.EntityId });

        // Append-only: no update or delete operation is ever mapped for this entity.
        builder.IsMultiTenant();
    }
}
