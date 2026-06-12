using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Application.Migrations
{
    /// <inheritdoc />
    public partial class AddCustomerProfileBalance : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<decimal>(
                name: "Balance",
                table: "CustomerProfiles",
                type: "numeric",
                nullable: false,
                defaultValue: 0m);

            // Migrate existing balances from Identity claims into CustomerProfiles
            migrationBuilder.Sql(@"
                UPDATE ""CustomerProfiles"" cp
                SET ""Balance"" = CAST(c.""ClaimValue"" AS NUMERIC)
                FROM ""AspNetUserClaims"" c
                WHERE c.""UserId"" = cp.""UserId""
                  AND c.""ClaimType"" = 'CustomerBalance'
                  AND c.""ClaimValue"" ~ '^[0-9]+(\.[0-9]+)?$'
            ");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "Balance",
                table: "CustomerProfiles");
        }
    }
}
