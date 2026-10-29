using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using OpenFarm.Domain.Printers;

namespace OpenFarm.Infrastructure.Persistence.Configurations;

public sealed class PrinterModelCostConfiguration : IEntityTypeConfiguration<PrinterModelCost>
{
    public void Configure(EntityTypeBuilder<PrinterModelCost> builder)
    {
        builder.ToTable("PrinterModelCosts", t =>
        {
            t.HasCheckConstraint("CK_PrinterModelCosts_Values", "\"PrinterPurchasePrice\" >= 0 AND \"PrinterUpfrontCost\" >= 0 AND \"LifetimeServiceCost\" >= 0 AND \"LifetimePrintingHours\" > 0 AND \"AveragePrintingWatts\" >= 0");
        });
        builder.HasKey(x => new { x.Brand, x.ModelCode });
        builder.Ignore(x => x.Rates);
    }
}
