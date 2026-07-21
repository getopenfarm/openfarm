using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using OpenFarm.Infrastructure.Persistence;

#nullable disable

namespace OpenFarm.Infrastructure.Persistence.Migrations
{
    // See AddPartsCatalog's header comment — attributed directly, no separate Designer.cs (no dotnet
    // SDK access in this sandbox; regenerate via `dotnet ef migrations add` for design-time tooling).
    [DbContext(typeof(OpenFarmDbContext))]
    [Migration("20260719150200_AddOrganizationSlicingDefaults")]
    public partial class AddOrganizationSlicingDefaults : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "DefaultSlicerEngine",
                table: "Organizations",
                type: "text",
                nullable: false,
                defaultValue: "OrcaSlicer");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "DefaultSlicerEngine",
                table: "Organizations");
        }
    }
}
