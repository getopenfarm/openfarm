using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace OpenFarm.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class NeutralFeedReadings : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // The live telemetry cache is reset, not converted: dispatch waits for a nozzle confirmed on the current
            // connection anyway, and a printer's first full report after the restart refills it.
            migrationBuilder.DropColumn(
                name: "ActiveTrayId",
                table: "Printers");

            migrationBuilder.DropColumn(
                name: "ExternalSpoolTray",
                table: "Printers");

            migrationBuilder.DropColumn(
                name: "AmsUnits",
                table: "Printers");

            migrationBuilder.AddColumn<string>(
                name: "FeedReadings",
                table: "Printers",
                type: "jsonb",
                nullable: false,
                defaultValueSql: "'[]'::jsonb");

            migrationBuilder.AddColumn<string>(
                name: "Extruders",
                table: "Printers",
                type: "jsonb",
                nullable: false,
                defaultValueSql: "'[]'::jsonb");

            migrationBuilder.AddColumn<int>(
                name: "ExtruderIndex",
                table: "FeedSlots",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            // The U1's toolheads are one unit whose slot n is the spool on head n.
            migrationBuilder.Sql("""
                UPDATE "FeedSlots" AS s SET "ExtruderIndex" = s."SlotPosition"
                FROM "FeedUnits" AS u JOIN "Printers" AS p ON p."Id" = u."PrinterId"
                WHERE s."FeedUnitId" = u."Id" AND u."Kind" = 'MultiMaterialUnit' AND p."Brand" = 'Snapmaker' AND p."ModelCode" = 'U1';
                """);

            migrationBuilder.AddColumn<string>(
                name: "ObservedSlots",
                table: "ExternalPrints",
                type: "jsonb",
                nullable: false,
                defaultValueSql: "'[]'::jsonb");

            // Bambu tray ids (254 = the external spool, 128+ = an AMS HT unit, else unit * 4 + slot) become feed slots, in
            // order, so each stays paired with its ObservedSpoolIds entry.
            migrationBuilder.Sql("""
                UPDATE "ExternalPrints" SET "ObservedSlots" = (
                    SELECT coalesce(jsonb_agg(jsonb_build_object(
                        'Kind', CASE WHEN t = 254 THEN 'ExternalSpool' ELSE 'MultiMaterialUnit' END,
                        'UnitIndex', CASE WHEN t = 254 THEN 0 WHEN t >= 128 THEN t ELSE t / 4 END,
                        'SlotPosition', CASE WHEN t = 254 OR t >= 128 THEN 0 ELSE t % 4 END) ORDER BY i), '[]'::jsonb)
                    FROM unnest("ObservedTrayIds") WITH ORDINALITY AS o(t, i));
                """);

            migrationBuilder.DropColumn(
                name: "ObservedTrayIds",
                table: "ExternalPrints");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int[]>(
                name: "ObservedTrayIds",
                table: "ExternalPrints",
                type: "integer[]",
                nullable: false,
                defaultValue: new int[0]);

            migrationBuilder.Sql("""
                UPDATE "ExternalPrints" SET "ObservedTrayIds" = coalesce((
                    SELECT array_agg(CASE
                        WHEN o->>'Kind' = 'ExternalSpool' THEN 254
                        WHEN (o->>'UnitIndex')::int >= 128 THEN (o->>'UnitIndex')::int
                        ELSE (o->>'UnitIndex')::int * 4 + (o->>'SlotPosition')::int END ORDER BY i)
                    FROM jsonb_array_elements("ObservedSlots") WITH ORDINALITY AS e(o, i)), '{}');
                """);

            migrationBuilder.DropColumn(
                name: "ObservedSlots",
                table: "ExternalPrints");

            migrationBuilder.DropColumn(
                name: "ExtruderIndex",
                table: "FeedSlots");

            migrationBuilder.DropColumn(
                name: "Extruders",
                table: "Printers");

            migrationBuilder.DropColumn(
                name: "FeedReadings",
                table: "Printers");

            migrationBuilder.AddColumn<string>(
                name: "AmsUnits",
                table: "Printers",
                type: "jsonb",
                nullable: false,
                defaultValueSql: "'[]'::jsonb");

            migrationBuilder.AddColumn<string>(
                name: "ExternalSpoolTray",
                table: "Printers",
                type: "jsonb",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "ActiveTrayId",
                table: "Printers",
                type: "integer",
                nullable: true);
        }
    }
}
