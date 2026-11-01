using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Odm.Modules.Software.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddPackageDependencies : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "package_dependencies",
                schema: "software",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    PackageId = table.Column<Guid>(type: "uuid", nullable: false),
                    DependsOnId = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_package_dependencies", x => x.Id);
                    table.ForeignKey(
                        name: "FK_package_dependencies_packages_DependsOnId",
                        column: x => x.DependsOnId,
                        principalSchema: "software",
                        principalTable: "packages",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_package_dependencies_packages_PackageId",
                        column: x => x.PackageId,
                        principalSchema: "software",
                        principalTable: "packages",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_package_dependencies_DependsOnId",
                schema: "software",
                table: "package_dependencies",
                column: "DependsOnId");

            migrationBuilder.CreateIndex(
                name: "IX_package_dependencies_PackageId_DependsOnId",
                schema: "software",
                table: "package_dependencies",
                columns: new[] { "PackageId", "DependsOnId" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "package_dependencies",
                schema: "software");
        }
    }
}
