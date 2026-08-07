using Finbuckle.MultiTenant.EntityFrameworkCore.Extensions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using OpenFarm.Domain.Assemblies;

namespace OpenFarm.Infrastructure.Persistence.Configurations;

public sealed class NonPrintedComponentConfiguration : IEntityTypeConfiguration<NonPrintedComponent>
{
    public void Configure(EntityTypeBuilder<NonPrintedComponent> builder)
    {
        builder.ToTable("NonPrintedComponents");

        builder.HasKey(c => c.Id);

        builder.Property(c => c.OrganizationId).IsRequired();
        builder.Property(c => c.Name).IsRequired();
        builder.Property(c => c.Unit).IsRequired();
        builder.Property(c => c.UnitCost);
        builder.Property(c => c.CreatedAtUtc).IsRequired();
        builder.Property(c => c.UpdatedAtUtc).IsRequired();

        builder.Property<uint>("xmin").IsRowVersion();

        builder.IsMultiTenant();
    }
}
