using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace OpenFarm.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddAssembliesAndBom : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "Assemblies",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    OrganizationId = table.Column<Guid>(type: "uuid", nullable: false),
                    Name = table.Column<string>(type: "text", nullable: false),
                    Description = table.Column<string>(type: "text", nullable: true),
                    CreatedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    TenantId = table.Column<string>(type: "text", nullable: false),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Assemblies", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "NonPrintedComponents",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    OrganizationId = table.Column<Guid>(type: "uuid", nullable: false),
                    Name = table.Column<string>(type: "text", nullable: false),
                    Unit = table.Column<string>(type: "text", nullable: false),
                    UnitCost = table.Column<decimal>(type: "numeric", nullable: true),
                    CreatedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    TenantId = table.Column<string>(type: "text", nullable: false),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_NonPrintedComponents", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "BomLines",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    OrganizationId = table.Column<Guid>(type: "uuid", nullable: false),
                    AssemblyId = table.Column<Guid>(type: "uuid", nullable: false),
                    TargetKind = table.Column<string>(type: "text", nullable: false),
                    TargetPartId = table.Column<Guid>(type: "uuid", nullable: true),
                    TargetAssemblyId = table.Column<Guid>(type: "uuid", nullable: true),
                    TargetNonPrintedComponentId = table.Column<Guid>(type: "uuid", nullable: true),
                    Quantity = table.Column<decimal>(type: "numeric", nullable: false),
                    TenantId = table.Column<string>(type: "text", nullable: false)
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
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "BomLines");

            migrationBuilder.DropTable(
                name: "Assemblies");

            migrationBuilder.DropTable(
                name: "NonPrintedComponents");
        }
    }
}
