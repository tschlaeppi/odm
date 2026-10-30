using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Odm.Modules.Agents.Infrastructure.Migrations
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
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "CertThumbprint",
                schema: "agents",
                table: "agent_credentials",
                type: "character varying(64)",
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
