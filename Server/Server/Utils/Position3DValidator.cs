using Protocol;
using Server.Game.Map;
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
			if(position == null || room == null)
				return false;

			var cellPos = room.RoomMap.WorldToCell( position.PosX, position.PosZ );

			return	position.PosX >= room.MinX && position.PosX <= room.MaxX &&
					position.PosY >= room.MinY && position.PosY <= room.MaxY &&
					position.PosZ >= room.MinZ && position.PosZ <= room.MaxZ &&
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

			return new PosInfo
			{
				PosX = Math.Max( room.MinX, Math.Min( room.MaxX, position.PosX ) ),
				PosY = Math.Max( room.MinY, Math.Min( room.MaxY, position.PosY ) ),
				PosZ = Math.Max( room.MinZ, Math.Min( room.MaxZ, position.PosZ ) ),
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

			return new PosInfo
			{
				PosX = room.MinX + (room.RoomWidth / 2),
				PosY = room.MinY + (room.RoomHeight / 2),
				PosZ = room.MinZ + (room.RoomDepth / 2),
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

			// 룸 중앙에서 약간의 랜덤 오프셋
			float centerX = room.MinX + (room.RoomWidth / 2);
			float centerY = room.MinY + (room.RoomHeight / 2);
			float centerZ = room.MinZ + (room.RoomDepth / 2);

			float offsetRange = Math.Min(room.RoomWidth, room.RoomDepth) * 0.1f; // 룸 크기의 10% 범위

			return new PosInfo
			{
				PosX = centerX + (float)(rnd.NextDouble() - 0.5) * offsetRange,
				PosY = centerY,
				PosZ = centerZ + (float)(rnd.NextDouble() - 0.5) * offsetRange,
				RotationX = 0,
				RotationY = (float)(rnd.NextDouble() * 360), // 랜덤 방향
				RotationZ = 0,
				Timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()
			};
		}
	}
}
