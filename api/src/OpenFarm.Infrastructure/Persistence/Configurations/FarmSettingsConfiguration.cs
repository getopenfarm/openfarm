using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using OpenFarm.Domain.Settings;

namespace OpenFarm.Infrastructure.Persistence.Configurations;

public sealed class FarmSettingsConfiguration : IEntityTypeConfiguration<FarmSettings>
{
    public void Configure(EntityTypeBuilder<FarmSettings> builder)
    {
        builder.ToTable("FarmSettings");

        builder.HasKey(s => s.Id);

        builder.Property(s => s.DefaultSlicerEngine)
            .IsRequired()
            .HasConversion<string>();

        builder.Property(s => s.DefaultSlicerVersion);
        builder.Property(s => s.EstimationMachineProfileId);
        builder.Property(s => s.EstimationProcessProfileId);
        builder.Property(s => s.EstimationFilamentProfileId);
        builder.Property(s => s.EstimationBuildPlateType).HasConversion<string>();

        builder.Property(s => s.CreatedAtUtc)
            .IsRequired();
    }
}
