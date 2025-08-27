using Google.Protobuf;
using Microsoft.Extensions.Logging;
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
		protected readonly ConcurrentDictionary<int, GameSession> _players;
		protected readonly object _lock = new object();
		private static int _nextRoomId = 1;
		private bool _dispose = false;

		public int RoomId { get; private set; }
		public string RoomName { get; protected set; }
		public int MaxPlayers { get; protected set; }
		public int CurrentPlayerCount => _players.Count;
		public abstract RoomType RoomType { get; }
		public RoomState State { get; protected set; } = RoomState.Created;

		public ConcurrentQueue<IJob> JobQueue { get; } = new ConcurrentQueue<IJob>();

		public IReadOnlyList<GameSession> Players => _players.Values.ToList();
		public bool IsEmpty => _players.IsEmpty;
		public bool IsFull => MaxPlayers <= _players.Count;

		public event EventHandler<PlayerRoomEventArgs> PlayerEntered;
		public event EventHandler<PlayerRoomEventArgs> PlayerLeft;

		protected BaseRoom( ILogger logger, string roomName, int maxPlayers )
		{
			_logger = logger ?? throw new ArgumentNullException( nameof( logger ) );
			RoomId = GenerateNextRoomId();
			RoomName = roomName ?? throw new ArgumentNullException( nameof( roomName ) );
			MaxPlayers = 0 < maxPlayers ? maxPlayers : throw new ArgumentOutOfRangeException( nameof( maxPlayers ) );

			_players = new ConcurrentDictionary<int, GameSession>();

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

		public GameSession FindPlayer( int sessionId )
		{
			_players.TryGetValue( sessionId, out var session );
			return session;
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

		public virtual async Task BroadcastAsync(IMessage packet, GameSession excludeSession = null)
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

		public virtual async Task SendToPlayerAsync(GameSession session, IMessage packet)
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

		public virtual async Task HandlePlayerMoveAsync(GameSession session, Protocol.C_Move packet)
		{
			if(!ContainsPlayer( session ) || packet?.PosInfo == null)
				return;

			try
			{
				// 이동 검증 (하위 클래스에서 재정의 가능)
				if(!await ValidatePlayerMoveAsync( session, packet ))
					return;

				// 룸별 이동 처리
				await OnPlayerMoveAsync( session, packet );

				// 다른 플레이어들에게 브로드캐스트
				var moveResponse = new Protocol.S_Move
				{
					PlayerId = session.SessionId,
					PosInfo = packet.PosInfo
				};

				await BroadcastAsync( moveResponse, session );

				_logger.LogDebug( "Player {SessionId} moved in room {RoomId} to ({X}, {Y}, {Z})",
					session.SessionId, RoomId, packet.PosInfo.PosX, packet.PosInfo.PosY, packet.PosInfo.PosZ );
			}
			catch (Exception e)
			{
				_logger.LogError( e, "Failed to handle move for player {SessionId} in room {RoomId}",
					session.SessionId, RoomId );
			}
		}

		public virtual async Task HandlePlayerChatAsync(GameSession session, Protocol.C_Chat packet)
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

				_logger.LogDebug( "Player {SessionId} chatted in room {RoomId}: '{Message}'",
					session.SessionId, RoomId, packet.Message);
			}
			catch (Exception ex)
			{
				_logger.LogError( ex, "Failed to handle chat for player {SessionId} in room {RoomId}",
					session.SessionId, RoomId );
			}
		}

		protected virtual Task OnInitializeAsync() => Task.CompletedTask;
		protected virtual Task OnCleanupAsync() => Task.CompletedTask;
		protected virtual Task OnPlayerEnterAsync(GameSession session) => Task.CompletedTask;
		protected virtual Task OnPlayerLeaveAsync(GameSession session) => Task.CompletedTask;
		protected virtual Task OnPlayerMoveAsync(GameSession session, Protocol.C_Move packet) => Task.CompletedTask;
		protected virtual Task OnPlayerChatAsync(GameSession session, Protocol.C_Chat packet) => Task.CompletedTask;

		protected virtual Task<bool> ValidatePlayerMoveAsync( GameSession session, Protocol.C_Move packet ) => Task.FromResult( true );
		protected virtual Task<bool> ValidatePlayerChatAsync( GameSession session, Protocol.C_Chat packet ) => Task.FromResult( true );

		private static int GenerateNextRoomId()
		{
			return System.Threading.Interlocked.Increment( ref _nextRoomId );
		}

		private async Task<bool> ForceLeaveAsync(GameSession session)
		{
			return await InternalLeaveAsync( session, true );
		}

		private async Task<bool> InternalLeaveAsync(GameSession session, bool isForced)
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
			catch( Exception ex )
			{
				_logger.LogError(ex, "Failed to remove Player {SessionId} from room {RoomId}", session.SessionId, RoomId );
				return false;
			}
		}

		// 스레드 세이프 Job 추가 및 WorkerManager 통지
		public bool TryEnqueueJobSafely(IJob job)
		{
			if(job == null || _dispose)
				return false;

			bool wasEmpty;
			lock( _lock )
			{
				wasEmpty = JobQueue.IsEmpty;
				JobQueue.Enqueue( job );
			}

			// 큐가 비어있었다면 WorkerManager에 알림
			if( wasEmpty )
			{
				_ = JobQueueManager.Instance.PushAsync( this );
			}

			return true;
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
