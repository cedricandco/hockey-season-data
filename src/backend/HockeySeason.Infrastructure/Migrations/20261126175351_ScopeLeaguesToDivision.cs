using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace HockeySeason.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class ScopeLeaguesToDivision : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_teams_external_id",
                table: "teams");

            migrationBuilder.DropIndex(
                name: "ix_teams_league_id",
                table: "teams");

            migrationBuilder.DropIndex(
                name: "ix_leagues_division_id",
                table: "leagues");

            migrationBuilder.DropIndex(
                name: "ix_leagues_season_id_external_id",
                table: "leagues");

            migrationBuilder.DropIndex(
                name: "ix_league_schedules_external_id",
                table: "league_schedules");

            migrationBuilder.DropIndex(
                name: "ix_league_schedules_league_id",
                table: "league_schedules");

            migrationBuilder.CreateIndex(
                name: "ix_teams_external_id",
                table: "teams",
                column: "external_id");

            migrationBuilder.CreateIndex(
                name: "ix_teams_league_id_external_id",
                table: "teams",
                columns: new[] { "league_id", "external_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_leagues_division_id_season_id_external_id",
                table: "leagues",
                columns: new[] { "division_id", "season_id", "external_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_leagues_season_id",
                table: "leagues",
                column: "season_id");

            migrationBuilder.CreateIndex(
                name: "ix_league_schedules_external_id",
                table: "league_schedules",
                column: "external_id");

            migrationBuilder.CreateIndex(
                name: "ix_league_schedules_league_id_external_id",
                table: "league_schedules",
                columns: new[] { "league_id", "external_id" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_teams_external_id",
                table: "teams");

            migrationBuilder.DropIndex(
                name: "ix_teams_league_id_external_id",
                table: "teams");

            migrationBuilder.DropIndex(
                name: "ix_leagues_division_id_season_id_external_id",
                table: "leagues");

            migrationBuilder.DropIndex(
                name: "ix_leagues_season_id",
                table: "leagues");

            migrationBuilder.DropIndex(
                name: "ix_league_schedules_external_id",
                table: "league_schedules");

            migrationBuilder.DropIndex(
                name: "ix_league_schedules_league_id_external_id",
                table: "league_schedules");

            migrationBuilder.CreateIndex(
                name: "ix_teams_external_id",
                table: "teams",
                column: "external_id",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_teams_league_id",
                table: "teams",
                column: "league_id");

            migrationBuilder.CreateIndex(
                name: "ix_leagues_division_id",
                table: "leagues",
                column: "division_id");

            migrationBuilder.CreateIndex(
                name: "ix_leagues_season_id_external_id",
                table: "leagues",
                columns: new[] { "season_id", "external_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_league_schedules_external_id",
                table: "league_schedules",
                column: "external_id",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_league_schedules_league_id",
                table: "league_schedules",
                column: "league_id");
        }
    }
}
