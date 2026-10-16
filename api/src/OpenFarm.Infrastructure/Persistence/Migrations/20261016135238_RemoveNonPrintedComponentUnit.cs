using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace OpenFarm.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class RemoveNonPrintedComponentUnit : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Components are unitless whole pieces now: round any fractional line quantity up.
            migrationBuilder.Sql(
                """
                UPDATE "BomLines" SET "Quantity" = CEILING("Quantity")
                WHERE "TargetKind" = 'NonPrintedComponent' AND "Quantity" <> TRUNC("Quantity");
                """);

            migrationBuilder.DropColumn(
                name: "Unit",
                table: "NonPrintedComponents");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "Unit",
                table: "NonPrintedComponents",
                type: "text",
                nullable: false,
                defaultValue: "each");
        }
    }
}
