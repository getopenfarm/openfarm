using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace OpenFarm.Infrastructure.Persistence.Migrations;

public partial class AddFilamentPrices : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<decimal>(name: "PricePerKg", table: "Filaments", type: "numeric", nullable: true);
        migrationBuilder.AddColumn<decimal>(name: "PricePerKg", table: "FilamentVariants", type: "numeric", nullable: true);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropColumn(name: "PricePerKg", table: "FilamentVariants");
        migrationBuilder.DropColumn(name: "PricePerKg", table: "Filaments");
    }
}
