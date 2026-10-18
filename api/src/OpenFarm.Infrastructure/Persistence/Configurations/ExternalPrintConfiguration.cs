using Finbuckle.MultiTenant.EntityFrameworkCore.Extensions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using OpenFarm.Domain.FilamentTracking;
using OpenFarm.Domain.Printers;
using OpenFarm.Domain.Production;

namespace OpenFarm.Infrastructure.Persistence.Configurations;

public sealed class ExternalPrintConfiguration : IEntityTypeConfiguration<ExternalPrint>
{
    public void Configure(EntityTypeBuilder<ExternalPrint> builder)
    {
        builder.ToTable("ExternalPrints");
        builder.HasKey(p => p.Id);
        builder.Property(p => p.OrganizationId).IsRequired();
        builder.Property(p => p.PrinterId).IsRequired();
        builder.Property(p => p.JobName).IsRequired();
        builder.Property(p => p.StartedAtUtc).IsRequired();
        builder.Property(p => p.EndedAtUtc);
        builder.Property(p => p.Outcome).HasConversion<string>();
        builder.Property(p => p.ObservedTrayIds);
        builder.Property(p => p.ObservedSpoolIds);

        builder.HasOne<Printer>().WithMany().HasForeignKey(p => p.PrinterId).OnDelete(DeleteBehavior.Cascade);
        builder.HasMany(p => p.Filaments).WithOne().HasForeignKey(f => f.ExternalPrintId).OnDelete(DeleteBehavior.Cascade);
        builder.Navigation(p => p.Filaments).UsePropertyAccessMode(PropertyAccessMode.Field);
        builder.HasIndex(p => new { p.PrinterId, p.Outcome });

        builder.IsMultiTenant();
    }
}

public sealed class ExternalPrintFilamentConfiguration : IEntityTypeConfiguration<ExternalPrintFilament>
{
    public void Configure(EntityTypeBuilder<ExternalPrintFilament> builder)
    {
        builder.ToTable("ExternalPrintFilaments");
        builder.HasKey(f => f.Id);
        builder.Property(f => f.OrganizationId).IsRequired();
        builder.Property(f => f.ExternalPrintId).IsRequired();
        builder.Property(f => f.ToolIndex);
        builder.Property(f => f.MaterialType);
        builder.Property(f => f.ColorHex);
        builder.Property(f => f.SpoolId);
        builder.Property(f => f.GramsConsumed).HasColumnType("numeric");
        builder.Property(f => f.EstimatedGrams).HasColumnType("numeric");
        builder.Property(f => f.UsageCurve).HasConversion(
            curve => curve == null ? null : curve.ToJson(), json => FilamentUsageCurve.FromJson(json));

        builder.HasOne<Spool>().WithMany().HasForeignKey(f => f.SpoolId).OnDelete(DeleteBehavior.SetNull);

        builder.IsMultiTenant();
    }
}
