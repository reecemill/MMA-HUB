using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace Scraper.Migrations
{
    /// <inheritdoc />
    public partial class AddUfcCom : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "UfcEvents",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false),
                    Name = table.Column<string>(type: "text", nullable: false),
                    StartTime = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    TimeZone = table.Column<string>(type: "text", nullable: true),
                    Status = table.Column<string>(type: "text", nullable: true),
                    VenueId = table.Column<int>(type: "integer", nullable: true),
                    Venue = table.Column<string>(type: "text", nullable: true),
                    City = table.Column<string>(type: "text", nullable: true),
                    State = table.Column<string>(type: "text", nullable: true),
                    Country = table.Column<string>(type: "text", nullable: true),
                    CountryCode = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_UfcEvents", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "UfcFighters",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false),
                    MmaId = table.Column<int>(type: "integer", nullable: true),
                    FirstName = table.Column<string>(type: "text", nullable: true),
                    LastName = table.Column<string>(type: "text", nullable: true),
                    NickName = table.Column<string>(type: "text", nullable: true),
                    Slug = table.Column<string>(type: "text", nullable: true),
                    DateOfBirth = table.Column<DateOnly>(type: "date", nullable: true),
                    BornCity = table.Column<string>(type: "text", nullable: true),
                    BornState = table.Column<string>(type: "text", nullable: true),
                    BornCountry = table.Column<string>(type: "text", nullable: true),
                    FightingOutOfCity = table.Column<string>(type: "text", nullable: true),
                    FightingOutOfState = table.Column<string>(type: "text", nullable: true),
                    FightingOutOfCountry = table.Column<string>(type: "text", nullable: true),
                    Stance = table.Column<string>(type: "text", nullable: true),
                    HeightInches = table.Column<decimal>(type: "numeric", nullable: true),
                    ReachInches = table.Column<decimal>(type: "numeric", nullable: true),
                    WeightLbs = table.Column<decimal>(type: "numeric", nullable: true),
                    WeightClass = table.Column<string>(type: "text", nullable: true),
                    Wins = table.Column<int>(type: "integer", nullable: true),
                    Losses = table.Column<int>(type: "integer", nullable: true),
                    Draws = table.Column<int>(type: "integer", nullable: true),
                    NoContests = table.Column<int>(type: "integer", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_UfcFighters", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "UfcFighterProfiles",
                columns: table => new
                {
                    FighterId = table.Column<int>(type: "integer", nullable: false),
                    Slug = table.Column<string>(type: "text", nullable: false),
                    FetchedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    Status = table.Column<string>(type: "text", nullable: true),
                    Division = table.Column<string>(type: "text", nullable: true),
                    DivisionRank = table.Column<int>(type: "integer", nullable: true),
                    PoundForPoundRank = table.Column<int>(type: "integer", nullable: true),
                    IsChampion = table.Column<bool>(type: "boolean", nullable: false),
                    RecordText = table.Column<string>(type: "text", nullable: true),
                    PlaceOfBirth = table.Column<string>(type: "text", nullable: true),
                    TrainsAt = table.Column<string>(type: "text", nullable: true),
                    FightingStyle = table.Column<string>(type: "text", nullable: true),
                    Age = table.Column<int>(type: "integer", nullable: true),
                    HeightInches = table.Column<decimal>(type: "numeric", nullable: true),
                    WeightLbs = table.Column<decimal>(type: "numeric", nullable: true),
                    ReachInches = table.Column<decimal>(type: "numeric", nullable: true),
                    LegReachInches = table.Column<decimal>(type: "numeric", nullable: true),
                    OctagonDebut = table.Column<DateOnly>(type: "date", nullable: true),
                    ImageUrl = table.Column<string>(type: "text", nullable: true),
                    WinsByKnockout = table.Column<int>(type: "integer", nullable: true),
                    WinsBySubmission = table.Column<int>(type: "integer", nullable: true),
                    WinsByDecision = table.Column<int>(type: "integer", nullable: true),
                    FirstRoundFinishes = table.Column<int>(type: "integer", nullable: true),
                    SigStrikesLanded = table.Column<int>(type: "integer", nullable: true),
                    SigStrikesAttempted = table.Column<int>(type: "integer", nullable: true),
                    StrikingAccuracy = table.Column<decimal>(type: "numeric", nullable: true),
                    TakedownsLanded = table.Column<int>(type: "integer", nullable: true),
                    TakedownsAttempted = table.Column<int>(type: "integer", nullable: true),
                    TakedownAccuracy = table.Column<decimal>(type: "numeric", nullable: true),
                    SigStrikesLandedPerMin = table.Column<decimal>(type: "numeric", nullable: true),
                    SigStrikesAbsorbedPerMin = table.Column<decimal>(type: "numeric", nullable: true),
                    TakedownAvgPer15Min = table.Column<decimal>(type: "numeric", nullable: true),
                    SubmissionAvgPer15Min = table.Column<decimal>(type: "numeric", nullable: true),
                    SigStrikeDefense = table.Column<decimal>(type: "numeric", nullable: true),
                    TakedownDefense = table.Column<decimal>(type: "numeric", nullable: true),
                    KnockdownAvg = table.Column<decimal>(type: "numeric", nullable: true),
                    AverageFightSeconds = table.Column<int>(type: "integer", nullable: true),
                    SigStrikesStanding = table.Column<int>(type: "integer", nullable: true),
                    SigStrikesClinch = table.Column<int>(type: "integer", nullable: true),
                    SigStrikesGround = table.Column<int>(type: "integer", nullable: true),
                    SigStrikesHead = table.Column<int>(type: "integer", nullable: true),
                    SigStrikesBody = table.Column<int>(type: "integer", nullable: true),
                    SigStrikesLeg = table.Column<int>(type: "integer", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_UfcFighterProfiles", x => x.FighterId);
                    table.ForeignKey(
                        name: "FK_UfcFighterProfiles_UfcFighters_FighterId",
                        column: x => x.FighterId,
                        principalTable: "UfcFighters",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "UfcFights",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false),
                    EventId = table.Column<int>(type: "integer", nullable: false),
                    FightOrder = table.Column<int>(type: "integer", nullable: true),
                    Status = table.Column<string>(type: "text", nullable: true),
                    CardSegment = table.Column<string>(type: "text", nullable: true),
                    CardSegmentStartTime = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    CardSegmentBroadcaster = table.Column<string>(type: "text", nullable: true),
                    WeightClassId = table.Column<int>(type: "integer", nullable: true),
                    WeightClass = table.Column<string>(type: "text", nullable: true),
                    IsTitleFight = table.Column<bool>(type: "boolean", nullable: false),
                    TitleName = table.Column<string>(type: "text", nullable: true),
                    ScheduledRounds = table.Column<int>(type: "integer", nullable: true),
                    RuleSet = table.Column<string>(type: "text", nullable: true),
                    RefereeId = table.Column<int>(type: "integer", nullable: true),
                    Referee = table.Column<string>(type: "text", nullable: true),
                    WinnerFighterId = table.Column<int>(type: "integer", nullable: true),
                    Method = table.Column<string>(type: "text", nullable: true),
                    EndingRound = table.Column<int>(type: "integer", nullable: true),
                    EndingTime = table.Column<string>(type: "text", nullable: true),
                    DurationSeconds = table.Column<int>(type: "integer", nullable: true),
                    EndingStrike = table.Column<string>(type: "text", nullable: true),
                    EndingTarget = table.Column<string>(type: "text", nullable: true),
                    EndingPosition = table.Column<string>(type: "text", nullable: true),
                    EndingSubmission = table.Column<string>(type: "text", nullable: true),
                    EndingNotes = table.Column<string>(type: "text", nullable: true),
                    FightOfTheNight = table.Column<bool>(type: "boolean", nullable: false),
                    OfficialStats = table.Column<bool>(type: "boolean", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_UfcFights", x => x.Id);
                    table.ForeignKey(
                        name: "FK_UfcFights_UfcEvents_EventId",
                        column: x => x.EventId,
                        principalTable: "UfcEvents",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_UfcFights_UfcFighters_WinnerFighterId",
                        column: x => x.WinnerFighterId,
                        principalTable: "UfcFighters",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateTable(
                name: "UfcRankings",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    Division = table.Column<string>(type: "text", nullable: false),
                    Rank = table.Column<int>(type: "integer", nullable: false),
                    RankChange = table.Column<string>(type: "text", nullable: true),
                    FighterSlug = table.Column<string>(type: "text", nullable: false),
                    FighterName = table.Column<string>(type: "text", nullable: true),
                    FighterId = table.Column<int>(type: "integer", nullable: true),
                    FetchedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_UfcRankings", x => x.Id);
                    table.ForeignKey(
                        name: "FK_UfcRankings_UfcFighters_FighterId",
                        column: x => x.FighterId,
                        principalTable: "UfcFighters",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateTable(
                name: "UfcFightActions",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false),
                    FightId = table.Column<int>(type: "integer", nullable: false),
                    FighterId = table.Column<int>(type: "integer", nullable: true),
                    Type = table.Column<string>(type: "text", nullable: true),
                    Round = table.Column<int>(type: "integer", nullable: true),
                    RoundTime = table.Column<string>(type: "text", nullable: true),
                    Timestamp = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_UfcFightActions", x => x.Id);
                    table.ForeignKey(
                        name: "FK_UfcFightActions_UfcFighters_FighterId",
                        column: x => x.FighterId,
                        principalTable: "UfcFighters",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_UfcFightActions_UfcFights_FightId",
                        column: x => x.FightId,
                        principalTable: "UfcFights",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "UfcFightFighters",
                columns: table => new
                {
                    FightId = table.Column<int>(type: "integer", nullable: false),
                    FighterId = table.Column<int>(type: "integer", nullable: false),
                    Corner = table.Column<string>(type: "text", nullable: true),
                    Outcome = table.Column<string>(type: "text", nullable: true),
                    WeighIn = table.Column<decimal>(type: "numeric", nullable: true),
                    KOOfTheNight = table.Column<bool>(type: "boolean", nullable: false),
                    SubmissionOfTheNight = table.Column<bool>(type: "boolean", nullable: false),
                    PerformanceOfTheNight = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_UfcFightFighters", x => new { x.FightId, x.FighterId });
                    table.ForeignKey(
                        name: "FK_UfcFightFighters_UfcFighters_FighterId",
                        column: x => x.FighterId,
                        principalTable: "UfcFighters",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_UfcFightFighters_UfcFights_FightId",
                        column: x => x.FightId,
                        principalTable: "UfcFights",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "UfcFightScores",
                columns: table => new
                {
                    FightId = table.Column<int>(type: "integer", nullable: false),
                    JudgeId = table.Column<int>(type: "integer", nullable: false),
                    FighterId = table.Column<int>(type: "integer", nullable: false),
                    JudgeName = table.Column<string>(type: "text", nullable: true),
                    Score = table.Column<int>(type: "integer", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_UfcFightScores", x => new { x.FightId, x.JudgeId, x.FighterId });
                    table.ForeignKey(
                        name: "FK_UfcFightScores_UfcFighters_FighterId",
                        column: x => x.FighterId,
                        principalTable: "UfcFighters",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_UfcFightScores_UfcFights_FightId",
                        column: x => x.FightId,
                        principalTable: "UfcFights",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "UfcFightStats",
                columns: table => new
                {
                    FightId = table.Column<int>(type: "integer", nullable: false),
                    FighterId = table.Column<int>(type: "integer", nullable: false),
                    Knockdowns = table.Column<int>(type: "integer", nullable: false),
                    TotalStrikesAttempted = table.Column<int>(type: "integer", nullable: false),
                    TotalStrikesLanded = table.Column<int>(type: "integer", nullable: false),
                    TotalHeadStrikesAttempted = table.Column<int>(type: "integer", nullable: false),
                    TotalHeadStrikesLanded = table.Column<int>(type: "integer", nullable: false),
                    TotalBodyStrikesAttempted = table.Column<int>(type: "integer", nullable: false),
                    TotalBodyStrikesLanded = table.Column<int>(type: "integer", nullable: false),
                    TotalLegStrikesAttempted = table.Column<int>(type: "integer", nullable: false),
                    TotalLegStrikesLanded = table.Column<int>(type: "integer", nullable: false),
                    TotalClinchStrikesAttempted = table.Column<int>(type: "integer", nullable: false),
                    TotalClinchStrikesLanded = table.Column<int>(type: "integer", nullable: false),
                    TotalGroundStrikesAttempted = table.Column<int>(type: "integer", nullable: false),
                    TotalGroundStrikesLanded = table.Column<int>(type: "integer", nullable: false),
                    SigStrikesAttempted = table.Column<int>(type: "integer", nullable: false),
                    SigStrikesLanded = table.Column<int>(type: "integer", nullable: false),
                    SigStrikesAccuracy = table.Column<decimal>(type: "numeric", nullable: true),
                    SigHeadStrikesAttempted = table.Column<int>(type: "integer", nullable: false),
                    SigHeadStrikesLanded = table.Column<int>(type: "integer", nullable: false),
                    SigBodyStrikesAttempted = table.Column<int>(type: "integer", nullable: false),
                    SigBodyStrikesLanded = table.Column<int>(type: "integer", nullable: false),
                    SigLegStrikesAttempted = table.Column<int>(type: "integer", nullable: false),
                    SigLegStrikesLanded = table.Column<int>(type: "integer", nullable: false),
                    SigDistanceStrikesAttempted = table.Column<int>(type: "integer", nullable: false),
                    SigDistanceStrikesLanded = table.Column<int>(type: "integer", nullable: false),
                    SigClinchStrikesAttempted = table.Column<int>(type: "integer", nullable: false),
                    SigClinchStrikesLanded = table.Column<int>(type: "integer", nullable: false),
                    SigGroundStrikesAttempted = table.Column<int>(type: "integer", nullable: false),
                    SigGroundStrikesLanded = table.Column<int>(type: "integer", nullable: false),
                    SigDistanceHeadStrikesAttempted = table.Column<int>(type: "integer", nullable: false),
                    SigDistanceHeadStrikesLanded = table.Column<int>(type: "integer", nullable: false),
                    SigDistanceBodyStrikesAttempted = table.Column<int>(type: "integer", nullable: false),
                    SigDistanceBodyStrikesLanded = table.Column<int>(type: "integer", nullable: false),
                    SigDistanceLegStrikesAttempted = table.Column<int>(type: "integer", nullable: false),
                    SigDistanceLegStrikesLanded = table.Column<int>(type: "integer", nullable: false),
                    SigClinchHeadStrikesAttempted = table.Column<int>(type: "integer", nullable: false),
                    SigClinchHeadStrikesLanded = table.Column<int>(type: "integer", nullable: false),
                    SigClinchBodyStrikesAttempted = table.Column<int>(type: "integer", nullable: false),
                    SigClinchBodyStrikesLanded = table.Column<int>(type: "integer", nullable: false),
                    SigClinchLegStrikesAttempted = table.Column<int>(type: "integer", nullable: false),
                    SigClinchLegStrikesLanded = table.Column<int>(type: "integer", nullable: false),
                    SigGroundHeadStrikesAttempted = table.Column<int>(type: "integer", nullable: false),
                    SigGroundHeadStrikesLanded = table.Column<int>(type: "integer", nullable: false),
                    SigGroundBodyStrikesAttempted = table.Column<int>(type: "integer", nullable: false),
                    SigGroundBodyStrikesLanded = table.Column<int>(type: "integer", nullable: false),
                    SigGroundLegStrikesAttempted = table.Column<int>(type: "integer", nullable: false),
                    SigGroundLegStrikesLanded = table.Column<int>(type: "integer", nullable: false),
                    TakedownsAttempted = table.Column<int>(type: "integer", nullable: false),
                    TakedownsLanded = table.Column<int>(type: "integer", nullable: false),
                    TakedownsAccuracy = table.Column<decimal>(type: "numeric", nullable: true),
                    Reversals = table.Column<int>(type: "integer", nullable: false),
                    SubmissionsAttempted = table.Column<int>(type: "integer", nullable: false),
                    NeutralSeconds = table.Column<int>(type: "integer", nullable: false),
                    StandingSeconds = table.Column<int>(type: "integer", nullable: false),
                    DistanceSeconds = table.Column<int>(type: "integer", nullable: false),
                    ClinchSeconds = table.Column<int>(type: "integer", nullable: false),
                    GroundSeconds = table.Column<int>(type: "integer", nullable: false),
                    ControlSeconds = table.Column<int>(type: "integer", nullable: false),
                    ClinchControlSeconds = table.Column<int>(type: "integer", nullable: false),
                    GroundControlSeconds = table.Column<int>(type: "integer", nullable: false),
                    MiscGroundControlSeconds = table.Column<int>(type: "integer", nullable: false),
                    GuardControlSeconds = table.Column<int>(type: "integer", nullable: false),
                    HalfGuardControlSeconds = table.Column<int>(type: "integer", nullable: false),
                    SideControlSeconds = table.Column<int>(type: "integer", nullable: false),
                    MountControlSeconds = table.Column<int>(type: "integer", nullable: false),
                    BackControlSeconds = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_UfcFightStats", x => new { x.FightId, x.FighterId });
                    table.ForeignKey(
                        name: "FK_UfcFightStats_UfcFighters_FighterId",
                        column: x => x.FighterId,
                        principalTable: "UfcFighters",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_UfcFightStats_UfcFights_FightId",
                        column: x => x.FightId,
                        principalTable: "UfcFights",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "UfcRoundScores",
                columns: table => new
                {
                    FightId = table.Column<int>(type: "integer", nullable: false),
                    JudgeId = table.Column<int>(type: "integer", nullable: false),
                    Round = table.Column<int>(type: "integer", nullable: false),
                    FighterId = table.Column<int>(type: "integer", nullable: false),
                    JudgeName = table.Column<string>(type: "text", nullable: true),
                    Score = table.Column<int>(type: "integer", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_UfcRoundScores", x => new { x.FightId, x.JudgeId, x.Round, x.FighterId });
                    table.ForeignKey(
                        name: "FK_UfcRoundScores_UfcFighters_FighterId",
                        column: x => x.FighterId,
                        principalTable: "UfcFighters",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_UfcRoundScores_UfcFights_FightId",
                        column: x => x.FightId,
                        principalTable: "UfcFights",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "UfcRoundStats",
                columns: table => new
                {
                    FightId = table.Column<int>(type: "integer", nullable: false),
                    FighterId = table.Column<int>(type: "integer", nullable: false),
                    Round = table.Column<int>(type: "integer", nullable: false),
                    Knockdowns = table.Column<int>(type: "integer", nullable: false),
                    TotalStrikesAttempted = table.Column<int>(type: "integer", nullable: false),
                    TotalStrikesLanded = table.Column<int>(type: "integer", nullable: false),
                    TotalHeadStrikesAttempted = table.Column<int>(type: "integer", nullable: false),
                    TotalHeadStrikesLanded = table.Column<int>(type: "integer", nullable: false),
                    TotalBodyStrikesAttempted = table.Column<int>(type: "integer", nullable: false),
                    TotalBodyStrikesLanded = table.Column<int>(type: "integer", nullable: false),
                    TotalLegStrikesAttempted = table.Column<int>(type: "integer", nullable: false),
                    TotalLegStrikesLanded = table.Column<int>(type: "integer", nullable: false),
                    TotalClinchStrikesAttempted = table.Column<int>(type: "integer", nullable: false),
                    TotalClinchStrikesLanded = table.Column<int>(type: "integer", nullable: false),
                    TotalGroundStrikesAttempted = table.Column<int>(type: "integer", nullable: false),
                    TotalGroundStrikesLanded = table.Column<int>(type: "integer", nullable: false),
                    SigStrikesAttempted = table.Column<int>(type: "integer", nullable: false),
                    SigStrikesLanded = table.Column<int>(type: "integer", nullable: false),
                    SigStrikesAccuracy = table.Column<decimal>(type: "numeric", nullable: true),
                    SigHeadStrikesAttempted = table.Column<int>(type: "integer", nullable: false),
                    SigHeadStrikesLanded = table.Column<int>(type: "integer", nullable: false),
                    SigBodyStrikesAttempted = table.Column<int>(type: "integer", nullable: false),
                    SigBodyStrikesLanded = table.Column<int>(type: "integer", nullable: false),
                    SigLegStrikesAttempted = table.Column<int>(type: "integer", nullable: false),
                    SigLegStrikesLanded = table.Column<int>(type: "integer", nullable: false),
                    SigDistanceStrikesAttempted = table.Column<int>(type: "integer", nullable: false),
                    SigDistanceStrikesLanded = table.Column<int>(type: "integer", nullable: false),
                    SigClinchStrikesAttempted = table.Column<int>(type: "integer", nullable: false),
                    SigClinchStrikesLanded = table.Column<int>(type: "integer", nullable: false),
                    SigGroundStrikesAttempted = table.Column<int>(type: "integer", nullable: false),
                    SigGroundStrikesLanded = table.Column<int>(type: "integer", nullable: false),
                    SigDistanceHeadStrikesAttempted = table.Column<int>(type: "integer", nullable: false),
                    SigDistanceHeadStrikesLanded = table.Column<int>(type: "integer", nullable: false),
                    SigDistanceBodyStrikesAttempted = table.Column<int>(type: "integer", nullable: false),
                    SigDistanceBodyStrikesLanded = table.Column<int>(type: "integer", nullable: false),
                    SigDistanceLegStrikesAttempted = table.Column<int>(type: "integer", nullable: false),
                    SigDistanceLegStrikesLanded = table.Column<int>(type: "integer", nullable: false),
                    SigClinchHeadStrikesAttempted = table.Column<int>(type: "integer", nullable: false),
                    SigClinchHeadStrikesLanded = table.Column<int>(type: "integer", nullable: false),
                    SigClinchBodyStrikesAttempted = table.Column<int>(type: "integer", nullable: false),
                    SigClinchBodyStrikesLanded = table.Column<int>(type: "integer", nullable: false),
                    SigClinchLegStrikesAttempted = table.Column<int>(type: "integer", nullable: false),
                    SigClinchLegStrikesLanded = table.Column<int>(type: "integer", nullable: false),
                    SigGroundHeadStrikesAttempted = table.Column<int>(type: "integer", nullable: false),
                    SigGroundHeadStrikesLanded = table.Column<int>(type: "integer", nullable: false),
                    SigGroundBodyStrikesAttempted = table.Column<int>(type: "integer", nullable: false),
                    SigGroundBodyStrikesLanded = table.Column<int>(type: "integer", nullable: false),
                    SigGroundLegStrikesAttempted = table.Column<int>(type: "integer", nullable: false),
                    SigGroundLegStrikesLanded = table.Column<int>(type: "integer", nullable: false),
                    TakedownsAttempted = table.Column<int>(type: "integer", nullable: false),
                    TakedownsLanded = table.Column<int>(type: "integer", nullable: false),
                    TakedownsAccuracy = table.Column<decimal>(type: "numeric", nullable: true),
                    Reversals = table.Column<int>(type: "integer", nullable: false),
                    SubmissionsAttempted = table.Column<int>(type: "integer", nullable: false),
                    NeutralSeconds = table.Column<int>(type: "integer", nullable: false),
                    StandingSeconds = table.Column<int>(type: "integer", nullable: false),
                    DistanceSeconds = table.Column<int>(type: "integer", nullable: false),
                    ClinchSeconds = table.Column<int>(type: "integer", nullable: false),
                    GroundSeconds = table.Column<int>(type: "integer", nullable: false),
                    ControlSeconds = table.Column<int>(type: "integer", nullable: false),
                    ClinchControlSeconds = table.Column<int>(type: "integer", nullable: false),
                    GroundControlSeconds = table.Column<int>(type: "integer", nullable: false),
                    MiscGroundControlSeconds = table.Column<int>(type: "integer", nullable: false),
                    GuardControlSeconds = table.Column<int>(type: "integer", nullable: false),
                    HalfGuardControlSeconds = table.Column<int>(type: "integer", nullable: false),
                    SideControlSeconds = table.Column<int>(type: "integer", nullable: false),
                    MountControlSeconds = table.Column<int>(type: "integer", nullable: false),
                    BackControlSeconds = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_UfcRoundStats", x => new { x.FightId, x.FighterId, x.Round });
                    table.ForeignKey(
                        name: "FK_UfcRoundStats_UfcFighters_FighterId",
                        column: x => x.FighterId,
                        principalTable: "UfcFighters",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_UfcRoundStats_UfcFights_FightId",
                        column: x => x.FightId,
                        principalTable: "UfcFights",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_UfcFightActions_FighterId",
                table: "UfcFightActions",
                column: "FighterId");

            migrationBuilder.CreateIndex(
                name: "IX_UfcFightActions_FightId",
                table: "UfcFightActions",
                column: "FightId");

            migrationBuilder.CreateIndex(
                name: "IX_UfcFighters_Slug",
                table: "UfcFighters",
                column: "Slug");

            migrationBuilder.CreateIndex(
                name: "IX_UfcFightFighters_FighterId",
                table: "UfcFightFighters",
                column: "FighterId");

            migrationBuilder.CreateIndex(
                name: "IX_UfcFights_EventId",
                table: "UfcFights",
                column: "EventId");

            migrationBuilder.CreateIndex(
                name: "IX_UfcFights_WinnerFighterId",
                table: "UfcFights",
                column: "WinnerFighterId");

            migrationBuilder.CreateIndex(
                name: "IX_UfcFightScores_FighterId",
                table: "UfcFightScores",
                column: "FighterId");

            migrationBuilder.CreateIndex(
                name: "IX_UfcFightStats_FighterId",
                table: "UfcFightStats",
                column: "FighterId");

            migrationBuilder.CreateIndex(
                name: "IX_UfcRankings_FighterId",
                table: "UfcRankings",
                column: "FighterId");

            migrationBuilder.CreateIndex(
                name: "IX_UfcRoundScores_FighterId",
                table: "UfcRoundScores",
                column: "FighterId");

            migrationBuilder.CreateIndex(
                name: "IX_UfcRoundStats_FighterId",
                table: "UfcRoundStats",
                column: "FighterId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "UfcFightActions");

            migrationBuilder.DropTable(
                name: "UfcFighterProfiles");

            migrationBuilder.DropTable(
                name: "UfcFightFighters");

            migrationBuilder.DropTable(
                name: "UfcFightScores");

            migrationBuilder.DropTable(
                name: "UfcFightStats");

            migrationBuilder.DropTable(
                name: "UfcRankings");

            migrationBuilder.DropTable(
                name: "UfcRoundScores");

            migrationBuilder.DropTable(
                name: "UfcRoundStats");

            migrationBuilder.DropTable(
                name: "UfcFights");

            migrationBuilder.DropTable(
                name: "UfcEvents");

            migrationBuilder.DropTable(
                name: "UfcFighters");
        }
    }
}
