using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace OpenFarm.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class SsoSettingsAndBackchannelLogout : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Older sessions have no provider sid/subject. Require a new sign-in so every active
            // external session can be revoked through back-channel logout after this upgrade.
            migrationBuilder.Sql("UPDATE \"BrowserSessions\" SET \"RevokedAtUtc\" = CURRENT_TIMESTAMP WHERE \"ClientId\" IS NULL AND \"OidcConfigurationIdentity\" IS NOT NULL AND \"RevokedAtUtc\" IS NULL");
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
                name: "SsoConfigurations",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Revision = table.Column<Guid>(type: "uuid", nullable: false),
                    ProtectedConfiguration = table.Column<string>(type: "text", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SsoConfigurations", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_BrowserSessions_OidcConfigurationIdentity_OidcSessionId",
                table: "BrowserSessions",
                columns: new[] { "OidcConfigurationIdentity", "OidcSessionId" });

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
                name: "OidcLogoutReceipts");

            migrationBuilder.DropTable(
                name: "SsoConfigurations");

            migrationBuilder.DropIndex(
                name: "IX_BrowserSessions_OidcConfigurationIdentity_OidcSessionId",
                table: "BrowserSessions");

            migrationBuilder.DropColumn(
                name: "OidcSessionId",
                table: "BrowserSessions");

            migrationBuilder.DropColumn(
                name: "OidcSubject",
                table: "BrowserSessions");
        }
    }
}
