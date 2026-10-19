using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace OpenFarm.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddCostCatalogAndAssemblyCosts : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "Category",
                table: "NonPrintedComponents",
                type: "text",
                nullable: false,
                defaultValue: "Hardware");

            migrationBuilder.AddColumn<string>(
                name: "ItemType",
                table: "NonPrintedComponents",
                type: "text",
                nullable: false,
                defaultValue: "Physical");

            migrationBuilder.AddColumn<decimal>(
                name: "Length",
                table: "NonPrintedComponents",
                type: "numeric",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "LengthUnit",
                table: "NonPrintedComponents",
                type: "text",
                nullable: false,
                defaultValue: "m");

            migrationBuilder.AddColumn<string>(
                name: "PackageLabel",
                table: "NonPrintedComponents",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "PackagePrice",
                table: "NonPrintedComponents",
                type: "numeric",
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "PackageQuantity",
                table: "NonPrintedComponents",
                type: "numeric",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "PricingMode",
                table: "NonPrintedComponents",
                type: "text",
                nullable: false,
                defaultValue: "Direct");

            migrationBuilder.AddColumn<string>(
                name: "Unit",
                table: "NonPrintedComponents",
                type: "text",
                nullable: false,
                defaultValue: "Piece");

            migrationBuilder.AddColumn<decimal>(
                name: "Width",
                table: "NonPrintedComponents",
                type: "numeric",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "WidthUnit",
                table: "NonPrintedComponents",
                type: "text",
                nullable: false,
                defaultValue: "m");

            migrationBuilder.CreateTable(
                name: "AssemblyCostLines",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    AssemblyId = table.Column<Guid>(type: "uuid", nullable: false),
                    CatalogItemId = table.Column<Guid>(type: "uuid", nullable: false),
                    Purpose = table.Column<string>(type: "text", nullable: false),
                    Quantity = table.Column<decimal>(type: "numeric", nullable: false),
                    InputMode = table.Column<string>(type: "text", nullable: false),
                    Length = table.Column<decimal>(type: "numeric", nullable: true),
                    LengthUnit = table.Column<string>(type: "text", nullable: false),
                    Width = table.Column<decimal>(type: "numeric", nullable: true),
                    WidthUnit = table.Column<string>(type: "text", nullable: false),
                    Pieces = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AssemblyCostLines", x => x.Id);
                    table.ForeignKey(
                        name: "FK_AssemblyCostLines_Assemblies_AssemblyId",
                        column: x => x.AssemblyId,
                        principalTable: "Assemblies",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_AssemblyCostLines_NonPrintedComponents_CatalogItemId",
                        column: x => x.CatalogItemId,
                        principalTable: "NonPrintedComponents",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_AssemblyCostLines_AssemblyId_CatalogItemId",
                table: "AssemblyCostLines",
                columns: new[] { "AssemblyId", "CatalogItemId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_AssemblyCostLines_CatalogItemId",
                table: "AssemblyCostLines",
                column: "CatalogItemId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "AssemblyCostLines");

            migrationBuilder.DropColumn(
                name: "Category",
                table: "NonPrintedComponents");

            migrationBuilder.DropColumn(
                name: "ItemType",
                table: "NonPrintedComponents");

            migrationBuilder.DropColumn(
                name: "Length",
                table: "NonPrintedComponents");

            migrationBuilder.DropColumn(
                name: "LengthUnit",
                table: "NonPrintedComponents");

            migrationBuilder.DropColumn(
                name: "PackageLabel",
                table: "NonPrintedComponents");

            migrationBuilder.DropColumn(
                name: "PackagePrice",
                table: "NonPrintedComponents");

            migrationBuilder.DropColumn(
                name: "PackageQuantity",
                table: "NonPrintedComponents");

            migrationBuilder.DropColumn(
                name: "PricingMode",
                table: "NonPrintedComponents");

            migrationBuilder.DropColumn(
                name: "Unit",
                table: "NonPrintedComponents");

            migrationBuilder.DropColumn(
                name: "Width",
                table: "NonPrintedComponents");

            migrationBuilder.DropColumn(
                name: "WidthUnit",
                table: "NonPrintedComponents");
        }
    }
}
