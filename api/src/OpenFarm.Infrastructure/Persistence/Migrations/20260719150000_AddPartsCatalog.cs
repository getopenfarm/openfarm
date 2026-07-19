using System;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using OpenFarm.Infrastructure.Persistence;

#nullable disable

namespace OpenFarm.Infrastructure.Persistence.Migrations
{
    // [Migration]/[DbContext] attributed directly here rather than in a separate Designer.cs — this
    // sandbox has no dotnet SDK access (egress-blocked, see PR description), so BuildTargetModel's
    // full hand-verified model snapshot isn't safely producible by hand. Runtime Database.Migrate()
    // only needs these attributes (for discovery/ordering) plus Up()/Down() — it doesn't consult
    // BuildTargetModel, which is design-time-only (used when scaffolding the *next* migration).
    // Regenerate properly via `dotnet ef migrations add` once SDK access is available, to get a real
    // Designer.cs/updated ModelSnapshot for future scaffolding.
    [DbContext(typeof(OpenFarmDbContext))]
    [Migration("20260719150000_AddPartsCatalog")]
    public partial class AddPartsCatalog : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "Tags",
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
                    table.PrimaryKey("PK_Tags", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Parts",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    OrganizationId = table.Column<Guid>(type: "uuid", nullable: false),
                    Name = table.Column<string>(type: "text", nullable: false),
                    FileFormat = table.Column<string>(type: "text", nullable: false),
                    RequiresSlicing = table.Column<bool>(type: "boolean", nullable: false),
                    OriginalFileName = table.Column<string>(type: "text", nullable: false),
                    StoragePath = table.Column<string>(type: "text", nullable: false),
                    FileSizeBytes = table.Column<long>(type: "bigint", nullable: false),
                    FileUploadedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    NozzleDiameterMm = table.Column<decimal>(type: "numeric", nullable: false),
                    EstimateStatus = table.Column<string>(type: "text", nullable: false),
                    EstimatedPrintTimeMinutes = table.Column<int>(type: "integer", nullable: true),
                    EstimatedFilamentWeightGrams = table.Column<decimal>(type: "numeric", nullable: true),
                    SlicerEngineOverride = table.Column<string>(type: "text", nullable: true),
                    PlateCount = table.Column<int>(type: "integer", nullable: false),
                    CreatedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    TenantId = table.Column<string>(type: "text", nullable: false),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Parts", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "PartFilamentRequirements",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    OrganizationId = table.Column<Guid>(type: "uuid", nullable: false),
                    PartId = table.Column<Guid>(type: "uuid", nullable: false),
                    MaterialType = table.Column<string>(type: "text", nullable: true),
                    DetectedColorHex = table.Column<string>(type: "text", nullable: true),
                    DetectedSubBrand = table.Column<string>(type: "text", nullable: true),
                    NarrowFilamentVariantId = table.Column<Guid>(type: "uuid", nullable: true),
                    NarrowFilamentProfileId = table.Column<Guid>(type: "uuid", nullable: true),
                    CreatedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    TenantId = table.Column<string>(type: "text", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PartFilamentRequirements", x => x.Id);
                    table.ForeignKey(
                        name: "FK_PartFilamentRequirements_Parts_PartId",
                        column: x => x.PartId,
                        principalTable: "Parts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "PartThumbnails",
                columns: table => new
                {
                    PartId = table.Column<Guid>(type: "uuid", nullable: false),
                    PlateIndex = table.Column<int>(type: "integer", nullable: false),
                    OrganizationId = table.Column<Guid>(type: "uuid", nullable: false),
                    Bytes = table.Column<byte[]>(type: "bytea", nullable: false),
                    TenantId = table.Column<string>(type: "text", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PartThumbnails", x => new { x.PartId, x.PlateIndex });
                    table.ForeignKey(
                        name: "FK_PartThumbnails_Parts_PartId",
                        column: x => x.PartId,
                        principalTable: "Parts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "PartTags",
                columns: table => new
                {
                    PartId = table.Column<Guid>(type: "uuid", nullable: false),
                    TagId = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PartTags", x => new { x.PartId, x.TagId });
                    table.ForeignKey(
                        name: "FK_PartTags_Parts_PartId",
                        column: x => x.PartId,
                        principalTable: "Parts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_PartTags_Tags_TagId",
                        column: x => x.TagId,
                        principalTable: "Tags",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "PartProcessingJobs",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    OrganizationId = table.Column<Guid>(type: "uuid", nullable: false),
                    PartId = table.Column<Guid>(type: "uuid", nullable: true),
                    Kind = table.Column<string>(type: "text", nullable: false),
                    Status = table.Column<string>(type: "text", nullable: false),
                    FileFormat = table.Column<string>(type: "text", nullable: false),
                    OriginalFileName = table.Column<string>(type: "text", nullable: false),
                    StoragePath = table.Column<string>(type: "text", nullable: false),
                    FileSizeBytes = table.Column<long>(type: "bigint", nullable: false),
                    RequiresSlicing = table.Column<bool>(type: "boolean", nullable: false),
                    SlicerEngineUsed = table.Column<string>(type: "text", nullable: true),
                    TargetNozzleDiameterMm = table.Column<decimal>(type: "numeric", nullable: true),
                    TargetMaterialType = table.Column<string>(type: "text", nullable: true),
                    PreviewNozzleDiameterMm = table.Column<decimal>(type: "numeric", nullable: true),
                    PreviewEstimateStatus = table.Column<string>(type: "text", nullable: true),
                    PreviewEstimatedPrintTimeMinutes = table.Column<int>(type: "integer", nullable: true),
                    PreviewEstimatedFilamentWeightGrams = table.Column<decimal>(type: "numeric", nullable: true),
                    PreviewPlateCount = table.Column<int>(type: "integer", nullable: true),
                    PreviewFilamentRequirements = table.Column<string>(type: "jsonb", nullable: false),
                    ErrorMessage = table.Column<string>(type: "text", nullable: true),
                    RequestedByUserId = table.Column<Guid>(type: "uuid", nullable: true),
                    CreatedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    ProcessedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    ResolvedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    TenantId = table.Column<string>(type: "text", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PartProcessingJobs", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "PartProcessingJobPreviewThumbnails",
                columns: table => new
                {
                    JobId = table.Column<Guid>(type: "uuid", nullable: false),
                    PlateIndex = table.Column<int>(type: "integer", nullable: false),
                    OrganizationId = table.Column<Guid>(type: "uuid", nullable: false),
                    Bytes = table.Column<byte[]>(type: "bytea", nullable: false),
                    TenantId = table.Column<string>(type: "text", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PartProcessingJobPreviewThumbnails", x => new { x.JobId, x.PlateIndex });
                    table.ForeignKey(
                        name: "FK_PartProcessingJobPreviewThumbnails_PartProcessingJobs_JobId",
                        column: x => x.JobId,
                        principalTable: "PartProcessingJobs",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Tags_OrganizationId_Name",
                table: "Tags",
                columns: new[] { "OrganizationId", "Name" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_PartFilamentRequirements_PartId_MaterialType_DetectedColorHex",
                table: "PartFilamentRequirements",
                columns: new[] { "PartId", "MaterialType", "DetectedColorHex" });

            migrationBuilder.CreateIndex(
                name: "IX_PartTags_TagId",
                table: "PartTags",
                column: "TagId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "PartProcessingJobPreviewThumbnails");

            migrationBuilder.DropTable(
                name: "PartFilamentRequirements");

            migrationBuilder.DropTable(
                name: "PartThumbnails");

            migrationBuilder.DropTable(
                name: "PartTags");

            migrationBuilder.DropTable(
                name: "PartProcessingJobs");

            migrationBuilder.DropTable(
                name: "Tags");

            migrationBuilder.DropTable(
                name: "Parts");
        }
    }
}
