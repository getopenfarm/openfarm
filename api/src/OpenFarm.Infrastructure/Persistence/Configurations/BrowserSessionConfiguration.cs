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
        builder.Property(s => s.ClientId).HasMaxLength(36);
        builder.Property(s => s.RedirectUri).HasMaxLength(2048);
        builder.HasIndex(s => s.ClientId);
        builder.HasOne<OAuthClient>().WithMany().HasForeignKey(s => s.ClientId).OnDelete(DeleteBehavior.Cascade);
        // A connected app always has its scopes; a browser session never has any.
        builder.ToTable(t => t.HasCheckConstraint("CK_BrowserSessions_AppScopes", "(\"ClientId\" IS NULL) = (\"Scopes\" IS NULL)"));
    }
}

public sealed class OAuthClientConfiguration : IEntityTypeConfiguration<OAuthClient>
{
    public void Configure(EntityTypeBuilder<OAuthClient> builder)
    {
        builder.HasKey(c => c.Id);
        builder.Property(c => c.Id).HasMaxLength(36);
        builder.Property(c => c.Name).HasMaxLength(100);
        builder.Property(c => c.AuthMethod).HasMaxLength(32);
        builder.Property(c => c.SecretHash).HasMaxLength(64);
        builder.HasIndex(c => c.CreatedAtUtc);
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
