using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DeskPal.Server.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddStatsSync : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "StatsSyncEnabled",
                table: "Users",
                type: "INTEGER",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<string>(
                name: "StatsSyncKeyCheck",
                table: "Users",
                type: "TEXT",
                maxLength: 200,
                nullable: true);

            migrationBuilder.AddColumn<long>(
                name: "StatsSyncRevision",
                table: "Users",
                type: "INTEGER",
                nullable: false,
                defaultValue: 0L);

            migrationBuilder.AddColumn<string>(
                name: "StatsSyncSalt",
                table: "Users",
                type: "TEXT",
                maxLength: 200,
                nullable: true);

            migrationBuilder.CreateTable(
                name: "StatsSyncDays",
                columns: table => new
                {
                    UserId = table.Column<Guid>(type: "TEXT", nullable: false),
                    Date = table.Column<DateOnly>(type: "TEXT", nullable: false),
                    Revision = table.Column<long>(type: "INTEGER", nullable: false),
                    Data = table.Column<byte[]>(type: "BLOB", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_StatsSyncDays", x => new { x.UserId, x.Date });
                    table.ForeignKey(
                        name: "FK_StatsSyncDays_Users_UserId",
                        column: x => x.UserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "StatsSyncDays");

            migrationBuilder.DropColumn(
                name: "StatsSyncEnabled",
                table: "Users");

            migrationBuilder.DropColumn(
                name: "StatsSyncKeyCheck",
                table: "Users");

            migrationBuilder.DropColumn(
                name: "StatsSyncRevision",
                table: "Users");

            migrationBuilder.DropColumn(
                name: "StatsSyncSalt",
                table: "Users");
        }
    }
}
