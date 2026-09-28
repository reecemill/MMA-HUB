using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Scraper.Migrations
{
    /// <inheritdoc />
    public partial class AddFighterStats : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterColumn<double>(
                name: "DataAgeHours",
                table: "Events",
                type: "double precision",
                nullable: true,
                oldClrType: typeof(int),
                oldType: "integer",
                oldNullable: true);

            migrationBuilder.CreateTable(
                name: "FighterStats",
                columns: table => new
                {
                    FighterId = table.Column<Guid>(type: "uuid", nullable: false),
                    SigStrikesLanded = table.Column<int>(type: "integer", nullable: false),
                    SigStrikesAttempted = table.Column<int>(type: "integer", nullable: false),
                    StrikingAccuracy = table.Column<decimal>(type: "numeric", nullable: false),
                    SigStrikesLandedPerMin = table.Column<decimal>(type: "numeric", nullable: false),
                    SigStrikesAbsorbedPerMin = table.Column<decimal>(type: "numeric", nullable: false),
                    SigStrikesDefense = table.Column<decimal>(type: "numeric", nullable: false),
                    TakedownsLanded = table.Column<int>(type: "integer", nullable: false),
                    TakedownsAttempted = table.Column<int>(type: "integer", nullable: false),
                    TakedownAccuracy = table.Column<decimal>(type: "numeric", nullable: false),
                    TakedownAvgPer15Min = table.Column<decimal>(type: "numeric", nullable: false),
                    TakedownDefense = table.Column<decimal>(type: "numeric", nullable: false),
                    SubmissionAvgPer15Min = table.Column<decimal>(type: "numeric", nullable: false),
                    KnockdownAvg = table.Column<decimal>(type: "numeric", nullable: false),
                    AverageFightTimeSeconds = table.Column<int>(type: "integer", nullable: false),
                    StandingStrikePercent = table.Column<decimal>(type: "numeric", nullable: false),
                    ClinchStrikePercent = table.Column<decimal>(type: "numeric", nullable: false),
                    GroundStrikePercent = table.Column<decimal>(type: "numeric", nullable: false),
                    HeadStrikePercent = table.Column<decimal>(type: "numeric", nullable: false),
                    BodyStrikePercent = table.Column<decimal>(type: "numeric", nullable: false),
                    LegStrikePercent = table.Column<decimal>(type: "numeric", nullable: false)
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
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "FighterStats");

            migrationBuilder.AlterColumn<int>(
                name: "DataAgeHours",
                table: "Events",
                type: "integer",
                nullable: true,
                oldClrType: typeof(double),
                oldType: "double precision",
                oldNullable: true);
        }
    }
}
