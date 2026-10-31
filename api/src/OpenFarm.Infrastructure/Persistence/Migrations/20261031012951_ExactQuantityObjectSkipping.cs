using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace OpenFarm.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class ExactQuantityObjectSkipping : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "ObjectExecutionJson",
                table: "PrintJobs",
                type: "jsonb",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "TargetUnits",
                table: "PrintJobs",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ObjectManifestJson",
                table: "Parts",
                type: "jsonb",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                DO $$ BEGIN
                  IF EXISTS (SELECT 1 FROM "PrintJobs" WHERE "ObjectExecutionJson" IS NOT NULL OR "TargetUnits" <> "PlateQuantity")
                     OR EXISTS (SELECT 1 FROM "Parts" WHERE "ObjectManifestJson" IS NOT NULL) THEN
                    RAISE EXCEPTION 'Cannot downgrade while object mappings or exact-quantity executions would be lost.';
                  END IF;
                END $$;
                """);
            migrationBuilder.DropColumn(
                name: "ObjectExecutionJson",
                table: "PrintJobs");

            migrationBuilder.DropColumn(
                name: "TargetUnits",
                table: "PrintJobs");

            migrationBuilder.DropColumn(
                name: "ObjectManifestJson",
                table: "Parts");
        }
    }
}
