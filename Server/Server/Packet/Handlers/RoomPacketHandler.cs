using Microsoft.Extensions.Logging;
using Protocol;
using Server.Core.Session;
using Server.Game.Map;
using Server.Room;
using Server.Services;
using Server.Utils;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace Server.Packet.Handlers
{
	/// <summary>
	/// 룸 관련 패킷 핸들러
	/// </summary>
	public partial class RoomPacketHandler
	{
		private readonly ILogger<RoomPacketHandler> _logger;
		private readonly BaseRoom _room;
		private readonly PlayerPositionService _playerPositionService;

		public RoomPacketHandler(ILogger<RoomPacketHandler> logger, BaseRoom room, PlayerPositionService playerPositionService )
		{
			_logger = logger;
			_room = room;
			_playerPositionService = playerPositionService;
			InitializeHandlers();
		}

		private async Task HandleC_MoveAsync( IClientSession session, C_Move packet)
		{
			// 1. 기본 검증
			var basicValidation = PacketValidators.ValidateBasic(session, _room);
			if(!basicValidation.IsValid)
			{
				_logger.LogWarning( "Move validation failed: {Error}", basicValidation.ErrorMessage );
				return;
			}

			if(!session.Player.IsAlive)
			{
				_logger.LogWarning( "Dead player {PlayerId} attempted to move", session.PlayerId );
				return;
			}

			// 2. 범위 검증
			var rangeValidation = PacketValidators.ValidateRange(packet.PosInfo, _room);

			if(!rangeValidation.IsValid)
			{
				_logger.LogWarning("Move range validation failed: {Error}", rangeValidation.ErrorMessage );

				// 현재 위치를 클라이언트에 재전송 (동기화)
				var currentPos = session.Player.PosInfo;

				if(currentPos != null)
				{
					var correctionPacket = new S_Move
					{
						Objects = { session.Player.ToObjectInfo() }
					};

					_room.SendToPlayer(session, correctionPacket);

					_logger.LogInformation( "Position corrected: Session={SessionId}, Pos=({X}, {Y}, {Z})",
						session.SessionId, currentPos.PosX, currentPos.PosY, currentPos.PosZ );
				}
				return;
			}

			// 3. Player 객체 위치 업데이트 (메모리 동기화)
			session.Player.UpdatePosition(packet.PosInfo);

			// GameMap 내 플레이어 위치 업데이트
			_room.RoomMap.Update( session.Player, packet.PosInfo.PosX, packet.PosInfo.PosZ );

			// 4. Redis 캐시 업데이트 (근처 플레이어 검색 최적화용)
			await _playerPositionService.UpdatePositionAsync(session.PlayerId, packet.PosInfo);

			// 5. 브로드캐스트 (본인 제외)
			var response = new S_Move
			{
				Objects = { session.Player.ToObjectInfo() }
			};
			_room.BroadcastInRange( response, session.Player.PosInfo, excludeSession: session );

			_logger.LogInformation("Player {PlayerId} moved to ({X}, {Y}, {Z})",
				session.PlayerId, packet.PosInfo.PosX, packet.PosInfo.PosY, packet.PosInfo.PosZ );
		}

		/// <summary>
		/// C_Chat 패킷 처리
		/// 채팅 메시지 브로드캐스트
		/// </summary>
		private async Task HandleC_ChatAsync( IClientSession session, C_Chat packet )
		{
			// 1. 기본 검증
			var validation = PacketValidators.ValidateBasic(session, _room);
			if(!validation.IsValid)
			{
				_logger.LogWarning( "Chat validation failed: {Error}", validation.ErrorMessage );
				return;
			}

			// 2. 메시지 검증
			if(string.IsNullOrWhiteSpace( packet.Message ))
			{
				_logger.LogWarning( "Empty chat message from Player {PlayerId}", session.PlayerId );
				return;
			}

			if(200 < packet.Message.Length)
			{
				_logger.LogWarning( "Chat message too long from Player {PlayerId}: {Length} chars",
					session.PlayerId, packet.Message.Length );
				return;
			}

			// 3. 브로드캐스트
			var response = new S_Chat
			{
				PlayerId = session.PlayerId,
				Message = packet.Message,
			};
			_room.Broadcast( response );

			_logger.LogInformation( "Player {PlayerId} chatted: {Message}", session.PlayerId, packet.Message );
		}

		/// <summary>
		/// C_PlayerInfo 패킷 처리
		/// 플레이어 스탯 정보 조회
		/// </summary>
		private async Task HandleC_PlayerInfoAsync( IClientSession session, C_PlayerInfo packet)
		{
			// 1. 기본 검증
			var validation = PacketValidators.ValidateBasic(session, _room);
			if(!validation.IsValid)
			{
				_logger.LogWarning( "PlayerInfo validation failed: {Error}", validation.ErrorMessage );
				return;
			}

			// 2. 응답 생성
			var response = new S_PlayerStat
			{
				Player = session.Player.ToObjectInfo(),
			};

			// 3. 요청자에게만 전송
			_room.SendToPlayer( session, response );

			_logger.LogDebug( "Player {PlayerId} requested player info", session.PlayerId );
		}

		private async ValueTask HandleC_AutoMoveAsync( IClientSession session, C_AutoMove packet )
		{
			var validation = PacketValidators.ValidateBasic(session, _room);
			if (!validation.IsValid)
			{
				_logger.LogWarning("AutoMove ValidateBasic Failed: {Error}", validation.ErrorMessage );
				return;
			}

			// 목적지가 이동할 수 없는 지역이라면 로그 출력 후 A* 실행 - 이동 가능한 근처까지 목적지 설정.
			validation = PacketValidators.ValidateRange( packet.Destination, _room );
			if(!validation.IsValid)
			{
				_logger.LogWarning( "AutoMove Destination ValidateRange Failed: {Error}", validation.ErrorMessage );
			}

			PosInfo currentPos = session.Player.PosInfo;
			GameMap currentMap = session.CurrentRoom.RoomMap;

			var (startX, startZ) = currentMap.WorldToCell( currentPos.PosX, currentPos.PosZ );
			var (goalX, goalZ) = currentMap.WorldToCell( packet.Destination.PosX, packet.Destination.PosZ );

			List<(int x, int z)> path;
			if(PathFinder.HasClearLine( currentMap.MapData, startX, startZ, goalX, goalZ ))
			{
				path = new List<(int x, int z)> { (goalX, goalZ) }; // 직선 -> 목적지 단일 웨이포인트
				_logger.LogInformation( "Player {PlayerId} AutoMove: CLearLine ({Sx},{Sz} -> {Gx},{Gz})",
					session.PlayerId, startX, startZ, goalX, goalZ );
			}
			else
			{
				path = PathFinder.FindPath( currentMap.MapData, startX, startZ, goalX, goalZ );
				_logger.LogInformation( "Player {PlayerId} AutoMove: A* ({Sx}, {Sz} -> {Gx}, {Gz}) PathLength:{Len}",
					session.PlayerId, startX, startZ, goalX, goalZ, path.Count );
			}

			var wayPoints = new List<PosInfo>();
			foreach(var (cellX, cellZ) in path)
			{
				var (worldX, worldZ) = currentMap.CellToWorld(cellX, cellZ);
				float height = currentMap.MapData.GetHeightAt(cellX, cellZ);
				wayPoints.Add( new PosInfo { PosX = worldX, PosZ = worldZ, PosY = height } );
			}

			bool reachable = (0 < path.Count && path[path.Count - 1] == (goalX, goalZ));

			S_PathInfo response = new S_PathInfo
			{
				Reachable = reachable,
			};
			response.Waypoints.AddRange( wayPoints );

			session.Send( response );
			if(0 < response.Waypoints.Count)
			{
				var lastWayPoint = response.Waypoints[path.Count - 1];

				_logger.LogInformation( "Player {PlayerId} Automove Reachable: {reachable}, PathExitPoint: ({X}, {Y}, {Z})",
					session.PlayerId, response.Reachable, lastWayPoint.PosX, lastWayPoint.PosY, lastWayPoint.PosZ );
			}
			else
			{
				_logger.LogWarning( "Player {PlayerId} AutoMove Reachable: {reachable}, Path Not Found, PathCount: {Count}",
					session.PlayerId, response.Reachable, response.Waypoints.Count );
			}
			
		}
	}
}
