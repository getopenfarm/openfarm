using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace OpenFarm.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddPrinterReportedStateAndAlerts : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "ActiveAlerts",
                table: "Printers",
                type: "jsonb",
                nullable: false,
                defaultValue: "[]");

            migrationBuilder.AddColumn<string>(
                name: "ReportedState",
                table: "Printers",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "StateObservedAtUtc",
                table: "Printers",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Details",
                table: "ActivityLogEntries",
                type: "jsonb",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "ActiveAlerts",
                table: "Printers");

            migrationBuilder.DropColumn(
                name: "ReportedState",
                table: "Printers");

            migrationBuilder.DropColumn(
                name: "StateObservedAtUtc",
                table: "Printers");

            migrationBuilder.DropColumn(
                name: "Details",
                table: "ActivityLogEntries");
        }
    }
}
