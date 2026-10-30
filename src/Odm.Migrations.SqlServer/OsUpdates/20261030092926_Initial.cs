using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Odm.Migrations.SqlServer.OsUpdates
{
    /// <inheritdoc />
    public partial class Initial : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(
                name: "osupdates");

            migrationBuilder.CreateTable(
                name: "assignments",
                schema: "osupdates",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Scope = table.Column<string>(type: "nvarchar(16)", maxLength: 16, nullable: false),
                    ParentId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    TargetKind = table.Column<string>(type: "nvarchar(16)", maxLength: 16, nullable: false),
                    Target = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_assignments", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "maintenance_windows",
                schema: "osupdates",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Name = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                    Days = table.Column<int>(type: "int", nullable: false),
                    StartMinutes = table.Column<int>(type: "int", nullable: false),
                    DurationMinutes = table.Column<int>(type: "int", nullable: false),
                    TimeZoneId = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_maintenance_windows", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "policies",
                schema: "osupdates",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Name = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                    Description = table.Column<string>(type: "nvarchar(1024)", maxLength: 1024, nullable: true),
                    Mode = table.Column<string>(type: "nvarchar(16)", maxLength: 16, nullable: false),
                    Reboot = table.Column<string>(type: "nvarchar(16)", maxLength: 16, nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_policies", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "runs",
                schema: "osupdates",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    DeviceId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    WindowId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    OccurrenceStart = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    JobId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Status = table.Column<string>(type: "nvarchar(16)", maxLength: 16, nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    CompletedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    Installed = table.Column<int>(type: "int", nullable: true),
                    Failed = table.Column<int>(type: "int", nullable: true),
                    RebootRequired = table.Column<bool>(type: "bit", nullable: false),
                    Rebooting = table.Column<bool>(type: "bit", nullable: false),
                    Message = table.Column<string>(type: "nvarchar(2000)", maxLength: 2000, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_runs", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_assignments_Scope_ParentId_TargetKind_Target",
                schema: "osupdates",
                table: "assignments",
                columns: new[] { "Scope", "ParentId", "TargetKind", "Target" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_maintenance_windows_Name",
                schema: "osupdates",
                table: "maintenance_windows",
                column: "Name",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_policies_Name",
                schema: "osupdates",
                table: "policies",
                column: "Name",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_runs_CreatedAt",
                schema: "osupdates",
                table: "runs",
                column: "CreatedAt");

            migrationBuilder.CreateIndex(
                name: "IX_runs_DeviceId_WindowId_OccurrenceStart",
                schema: "osupdates",
                table: "runs",
                columns: new[] { "DeviceId", "WindowId", "OccurrenceStart" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_runs_JobId",
                schema: "osupdates",
                table: "runs",
                column: "JobId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "assignments",
                schema: "osupdates");

            migrationBuilder.DropTable(
                name: "maintenance_windows",
                schema: "osupdates");

            migrationBuilder.DropTable(
                name: "policies",
                schema: "osupdates");

            migrationBuilder.DropTable(
                name: "runs",
                schema: "osupdates");
        }
    }
}
