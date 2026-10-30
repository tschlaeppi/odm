using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Odm.Modules.Agents.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AgentJobOutput : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterColumn<string>(
                name: "Output",
                schema: "agents",
                table: "agent_jobs",
                type: "character varying(64000)",
                maxLength: 64000,
                nullable: true,
                oldClrType: typeof(string),
                oldType: "character varying(8000)",
                oldMaxLength: 8000,
                oldNullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterColumn<string>(
                name: "Output",
                schema: "agents",
                table: "agent_jobs",
                type: "character varying(8000)",
                maxLength: 8000,
                nullable: true,
                oldClrType: typeof(string),
                oldType: "character varying(64000)",
                oldMaxLength: 64000,
                oldNullable: true);
        }
    }
}
