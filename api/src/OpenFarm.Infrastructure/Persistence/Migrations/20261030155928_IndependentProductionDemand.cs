using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace OpenFarm.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class IndependentProductionDemand : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_RunLines_ProductionRuns_ProductionRunId",
                table: "RunLines");

            migrationBuilder.DropIndex(
                name: "IX_PartRequirements_RunLineId_PartId",
                table: "PartRequirements");

            migrationBuilder.AlterColumn<Guid>(
                name: "ProductionRunId",
                table: "RunLines",
                type: "uuid",
                nullable: true,
                oldClrType: typeof(Guid),
                oldType: "uuid");

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "BalanceSinceUtc",
                table: "RunLines",
                type: "timestamp with time zone",
                nullable: false,
                defaultValue: new DateTimeOffset(new DateTime(1, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)));

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "CreatedAtUtc",
                table: "RunLines",
                type: "timestamp with time zone",
                nullable: false,
                defaultValue: new DateTimeOffset(new DateTime(1, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)));

            migrationBuilder.AddColumn<string>(
                name: "Mode",
                table: "RunLines",
                type: "text",
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "QuantityBasis",
                table: "RunLines",
                type: "text",
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "State",
                table: "RunLines",
                type: "text",
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<Guid>(
                name: "SubmissionId",
                table: "RunLines",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<uint>(
                name: "xmin",
                table: "RunLines",
                type: "xid",
                rowVersion: true,
                nullable: false,
                defaultValue: 0u);

            migrationBuilder.AddColumn<bool>(
                name: "RecipeActive",
                table: "PartRequirements",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<decimal>(
                name: "UnitsPerSet",
                table: "PartRequirements",
                type: "numeric",
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.Sql("""
                UPDATE "RunLines" l SET
                    "Mode" = CASE WHEN l."Quantity" IS NULL THEN 'Backfill' ELSE 'Finite' END,
                    "QuantityBasis" = CASE WHEN l."TargetKind" = 'Assembly' THEN 'Sets' ELSE 'Units' END,
                    "State" = 'Enabled', "CreatedAtUtc" = r."CreatedAtUtc", "BalanceSinceUtc" = r."CreatedAtUtc"
                FROM "ProductionRuns" r WHERE l."ProductionRunId" = r."Id";
                UPDATE "PartRequirements" SET "RecipeActive" = true, "UnitsPerSet" = 1;
                WITH RECURSIVE recipe AS (
                    SELECT l."Id" AS demand, b."TargetKind" AS kind, b."TargetPartId" AS part,
                        b."TargetAssemblyId" AS assembly, b."Quantity" AS units, 1 AS depth
                    FROM "RunLines" l JOIN "BomLines" b ON b."AssemblyId" = l."TargetId"
                    WHERE l."Quantity" IS NULL AND l."TargetKind" = 'Assembly'
                    UNION ALL
                    SELECT r.demand, b."TargetKind", b."TargetPartId", b."TargetAssemblyId", r.units * b."Quantity", r.depth + 1
                    FROM recipe r JOIN "BomLines" b ON b."AssemblyId" = r.assembly
                    WHERE r.kind = 'Assembly' AND r.depth < 10
                ), ratios AS (SELECT demand, part, sum(units) AS units FROM recipe WHERE kind = 'Part' GROUP BY demand, part)
                UPDATE "PartRequirements" p SET "UnitsPerSet" = r.units FROM ratios r
                WHERE p."RunLineId" = r.demand AND p."PartId" = r.part AND r.units > 0;
                """);

            migrationBuilder.CreateIndex(
                name: "IX_RunLines_SubmissionId",
                table: "RunLines",
                column: "SubmissionId",
                unique: true,
                filter: "\"SubmissionId\" IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_PartRequirements_RunLineId_PartId",
                table: "PartRequirements",
                columns: new[] { "RunLineId", "PartId" },
                unique: true,
                filter: "\"RecipeActive\"");

            migrationBuilder.AddForeignKey(
                name: "FK_RunLines_ProductionRuns_ProductionRunId",
                table: "RunLines",
                column: "ProductionRunId",
                principalTable: "ProductionRuns",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                DO $$ BEGIN
                    IF EXISTS (SELECT 1 FROM "RunLines" WHERE "ProductionRunId" IS NULL)
                        OR EXISTS (SELECT 1 FROM "PartRequirements" GROUP BY "RunLineId", "PartId" HAVING count(*) > 1) THEN
                        RAISE EXCEPTION 'Cannot downgrade while standalone demand or recipe history exists.';
                    END IF;
                END $$;
                """);
            migrationBuilder.DropForeignKey(
                name: "FK_RunLines_ProductionRuns_ProductionRunId",
                table: "RunLines");

            migrationBuilder.DropIndex(
                name: "IX_RunLines_SubmissionId",
                table: "RunLines");

            migrationBuilder.DropIndex(
                name: "IX_PartRequirements_RunLineId_PartId",
                table: "PartRequirements");

            migrationBuilder.DropColumn(
                name: "BalanceSinceUtc",
                table: "RunLines");

            migrationBuilder.DropColumn(
                name: "CreatedAtUtc",
                table: "RunLines");

            migrationBuilder.DropColumn(
                name: "Mode",
                table: "RunLines");

            migrationBuilder.DropColumn(
                name: "QuantityBasis",
                table: "RunLines");

            migrationBuilder.DropColumn(
                name: "State",
                table: "RunLines");

            migrationBuilder.DropColumn(
                name: "SubmissionId",
                table: "RunLines");

            migrationBuilder.DropColumn(
                name: "xmin",
                table: "RunLines");

            migrationBuilder.DropColumn(
                name: "RecipeActive",
                table: "PartRequirements");

            migrationBuilder.DropColumn(
                name: "UnitsPerSet",
                table: "PartRequirements");

            migrationBuilder.AlterColumn<Guid>(
                name: "ProductionRunId",
                table: "RunLines",
                type: "uuid",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"),
                oldClrType: typeof(Guid),
                oldType: "uuid",
                oldNullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_PartRequirements_RunLineId_PartId",
                table: "PartRequirements",
                columns: new[] { "RunLineId", "PartId" },
                unique: true);

            migrationBuilder.AddForeignKey(
                name: "FK_RunLines_ProductionRuns_ProductionRunId",
                table: "RunLines",
                column: "ProductionRunId",
                principalTable: "ProductionRuns",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);
        }
    }
}
