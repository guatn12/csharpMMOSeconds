using BenchmarkDotNet.Attributes;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Server.Data.Models;
using Server.Game.Map;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Server.Benchmarks
{
	/// <summary>
	/// A* PathFinder 성능 벤치마크
	/// - 다양한 맵/시나리오별 FindPath 성능 측정
	/// - HasClearLine 성능 측정
	/// - 목표: 20*20 맵 기준 평균 < 1ms
	/// </summary>
	[MemoryDiagnoser]
	[SimpleJob(launchCount:1, warmupCount:3, iterationCount:10)]
	public class PathFInderBenchmark
	{
		// ============================= 맵 데이터 =============================
		private MapData _openFieldMap = null;   // 시나리오1: 20*20 오픈 필드
		private MapData _wallBypassMap = null;  // 시나리오2: 20*20 중앙 벽
		private MapData _dungeonMap = null;     // 시나리오3: 16*16 실제 던전
		private MapData _bestEffortMap = null;  // 시나리오4: 20*20 목표=wall

		[GlobalSetup]
		public void Setup()
		{
			_openFieldMap = CreateTestMap( 20, 20, new string( '0', 400 ) );

			_wallBypassMap = CreateWallBypassMap();

			_dungeonMap = CreateDungeonMap();

			_bestEffortMap = CreateBestEffortMap();
		}

		// ========================= FindPath 벤치마크 ==========================
		[Benchmark(Description = "FindPath: 20*20 오픈필드 (0,0 -> 19,19")]
		public List<(int x, int z)> FindPath_OpenField()
		{
			return PathFinder.FindPath( _openFieldMap, 0, 0, 19, 19 );
		}

		[Benchmark( Description = "FindPath: 20*20 벽 우회 (0,0 -> 19,19" )]
		public List<(int x, int z)> FindPath_WallBypass()
		{
			return PathFinder.FindPath( _wallBypassMap, 0, 0, 19, 19 );
		}

		[Benchmark( Description = "FindPath: 16*16 던전 복도 (3,3 -> 12,12" )]
		public List<(int x, int z)> FindPath_DungeonCorridor()
		{
			return PathFinder.FindPath( _dungeonMap, 3, 3, 12, 12 );
		}

		[Benchmark( Description = "FindPath: 20×20 Best-Effort 목표=Wall" )]
		public List<(int x, int z)> FindPath_BestEffort()
		{
			return PathFinder.FindPath( _bestEffortMap, 0, 0, 10, 10 );
		}

		[Benchmark( Description = "FindPath: 20×20 근거리 (0,0→3,3)" )]
		public List<(int x, int z)> FindPath_ShortRange()
		{
			return PathFinder.FindPath( _openFieldMap, 0, 0, 3, 3 );
		}

		// ======================== HasClearLine 벤치마크 =========================
		[Benchmark( Description = "HasClearLine: 20×20 장애물 없음 (0,0→19,19)" )]
		public bool HasClearLine_NoObstacle()
		{
			return PathFinder.HasClearLine( _openFieldMap, 0, 0, 19, 19 );
		}

		[Benchmark( Description = "HasClearLine: 20×20 장애물 있음 (0,0→19,19)" )]
		public bool HasClearLine_WithObstacle()
		{
			return PathFinder.HasClearLine( _wallBypassMap, 0, 0, 19, 19 );
		}

		[Benchmark( Description = "HasClearLine: 16×16 던전 (3,3→12,12)" )]
		public bool HasClearLine_Dungeon()
		{
			return PathFinder.HasClearLine( _dungeonMap, 3, 3, 12, 12 );
		}

		// ============================= 맵 생성 헬퍼 =============================
		private static MapData CreateTestMap(int width, int depth, string cells)
		{
			var map = new MapData
			{
				Id = 100,
				Name = "Benchmark",
				Width = width,
				Depth = depth,
				CellSize = 1.0f,
				Cells = cells
			};
			map.ParseCells();
			return map;
		}

		private static MapData CreateWallBypassMap()
		{
			char[] cells = new char[400];
			Array.Fill( cells, '0' );

			for(int x = 2; x <= 17; x++)
			{
				cells[ 10*20+x ] = '1';
			}

			return CreateTestMap( 20, 20, new string( cells ) );
		}

		private static MapData CreateDungeonMap()
		{
			string dungeonCells =
				"1111111111111111" +  // z=0
                "1000001111000001" +  // z=1
                "1000001111000001" +  // z=2
                "1000000000000001" +  // z=3  ← 방A(3,3) 시작점
                "1000001111000001" +  // z=4
                "1000001111000001" +  // z=5
                "1110111111110111" +  // z=6  ← 벽 (복도 구간)
                "1110111111110111" +  // z=7
                "1011111111011111" +  // z=8
                "1011111111011111" +  // z=9
                "1000001111000001" +  // z=10
                "1000001111000001" +  // z=11
                "1000000000000001" +  // z=12 ← 방D(12,12) 목표점
                "1000001111000001" +  // z=13
                "1000001111000001" +  // z=14
                "1111111111111111";   // z=15

			var map = new MapData
			{
				Id = 3,
				Name = "Dungeon Benchmark",
				Width = 16,
				Depth = 16,
				CellSize = 4.0f,
				Cells = dungeonCells,
				Heights = CreateDungeonHeights()
			};
			map.ParseCells();
			return map;
		}

		/// <summary> 던전 높이 데이터 (maps.json에서 발췌) </summary>
		private static List<float> CreateDungeonHeights()
		{
			var heights = new List<float>(256);
			float[] data = {
				0.0f,  0.0f,  0.0f,  0.0f,  0.0f,  0.0f,  0.0f,  0.0f,  0.0f,  0.0f,  0.0f,  0.0f,  0.0f,  0.0f,  0.0f,  0.0f,
				0.0f,  0.0f,  0.0f,  0.0f,  0.0f,  0.0f,  0.0f,  0.0f,  0.0f,  0.0f,  0.5f,  0.5f,  0.5f,  0.5f,  0.5f,  0.0f,
				0.0f,  0.0f,  0.0f,  0.0f,  0.0f,  0.0f,  0.0f,  0.0f,  0.0f,  0.0f,  0.5f,  0.5f,  0.5f,  0.5f,  0.5f,  0.0f,
				0.0f,  0.25f, 0.25f, 0.25f, 0.25f, 0.25f, 0.25f, 0.25f, 0.25f, 0.25f, 0.25f, 0.25f, 0.25f, 0.25f, 0.25f, 0.0f,
				0.0f,  0.0f,  0.0f,  0.0f,  0.0f,  0.0f,  0.0f,  0.0f,  0.0f,  0.0f,  0.5f,  0.5f,  0.5f,  0.5f,  0.5f,  0.0f,
				0.0f,  0.0f,  0.0f,  0.0f,  0.0f,  0.0f,  0.0f,  0.0f,  0.0f,  0.0f,  0.5f,  0.5f,  0.5f,  0.5f,  0.5f,  0.0f,
				0.0f,  0.0f,  0.0f, -0.25f, 0.0f,  0.0f,  0.0f,  0.0f,  0.0f,  0.0f,  0.0f,  0.0f, -0.25f, 0.0f,  0.0f,  0.0f,
				0.0f,  0.0f,  0.0f, -0.25f, 0.0f,  0.0f,  0.0f,  0.0f,  0.0f,  0.0f,  0.0f,  0.0f, -0.25f, 0.0f,  0.0f,  0.0f,
				0.0f,  0.0f,  0.0f, -0.25f, 0.0f,  0.0f,  0.0f,  0.0f,  0.0f,  0.0f,  0.0f,  0.0f, -0.25f, 0.0f,  0.0f,  0.0f,
				0.0f,  0.0f,  0.0f, -0.25f, 0.0f,  0.0f,  0.0f,  0.0f,  0.0f,  0.0f,  0.0f,  0.0f, -0.25f, 0.0f,  0.0f,  0.0f,
				0.0f, -0.5f, -0.5f, -0.5f, -0.5f, -0.5f,  0.0f,  0.0f,  0.0f,  0.0f, -1.0f, -1.0f, -1.0f, -1.0f, -1.0f,  0.0f,
				0.0f, -0.5f, -0.5f, -0.5f, -0.5f, -0.5f,  0.0f,  0.0f,  0.0f,  0.0f, -1.0f, -1.0f, -1.0f, -1.0f, -1.0f,  0.0f,
				0.0f, -0.75f,-0.75f,-0.75f,-0.75f,-0.75f,-0.75f,-0.75f,-0.75f,-0.75f,-0.75f,-0.75f,-0.75f,-0.75f,-0.75f,  0.0f,
				0.0f, -0.5f, -0.5f, -0.5f, -0.5f, -0.5f,  0.0f,  0.0f,  0.0f,  0.0f, -1.0f, -1.0f, -1.0f, -1.0f, -1.0f,  0.0f,
				0.0f, -0.5f, -0.5f, -0.5f, -0.5f, -0.5f,  0.0f,  0.0f,  0.0f,  0.0f, -1.0f, -1.0f, -1.0f, -1.0f, -1.0f,  0.0f,
				0.0f,  0.0f,  0.0f,  0.0f,  0.0f,  0.0f,  0.0f,  0.0f,  0.0f,  0.0f,  0.0f,  0.0f,  0.0f,  0.0f,  0.0f,  0.0f
			};
			heights.AddRange( data );
			return heights;
		}

		private static MapData CreateBestEffortMap()
		{
			char[] cells = new char[400];
			Array.Fill( cells, '0' );

			for(int z = 9; z <= 11; z++)
			{
				for(int x = 9; x <= 11; x++)
				{
					cells[ z*20+x ] = '1';
				}
			}

			return CreateTestMap( 20, 20, new string( cells ) );
		}
	}
}
