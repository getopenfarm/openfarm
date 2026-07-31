using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using OpenFarm.Infrastructure.Persistence;

#nullable disable

namespace OpenFarm.Infrastructure.Persistence.Migrations
{
    // [Migration]/[DbContext] attributed directly here rather than in a separate Designer.cs — see
    // AddPrinterMachineProfileLink's header comment (same reasoning). OpenFarmDbContextModelSnapshot.cs
    // was regenerated for real via `dotnet ef migrations add` in this session, but the per-migration
    // Designer.cs EF produced for this specific migration turned out empty against that freshly-synced
    // snapshot (the snapshot already reflects Printer.BuildPlateType once regenerated from current
    // code) — the real, tool-verified column diff below was captured from the first (pre-snapshot-sync)
    // generation attempt and is hand-attached here instead of a second Designer.cs.
    [DbContext(typeof(OpenFarmDbContext))]
    [Migration("20260731141348_AddPrinterBuildPlate")]
    public partial class AddPrinterBuildPlate : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "BuildPlateType",
                table: "Printers",
                type: "text",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "BuildPlateType",
                table: "Printers");
        }
    }
}
