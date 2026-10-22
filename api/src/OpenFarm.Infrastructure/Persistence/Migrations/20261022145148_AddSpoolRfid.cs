using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace OpenFarm.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddSpoolRfid : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "RfidId",
                table: "Spools",
                type: "character varying(64)",
                maxLength: 64,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "LinkedRfidId",
                table: "FeedSlots",
                type: "character varying(64)",
                maxLength: 64,
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_Spools_RfidId",
                table: "Spools",
                column: "RfidId",
                unique: true,
                filter: "\"RfidId\" IS NOT NULL AND \"State\" <> 'Finished'");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Spools_RfidId",
                table: "Spools");

            migrationBuilder.DropColumn(
                name: "RfidId",
                table: "Spools");

            migrationBuilder.DropColumn(
                name: "LinkedRfidId",
                table: "FeedSlots");
        }
    }
}
