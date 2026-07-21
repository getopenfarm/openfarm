using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using OpenFarm.Infrastructure.Persistence;

#nullable disable

namespace OpenFarm.Infrastructure.Persistence.Migrations
{
    // See AddPartsCatalog's header comment — attributed directly, no separate Designer.cs (no dotnet
    // SDK access in this sandbox; regenerate via `dotnet ef migrations add` for design-time tooling).
    [DbContext(typeof(OpenFarmDbContext))]
    [Migration("20260721120000_AddSlicerVersionTracking")]
    public partial class AddSlicerVersionTracking : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "SlicerVersionUsed",
                table: "Parts",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "SlicerVersionUsed",
                table: "PartProcessingJobs",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "DefaultSlicerVersion",
                table: "Organizations",
                type: "text",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "SlicerVersionUsed",
                table: "Parts");

            migrationBuilder.DropColumn(
                name: "SlicerVersionUsed",
                table: "PartProcessingJobs");

            migrationBuilder.DropColumn(
                name: "DefaultSlicerVersion",
                table: "Organizations");
        }
    }
}
