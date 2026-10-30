using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Odm.Modules.Software.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class SoftwareUninstall : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterColumn<string>(
                name: "Message",
                schema: "software",
                table: "installations",
                type: "character varying(64000)",
                maxLength: 64000,
                nullable: true,
                oldClrType: typeof(string),
                oldType: "character varying(2000)",
                oldMaxLength: 2000,
                oldNullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "Removal",
                schema: "software",
                table: "installations",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<string>(
                name: "Intent",
                schema: "software",
                table: "assignments",
                type: "character varying(16)",
                maxLength: 16,
                nullable: false,
                defaultValue: "Install");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "Removal",
                schema: "software",
                table: "installations");

            migrationBuilder.DropColumn(
                name: "Intent",
                schema: "software",
                table: "assignments");

            migrationBuilder.AlterColumn<string>(
                name: "Message",
                schema: "software",
                table: "installations",
                type: "character varying(2000)",
                maxLength: 2000,
                nullable: true,
                oldClrType: typeof(string),
                oldType: "character varying(64000)",
                oldMaxLength: 64000,
                oldNullable: true);
        }
    }
}
