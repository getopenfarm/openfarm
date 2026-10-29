using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace OpenFarm.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddFarmCurrency : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "CurrencyCode",
                table: "FarmSettings",
                type: "character varying(3)",
                maxLength: 3,
                nullable: false,
                defaultValue: "USD");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "CurrencyCode",
                table: "FarmSettings");
        }
    }
}
