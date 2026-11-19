using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DeskPal.Server.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddPersonalSync : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "PersonalSync",
                table: "Users",
                type: "INTEGER",
                nullable: false,
                defaultValue: false);

            // Accounts that already keep personal data here stay synced, so a computer upgraded first does not lose it on the others.
            migrationBuilder.Sql("""
                UPDATE "Users" SET "PersonalSync" = 1
                WHERE "Preferences" IS NOT NULL OR "IdleMinutes" IS NOT NULL
                   OR EXISTS (SELECT 1 FROM "UserRules" r WHERE r."UserId" = "Users"."Id")
                """);

            migrationBuilder.CreateTable(
                name: "SharedRuleReports",
                columns: table => new
                {
                    UserId = table.Column<Guid>(type: "TEXT", nullable: false),
                    Pattern = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false),
                    Category = table.Column<int>(type: "INTEGER", nullable: false),
                    Name = table.Column<string>(type: "TEXT", maxLength: 60, nullable: true),
                    ReportedAt = table.Column<DateTimeOffset>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SharedRuleReports", x => new { x.UserId, x.Pattern });
                    table.ForeignKey(
                        name: "FK_SharedRuleReports_Users_UserId",
                        column: x => x.UserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "UserNoTitleRules",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    UserId = table.Column<Guid>(type: "TEXT", nullable: false),
                    Kind = table.Column<int>(type: "INTEGER", nullable: false),
                    Pattern = table.Column<string>(type: "TEXT", maxLength: 100, nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_UserNoTitleRules", x => x.Id);
                    table.ForeignKey(
                        name: "FK_UserNoTitleRules_Users_UserId",
                        column: x => x.UserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_UserNoTitleRules_UserId",
                table: "UserNoTitleRules",
                column: "UserId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "SharedRuleReports");

            migrationBuilder.DropTable(
                name: "UserNoTitleRules");

            migrationBuilder.DropColumn(
                name: "PersonalSync",
                table: "Users");
        }
    }
}
