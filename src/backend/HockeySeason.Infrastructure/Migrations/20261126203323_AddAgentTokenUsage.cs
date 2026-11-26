using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace HockeySeason.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddAgentTokenUsage : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "agent",
                columns: table => new
                {
                    id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    agent_name = table.Column<string>(type: "text", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_agent", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "message",
                columns: table => new
                {
                    id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    agent_id = table.Column<int>(type: "integer", nullable: false),
                    text = table.Column<string>(type: "text", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_message", x => x.id);
                    table.ForeignKey(
                        name: "fk_message_agent_agent_id",
                        column: x => x.agent_id,
                        principalTable: "agent",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "token_usage",
                columns: table => new
                {
                    id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    agent_id = table.Column<int>(type: "integer", nullable: false),
                    input_token_count = table.Column<long>(type: "bigint", nullable: true),
                    output_token_count = table.Column<long>(type: "bigint", nullable: true),
                    total_token_count = table.Column<long>(type: "bigint", nullable: true),
                    reasoning_tokens = table.Column<long>(type: "bigint", nullable: true),
                    user_id = table.Column<int>(type: "integer", nullable: true),
                    message_id = table.Column<int>(type: "integer", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_token_usage", x => x.id);
                    table.ForeignKey(
                        name: "fk_token_usage_agent_agent_id",
                        column: x => x.agent_id,
                        principalTable: "agent",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_token_usage_message_message_id",
                        column: x => x.message_id,
                        principalTable: "message",
                        principalColumn: "id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "fk_token_usage_users_user_id",
                        column: x => x.user_id,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.InsertData(
                table: "agent",
                columns: new[] { "id", "agent_name" },
                values: new object[] { 1, "hockey-season-data.agent" });

            migrationBuilder.CreateIndex(
                name: "ix_agent_agent_name",
                table: "agent",
                column: "agent_name",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_message_agent_id",
                table: "message",
                column: "agent_id");

            migrationBuilder.CreateIndex(
                name: "ix_token_usage_agent_id",
                table: "token_usage",
                column: "agent_id");

            migrationBuilder.CreateIndex(
                name: "ix_token_usage_message_id",
                table: "token_usage",
                column: "message_id");

            migrationBuilder.CreateIndex(
                name: "ix_token_usage_user_id",
                table: "token_usage",
                column: "user_id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "token_usage");

            migrationBuilder.DropTable(
                name: "message");

            migrationBuilder.DropTable(
                name: "agent");
        }
    }
}
