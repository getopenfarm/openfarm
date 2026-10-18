using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace OpenFarm.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class InitialCreate : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "ActivityLogEntries",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Details = table.Column<string>(type: "jsonb", nullable: true),
                    EntityType = table.Column<string>(type: "text", nullable: false),
                    EntityId = table.Column<string>(type: "text", nullable: false),
                    EventType = table.Column<string>(type: "text", nullable: false),
                    ActorId = table.Column<Guid>(type: "uuid", nullable: true),
                    ActorType = table.Column<string>(type: "text", nullable: false),
                    TimestampUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ActivityLogEntries", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "AspNetRoles",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Name = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    NormalizedName = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    ConcurrencyStamp = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AspNetRoles", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "AspNetUsers",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    UserName = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    NormalizedUserName = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    Email = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    NormalizedEmail = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    EmailConfirmed = table.Column<bool>(type: "boolean", nullable: false),
                    PasswordHash = table.Column<string>(type: "text", nullable: true),
                    SecurityStamp = table.Column<string>(type: "text", nullable: true),
                    ConcurrencyStamp = table.Column<string>(type: "text", nullable: true),
                    PhoneNumber = table.Column<string>(type: "text", nullable: true),
                    PhoneNumberConfirmed = table.Column<bool>(type: "boolean", nullable: false),
                    TwoFactorEnabled = table.Column<bool>(type: "boolean", nullable: false),
                    LockoutEnd = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    LockoutEnabled = table.Column<bool>(type: "boolean", nullable: false),
                    AccessFailedCount = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AspNetUsers", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Assemblies",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Name = table.Column<string>(type: "text", nullable: false),
                    Description = table.Column<string>(type: "text", nullable: true),
                    CreatedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Assemblies", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "ConnectionSecrets",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    PrinterSerialNumber = table.Column<string>(type: "text", nullable: false),
                    EncryptedAccessCode = table.Column<string>(type: "text", nullable: false),
                    CreatedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ConnectionSecrets", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "FarmSettings",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    DefaultSlicerEngine = table.Column<string>(type: "text", nullable: false),
                    DefaultSlicerVersion = table.Column<string>(type: "text", nullable: true),
                    EstimationMachineProfileId = table.Column<Guid>(type: "uuid", nullable: true),
                    EstimationProcessProfileId = table.Column<Guid>(type: "uuid", nullable: true),
                    EstimationFilamentProfileId = table.Column<Guid>(type: "uuid", nullable: true),
                    EstimationBuildPlateType = table.Column<string>(type: "text", nullable: true),
                    CreatedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_FarmSettings", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "FeedUnits",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    PrinterId = table.Column<Guid>(type: "uuid", nullable: false),
                    Kind = table.Column<string>(type: "text", nullable: false),
                    UnitIndex = table.Column<int>(type: "integer", nullable: false),
                    Name = table.Column<string>(type: "text", nullable: true),
                    ReplacesFeedUnitId = table.Column<Guid>(type: "uuid", nullable: true),
                    ArchivedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    FeedingSlotId = table.Column<Guid>(type: "uuid", nullable: true),
                    FeedingSelectionSource = table.Column<string>(type: "text", nullable: true),
                    IsRevealed = table.Column<bool>(type: "boolean", nullable: false),
                    CreatedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
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
                name: "FilamentProfiles",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Name = table.Column<string>(type: "text", nullable: false),
                    SourceFormat = table.Column<string>(type: "text", nullable: false),
                    BaseProfileName = table.Column<string>(type: "text", nullable: true),
                    BaseProfileId = table.Column<Guid>(type: "uuid", nullable: true),
                    RawDataJson = table.Column<string>(type: "jsonb", nullable: false),
                    CreatedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    Origin = table.Column<string>(type: "text", nullable: false),
                    VendorName = table.Column<string>(type: "text", nullable: true),
                    ExternalId = table.Column<string>(type: "text", nullable: true),
                    IsCurrentlyOffered = table.Column<bool>(type: "boolean", nullable: false),
                    IsSelectable = table.Column<bool>(type: "boolean", nullable: false),
                    CompatibleMachineNames = table.Column<string>(type: "jsonb", nullable: true),
                    MaterialType = table.Column<string>(type: "text", nullable: true),
                    IncludedTemplateNames = table.Column<string>(type: "jsonb", nullable: true)
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
                    Name = table.Column<string>(type: "text", nullable: false),
                    SourceFormat = table.Column<string>(type: "text", nullable: false),
                    BaseProfileName = table.Column<string>(type: "text", nullable: true),
                    BaseProfileId = table.Column<Guid>(type: "uuid", nullable: true),
                    RawDataJson = table.Column<string>(type: "jsonb", nullable: false),
                    CreatedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    Origin = table.Column<string>(type: "text", nullable: false),
                    VendorName = table.Column<string>(type: "text", nullable: true),
                    ExternalId = table.Column<string>(type: "text", nullable: true),
                    IsCurrentlyOffered = table.Column<bool>(type: "boolean", nullable: false),
                    IsSelectable = table.Column<bool>(type: "boolean", nullable: false),
                    VendorMachineModelName = table.Column<string>(type: "text", nullable: true),
                    IncludedTemplateNames = table.Column<string>(type: "jsonb", nullable: true)
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

            migrationBuilder.CreateTable(
                name: "NonPrintedComponents",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Name = table.Column<string>(type: "text", nullable: false),
                    UnitCost = table.Column<decimal>(type: "numeric", nullable: true),
                    CreatedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_NonPrintedComponents", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "PartProcessingJobs",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    PartId = table.Column<Guid>(type: "uuid", nullable: true),
                    Kind = table.Column<string>(type: "text", nullable: false),
                    Status = table.Column<string>(type: "text", nullable: false),
                    FileFormat = table.Column<string>(type: "text", nullable: false),
                    OriginalFileName = table.Column<string>(type: "text", nullable: false),
                    StoragePath = table.Column<string>(type: "text", nullable: false),
                    FileSizeBytes = table.Column<long>(type: "bigint", nullable: false),
                    RequiresSlicing = table.Column<bool>(type: "boolean", nullable: false),
                    SlicerEngineUsed = table.Column<string>(type: "text", nullable: true),
                    SlicerVersionUsed = table.Column<string>(type: "text", nullable: true),
                    TargetNozzleDiameterMm = table.Column<decimal>(type: "numeric", nullable: true),
                    TargetMaterialType = table.Column<string>(type: "text", nullable: true),
                    SourcePartUpdatedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    SourceRevisionHash = table.Column<string>(type: "text", nullable: true),
                    PreviewMaterialMetadataComplete = table.Column<bool>(type: "boolean", nullable: false, defaultValue: true),
                    EstimateUnavailableReason = table.Column<string>(type: "text", nullable: true),
                    EstimationConfigurationJson = table.Column<string>(type: "jsonb", nullable: true),
                    MetadataWarningsJson = table.Column<string>(type: "jsonb", nullable: true),
                    EstimationDetailsJson = table.Column<string>(type: "jsonb", nullable: true),
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
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PartProcessingJobs", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Parts",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Name = table.Column<string>(type: "text", nullable: false),
                    FileFormat = table.Column<string>(type: "text", nullable: false),
                    RequiresSlicing = table.Column<bool>(type: "boolean", nullable: false),
                    OriginalFileName = table.Column<string>(type: "text", nullable: false),
                    StoragePath = table.Column<string>(type: "text", nullable: false),
                    FileSizeBytes = table.Column<long>(type: "bigint", nullable: false),
                    FileUploadedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    NozzleDiameterMm = table.Column<decimal>(type: "numeric", nullable: true),
                    MaterialMetadataComplete = table.Column<bool>(type: "boolean", nullable: false, defaultValue: true),
                    ActiveProcessingJobId = table.Column<Guid>(type: "uuid", nullable: true),
                    EstimateUnavailableReason = table.Column<string>(type: "text", nullable: true),
                    MetadataWarningsJson = table.Column<string>(type: "jsonb", nullable: true),
                    EstimationDetailsJson = table.Column<string>(type: "jsonb", nullable: true),
                    EstimateStatus = table.Column<string>(type: "text", nullable: false),
                    EstimatedPrintTimeMinutes = table.Column<int>(type: "integer", nullable: true),
                    EstimatedFilamentWeightGrams = table.Column<decimal>(type: "numeric", nullable: true),
                    SlicerEngineOverride = table.Column<string>(type: "text", nullable: true),
                    SlicerVersionUsed = table.Column<string>(type: "text", nullable: true),
                    PlateCount = table.Column<int>(type: "integer", nullable: false),
                    UnitsPerPrint = table.Column<int>(type: "integer", nullable: false),
                    RevisionHash = table.Column<string>(type: "text", nullable: false),
                    RevisionNumber = table.Column<int>(type: "integer", nullable: false),
                    CreatedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Parts", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "ProcessProfiles",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Name = table.Column<string>(type: "text", nullable: false),
                    SourceFormat = table.Column<string>(type: "text", nullable: false),
                    BaseProfileName = table.Column<string>(type: "text", nullable: true),
                    BaseProfileId = table.Column<Guid>(type: "uuid", nullable: true),
                    RawDataJson = table.Column<string>(type: "jsonb", nullable: false),
                    CreatedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    Origin = table.Column<string>(type: "text", nullable: false),
                    VendorName = table.Column<string>(type: "text", nullable: true),
                    ExternalId = table.Column<string>(type: "text", nullable: true),
                    IsCurrentlyOffered = table.Column<bool>(type: "boolean", nullable: false),
                    IsSelectable = table.Column<bool>(type: "boolean", nullable: false),
                    CompatibleMachineNames = table.Column<string>(type: "jsonb", nullable: true)
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

            migrationBuilder.CreateTable(
                name: "ProductionRuns",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Name = table.Column<string>(type: "text", nullable: true),
                    DueDate = table.Column<DateOnly>(type: "date", nullable: true),
                    Priority = table.Column<string>(type: "text", nullable: true),
                    Notes = table.Column<string>(type: "text", nullable: true),
                    Status = table.Column<string>(type: "text", nullable: false),
                    CreatedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ProductionRuns", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Tags",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Name = table.Column<string>(type: "text", nullable: false),
                    CreatedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Tags", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Vendors",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Name = table.Column<string>(type: "text", nullable: false),
                    CreatedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Vendors", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "AspNetRoleClaims",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    RoleId = table.Column<Guid>(type: "uuid", nullable: false),
                    ClaimType = table.Column<string>(type: "text", nullable: true),
                    ClaimValue = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AspNetRoleClaims", x => x.Id);
                    table.ForeignKey(
                        name: "FK_AspNetRoleClaims_AspNetRoles_RoleId",
                        column: x => x.RoleId,
                        principalTable: "AspNetRoles",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "AspNetUserClaims",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    UserId = table.Column<Guid>(type: "uuid", nullable: false),
                    ClaimType = table.Column<string>(type: "text", nullable: true),
                    ClaimValue = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AspNetUserClaims", x => x.Id);
                    table.ForeignKey(
                        name: "FK_AspNetUserClaims_AspNetUsers_UserId",
                        column: x => x.UserId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "AspNetUserLogins",
                columns: table => new
                {
                    LoginProvider = table.Column<string>(type: "text", nullable: false),
                    ProviderKey = table.Column<string>(type: "text", nullable: false),
                    ProviderDisplayName = table.Column<string>(type: "text", nullable: true),
                    UserId = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AspNetUserLogins", x => new { x.LoginProvider, x.ProviderKey });
                    table.ForeignKey(
                        name: "FK_AspNetUserLogins_AspNetUsers_UserId",
                        column: x => x.UserId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "AspNetUserRoles",
                columns: table => new
                {
                    UserId = table.Column<Guid>(type: "uuid", nullable: false),
                    RoleId = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AspNetUserRoles", x => new { x.UserId, x.RoleId });
                    table.ForeignKey(
                        name: "FK_AspNetUserRoles_AspNetRoles_RoleId",
                        column: x => x.RoleId,
                        principalTable: "AspNetRoles",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_AspNetUserRoles_AspNetUsers_UserId",
                        column: x => x.UserId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "AspNetUserTokens",
                columns: table => new
                {
                    UserId = table.Column<Guid>(type: "uuid", nullable: false),
                    LoginProvider = table.Column<string>(type: "text", nullable: false),
                    Name = table.Column<string>(type: "text", nullable: false),
                    Value = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AspNetUserTokens", x => new { x.UserId, x.LoginProvider, x.Name });
                    table.ForeignKey(
                        name: "FK_AspNetUserTokens_AspNetUsers_UserId",
                        column: x => x.UserId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "FeedSlots",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    FeedUnitId = table.Column<Guid>(type: "uuid", nullable: false),
                    SlotPosition = table.Column<int>(type: "integer", nullable: false),
                    DeactivatedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    CreatedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
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
                name: "PartProcessingJobPreviewThumbnails",
                columns: table => new
                {
                    JobId = table.Column<Guid>(type: "uuid", nullable: false),
                    PlateIndex = table.Column<int>(type: "integer", nullable: false),
                    Bytes = table.Column<byte[]>(type: "bytea", nullable: false)
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

            migrationBuilder.CreateTable(
                name: "BomLines",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    AssemblyId = table.Column<Guid>(type: "uuid", nullable: false),
                    TargetKind = table.Column<string>(type: "text", nullable: false),
                    TargetPartId = table.Column<Guid>(type: "uuid", nullable: true),
                    TargetAssemblyId = table.Column<Guid>(type: "uuid", nullable: true),
                    TargetNonPrintedComponentId = table.Column<Guid>(type: "uuid", nullable: true),
                    Quantity = table.Column<decimal>(type: "numeric", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_BomLines", x => x.Id);
                    table.CheckConstraint("CK_BomLines_ExactlyOneTargetMatchingKind", "(\"TargetKind\" = 'Part' AND \"TargetPartId\" IS NOT NULL AND \"TargetAssemblyId\" IS NULL AND \"TargetNonPrintedComponentId\" IS NULL) OR\n(\"TargetKind\" = 'Assembly' AND \"TargetAssemblyId\" IS NOT NULL AND \"TargetPartId\" IS NULL AND \"TargetNonPrintedComponentId\" IS NULL) OR\n(\"TargetKind\" = 'NonPrintedComponent' AND \"TargetNonPrintedComponentId\" IS NOT NULL AND \"TargetPartId\" IS NULL AND \"TargetAssemblyId\" IS NULL)");
                    table.CheckConstraint("CK_BomLines_NoDirectSelfReference", "\"TargetAssemblyId\" IS NULL OR \"TargetAssemblyId\" <> \"AssemblyId\"");
                    table.ForeignKey(
                        name: "FK_BomLines_Assemblies_AssemblyId",
                        column: x => x.AssemblyId,
                        principalTable: "Assemblies",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_BomLines_Assemblies_TargetAssemblyId",
                        column: x => x.TargetAssemblyId,
                        principalTable: "Assemblies",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_BomLines_NonPrintedComponents_TargetNonPrintedComponentId",
                        column: x => x.TargetNonPrintedComponentId,
                        principalTable: "NonPrintedComponents",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_BomLines_Parts_TargetPartId",
                        column: x => x.TargetPartId,
                        principalTable: "Parts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "PartFilamentRequirements",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    PartId = table.Column<Guid>(type: "uuid", nullable: false),
                    MaterialType = table.Column<string>(type: "text", nullable: true),
                    DetectedColorHex = table.Column<string>(type: "text", nullable: true),
                    DetectedSubBrand = table.Column<string>(type: "text", nullable: true),
                    NarrowFilamentVariantId = table.Column<Guid>(type: "uuid", nullable: true),
                    NarrowFilamentProfileId = table.Column<Guid>(type: "uuid", nullable: true),
                    CreatedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
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
                    Bytes = table.Column<byte[]>(type: "bytea", nullable: false)
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
                name: "Printers",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Name = table.Column<string>(type: "text", nullable: false),
                    Brand = table.Column<string>(type: "text", nullable: false),
                    ModelCode = table.Column<string>(type: "text", nullable: false),
                    IpAddress = table.Column<string>(type: "text", nullable: false),
                    SerialNumber = table.Column<string>(type: "text", nullable: false),
                    Status = table.Column<string>(type: "text", nullable: false),
                    ReportedState = table.Column<string>(type: "text", nullable: true),
                    StateObservedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    ActiveAlerts = table.Column<string>(type: "jsonb", nullable: false),
                    IsReachable = table.Column<bool>(type: "boolean", nullable: false),
                    LastMessageUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    ReportedNozzleDiameterMm = table.Column<decimal>(type: "numeric", nullable: true),
                    NozzleObservedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    NozzleConfirmed = table.Column<bool>(type: "boolean", nullable: false),
                    NozzleTempC = table.Column<decimal>(type: "numeric", nullable: true),
                    BedTempC = table.Column<decimal>(type: "numeric", nullable: true),
                    PrintProgressPercent = table.Column<int>(type: "integer", nullable: true),
                    ActiveTrayId = table.Column<int>(type: "integer", nullable: true),
                    CurrentLayer = table.Column<int>(type: "integer", nullable: true),
                    TotalLayers = table.Column<int>(type: "integer", nullable: true),
                    CurrentStageLabel = table.Column<string>(type: "text", nullable: true),
                    RemainingTimeMinutes = table.Column<int>(type: "integer", nullable: true),
                    CurrentJobName = table.Column<string>(type: "text", nullable: true),
                    CurrentGcodeFile = table.Column<string>(type: "text", nullable: true),
                    AmsUnits = table.Column<string>(type: "jsonb", nullable: false),
                    ExternalSpoolTray = table.Column<string>(type: "jsonb", nullable: true),
                    MachineProfileId = table.Column<Guid>(type: "uuid", nullable: true),
                    ProcessProfileId = table.Column<Guid>(type: "uuid", nullable: true),
                    BuildPlateType = table.Column<string>(type: "text", nullable: true),
                    CreatedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Printers", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Printers_MachineProfiles_MachineProfileId",
                        column: x => x.MachineProfileId,
                        principalTable: "MachineProfiles",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_Printers_ProcessProfiles_ProcessProfileId",
                        column: x => x.ProcessProfileId,
                        principalTable: "ProcessProfiles",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
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
                name: "Filaments",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    VendorId = table.Column<Guid>(type: "uuid", nullable: false),
                    Material = table.Column<string>(type: "text", nullable: false),
                    MaterialType = table.Column<string>(type: "text", nullable: false),
                    CreatedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
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
                name: "Prints",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    PrinterId = table.Column<Guid>(type: "uuid", nullable: true),
                    Name = table.Column<string>(type: "text", nullable: false),
                    Source = table.Column<string>(type: "text", nullable: false),
                    Status = table.Column<string>(type: "text", nullable: false),
                    StartedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    EndedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    ActualDurationMinutes = table.Column<int>(type: "integer", nullable: true),
                    CreatedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    StopLayer = table.Column<int>(type: "integer", nullable: true),
                    StopTotalLayers = table.Column<int>(type: "integer", nullable: true),
                    StopPercent = table.Column<int>(type: "integer", nullable: true),
                    FilamentFollowUpDismissedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Prints", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Prints_Printers_PrinterId",
                        column: x => x.PrinterId,
                        principalTable: "Printers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateTable(
                name: "RunLines",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ProductionRunId = table.Column<Guid>(type: "uuid", nullable: false),
                    TargetKind = table.Column<string>(type: "text", nullable: false),
                    TargetId = table.Column<Guid>(type: "uuid", nullable: false),
                    TargetName = table.Column<string>(type: "text", nullable: true),
                    Quantity = table.Column<int>(type: "integer", nullable: true),
                    RevisionMode = table.Column<string>(type: "text", nullable: false),
                    DefaultAssignmentMode = table.Column<string>(type: "text", nullable: false),
                    DefaultPinnedPrinterId = table.Column<Guid>(type: "uuid", nullable: true),
                    BackfillPrinterCap = table.Column<int>(type: "integer", nullable: false),
                    BackfillCumulativeUnitCeiling = table.Column<int>(type: "integer", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_RunLines", x => x.Id);
                    table.CheckConstraint("CK_RunLines_PinnedRequiresPrinter", "\"DefaultAssignmentMode\" = 'Unassigned' OR \"DefaultPinnedPrinterId\" IS NOT NULL");
                    table.CheckConstraint("CK_RunLines_QuantityAtLeastOne", "\"Quantity\" IS NULL OR \"Quantity\" >= 1");
                    table.ForeignKey(
                        name: "FK_RunLines_Printers_DefaultPinnedPrinterId",
                        column: x => x.DefaultPinnedPrinterId,
                        principalTable: "Printers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_RunLines_ProductionRuns_ProductionRunId",
                        column: x => x.ProductionRunId,
                        principalTable: "ProductionRuns",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
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
                name: "ExternalPrints",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ObservedTrayIds = table.Column<int[]>(type: "integer[]", nullable: false),
                    ObservedSpoolIds = table.Column<Guid[]>(type: "uuid[]", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ExternalPrints", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ExternalPrints_Prints_Id",
                        column: x => x.Id,
                        principalTable: "Prints",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "PartRequirements",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    RunLineId = table.Column<Guid>(type: "uuid", nullable: false),
                    PartId = table.Column<Guid>(type: "uuid", nullable: false),
                    LibraryPartId = table.Column<Guid>(type: "uuid", nullable: true),
                    PartName = table.Column<string>(type: "text", nullable: true),
                    Quantity = table.Column<decimal>(type: "numeric", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PartRequirements", x => x.Id);
                    table.ForeignKey(
                        name: "FK_PartRequirements_Parts_LibraryPartId",
                        column: x => x.LibraryPartId,
                        principalTable: "Parts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_PartRequirements_RunLines_RunLineId",
                        column: x => x.RunLineId,
                        principalTable: "RunLines",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "FilamentProfileAssignments",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    FilamentId = table.Column<Guid>(type: "uuid", nullable: false),
                    Brand = table.Column<string>(type: "text", nullable: false),
                    ModelCode = table.Column<string>(type: "text", nullable: false),
                    NozzleDiameterMm = table.Column<decimal>(type: "numeric", nullable: false),
                    FilamentProfileId = table.Column<Guid>(type: "uuid", nullable: false),
                    FilamentVariantId = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_FilamentProfileAssignments", x => x.Id);
                    table.CheckConstraint("CK_FilamentAssignment_Nozzle", "\"NozzleDiameterMm\" > 0");
                    table.ForeignKey(
                        name: "FK_FilamentProfileAssignments_FilamentProfiles_FilamentProfile~",
                        column: x => x.FilamentProfileId,
                        principalTable: "FilamentProfiles",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_FilamentProfileAssignments_FilamentVariants_FilamentVariant~",
                        column: x => x.FilamentVariantId,
                        principalTable: "FilamentVariants",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_FilamentProfileAssignments_Filaments_FilamentId",
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
                    FilamentVariantId = table.Column<Guid>(type: "uuid", nullable: false),
                    Label = table.Column<string>(type: "text", nullable: true),
                    SpoolWeightGrams = table.Column<decimal>(type: "numeric", nullable: false),
                    RemainingWeightGrams = table.Column<decimal>(type: "numeric", nullable: false),
                    HasUsageOverrun = table.Column<bool>(type: "boolean", nullable: false),
                    State = table.Column<string>(type: "text", nullable: false),
                    FeedSlotId = table.Column<Guid>(type: "uuid", nullable: true),
                    CreatedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
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
                name: "PrintJobs",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ProductionSnapshotJson = table.Column<string>(type: "jsonb", nullable: true),
                    SnapshotRecoveryReason = table.Column<string>(type: "text", nullable: true),
                    SnapshotUnavailableAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    PartRequirementId = table.Column<Guid>(type: "uuid", nullable: false),
                    QueuePosition = table.Column<double>(type: "double precision", nullable: false),
                    PlateQuantity = table.Column<int>(type: "integer", nullable: false),
                    AssignmentMode = table.Column<string>(type: "text", nullable: false),
                    PinnedPrinterId = table.Column<Guid>(type: "uuid", nullable: true),
                    FileRevisionHash = table.Column<string>(type: "text", nullable: false),
                    FileRevisionNumber = table.Column<int>(type: "integer", nullable: false),
                    DispatchedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    CancelRequestedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    RequeuedAsJobId = table.Column<Guid>(type: "uuid", nullable: true),
                    HasFilamentRequirementOverride = table.Column<bool>(type: "boolean", nullable: false, defaultValue: false),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PrintJobs", x => x.Id);
                    table.ForeignKey(
                        name: "FK_PrintJobs_PartRequirements_PartRequirementId",
                        column: x => x.PartRequirementId,
                        principalTable: "PartRequirements",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_PrintJobs_PrintJobs_RequeuedAsJobId",
                        column: x => x.RequeuedAsJobId,
                        principalTable: "PrintJobs",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_PrintJobs_Printers_PinnedPrinterId",
                        column: x => x.PinnedPrinterId,
                        principalTable: "Printers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_PrintJobs_Prints_Id",
                        column: x => x.Id,
                        principalTable: "Prints",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "PrintFilaments",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    PrintId = table.Column<Guid>(type: "uuid", nullable: false),
                    ToolIndex = table.Column<int>(type: "integer", nullable: false),
                    MaterialType = table.Column<string>(type: "text", nullable: true),
                    DetectedColorHex = table.Column<string>(type: "text", nullable: true),
                    NarrowFilamentVariantId = table.Column<Guid>(type: "uuid", nullable: true),
                    NarrowFilamentProfileId = table.Column<Guid>(type: "uuid", nullable: true),
                    SpoolId = table.Column<Guid>(type: "uuid", nullable: true),
                    GramsConsumed = table.Column<decimal>(type: "numeric", nullable: true),
                    EstimatedGrams = table.Column<decimal>(type: "numeric", nullable: true),
                    UsageCurve = table.Column<string>(type: "text", nullable: true),
                    SkippedReason = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PrintFilaments", x => x.Id);
                    table.ForeignKey(
                        name: "FK_PrintFilaments_Prints_PrintId",
                        column: x => x.PrintId,
                        principalTable: "Prints",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_PrintFilaments_Spools_SpoolId",
                        column: x => x.SpoolId,
                        principalTable: "Spools",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateTable(
                name: "SpoolUsageRecords",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    SpoolId = table.Column<Guid>(type: "uuid", nullable: false),
                    GramsConsumed = table.Column<decimal>(type: "numeric", nullable: false),
                    RecordedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    JobId = table.Column<Guid>(type: "uuid", nullable: true),
                    Source = table.Column<string>(type: "text", nullable: false)
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
                name: "IX_ActivityLogEntries_EntityType_EntityId",
                table: "ActivityLogEntries",
                columns: new[] { "EntityType", "EntityId" });

            migrationBuilder.CreateIndex(
                name: "IX_ActivityLogEntries_TimestampUtc",
                table: "ActivityLogEntries",
                column: "TimestampUtc");

            migrationBuilder.CreateIndex(
                name: "IX_AspNetRoleClaims_RoleId",
                table: "AspNetRoleClaims",
                column: "RoleId");

            migrationBuilder.CreateIndex(
                name: "RoleNameIndex",
                table: "AspNetRoles",
                column: "NormalizedName",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_AspNetUserClaims_UserId",
                table: "AspNetUserClaims",
                column: "UserId");

            migrationBuilder.CreateIndex(
                name: "IX_AspNetUserLogins_UserId",
                table: "AspNetUserLogins",
                column: "UserId");

            migrationBuilder.CreateIndex(
                name: "IX_AspNetUserRoles_RoleId",
                table: "AspNetUserRoles",
                column: "RoleId");

            migrationBuilder.CreateIndex(
                name: "EmailIndex",
                table: "AspNetUsers",
                column: "NormalizedEmail");

            migrationBuilder.CreateIndex(
                name: "UserNameIndex",
                table: "AspNetUsers",
                column: "NormalizedUserName",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_BomLines_AssemblyId_TargetAssemblyId",
                table: "BomLines",
                columns: new[] { "AssemblyId", "TargetAssemblyId" },
                unique: true,
                filter: "\"TargetAssemblyId\" IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_BomLines_AssemblyId_TargetNonPrintedComponentId",
                table: "BomLines",
                columns: new[] { "AssemblyId", "TargetNonPrintedComponentId" },
                unique: true,
                filter: "\"TargetNonPrintedComponentId\" IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_BomLines_AssemblyId_TargetPartId",
                table: "BomLines",
                columns: new[] { "AssemblyId", "TargetPartId" },
                unique: true,
                filter: "\"TargetPartId\" IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_BomLines_TargetAssemblyId",
                table: "BomLines",
                column: "TargetAssemblyId");

            migrationBuilder.CreateIndex(
                name: "IX_BomLines_TargetNonPrintedComponentId",
                table: "BomLines",
                column: "TargetNonPrintedComponentId");

            migrationBuilder.CreateIndex(
                name: "IX_BomLines_TargetPartId",
                table: "BomLines",
                column: "TargetPartId");

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
                column: "ReplacesFeedUnitId",
                unique: true,
                filter: "\"ArchivedAtUtc\" IS NULL AND \"Kind\" = 'AutomaticFilamentReloader'");

            migrationBuilder.CreateIndex(
                name: "IX_FilamentProfileAssignments_FilamentId_Brand_ModelCode_Nozz~1",
                table: "FilamentProfileAssignments",
                columns: new[] { "FilamentId", "Brand", "ModelCode", "NozzleDiameterMm", "FilamentVariantId" },
                unique: true,
                filter: "\"FilamentVariantId\" IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_FilamentProfileAssignments_FilamentId_Brand_ModelCode_Nozzl~",
                table: "FilamentProfileAssignments",
                columns: new[] { "FilamentId", "Brand", "ModelCode", "NozzleDiameterMm" },
                unique: true,
                filter: "\"FilamentVariantId\" IS NULL");

            migrationBuilder.CreateIndex(
                name: "IX_FilamentProfileAssignments_FilamentProfileId",
                table: "FilamentProfileAssignments",
                column: "FilamentProfileId");

            migrationBuilder.CreateIndex(
                name: "IX_FilamentProfileAssignments_FilamentVariantId",
                table: "FilamentProfileAssignments",
                column: "FilamentVariantId");

            migrationBuilder.CreateIndex(
                name: "IX_FilamentProfiles_BaseProfileId",
                table: "FilamentProfiles",
                column: "BaseProfileId");

            migrationBuilder.CreateIndex(
                name: "IX_FilamentProfiles_SourceFormat_VendorName_ExternalId",
                table: "FilamentProfiles",
                columns: new[] { "SourceFormat", "VendorName", "ExternalId" },
                unique: true,
                filter: "\"Origin\" = 'System' AND \"ExternalId\" IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_FilamentProfiles_SourceFormat_VendorName_Name",
                table: "FilamentProfiles",
                columns: new[] { "SourceFormat", "VendorName", "Name" },
                unique: true,
                filter: "\"Origin\" = 'System' AND \"ExternalId\" IS NULL");

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
                name: "IX_MachineProfiles_BaseProfileId",
                table: "MachineProfiles",
                column: "BaseProfileId");

            migrationBuilder.CreateIndex(
                name: "IX_MachineProfiles_SourceFormat_VendorName_ExternalId",
                table: "MachineProfiles",
                columns: new[] { "SourceFormat", "VendorName", "ExternalId" },
                unique: true,
                filter: "\"Origin\" = 'System' AND \"ExternalId\" IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_MachineProfiles_SourceFormat_VendorName_Name",
                table: "MachineProfiles",
                columns: new[] { "SourceFormat", "VendorName", "Name" },
                unique: true,
                filter: "\"Origin\" = 'System' AND \"ExternalId\" IS NULL");

            migrationBuilder.CreateIndex(
                name: "IX_PartFilamentRequirements_PartId_MaterialType_DetectedColorH~",
                table: "PartFilamentRequirements",
                columns: new[] { "PartId", "MaterialType", "DetectedColorHex" });

            migrationBuilder.CreateIndex(
                name: "IX_PartProcessingJobs_PartId",
                table: "PartProcessingJobs",
                column: "PartId",
                unique: true,
                filter: "\"Kind\" = 'Reprocess' AND \"Status\" IN ('Processing', 'ReadyForReview')");

            migrationBuilder.CreateIndex(
                name: "IX_PartRequirements_LibraryPartId",
                table: "PartRequirements",
                column: "LibraryPartId");

            migrationBuilder.CreateIndex(
                name: "IX_PartRequirements_RunLineId_PartId",
                table: "PartRequirements",
                columns: new[] { "RunLineId", "PartId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_PartTags_TagId",
                table: "PartTags",
                column: "TagId");

            migrationBuilder.CreateIndex(
                name: "IX_Printers_MachineProfileId",
                table: "Printers",
                column: "MachineProfileId");

            migrationBuilder.CreateIndex(
                name: "IX_Printers_ProcessProfileId",
                table: "Printers",
                column: "ProcessProfileId");

            migrationBuilder.CreateIndex(
                name: "IX_Printers_SerialNumber",
                table: "Printers",
                column: "SerialNumber",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_PrintFilaments_PrintId",
                table: "PrintFilaments",
                column: "PrintId");

            migrationBuilder.CreateIndex(
                name: "IX_PrintFilaments_SpoolId",
                table: "PrintFilaments",
                column: "SpoolId");

            migrationBuilder.CreateIndex(
                name: "IX_PrintJobs_AssignmentMode_QueuePosition",
                table: "PrintJobs",
                columns: new[] { "AssignmentMode", "QueuePosition" });

            migrationBuilder.CreateIndex(
                name: "IX_PrintJobs_PartRequirementId",
                table: "PrintJobs",
                column: "PartRequirementId");

            migrationBuilder.CreateIndex(
                name: "IX_PrintJobs_PinnedPrinterId",
                table: "PrintJobs",
                column: "PinnedPrinterId",
                filter: "\"PinnedPrinterId\" IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_PrintJobs_RequeuedAsJobId",
                table: "PrintJobs",
                column: "RequeuedAsJobId");

            migrationBuilder.CreateIndex(
                name: "IX_Prints_PrinterId_Status",
                table: "Prints",
                columns: new[] { "PrinterId", "Status" });

            migrationBuilder.CreateIndex(
                name: "IX_Prints_Source_StartedAtUtc",
                table: "Prints",
                columns: new[] { "Source", "StartedAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_Prints_Status",
                table: "Prints",
                column: "Status");

            migrationBuilder.CreateIndex(
                name: "IX_ProcessProfiles_BaseProfileId",
                table: "ProcessProfiles",
                column: "BaseProfileId");

            migrationBuilder.CreateIndex(
                name: "IX_ProcessProfiles_SourceFormat_VendorName_ExternalId",
                table: "ProcessProfiles",
                columns: new[] { "SourceFormat", "VendorName", "ExternalId" },
                unique: true,
                filter: "\"Origin\" = 'System' AND \"ExternalId\" IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_ProcessProfiles_SourceFormat_VendorName_Name",
                table: "ProcessProfiles",
                columns: new[] { "SourceFormat", "VendorName", "Name" },
                unique: true,
                filter: "\"Origin\" = 'System' AND \"ExternalId\" IS NULL");

            migrationBuilder.CreateIndex(
                name: "IX_RunLines_DefaultPinnedPrinterId",
                table: "RunLines",
                column: "DefaultPinnedPrinterId");

            migrationBuilder.CreateIndex(
                name: "IX_RunLines_ProductionRunId",
                table: "RunLines",
                column: "ProductionRunId");

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
                name: "IX_Spools_State_CreatedAtUtc",
                table: "Spools",
                columns: new[] { "State", "CreatedAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_SpoolUsageRecords_SpoolId",
                table: "SpoolUsageRecords",
                column: "SpoolId");

            migrationBuilder.CreateIndex(
                name: "IX_Tags_Name",
                table: "Tags",
                column: "Name",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Vendors_Name",
                table: "Vendors",
                column: "Name",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ActivityLogEntries");

            migrationBuilder.DropTable(
                name: "AspNetRoleClaims");

            migrationBuilder.DropTable(
                name: "AspNetUserClaims");

            migrationBuilder.DropTable(
                name: "AspNetUserLogins");

            migrationBuilder.DropTable(
                name: "AspNetUserRoles");

            migrationBuilder.DropTable(
                name: "AspNetUserTokens");

            migrationBuilder.DropTable(
                name: "BomLines");

            migrationBuilder.DropTable(
                name: "ConnectionSecrets");

            migrationBuilder.DropTable(
                name: "ExternalPrints");

            migrationBuilder.DropTable(
                name: "FarmSettings");

            migrationBuilder.DropTable(
                name: "FilamentProfileAssignments");

            migrationBuilder.DropTable(
                name: "PartFilamentRequirements");

            migrationBuilder.DropTable(
                name: "PartProcessingJobPreviewThumbnails");

            migrationBuilder.DropTable(
                name: "PartTags");

            migrationBuilder.DropTable(
                name: "PartThumbnails");

            migrationBuilder.DropTable(
                name: "PrintFilaments");

            migrationBuilder.DropTable(
                name: "PrintJobs");

            migrationBuilder.DropTable(
                name: "SpoolUsageRecords");

            migrationBuilder.DropTable(
                name: "AspNetRoles");

            migrationBuilder.DropTable(
                name: "AspNetUsers");

            migrationBuilder.DropTable(
                name: "Assemblies");

            migrationBuilder.DropTable(
                name: "NonPrintedComponents");

            migrationBuilder.DropTable(
                name: "FilamentProfiles");

            migrationBuilder.DropTable(
                name: "PartProcessingJobs");

            migrationBuilder.DropTable(
                name: "Tags");

            migrationBuilder.DropTable(
                name: "PartRequirements");

            migrationBuilder.DropTable(
                name: "Prints");

            migrationBuilder.DropTable(
                name: "Spools");

            migrationBuilder.DropTable(
                name: "Parts");

            migrationBuilder.DropTable(
                name: "RunLines");

            migrationBuilder.DropTable(
                name: "FeedSlots");

            migrationBuilder.DropTable(
                name: "FilamentVariants");

            migrationBuilder.DropTable(
                name: "Printers");

            migrationBuilder.DropTable(
                name: "ProductionRuns");

            migrationBuilder.DropTable(
                name: "FeedUnits");

            migrationBuilder.DropTable(
                name: "Filaments");

            migrationBuilder.DropTable(
                name: "MachineProfiles");

            migrationBuilder.DropTable(
                name: "ProcessProfiles");

            migrationBuilder.DropTable(
                name: "Vendors");
        }
    }
}
