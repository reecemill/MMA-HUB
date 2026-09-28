using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Scraper.Migrations
{
    /// <inheritdoc />
    public partial class AddBoutFighterStats : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "BoutFighterStats",
                columns: table => new
                {
                    Id = table.Column<string>(type: "text", nullable: false),
                    BoutId = table.Column<string>(type: "text", nullable: false),
                    FighterId = table.Column<Guid>(type: "uuid", nullable: true),
                    FighterSlug = table.Column<string>(type: "text", nullable: true),
                    Knockdowns = table.Column<int>(type: "integer", nullable: false),
                    SigStrikesLanded = table.Column<int>(type: "integer", nullable: false),
                    SigStrikesAttempted = table.Column<int>(type: "integer", nullable: false),
                    TotalStrikesLanded = table.Column<int>(type: "integer", nullable: false),
                    TotalStrikesAttempted = table.Column<int>(type: "integer", nullable: false),
                    TakedownsLanded = table.Column<int>(type: "integer", nullable: false),
                    TakedownsAttempted = table.Column<int>(type: "integer", nullable: false),
                    SubmissionAttempts = table.Column<int>(type: "integer", nullable: false),
                    Reversals = table.Column<int>(type: "integer", nullable: false),
                    ControlTime = table.Column<string>(type: "text", nullable: true),
                    HeadLanded = table.Column<int>(type: "integer", nullable: false),
                    HeadAttempted = table.Column<int>(type: "integer", nullable: false),
                    BodyLanded = table.Column<int>(type: "integer", nullable: false),
                    BodyAttempted = table.Column<int>(type: "integer", nullable: false),
                    LegLanded = table.Column<int>(type: "integer", nullable: false),
                    LegAttempted = table.Column<int>(type: "integer", nullable: false),
                    DistanceLanded = table.Column<int>(type: "integer", nullable: false),
                    DistanceAttempted = table.Column<int>(type: "integer", nullable: false),
                    ClinchLanded = table.Column<int>(type: "integer", nullable: false),
                    ClinchAttempted = table.Column<int>(type: "integer", nullable: false),
                    GroundLanded = table.Column<int>(type: "integer", nullable: false),
                    GroundAttempted = table.Column<int>(type: "integer", nullable: false)
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
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "BoutFighterStats");
        }
    }
}
