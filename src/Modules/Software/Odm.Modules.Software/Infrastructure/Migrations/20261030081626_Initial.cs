using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Odm.Modules.Software.Infrastructure.Migrations
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
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Name = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    Description = table.Column<string>(type: "character varying(1024)", maxLength: 1024, nullable: true),
                    Kind = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    SpecJson = table.Column<string>(type: "character varying(4000)", maxLength: 4000, nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_packages", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "secrets",
                schema: "software",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Name = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    Ciphertext = table.Column<byte[]>(type: "bytea", maxLength: 8192, nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
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
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    PackageId = table.Column<Guid>(type: "uuid", nullable: false),
                    TargetKind = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    Target = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    VersionPolicy = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    PinnedVersionId = table.Column<Guid>(type: "uuid", nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
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
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    DeviceId = table.Column<Guid>(type: "uuid", nullable: false),
                    PackageId = table.Column<Guid>(type: "uuid", nullable: false),
                    TargetVersionId = table.Column<Guid>(type: "uuid", nullable: false),
                    InstalledVersionId = table.Column<Guid>(type: "uuid", nullable: true),
                    JobVersionId = table.Column<Guid>(type: "uuid", nullable: true),
                    JobId = table.Column<Guid>(type: "uuid", nullable: true),
                    Status = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    Attempts = table.Column<int>(type: "integer", nullable: false),
                    Message = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
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
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    PackageId = table.Column<Guid>(type: "uuid", nullable: false),
                    Version = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    ObjectKey = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    SizeBytes = table.Column<long>(type: "bigint", nullable: false),
                    Sha256 = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    SpecSnapshot = table.Column<string>(type: "character varying(4000)", maxLength: 4000, nullable: true),
                    Notes = table.Column<string>(type: "character varying(1024)", maxLength: 1024, nullable: true),
                    UploadedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
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
                name: "secrets",
                schema: "software");

            migrationBuilder.DropTable(
                name: "packages",
                schema: "software");
        }
    }
}
