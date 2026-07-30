using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DatabaseLib.Migrations
{
    /// <inheritdoc />
    public partial class AddAccountId : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<long>(
                name: "account_id",
                table: "players",
                type: "bigint",
                nullable: false,
                defaultValue: 0L);

            migrationBuilder.CreateIndex(
                name: "ix_players_account_id",
                table: "players",
                column: "account_id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_players_account_id",
                table: "players");

            migrationBuilder.DropColumn(
                name: "account_id",
                table: "players");
        }
    }
}
