using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Odm.Migrations.SqlServer.Agents
{
    /// <inheritdoc />
    public partial class AgentCertificates : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "CertNotAfter",
                schema: "agents",
                table: "agent_credentials",
                type: "datetimeoffset",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "CertThumbprint",
                schema: "agents",
                table: "agent_credentials",
                type: "nvarchar(64)",
                maxLength: 64,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "CertNotAfter",
                schema: "agents",
                table: "agent_credentials");

            migrationBuilder.DropColumn(
                name: "CertThumbprint",
                schema: "agents",
                table: "agent_credentials");
        }
    }
}
