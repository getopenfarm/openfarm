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
        builder.Property(u => u.ProvisioningGivenName).HasMaxLength(256);
        builder.Property(u => u.ProvisioningFamilyName).HasMaxLength(256);
        builder.HasIndex(u => new { u.ProvisioningProvider, u.ProvisioningExternalId }).IsUnique();
    }
}

public sealed class ProvisioningGroupConfiguration : IEntityTypeConfiguration<ProvisioningGroup>
{
    public void Configure(EntityTypeBuilder<ProvisioningGroup> builder)
    {
        builder.HasKey(g => g.Id);
        builder.Property(g => g.Provider).HasMaxLength(69);
        builder.Property(g => g.ExternalId).HasMaxLength(255);
        builder.Property(g => g.DisplayName).HasMaxLength(256);
        builder.Property(g => g.NormalizedDisplayName).HasMaxLength(256);
        builder.HasIndex(g => new { g.Provider, g.ExternalId }).IsUnique();
        builder.HasIndex(g => new { g.Provider, g.NormalizedDisplayName }).IsUnique();
    }
}

public sealed class ProvisioningGroupMemberConfiguration : IEntityTypeConfiguration<ProvisioningGroupMember>
{
    public void Configure(EntityTypeBuilder<ProvisioningGroupMember> builder)
    {
        builder.HasKey(m => new { m.GroupId, m.UserId });
        builder.HasOne<ProvisioningGroup>().WithMany().HasForeignKey(m => m.GroupId).OnDelete(DeleteBehavior.Cascade);
        builder.HasOne<ApplicationUser>().WithMany().HasForeignKey(m => m.UserId).OnDelete(DeleteBehavior.Cascade);
    }
}
