using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace OpenFarm.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class ExternalPrintFilamentDeduction : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "CurrentGcodeFile",
                table: "Printers",
                type: "text",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "ExternalPrints",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    OrganizationId = table.Column<Guid>(type: "uuid", nullable: false),
                    PrinterId = table.Column<Guid>(type: "uuid", nullable: false),
                    JobName = table.Column<string>(type: "text", nullable: false),
                    StartedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    EndedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    Outcome = table.Column<string>(type: "text", nullable: true),
                    ObservedTrayIds = table.Column<int[]>(type: "integer[]", nullable: false),
                    ObservedSpoolIds = table.Column<Guid[]>(type: "uuid[]", nullable: false),
                    TenantId = table.Column<string>(type: "text", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ExternalPrints", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ExternalPrints_Printers_PrinterId",
                        column: x => x.PrinterId,
                        principalTable: "Printers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "ExternalPrintFilaments",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    OrganizationId = table.Column<Guid>(type: "uuid", nullable: false),
                    ExternalPrintId = table.Column<Guid>(type: "uuid", nullable: false),
                    ToolIndex = table.Column<int>(type: "integer", nullable: false),
                    MaterialType = table.Column<string>(type: "text", nullable: true),
                    ColorHex = table.Column<string>(type: "text", nullable: true),
                    SpoolId = table.Column<Guid>(type: "uuid", nullable: true),
                    EstimatedGrams = table.Column<decimal>(type: "numeric", nullable: true),
                    GramsConsumed = table.Column<decimal>(type: "numeric", nullable: true),
                    UsageCurve = table.Column<string>(type: "text", nullable: true),
                    TenantId = table.Column<string>(type: "text", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ExternalPrintFilaments", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ExternalPrintFilaments_ExternalPrints_ExternalPrintId",
                        column: x => x.ExternalPrintId,
                        principalTable: "ExternalPrints",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_ExternalPrintFilaments_Spools_SpoolId",
                        column: x => x.SpoolId,
                        principalTable: "Spools",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateIndex(
                name: "IX_ExternalPrintFilaments_ExternalPrintId",
                table: "ExternalPrintFilaments",
                column: "ExternalPrintId");

            migrationBuilder.CreateIndex(
                name: "IX_ExternalPrintFilaments_SpoolId",
                table: "ExternalPrintFilaments",
                column: "SpoolId");

            migrationBuilder.CreateIndex(
                name: "IX_ExternalPrints_PrinterId_Outcome",
                table: "ExternalPrints",
                columns: new[] { "PrinterId", "Outcome" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ExternalPrintFilaments");

            migrationBuilder.DropTable(
                name: "ExternalPrints");

            migrationBuilder.DropColumn(
                name: "CurrentGcodeFile",
                table: "Printers");
        }
    }
}
