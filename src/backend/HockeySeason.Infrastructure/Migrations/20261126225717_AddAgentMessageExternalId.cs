using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace HockeySeason.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddAgentMessageExternalId : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "external_id",
                table: "message",
                type: "text",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "ix_message_agent_id_external_id",
                table: "message",
                columns: new[] { "agent_id", "external_id" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_message_agent_id_external_id",
                table: "message");

            migrationBuilder.DropColumn(
                name: "external_id",
                table: "message");
        }
    }
}
