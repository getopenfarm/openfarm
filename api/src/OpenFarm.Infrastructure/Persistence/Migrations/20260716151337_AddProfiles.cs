using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace OpenFarm.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddProfiles : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "FilamentProfiles",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    OrganizationId = table.Column<Guid>(type: "uuid", nullable: false),
                    Name = table.Column<string>(type: "text", nullable: false),
                    SourceFormat = table.Column<string>(type: "text", nullable: false),
                    BaseProfileName = table.Column<string>(type: "text", nullable: true),
                    BaseProfileId = table.Column<Guid>(type: "uuid", nullable: true),
                    RawDataJson = table.Column<string>(type: "jsonb", nullable: false),
                    CreatedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    TenantId = table.Column<string>(type: "text", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_FilamentProfiles", x => x.Id);
                    table.ForeignKey(
                        name: "FK_FilamentProfiles_FilamentProfiles_BaseProfileId",
                        column: x => x.BaseProfileId,
                        principalTable: "FilamentProfiles",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateTable(
                name: "MachineProfiles",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    OrganizationId = table.Column<Guid>(type: "uuid", nullable: false),
                    Name = table.Column<string>(type: "text", nullable: false),
                    SourceFormat = table.Column<string>(type: "text", nullable: false),
                    BaseProfileName = table.Column<string>(type: "text", nullable: true),
                    BaseProfileId = table.Column<Guid>(type: "uuid", nullable: true),
                    RawDataJson = table.Column<string>(type: "jsonb", nullable: false),
                    CreatedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    TenantId = table.Column<string>(type: "text", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_MachineProfiles", x => x.Id);
                    table.ForeignKey(
                        name: "FK_MachineProfiles_MachineProfiles_BaseProfileId",
                        column: x => x.BaseProfileId,
                        principalTable: "MachineProfiles",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateIndex(
                name: "IX_FilamentProfiles_BaseProfileId",
                table: "FilamentProfiles",
                column: "BaseProfileId");

            migrationBuilder.CreateIndex(
                name: "IX_MachineProfiles_BaseProfileId",
                table: "MachineProfiles",
                column: "BaseProfileId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "FilamentProfiles");

            migrationBuilder.DropTable(
                name: "MachineProfiles");
        }
    }
}
