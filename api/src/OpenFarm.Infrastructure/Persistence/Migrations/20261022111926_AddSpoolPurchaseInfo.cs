using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace OpenFarm.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddSpoolPurchaseInfo : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "OrderNumber",
                table: "Spools",
                type: "character varying(100)",
                maxLength: 100,
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "Price",
                table: "Spools",
                type: "numeric",
                nullable: true);

            migrationBuilder.AddColumn<DateOnly>(
                name: "ReceivedOn",
                table: "Spools",
                type: "date",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Supplier",
                table: "Spools",
                type: "character varying(100)",
                maxLength: 100,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "OrderNumber",
                table: "Spools");

            migrationBuilder.DropColumn(
                name: "Price",
                table: "Spools");

            migrationBuilder.DropColumn(
                name: "ReceivedOn",
                table: "Spools");

            migrationBuilder.DropColumn(
                name: "Supplier",
                table: "Spools");
        }
    }
}
