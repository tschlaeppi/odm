using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace Odm.Modules.Devices.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class Initial : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(
                name: "devices");

            migrationBuilder.CreateTable(
                name: "device_groups",
                schema: "devices",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Name = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    Description = table.Column<string>(type: "character varying(1024)", maxLength: 1024, nullable: true),
                    MatchMode = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_device_groups", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "devices",
                schema: "devices",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Name = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    OsFamily = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    OsName = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    OsVersion = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    PrimaryUser = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    PrimaryUserOrgUnit = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    CostCenter = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    RegisteredAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    LastSeenAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    AgentVersion = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_devices", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "device_group_rules",
                schema: "devices",
                columns: table => new
                {
                    DeviceGroupId = table.Column<Guid>(type: "uuid", nullable: false),
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    Field = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    Operator = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    Value = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_device_group_rules", x => new { x.DeviceGroupId, x.Id });
                    table.ForeignKey(
                        name: "FK_device_group_rules_device_groups_DeviceGroupId",
                        column: x => x.DeviceGroupId,
                        principalSchema: "devices",
                        principalTable: "device_groups",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_device_groups_Name",
                schema: "devices",
                table: "device_groups",
                column: "Name",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_devices_Name",
                schema: "devices",
                table: "devices",
                column: "Name");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "device_group_rules",
                schema: "devices");

            migrationBuilder.DropTable(
                name: "devices",
                schema: "devices");

            migrationBuilder.DropTable(
                name: "device_groups",
                schema: "devices");
        }
    }
}
