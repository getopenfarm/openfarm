using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace OpenFarm.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddFilamentAnyColor : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "NarrowFilamentId",
                table: "PrintFilaments",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "NarrowFilamentId",
                table: "PartFilamentRequirements",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "AnyColor",
                table: "PrintFilaments",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<bool>(
                name: "AnyColor",
                table: "PartFilamentRequirements",
                type: "boolean",
                nullable: false,
                defaultValue: false);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "NarrowFilamentId",
                table: "PrintFilaments");

            migrationBuilder.DropColumn(
                name: "NarrowFilamentId",
                table: "PartFilamentRequirements");

            migrationBuilder.DropColumn(
                name: "AnyColor",
                table: "PrintFilaments");

            migrationBuilder.DropColumn(
                name: "AnyColor",
                table: "PartFilamentRequirements");
        }
    }
}
