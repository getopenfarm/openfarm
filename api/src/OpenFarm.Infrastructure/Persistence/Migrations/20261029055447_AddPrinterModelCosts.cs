using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace OpenFarm.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddPrinterModelCosts : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<decimal>(
                name: "ElectricityPricePerKwh",
                table: "FarmSettings",
                type: "numeric",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "PrinterModelCosts",
                columns: table => new
                {
                    Brand = table.Column<string>(type: "text", nullable: false),
                    ModelCode = table.Column<string>(type: "text", nullable: false),
                    PrinterPurchasePrice = table.Column<decimal>(type: "numeric", nullable: true),
                    PrinterUpfrontCost = table.Column<decimal>(type: "numeric", nullable: true),
                    LifetimeServiceCost = table.Column<decimal>(type: "numeric", nullable: true),
                    LifetimePrintingHours = table.Column<decimal>(type: "numeric", nullable: true),
                    AveragePrintingWatts = table.Column<decimal>(type: "numeric", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PrinterModelCosts", x => new { x.Brand, x.ModelCode });
                    table.CheckConstraint("CK_PrinterModelCosts_Values", "\"PrinterPurchasePrice\" >= 0 AND \"PrinterUpfrontCost\" >= 0 AND \"LifetimeServiceCost\" >= 0 AND \"LifetimePrintingHours\" > 0 AND \"AveragePrintingWatts\" >= 0");
                });
            migrationBuilder.Sql("""
                INSERT INTO "PrinterModelCosts" ("Brand", "ModelCode")
                SELECT DISTINCT "Brand", "ModelCode" FROM "Printers"
                ON CONFLICT ("Brand", "ModelCode") DO NOTHING;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "PrinterModelCosts");

            migrationBuilder.DropColumn(
                name: "ElectricityPricePerKwh",
                table: "FarmSettings");
        }
    }
}
