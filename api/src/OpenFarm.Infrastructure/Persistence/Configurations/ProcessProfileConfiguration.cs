using Finbuckle.MultiTenant.EntityFrameworkCore.Extensions;
using Microsoft.EntityFrameworkCore;
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

        // No delete endpoint exists yet — ON DELETE SET NULL is a defensive default for whenever
        // one is added, matching MachineProfile/FilamentProfile's own BaseProfileId treatment.
        builder.HasOne<ProcessProfile>()
            .WithMany()
            .HasForeignKey(p => p.BaseProfileId)
            .OnDelete(DeleteBehavior.SetNull);

        // No uniqueness constraint on Name — matches MachineProfile/FilamentProfile, duplicate-named
        // imports are separate rows.
        builder.IsMultiTenant();
    }
}
