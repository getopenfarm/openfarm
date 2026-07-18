using System.Text.Json;
using Finbuckle.MultiTenant.EntityFrameworkCore.Extensions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using OpenFarm.Domain.Printers;

namespace OpenFarm.Infrastructure.Persistence.Configurations;

public sealed class PrinterConfiguration : IEntityTypeConfiguration<Printer>
{
    public void Configure(EntityTypeBuilder<Printer> builder)
    {
        builder.ToTable("Printers");

        builder.HasKey(p => p.Id);

        builder.Property(p => p.Name)
            .IsRequired();

        builder.Property(p => p.Brand)
            .IsRequired();

        builder.Property(p => p.ModelCode)
            .IsRequired();

        builder.Property(p => p.IpAddress)
            .IsRequired();

        builder.Property(p => p.SerialNumber)
            .IsRequired();

        builder.Property(p => p.Status)
            .IsRequired()
            .HasConversion<string>();

        builder.Property(p => p.IsReachable)
            .IsRequired();

        builder.Property(p => p.LastMessageUtc);

        builder.Property(p => p.NozzleTempC);
        builder.Property(p => p.BedTempC);
        builder.Property(p => p.PrintProgressPercent);
        builder.Property(p => p.CurrentStageLabel);

        builder.Property(p => p.AmsUnits)
            .HasColumnType("jsonb")
            .HasConversion(
                units => JsonSerializer.Serialize(units, (JsonSerializerOptions?)null),
                json => JsonSerializer.Deserialize<IReadOnlyList<AmsUnit>>(json, (JsonSerializerOptions?)null) ?? Array.Empty<AmsUnit>())
            .Metadata.SetValueComparer(new ValueComparer<IReadOnlyList<AmsUnit>>(
                (left, right) => (left ?? Array.Empty<AmsUnit>()).SequenceEqual(right ?? Array.Empty<AmsUnit>()),
                units => units.Aggregate(0, (hash, unit) => HashCode.Combine(hash, unit.GetHashCode())),
                units => units.ToList()));

        builder.Property(p => p.ExternalSpoolTray)
            .HasColumnType("jsonb")
            .HasConversion(
                tray => JsonSerializer.Serialize(tray, (JsonSerializerOptions?)null),
                json => JsonSerializer.Deserialize<AmsTray?>(json, (JsonSerializerOptions?)null));

        builder.Property(p => p.CreatedAtUtc)
            .IsRequired();

        // Serial number is the natural key ConnectionSecret is looked up by (data-model.md) — must
        // stay unique per organization.
        builder.HasIndex(p => new { p.OrganizationId, p.SerialNumber })
            .IsUnique();

        builder.IsMultiTenant();
    }
}
