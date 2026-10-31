using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Odm.Modules.Agents.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class RegistrationTokenUses : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "MaxUses",
                schema: "agents",
                table: "registration_tokens",
                type: "integer",
                nullable: false,
                defaultValue: 1);

            migrationBuilder.AddColumn<int>(
                name: "UseCount",
                schema: "agents",
                table: "registration_tokens",
                type: "integer",
                nullable: false,
                defaultValue: 0);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "MaxUses",
                schema: "agents",
                table: "registration_tokens");

            migrationBuilder.DropColumn(
                name: "UseCount",
                schema: "agents",
                table: "registration_tokens");
        }
    }
}
