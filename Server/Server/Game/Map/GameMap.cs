using Server.Core.Session;
using Server.Data.Models;
using Server.Game.Monsters;
using System;
using System.Collections.Generic;
using System.Linq;

namespace Server.Game.Map
{
	/// <summary>
	/// 공간 분할(Spatial Partitioning)을 통해 엔티티를 셀 단위로 관리하는 클래스입니다.
	///
	/// 구조: 단일 맵, 다중 리스트
	/// - 좌표계는 하나의 GameMap으로 통합 관리
	/// - 각 격자(MapCell) 내부에서 타입별(Player, Monster 등) 분리 저장
	/// 
	/// 장점:
	/// - 좌표 변환 로직 중복 제거
	/// - 메모리 효율(단일 2D 배열)
	/// - 타입별 검색 시 캐스팅 불필요
	/// </summary>
	public class GameMap
	{
		/// <summary>맵의 크기, 셀 크기 등 지형 정보 참조</summary>
		private readonly MapData _mapData;

		/// <summary>
		/// 2D 배열의 각 셀에 존재하는 엔티티 집합
		/// </summary>
		private readonly MapCell[,] _cells;
		/// <summary> 플레이어 → 현재 소속 셀 매핑 (역참조용)</summary>
		private readonly Dictionary<IClientSession, (int x, int z)> _playerPositions;
		/// <summary> 몬스터 → 현재 소속 셀 매핑 (역참조용)</summary>
		private readonly Dictionary<Monster, (int x, int z)> _monsterPositions;

		public int Width => _mapData.Width;
		public int Depth => _mapData.Depth;
		public float CellSize => _mapData.CellSize;
		public MapData MapData => _mapData;

		public int MapId => _mapData.Id;

		/// <summary>
		/// GridMap 생성자
		/// </summary>
		public GameMap(MapData mapData)
		{
			_mapData = mapData;
			_cells = new MapCell[mapData.Width, mapData.Depth];
			_playerPositions = new Dictionary<IClientSession, (int x, int z)>();
			_monsterPositions = new Dictionary<Monster, (int x, int z)>();

			// 각 셀 초기화
			for (int x = 0; x < mapData.Width; x++)
			{
				for (int z = 0; z < mapData.Depth; z++)
				{
					_cells[x, z] = new MapCell();
				}
			}
		}

		#region 좌표 변환

		///<summary> 월드 좌표를 셀 좌표로 변환 </summary>
		public (int x, int z) WorldToCell(float worldX, float worldZ)
		{
			return _mapData.WorldToCell(worldX, worldZ);
		}

		///<summary> 셀 좌표를 월드 좌표로 변환 </summary>
		public (float x, float z) CellToWorld(int cellX, int cellZ)
		{
			return _mapData.CellToWorld(cellX, cellZ);
		}

		/// <summary> 셀 좌표가 유효한 범위인지 확인 </summary>
		public bool IsValidCell(int x, int z)
		{
			return 0 <= x && x < _mapData.Width && 0 <= z && z < _mapData.Depth;
		}

		/// <summary> 해당 셀이 이동 가능한지 확인 </summary>
		public bool IsWalkable(int x, int z)
		{
			return _mapData.IsWalkable(x, z);
		}

		public bool IsWalkableWorld(float x, float z)
		{
			return _mapData.IsWalkableWorld(x, z);
		}

		#endregion

		#region 셀 접근

		/// <summary> 지정한 셀의 MapCell 객체를 반환 </summary>
		public MapCell GetCell(int x, int z)
		{
			if(!IsValidCell( x, z ))
				return null;

			return _cells[x, z];
		}

		/// <summary> 지정한 월드 좌표가 속한 셀의 MapCell 객체를 반환 </summary>
		public MapCell GetCellAt(float worldX, float worldZ)
		{
			var (cellX, cellZ) = _mapData.WorldToCell( worldX, worldZ );
			return GetCell( cellX, cellZ );
		}

		#endregion

		#region 플레이어 관리

		/// <summary> 플레이어를 지정한 셀에 추가 </summary>
		public void AddPlayer(IClientSession player, float worldX, float worldZ)
		{
			if(player == null)
				return;

			var (cellX, cellZ) = WorldToCell( worldX, worldZ );
			if(!IsValidCell( cellX, cellZ ))
				return;

			if(_playerPositions.ContainsKey( player ))
			{
				// 이미 등록된 플레이어인 경우 기존 데이터 무시 후 재등록
				RemovePlayer( player );
			}

			_cells[ cellX, cellZ ].Players.Add( player );
			_playerPositions[ player ] = ( cellX, cellZ );
		}

		/// <summary> 플레이어를 현재 셀에서 제거 </summary>
		public void RemovePlayer(IClientSession player )
		{
			if(player == null)
				return;

			if(_playerPositions.TryGetValue( player, out var cellPos ))
			{
				_cells[ cellPos.x, cellPos.z ].Players.Remove( player );
				_playerPositions.Remove( player );
			}
		}

		/// <summary> 플레이어의 위치를 업데이트 (셀 이동 시) </summary>
		public void UpdatePlayer(IClientSession player, float worldX, float worldZ)
		{
			if(player == null)
				return;

			var (newCellX, newCellZ) = WorldToCell( worldX, worldZ );
			if(!IsValidCell( newCellX, newCellZ ))
				return;

			if(_playerPositions.TryGetValue( player, out var oldCellPos ))
			{
				//Console.WriteLine( $"before [UpdatePlayer] oldCell({oldCellPos.x},{oldCellPos.z}) players:{string.Join( ", ", _cells[ oldCellPos.x, oldCellPos.z ].Players.Select( p => $"ID:{p.PlayerId}" ) )}");
				// 셀 변경 시에만 이동 처리
				if(oldCellPos.x != newCellX || oldCellPos.z != newCellZ)
				{
					_cells[ oldCellPos.x, oldCellPos.z ].Players.Remove( player );
					_cells[ newCellX, newCellZ ].Players.Add( player );
					_playerPositions[ player ] = ( newCellX, newCellZ );
				}

				//Console.WriteLine( $"after [UpdatePlayer] oldCell({oldCellPos.x},{oldCellPos.z}) players:{string.Join( ", ", _cells[ oldCellPos.x, oldCellPos.z ].Players.Select( p => $"ID:{p.PlayerId}" ) )}" );
				//Console.WriteLine( $"after [UpdatePlayer] NewCell({newCellX},{newCellZ}) players:{string.Join( ", ", _cells[ newCellX, newCellZ ].Players.Select( p => $"ID:{p.PlayerId}" ) )}" );
			}
		}

		#endregion

		#region 몬스터 관리

		/// <summary> 몬스터를 지정한 셀에 추가 </summary>
		public void AddMonster( Monster monster, float worldX, float worldZ )
		{
			if(monster == null)
				return;

			var (cellX, cellZ) = WorldToCell( worldX, worldZ );
			if(!IsValidCell( cellX, cellZ ))
				return;

			_cells[ cellX, cellZ ].Monsters.Add( monster );
			_monsterPositions[ monster ] = (cellX, cellZ);
		}

		/// <summary> 몬스터를 현재 셀에서 제거 </summary>
		public void RemoveMonster( Monster monster )
		{
			if(monster == null)
				return;

			if(_monsterPositions.TryGetValue( monster, out var cellPos ))
			{
				_cells[ cellPos.x, cellPos.z ].Monsters.Remove( monster );
				_monsterPositions.Remove( monster );
			}
		}

		/// <summary> 몬스터의 위치를 업데이트 (셀 이동 시) </summary>
		public void UpdateMonsters( Monster monster, float worldX, float worldZ )
		{
			if(monster == null)
				return;

			var (newCellX, newCellZ) = WorldToCell( worldX, worldZ );
			if(!IsValidCell( newCellX, newCellZ ))
				return;

			if(_monsterPositions.TryGetValue( monster, out var oldCellPos ))
			{
				// 셀 변경 시에만 이동 처리
				if(oldCellPos.x != newCellX || oldCellPos.z != newCellZ)
				{
					_cells[ oldCellPos.x, oldCellPos.z ].Monsters.Remove( monster );
					_cells[ newCellX, newCellZ ].Monsters.Add( monster );
					_monsterPositions[ monster ] = (newCellX, newCellZ);
				}
			}
		}

		#endregion

		#region 범위 검색

		/// <summary> 주변 셀에 있는 모든 플레이어를 반환합니다. </summary>
		public IEnumerable<IClientSession> GetNearByPlayers(float worldX, float worldZ, int radius)
		{
			var (centerX, centerZ) = _mapData.WorldToCell(worldX, worldZ);
			var result = new List<IClientSession>();

			for(int x = centerX - radius; x <= centerX + radius; x++)
			{
				for(int z = centerZ - radius; z <= centerZ + radius; z++)
				{
					// 맵 경계 체크
					if(!IsValidCell( x, z ))
						continue;

					result.AddRange( _cells[ x, z ].Players );
				}
			}

			return result;
		}

		/// <summary> 주변 셀에 있는 모든 몬스터를 반환합니다. </summary>
		public IEnumerable<Monster> GetNearByMonster( float worldX, float worldZ, int radius )
		{
			var (centerX, centerZ) = _mapData.WorldToCell( worldX, worldZ );
			var result = new List<Monster>();

			for(int x = centerX - radius; x <= centerX + radius; x++)
			{
				for(int z = centerZ - radius; z <= centerZ + radius; z++)
				{
					// 맵 경계 체크
					if(!IsValidCell( x, z ))
						continue;

					result.AddRange( _cells[ x, z ].Monsters );
				}
			}

			return result;
		}

		#endregion
	}
}
