using Google.Protobuf;
using Microsoft.Extensions.Logging;
using Protocol;
using Server.Core.Session;
using ServerCore;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Server.Room
{
	public abstract class BaseRoom : IRoom, IDisposable
	{
		protected readonly ILogger _logger;
		protected readonly ConcurrentDictionary<long, GameSession> _players;
		protected readonly object _lock = new object();
		private static int _nextRoomId = 1;
		private bool _dispose = false;

		public int RoomId { get; private set; }
		public string RoomName { get; protected set; }
		public int MaxPlayers { get; protected set; }

		// 룸 크기 속성 
		public float RoomWidth { get; protected set; } = 100.0f;    // x 축 크기
		public float RoomHeight { get; protected set; } = 50.0f;    // y 축 크기
		public float RoomDepth { get; protected set; } = 100.0f;    // z 축 크기

		// 룸 경계 정보를 위한 속성
		public float MinX { get; protected set; } = 0.0f;
		public float MaxX => MinX + RoomWidth;
		public float MinY { get; protected set; } = 0.0f;
		public float MaxY => MinY + RoomHeight;
		public float MinZ { get; protected set; } = 0.0f;
		public float MaxZ => MinZ + RoomDepth;

		public int CurrentPlayerCount => _players.Count;
		public abstract RoomType RoomType { get; }
		public RoomState State { get; protected set; } = RoomState.Created;

		public IReadOnlyList<GameSession> Players => _players.Values.ToList();
		public bool IsEmpty => _players.IsEmpty;
		public bool IsFull => MaxPlayers <= _players.Count;

		public event EventHandler<PlayerRoomEventArgs> PlayerEntered;
		public event EventHandler<PlayerRoomEventArgs> PlayerLeft;

		protected BaseRoom( ILogger logger, string roomName, int maxPlayers,
			float roomWidth = 100.0f, float roomHeight = 50.0f, float roomDepth = 100.0f,
			float minX = 0.0f, float minY = 0.0f, float minZ = 0.0f )
		{
			_logger = logger ?? throw new ArgumentNullException( nameof( logger ) );
			RoomId = GenerateNextRoomId();
			RoomName = roomName ?? throw new ArgumentNullException( nameof( roomName ) );
			MaxPlayers = 0 < maxPlayers ? maxPlayers : throw new ArgumentOutOfRangeException( nameof( maxPlayers ) );

			// 3D 룸 크기 설정
			RoomWidth = roomWidth;
			RoomHeight = roomHeight;
			RoomDepth = roomDepth;

			MinX = minX;
			MinY = minY;
			MinZ = minZ;

			_players = new ConcurrentDictionary<long, GameSession>();

			_logger.LogInformation( "Room created: {RoomId} '{RoomName}' (Type: {RoomType}, Max: {MaxPlayers})",
				RoomId, RoomName, RoomType, MaxPlayers );
		}

		public virtual async Task InitializeAsync()
		{
			lock(_lock)
			{
				if(State != RoomState.Created)
				{
					_logger.LogWarning( "Room {RoomId} already initialized (State: {State})", RoomId, State );
					return;
				}

				State = RoomState.Active;
			}

			await OnInitializeAsync();

			_logger.LogInformation( "Room {RoomId} '{RoomName}' initialized successfully", RoomId, RoomName );
		}

		public virtual async Task CleanupAsync()
		{
			lock(_lock)
			{
				if(State == RoomState.Closed)
				{
					return;
				}

				State = RoomState.Closing;
			}

			// 모든 플레이어 강제 퇴장
			List<GameSession> playersToRemove = _players.Values.ToList();
			foreach(var player in playersToRemove)
			{
				await ForceLeaveAsync( player );
			}

			await OnCleanupAsync();
		}

		public bool ContainsPlayer( GameSession session )
		{
			return session != null && _players.ContainsKey( session.SessionId );
		}

		public bool ContainsPlayerToPlayerId( long playerId )
		{
			return 0 < playerId && _players.Values.Where( p => p.Player.PlayerId == playerId ).Any();
		}

		public GameSession FindPlayer( int sessionId )
		{
			_players.TryGetValue( sessionId, out var session );
			return session;
		}

		public GameSession FindPlayerToPlayerId( long playerId )
		{
			return _players.Values.Where( p => p.Player.PlayerId == playerId ).FirstOrDefault();
		}

		public virtual async Task<RoomEnterResult> TryEnterAsync( GameSession session )
		{
			if(session == null)
				return RoomEnterResult.InvalidState;

			lock(_lock)
			{
				// 상태 검증
				if(State != RoomState.Active && State != RoomState.Created)
					return RoomEnterResult.RoomClosed;

				// 이미 룸에 있는지 확인
				if(_players.ContainsKey( session.SessionId ))
					return RoomEnterResult.AlreadyInRoom;

				// 룸이 가득 찬 상태인지 확인
				if(MaxPlayers <= _players.Count)
					return RoomEnterResult.RoomFull;

				// 플레이어 추가
				if(!_players.TryAdd( session.SessionId, session ))
					return RoomEnterResult.UnknownError;

				// 룸이 가득 찼는지 상태 업데이트
				if(MaxPlayers <= _players.Count)
					State = RoomState.Full;
			}

			try
			{
				// 입장 시 session의 currentRoom 변경
				session.CurrentRoom = this;

				// 룸 별 입장 로직 실행
				await OnPlayerEnterAsync( session );

				// 이벤트 발생
				PlayerEntered?.Invoke( this, new PlayerRoomEventArgs( session, this ) );

				_logger.LogInformation( "Player {SessionId} entered room {RoomId} ({CurrentCount}/{MaxPlayers})",
					session.SessionId, RoomId, CurrentPlayerCount, MaxPlayers );

				return RoomEnterResult.Success;
			}
			catch(Exception e)
			{
				// 실패 시 플레이어 제거
				_players.TryRemove( session.SessionId, out _ );
				session.CurrentRoom = null; // 실패 시 session의 현재 룸도 초기화.
				_logger.LogError( e, "Failed to enter player {SessionId} to room {RoomId}", session.SessionId, RoomId );
				return RoomEnterResult.UnknownError;
			}
		}

		public virtual async Task<bool> TryLeaveAsync( GameSession session )
		{
			if(session == null || !_players.ContainsKey( session.SessionId ))
				return false;

			return await InternalLeaveAsync( session, false );
		}

		public virtual async Task BroadcastAsync( IMessage packet, GameSession excludeSession = null )
		{
			if(packet == null)
				return;

			List<GameSession> currentPlayers = _players.Values.ToList();
			List<Task> tasks = new List<Task>();

			foreach(var player in currentPlayers)
			{
				// TODO : Send 호출 플레이어 제외 임시 주석 처리. 주석 다시 해제
				if(player != excludeSession)
				{
					tasks.Add( SendToPlayerAsync( player, packet ) );
				}
			}

			if(0 < tasks.Count)
			{
				await Task.WhenAll( tasks );
			}
		}

		public virtual async Task SendToPlayerAsync( GameSession session, IMessage packet )
		{
			if(session == null || packet == null)
				return;

			try
			{
				await Task.Run( () => session.Send( packet ) );
			}
			catch(Exception e)
			{
				_logger.LogError( e, "Failed to send packet to player {SessionId} in room {RoomId}",
					session.SessionId, RoomId );
			}
		}

		public virtual async Task HandlePlayerMoveAsync( GameSession session, Protocol.C_Move packet, ILogger logger )
		{
			if(!ContainsPlayer( session ) || packet?.PosInfo == null)
				return;

			try
			{
				// 이동 검증 (하위 클래스에서 재정의 가능)
				if(!await ValidatePlayerMoveAsync( session, packet ))
				{
					// 검증 실패 시 현재 위치를 클라이언트에 재전송 (동기화)
					PosInfo currentPos = await session.GetCurrentPositionAsync();
					if(currentPos != null)
					{
						var correctionPacket = new Protocol.S_Move
						{
							PlayerId = session.SessionId,
							PosInfo = currentPos
						};
						await SendToPlayerAsync(session, correctionPacket );
						logger.LogWarning("위치 검증 실패로 클라이언트 위치 동기화: Player {SessionId}", session.SessionId);
					}

					return;
				}

				// GameSession을 통해 Redis 기반 3D 위치 업데이트
				bool positionUpdated = await session.UpdatePositionAsync(packet.PosInfo);
				if(!positionUpdated)
				{
					logger.LogWarning( "Player {PlayerId} 위치 업데이트 실패 (경계 밖 또는 오류)", session.SessionId );
					return;
				}

				// 룸별 이동 처리
				await OnPlayerMoveAsync( session, packet );

				// 다른 플레이어들에게 브로드캐스트
				var moveResponse = new Protocol.S_Move
				{
					PlayerId = session.SessionId,
					PosInfo = packet.PosInfo
				};

				await BroadcastAsync( moveResponse, session );

				logger.LogDebug( "Player {SessionId} moved in room {RoomId} to ({X}, {Y}, {Z})",
					session.SessionId, RoomId, packet.PosInfo.PosX, packet.PosInfo.PosY, packet.PosInfo.PosZ );
			}
			catch(Exception e)
			{
				logger.LogError( e, "Failed to handle move for player {SessionId} in room {RoomId}",
					session.SessionId, RoomId );
			}
		}

		public virtual async Task HandlePlayerChatAsync( GameSession session, Protocol.C_Chat packet, ILogger logger )
		{
			if(!ContainsPlayer( session ) || string.IsNullOrWhiteSpace( packet?.Message ))
				return;

			try
			{
				// 채팅 검증 (하위 클래스에서 재정의 가능)
				if(!await ValidatePlayerChatAsync( session, packet ))
					return;

				// 룸별 채팅 처리
				await OnPlayerChatAsync( session, packet );

				// 룸 내 브로드캐스트
				var chatResponse = new Protocol.S_Chat
				{
					PlayerId= session.SessionId,
					Message = packet.Message,
				};

				await BroadcastAsync( chatResponse, session );

				logger.LogDebug( "Player {SessionId} chatted in room {RoomId}: '{Message}'",
					session.SessionId, RoomId, packet.Message );
			}
			catch(Exception ex)
			{
				logger.LogError( ex, "Failed to handle chat for player {SessionId} in room {RoomId}",
					session.SessionId, RoomId );
			}
		}

		public virtual async Task HandlePlayerPlayerInfoAsync( GameSession session, C_PlayerInfo packet, ILogger logger )
		{
			if(!ContainsPlayer( session ) || packet?.TargetPlayerId < 0 ||
				!ContainsPlayerToPlayerId( packet.TargetPlayerId ))
				return;

			try
			{
				// 플레이어 정보를 찾는데 검증이 필요할지 의문.
				if(!await ValidatePlayerInfoAsync( session, packet ))
					return;

				GameSession targetSession = FindPlayerToPlayerId(packet.TargetPlayerId);
				if(targetSession == null)
				{
					logger.LogWarning( "TargetPlayer {PlayerId} Not Found int Room {RoomId}",
						packet.TargetPlayerId, RoomId );

					Protocol.S_PlayerStat errorResponse = new Protocol.S_PlayerStat();
					await SendToPlayerAsync( session, errorResponse );
					return;
				}

				// 데이터 탐색 및 전달.
				Protocol.S_PlayerStat response = new Protocol.S_PlayerStat
				{
					Player = targetSession.Player.Info
				};

				await SendToPlayerAsync( session, response );

				logger.LogDebug( "TargetPlayer (Id: {PlayerId}, Name: {PlayerName}) Find in Room {RoomId}",
					targetSession.Player.PlayerId, targetSession.Player.PlayerName, RoomId );
			}
			catch(Exception e)
			{
				logger.LogError( e, "Failed to handle PlayerInfo Find player {TargetPlayerId} in room {RoomId}",
					packet.TargetPlayerId, RoomId );
			}
		}

		public virtual async Task HandlePlayerUseSkillAsync( GameSession session, C_UseSkill packet, ILogger logger )
		{
			if(!ContainsPlayer( session ) || !ContainsPlayerToPlayerId( packet.TargetId ))
				return;

			try
			{
				// 스킬 사용 검증 (하위 클래스에서 재정의 가능)
				if(!await ValidatePlayerUseSkillAsync( session, packet ))
					return;

				// 스킬 사용 실행
				bool skillUsed = session.Player.UseSkill(packet.SkillId);
				if(!skillUsed)
				{
					logger.LogWarning( "Player {PlayerId} failed to use skill {SkillId} - insufficient resources or invalid state",
						session.Player.PlayerId, packet.SkillId );
					return;
				}

				// 룸별 스킬 사용 처리. - 스킬 사용만으로 힐, 데미지 버프등을 판단하여 브로드캐스트 할 수 없음.
				// 내부에서 판단하여 주변 유저들에게 전파해야 한다.
				await OnPlayerUseSkillAsync( session, packet );

				logger.LogDebug( "Player {PlayerId} Use Skill ({SkillId}) Success, To Target Player {PlayerId} In Room {RoomId}",
					session.Player.PlayerId, packet.SkillId, packet.TargetId, RoomId );
			}
			catch(Exception e)
			{
				logger.LogError( e, "Failed to Use Skill (PlayerId: {PlayerId}, SkillId: {SkillId}, TargetId: {TargetId}) in room {RoomId}",
					session.Player.PlayerId, packet.SkillId, packet.TargetId, RoomId );
			}
		}

		protected virtual Task OnInitializeAsync() => Task.CompletedTask;
		protected virtual Task OnCleanupAsync() => Task.CompletedTask;
		protected virtual Task OnPlayerEnterAsync( GameSession session ) => Task.CompletedTask;
		protected virtual Task OnPlayerLeaveAsync( GameSession session ) => Task.CompletedTask;
		protected virtual Task OnPlayerMoveAsync( GameSession session, Protocol.C_Move packet ) => Task.CompletedTask;
		protected virtual Task OnPlayerChatAsync( GameSession session, Protocol.C_Chat packet ) => Task.CompletedTask;
		protected virtual Task OnPlayerUseSkillAsync( GameSession session, Protocol.C_UseSkill packet ) => Task.CompletedTask;

		protected virtual Task<bool> ValidatePlayerMoveAsync( GameSession session, Protocol.C_Move packet )
		{
			// 기본 3D 위치 검증
			bool isValid = Utils.Position3DValidator.IsValidPosition(packet.PosInfo, this);

			if(!isValid)
			{
				_logger.LogWarning( "Invalid move attempt by player {SessionId} in room{ RoomId}: Position ({X}, {Y}, {Z}) is outside room bounds ({MinX}-{MaxX},{ MinY}-{ MaxY}, { MinZ}-{ MaxZ})",
					session.SessionId, RoomId,packet.PosInfo.PosX, packet.PosInfo.PosY, packet.PosInfo.PosZ,
					MinX, MaxX, MinY, MaxY, MinZ, MaxZ );
			}

			return Task.FromResult( isValid );
		}
		protected virtual Task<bool> ValidatePlayerChatAsync( GameSession session, Protocol.C_Chat packet ) => Task.FromResult( true );
		protected virtual Task<bool> ValidatePlayerInfoAsync( GameSession session, Protocol.C_PlayerInfo packet ) => Task.FromResult( true );
		protected virtual Task<bool> ValidatePlayerUseSkillAsync( GameSession session, Protocol.C_UseSkill packet ) => Task.FromResult( true );

		private static int GenerateNextRoomId()
		{
			return System.Threading.Interlocked.Increment( ref _nextRoomId );
		}

		private async Task<bool> ForceLeaveAsync( GameSession session )
		{
			return await InternalLeaveAsync( session, true );
		}

		private async Task<bool> InternalLeaveAsync( GameSession session, bool isForced )
		{
			if(!_players.TryRemove( session.SessionId, out var removedSession ))
				return false;

			try
			{
				// 룸 별 퇴장 로직 실행
				await OnPlayerLeaveAsync( session );

				// 상태 업데이트
				lock(_lock)
				{
					if(State == RoomState.Full && _players.Count < MaxPlayers)
						State = RoomState.Active;
				}

				// 이벤트 발생
				PlayerLeft?.Invoke( this, new PlayerRoomEventArgs( session, this ) );

				var leaveType = isForced ? "forced to leave" : "left";
				_logger.LogInformation( "Player {SessionId} {LeaveType} room {RoomId} ({CurrentCount}/{MaxPlayers})",
					session.SessionId, leaveType, RoomId, CurrentPlayerCount, MaxPlayers );

				return true;
			}
			catch(Exception ex)
			{
				_logger.LogError( ex, "Failed to remove Player {SessionId} from room {RoomId}", session.SessionId, RoomId );
				return false;
			}
		}

		public void Dispose()
		{
			Dispose( true );
			GC.SuppressFinalize( this );
		}

		protected virtual void Dispose( bool disposing )
		{
			if(!_dispose && disposing)
			{
				CleanupAsync().GetAwaiter().GetResult();
				_dispose = true;
			}
		}


	}
}
