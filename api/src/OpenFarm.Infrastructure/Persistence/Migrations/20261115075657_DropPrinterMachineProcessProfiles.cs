using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace OpenFarm.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class DropPrinterMachineProcessProfiles : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Printers_MachineProfiles_MachineProfileId",
                table: "Printers");

            migrationBuilder.DropForeignKey(
                name: "FK_Printers_ProcessProfiles_ProcessProfileId",
                table: "Printers");

            migrationBuilder.DropIndex(
                name: "IX_Printers_MachineProfileId",
                table: "Printers");

            migrationBuilder.DropIndex(
                name: "IX_Printers_ProcessProfileId",
                table: "Printers");

            migrationBuilder.DropColumn(
                name: "MachineProfileId",
                table: "Printers");

            migrationBuilder.DropColumn(
                name: "ProcessProfileId",
                table: "Printers");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "MachineProfileId",
                table: "Printers",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "ProcessProfileId",
                table: "Printers",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_Printers_MachineProfileId",
                table: "Printers",
                column: "MachineProfileId");

            migrationBuilder.CreateIndex(
                name: "IX_Printers_ProcessProfileId",
                table: "Printers",
                column: "ProcessProfileId");

            migrationBuilder.AddForeignKey(
                name: "FK_Printers_MachineProfiles_MachineProfileId",
                table: "Printers",
                column: "MachineProfileId",
                principalTable: "MachineProfiles",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);

            migrationBuilder.AddForeignKey(
                name: "FK_Printers_ProcessProfiles_ProcessProfileId",
                table: "Printers",
                column: "ProcessProfileId",
                principalTable: "ProcessProfiles",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);
        }
    }
}
