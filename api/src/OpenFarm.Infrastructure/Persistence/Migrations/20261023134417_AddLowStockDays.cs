using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace OpenFarm.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddLowStockDays : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "LowStockDays",
                table: "FilamentVariants",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "LowStockDays",
                table: "Filaments",
                type: "integer",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "LowStockDays",
                table: "FilamentVariants");

            migrationBuilder.DropColumn(
                name: "LowStockDays",
                table: "Filaments");
        }
    }
}
