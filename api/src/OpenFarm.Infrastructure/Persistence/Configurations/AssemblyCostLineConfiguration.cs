using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using OpenFarm.Domain.Assemblies;

namespace OpenFarm.Infrastructure.Persistence.Configurations;

public sealed class AssemblyCostLineConfiguration : IEntityTypeConfiguration<AssemblyCostLine>
{
    public void Configure(EntityTypeBuilder<AssemblyCostLine> builder)
    {
        builder.ToTable("AssemblyCostLines");
        builder.HasKey(l => l.Id);
        builder.Ignore(l => l.Input);
        builder.HasOne<NonPrintedComponent>().WithMany().HasForeignKey(l => l.CatalogItemId).OnDelete(DeleteBehavior.Restrict);
        builder.HasIndex(l => new { l.AssemblyId, l.CatalogItemId }).IsUnique();
    }
}
