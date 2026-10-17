using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace OpenFarm.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AutoFilamentDeduction : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "ActiveTrayId",
                table: "Printers",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "CurrentLayer",
                table: "Printers",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "TotalLayers",
                table: "Printers",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "EstimatedGrams",
                table: "JobFilamentConsumptions",
                type: "numeric",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "UsageCurve",
                table: "JobFilamentConsumptions",
                type: "text",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "ActiveTrayId",
                table: "Printers");

            migrationBuilder.DropColumn(
                name: "CurrentLayer",
                table: "Printers");

            migrationBuilder.DropColumn(
                name: "TotalLayers",
                table: "Printers");

            migrationBuilder.DropColumn(
                name: "EstimatedGrams",
                table: "JobFilamentConsumptions");

            migrationBuilder.DropColumn(
                name: "UsageCurve",
                table: "JobFilamentConsumptions");
        }
    }
}
