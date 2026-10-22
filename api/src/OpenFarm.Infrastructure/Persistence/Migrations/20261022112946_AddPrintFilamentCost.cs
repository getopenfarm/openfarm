using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace OpenFarm.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddPrintFilamentCost : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<decimal>(
                name: "PricePerKg",
                table: "PrintFilaments",
                type: "numeric",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "PriceSource",
                table: "PrintFilaments",
                type: "text",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "PricePerKg",
                table: "PrintFilaments");

            migrationBuilder.DropColumn(
                name: "PriceSource",
                table: "PrintFilaments");
        }
    }
}
