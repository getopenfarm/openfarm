using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace OpenFarm.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddPrinters : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "Printers",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    OrganizationId = table.Column<Guid>(type: "uuid", nullable: false),
                    Name = table.Column<string>(type: "text", nullable: false),
                    Brand = table.Column<string>(type: "text", nullable: false),
                    ModelCode = table.Column<string>(type: "text", nullable: false),
                    IpAddress = table.Column<string>(type: "text", nullable: false),
                    SerialNumber = table.Column<string>(type: "text", nullable: false),
                    Status = table.Column<string>(type: "text", nullable: false),
                    IsReachable = table.Column<bool>(type: "boolean", nullable: false),
                    LastMessageUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    NozzleTempC = table.Column<decimal>(type: "numeric", nullable: true),
                    BedTempC = table.Column<decimal>(type: "numeric", nullable: true),
                    PrintProgressPercent = table.Column<int>(type: "integer", nullable: true),
                    CurrentStageLabel = table.Column<string>(type: "text", nullable: true),
                    AmsUnits = table.Column<string>(type: "jsonb", nullable: false),
                    CreatedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    TenantId = table.Column<string>(type: "text", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Printers", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Printers_OrganizationId_SerialNumber",
                table: "Printers",
                columns: new[] { "OrganizationId", "SerialNumber" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "Printers");
        }
    }
}
