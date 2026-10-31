using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace OpenFarm.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class BrowserSessionsAndPermissions : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "CreatedAtUtc",
                table: "AspNetUsers",
                type: "timestamp with time zone",
                nullable: false,
                defaultValue: new DateTimeOffset(new DateTime(1, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)));

            migrationBuilder.AddColumn<string>(
                name: "DisplayName",
                table: "AspNetUsers",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "IsEnabled",
                table: "AspNetUsers",
                type: "boolean",
                nullable: false,
                defaultValue: true);

            migrationBuilder.AddColumn<string>(
                name: "Description",
                table: "AspNetRoles",
                type: "text",
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<bool>(
                name: "IsProtected",
                table: "AspNetRoles",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.CreateTable(
                name: "BrowserSessions",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    UserId = table.Column<Guid>(type: "uuid", nullable: false),
                    CreatedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    ExpiresAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    LastSeenAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    RevokedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    Device = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_BrowserSessions", x => x.Id);
                    table.ForeignKey(
                        name: "FK_BrowserSessions_AspNetUsers_UserId",
                        column: x => x.UserId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "SessionCredentials",
                columns: table => new
                {
                    Hash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    SessionId = table.Column<Guid>(type: "uuid", nullable: false),
                    UsedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    ProtectedSuccessor = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SessionCredentials", x => x.Hash);
                    table.ForeignKey(
                        name: "FK_SessionCredentials_BrowserSessions_SessionId",
                        column: x => x.SessionId,
                        principalTable: "BrowserSessions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_BrowserSessions_UserId_ExpiresAtUtc",
                table: "BrowserSessions",
                columns: new[] { "UserId", "ExpiresAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_SessionCredentials_SessionId",
                table: "SessionCredentials",
                column: "SessionId");
            migrationBuilder.Sql("UPDATE \"AspNetUsers\" SET \"CreatedAtUtc\" = CURRENT_TIMESTAMP");
            migrationBuilder.Sql("""
                INSERT INTO "AspNetRoles" ("Id", "Name", "NormalizedName", "ConcurrencyStamp", "Description", "IsProtected") VALUES ('f246d189-b11c-4494-9999-000000000001', 'Administrators', 'ADMINISTRATORS', 'f246d189-b11c-4494-9999-000000000001', 'Every permission. Protected administrator group.', TRUE);
                INSERT INTO "AspNetRoles" ("Id", "Name", "NormalizedName", "ConcurrencyStamp", "Description", "IsProtected") VALUES ('f246d189-b11c-4494-9999-000000000002', 'Operators', 'OPERATORS', 'f246d189-b11c-4494-9999-000000000002', 'Routine farm operations. Sensitive actions require explicit permission.', FALSE);
                INSERT INTO "AspNetRoles" ("Id", "Name", "NormalizedName", "ConcurrencyStamp", "Description", "IsProtected") VALUES ('f246d189-b11c-4494-9999-000000000003', 'Viewers', 'VIEWERS', 'f246d189-b11c-4494-9999-000000000003', 'Read-only farm access.', FALSE);
                INSERT INTO "AspNetRoleClaims" ("RoleId", "ClaimType", "ClaimValue") VALUES ('f246d189-b11c-4494-9999-000000000002', 'openfarm.permission', 'assemblies:create');
                INSERT INTO "AspNetRoleClaims" ("RoleId", "ClaimType", "ClaimValue") VALUES ('f246d189-b11c-4494-9999-000000000002', 'openfarm.permission', 'assemblies:edit');
                INSERT INTO "AspNetRoleClaims" ("RoleId", "ClaimType", "ClaimValue") VALUES ('f246d189-b11c-4494-9999-000000000002', 'openfarm.permission', 'assemblies:view');
                INSERT INTO "AspNetRoleClaims" ("RoleId", "ClaimType", "ClaimValue") VALUES ('f246d189-b11c-4494-9999-000000000003', 'openfarm.permission', 'assemblies:view');
                INSERT INTO "AspNetRoleClaims" ("RoleId", "ClaimType", "ClaimValue") VALUES ('f246d189-b11c-4494-9999-000000000002', 'openfarm.permission', 'feed-units:add');
                INSERT INTO "AspNetRoleClaims" ("RoleId", "ClaimType", "ClaimValue") VALUES ('f246d189-b11c-4494-9999-000000000002', 'openfarm.permission', 'feed-units:configure');
                INSERT INTO "AspNetRoleClaims" ("RoleId", "ClaimType", "ClaimValue") VALUES ('f246d189-b11c-4494-9999-000000000002', 'openfarm.permission', 'feed-units:list');
                INSERT INTO "AspNetRoleClaims" ("RoleId", "ClaimType", "ClaimValue") VALUES ('f246d189-b11c-4494-9999-000000000003', 'openfarm.permission', 'feed-units:list');
                INSERT INTO "AspNetRoleClaims" ("RoleId", "ClaimType", "ClaimValue") VALUES ('f246d189-b11c-4494-9999-000000000002', 'openfarm.permission', 'filaments:assign-profiles');
                INSERT INTO "AspNetRoleClaims" ("RoleId", "ClaimType", "ClaimValue") VALUES ('f246d189-b11c-4494-9999-000000000002', 'openfarm.permission', 'filaments:create');
                INSERT INTO "AspNetRoleClaims" ("RoleId", "ClaimType", "ClaimValue") VALUES ('f246d189-b11c-4494-9999-000000000002', 'openfarm.permission', 'filaments:list');
                INSERT INTO "AspNetRoleClaims" ("RoleId", "ClaimType", "ClaimValue") VALUES ('f246d189-b11c-4494-9999-000000000003', 'openfarm.permission', 'filaments:list');
                INSERT INTO "AspNetRoleClaims" ("RoleId", "ClaimType", "ClaimValue") VALUES ('f246d189-b11c-4494-9999-000000000002', 'openfarm.permission', 'library:manage');
                INSERT INTO "AspNetRoleClaims" ("RoleId", "ClaimType", "ClaimValue") VALUES ('f246d189-b11c-4494-9999-000000000002', 'openfarm.permission', 'library:view');
                INSERT INTO "AspNetRoleClaims" ("RoleId", "ClaimType", "ClaimValue") VALUES ('f246d189-b11c-4494-9999-000000000003', 'openfarm.permission', 'library:view');
                INSERT INTO "AspNetRoleClaims" ("RoleId", "ClaimType", "ClaimValue") VALUES ('f246d189-b11c-4494-9999-000000000002', 'openfarm.permission', 'non-printed-components:create');
                INSERT INTO "AspNetRoleClaims" ("RoleId", "ClaimType", "ClaimValue") VALUES ('f246d189-b11c-4494-9999-000000000002', 'openfarm.permission', 'non-printed-components:edit');
                INSERT INTO "AspNetRoleClaims" ("RoleId", "ClaimType", "ClaimValue") VALUES ('f246d189-b11c-4494-9999-000000000002', 'openfarm.permission', 'non-printed-components:view');
                INSERT INTO "AspNetRoleClaims" ("RoleId", "ClaimType", "ClaimValue") VALUES ('f246d189-b11c-4494-9999-000000000003', 'openfarm.permission', 'non-printed-components:view');
                INSERT INTO "AspNetRoleClaims" ("RoleId", "ClaimType", "ClaimValue") VALUES ('f246d189-b11c-4494-9999-000000000002', 'openfarm.permission', 'parts:create');
                INSERT INTO "AspNetRoleClaims" ("RoleId", "ClaimType", "ClaimValue") VALUES ('f246d189-b11c-4494-9999-000000000002', 'openfarm.permission', 'parts:edit');
                INSERT INTO "AspNetRoleClaims" ("RoleId", "ClaimType", "ClaimValue") VALUES ('f246d189-b11c-4494-9999-000000000002', 'openfarm.permission', 'parts:list');
                INSERT INTO "AspNetRoleClaims" ("RoleId", "ClaimType", "ClaimValue") VALUES ('f246d189-b11c-4494-9999-000000000003', 'openfarm.permission', 'parts:list');
                INSERT INTO "AspNetRoleClaims" ("RoleId", "ClaimType", "ClaimValue") VALUES ('f246d189-b11c-4494-9999-000000000002', 'openfarm.permission', 'parts:read');
                INSERT INTO "AspNetRoleClaims" ("RoleId", "ClaimType", "ClaimValue") VALUES ('f246d189-b11c-4494-9999-000000000003', 'openfarm.permission', 'parts:read');
                INSERT INTO "AspNetRoleClaims" ("RoleId", "ClaimType", "ClaimValue") VALUES ('f246d189-b11c-4494-9999-000000000002', 'openfarm.permission', 'parts:replace-file');
                INSERT INTO "AspNetRoleClaims" ("RoleId", "ClaimType", "ClaimValue") VALUES ('f246d189-b11c-4494-9999-000000000002', 'openfarm.permission', 'parts:view');
                INSERT INTO "AspNetRoleClaims" ("RoleId", "ClaimType", "ClaimValue") VALUES ('f246d189-b11c-4494-9999-000000000003', 'openfarm.permission', 'parts:view');
                INSERT INTO "AspNetRoleClaims" ("RoleId", "ClaimType", "ClaimValue") VALUES ('f246d189-b11c-4494-9999-000000000002', 'openfarm.permission', 'print-jobs:pin');
                INSERT INTO "AspNetRoleClaims" ("RoleId", "ClaimType", "ClaimValue") VALUES ('f246d189-b11c-4494-9999-000000000002', 'openfarm.permission', 'print-jobs:reorder');
                INSERT INTO "AspNetRoleClaims" ("RoleId", "ClaimType", "ClaimValue") VALUES ('f246d189-b11c-4494-9999-000000000002', 'openfarm.permission', 'print-jobs:requeue');
                INSERT INTO "AspNetRoleClaims" ("RoleId", "ClaimType", "ClaimValue") VALUES ('f246d189-b11c-4494-9999-000000000002', 'openfarm.permission', 'print-jobs:update-status');
                INSERT INTO "AspNetRoleClaims" ("RoleId", "ClaimType", "ClaimValue") VALUES ('f246d189-b11c-4494-9999-000000000002', 'openfarm.permission', 'print-jobs:view');
                INSERT INTO "AspNetRoleClaims" ("RoleId", "ClaimType", "ClaimValue") VALUES ('f246d189-b11c-4494-9999-000000000003', 'openfarm.permission', 'print-jobs:view');
                INSERT INTO "AspNetRoleClaims" ("RoleId", "ClaimType", "ClaimValue") VALUES ('f246d189-b11c-4494-9999-000000000002', 'openfarm.permission', 'printers:get-detail');
                INSERT INTO "AspNetRoleClaims" ("RoleId", "ClaimType", "ClaimValue") VALUES ('f246d189-b11c-4494-9999-000000000003', 'openfarm.permission', 'printers:get-detail');
                INSERT INTO "AspNetRoleClaims" ("RoleId", "ClaimType", "ClaimValue") VALUES ('f246d189-b11c-4494-9999-000000000002', 'openfarm.permission', 'printers:list');
                INSERT INTO "AspNetRoleClaims" ("RoleId", "ClaimType", "ClaimValue") VALUES ('f246d189-b11c-4494-9999-000000000003', 'openfarm.permission', 'printers:list');
                INSERT INTO "AspNetRoleClaims" ("RoleId", "ClaimType", "ClaimValue") VALUES ('f246d189-b11c-4494-9999-000000000002', 'openfarm.permission', 'printers:mark-ready');
                INSERT INTO "AspNetRoleClaims" ("RoleId", "ClaimType", "ClaimValue") VALUES ('f246d189-b11c-4494-9999-000000000002', 'openfarm.permission', 'printers:register');
                INSERT INTO "AspNetRoleClaims" ("RoleId", "ClaimType", "ClaimValue") VALUES ('f246d189-b11c-4494-9999-000000000002', 'openfarm.permission', 'production-runs:create');
                INSERT INTO "AspNetRoleClaims" ("RoleId", "ClaimType", "ClaimValue") VALUES ('f246d189-b11c-4494-9999-000000000002', 'openfarm.permission', 'production-runs:edit');
                INSERT INTO "AspNetRoleClaims" ("RoleId", "ClaimType", "ClaimValue") VALUES ('f246d189-b11c-4494-9999-000000000002', 'openfarm.permission', 'production-runs:view');
                INSERT INTO "AspNetRoleClaims" ("RoleId", "ClaimType", "ClaimValue") VALUES ('f246d189-b11c-4494-9999-000000000003', 'openfarm.permission', 'production-runs:view');
                INSERT INTO "AspNetRoleClaims" ("RoleId", "ClaimType", "ClaimValue") VALUES ('f246d189-b11c-4494-9999-000000000002', 'openfarm.permission', 'profiles:confirm-import');
                INSERT INTO "AspNetRoleClaims" ("RoleId", "ClaimType", "ClaimValue") VALUES ('f246d189-b11c-4494-9999-000000000002', 'openfarm.permission', 'profiles:edit-filament-raw-data');
                INSERT INTO "AspNetRoleClaims" ("RoleId", "ClaimType", "ClaimValue") VALUES ('f246d189-b11c-4494-9999-000000000002', 'openfarm.permission', 'profiles:edit-machine-raw-data');
                INSERT INTO "AspNetRoleClaims" ("RoleId", "ClaimType", "ClaimValue") VALUES ('f246d189-b11c-4494-9999-000000000002', 'openfarm.permission', 'profiles:get-filament');
                INSERT INTO "AspNetRoleClaims" ("RoleId", "ClaimType", "ClaimValue") VALUES ('f246d189-b11c-4494-9999-000000000003', 'openfarm.permission', 'profiles:get-filament');
                INSERT INTO "AspNetRoleClaims" ("RoleId", "ClaimType", "ClaimValue") VALUES ('f246d189-b11c-4494-9999-000000000002', 'openfarm.permission', 'profiles:get-machine');
                INSERT INTO "AspNetRoleClaims" ("RoleId", "ClaimType", "ClaimValue") VALUES ('f246d189-b11c-4494-9999-000000000003', 'openfarm.permission', 'profiles:get-machine');
                INSERT INTO "AspNetRoleClaims" ("RoleId", "ClaimType", "ClaimValue") VALUES ('f246d189-b11c-4494-9999-000000000002', 'openfarm.permission', 'profiles:get-process');
                INSERT INTO "AspNetRoleClaims" ("RoleId", "ClaimType", "ClaimValue") VALUES ('f246d189-b11c-4494-9999-000000000003', 'openfarm.permission', 'profiles:get-process');
                INSERT INTO "AspNetRoleClaims" ("RoleId", "ClaimType", "ClaimValue") VALUES ('f246d189-b11c-4494-9999-000000000002', 'openfarm.permission', 'profiles:list-filament');
                INSERT INTO "AspNetRoleClaims" ("RoleId", "ClaimType", "ClaimValue") VALUES ('f246d189-b11c-4494-9999-000000000003', 'openfarm.permission', 'profiles:list-filament');
                INSERT INTO "AspNetRoleClaims" ("RoleId", "ClaimType", "ClaimValue") VALUES ('f246d189-b11c-4494-9999-000000000002', 'openfarm.permission', 'profiles:list-machine');
                INSERT INTO "AspNetRoleClaims" ("RoleId", "ClaimType", "ClaimValue") VALUES ('f246d189-b11c-4494-9999-000000000003', 'openfarm.permission', 'profiles:list-machine');
                INSERT INTO "AspNetRoleClaims" ("RoleId", "ClaimType", "ClaimValue") VALUES ('f246d189-b11c-4494-9999-000000000002', 'openfarm.permission', 'profiles:list-process');
                INSERT INTO "AspNetRoleClaims" ("RoleId", "ClaimType", "ClaimValue") VALUES ('f246d189-b11c-4494-9999-000000000003', 'openfarm.permission', 'profiles:list-process');
                INSERT INTO "AspNetRoleClaims" ("RoleId", "ClaimType", "ClaimValue") VALUES ('f246d189-b11c-4494-9999-000000000002', 'openfarm.permission', 'profiles:preview-import');
                INSERT INTO "AspNetRoleClaims" ("RoleId", "ClaimType", "ClaimValue") VALUES ('f246d189-b11c-4494-9999-000000000002', 'openfarm.permission', 'spools:list');
                INSERT INTO "AspNetRoleClaims" ("RoleId", "ClaimType", "ClaimValue") VALUES ('f246d189-b11c-4494-9999-000000000003', 'openfarm.permission', 'spools:list');
                INSERT INTO "AspNetRoleClaims" ("RoleId", "ClaimType", "ClaimValue") VALUES ('f246d189-b11c-4494-9999-000000000002', 'openfarm.permission', 'spools:load');
                INSERT INTO "AspNetRoleClaims" ("RoleId", "ClaimType", "ClaimValue") VALUES ('f246d189-b11c-4494-9999-000000000002', 'openfarm.permission', 'spools:record-usage');
                INSERT INTO "AspNetRoleClaims" ("RoleId", "ClaimType", "ClaimValue") VALUES ('f246d189-b11c-4494-9999-000000000002', 'openfarm.permission', 'spools:register');
                INSERT INTO "AspNetRoleClaims" ("RoleId", "ClaimType", "ClaimValue") VALUES ('f246d189-b11c-4494-9999-000000000002', 'openfarm.permission', 'spools:unload');
                INSERT INTO "AspNetRoleClaims" ("RoleId", "ClaimType", "ClaimValue") VALUES ('f246d189-b11c-4494-9999-000000000002', 'openfarm.permission', 'tags:list');
                INSERT INTO "AspNetRoleClaims" ("RoleId", "ClaimType", "ClaimValue") VALUES ('f246d189-b11c-4494-9999-000000000003', 'openfarm.permission', 'tags:list');
                INSERT INTO "AspNetRoleClaims" ("RoleId", "ClaimType", "ClaimValue") VALUES ('f246d189-b11c-4494-9999-000000000002', 'openfarm.permission', 'vendors:create');
                INSERT INTO "AspNetRoleClaims" ("RoleId", "ClaimType", "ClaimValue") VALUES ('f246d189-b11c-4494-9999-000000000002', 'openfarm.permission', 'vendors:list');
                INSERT INTO "AspNetRoleClaims" ("RoleId", "ClaimType", "ClaimValue") VALUES ('f246d189-b11c-4494-9999-000000000003', 'openfarm.permission', 'vendors:list');
                INSERT INTO "AspNetUserRoles" ("UserId", "RoleId") SELECT "Id", 'f246d189-b11c-4494-9999-000000000001'::uuid FROM "AspNetUsers";
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("DELETE FROM \"AspNetRoles\" WHERE \"Id\" IN ('f246d189-b11c-4494-9999-000000000001', 'f246d189-b11c-4494-9999-000000000002', 'f246d189-b11c-4494-9999-000000000003')");
            migrationBuilder.DropTable(
                name: "SessionCredentials");

            migrationBuilder.DropTable(
                name: "BrowserSessions");

            migrationBuilder.DropColumn(
                name: "CreatedAtUtc",
                table: "AspNetUsers");

            migrationBuilder.DropColumn(
                name: "DisplayName",
                table: "AspNetUsers");

            migrationBuilder.DropColumn(
                name: "IsEnabled",
                table: "AspNetUsers");

            migrationBuilder.DropColumn(
                name: "Description",
                table: "AspNetRoles");

            migrationBuilder.DropColumn(
                name: "IsProtected",
                table: "AspNetRoles");
        }
    }
}
