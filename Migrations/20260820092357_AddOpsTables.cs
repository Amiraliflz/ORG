using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace Application.Migrations
{
    /// <inheritdoc />
    public partial class AddOpsTables : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "AppLogEntries",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    Timestamp = table.Column<DateTime>(type: "timestamp without time zone", nullable: false),
                    Level = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    Category = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    Message = table.Column<string>(type: "character varying(4000)", maxLength: 4000, nullable: false),
                    Exception = table.Column<string>(type: "text", nullable: true),
                    RequestPath = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    RequestMethod = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: true),
                    StatusCode = table.Column<int>(type: "integer", nullable: true),
                    DurationMs = table.Column<int>(type: "integer", nullable: true),
                    UserId = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: true),
                    PropertiesJson = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AppLogEntries", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "OperationAudits",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    Action = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    UserId = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: true),
                    At = table.Column<DateTime>(type: "timestamp without time zone", nullable: false),
                    Success = table.Column<bool>(type: "boolean", nullable: false),
                    Details = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_OperationAudits", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "SystemHeartbeats",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    CheckedAt = table.Column<DateTime>(type: "timestamp without time zone", nullable: false),
                    IsHealthy = table.Column<bool>(type: "boolean", nullable: false),
                    Component = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    ResponseMs = table.Column<int>(type: "integer", nullable: true),
                    Details = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SystemHeartbeats", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_AppLogEntries_Level",
                table: "AppLogEntries",
                column: "Level");

            migrationBuilder.CreateIndex(
                name: "IX_AppLogEntries_RequestPath",
                table: "AppLogEntries",
                column: "RequestPath");

            migrationBuilder.CreateIndex(
                name: "IX_AppLogEntries_Timestamp",
                table: "AppLogEntries",
                column: "Timestamp");

            migrationBuilder.CreateIndex(
                name: "IX_SystemHeartbeats_Component_CheckedAt",
                table: "SystemHeartbeats",
                columns: new[] { "Component", "CheckedAt" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "AppLogEntries");

            migrationBuilder.DropTable(
                name: "OperationAudits");

            migrationBuilder.DropTable(
                name: "SystemHeartbeats");
        }
    }
}
