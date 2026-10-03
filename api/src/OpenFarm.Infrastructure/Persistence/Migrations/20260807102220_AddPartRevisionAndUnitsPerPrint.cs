using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace OpenFarm.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddPartRevisionAndUnitsPerPrint : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "RevisionHash",
                table: "Parts",
                type: "text",
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<int>(
                name: "RevisionNumber",
                table: "Parts",
                type: "integer",
                nullable: false,
                defaultValue: 1);

            migrationBuilder.AddColumn<int>(
                name: "UnitsPerPrint",
                table: "Parts",
                type: "integer",
                nullable: false,
                defaultValue: 1);

            // One-time data backfill (data-model.md) — every existing Part row leaves the migration
            // with a real, stable, per-row RevisionHash rather than the placeholder empty-string
            // default. A true SHA-256 of the file's actual bytes (what Create/ReplaceFile compute
            // going forward, research.md R6) isn't reachable from pure SQL — the file lives on the
            // app's configured local-disk storage root, outside anything Postgres can read without
            // superuser filesystem access. md5(Id || StoragePath) is used instead: deterministic,
            // stable, and collision-free across existing rows. This is safe specifically because a
            // pre-existing row's StoragePath is left untouched by this migration (still the old
            // owner-GUID-keyed convention) — RevisionHash only needs to change when the file
            // actually changes (ReplaceFile, always with a real content hash from then on), never to
            // literally match its own StoragePath.
            migrationBuilder.Sql(
                """
                UPDATE "Parts" SET "RevisionHash" = md5("Id"::text || "StoragePath") WHERE "RevisionHash" = '';
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "RevisionHash",
                table: "Parts");

            migrationBuilder.DropColumn(
                name: "RevisionNumber",
                table: "Parts");

            migrationBuilder.DropColumn(
                name: "UnitsPerPrint",
                table: "Parts");
        }
    }
}
