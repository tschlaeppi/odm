using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Odm.Modules.Software.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class SoftwareProfileTranchen : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "RolloutJson",
                schema: "software",
                table: "assignments",
                type: "character varying(2000)",
                maxLength: 2000,
                nullable: true);

            migrationBuilder.CreateTable(
                name: "profiles",
                schema: "software",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Name = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    Description = table.Column<string>(type: "character varying(1024)", maxLength: 1024, nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_profiles", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "profile_assignments",
                schema: "software",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ProfileId = table.Column<Guid>(type: "uuid", nullable: false),
                    TargetKind = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    Target = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    RolloutJson = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_profile_assignments", x => x.Id);
                    table.ForeignKey(
                        name: "FK_profile_assignments_profiles_ProfileId",
                        column: x => x.ProfileId,
                        principalSchema: "software",
                        principalTable: "profiles",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "profile_items",
                schema: "software",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ProfileId = table.Column<Guid>(type: "uuid", nullable: false),
                    PackageId = table.Column<Guid>(type: "uuid", nullable: false),
                    Position = table.Column<int>(type: "integer", nullable: false),
                    VersionPolicy = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    PinnedVersionId = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_profile_items", x => x.Id);
                    table.ForeignKey(
                        name: "FK_profile_items_packages_PackageId",
                        column: x => x.PackageId,
                        principalSchema: "software",
                        principalTable: "packages",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_profile_items_profiles_ProfileId",
                        column: x => x.ProfileId,
                        principalSchema: "software",
                        principalTable: "profiles",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_profile_assignments_ProfileId_TargetKind_Target",
                schema: "software",
                table: "profile_assignments",
                columns: new[] { "ProfileId", "TargetKind", "Target" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_profile_items_PackageId",
                schema: "software",
                table: "profile_items",
                column: "PackageId");

            migrationBuilder.CreateIndex(
                name: "IX_profile_items_ProfileId_PackageId",
                schema: "software",
                table: "profile_items",
                columns: new[] { "ProfileId", "PackageId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_profile_items_ProfileId_Position",
                schema: "software",
                table: "profile_items",
                columns: new[] { "ProfileId", "Position" });

            migrationBuilder.CreateIndex(
                name: "IX_profiles_Name",
                schema: "software",
                table: "profiles",
                column: "Name",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "profile_assignments",
                schema: "software");

            migrationBuilder.DropTable(
                name: "profile_items",
                schema: "software");

            migrationBuilder.DropTable(
                name: "profiles",
                schema: "software");

            migrationBuilder.DropColumn(
                name: "RolloutJson",
                schema: "software",
                table: "assignments");
        }
    }
}
