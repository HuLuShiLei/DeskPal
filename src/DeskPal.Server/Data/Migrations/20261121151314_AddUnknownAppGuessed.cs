using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DeskPal.Server.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddUnknownAppGuessed : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "Guessed",
                table: "UnknownAppReports",
                type: "INTEGER",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "Guessed",
                table: "UnknownAppReports");
        }
    }
}
