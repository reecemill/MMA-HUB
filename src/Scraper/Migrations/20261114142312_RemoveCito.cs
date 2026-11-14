using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace Scraper.Migrations
{
    /// <inheritdoc />
    public partial class RemoveCito : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "BoutFighterStats");

            migrationBuilder.DropTable(
                name: "FighterHeroStats");

            migrationBuilder.DropTable(
                name: "FighterStats");

            migrationBuilder.DropTable(
                name: "Bouts");

            migrationBuilder.DropTable(
                name: "Events");

            migrationBuilder.DropTable(
                name: "Fighters");

            migrationBuilder.AddColumn<DateOnly>(
                name: "Date",
                table: "UfcEvents",
                type: "date",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "Date",
                table: "UfcEvents");

            migrationBuilder.CreateTable(
                name: "Events",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    City = table.Column<string>(type: "text", nullable: true),
                    Country = table.Column<string>(type: "text", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    DataAgeHours = table.Column<double>(type: "double precision", nullable: true),
                    DataFreshness = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    DataId = table.Column<string>(type: "text", nullable: true),
                    DataSource = table.Column<string>(type: "text", nullable: true),
                    DuplicateOfEventId = table.Column<Guid>(type: "uuid", nullable: true),
                    EventDate = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    EventDateLabel = table.Column<string>(type: "text", nullable: true),
                    EventTimeZone = table.Column<string>(type: "text", nullable: true),
                    EventWeekday = table.Column<string>(type: "text", nullable: true),
                    FreshnessStatus = table.Column<string>(type: "text", nullable: true),
                    HasStats = table.Column<bool>(type: "boolean", nullable: false),
                    ImageUrl = table.Column<string>(type: "text", nullable: true),
                    LastSyncedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    LocationText = table.Column<string>(type: "text", nullable: true),
                    RawJson = table.Column<string>(type: "jsonb", nullable: true),
                    ShortTitle = table.Column<string>(type: "text", nullable: true),
                    Slug = table.Column<string>(type: "text", nullable: true),
                    StartsAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    State = table.Column<string>(type: "text", nullable: true),
                    Status = table.Column<string>(type: "text", nullable: true),
                    Title = table.Column<string>(type: "text", nullable: true),
                    Venue = table.Column<string>(type: "text", nullable: true),
                    VenueDate = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    VenueDateLabel = table.Column<string>(type: "text", nullable: true),
                    VenueTimeZone = table.Column<string>(type: "text", nullable: true),
                    VenueWeekday = table.Column<string>(type: "text", nullable: true),
                    Warning = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Events", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Fighters",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Age = table.Column<int>(type: "integer", nullable: true),
                    Bio = table.Column<string>(type: "text", nullable: true),
                    ChampionStatus = table.Column<string>(type: "text", nullable: true),
                    Country = table.Column<string>(type: "text", nullable: true),
                    Division = table.Column<string>(type: "text", nullable: true),
                    FightingStyle = table.Column<string>(type: "text", nullable: true),
                    FirstName = table.Column<string>(type: "text", nullable: true),
                    HeightInches = table.Column<string>(type: "text", nullable: true),
                    ImageUrl = table.Column<string>(type: "text", nullable: true),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false),
                    LastName = table.Column<string>(type: "text", nullable: true),
                    LegReachInches = table.Column<string>(type: "text", nullable: true),
                    Name = table.Column<string>(type: "text", nullable: true),
                    Nickname = table.Column<string>(type: "text", nullable: true),
                    OctagonDebut = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    P4pRank = table.Column<int>(type: "integer", nullable: true),
                    PlaceOfBirth = table.Column<string>(type: "text", nullable: true),
                    ProfileUrl = table.Column<string>(type: "text", nullable: true),
                    Rank = table.Column<int>(type: "integer", nullable: true),
                    RawJson = table.Column<string>(type: "jsonb", nullable: true),
                    ReachInches = table.Column<string>(type: "text", nullable: true),
                    RecordDraws = table.Column<int>(type: "integer", nullable: true),
                    RecordLosses = table.Column<int>(type: "integer", nullable: true),
                    RecordNoContest = table.Column<int>(type: "integer", nullable: true),
                    RecordText = table.Column<string>(type: "text", nullable: true),
                    RecordWins = table.Column<int>(type: "integer", nullable: true),
                    Slug = table.Column<string>(type: "text", nullable: true),
                    Stance = table.Column<string>(type: "text", nullable: true),
                    Status = table.Column<string>(type: "text", nullable: true),
                    TrainsAt = table.Column<string>(type: "text", nullable: true),
                    UfcStatsId = table.Column<string>(type: "text", nullable: true),
                    WeightLbs = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Fighters", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Bouts",
                columns: table => new
                {
                    Id = table.Column<string>(type: "text", nullable: false),
                    BoutOrder = table.Column<int>(type: "integer", nullable: true),
                    CardSection = table.Column<string>(type: "text", nullable: true),
                    CardSectionOrder = table.Column<int>(type: "integer", nullable: true),
                    DuplicateOfBoutId = table.Column<string>(type: "text", nullable: true),
                    EventId = table.Column<Guid>(type: "uuid", nullable: false),
                    Fighter1Corner = table.Column<string>(type: "text", nullable: true),
                    Fighter1Id = table.Column<Guid>(type: "uuid", nullable: true),
                    Fighter1Name = table.Column<string>(type: "text", nullable: true),
                    Fighter1Outcome = table.Column<string>(type: "text", nullable: true),
                    Fighter1Slug = table.Column<string>(type: "text", nullable: true),
                    Fighter2Corner = table.Column<string>(type: "text", nullable: true),
                    Fighter2Id = table.Column<Guid>(type: "uuid", nullable: true),
                    Fighter2Name = table.Column<string>(type: "text", nullable: true),
                    Fighter2Outcome = table.Column<string>(type: "text", nullable: true),
                    Fighter2Slug = table.Column<string>(type: "text", nullable: true),
                    IsCancelled = table.Column<bool>(type: "boolean", nullable: false),
                    Method = table.Column<string>(type: "text", nullable: true),
                    ResultRound = table.Column<int>(type: "integer", nullable: true),
                    ResultTime = table.Column<string>(type: "text", nullable: true),
                    Status = table.Column<string>(type: "text", nullable: true),
                    TitleBout = table.Column<bool>(type: "boolean", nullable: false),
                    WeightClass = table.Column<string>(type: "text", nullable: true),
                    WinnerFighterId = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Bouts", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Bouts_Events_EventId",
                        column: x => x.EventId,
                        principalTable: "Events",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_Bouts_Fighters_Fighter1Id",
                        column: x => x.Fighter1Id,
                        principalTable: "Fighters",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_Bouts_Fighters_Fighter2Id",
                        column: x => x.Fighter2Id,
                        principalTable: "Fighters",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_Bouts_Fighters_WinnerFighterId",
                        column: x => x.WinnerFighterId,
                        principalTable: "Fighters",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
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

            migrationBuilder.CreateTable(
                name: "FighterStats",
                columns: table => new
                {
                    FighterId = table.Column<Guid>(type: "uuid", nullable: false),
                    AverageFightTimeSeconds = table.Column<int>(type: "integer", nullable: false),
                    BodyStrikePercent = table.Column<decimal>(type: "numeric", nullable: false),
                    ClinchStrikePercent = table.Column<decimal>(type: "numeric", nullable: false),
                    GroundStrikePercent = table.Column<decimal>(type: "numeric", nullable: false),
                    HeadStrikePercent = table.Column<decimal>(type: "numeric", nullable: false),
                    KnockdownAvg = table.Column<decimal>(type: "numeric", nullable: false),
                    LegStrikePercent = table.Column<decimal>(type: "numeric", nullable: false),
                    SigStrikesAbsorbedPerMin = table.Column<decimal>(type: "numeric", nullable: false),
                    SigStrikesAttempted = table.Column<int>(type: "integer", nullable: false),
                    SigStrikesDefense = table.Column<decimal>(type: "numeric", nullable: false),
                    SigStrikesLanded = table.Column<int>(type: "integer", nullable: false),
                    SigStrikesLandedPerMin = table.Column<decimal>(type: "numeric", nullable: false),
                    StandingStrikePercent = table.Column<decimal>(type: "numeric", nullable: false),
                    StrikingAccuracy = table.Column<decimal>(type: "numeric", nullable: false),
                    SubmissionAvgPer15Min = table.Column<decimal>(type: "numeric", nullable: false),
                    TakedownAccuracy = table.Column<decimal>(type: "numeric", nullable: false),
                    TakedownAvgPer15Min = table.Column<decimal>(type: "numeric", nullable: false),
                    TakedownDefense = table.Column<decimal>(type: "numeric", nullable: false),
                    TakedownsAttempted = table.Column<int>(type: "integer", nullable: false),
                    TakedownsLanded = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_FighterStats", x => x.FighterId);
                    table.ForeignKey(
                        name: "FK_FighterStats_Fighters_FighterId",
                        column: x => x.FighterId,
                        principalTable: "Fighters",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "BoutFighterStats",
                columns: table => new
                {
                    Id = table.Column<string>(type: "text", nullable: false),
                    BodyAttempted = table.Column<int>(type: "integer", nullable: false),
                    BodyLanded = table.Column<int>(type: "integer", nullable: false),
                    BoutId = table.Column<string>(type: "text", nullable: false),
                    ClinchAttempted = table.Column<int>(type: "integer", nullable: false),
                    ClinchLanded = table.Column<int>(type: "integer", nullable: false),
                    ControlTime = table.Column<string>(type: "text", nullable: true),
                    DistanceAttempted = table.Column<int>(type: "integer", nullable: false),
                    DistanceLanded = table.Column<int>(type: "integer", nullable: false),
                    FighterId = table.Column<Guid>(type: "uuid", nullable: true),
                    FighterSlug = table.Column<string>(type: "text", nullable: true),
                    GroundAttempted = table.Column<int>(type: "integer", nullable: false),
                    GroundLanded = table.Column<int>(type: "integer", nullable: false),
                    HeadAttempted = table.Column<int>(type: "integer", nullable: false),
                    HeadLanded = table.Column<int>(type: "integer", nullable: false),
                    Knockdowns = table.Column<int>(type: "integer", nullable: false),
                    LegAttempted = table.Column<int>(type: "integer", nullable: false),
                    LegLanded = table.Column<int>(type: "integer", nullable: false),
                    Reversals = table.Column<int>(type: "integer", nullable: false),
                    SigStrikesAttempted = table.Column<int>(type: "integer", nullable: false),
                    SigStrikesLanded = table.Column<int>(type: "integer", nullable: false),
                    SubmissionAttempts = table.Column<int>(type: "integer", nullable: false),
                    TakedownsAttempted = table.Column<int>(type: "integer", nullable: false),
                    TakedownsLanded = table.Column<int>(type: "integer", nullable: false),
                    TotalStrikesAttempted = table.Column<int>(type: "integer", nullable: false),
                    TotalStrikesLanded = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_BoutFighterStats", x => x.Id);
                    table.ForeignKey(
                        name: "FK_BoutFighterStats_Bouts_BoutId",
                        column: x => x.BoutId,
                        principalTable: "Bouts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_BoutFighterStats_Fighters_FighterId",
                        column: x => x.FighterId,
                        principalTable: "Fighters",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateIndex(
                name: "IX_BoutFighterStats_BoutId",
                table: "BoutFighterStats",
                column: "BoutId");

            migrationBuilder.CreateIndex(
                name: "IX_BoutFighterStats_FighterId",
                table: "BoutFighterStats",
                column: "FighterId");

            migrationBuilder.CreateIndex(
                name: "IX_Bouts_EventId",
                table: "Bouts",
                column: "EventId");

            migrationBuilder.CreateIndex(
                name: "IX_Bouts_Fighter1Id",
                table: "Bouts",
                column: "Fighter1Id");

            migrationBuilder.CreateIndex(
                name: "IX_Bouts_Fighter2Id",
                table: "Bouts",
                column: "Fighter2Id");

            migrationBuilder.CreateIndex(
                name: "IX_Bouts_WinnerFighterId",
                table: "Bouts",
                column: "WinnerFighterId");

            migrationBuilder.CreateIndex(
                name: "IX_FighterHeroStats_FighterId",
                table: "FighterHeroStats",
                column: "FighterId");
        }
    }
}
