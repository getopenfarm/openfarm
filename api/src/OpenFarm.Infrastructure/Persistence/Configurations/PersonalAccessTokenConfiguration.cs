using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using OpenFarm.Infrastructure.Identity;

namespace OpenFarm.Infrastructure.Persistence.Configurations;

public sealed class PersonalAccessTokenConfiguration : IEntityTypeConfiguration<PersonalAccessToken>
{
    public void Configure(EntityTypeBuilder<PersonalAccessToken> builder)
    {
        builder.HasKey(t => t.Id);
        builder.Property(t => t.Name).HasMaxLength(100);
        builder.Property(t => t.CredentialHash).HasMaxLength(64);
        builder.Property(t => t.LastFour).HasMaxLength(4);
        builder.HasIndex(t => t.CredentialHash).IsUnique();
        builder.HasIndex(t => new { t.UserId, t.Name }).IsUnique().HasFilter("\"RevokedAtUtc\" IS NULL");
        builder.HasOne<ApplicationUser>().WithMany().HasForeignKey(t => t.UserId).OnDelete(DeleteBehavior.Cascade);
    }
}
