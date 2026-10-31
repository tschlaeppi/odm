using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Odm.Modules.Audit.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class Initial : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(
                name: "audit");

            migrationBuilder.CreateTable(
                name: "audit_entries",
                schema: "audit",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Timestamp = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    Actor = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: false),
                    Role = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: true),
                    Action = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: false),
                    Target = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: true),
                    Outcome = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    StatusCode = table.Column<int>(type: "integer", nullable: true),
                    IpAddress = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    Details = table.Column<string>(type: "character varying(1024)", maxLength: 1024, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_audit_entries", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_audit_entries_Actor",
                schema: "audit",
                table: "audit_entries",
                column: "Actor");

            migrationBuilder.CreateIndex(
                name: "IX_audit_entries_Timestamp",
                schema: "audit",
                table: "audit_entries",
                column: "Timestamp");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "audit_entries",
                schema: "audit");
        }
    }
}
