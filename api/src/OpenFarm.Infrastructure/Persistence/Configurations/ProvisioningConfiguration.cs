using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using OpenFarm.Infrastructure.Identity;

namespace OpenFarm.Infrastructure.Persistence.Configurations;

public sealed class ProvisionedUserConfiguration : IEntityTypeConfiguration<ApplicationUser>
{
    public void Configure(EntityTypeBuilder<ApplicationUser> builder)
    {
        builder.Property(u => u.ProvisioningProvider).HasMaxLength(69);
        builder.Property(u => u.ProvisioningExternalId).HasMaxLength(255);
        builder.HasIndex(u => new { u.ProvisioningProvider, u.ProvisioningExternalId }).IsUnique();
    }
}
