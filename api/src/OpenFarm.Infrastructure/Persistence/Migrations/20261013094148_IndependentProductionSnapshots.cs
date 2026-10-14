using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace OpenFarm.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class IndependentProductionSnapshots : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_PartRequirements_Parts_PartId",
                table: "PartRequirements");

            migrationBuilder.DropIndex(
                name: "IX_PartRequirements_PartId",
                table: "PartRequirements");

            migrationBuilder.AddColumn<string>(
                name: "TargetName",
                table: "RunLines",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ProductionSnapshotJson",
                table: "PrintJobs",
                type: "jsonb",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "SnapshotRecoveryReason",
                table: "PrintJobs",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "LibraryPartId",
                table: "PartRequirements",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "PartName",
                table: "PartRequirements",
                type: "text",
                nullable: true);

            migrationBuilder.Sql("""
                UPDATE "PartRequirements" AS r SET "LibraryPartId" = r."PartId", "PartName" = p."Name"
                FROM "Parts" AS p WHERE r."PartId" = p."Id";
                UPDATE "RunLines" AS l SET "TargetName" = p."Name" FROM "Parts" AS p
                WHERE l."TargetKind" = 'Part' AND l."TargetId" = p."Id";
                UPDATE "RunLines" AS l SET "TargetName" = a."Name" FROM "Assemblies" AS a
                WHERE l."TargetKind" = 'Assembly' AND l."TargetId" = a."Id";
                UPDATE "RunLines" SET "RevisionMode" = CASE WHEN "Quantity" IS NULL THEN 'FollowLatest' ELSE 'Snapshot' END;
                UPDATE "PrintJobs" SET "SnapshotRecoveryReason" = 'Legacy snapshot awaiting verification of original file and metadata.';
                """);

            migrationBuilder.CreateIndex(
                name: "IX_PartRequirements_LibraryPartId",
                table: "PartRequirements",
                column: "LibraryPartId");

            migrationBuilder.AddForeignKey(
                name: "FK_PartRequirements_Parts_LibraryPartId",
                table: "PartRequirements",
                column: "LibraryPartId",
                principalTable: "Parts",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_PartRequirements_Parts_LibraryPartId",
                table: "PartRequirements");

            migrationBuilder.DropIndex(
                name: "IX_PartRequirements_LibraryPartId",
                table: "PartRequirements");

            migrationBuilder.DropColumn(
                name: "TargetName",
                table: "RunLines");

            migrationBuilder.DropColumn(
                name: "ProductionSnapshotJson",
                table: "PrintJobs");

            migrationBuilder.DropColumn(
                name: "SnapshotRecoveryReason",
                table: "PrintJobs");

            migrationBuilder.DropColumn(
                name: "LibraryPartId",
                table: "PartRequirements");

            migrationBuilder.DropColumn(
                name: "PartName",
                table: "PartRequirements");

            migrationBuilder.CreateIndex(
                name: "IX_PartRequirements_PartId",
                table: "PartRequirements",
                column: "PartId");

            migrationBuilder.AddForeignKey(
                name: "FK_PartRequirements_Parts_PartId",
                table: "PartRequirements",
                column: "PartId",
                principalTable: "Parts",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }
    }
}
