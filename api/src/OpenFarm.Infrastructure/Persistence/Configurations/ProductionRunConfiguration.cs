using Finbuckle.MultiTenant.EntityFrameworkCore.Extensions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using OpenFarm.Domain.Production;

namespace OpenFarm.Infrastructure.Persistence.Configurations;

public sealed class ProductionRunConfiguration : IEntityTypeConfiguration<ProductionRun>
{
    public void Configure(EntityTypeBuilder<ProductionRun> builder)
    {
        builder.ToTable("ProductionRuns");

        builder.HasKey(r => r.Id);

        builder.Property(r => r.OrganizationId).IsRequired();
        builder.Property(r => r.Name);
        builder.Property(r => r.DueDate);
        builder.Property(r => r.Priority);
        builder.Property(r => r.Notes);
        builder.Property(r => r.Status).IsRequired().HasConversion<string>();
        builder.Property(r => r.CreatedAtUtc).IsRequired();
        builder.Property(r => r.UpdatedAtUtc).IsRequired();

        builder.HasMany(r => r.Lines)
            .WithOne()
            .HasForeignKey(l => l.ProductionRunId)
            .OnDelete(DeleteBehavior.Cascade);
        builder.Navigation(r => r.Lines).UsePropertyAccessMode(PropertyAccessMode.Field);

        // Postgres system column; the Npgsql provider auto-maps a "xmin"-named uint row-version
        // property to it (data-model.md's optimistic-concurrency requirement, mirrors AssemblyConfiguration).
        builder.Property<uint>("xmin").IsRowVersion();

        builder.IsMultiTenant();
    }
}
