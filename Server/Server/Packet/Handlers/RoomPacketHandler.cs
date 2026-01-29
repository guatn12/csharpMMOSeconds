using Microsoft.Extensions.Logging;
using Protocol;
using Server.Core.Session;
using Server.Room;
using Server.Services;
using Server.Utils;
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

			// 2. 범위 검증
			var rangeValidation = PacketValidators.ValidateRange(packet.PosInfo, _room);

			if(!rangeValidation.IsValid)
			{
				_logger.LogWarning("Move range validation failed: {Error}", rangeValidation.ErrorMessage );

				// 현재 위치를 클라이언트에 재전송 (동기화)
				var currentPos = session.Player.Info.PosInfo;

				if(currentPos != null)
				{
					var correctionPacket = new S_Move
					{
						PlayerId = session.PlayerId,
						PosInfo = currentPos,
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
			_room.RoomMap.UpdatePlayer( session, packet.PosInfo.PosX, packet.PosInfo.PosZ );

			// 4. Redis 캐시 업데이트 (근처 플레이어 검색 최적화용)
			await _playerPositionService.UpdatePositionAsync(session.PlayerId, packet.PosInfo);

			// 5. 브로드캐스트 (본인 제외)
			var response = new S_Move
			{
				PlayerId = session.PlayerId,
				PosInfo = packet.PosInfo,
			};
			_room.Broadcast( response, excludeSession: session );

			// 6. 룸별 이동 후처리
			await _room.OnPlayerMoveAsync( session, packet );

			_logger.LogDebug("Player {PlayerId} moved to ({X}, {Y}, {Z})",
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
				Player = session.Player.Info
			};

			// 3. 요청자에게만 전송
			_room.SendToPlayer( session, response );

			_logger.LogDebug( "Player {PlayerId} requested player info", session.PlayerId );
		}

		/// <summary>
		/// C_UseSkill 패킷 처리
		/// 스킬 사용 및 효과 처리
		/// </summary>
		private async Task HandleC_UseSkillAsync( IClientSession session, C_UseSkill packet)
		{
			// 1. 기본 검증
			var validation = PacketValidators.ValidateBasic(session, _room);
			if(!validation.IsValid)
			{
				_logger.LogWarning( "UseSkill validation failed: {Error}", validation.ErrorMessage );
				return;
			}

			// 2. 대상 확인
			if(!_room.ContainsPlayerToPlayerId(packet.TargetId))
			{
				_logger.LogWarning( "Target player {TargetId} not found in room", packet.TargetId );
				return;
			}

			// 3. 스킬 사용 검증 (room에 따라, 스킬 사용 가능 룸과 불가 룸을 구분)
			if(!await _room.ValidatePlayerUseSkillAsync(session, packet))
			{
				_logger.LogWarning( "Skill validation failed for Player {PlayerId}, Skill {SkillId}",
					session.PlayerId, packet.SkillId );
				return;
			}

			// 4. 스킬 사용
			bool skillUsed = session.Player.UseSkill( packet.SkillId );
			if(!skillUsed)
			{
				_logger.LogWarning( "Player {PlayerId} failed to use skill {SkillId}",
					session.PlayerId, packet.SkillId );
				return;
			}

			// 룸별 스킬 효과 처리
			await _room.OnPlayerUseSkillAsync( session, packet );

			_logger.LogDebug( "Player {PlayerId} used skill {SkillId} on target {TargetId}",
				session.PlayerId, packet.SkillId, packet.TargetId );
		}
	}
}
