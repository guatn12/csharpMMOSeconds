using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Server.Game.Map
{
	/// <summary>
	/// 맵의 지형 데이터를 관리하는 클래스입니다.
	/// 2D Grid 기반으로 X-Z 평면을 격자로 나누어 지형 정보를 저장합니다.
	///
	/// 사용 목적:
	/// 1. 충돌 판정 - 플레이어/몬스터가 벽을 통과하지 못하도록 검사
	/// 2. 좌표 변환 - 월드 좌표(float) ↔ 셀 좌표(int) 변환
	/// 3. 경로 탐색 - 향후 A* 등 길찾기 알고리즘의 기반 데이터
	/// </summary>
	public class MapData
	{
		/// <summary>맵의 가로 셀 개수 (X축 방향)</summary>
		public int Width { get; init; }

		/// <summary>맵의 세로 셀 개수 (Z축 방향, 3D에서 깊이)</summary>
		public int Height { get; init; }

		/// <summary>각 셀의 지형 타입을 저장하는 2D 배열 [x, z]</summary>
		public CellType[,] Cells { get; init; }

		/// <summary>
		/// 하나의 셀이 실제 월드에서 차지하는 크기 (단위: 미터)
		/// 예: CellSize=1.0f면 1셀 = 1m x 1m
		/// </summary>
		public float CellSize { get; init; } = 1.0f;

		public MapData(int width, int height)
		{
			Width = width;
			Height = height;
			Cells = new CellType[width, height];
		}

		/// <summary>
		/// 지정한 셀 좌표가 이동 가능한지 확인합니다.
		///
		/// 사용 시점: 플레이어/몬스터 이동 요청 시 목적지 검증
		///
		/// 처리 과정:
		/// 1. 맵 경계 검사 - 좌표가 맵 범위 밖이면 이동 불가
		/// 2. 지형 검사 - 해당 셀이 Walkable 타입인지 확인
		/// </summary>
		/// <param name="x">셀의 X 좌표</param>
		/// <param name="z">셀의 Z 좌표</param>
		/// <returns>이동 가능하면 true, 불가능하면 false</returns>
		public bool IsWalkable(int x, int z)
		{
			// 맵 경계 검사: 음수이거나 맵 크기를 벗어나면 이동 불가
			if(x < 0 || x >= Width || z < 0 || z >= Height)
				return false;

			// 지형 검사: Walkable 타입만 이동 가능
			return Cells[x, z] == CellType.Walkable;
		}

		/// <summary>
		/// 월드 좌표(float)를 셀 좌표(int)로 변환합니다.
		///
		/// 사용 시점:
		/// - 엔티티의 현재 위치로 소속 셀 계산
		/// - 이동 목적지의 셀 좌표 계산
		///
		/// 처리 과정:
		/// 월드 좌표를 CellSize로 나누어 정수 인덱스로 변환
		/// 예: worldX=2.5, CellSize=1.0 → cellX=2
		/// </summary>
		/// <param name="worldX">월드 X 좌표</param>
		/// <param name="worldZ">월드 Z 좌표</param>
		/// <returns>해당하는 셀의 (x, z) 인덱스</returns>
		public (int x, int z) WorldToCell(float worldX, float worldZ)
		{
			int cellX = (int)(worldX / CellSize);
			int cellZ = (int)(worldZ / CellSize);
			return (cellX, cellZ);
		}

		/// <summary>
		/// 셀 좌표(int)를 월드 좌표(float)로 변환합니다. (셀의 중앙 위치 반환)
		///
		/// 사용 시점:
		/// - 몬스터 스폰 시 정확한 월드 좌표 계산
		/// - 위치 보정 시 셀 중앙으로 스냅
		///
		/// 처리 과정:
		/// 셀 인덱스에 0.5를 더해 중앙 좌표를 구한 뒤 CellSize를 곱함
		/// 예: cellX=2, CellSize=1.0 → worldX=2.5 (셀 중앙)
		/// </summary>
		/// <param name="cellX">셀의 X 인덱스</param>
		/// <param name="cellZ">셀의 Z 인덱스</param>
		/// <returns>해당 셀 중앙의 월드 좌표 (x, z)</returns>
		public (float x, float z) CellToWorld(int cellX, int cellZ)
		{
			float worldX = (cellX + 0.5f) * CellSize;
			float worldZ = (cellZ + 0.5f) * CellSize;
			return (worldX, worldZ);
		}
	}
}
