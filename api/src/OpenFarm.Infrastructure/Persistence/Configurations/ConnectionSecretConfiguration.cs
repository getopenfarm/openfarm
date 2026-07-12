using Finbuckle.MultiTenant.EntityFrameworkCore.Extensions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using OpenFarm.Domain.ConnectionSecrets;

namespace OpenFarm.Infrastructure.Persistence.Configurations;

public sealed class ConnectionSecretConfiguration : IEntityTypeConfiguration<ConnectionSecret>
{
    public void Configure(EntityTypeBuilder<ConnectionSecret> builder)
    {
        builder.ToTable("ConnectionSecrets");

        builder.HasKey(c => c.Id);

        builder.Property(c => c.OrganizationId)
            .IsRequired();

        builder.Property(c => c.PrinterSerialNumber)
            .IsRequired();

        builder.Property(c => c.EncryptedAccessCode)
            .IsRequired();

        builder.Property(c => c.CreatedAtUtc)
            .IsRequired();

        // Immutable, append-only: no update-path columns/navigations are mapped beyond creation.
        builder.IsMultiTenant();
    }
}
