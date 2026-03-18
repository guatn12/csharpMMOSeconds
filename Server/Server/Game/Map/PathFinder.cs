using Server.Data.Models;
using System;
using System.Collections.Generic;

namespace Server.Game.Map
{
	public class PathFinder
	{
		private struct Node : IComparable<Node>
		{
			public int x;
			public int z;
			public float G;     // 시작 노드에서 현재 노드까지의 비용
			public float F;     // G + H (총 추정 비용)

			public int CompareTo( Node other )
			{
				return F.CompareTo( other.F );
			}
		}

		private const float STRAIGHT_COST = 1.0f;               // 직선 이동 비용
		private const float DIAGONAL_COST = 1.414f;             // 대각선 이동 비용 (√2)	
		private const float SQRT2_MINUS_1 = 0.414f;             // 대각선 비용에서 직선 비용을 뺀 값 (1.414 - 1)
		private const int MAX_SEARCH_NODES = 1000;				// 최대 탐색 노드 수 (성능 보호)
		private const float MAX_STEP_HEIGHT = 1.0f;             // 최대 허용 높이 차이 (점프/낙하 제한)

		private static readonly (int dx, int dz, float cost)[] Directions = new[]
		{
			// 직선 (비용 1.0)
			( 0, 1, STRAIGHT_COST),		// 북
			( 0, -1, STRAIGHT_COST),	// 남
			( 1, 0, STRAIGHT_COST),		// 동
			( -1, 0, STRAIGHT_COST),	// 서
			// 대각선 (비용 1.414)
			( 1, 1, DIAGONAL_COST),		// 북동
			( 1, -1, DIAGONAL_COST),	// 남동
			( -1, 1, DIAGONAL_COST),	// 북서
			( -1, -1, DIAGONAL_COST)	// 남서
		};

		/// <summary>
		/// Octile 휴리스틱 계산 - 8방향 이동
		/// </summary>
		/// <param name="x1"></param>
		/// <param name="z1"></param>
		/// <param name="x2"></param>
		/// <param name="z2"></param>
		/// <returns></returns>
		public static float Heuristic(int x1, int z1, int x2, int z2)
		{
			int dx = Math.Abs(x1 - x2);
			int dz = Math.Abs(z1 - z2);
			return Math.Max( dx, dz ) + SQRT2_MINUS_1 * Math.Min( dx, dz );
		}

		/// <summary>
		/// A* 알고리즘으로 시작점에서 목표점까지의 최단 경로를 탐색한다.
		/// 경로를 찾지 못한 경우 목적지에 가장 가까운 위치까지의 경로를 반환 한다.
		/// </summary>
		/// <param name="mapData">지형 데이터</param>
		/// <param name="startX">시작 셀 X</param>
		/// <param name="startZ">시작 셀 Z</param>
		/// <param name="goalX">목표 셀 X</param>
		/// <param name="goalZ">목표 셀 Z</param>
		/// <returns></returns>
		public static List<(int x, int z)> FindPath(MapData mapData, int startX, int startZ, int goalX, int goalZ)
		{
			// A* 알고리즘 구현

			// 예외처리 - 이미 목표 위치에 있는 경우
			if(startX == goalX && startZ == goalZ) 
				return new List<(int x, int z)>();

			// 초기화
			// 탐색할 노드들 (우선순위 큐)
			PriorityQueue<Node, float> openSet = new PriorityQueue<Node, float>();
			// 시작 노드에서 각 노드까지의 실제 비용
			Dictionary<(int,int), float> gScore = new Dictionary<(int,int), float>();
			// 경로 재구성용 부모 노드 맵
			Dictionary<(int, int), (int, int)> cameFrom = new Dictionary<(int, int), (int, int)>();
			// 이미 탐색한 노드들
			HashSet<(int, int)> closedSet = new HashSet<(int, int)>();
			// 최적 경로를 찾지 못했을 때 가장 목표에 가까운 노드
			(int, int) bestNode = (startX, startZ);
			// 시작 노드의 휴리스틱 계산
			float bestH = Heuristic(startX, startZ, goalX, goalZ);

			// 시작 노드 초기화 및 오픈 세트에 추가
			Node startNode = new Node
			{
				x = startX,
				z = startZ,
				G = 0,
				F = 0 + bestH
			};
			openSet.Enqueue( startNode, startNode.F );
			gScore[ (startX, startZ) ] = 0;

			// A* 탐색 루프
			while( openSet.Count > 0 )
			{
				Node current = openSet.Dequeue();
				// 현재 노드가 목표 지점인지 체크
				if(current.x == goalX && current.z == goalZ)
					return ReconstructPath( cameFrom, (goalX, goalZ) );

				// 탐색 완료 노드로 추가
				closedSet.Add( (current.x, current.z) );
				// 현재 노드가 목표에 더 가까운지 체크
				float currentH = Heuristic(current.x, current.z, goalX, goalZ);
				// 최적 경로를 찾지 못했을 때를 대비해 가장 목표에 가까운 노드를 기록
				if(currentH < bestH)
				{
					bestH = currentH;
					bestNode = (current.x, current.z);
				}

				// 탐색 노드 수 제한 체크
				if(MAX_SEARCH_NODES < closedSet.Count)
					break;

				// 인접 8방향 노드 탐색
				foreach(var (dx, dz, cost) in Directions)
				{
					// 다음 노드 좌표 계산
					int nx = current.x + dx;
					int nz = current.z + dz;
					// 이동 가능한 Cell / Node 체크
					if(!mapData.IsWalkable( nx, nz ))
						continue;

					// 이미 탐색한 노드인지 체크
					if(closedSet.Contains( (nx, nz) ))
						continue;

					// corner cutting 방지 (대각선 이동 시 양쪽 직선이 모두 통과 가능한지 체크)
					if(cost == DIAGONAL_COST)
					{
						if(!mapData.IsWalkable( nx, current.z ) || !mapData.IsWalkable( current.x, nz ))
							continue;
					}

					// 높이 차이 체크 (점프/낙하 제한)
					if(MAX_STEP_HEIGHT < Math.Abs( mapData.GetHeightAt( nx, nz ) - mapData.GetHeightAt( current.x, current.z ) )) 
						continue;

					// gScore 계산 및 업데이트
					float tentativeG = gScore[ (current.x, current.z) ] + cost;
					if(tentativeG < gScore.GetValueOrDefault( (nx, nz), float.MaxValue ))
					{
						cameFrom[ (nx, nz) ] = (current.x, current.z);
						gScore[ (nx, nz) ] = tentativeG;
						float tentativeF = tentativeG + Heuristic( nx, nz, goalX, goalZ );
						openSet.Enqueue( new Node { x = nx, z = nz, F = tentativeF, G = tentativeG }, tentativeF );
					}
				}
			}

			if(bestNode != (startX, startZ))
				return ReconstructPath( cameFrom, bestNode );

			return new List<(int x, int z)>(); // 경로를 찾지 못한 경우 빈 리스트 반환
		}

		/// <summary>
		/// Bresenham 직선 체크 - 두 셀 사이 직선 경로에 장애물이 없는지 확인
		/// </summary>
		public static bool HasClearLine(MapData mapData, int startX, int startZ, int goalX, int goalZ )
		{
			int x = startX, z = startZ;
			int dx = Math.Abs(goalX - startX);
			int dz = Math.Abs(goalZ - startZ);
			int sx = startX < goalX ? 1 : -1;
			int sz = startZ < goalZ ? 1 : -1;
			int err = dx - dz;

			while(true)
			{
				if(!mapData.IsWalkable( x, z ))
					return false;

				if(x == goalX && z == goalZ)
					return true;

				int e2 = err * 2;

				// 대각선 이동 시 corner cutting 방지
				if(-dz < e2 && e2 < dx)
				{
					if(!mapData.IsWalkable( x + sx, z ) || !mapData.IsWalkable( x, z+sz ))
						return false;
				}

				// 높이 차이 체크
				int nextX = x, nextZ = z;
				if(-dz < e2) { err -= dz; nextX = x + sx; }
				if(e2 < dx) { err += dx; nextZ = z+sz; }

				if(MAX_STEP_HEIGHT < Math.Abs( mapData.GetHeightAt( nextX, nextZ ) - mapData.GetHeightAt( x, z ) ))
					return false;

				x = nextX;
				z = nextZ;
			}
		}

		/// <summary> cameFrom 맵을 역추적하여 경로 리스트 생성 (시작점 제외) </summary>
		private static List<(int x, int z)> ReconstructPath(Dictionary<(int, int), (int, int)> cameFrom,
			(int x, int z) current)
		{
			var path = new List<(int x, int z)>();
			while(cameFrom.ContainsKey(current))
			{
				path.Add( current );
				current = cameFrom[ current ];
			}
			path.Reverse();
			return path;
		}

	}
}
