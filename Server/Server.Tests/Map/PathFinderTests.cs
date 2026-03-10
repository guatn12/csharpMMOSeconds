using Server.Data.Models;
using Server.Game.Map;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Server.Tests.Map
{
	public class PathFinderTests
	{

		/// <summary>
		/// 테스트용 mapData 생성 헬퍼 메서드
		/// </summary>
		private static MapData CreateTestMap(int width, int depth, string cells, List<float> heights = null)
		{
			var map = new MapData
			{
				Width = width,
				Depth = depth,
				CellSize = 1.0f,
				Cells = cells,
				Heights = heights
			};
			map.ParseCells();
			return map;
		}

		/// <summary>
		/// 장애물 없는 5X5 맵에서 (0,0)에서 (4,4)까지 경로를 찾는 테스트
		/// </summary>
		[Fact]
		public void FIndPath_NoObstacle_ReturnsPath()
		{
			var map = CreateTestMap(5, 5, new string('0', 25)); // 5x5 맵, 모든 셀이 통과 가능

			var path = PathFinder.FindPath(map, 0, 0, 4, 4);

			Assert.NotEmpty( path );
			Assert.Equal( (4, 4), path[ path.Count - 1 ] );
		}

		/// <summary>
		/// 벽 우회 테스트: 5X5 맵에서 (0,0)에서 (4,2)까지 경로를 찾는데, 중간에 벽이 있어 우회해야 하는 상황
		/// </summary>
		[Fact]
		public void FindPath_WithWall_ReturnDetourPath()
		{
			string cells = 
				"00000" +
				"11110" +
				"00000" +
				"01111" +
				"00000"; // 5x5 맵, '1'은 벽
			var map = CreateTestMap(5, 5, cells);

			var path = PathFinder.FindPath(map, 0, 0, 4, 2);

			Assert.NotEmpty( path );
			Assert.Equal( (4, 2), path[ path.Count - 1 ] );
			foreach(var (x,z) in path)
			{
				Assert.True( map.IsWalkable( x, z ), $"경로 셀({x}, {z})가 Wall이면 안됨" );
			}
		}

		/// <summary>
		/// 완전 차단 테스트: 3X3 맵에서 (0,0)에서 (2,2)까지 경로를 찾는데, 모든 경로가 벽으로 막혀 있는 상황
		/// </summary>
		[Fact]
		public void FindPath_CompletelyBlocked_ReturnsEmpty()
		{
			string cells = "010" + "101" + "010"; // 3x3 맵
			var map = CreateTestMap(3, 3, cells);

			var path = PathFinder.FindPath(map, 0, 0, 2, 2);

			Assert.Empty( path );
		}

		/// <summary>
		/// 시작점 == 목표점 테스트: 5X5 맵에서 (2,2)에서 (2,2)까지 경로를 찾는 테스트. 이 경우 경로는 빈 리스트여야 함
		/// </summary>
		[Fact]
		public void FindPath_StartEqualsGoal_ReturnsEmpty()
		{
			var map = CreateTestMap(5,5, new string('0', 25));

			var path = PathFinder.FindPath(map, 2, 2, 2, 2);

			Assert.Empty( path );
		}

		/// <summary>
		/// Best-Effort 테스트: 5X5 맵에서 (0,0)에서 (4,1)까지 경로를 찾는데, 
		/// 목표점이 벽으로 막혀 있는 상황. 이 경우 도달 가능한 최종점까지의 경로가 반환되어야 함
		/// </summary>
		[Fact]
		public void FindPath_GoalIsWall_ReturnsBestEffortPath()
		{
			string cells = "00000" + "00011" + "00000" + "00000" + "00000"; // 5x5 맵, (3,1)과 (4,1)가 벽
			var map = CreateTestMap(5, 5, cells);

			var path = PathFinder.FindPath(map, 0, 0, 4, 1);

			Assert.NotEmpty( path );
			var last = path[ path.Count - 1 ];
			Assert.True( map.IsWalkable( last.x, last.z ), "Best-Effort 끝점은 Walkable이어야 함" );
			Assert.NotEqual( (4, 1), last ); // 목표점이 벽이므로 도달할 수 없어야 함
		}

		/// <summary>
		/// Corner Cutting 방지 테스트: 3X3 맵에서 (0,0)에서 (2,2)까지 경로를 찾는데, 
		/// 대각선으로 바로 가는 길이 벽으로 막혀 있고, 우회 경로가 존재하는 상황. 
		/// 이 경우 대각선 직통 경로가 아닌 우회 경로가 반환되어야 함
		/// </summary>
		[Fact]
		public void FindPath_CornerCutting_Prevented()
		{
			string cells = "010" + "000" + "000";
			var map = CreateTestMap(3, 3, cells);

			var path = PathFinder.FindPath(map, 0, 0, 2, 2);

			Assert.NotEmpty( path );
			Assert.Equal( (2, 2), path[ path.Count - 1 ] );
			// 대각선 직통(2칸)보다 우회 경로가 더 길어야 함
			Assert.True( path.Count > 2, $"Corner Cutting 방지 우회로 경로가 2칸 초과여야 함(실제: {path.Count}칸" );

			// 경로 전체 Walable
			foreach(var (x, z ) in path)
				Assert.True(map.IsWalkable(x,z), $"경로 셀({x}, {z})가 Wall이면 안됨" );
		}

		/// <summary>
		/// 높이 차이로 인한 경로 차단 테스트: 3X1 맵에서 (0,0)에서 (2,0)까지 경로를 찾는데,
		/// 중간 셀의 높이가 너무 높아서 경로가 차단되는 상황. 이 경우 빈 경로가 반환되어야 함
		/// </summary>
		[Fact]
		public void FindPath_HeightDifference_BlocksPath()
		{
			string cells = "000";
			var heights = new List<float> {0.0f, 5.0f, 0.0f};
			var map = CreateTestMap(3, 1, cells, heights);

			var path = PathFinder.FindPath(map, 0, 0, 2, 0);

			// |5.0 - 0.0| = 5.0 > MAX_STEP_HEIGHT(1.0) 이므로 경로가 차단되어야 함
			Assert.Empty( path );
		}

	}
}
