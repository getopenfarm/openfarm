using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace OpenFarm.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class OptionalPartMetadata : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterColumn<decimal>(
                name: "NozzleDiameterMm",
                table: "Parts",
                type: "numeric",
                nullable: true,
                oldClrType: typeof(decimal),
                oldType: "numeric");

            migrationBuilder.AddColumn<Guid>(
                name: "ActiveProcessingJobId",
                table: "Parts",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "EstimateUnavailableReason",
                table: "Parts",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "MaterialMetadataComplete",
                table: "Parts",
                type: "boolean",
                nullable: false,
                defaultValue: true);

            migrationBuilder.AddColumn<string>(
                name: "EstimateUnavailableReason",
                table: "PartProcessingJobs",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "PreviewMaterialMetadataComplete",
                table: "PartProcessingJobs",
                type: "boolean",
                nullable: false,
                defaultValue: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "SourcePartUpdatedAtUtc",
                table: "PartProcessingJobs",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "SourceRevisionHash",
                table: "PartProcessingJobs",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<uint>(
                name: "xmin",
                table: "PartProcessingJobs",
                type: "xid",
                rowVersion: true,
                nullable: false,
                defaultValue: 0u);

            migrationBuilder.CreateIndex(
                name: "IX_PartProcessingJobs_PartId",
                table: "PartProcessingJobs",
                column: "PartId",
                unique: true,
                filter: "\"Kind\" = 'Reprocess' AND \"Status\" IN ('Processing', 'ReadyForReview')");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_PartProcessingJobs_PartId",
                table: "PartProcessingJobs");

            migrationBuilder.DropColumn(
                name: "ActiveProcessingJobId",
                table: "Parts");

            migrationBuilder.DropColumn(
                name: "EstimateUnavailableReason",
                table: "Parts");

            migrationBuilder.DropColumn(
                name: "MaterialMetadataComplete",
                table: "Parts");

            migrationBuilder.DropColumn(
                name: "EstimateUnavailableReason",
                table: "PartProcessingJobs");

            migrationBuilder.DropColumn(
                name: "PreviewMaterialMetadataComplete",
                table: "PartProcessingJobs");

            migrationBuilder.DropColumn(
                name: "SourcePartUpdatedAtUtc",
                table: "PartProcessingJobs");

            migrationBuilder.DropColumn(
                name: "SourceRevisionHash",
                table: "PartProcessingJobs");

            migrationBuilder.DropColumn(
                name: "xmin",
                table: "PartProcessingJobs");

            migrationBuilder.AlterColumn<decimal>(
                name: "NozzleDiameterMm",
                table: "Parts",
                type: "numeric",
                nullable: false,
                defaultValue: 0m,
                oldClrType: typeof(decimal),
                oldType: "numeric",
                oldNullable: true);
        }
    }
}
