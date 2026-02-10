using Protocol;
using Server.Data.Models;
using Server.Game.Map;
using Server.Game.Monsters;
using Server.Room;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading.Tasks;

namespace Server.Utils
{
	public class Position3DValidator
	{
		/// <summary>
		/// 3D 위치가 룸 경계 내에 있는지 검증
		/// </summary>
		public static bool IsValidPosition(PosInfo position, BaseRoom room)
		{
			if(position == null || room.RoomMap.MapData == null)
				return false;

			var mapData = room.RoomMap.MapData;
			float maxX = mapData.Width * mapData.CellSize;
			float maxZ = mapData.Depth * mapData.CellSize;
			var cellPos = room.RoomMap.WorldToCell( position.PosX, position.PosZ );

			return	position.PosX >= 0 && position.PosX <= maxX &&
					position.PosY >= mapData.GroundY && position.PosY <= mapData.MaxHeight &&
					position.PosZ >= 0 && position.PosZ <= maxZ &&
					room.RoomMap.IsValidCell( cellPos.x, cellPos.z ) &&
					room.RoomMap.IsWalkable( cellPos.x, cellPos.z );
		}

		/// <summary>
		/// 위치를 룸 경계 내로 클램핑
		/// </summary>
		public static PosInfo ClampToRoomBounds(PosInfo position, BaseRoom room)
		{
			if(position== null || room == null)
				return position;

			var mapData = room.RoomMap.MapData;
			float maxX = mapData.Width * mapData.CellSize;
			float maxZ = mapData.Depth * mapData.CellSize;

			return new PosInfo
			{
				PosX = Math.Max( 0, Math.Min( maxX, position.PosX ) ),
				PosY = Math.Max( mapData.GroundY, Math.Min( mapData.MaxHeight, position.PosY ) ),
				PosZ = Math.Max( 0, Math.Min( maxZ, position.PosZ ) ),
				RotationX = position.RotationX,
				RotationY = position.RotationY,
				RotationZ = position.RotationZ,
				Timestamp = position.Timestamp,
			};
		}

		/// <summary>
		/// 3D 거리 계산
		/// </summary>
		public static float CalculateDistance3D(PosInfo pos1, PosInfo pos2)
		{
			if(pos1 == null || pos2 == null)
				return float.MaxValue;

			float deltaX = pos1.PosX - pos2.PosX;
			float deltaY = pos1.PosY - pos2.PosY;
			float deltaZ = pos1.PosZ - pos2.PosZ;

			return (float)Math.Sqrt(deltaX * deltaX + deltaY * deltaY + deltaZ * deltaZ );
		}

		/// <summary>
		/// 룸 중앙 위치 계산
		/// </summary>
		public static PosInfo GetRoomCenter(BaseRoom room)
		{
			if(room == null) return new PosInfo();

			var mapData = room.RoomMap.MapData;
			float maxX = mapData.Width * mapData.CellSize;
			float maxZ = mapData.Depth * mapData.CellSize;

			return new PosInfo
			{
				PosX = maxX / 2,
				PosY = mapData.GroundY,
				PosZ = maxZ / 2,
				RotationX = 0,
				RotationY = 0,
				RotationZ= 0,
				Timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()
			};
		}

		/// <summary>
		/// 이동 가능한 위치인지 검증 (기본 구현)
		/// </summary>
		public static bool IsValidMove(PosInfo fromPos, PosInfo toPos, BaseRoom room,
			float maxMoveDistance = 50.0f)
		{
			if(!IsValidPosition( toPos, room ))
				return false;

			// 너무 먼 거리 이동 방지
			float distance = CalculateDistance3D(fromPos, toPos);
			return distance <= maxMoveDistance;
		}

		/// <summary>
		/// 스폰 위치 계산 (룸 중앙 근처)
		/// </summary>
		public static PosInfo GetSpawnPosition(BaseRoom room, Random random = null)
		{
			if(room == null) return new PosInfo();

			Random rnd = random ?? new Random();

			var mapData = room.RoomMap.MapData;
			float maxX = mapData.Width * mapData.CellSize;
			float maxZ = mapData.Depth * mapData.CellSize;

			float offsetRange = Math.Min(maxX, maxZ) * 0.9f; // 룸 크기의 90% 범위

			float posX, posZ = 0.0f;
			for(int i = 0; i < 10; i++)
			{
				posX = (float)(rnd.NextDouble()) * offsetRange;
				posZ = (float)(rnd.NextDouble()) * offsetRange;
				if(mapData.IsWalkableWorld( posX, posZ ))
				{
					return new PosInfo
					{
						PosX = posX,
						PosY = mapData.GetWorldHeight(posX, posZ),
						PosZ = posZ,
						RotationX = 0,
						RotationY = (float)(rnd.NextDouble() * 360), // 랜덤 방향
						RotationZ = 0,
						Timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()
					};
				}
			}

			var centerCell = FindFirstWalkablePosition(room);
			float worldCenterX = centerCell.x + ((float)rnd.NextDouble() * (float)mapData.CellSize);
			float worldCenterZ = centerCell.z + ((float)rnd.NextDouble() * (float)mapData.CellSize);
			// 실패 시 그냥 중앙셀 내의 랜덤 반환
			return new PosInfo
			{
				PosX = worldCenterX,
				PosY = mapData.GetWorldHeight(worldCenterX, worldCenterZ),
				PosZ = worldCenterZ,
				RotationX = 0,
				RotationY = (float)(rnd.NextDouble() * 360), // 랜덤 방향
				RotationZ = 0,
				Timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()
			};
		}

		public static (int x, int z) FindFirstWalkablePosition( BaseRoom room )
		{
			var mapData = room.RoomMap.MapData;

			// 룸 중앙 셀에서 부터 이동 가능 지역 탐색.
			int walkableX, walkableZ = 0;
			// 중앙에서 부터 x를 1씩 증가
			for(int x = mapData.Width / 2; x < mapData.Width; x++)
			{
				walkableX = x;
				// 오른쪽
				for(int z = x; x - (x - mapData.Width / 2) * 2 <= z; z--)
				{
					walkableZ = z;
					if(mapData.IsWalkable( walkableX, walkableZ ))
					{
						return (walkableX, walkableZ);
					}
				}

				// 아래쪽
				for(int downX = x - 1; mapData.Width/2 - (x - mapData.Width/2) <= downX; downX--)
				{
					walkableX = downX;
					if(mapData.IsWalkable( walkableX, walkableZ ))
					{
						return (walkableX, walkableZ);
					}
				}

				//왼쪽
				for(int leftZ = walkableZ + 1; leftZ <= x; leftZ++)
				{
					walkableZ = leftZ;
					if(mapData.IsWalkable( walkableX, walkableZ ))
					{
						return (walkableX, walkableZ);
					}
				}

				// 위쪽
				for(int topX = walkableX + 1; topX < x; topX++)
				{
					walkableX = topX;
					if(mapData.IsWalkable( walkableX, walkableZ ))
					{
						return (walkableX, walkableZ);
					}
				}
			}

			var returnValue = GetRoomCenter(room);
			return mapData.WorldToCell( returnValue.PosX, returnValue.PosZ );
		}

		public PosInfo MoveTowards( IRoom room, PosInfo current, PosInfo target, double intervalSeconds, float speed )
		{
			if(target == null) return current;

			//방향 벡터 계산
			float dx = target.PosX - current.PosX;
			float dy = target.PosY - current.PosY;
			float dz = target.PosZ - current.PosZ;

			float distance = (float)Math.Sqrt(dx*dx+dy*dy+dz*dz);

			if(distance < 0.01f) return current;

			// 정규화된 방향 벡터
			float dirX = dx / distance;
			float dirY = dy / distance;
			float dirZ = dz / distance;

			// 이동 거리 (초당 speed 단위)
			float moveDistance = speed * (float)intervalSeconds;

			// 목표 위치보다 가까우면 목표 위치로 이동.
			if(distance <= moveDistance)
			{
				if(!room.RoomMap.IsWalkableWorld( target.PosX, target.PosZ ))
				{
					return current;
				}

				return target;
			}
			else
			{
				float posX = current.PosX + dirX * moveDistance;
				float posY = current.PosY + dirY * moveDistance;
				float posZ = current.PosZ + dirZ * moveDistance;

				if(!room.RoomMap.IsWalkableWorld( posX, posZ ))
				{
					return current;
				}

				PosInfo newPosition = new PosInfo
				{
					PosX = posX,
					PosY = posY,
					PosZ = posZ,
					RotationX = current.RotationX,
					RotationY = (float)Math.Atan2(dirX, dirZ) * (180f / (float)Math.PI), // Yaw 계산
					RotationZ = current.RotationZ
				};

				return newPosition;
			}
		}
	}
}
