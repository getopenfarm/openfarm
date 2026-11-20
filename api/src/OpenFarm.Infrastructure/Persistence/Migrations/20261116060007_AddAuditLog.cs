using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace OpenFarm.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddAuditLog : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "Action",
                table: "ActivityLogEntries",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ActorName",
                table: "ActivityLogEntries",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Category",
                table: "ActivityLogEntries",
                type: "text",
                nullable: false,
                defaultValue: "Audit");

            migrationBuilder.AddColumn<string>(
                name: "ChangesJson",
                table: "ActivityLogEntries",
                type: "jsonb",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Channel",
                table: "ActivityLogEntries",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "CorrelationId",
                table: "ActivityLogEntries",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "CredentialId",
                table: "ActivityLogEntries",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "CredentialLabel",
                table: "ActivityLogEntries",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "EntityLabel",
                table: "ActivityLogEntries",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Operation",
                table: "ActivityLogEntries",
                type: "text",
                nullable: true);

            migrationBuilder.Sql("""
                UPDATE "ActivityLogEntries" SET "Category" = 'MachineObservation'
                WHERE "EventType" IN ('PrinterStatusObserved', 'PrinterAlertsChanged');
                UPDATE "ActivityLogEntries" SET
                  "Channel" = CASE WHEN "ActorType" = 'System' THEN 'System'
                    WHEN "Details"->'Via'->>'Kind' IN ('Token', 'App') THEN "Details"->'Via'->>'Kind'
                    ELSE 'Browser' END,
                  "CredentialId" = CASE WHEN "Details"->'Via'->>'CredentialId' ~* '^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$'
                    THEN ("Details"->'Via'->>'CredentialId')::uuid ELSE NULL END,
                  "CredentialLabel" = "Details"->'Via'->>'Label';
                UPDATE "AspNetRoleClaims" SET "ClaimValue" = 'audit-log:list' WHERE "ClaimType" = 'openfarm.permission' AND "ClaimValue" = 'activity-log:list';
                UPDATE "AspNetUserClaims" SET "ClaimValue" = 'audit-log:list' WHERE "ClaimType" = 'openfarm.permission' AND "ClaimValue" = 'activity-log:list';
                UPDATE "PersonalAccessTokens" SET "Permissions" = array_replace("Permissions", 'activity-log:list', 'audit-log:list') WHERE 'activity-log:list' = ANY("Permissions");
                """);

            migrationBuilder.CreateIndex(
                name: "IX_ActivityLogEntries_ActorId_TimestampUtc_Id",
                table: "ActivityLogEntries",
                columns: new[] { "ActorId", "TimestampUtc", "Id" },
                descending: new[] { false, true, false });

            migrationBuilder.CreateIndex(
                name: "IX_ActivityLogEntries_Category_TimestampUtc_Id",
                table: "ActivityLogEntries",
                columns: new[] { "Category", "TimestampUtc", "Id" },
                descending: new[] { false, true, false });

            migrationBuilder.CreateIndex(
                name: "IX_ActivityLogEntries_Channel_TimestampUtc_Id",
                table: "ActivityLogEntries",
                columns: new[] { "Channel", "TimestampUtc", "Id" },
                descending: new[] { false, true, false });

            migrationBuilder.CreateIndex(
                name: "IX_ActivityLogEntries_CorrelationId",
                table: "ActivityLogEntries",
                column: "CorrelationId");

            migrationBuilder.CreateIndex(
                name: "IX_ActivityLogEntries_CredentialId_TimestampUtc_Id",
                table: "ActivityLogEntries",
                columns: new[] { "CredentialId", "TimestampUtc", "Id" },
                descending: new[] { false, true, false });

            migrationBuilder.CreateIndex(
                name: "IX_ActivityLogEntries_EntityType_EntityId_TimestampUtc_Id",
                table: "ActivityLogEntries",
                columns: new[] { "EntityType", "EntityId", "TimestampUtc", "Id" },
                descending: new[] { false, false, true, false });

            migrationBuilder.AddColumn<Guid[]>(
                name: "RelatedPrinterIds",
                table: "ActivityLogEntries",
                type: "uuid[]",
                nullable: false,
                defaultValueSql: "'{}'::uuid[]");

            migrationBuilder.Sql("""
                UPDATE "ActivityLogEntries" SET "RelatedPrinterIds" = ARRAY["EntityId"::uuid]
                WHERE "EntityType" = 'Printer' AND "EntityId" ~* '^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$';
                """);

            migrationBuilder.CreateIndex(
                name: "IX_ActivityLogEntries_RelatedPrinterIds",
                table: "ActivityLogEntries",
                column: "RelatedPrinterIds")
                .Annotation("Npgsql:IndexMethod", "gin");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_ActivityLogEntries_RelatedPrinterIds",
                table: "ActivityLogEntries");

            migrationBuilder.DropColumn(
                name: "RelatedPrinterIds",
                table: "ActivityLogEntries");

            migrationBuilder.Sql("""
                UPDATE "AspNetRoleClaims" SET "ClaimValue" = 'activity-log:list' WHERE "ClaimType" = 'openfarm.permission' AND "ClaimValue" = 'audit-log:list';
                UPDATE "AspNetUserClaims" SET "ClaimValue" = 'activity-log:list' WHERE "ClaimType" = 'openfarm.permission' AND "ClaimValue" = 'audit-log:list';
                """);
            migrationBuilder.DropIndex(
                name: "IX_ActivityLogEntries_ActorId_TimestampUtc_Id",
                table: "ActivityLogEntries");

            migrationBuilder.DropIndex(
                name: "IX_ActivityLogEntries_Category_TimestampUtc_Id",
                table: "ActivityLogEntries");

            migrationBuilder.DropIndex(
                name: "IX_ActivityLogEntries_Channel_TimestampUtc_Id",
                table: "ActivityLogEntries");

            migrationBuilder.DropIndex(
                name: "IX_ActivityLogEntries_CorrelationId",
                table: "ActivityLogEntries");

            migrationBuilder.DropIndex(
                name: "IX_ActivityLogEntries_CredentialId_TimestampUtc_Id",
                table: "ActivityLogEntries");

            migrationBuilder.DropIndex(
                name: "IX_ActivityLogEntries_EntityType_EntityId_TimestampUtc_Id",
                table: "ActivityLogEntries");

            migrationBuilder.DropColumn(
                name: "Action",
                table: "ActivityLogEntries");

            migrationBuilder.DropColumn(
                name: "ActorName",
                table: "ActivityLogEntries");

            migrationBuilder.DropColumn(
                name: "Category",
                table: "ActivityLogEntries");

            migrationBuilder.DropColumn(
                name: "ChangesJson",
                table: "ActivityLogEntries");

            migrationBuilder.DropColumn(
                name: "Channel",
                table: "ActivityLogEntries");

            migrationBuilder.DropColumn(
                name: "CorrelationId",
                table: "ActivityLogEntries");

            migrationBuilder.DropColumn(
                name: "CredentialId",
                table: "ActivityLogEntries");

            migrationBuilder.DropColumn(
                name: "CredentialLabel",
                table: "ActivityLogEntries");

            migrationBuilder.DropColumn(
                name: "EntityLabel",
                table: "ActivityLogEntries");

            migrationBuilder.DropColumn(
                name: "Operation",
                table: "ActivityLogEntries");
        }
    }
}
