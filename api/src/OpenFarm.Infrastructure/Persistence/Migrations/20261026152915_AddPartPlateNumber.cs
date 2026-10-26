using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace OpenFarm.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddPartPlateNumber : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "PlateCount",
                table: "Parts");

            migrationBuilder.DropColumn(
                name: "PreviewPlateCount",
                table: "PartProcessingJobs");

            migrationBuilder.AddColumn<string>(
                name: "PlateName",
                table: "Parts",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "PlateNumber",
                table: "Parts",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "PreviewPlatesJson",
                table: "PartProcessingJobs",
                type: "jsonb",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "PlateName",
                table: "Parts");

            migrationBuilder.DropColumn(
                name: "PlateNumber",
                table: "Parts");

            migrationBuilder.DropColumn(
                name: "PreviewPlatesJson",
                table: "PartProcessingJobs");

            migrationBuilder.AddColumn<int>(
                name: "PlateCount",
                table: "Parts",
                type: "integer",
                nullable: false,
                defaultValue: 1);

            migrationBuilder.AddColumn<int>(
                name: "PreviewPlateCount",
                table: "PartProcessingJobs",
                type: "integer",
                nullable: true);
        }
    }
}
