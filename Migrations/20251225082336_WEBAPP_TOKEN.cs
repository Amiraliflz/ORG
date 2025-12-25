using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Application.Migrations
{
    /// <inheritdoc />
    public partial class WEBAPP_TOKEN : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "WebappToken",
                table: "Tickets",
                type: "text",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "WebappToken",
                table: "Tickets");
        }
    }
}
