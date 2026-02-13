using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace jifas_assistant.DAL.Migrations
{
    /// <inheritdoc />
    public partial class AddChatHistoryTable : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "ChatHistory",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    SessionId = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    UserId = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    UserMessage = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    AiResponse = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    ResponseSource = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    ConfidenceScore = table.Column<double>(type: "float", nullable: true),
                    IsFromKnowledgeBase = table.Column<bool>(type: "bit", nullable: false),
                    ResponseTimeMs = table.Column<long>(type: "bigint", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false, defaultValueSql: "(getutcdate())"),
                    Success = table.Column<bool>(type: "bit", nullable: false),
                    UsedDocumentIds = table.Column<string>(type: "nvarchar(max)", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ChatHistory", x => x.Id);
                });

            // Add indexes for common queries
            migrationBuilder.CreateIndex(
                name: "IX_ChatHistory_SessionId",
                table: "ChatHistory",
                column: "SessionId");

            migrationBuilder.CreateIndex(
                name: "IX_ChatHistory_UserId",
                table: "ChatHistory",
                column: "UserId");

            migrationBuilder.CreateIndex(
                name: "IX_ChatHistory_CreatedAt",
                table: "ChatHistory",
                column: "CreatedAt");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ChatHistory");
        }
    }
}
