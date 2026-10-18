using System.Text.Json;
using Finbuckle.MultiTenant.EntityFrameworkCore.Extensions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using OpenFarm.Domain.Printers;
using OpenFarm.Domain.Profiles;

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

        builder.Property(p => p.ReportedNozzleDiameterMm);
        builder.Property(p => p.NozzleObservedAtUtc);
        builder.Property(p => p.NozzleConfirmed);
        builder.Property(p => p.NozzleTempC);
        builder.Property(p => p.BedTempC);
        builder.Property(p => p.PrintProgressPercent);
        builder.Property(p => p.ActiveTrayId);
        builder.Property(p => p.CurrentLayer);
        builder.Property(p => p.TotalLayers);
        builder.Property(p => p.CurrentStageLabel);
        builder.Property(p => p.RemainingTimeMinutes);
        builder.Property(p => p.CurrentJobName);
        builder.Property(p => p.CurrentGcodeFile);
        builder.Property(p => p.ReportedState);
        builder.Property(p => p.StateObservedAtUtc);
        builder.Property(p => p.ActiveAlerts)
            .HasColumnType("jsonb")
            .HasConversion(
                alerts => JsonSerializer.Serialize(alerts, (JsonSerializerOptions?)null),
                json => JsonSerializer.Deserialize<IReadOnlyList<PrinterAlert>>(json, (JsonSerializerOptions?)null) ?? Array.Empty<PrinterAlert>())
            .Metadata.SetValueComparer(new ValueComparer<IReadOnlyList<PrinterAlert>>(
                (left, right) => (left ?? Array.Empty<PrinterAlert>()).SequenceEqual(right ?? Array.Empty<PrinterAlert>()),
                alerts => alerts.Aggregate(0, (hash, alert) => HashCode.Combine(hash, alert.GetHashCode())),
                alerts => alerts.ToList()));

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

        builder.Property(p => p.MachineProfileId);
        builder.Property(p => p.ProcessProfileId);

        builder.Property(p => p.BuildPlateType)
            .HasConversion<string>();

        builder.Property(p => p.CreatedAtUtc)
            .IsRequired();

        // Serial number is the natural key ConnectionSecret is looked up by (data-model.md) — must
        // stay unique per organization.
        builder.HasIndex(p => new { p.OrganizationId, p.SerialNumber })
            .IsUnique();

        // Enforced FK, unlike PartFilamentRequirement's narrow overrides — FR-005a's nozzle-diameter
        // auto-detection is a farm-wide correctness computation with no per-part tolerance for a
        // dangling reference (research.md §6, Part Library Phase).
        builder.HasOne<MachineProfile>()
            .WithMany()
            .HasForeignKey(p => p.MachineProfileId)
            .OnDelete(DeleteBehavior.SetNull);

        // Parallel to MachineProfileId (009) — one process selection per printer, matching
        // MachineProfileId's own FK treatment.
        builder.HasOne<ProcessProfile>()
            .WithMany()
            .HasForeignKey(p => p.ProcessProfileId)
            .OnDelete(DeleteBehavior.SetNull);

        builder.IsMultiTenant();
    }
}
