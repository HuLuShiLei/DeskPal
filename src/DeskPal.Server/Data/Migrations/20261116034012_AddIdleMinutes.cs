using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DeskPal.Server.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddIdleMinutes : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "IdleMinutes",
                table: "Users",
                type: "INTEGER",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "IdleMinutes",
                table: "Users");
        }
    }
}
