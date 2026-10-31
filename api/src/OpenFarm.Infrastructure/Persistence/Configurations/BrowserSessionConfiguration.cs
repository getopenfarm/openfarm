using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using OpenFarm.Infrastructure.Identity;

namespace OpenFarm.Infrastructure.Persistence.Configurations;

public sealed class BrowserSessionConfiguration : IEntityTypeConfiguration<BrowserSession>
{
    public void Configure(EntityTypeBuilder<BrowserSession> builder)
    {
        builder.HasKey(s => s.Id);
        builder.Property(s => s.Device).HasMaxLength(256);
        builder.HasIndex(s => new { s.UserId, s.ExpiresAtUtc });
        builder.HasOne<ApplicationUser>().WithMany().HasForeignKey(s => s.UserId).OnDelete(DeleteBehavior.Cascade);
    }
}

public sealed class SessionCredentialConfiguration : IEntityTypeConfiguration<SessionCredential>
{
    public void Configure(EntityTypeBuilder<SessionCredential> builder)
    {
        builder.HasKey(c => c.Hash);
        builder.Property(c => c.Hash).HasMaxLength(64);
        builder.HasIndex(c => c.SessionId);
        builder.HasOne<BrowserSession>().WithMany().HasForeignKey(c => c.SessionId).OnDelete(DeleteBehavior.Cascade);
    }
}
