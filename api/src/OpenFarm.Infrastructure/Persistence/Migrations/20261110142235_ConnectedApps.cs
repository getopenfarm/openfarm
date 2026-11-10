using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace OpenFarm.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class ConnectedApps : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "ClientId",
                table: "BrowserSessions",
                type: "character varying(36)",
                maxLength: 36,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "RedirectUri",
                table: "BrowserSessions",
                type: "character varying(2048)",
                maxLength: 2048,
                nullable: true);

            migrationBuilder.AddColumn<string[]>(
                name: "Scopes",
                table: "BrowserSessions",
                type: "text[]",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "OAuthClients",
                columns: table => new
                {
                    Id = table.Column<string>(type: "character varying(36)", maxLength: 36, nullable: false),
                    Name = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    RedirectUris = table.Column<string[]>(type: "text[]", nullable: false),
                    AuthMethod = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    SecretHash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    CreatedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_OAuthClients", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_BrowserSessions_ClientId",
                table: "BrowserSessions",
                column: "ClientId");

            migrationBuilder.AddCheckConstraint(
                name: "CK_BrowserSessions_AppScopes",
                table: "BrowserSessions",
                sql: "(\"ClientId\" IS NULL) = (\"Scopes\" IS NULL)");

            migrationBuilder.CreateIndex(
                name: "IX_OAuthClients_CreatedAtUtc",
                table: "OAuthClients",
                column: "CreatedAtUtc");

            migrationBuilder.AddForeignKey(
                name: "FK_BrowserSessions_OAuthClients_ClientId",
                table: "BrowserSessions",
                column: "ClientId",
                principalTable: "OAuthClients",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Connected apps cannot exist without their columns: remove them before dropping the schema.
            migrationBuilder.Sql("DELETE FROM \"BrowserSessions\" WHERE \"ClientId\" IS NOT NULL");

            migrationBuilder.DropForeignKey(
                name: "FK_BrowserSessions_OAuthClients_ClientId",
                table: "BrowserSessions");

            migrationBuilder.DropTable(
                name: "OAuthClients");

            migrationBuilder.DropIndex(
                name: "IX_BrowserSessions_ClientId",
                table: "BrowserSessions");

            migrationBuilder.DropCheckConstraint(
                name: "CK_BrowserSessions_AppScopes",
                table: "BrowserSessions");

            migrationBuilder.DropColumn(
                name: "ClientId",
                table: "BrowserSessions");

            migrationBuilder.DropColumn(
                name: "RedirectUri",
                table: "BrowserSessions");

            migrationBuilder.DropColumn(
                name: "Scopes",
                table: "BrowserSessions");
        }
    }
}
