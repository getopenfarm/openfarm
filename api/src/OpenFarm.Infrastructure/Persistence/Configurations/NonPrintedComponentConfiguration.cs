using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using OpenFarm.Domain.Assemblies;

namespace OpenFarm.Infrastructure.Persistence.Configurations;

public sealed class NonPrintedComponentConfiguration : IEntityTypeConfiguration<NonPrintedComponent>
{
    public void Configure(EntityTypeBuilder<NonPrintedComponent> builder)
    {
        builder.ToTable("NonPrintedComponents");

        builder.Ignore(c => c.Pricing);
        builder.Ignore(c => c.IsLegacyCompatible);
        builder.Property(c => c.ItemType).HasDefaultValue("Physical");
        builder.Property(c => c.Category).HasDefaultValue("Hardware");
        builder.Property(c => c.Unit).HasDefaultValue("Piece");
        builder.Property(c => c.PricingMode).HasDefaultValue("Direct");
        builder.Property(c => c.LengthUnit).HasDefaultValue("m");
        builder.Property(c => c.WidthUnit).HasDefaultValue("m");
        builder.HasKey(c => c.Id);
        builder.Property(c => c.Name).IsRequired();
        builder.Property(c => c.UnitCost);
        builder.Property(c => c.CreatedAtUtc).IsRequired();
        builder.Property(c => c.UpdatedAtUtc).IsRequired();

        builder.Property<uint>("xmin").IsRowVersion();
    }
}
