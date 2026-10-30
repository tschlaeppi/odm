using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Odm.Migrations.SqlServer.Software
{
    /// <inheritdoc />
    public partial class Initial : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(
                name: "software");

            migrationBuilder.CreateTable(
                name: "packages",
                schema: "software",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Name = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                    Description = table.Column<string>(type: "nvarchar(1024)", maxLength: 1024, nullable: true),
                    Kind = table.Column<string>(type: "nvarchar(16)", maxLength: 16, nullable: false),
                    SpecJson = table.Column<string>(type: "nvarchar(4000)", maxLength: 4000, nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_packages", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "profiles",
                schema: "software",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Name = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                    Description = table.Column<string>(type: "nvarchar(1024)", maxLength: 1024, nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_profiles", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "secrets",
                schema: "software",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Name = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    Ciphertext = table.Column<byte[]>(type: "varbinary(max)", maxLength: 8192, nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_secrets", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "assignments",
                schema: "software",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    PackageId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    TargetKind = table.Column<string>(type: "nvarchar(16)", maxLength: 16, nullable: false),
                    Target = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: false),
                    VersionPolicy = table.Column<string>(type: "nvarchar(16)", maxLength: 16, nullable: false),
                    PinnedVersionId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    RolloutJson = table.Column<string>(type: "nvarchar(2000)", maxLength: 2000, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_assignments", x => x.Id);
                    table.ForeignKey(
                        name: "FK_assignments_packages_PackageId",
                        column: x => x.PackageId,
                        principalSchema: "software",
                        principalTable: "packages",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "installations",
                schema: "software",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    DeviceId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    PackageId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    TargetVersionId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    InstalledVersionId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    JobVersionId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    JobId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    Status = table.Column<string>(type: "nvarchar(16)", maxLength: 16, nullable: false),
                    Attempts = table.Column<int>(type: "int", nullable: false),
                    Message = table.Column<string>(type: "nvarchar(2000)", maxLength: 2000, nullable: true),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_installations", x => x.Id);
                    table.ForeignKey(
                        name: "FK_installations_packages_PackageId",
                        column: x => x.PackageId,
                        principalSchema: "software",
                        principalTable: "packages",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "package_versions",
                schema: "software",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    PackageId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Version = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    ObjectKey = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: true),
                    SizeBytes = table.Column<long>(type: "bigint", nullable: false),
                    Sha256 = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: true),
                    SpecSnapshot = table.Column<string>(type: "nvarchar(4000)", maxLength: 4000, nullable: true),
                    Notes = table.Column<string>(type: "nvarchar(1024)", maxLength: 1024, nullable: true),
                    UploadedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_package_versions", x => x.Id);
                    table.ForeignKey(
                        name: "FK_package_versions_packages_PackageId",
                        column: x => x.PackageId,
                        principalSchema: "software",
                        principalTable: "packages",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "profile_assignments",
                schema: "software",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ProfileId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    TargetKind = table.Column<string>(type: "nvarchar(16)", maxLength: 16, nullable: false),
                    Target = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: false),
                    RolloutJson = table.Column<string>(type: "nvarchar(2000)", maxLength: 2000, nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false)
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
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ProfileId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    PackageId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Position = table.Column<int>(type: "int", nullable: false),
                    VersionPolicy = table.Column<string>(type: "nvarchar(16)", maxLength: 16, nullable: false),
                    PinnedVersionId = table.Column<Guid>(type: "uniqueidentifier", nullable: true)
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
                name: "IX_assignments_PackageId_TargetKind_Target",
                schema: "software",
                table: "assignments",
                columns: new[] { "PackageId", "TargetKind", "Target" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_installations_DeviceId_PackageId",
                schema: "software",
                table: "installations",
                columns: new[] { "DeviceId", "PackageId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_installations_JobId",
                schema: "software",
                table: "installations",
                column: "JobId");

            migrationBuilder.CreateIndex(
                name: "IX_installations_PackageId",
                schema: "software",
                table: "installations",
                column: "PackageId");

            migrationBuilder.CreateIndex(
                name: "IX_package_versions_PackageId_Version",
                schema: "software",
                table: "package_versions",
                columns: new[] { "PackageId", "Version" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_packages_Name",
                schema: "software",
                table: "packages",
                column: "Name",
                unique: true);

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

            migrationBuilder.CreateIndex(
                name: "IX_secrets_Name",
                schema: "software",
                table: "secrets",
                column: "Name",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "assignments",
                schema: "software");

            migrationBuilder.DropTable(
                name: "installations",
                schema: "software");

            migrationBuilder.DropTable(
                name: "package_versions",
                schema: "software");

            migrationBuilder.DropTable(
                name: "profile_assignments",
                schema: "software");

            migrationBuilder.DropTable(
                name: "profile_items",
                schema: "software");

            migrationBuilder.DropTable(
                name: "secrets",
                schema: "software");

            migrationBuilder.DropTable(
                name: "packages",
                schema: "software");

            migrationBuilder.DropTable(
                name: "profiles",
                schema: "software");
        }
    }
}
