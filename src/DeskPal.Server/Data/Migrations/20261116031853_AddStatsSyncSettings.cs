using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DeskPal.Server.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddStatsSyncSettings : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<byte[]>(
                name: "StatsSyncSettings",
                table: "Users",
                type: "BLOB",
                nullable: true);

            migrationBuilder.AddColumn<long>(
                name: "StatsSyncSettingsRevision",
                table: "Users",
                type: "INTEGER",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "StatsSyncSettings",
                table: "Users");

            migrationBuilder.DropColumn(
                name: "StatsSyncSettingsRevision",
                table: "Users");
        }
    }
}
