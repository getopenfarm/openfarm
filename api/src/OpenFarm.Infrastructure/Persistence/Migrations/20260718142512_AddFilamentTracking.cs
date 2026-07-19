using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace OpenFarm.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddFilamentTracking : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "FeedUnits",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    OrganizationId = table.Column<Guid>(type: "uuid", nullable: false),
                    PrinterId = table.Column<Guid>(type: "uuid", nullable: false),
                    Kind = table.Column<string>(type: "text", nullable: false),
                    UnitIndex = table.Column<int>(type: "integer", nullable: false),
                    Name = table.Column<string>(type: "text", nullable: true),
                    ReplacesFeedUnitId = table.Column<Guid>(type: "uuid", nullable: true),
                    IsRevealed = table.Column<bool>(type: "boolean", nullable: false),
                    CreatedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    TenantId = table.Column<string>(type: "text", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_FeedUnits", x => x.Id);
                    table.ForeignKey(
                        name: "FK_FeedUnits_FeedUnits_ReplacesFeedUnitId",
                        column: x => x.ReplacesFeedUnitId,
                        principalTable: "FeedUnits",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateTable(
                name: "Vendors",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    OrganizationId = table.Column<Guid>(type: "uuid", nullable: false),
                    Name = table.Column<string>(type: "text", nullable: false),
                    CreatedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    TenantId = table.Column<string>(type: "text", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Vendors", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "FeedSlots",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    OrganizationId = table.Column<Guid>(type: "uuid", nullable: false),
                    FeedUnitId = table.Column<Guid>(type: "uuid", nullable: false),
                    SlotPosition = table.Column<int>(type: "integer", nullable: false),
                    DeactivatedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    CreatedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    TenantId = table.Column<string>(type: "text", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_FeedSlots", x => x.Id);
                    table.ForeignKey(
                        name: "FK_FeedSlots_FeedUnits_FeedUnitId",
                        column: x => x.FeedUnitId,
                        principalTable: "FeedUnits",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "Filaments",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    OrganizationId = table.Column<Guid>(type: "uuid", nullable: false),
                    VendorId = table.Column<Guid>(type: "uuid", nullable: false),
                    Material = table.Column<string>(type: "text", nullable: false),
                    MaterialType = table.Column<string>(type: "text", nullable: false),
                    FilamentProfileId = table.Column<Guid>(type: "uuid", nullable: true),
                    CreatedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    TenantId = table.Column<string>(type: "text", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Filaments", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Filaments_Vendors_VendorId",
                        column: x => x.VendorId,
                        principalTable: "Vendors",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "FilamentVariants",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    FilamentId = table.Column<Guid>(type: "uuid", nullable: false),
                    ColorName = table.Column<string>(type: "text", nullable: false),
                    ColorHex = table.Column<string>(type: "text", nullable: false),
                    CreatedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_FilamentVariants", x => x.Id);
                    table.ForeignKey(
                        name: "FK_FilamentVariants_Filaments_FilamentId",
                        column: x => x.FilamentId,
                        principalTable: "Filaments",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "Spools",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    OrganizationId = table.Column<Guid>(type: "uuid", nullable: false),
                    FilamentVariantId = table.Column<Guid>(type: "uuid", nullable: false),
                    Label = table.Column<string>(type: "text", nullable: true),
                    SpoolWeightGrams = table.Column<decimal>(type: "numeric", nullable: false),
                    RemainingWeightGrams = table.Column<decimal>(type: "numeric", nullable: false),
                    HasUsageOverrun = table.Column<bool>(type: "boolean", nullable: false),
                    State = table.Column<string>(type: "text", nullable: false),
                    FeedSlotId = table.Column<Guid>(type: "uuid", nullable: true),
                    CreatedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    TenantId = table.Column<string>(type: "text", nullable: false),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Spools", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Spools_FeedSlots_FeedSlotId",
                        column: x => x.FeedSlotId,
                        principalTable: "FeedSlots",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_Spools_FilamentVariants_FilamentVariantId",
                        column: x => x.FilamentVariantId,
                        principalTable: "FilamentVariants",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "SpoolUsageRecords",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    OrganizationId = table.Column<Guid>(type: "uuid", nullable: false),
                    SpoolId = table.Column<Guid>(type: "uuid", nullable: false),
                    GramsConsumed = table.Column<decimal>(type: "numeric", nullable: false),
                    RecordedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    JobId = table.Column<Guid>(type: "uuid", nullable: true),
                    Source = table.Column<string>(type: "text", nullable: false),
                    TenantId = table.Column<string>(type: "text", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SpoolUsageRecords", x => x.Id);
                    table.ForeignKey(
                        name: "FK_SpoolUsageRecords_Spools_SpoolId",
                        column: x => x.SpoolId,
                        principalTable: "Spools",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_FeedSlots_FeedUnitId_SlotPosition",
                table: "FeedSlots",
                columns: new[] { "FeedUnitId", "SlotPosition" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_FeedUnits_PrinterId_Kind_UnitIndex",
                table: "FeedUnits",
                columns: new[] { "PrinterId", "Kind", "UnitIndex" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_FeedUnits_ReplacesFeedUnitId",
                table: "FeedUnits",
                column: "ReplacesFeedUnitId");

            migrationBuilder.CreateIndex(
                name: "IX_Filaments_VendorId",
                table: "Filaments",
                column: "VendorId");

            migrationBuilder.CreateIndex(
                name: "IX_FilamentVariants_FilamentId_ColorName",
                table: "FilamentVariants",
                columns: new[] { "FilamentId", "ColorName" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Spools_FeedSlotId",
                table: "Spools",
                column: "FeedSlotId",
                unique: true,
                filter: "\"State\" = 'Using'");

            migrationBuilder.CreateIndex(
                name: "IX_Spools_FilamentVariantId",
                table: "Spools",
                column: "FilamentVariantId");

            migrationBuilder.CreateIndex(
                name: "IX_SpoolUsageRecords_SpoolId",
                table: "SpoolUsageRecords",
                column: "SpoolId");

            migrationBuilder.CreateIndex(
                name: "IX_Vendors_OrganizationId_Name",
                table: "Vendors",
                columns: new[] { "OrganizationId", "Name" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "SpoolUsageRecords");

            migrationBuilder.DropTable(
                name: "Spools");

            migrationBuilder.DropTable(
                name: "FeedSlots");

            migrationBuilder.DropTable(
                name: "FilamentVariants");

            migrationBuilder.DropTable(
                name: "FeedUnits");

            migrationBuilder.DropTable(
                name: "Filaments");

            migrationBuilder.DropTable(
                name: "Vendors");
        }
    }
}
