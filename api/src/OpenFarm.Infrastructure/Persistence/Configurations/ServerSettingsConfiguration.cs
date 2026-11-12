using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using OpenFarm.Domain.Settings;

namespace OpenFarm.Infrastructure.Persistence.Configurations;

public sealed class ServerSettingsConfiguration : IEntityTypeConfiguration<ServerSettings>
{
    public void Configure(EntityTypeBuilder<ServerSettings> builder)
    {
        builder.ToTable("ServerSettings");
        builder.HasKey(s => s.Id);
        builder.Property(s => s.PublicUrl).HasMaxLength(2048);
        builder.Property(s => s.McpMode).HasConversion<string>().HasMaxLength(32);
        builder.Property(s => s.DeploymentFingerprint).HasMaxLength(64);
        builder.Property(s => s.Revision).IsConcurrencyToken();
    }
}
