using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Server.Migrations
{
	/// <inheritdoc />
	public partial class JsonbPerformanceOptimization : Migration
	{
		/// <inheritdoc />
		protected override void Up( MigrationBuilder migrationBuilder )
		{
			// 자주 검색되는 JSONB 필드에 대한 성능 인덱스
			migrationBuilder.Sql( @"
				CREATE INDEX IF NOT EXISTS idx_inventory_gold_range
				ON inventory USING BTREE (((inventory_data ->> 'Gold')::bigint));
			" );

			migrationBuilder.Sql( @"
				CREATE INDEX IF NOT EXISTS idx_player_settings_level_search
				ON players USING GIN ((player_settings -> 'Statistics'));
			" );

			migrationBuilder.Sql( @"
				CREATE INDEX IF NOT EXISTS idx_player_level_updated
				ON players (level DESC, updated_at DESC);
			" );

			migrationBuilder.Sql( @"
				CREATE INDEX IF NOT EXISTS idx_inventory_player_updated
				ON inventory (player_id, last_updated DESC);" );

			migrationBuilder.Sql( @"
				CREATE INDEX IF NOT EXISTS idx_activity_player_type_date
				ON game_activity (player_id, activity_type, created_at DESC);" );
		}

		/// <inheritdoc />
		protected override void Down( MigrationBuilder migrationBuilder )
		{
			migrationBuilder.Sql( "DROP INDEX IF EXISTS idx_inventory_gold_range;" );
			migrationBuilder.Sql( "DROP INDEX IF EXISTS idx_player_settings_level_search;" );
			migrationBuilder.Sql( "DROP INDEX IF EXISTS idx_player_level_updated;" );
		}
	}
}
