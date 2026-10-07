using System.Text.Json;
using Finbuckle.MultiTenant.EntityFrameworkCore.Extensions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using OpenFarm.Domain.Profiles;

namespace OpenFarm.Infrastructure.Persistence.Configurations;

public sealed class ProcessProfileConfiguration : IEntityTypeConfiguration<ProcessProfile>
{
    public void Configure(EntityTypeBuilder<ProcessProfile> builder)
    {
        builder.ToTable("ProcessProfiles");

        builder.HasKey(p => p.Id);

        builder.Property(p => p.Name)
            .IsRequired();

        builder.Property(p => p.SourceFormat)
            .IsRequired()
            .HasConversion<string>();

        builder.Property(p => p.BaseProfileName);

        builder.Property(p => p.RawDataJson)
            .IsRequired()
            .HasColumnType("jsonb");

        builder.Property(p => p.CreatedAtUtc)
            .IsRequired();

        builder.Property(p => p.Origin)
            .IsRequired()
            .HasConversion<string>();

        builder.Property(p => p.VendorName);
        builder.Property(p => p.ExternalId);
        builder.Property(p => p.IsCurrentlyOffered).IsRequired();
        builder.Property(p => p.IsSelectable).IsRequired();

        builder.Property(p => p.CompatibleMachineNames)
            .HasColumnType("jsonb")
            .HasConversion(
                names => JsonSerializer.Serialize(names, (JsonSerializerOptions?)null),
                json => JsonSerializer.Deserialize<IReadOnlyList<string>?>(json, (JsonSerializerOptions?)null))
            .Metadata.SetValueComparer(new ValueComparer<IReadOnlyList<string>?>(
                (left, right) => (left ?? Array.Empty<string>()).SequenceEqual(right ?? Array.Empty<string>()),
                names => (names ?? Array.Empty<string>()).Aggregate(0, (hash, name) => HashCode.Combine(hash, name.GetHashCode())),
                names => names == null ? null : names.ToList()));

        // No delete endpoint exists yet — ON DELETE SET NULL is a defensive default for whenever
        // one is added, matching MachineProfile/FilamentProfile's own BaseProfileId treatment.
        builder.HasOne<ProcessProfile>()
            .WithMany()
            .HasForeignKey(p => p.BaseProfileId)
            .OnDelete(DeleteBehavior.SetNull);

        // Research.md §1 idempotency keys — same shape as MachineProfile/FilamentProfile. Every
        // ProcessProfile row is System-origin after 009 (FR-011), but the index stays partial for
        // consistency and to tolerate any pre-existing interim-era User row (migration default).
        builder.HasIndex(p => new { p.OrganizationId, p.SourceFormat, p.VendorName, p.ExternalId })
            .IsUnique()
            .HasFilter("\"Origin\" = 'System' AND \"ExternalId\" IS NOT NULL");

        builder.HasIndex(p => new { p.OrganizationId, p.SourceFormat, p.VendorName, p.Name })
            .IsUnique()
            .HasFilter("\"Origin\" = 'System' AND \"ExternalId\" IS NULL");

        builder.IsMultiTenant();
    }
}
