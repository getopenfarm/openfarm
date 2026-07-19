using System;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using OpenFarm.Infrastructure.Persistence;

#nullable disable

namespace OpenFarm.Infrastructure.Persistence.Migrations
{
    // See AddPartsCatalog's header comment — attributed directly, no separate Designer.cs (no dotnet
    // SDK access in this sandbox; regenerate via `dotnet ef migrations add` for design-time tooling).
    [DbContext(typeof(OpenFarmDbContext))]
    [Migration("20260719150100_AddPrinterMachineProfileLink")]
    public partial class AddPrinterMachineProfileLink : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "MachineProfileId",
                table: "Printers",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_Printers_MachineProfileId",
                table: "Printers",
                column: "MachineProfileId");

            migrationBuilder.AddForeignKey(
                name: "FK_Printers_MachineProfiles_MachineProfileId",
                table: "Printers",
                column: "MachineProfileId",
                principalTable: "MachineProfiles",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Printers_MachineProfiles_MachineProfileId",
                table: "Printers");

            migrationBuilder.DropIndex(
                name: "IX_Printers_MachineProfileId",
                table: "Printers");

            migrationBuilder.DropColumn(
                name: "MachineProfileId",
                table: "Printers");
        }
    }
}
