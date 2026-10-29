using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable
namespace OpenFarm.Infrastructure.Persistence.Migrations;

public partial class RemoveEstimationDefaults : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropColumn(name: "EstimationMachineProfileId", table: "FarmSettings");
        migrationBuilder.DropColumn(name: "EstimationProcessProfileId", table: "FarmSettings");
        migrationBuilder.DropColumn(name: "EstimationFilamentProfileId", table: "FarmSettings");
        migrationBuilder.DropColumn(name: "EstimationBuildPlateType", table: "FarmSettings");
    }
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<System.Guid>(name: "EstimationMachineProfileId", table: "FarmSettings", type: "uuid", nullable: true);
        migrationBuilder.AddColumn<System.Guid>(name: "EstimationProcessProfileId", table: "FarmSettings", type: "uuid", nullable: true);
        migrationBuilder.AddColumn<System.Guid>(name: "EstimationFilamentProfileId", table: "FarmSettings", type: "uuid", nullable: true);
        migrationBuilder.AddColumn<string>(name: "EstimationBuildPlateType", table: "FarmSettings", type: "text", nullable: true);
    }
}
