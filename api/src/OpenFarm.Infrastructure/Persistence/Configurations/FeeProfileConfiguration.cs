using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using OpenFarm.Domain.Settings;

namespace OpenFarm.Infrastructure.Persistence.Configurations;

public sealed class FeeProfileConfiguration : IEntityTypeConfiguration<FeeProfile>
{
    public void Configure(EntityTypeBuilder<FeeProfile> builder)
    {
        builder.ToTable("FeeProfiles");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Name).HasMaxLength(100).IsRequired();
        builder.HasIndex(x => x.Name).IsUnique();
        builder.Property(x => x.Fees)
            .HasColumnType("jsonb")
            .HasConversion(
                fees => JsonSerializer.Serialize(fees, (JsonSerializerOptions?)null),
                json => JsonSerializer.Deserialize<Fee[]>(json, (JsonSerializerOptions?)null) ?? Array.Empty<Fee>())
            .Metadata.SetValueComparer(new ValueComparer<Fee[]>(
                (left, right) => (left ?? Array.Empty<Fee>()).SequenceEqual(right ?? Array.Empty<Fee>()),
                fees => fees.Aggregate(0, (hash, fee) => HashCode.Combine(hash, fee.GetHashCode())),
                fees => fees.ToArray()));
        builder.Ignore(x => x.FeePercent);
        builder.Ignore(x => x.FixedFee);
    }
}
