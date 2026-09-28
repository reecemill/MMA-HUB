using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Scraper.Migrations
{
    /// <inheritdoc />
    public partial class AddBouts : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "Bouts",
                columns: table => new
                {
                    Id = table.Column<string>(type: "text", nullable: false),
                    EventId = table.Column<Guid>(type: "uuid", nullable: false),
                    CardSection = table.Column<string>(type: "text", nullable: true),
                    CardSectionOrder = table.Column<int>(type: "integer", nullable: true),
                    BoutOrder = table.Column<int>(type: "integer", nullable: true),
                    WeightClass = table.Column<string>(type: "text", nullable: true),
                    TitleBout = table.Column<bool>(type: "boolean", nullable: false),
                    Status = table.Column<string>(type: "text", nullable: true),
                    IsCancelled = table.Column<bool>(type: "boolean", nullable: false),
                    ResultRound = table.Column<int>(type: "integer", nullable: true),
                    ResultTime = table.Column<string>(type: "text", nullable: true),
                    Method = table.Column<string>(type: "text", nullable: true),
                    Fighter1Id = table.Column<Guid>(type: "uuid", nullable: true),
                    Fighter1Slug = table.Column<string>(type: "text", nullable: true),
                    Fighter1Name = table.Column<string>(type: "text", nullable: true),
                    Fighter1Corner = table.Column<string>(type: "text", nullable: true),
                    Fighter1Outcome = table.Column<string>(type: "text", nullable: true),
                    Fighter2Id = table.Column<Guid>(type: "uuid", nullable: true),
                    Fighter2Slug = table.Column<string>(type: "text", nullable: true),
                    Fighter2Name = table.Column<string>(type: "text", nullable: true),
                    Fighter2Corner = table.Column<string>(type: "text", nullable: true),
                    Fighter2Outcome = table.Column<string>(type: "text", nullable: true),
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
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "Bouts");
        }
    }
}
