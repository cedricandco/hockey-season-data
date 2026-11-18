using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace HockeySeason.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class LeagueIsSeasonCategory : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_leagues_external_id",
                table: "leagues");

            migrationBuilder.DropIndex(
                name: "ix_leagues_season_id",
                table: "leagues");

            migrationBuilder.CreateIndex(
                name: "ix_leagues_season_id_external_id",
                table: "leagues",
                columns: new[] { "season_id", "external_id" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_leagues_season_id_external_id",
                table: "leagues");

            migrationBuilder.CreateIndex(
                name: "ix_leagues_external_id",
                table: "leagues",
                column: "external_id",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_leagues_season_id",
                table: "leagues",
                column: "season_id");
        }
    }
}
