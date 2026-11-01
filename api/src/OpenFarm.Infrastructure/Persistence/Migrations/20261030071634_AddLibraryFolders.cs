using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace OpenFarm.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddLibraryFolders : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "FolderId",
                table: "Parts",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "FolderId",
                table: "Assemblies",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "LibraryFolders",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Library = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    ParentId = table.Column<Guid>(type: "uuid", nullable: true),
                    Name = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                    CreatedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_LibraryFolders", x => x.Id);
                    table.CheckConstraint("CK_LibraryFolders_Library", "\"Library\" IN ('parts', 'assemblies')");
                    table.ForeignKey(
                        name: "FK_LibraryFolders_LibraryFolders_ParentId",
                        column: x => x.ParentId,
                        principalTable: "LibraryFolders",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Parts_FolderId",
                table: "Parts",
                column: "FolderId");

            migrationBuilder.CreateIndex(
                name: "IX_Assemblies_FolderId",
                table: "Assemblies",
                column: "FolderId");

            migrationBuilder.CreateIndex(
                name: "IX_LibraryFolders_Library_ParentId",
                table: "LibraryFolders",
                columns: new[] { "Library", "ParentId" });

            migrationBuilder.CreateIndex(
                name: "IX_LibraryFolders_ParentId",
                table: "LibraryFolders",
                column: "ParentId");

            migrationBuilder.AddForeignKey(
                name: "FK_Assemblies_LibraryFolders_FolderId",
                table: "Assemblies",
                column: "FolderId",
                principalTable: "LibraryFolders",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_Parts_LibraryFolders_FolderId",
                table: "Parts",
                column: "FolderId",
                principalTable: "LibraryFolders",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Assemblies_LibraryFolders_FolderId",
                table: "Assemblies");

            migrationBuilder.DropForeignKey(
                name: "FK_Parts_LibraryFolders_FolderId",
                table: "Parts");

            migrationBuilder.DropTable(
                name: "LibraryFolders");

            migrationBuilder.DropIndex(
                name: "IX_Parts_FolderId",
                table: "Parts");

            migrationBuilder.DropIndex(
                name: "IX_Assemblies_FolderId",
                table: "Assemblies");

            migrationBuilder.DropColumn(
                name: "FolderId",
                table: "Parts");

            migrationBuilder.DropColumn(
                name: "FolderId",
                table: "Assemblies");
        }
    }
}
