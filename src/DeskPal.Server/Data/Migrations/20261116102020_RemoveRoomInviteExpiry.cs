using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DeskPal.Server.Data.Migrations
{
    /// <inheritdoc />
    public partial class RemoveRoomInviteExpiry : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Codes no longer expire. Drop the already expired ones so they do not start working again;
            // RoomService creates a new code for such rooms the next time a member opens it.
            migrationBuilder.Sql("""DELETE FROM "RoomInvites" WHERE "ExpiresAt" <= strftime('%Y-%m-%d %H:%M:%S', 'now');""");
            migrationBuilder.Sql("""DELETE FROM "RoomInvites" WHERE rowid NOT IN (SELECT min(rowid) FROM "RoomInvites" GROUP BY "RoomId");""");

            migrationBuilder.DropIndex(
                name: "IX_RoomInvites_RoomId",
                table: "RoomInvites");

            migrationBuilder.DropColumn(
                name: "ExpiresAt",
                table: "RoomInvites");

            migrationBuilder.CreateIndex(
                name: "IX_RoomInvites_RoomId",
                table: "RoomInvites",
                column: "RoomId",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_RoomInvites_RoomId",
                table: "RoomInvites");

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "ExpiresAt",
                table: "RoomInvites",
                type: "TEXT",
                nullable: false,
                defaultValue: new DateTimeOffset(new DateTime(1, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)));

            migrationBuilder.CreateIndex(
                name: "IX_RoomInvites_RoomId",
                table: "RoomInvites",
                column: "RoomId");
        }
    }
}
