using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace OpenFarm.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddSpoolLedgerWeightAndReason : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_SpoolUsageRecords_SpoolId",
                table: "SpoolUsageRecords");

            migrationBuilder.AddColumn<string>(
                name: "Reason",
                table: "SpoolUsageRecords",
                type: "character varying(200)",
                maxLength: 200,
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "RemainingAfterGrams",
                table: "SpoolUsageRecords",
                type: "numeric",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_SpoolUsageRecords_SpoolId_RecordedAtUtc",
                table: "SpoolUsageRecords",
                columns: new[] { "SpoolId", "RecordedAtUtc" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_SpoolUsageRecords_SpoolId_RecordedAtUtc",
                table: "SpoolUsageRecords");

            migrationBuilder.DropColumn(
                name: "Reason",
                table: "SpoolUsageRecords");

            migrationBuilder.DropColumn(
                name: "RemainingAfterGrams",
                table: "SpoolUsageRecords");

            migrationBuilder.CreateIndex(
                name: "IX_SpoolUsageRecords_SpoolId",
                table: "SpoolUsageRecords",
                column: "SpoolId");
        }
    }
}
