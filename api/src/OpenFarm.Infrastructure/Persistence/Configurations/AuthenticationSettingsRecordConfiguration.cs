using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using OpenFarm.Infrastructure.Identity;

namespace OpenFarm.Infrastructure.Persistence.Configurations;

public sealed class AuthenticationSettingsRecordConfiguration : IEntityTypeConfiguration<AuthenticationSettingsRecord>
{
    public void Configure(EntityTypeBuilder<AuthenticationSettingsRecord> builder)
    {
        builder.HasKey(s => s.Id);
        builder.Property(s => s.Revision).IsConcurrencyToken();
        builder.Property(s => s.ProtectedConfiguration).IsRequired();
    }
}

public sealed class OidcLogoutReceiptConfiguration : IEntityTypeConfiguration<OidcLogoutReceipt>
{
    public void Configure(EntityTypeBuilder<OidcLogoutReceipt> builder)
    {
        builder.HasKey(s => s.Id);
        builder.Property(s => s.Id).HasMaxLength(64);
        builder.Property(s => s.Provider).HasMaxLength(69);
        builder.Property(s => s.Subject).HasMaxLength(255);
        builder.Property(s => s.SessionId).HasMaxLength(255);
        builder.HasIndex(s => new { s.Provider, s.SessionId });
        builder.HasIndex(s => s.ExpiresAtUtc);
    }
}
