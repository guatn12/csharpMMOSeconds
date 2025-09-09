using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace Server.Migrations
{
    /// <inheritdoc />
    public partial class MmoEntities : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<long>(
                name: "experience",
                table: "players",
                type: "bigint",
                nullable: false,
                defaultValue: 0L);

            migrationBuilder.AddColumn<DateTime>(
                name: "last_login_at",
                table: "players",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "login_token",
                table: "players",
                type: "character varying(255)",
                maxLength: 255,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "player_settings",
                table: "players",
                type: "jsonb",
                nullable: true,
                defaultValue: "{}");

            migrationBuilder.AddColumn<long>(
                name: "total_play_time_minutes",
                table: "players",
                type: "bigint",
                nullable: false,
                defaultValue: 0L);

            migrationBuilder.AddColumn<DateTime>(
                name: "updated_at",
                table: "players",
                type: "timestamp with time zone",
                nullable: false,
                defaultValueSql: "NOW()");

            migrationBuilder.CreateTable(
                name: "game_activity",
                columns: table => new
                {
                    activity_id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    player_id = table.Column<long>(type: "bigint", nullable: false),
                    activity_type = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false, defaultValueSql: "NOW()"),
                    activity_data = table.Column<string>(type: "jsonb", nullable: true, defaultValue: "{}")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_game_activity", x => x.activity_id);
                    table.ForeignKey(
                        name: "FK_game_activity_players_player_id",
                        column: x => x.player_id,
                        principalTable: "players",
                        principalColumn: "player_id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "inventory",
                columns: table => new
                {
                    inventory_id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    player_id = table.Column<long>(type: "bigint", nullable: false),
                    max_slots = table.Column<int>(type: "integer", nullable: false, defaultValue: 50),
                    version = table.Column<int>(type: "integer", nullable: false, defaultValue: 1),
                    last_updated = table.Column<DateTime>(type: "timestamp with time zone", nullable: false, defaultValueSql: "NOW()"),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false, defaultValueSql: "NOW()"),
                    inventory_data = table.Column<string>(type: "jsonb", nullable: true, defaultValue: "{}")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_inventory", x => x.inventory_id);
                    table.ForeignKey(
                        name: "FK_inventory_players_player_id",
                        column: x => x.player_id,
                        principalTable: "players",
                        principalColumn: "player_id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_players_login_token",
                table: "players",
                column: "login_token");

            migrationBuilder.CreateIndex(
                name: "ix_players_settings_gin",
                table: "players",
                column: "player_settings")
                .Annotation("Npgsql:IndexMethod", "gin");

            migrationBuilder.CreateIndex(
                name: "ix_activity_data_gin",
                table: "game_activity",
                column: "activity_data")
                .Annotation("Npgsql:IndexMethod", "gin");

            migrationBuilder.CreateIndex(
                name: "ix_game_activity_player_data",
                table: "game_activity",
                columns: new[] { "player_id", "created_at" });

            migrationBuilder.CreateIndex(
                name: "ix_game_activity_player_id",
                table: "game_activity",
                column: "player_id");

            migrationBuilder.CreateIndex(
                name: "ix_game_activity_type_data",
                table: "game_activity",
                columns: new[] { "activity_type", "created_at" });

            migrationBuilder.CreateIndex(
                name: "ix_inventory_data_gin",
                table: "inventory",
                column: "inventory_data")
                .Annotation("Npgsql:IndexMethod", "gin");

            migrationBuilder.CreateIndex(
                name: "ix_inventory_last_updated",
                table: "inventory",
                column: "last_updated");

            migrationBuilder.CreateIndex(
                name: "ix_inventory_player_id",
                table: "inventory",
                column: "player_id",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "game_activity");

            migrationBuilder.DropTable(
                name: "inventory");

            migrationBuilder.DropIndex(
                name: "ix_players_login_token",
                table: "players");

            migrationBuilder.DropIndex(
                name: "ix_players_settings_gin",
                table: "players");

            migrationBuilder.DropColumn(
                name: "experience",
                table: "players");

            migrationBuilder.DropColumn(
                name: "last_login_at",
                table: "players");

            migrationBuilder.DropColumn(
                name: "login_token",
                table: "players");

            migrationBuilder.DropColumn(
                name: "player_settings",
                table: "players");

            migrationBuilder.DropColumn(
                name: "total_play_time_minutes",
                table: "players");

            migrationBuilder.DropColumn(
                name: "updated_at",
                table: "players");
        }
    }
}
