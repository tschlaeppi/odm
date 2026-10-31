using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Odm.Migrations.SqlServer.Devices
{
    /// <inheritdoc />
    public partial class DeviceStatus : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "BootTime",
                schema: "devices",
                table: "device_hardware",
                type: "datetimeoffset",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "DisksJson",
                schema: "devices",
                table: "device_hardware",
                type: "nvarchar(max)",
                maxLength: 8000,
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "LastLoginAt",
                schema: "devices",
                table: "device_hardware",
                type: "datetimeoffset",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "LastUser",
                schema: "devices",
                table: "device_hardware",
                type: "nvarchar(256)",
                maxLength: 256,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "NetworkJson",
                schema: "devices",
                table: "device_hardware",
                type: "nvarchar(max)",
                maxLength: 8000,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "BootTime",
                schema: "devices",
                table: "device_hardware");

            migrationBuilder.DropColumn(
                name: "DisksJson",
                schema: "devices",
                table: "device_hardware");

            migrationBuilder.DropColumn(
                name: "LastLoginAt",
                schema: "devices",
                table: "device_hardware");

            migrationBuilder.DropColumn(
                name: "LastUser",
                schema: "devices",
                table: "device_hardware");

            migrationBuilder.DropColumn(
                name: "NetworkJson",
                schema: "devices",
                table: "device_hardware");
        }
    }
}
