using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using OpenFarm.Infrastructure.Persistence;

#nullable disable

namespace OpenFarm.Infrastructure.Persistence.Migrations
{
    // See AddPartsCatalog's header comment — attributed directly, no separate Designer.cs (no dotnet
    // SDK access in this sandbox; regenerate via `dotnet ef migrations add` for design-time tooling).
    [DbContext(typeof(OpenFarmDbContext))]
    [Migration("20260721160000_AddProcessProfilesAndSlicingProfileDefaults")]
    public partial class AddProcessProfilesAndSlicingProfileDefaults : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "ProcessProfiles",
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
                    table.PrimaryKey("PK_ProcessProfiles", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ProcessProfiles_ProcessProfiles_BaseProfileId",
                        column: x => x.BaseProfileId,
                        principalTable: "ProcessProfiles",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateIndex(
                name: "IX_ProcessProfiles_BaseProfileId",
                table: "ProcessProfiles",
                column: "BaseProfileId");

            migrationBuilder.CreateTable(
                name: "OrganizationFilamentDefaults",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    OrganizationId = table.Column<Guid>(type: "uuid", nullable: false),
                    MaterialType = table.Column<string>(type: "text", nullable: false),
                    FilamentProfileId = table.Column<Guid>(type: "uuid", nullable: false),
                    CreatedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    TenantId = table.Column<string>(type: "text", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_OrganizationFilamentDefaults", x => x.Id);
                    table.ForeignKey(
                        name: "FK_OrganizationFilamentDefaults_FilamentProfiles_FilamentProfileId",
                        column: x => x.FilamentProfileId,
                        principalTable: "FilamentProfiles",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_OrganizationFilamentDefaults_OrganizationId_MaterialType",
                table: "OrganizationFilamentDefaults",
                columns: new[] { "OrganizationId", "MaterialType" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_OrganizationFilamentDefaults_FilamentProfileId",
                table: "OrganizationFilamentDefaults",
                column: "FilamentProfileId");

            migrationBuilder.AddColumn<Guid>(
                name: "DefaultProcessProfileId",
                table: "MachineProfiles",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_MachineProfiles_DefaultProcessProfileId",
                table: "MachineProfiles",
                column: "DefaultProcessProfileId");

            migrationBuilder.AddForeignKey(
                name: "FK_MachineProfiles_ProcessProfiles_DefaultProcessProfileId",
                table: "MachineProfiles",
                column: "DefaultProcessProfileId",
                principalTable: "ProcessProfiles",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);

            migrationBuilder.AddColumn<Guid>(
                name: "DefaultMachineProfileId",
                table: "Organizations",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_Organizations_DefaultMachineProfileId",
                table: "Organizations",
                column: "DefaultMachineProfileId");

            migrationBuilder.AddForeignKey(
                name: "FK_Organizations_MachineProfiles_DefaultMachineProfileId",
                table: "Organizations",
                column: "DefaultMachineProfileId",
                principalTable: "MachineProfiles",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Organizations_MachineProfiles_DefaultMachineProfileId",
                table: "Organizations");

            migrationBuilder.DropIndex(
                name: "IX_Organizations_DefaultMachineProfileId",
                table: "Organizations");

            migrationBuilder.DropColumn(
                name: "DefaultMachineProfileId",
                table: "Organizations");

            migrationBuilder.DropForeignKey(
                name: "FK_MachineProfiles_ProcessProfiles_DefaultProcessProfileId",
                table: "MachineProfiles");

            migrationBuilder.DropIndex(
                name: "IX_MachineProfiles_DefaultProcessProfileId",
                table: "MachineProfiles");

            migrationBuilder.DropColumn(
                name: "DefaultProcessProfileId",
                table: "MachineProfiles");

            migrationBuilder.DropTable(
                name: "OrganizationFilamentDefaults");

            migrationBuilder.DropTable(
                name: "ProcessProfiles");
        }
    }
}
