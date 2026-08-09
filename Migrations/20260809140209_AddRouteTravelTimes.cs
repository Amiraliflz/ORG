using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace Application.Migrations
{
    /// <inheritdoc />
    public partial class AddRouteTravelTimes : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "CityCoordinates",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    CityId = table.Column<int>(type: "integer", nullable: false),
                    NameFa = table.Column<string>(type: "text", nullable: false),
                    Lat = table.Column<double>(type: "double precision", nullable: false),
                    Lng = table.Column<double>(type: "double precision", nullable: false),
                    Source = table.Column<string>(type: "text", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp without time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CityCoordinates", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "RouteTravelTimes",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    OriginCityId = table.Column<int>(type: "integer", nullable: false),
                    DestinationCityId = table.Column<int>(type: "integer", nullable: false),
                    OriginNameFa = table.Column<string>(type: "text", nullable: false),
                    DestinationNameFa = table.Column<string>(type: "text", nullable: false),
                    TravelTimeMins = table.Column<int>(type: "integer", nullable: false),
                    DistanceMeters = table.Column<int>(type: "integer", nullable: true),
                    Source = table.Column<string>(type: "text", nullable: false),
                    ShamsiYear = table.Column<int>(type: "integer", nullable: false),
                    ShamsiMonth = table.Column<int>(type: "integer", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp without time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_RouteTravelTimes", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "TravelTimeSyncStates",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    LastSyncedShamsiYear = table.Column<int>(type: "integer", nullable: true),
                    LastSyncedShamsiMonth = table.Column<int>(type: "integer", nullable: true),
                    LastRunAt = table.Column<DateTime>(type: "timestamp without time zone", nullable: true),
                    LastStatus = table.Column<string>(type: "text", nullable: false),
                    LastError = table.Column<string>(type: "text", nullable: true),
                    LastUpdatedRoutes = table.Column<int>(type: "integer", nullable: false),
                    LastFailedRoutes = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TravelTimeSyncStates", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_CityCoordinates_CityId",
                table: "CityCoordinates",
                column: "CityId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_CityCoordinates_NameFa",
                table: "CityCoordinates",
                column: "NameFa");

            migrationBuilder.CreateIndex(
                name: "IX_RouteTravelTimes_OriginCityId_DestinationCityId",
                table: "RouteTravelTimes",
                columns: new[] { "OriginCityId", "DestinationCityId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_RouteTravelTimes_OriginNameFa_DestinationNameFa",
                table: "RouteTravelTimes",
                columns: new[] { "OriginNameFa", "DestinationNameFa" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "CityCoordinates");

            migrationBuilder.DropTable(
                name: "RouteTravelTimes");

            migrationBuilder.DropTable(
                name: "TravelTimeSyncStates");
        }
    }
}
