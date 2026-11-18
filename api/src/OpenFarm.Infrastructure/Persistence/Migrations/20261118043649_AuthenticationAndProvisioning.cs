using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace OpenFarm.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AuthenticationAndProvisioning : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "OidcConfigurationIdentity",
                table: "BrowserSessions",
                type: "character varying(64)",
                maxLength: 64,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "OidcSessionId",
                table: "BrowserSessions",
                type: "character varying(255)",
                maxLength: 255,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "OidcSubject",
                table: "BrowserSessions",
                type: "character varying(255)",
                maxLength: 255,
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "IsScimDeleted",
                table: "AspNetUsers",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<string>(
                name: "ProvisioningExternalId",
                table: "AspNetUsers",
                type: "character varying(255)",
                maxLength: 255,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ProvisioningFamilyName",
                table: "AspNetUsers",
                type: "character varying(256)",
                maxLength: 256,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ProvisioningGivenName",
                table: "AspNetUsers",
                type: "character varying(256)",
                maxLength: 256,
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "ProvisioningModifiedAtUtc",
                table: "AspNetUsers",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ProvisioningProvider",
                table: "AspNetUsers",
                type: "character varying(69)",
                maxLength: 69,
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "ProvisioningRevision",
                table: "AspNetUsers",
                type: "uuid",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"));

            migrationBuilder.CreateTable(
                name: "AuthenticationSettings",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Revision = table.Column<Guid>(type: "uuid", nullable: false),
                    TestedProviderIdentity = table.Column<string>(type: "text", nullable: true),
                    TestedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    ProtectedConfiguration = table.Column<string>(type: "text", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AuthenticationSettings", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "OidcLogoutReceipts",
                columns: table => new
                {
                    Id = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    Provider = table.Column<string>(type: "character varying(69)", maxLength: 69, nullable: false),
                    Subject = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: true),
                    SessionId = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: true),
                    ReceivedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    ExpiresAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_OidcLogoutReceipts", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "ProvisioningGroups",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Provider = table.Column<string>(type: "character varying(69)", maxLength: 69, nullable: false),
                    ExternalId = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: true),
                    DisplayName = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    NormalizedDisplayName = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    CreatedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    ModifiedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    Revision = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ProvisioningGroups", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "ProvisioningGroupMembers",
                columns: table => new
                {
                    GroupId = table.Column<Guid>(type: "uuid", nullable: false),
                    UserId = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ProvisioningGroupMembers", x => new { x.GroupId, x.UserId });
                    table.ForeignKey(
                        name: "FK_ProvisioningGroupMembers_AspNetUsers_UserId",
                        column: x => x.UserId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_ProvisioningGroupMembers_ProvisioningGroups_GroupId",
                        column: x => x.GroupId,
                        principalTable: "ProvisioningGroups",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_BrowserSessions_OidcConfigurationIdentity_OidcSessionId",
                table: "BrowserSessions",
                columns: new[] { "OidcConfigurationIdentity", "OidcSessionId" });

            migrationBuilder.CreateIndex(
                name: "IX_AspNetUsers_ProvisioningProvider_ProvisioningExternalId",
                table: "AspNetUsers",
                columns: new[] { "ProvisioningProvider", "ProvisioningExternalId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_OidcLogoutReceipts_ExpiresAtUtc",
                table: "OidcLogoutReceipts",
                column: "ExpiresAtUtc");

            migrationBuilder.CreateIndex(
                name: "IX_OidcLogoutReceipts_Provider_SessionId",
                table: "OidcLogoutReceipts",
                columns: new[] { "Provider", "SessionId" });

            migrationBuilder.CreateIndex(
                name: "IX_ProvisioningGroupMembers_UserId",
                table: "ProvisioningGroupMembers",
                column: "UserId");

            migrationBuilder.CreateIndex(
                name: "IX_ProvisioningGroups_Provider_ExternalId",
                table: "ProvisioningGroups",
                columns: new[] { "Provider", "ExternalId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ProvisioningGroups_Provider_NormalizedDisplayName",
                table: "ProvisioningGroups",
                columns: new[] { "Provider", "NormalizedDisplayName" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "AuthenticationSettings");

            migrationBuilder.DropTable(
                name: "OidcLogoutReceipts");

            migrationBuilder.DropTable(
                name: "ProvisioningGroupMembers");

            migrationBuilder.DropTable(
                name: "ProvisioningGroups");

            migrationBuilder.DropIndex(
                name: "IX_BrowserSessions_OidcConfigurationIdentity_OidcSessionId",
                table: "BrowserSessions");

            migrationBuilder.DropIndex(
                name: "IX_AspNetUsers_ProvisioningProvider_ProvisioningExternalId",
                table: "AspNetUsers");

            migrationBuilder.DropColumn(
                name: "OidcConfigurationIdentity",
                table: "BrowserSessions");

            migrationBuilder.DropColumn(
                name: "OidcSessionId",
                table: "BrowserSessions");

            migrationBuilder.DropColumn(
                name: "OidcSubject",
                table: "BrowserSessions");

            migrationBuilder.DropColumn(
                name: "IsScimDeleted",
                table: "AspNetUsers");

            migrationBuilder.DropColumn(
                name: "ProvisioningExternalId",
                table: "AspNetUsers");

            migrationBuilder.DropColumn(
                name: "ProvisioningFamilyName",
                table: "AspNetUsers");

            migrationBuilder.DropColumn(
                name: "ProvisioningGivenName",
                table: "AspNetUsers");

            migrationBuilder.DropColumn(
                name: "ProvisioningModifiedAtUtc",
                table: "AspNetUsers");

            migrationBuilder.DropColumn(
                name: "ProvisioningProvider",
                table: "AspNetUsers");

            migrationBuilder.DropColumn(
                name: "ProvisioningRevision",
                table: "AspNetUsers");
        }
    }
}
