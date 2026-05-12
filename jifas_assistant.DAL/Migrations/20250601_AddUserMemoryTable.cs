using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace jifas_assistant.DAL.Migrations
{
    /// <inheritdoc />
    public partial class AddUserMemoryTable : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "UserMemory",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    UserId = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    FavoriteModules = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    FrequentTopics = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    RecentQuestions = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    ExpertiseLevel = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false, defaultValue: "Beginner"),
                    PreferredLanguage = table.Column<string>(type: "nvarchar(5)", maxLength: 5, nullable: false, defaultValue: "id"),
                    DetectedDepartment = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    DetectedRole = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    TotalSessions = table.Column<int>(type: "int", nullable: false, defaultValue: 0),
                    TotalQuestions = table.Column<int>(type: "int", nullable: false, defaultValue: 0),
                    HowToCount = table.Column<int>(type: "int", nullable: false, defaultValue: 0),
                    TroubleshootingCount = table.Column<int>(type: "int", nullable: false, defaultValue: 0),
                    AverageConfidenceReceived = table.Column<double>(type: "float", nullable: false, defaultValue: 0.0),
                    FirstSeenAt = table.Column<DateTime>(type: "datetime2", nullable: false, defaultValueSql: "(getutcdate())"),
                    LastSeenAt = table.Column<DateTime>(type: "datetime2", nullable: false, defaultValueSql: "(getutcdate())"),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2", nullable: false, defaultValueSql: "(getutcdate())")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_UserMemory", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_UserMemory_UserId",
                table: "UserMemory",
                column: "UserId",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(name: "UserMemory");
        }
    }
}
