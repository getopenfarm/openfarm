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

            migrationBuilder.AddColumn<string>(
                name: "ProvisioningExternalId",
                table: "AspNetUsers",
                type: "character varying(255)",
                maxLength: 255,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ProvisioningProvider",
                table: "AspNetUsers",
                type: "character varying(69)",
                maxLength: 69,
                nullable: true);

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

        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "AuthenticationSettings");

            migrationBuilder.DropTable(
                name: "OidcLogoutReceipts");

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
                name: "ProvisioningExternalId",
                table: "AspNetUsers");

            migrationBuilder.DropColumn(
                name: "ProvisioningProvider",
                table: "AspNetUsers");

        }
    }
}
