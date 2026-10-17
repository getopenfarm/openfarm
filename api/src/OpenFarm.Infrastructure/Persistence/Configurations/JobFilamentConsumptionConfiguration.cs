using Finbuckle.MultiTenant.EntityFrameworkCore.Extensions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using OpenFarm.Domain.FilamentTracking;
using OpenFarm.Domain.Production;

namespace OpenFarm.Infrastructure.Persistence.Configurations;

public sealed class JobFilamentConsumptionConfiguration : IEntityTypeConfiguration<JobFilamentConsumption>
{
    public void Configure(EntityTypeBuilder<JobFilamentConsumption> builder)
    {
        builder.ToTable("JobFilamentConsumptions");

        builder.HasKey(c => c.Id);

        builder.Property(c => c.OrganizationId).IsRequired();
        builder.Property(c => c.PrintJobId).IsRequired();
        builder.Property(c => c.MaterialType);
        builder.Property(c => c.DetectedColorHex);
        builder.Property(c => c.SpoolId);
        builder.Property(c => c.NarrowFilamentVariantId);
        builder.Property(c => c.NarrowFilamentProfileId);
        builder.Ignore(c => c.Requirement);
        builder.Property(c => c.GramsConsumed).HasColumnType("numeric");
        builder.Property(c => c.EstimatedGrams).HasColumnType("numeric");
        builder.Property(c => c.UsageCurve).HasConversion(
            curve => curve == null ? null : curve.ToJson(), json => FilamentUsageCurve.FromJson(json));

        builder.HasOne<Spool>().WithMany().HasForeignKey(c => c.SpoolId).OnDelete(DeleteBehavior.SetNull);

        builder.IsMultiTenant();
    }
}
