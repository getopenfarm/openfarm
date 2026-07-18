using Finbuckle.MultiTenant.EntityFrameworkCore.Extensions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using OpenFarm.Domain.FilamentTracking;

namespace OpenFarm.Infrastructure.Persistence.Configurations;

public sealed class FeedUnitConfiguration : IEntityTypeConfiguration<FeedUnit>
{
    public void Configure(EntityTypeBuilder<FeedUnit> builder)
    {
        builder.ToTable("FeedUnits");

        builder.HasKey(u => u.Id);

        builder.Property(u => u.OrganizationId)
            .IsRequired();

        builder.Property(u => u.PrinterId)
            .IsRequired();

        // Open, extensible tag — deliberately not a Postgres enum (research.md §2).
        builder.Property(u => u.Kind)
            .IsRequired();

        builder.Property(u => u.UnitIndex)
            .IsRequired();

        builder.Property(u => u.Name);

        builder.Property(u => u.ReplacesFeedUnitId);

        builder.Property(u => u.IsRevealed)
            .IsRequired();

        builder.Property(u => u.CreatedAtUtc)
            .IsRequired();

        builder.HasOne<FeedUnit>()
            .WithMany()
            .HasForeignKey(u => u.ReplacesFeedUnitId)
            .OnDelete(DeleteBehavior.SetNull);

        builder.HasMany(u => u.FeedSlots)
            .WithOne()
            .HasForeignKey(s => s.FeedUnitId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.Navigation(u => u.FeedSlots)
            .UsePropertyAccessMode(PropertyAccessMode.Field);

        builder.HasIndex(u => new { u.PrinterId, u.Kind, u.UnitIndex })
            .IsUnique();

        builder.IsMultiTenant();
    }
}
