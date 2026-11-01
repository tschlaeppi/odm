using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Odm.Migrations.SqlServer.OsUpdates
{
    /// <inheritdoc />
    public partial class AddReportedUpdates : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "reported_updates",
                schema: "osupdates",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    DeviceId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Title = table.Column<string>(type: "nvarchar(300)", maxLength: 300, nullable: false),
                    InstalledAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_reported_updates", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_reported_updates_DeviceId_InstalledAt",
                schema: "osupdates",
                table: "reported_updates",
                columns: new[] { "DeviceId", "InstalledAt" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "reported_updates",
                schema: "osupdates");
        }
    }
}
