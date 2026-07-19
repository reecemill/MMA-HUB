using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace Scraper.Migrations
{
    /// <inheritdoc />
    public partial class InitialCreate : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "Fighters",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Slug = table.Column<string>(type: "text", nullable: true),
                    UfcStatsId = table.Column<string>(type: "text", nullable: true),
                    Name = table.Column<string>(type: "text", nullable: true),
                    FirstName = table.Column<string>(type: "text", nullable: true),
                    LastName = table.Column<string>(type: "text", nullable: true),
                    Nickname = table.Column<string>(type: "text", nullable: true),
                    Division = table.Column<string>(type: "text", nullable: true),
                    Status = table.Column<string>(type: "text", nullable: true),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false),
                    ChampionStatus = table.Column<string>(type: "text", nullable: true),
                    P4pRank = table.Column<int>(type: "integer", nullable: true),
                    RecordWins = table.Column<int>(type: "integer", nullable: true),
                    RecordLosses = table.Column<int>(type: "integer", nullable: true),
                    RecordDraws = table.Column<int>(type: "integer", nullable: true),
                    RecordNoContest = table.Column<int>(type: "integer", nullable: true),
                    RecordText = table.Column<string>(type: "text", nullable: true),
                    Country = table.Column<string>(type: "text", nullable: true),
                    PlaceOfBirth = table.Column<string>(type: "text", nullable: true),
                    TrainsAt = table.Column<string>(type: "text", nullable: true),
                    FightingStyle = table.Column<string>(type: "text", nullable: true),
                    Age = table.Column<int>(type: "integer", nullable: true),
                    HeightInches = table.Column<string>(type: "text", nullable: true),
                    WeightLbs = table.Column<string>(type: "text", nullable: true),
                    ReachInches = table.Column<string>(type: "text", nullable: true),
                    LegReachInches = table.Column<string>(type: "text", nullable: true),
                    Stance = table.Column<string>(type: "text", nullable: true),
                    OctagonDebut = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    ProfileUrl = table.Column<string>(type: "text", nullable: true),
                    ImageUrl = table.Column<string>(type: "text", nullable: true),
                    Bio = table.Column<string>(type: "text", nullable: true),
                    RawJson = table.Column<string>(type: "jsonb", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Fighters", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "FighterHeroStats",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    FighterId = table.Column<Guid>(type: "uuid", nullable: false),
                    StatKey = table.Column<string>(type: "text", nullable: false),
                    StatValue = table.Column<string>(type: "text", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_FighterHeroStats", x => x.Id);
                    table.ForeignKey(
                        name: "FK_FighterHeroStats_Fighters_FighterId",
                        column: x => x.FighterId,
                        principalTable: "Fighters",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_FighterHeroStats_FighterId",
                table: "FighterHeroStats",
                column: "FighterId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "FighterHeroStats");

            migrationBuilder.DropTable(
                name: "Fighters");
        }
    }
}
