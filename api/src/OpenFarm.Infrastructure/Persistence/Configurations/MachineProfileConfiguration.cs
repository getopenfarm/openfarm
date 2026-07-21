using Finbuckle.MultiTenant.EntityFrameworkCore.Extensions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using OpenFarm.Domain.Profiles;

namespace OpenFarm.Infrastructure.Persistence.Configurations;

public sealed class MachineProfileConfiguration : IEntityTypeConfiguration<MachineProfile>
{
    public void Configure(EntityTypeBuilder<MachineProfile> builder)
    {
        builder.ToTable("MachineProfiles");

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

        builder.Property(p => p.DefaultProcessProfileId);

        // No delete endpoint exists yet (FR-001–015 name no deletion capability) — ON DELETE SET
        // NULL is a defensive default for whenever one is added (data-model.md).
        builder.HasOne<MachineProfile>()
            .WithMany()
            .HasForeignKey(p => p.BaseProfileId)
            .OnDelete(DeleteBehavior.SetNull);

        builder.HasOne<ProcessProfile>()
            .WithMany()
            .HasForeignKey(p => p.DefaultProcessProfileId)
            .OnDelete(DeleteBehavior.SetNull);

        // No uniqueness constraint on Name — FR-009 permits duplicate-named imports as separate
        // rows (data-model.md's Validation rules).
        builder.IsMultiTenant();
    }
}
