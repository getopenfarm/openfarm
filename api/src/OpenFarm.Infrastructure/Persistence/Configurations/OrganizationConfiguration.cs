using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using OpenFarm.Domain.Organizations;
using OpenFarm.Domain.Profiles;

namespace OpenFarm.Infrastructure.Persistence.Configurations;

public sealed class OrganizationConfiguration : IEntityTypeConfiguration<Organization>
{
    public void Configure(EntityTypeBuilder<Organization> builder)
    {
        builder.ToTable("Organizations");

        builder.HasKey(o => o.Id);

        builder.Property(o => o.Name)
            .IsRequired();

        builder.Property(o => o.DefaultSlicerEngine)
            .IsRequired()
            .HasConversion<string>();

        builder.Property(o => o.DefaultSlicerVersion);

        builder.Property(o => o.DefaultMachineProfileId);

        builder.Property(o => o.CreatedAtUtc)
            .IsRequired();

        // Enforced FK, matching Printer.MachineProfileId's own treatment (PrinterConfiguration).
        builder.HasOne<MachineProfile>()
            .WithMany()
            .HasForeignKey(o => o.DefaultMachineProfileId)
            .OnDelete(DeleteBehavior.SetNull);
    }
}
