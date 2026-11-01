using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Odm.Modules.OsUpdates.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddRunItems : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "run_items",
                schema: "osupdates",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    RunId = table.Column<Guid>(type: "uuid", nullable: false),
                    Title = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_run_items", x => x.Id);
                    table.ForeignKey(
                        name: "FK_run_items_runs_RunId",
                        column: x => x.RunId,
                        principalSchema: "osupdates",
                        principalTable: "runs",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_run_items_RunId",
                schema: "osupdates",
                table: "run_items",
                column: "RunId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "run_items",
                schema: "osupdates");
        }
    }
}
