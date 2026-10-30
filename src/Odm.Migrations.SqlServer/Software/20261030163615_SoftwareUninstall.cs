using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Odm.Migrations.SqlServer.Software
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
                type: "nvarchar(max)",
                maxLength: 64000,
                nullable: true,
                oldClrType: typeof(string),
                oldType: "nvarchar(2000)",
                oldMaxLength: 2000,
                oldNullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "Removal",
                schema: "software",
                table: "installations",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<string>(
                name: "Intent",
                schema: "software",
                table: "assignments",
                type: "nvarchar(16)",
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
                type: "nvarchar(2000)",
                maxLength: 2000,
                nullable: true,
                oldClrType: typeof(string),
                oldType: "nvarchar(max)",
                oldMaxLength: 64000,
                oldNullable: true);
        }
    }
}
