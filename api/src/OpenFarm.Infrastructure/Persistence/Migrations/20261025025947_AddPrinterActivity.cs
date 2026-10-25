using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace OpenFarm.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddPrinterActivity : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "StopAlerts",
                table: "Prints",
                type: "jsonb",
                nullable: false,
                defaultValueSql: "'[]'::jsonb");

            // Existing printers start Unknown from now: their first message after this restart opens the real stretch.
            migrationBuilder.AddColumn<string>(
                name: "Activity",
                table: "Printers",
                type: "text",
                nullable: false,
                defaultValue: "Unknown");

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "ActivitySinceUtc",
                table: "Printers",
                type: "timestamp with time zone",
                nullable: false,
                defaultValueSql: "now()");

            migrationBuilder.CreateTable(
                name: "PrinterActivitySpans",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    PrinterId = table.Column<Guid>(type: "uuid", nullable: false),
                    Activity = table.Column<string>(type: "text", nullable: false),
                    StartedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    EndedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PrinterActivitySpans", x => x.Id);
                    table.ForeignKey(
                        name: "FK_PrinterActivitySpans_Printers_PrinterId",
                        column: x => x.PrinterId,
                        principalTable: "Printers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_PrinterActivitySpans_PrinterId_EndedAtUtc",
                table: "PrinterActivitySpans",
                columns: new[] { "PrinterId", "EndedAtUtc" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "PrinterActivitySpans");

            migrationBuilder.DropColumn(
                name: "StopAlerts",
                table: "Prints");

            migrationBuilder.DropColumn(
                name: "Activity",
                table: "Printers");

            migrationBuilder.DropColumn(
                name: "ActivitySinceUtc",
                table: "Printers");
        }
    }
}
