using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Scraper.Migrations
{
    /// <inheritdoc />
    public partial class MarkDuplicates : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "DuplicateOfEventId",
                table: "Events",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "DuplicateOfBoutId",
                table: "Bouts",
                type: "text",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "DuplicateOfEventId",
                table: "Events");

            migrationBuilder.DropColumn(
                name: "DuplicateOfBoutId",
                table: "Bouts");
        }
    }
}
