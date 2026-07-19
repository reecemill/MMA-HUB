using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Scraper.Migrations
{
    /// <inheritdoc />
    public partial class AddEvents : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "Events",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Slug = table.Column<string>(type: "text", nullable: true),
                    Title = table.Column<string>(type: "text", nullable: true),
                    ShortTitle = table.Column<string>(type: "text", nullable: true),
                    Status = table.Column<string>(type: "text", nullable: true),
                    StartsAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    Venue = table.Column<string>(type: "text", nullable: true),
                    City = table.Column<string>(type: "text", nullable: true),
                    State = table.Column<string>(type: "text", nullable: true),
                    Country = table.Column<string>(type: "text", nullable: true),
                    LocationText = table.Column<string>(type: "text", nullable: true),
                    ImageUrl = table.Column<string>(type: "text", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    LastSyncedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    DataId = table.Column<string>(type: "text", nullable: true),
                    HasStats = table.Column<bool>(type: "boolean", nullable: false),
                    DataFreshness = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    FreshnessStatus = table.Column<string>(type: "text", nullable: true),
                    DataAgeHours = table.Column<int>(type: "integer", nullable: true),
                    DataSource = table.Column<string>(type: "text", nullable: true),
                    Warning = table.Column<string>(type: "text", nullable: true),
                    EventDate = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    EventDateLabel = table.Column<string>(type: "text", nullable: true),
                    EventWeekday = table.Column<string>(type: "text", nullable: true),
                    EventTimeZone = table.Column<string>(type: "text", nullable: true),
                    VenueTimeZone = table.Column<string>(type: "text", nullable: true),
                    VenueDate = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    VenueDateLabel = table.Column<string>(type: "text", nullable: true),
                    VenueWeekday = table.Column<string>(type: "text", nullable: true),
                    RawJson = table.Column<string>(type: "jsonb", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Events", x => x.Id);
                });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "Events");
        }
    }
}
