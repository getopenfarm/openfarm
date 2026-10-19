using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using OpenFarm.Domain.Assemblies;

namespace OpenFarm.Infrastructure.Persistence.Configurations;

public sealed class AssemblyConfiguration : IEntityTypeConfiguration<Assembly>
{
    public void Configure(EntityTypeBuilder<Assembly> builder)
    {
        builder.ToTable("Assemblies");

        builder.HasKey(a => a.Id);
        builder.Property(a => a.Name).IsRequired();
        builder.Property(a => a.Description);
        builder.Property(a => a.CreatedAtUtc).IsRequired();
        builder.Property(a => a.UpdatedAtUtc).IsRequired();

        builder.HasMany(a => a.Lines)
            .WithOne()
            .HasForeignKey(l => l.AssemblyId)
            .OnDelete(DeleteBehavior.Cascade);
        builder.Navigation(a => a.Lines).UsePropertyAccessMode(PropertyAccessMode.Field);

        builder.HasMany(a => a.AdditionalCosts).WithOne().HasForeignKey(l => l.AssemblyId).OnDelete(DeleteBehavior.Cascade);
        builder.Navigation(a => a.AdditionalCosts).UsePropertyAccessMode(PropertyAccessMode.Field);

        // Postgres system column; the Npgsql provider auto-maps a "xmin"-named uint row-version
        // property to it (data-model.md's optimistic-concurrency requirement, mirrors PartConfiguration).
        builder.Property<uint>("xmin").IsRowVersion();
    }
}
