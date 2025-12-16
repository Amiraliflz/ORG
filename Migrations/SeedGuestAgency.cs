using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Application.Migrations
{
    public partial class SeedGuestAgency : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Insert guest agency - this will be used for all guest ticket bookings
            // Using PostgreSQL syntax
            migrationBuilder.Sql(@"
                INSERT INTO ""Agencies"" (""Name"", ""PhoneNumber"", ""Address"", ""AdminMobile"", ""DateJoined"", ""ORSAPI_token"", ""Commission"", ""IdentityUserId"")
                VALUES (
                    'مستر شوفر - مهمان',
                    '02100000000',
                    'تهران',
                    '09900000000',
                    NOW(),
                    'eyJhbGciOiJIUzI1NiIsInR5cCI6IkpXVCJ9.eyJodHRwOi8vc2NoZW1hcy54bWxzb2FwLm9yZy93cy8yMDA1LzA1L2lkZW50aXR5L2NsYWltcy9uYW1laWRlbnRpZmllciI6IjU1NCIsImp0aSI6Ijk1ZTI1NjYzLTkzM2EtNGY1ZS04ZTdiLTMwNGQ0Yjg3M2Q3NiIsImV4cCI6MTkyMjc3ODkwOCwiaXNzIjoibXJzaG9vZmVyLmlyIiwiYXVkIjoibXJzaG9vZmVyLmlyIn0.2r5WoGmqb5Ra_6epV5jR3Y0RlHs5bcwE0li0wo1ricE',
                    0,
                    NULL
                )
                ON CONFLICT DO NOTHING
            ");
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(@"
                DELETE FROM ""Agencies"" WHERE ""Name"" = 'مستر شوفر - مهمان' AND ""IdentityUserId"" IS NULL
            ");
        }
    }
}
