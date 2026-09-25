using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace DatabaseLib.Migrations
{
    /// <inheritdoc />
    public partial class AddAccountAndPlayerEntryTimestamp : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_players_login_token",
                table: "players");

            migrationBuilder.DropColumn(
                name: "login_token",
                table: "players");

            migrationBuilder.RenameColumn(
                name: "last_login_at",
                table: "players",
                newName: "last_entered_game_at");

            migrationBuilder.AlterColumn<long>(
                name: "account_id",
                table: "players",
                type: "bigint",
                nullable: false,
                oldClrType: typeof(long),
                oldType: "bigint",
                oldDefaultValue: 0L);

            migrationBuilder.CreateTable(
                name: "accounts",
                columns: table => new
                {
                    account_id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    login_id = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    normalized_login_id = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    password_hash = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    last_login_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false, defaultValueSql: "NOW()"),
                    updated_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false, defaultValueSql: "NOW()")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_accounts", x => x.account_id);
                });

            migrationBuilder.CreateIndex(
                name: "idx_accounts_normalized_login_id",
                table: "accounts",
                column: "normalized_login_id",
                unique: true);

            migrationBuilder.AddForeignKey(
                name: "FK_players_accounts_account_id",
                table: "players",
                column: "account_id",
                principalTable: "accounts",
                principalColumn: "account_id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_players_accounts_account_id",
                table: "players");

            migrationBuilder.DropTable(
                name: "accounts");

            migrationBuilder.RenameColumn(
                name: "last_entered_game_at",
                table: "players",
                newName: "last_login_at");

            migrationBuilder.AlterColumn<long>(
                name: "account_id",
                table: "players",
                type: "bigint",
                nullable: false,
                defaultValue: 0L,
                oldClrType: typeof(long),
                oldType: "bigint");

            migrationBuilder.AddColumn<string>(
                name: "login_token",
                table: "players",
                type: "character varying(255)",
                maxLength: 255,
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "ix_players_login_token",
                table: "players",
                column: "login_token");
        }
    }
}
