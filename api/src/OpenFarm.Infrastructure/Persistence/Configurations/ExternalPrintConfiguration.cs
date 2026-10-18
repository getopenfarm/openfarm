using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using OpenFarm.Domain.Production;

namespace OpenFarm.Infrastructure.Persistence.Configurations;

public sealed class ExternalPrintConfiguration : IEntityTypeConfiguration<ExternalPrint>
{
    public void Configure(EntityTypeBuilder<ExternalPrint> builder)
    {
        builder.ToTable("ExternalPrints");
        builder.Property(p => p.ObservedTrayIds);
        builder.Property(p => p.ObservedSpoolIds);
    }
}
