using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace OpenFarm.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class ConfigureReloaderChannels : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_FeedUnits_ReplacesFeedUnitId",
                table: "FeedUnits");

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "ArchivedAtUtc",
                table: "FeedUnits",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "FeedingSelectionSource",
                table: "FeedUnits",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "FeedingSlotId",
                table: "FeedUnits",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<uint>(
                name: "xmin",
                table: "FeedUnits",
                type: "xid",
                rowVersion: true,
                nullable: false,
                defaultValue: 0u);

            migrationBuilder.Sql("""
                UPDATE "FeedUnits" AS u SET "ArchivedAtUtc" = COALESCE(
                    (SELECT MAX(s."DeactivatedAtUtc") FROM "FeedSlots" s WHERE s."FeedUnitId" = u."Id"), NOW())
                WHERE u."Kind" = 'AutomaticFilamentReloader'
                    AND NOT EXISTS (SELECT 1 FROM "FeedSlots" s WHERE s."FeedUnitId" = u."Id" AND s."DeactivatedAtUtc" IS NULL);

                UPDATE "Spools" SET "State" = 'InStorage', "FeedSlotId" = NULL
                WHERE "State" = 'Using' AND "FeedSlotId" IN (
                    SELECT s."Id" FROM "FeedSlots" s JOIN "FeedUnits" u ON u."Id" = s."FeedUnitId"
                    WHERE u."Kind" = 'AutomaticFilamentReloader' AND u."ArchivedAtUtc" IS NOT NULL);

                WITH claims AS (
                    SELECT "Id", ROW_NUMBER() OVER (PARTITION BY "ReplacesFeedUnitId" ORDER BY "CreatedAtUtc", "Id") AS rank
                    FROM "FeedUnits" WHERE "Kind" = 'AutomaticFilamentReloader'
                        AND "ArchivedAtUtc" IS NULL AND "ReplacesFeedUnitId" IS NOT NULL
                )
                UPDATE "FeedUnits" SET "ReplacesFeedUnitId" = NULL
                WHERE "Id" IN (SELECT "Id" FROM claims WHERE rank > 1);
                """);

            migrationBuilder.CreateIndex(
                name: "IX_FeedUnits_ReplacesFeedUnitId",
                table: "FeedUnits",
                column: "ReplacesFeedUnitId",
                unique: true,
                filter: "\"ArchivedAtUtc\" IS NULL AND \"Kind\" = 'AutomaticFilamentReloader'");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_FeedUnits_ReplacesFeedUnitId",
                table: "FeedUnits");

            migrationBuilder.DropColumn(
                name: "ArchivedAtUtc",
                table: "FeedUnits");

            migrationBuilder.DropColumn(
                name: "FeedingSelectionSource",
                table: "FeedUnits");

            migrationBuilder.DropColumn(
                name: "FeedingSlotId",
                table: "FeedUnits");

            migrationBuilder.DropColumn(
                name: "xmin",
                table: "FeedUnits");

            migrationBuilder.CreateIndex(
                name: "IX_FeedUnits_ReplacesFeedUnitId",
                table: "FeedUnits",
                column: "ReplacesFeedUnitId");
        }
    }
}
