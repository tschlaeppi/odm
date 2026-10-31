using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Odm.Migrations.SqlServer.Devices
{
    /// <inheritdoc />
    public partial class Inventory : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "device_hardware",
                schema: "devices",
                columns: table => new
                {
                    DeviceId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CollectedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    Manufacturer = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: true),
                    Model = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: true),
                    Cpu = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: true),
                    CpuCores = table.Column<int>(type: "int", nullable: true),
                    MemoryBytes = table.Column<long>(type: "bigint", nullable: true),
                    Architecture = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: true),
                    DiskTotalBytes = table.Column<long>(type: "bigint", nullable: true),
                    DiskFreeBytes = table.Column<long>(type: "bigint", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_device_hardware", x => x.DeviceId);
                });

            migrationBuilder.CreateTable(
                name: "installed_software",
                schema: "devices",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    DeviceId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Name = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: false),
                    Version = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: true),
                    Publisher = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_installed_software", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_installed_software_DeviceId",
                schema: "devices",
                table: "installed_software",
                column: "DeviceId");

            migrationBuilder.CreateIndex(
                name: "IX_installed_software_Name",
                schema: "devices",
                table: "installed_software",
                column: "Name");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "device_hardware",
                schema: "devices");

            migrationBuilder.DropTable(
                name: "installed_software",
                schema: "devices");
        }
    }
}
