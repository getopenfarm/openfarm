using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using OpenFarm.Domain.Profiles;

namespace OpenFarm.Infrastructure.Persistence.Configurations;

public sealed class MachineProfileConfiguration : IEntityTypeConfiguration<MachineProfile>
{
    public void Configure(EntityTypeBuilder<MachineProfile> builder)
    {
        builder.ToTable("MachineProfiles", table => table.HasCheckConstraint(
            "CK_MachineProfiles_SystemHasVersion", "(\"Origin\" = 'System') = (\"SourceVersion\" IS NOT NULL)"));

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
        builder.Property(p => p.SourceVersion);
        builder.Property(p => p.IsCurrentlyOffered).IsRequired();
        builder.Property(p => p.IsSelectable).IsRequired();
        builder.Property(p => p.VendorMachineModelName);

        builder.Property(p => p.IncludedTemplateNames)
            .HasColumnType("jsonb")
            .HasConversion(
                names => JsonSerializer.Serialize(names, (JsonSerializerOptions?)null),
                json => JsonSerializer.Deserialize<IReadOnlyList<string>?>(json, (JsonSerializerOptions?)null))
            .Metadata.SetValueComparer(new ValueComparer<IReadOnlyList<string>?>(
                (left, right) => (left ?? Array.Empty<string>()).SequenceEqual(right ?? Array.Empty<string>()),
                names => (names ?? Array.Empty<string>()).Aggregate(0, (hash, name) => HashCode.Combine(hash, name.GetHashCode())),
                names => names == null ? null : names.ToList()));

        // No delete endpoint exists yet (FR-001–015 name no deletion capability) — ON DELETE SET
        // NULL is a defensive default for whenever one is added (data-model.md).
        builder.HasOne<MachineProfile>()
            .WithMany()
            .HasForeignKey(p => p.BaseProfileId)
            .OnDelete(DeleteBehavior.SetNull);

        // Research.md §1 idempotency keys — leaf rows keyed by (VendorName, ExternalId), abstract
        // bases (no ExternalId) keyed by (VendorName, Name). Both partial, System-only.
        // Per-version catalogs: a System row is unique per (engine, slicer version, vendor, identity). A System row
        // always has a version (CK_*_SystemHasVersion), so the keys never contain NULL.
        builder.HasIndex(p => new { p.SourceFormat, p.SourceVersion, p.VendorName, p.ExternalId })
            .IsUnique()
            .HasFilter("\"Origin\" = 'System' AND \"ExternalId\" IS NOT NULL");

        builder.HasIndex(p => new { p.SourceFormat, p.SourceVersion, p.VendorName, p.Name })
            .IsUnique()
            .HasFilter("\"Origin\" = 'System' AND \"ExternalId\" IS NULL");

        // No uniqueness constraint on Name for User rows — FR-009 permits duplicate-named imports as
        // separate rows (data-model.md's Validation rules).
    }
}
