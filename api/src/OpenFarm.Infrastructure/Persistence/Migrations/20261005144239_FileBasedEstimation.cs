using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace OpenFarm.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class FileBasedEstimation : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "EstimationDetailsJson",
                table: "Parts",
                type: "jsonb",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "MetadataWarningsJson",
                table: "Parts",
                type: "jsonb",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "EstimationConfigurationJson",
                table: "PartProcessingJobs",
                type: "jsonb",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "EstimationDetailsJson",
                table: "PartProcessingJobs",
                type: "jsonb",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "MetadataWarningsJson",
                table: "PartProcessingJobs",
                type: "jsonb",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "EstimationBuildPlateType",
                table: "Organizations",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "EstimationFilamentProfileId",
                table: "Organizations",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "EstimationMachineProfileId",
                table: "Organizations",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "EstimationProcessProfileId",
                table: "Organizations",
                type: "uuid",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "EstimationDetailsJson",
                table: "Parts");

            migrationBuilder.DropColumn(
                name: "MetadataWarningsJson",
                table: "Parts");

            migrationBuilder.DropColumn(
                name: "EstimationConfigurationJson",
                table: "PartProcessingJobs");

            migrationBuilder.DropColumn(
                name: "EstimationDetailsJson",
                table: "PartProcessingJobs");

            migrationBuilder.DropColumn(
                name: "MetadataWarningsJson",
                table: "PartProcessingJobs");

            migrationBuilder.DropColumn(
                name: "EstimationBuildPlateType",
                table: "Organizations");

            migrationBuilder.DropColumn(
                name: "EstimationFilamentProfileId",
                table: "Organizations");

            migrationBuilder.DropColumn(
                name: "EstimationMachineProfileId",
                table: "Organizations");

            migrationBuilder.DropColumn(
                name: "EstimationProcessProfileId",
                table: "Organizations");
        }
    }
}
