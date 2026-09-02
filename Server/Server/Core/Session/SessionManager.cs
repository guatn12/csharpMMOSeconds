using DatabaseLib.Redis;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Protocol;
using Server.Config;
using Server.Game;
using Server.Infra;
using Server.Packet;
using Server.Services;
using ServerCore;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace Server.Core.Session
{
	/// <summary>
	/// 전역 세션 관리자
	/// </summary>
	public class SessionManager : ISessionManager
	{
		private readonly ILogger<SessionManager> _logger;
		private readonly IServiceProvider _serviceProvider;
		private readonly IRedisService _redisService;
		private readonly IPlayerPositionService _playerPositionService;
		private readonly long _sessionTimoutMs;
		private long _nextSessionId = 1;
		private readonly object _lock = new object();

		private readonly ConcurrentDictionary<long, IClientSession> _sessionById;
		private readonly ConcurrentDictionary<long, IClientSession> _sessionByPlayerRawId;

		#region 이벤트
		public event EventHandler<SessionRegisteredEventArgs> SessionRegistered;
		public event EventHandler<SessionUnregisteredEventArgs> SessionUnregistered;
		public event EventHandler<SessionDisconnectingEventArgs> SessionDisconnecting;
		#endregion

		public SessionManager( ILogger<SessionManager> logger, IServiceProvider serviceProvider,
			IRedisService redisService, IPlayerPositionService playerPositionService, 
			TickService tickService, IOptions<ServerSettings> settings )
		{
			_logger = logger ?? throw new ArgumentNullException( nameof( logger ) );
			_serviceProvider = serviceProvider ?? throw new ArgumentNullException( nameof( serviceProvider ) );
			_redisService=redisService;
			_playerPositionService=playerPositionService;
			_sessionById = new ConcurrentDictionary<long, IClientSession>();
			_sessionByPlayerRawId = new ConcurrentDictionary<long, IClientSession>();

			// 설정에서 주기 값 읽기
			SessionConfig sessionConfig = settings.Value.Session;
			_sessionTimoutMs = sessionConfig.TimeoutMs;

			tickService.Register( "SessionManager.Heartbeat", sessionConfig.HeartbeatIntervalMs, CheckTimeouts );

			_logger.LogInformation( "SessionManager Created. Heartbeat: {Heartbeat}ms, Timeout: {Timeout}ms",
				sessionConfig.HeartbeatIntervalMs, sessionConfig.TimeoutMs );
		}

		#region 세션 생성

		public ClientSession CreateSession()
		{
			// SessionId 생성 (Thread-Safe)
			long sessionId = Interlocked.Increment(ref _nextSessionId);

			// DI 컨테이너에서 의존성 해결
			var logger = _serviceProvider.GetRequiredService<ILogger<ClientSession>>();
			var packetManager = _serviceProvider.GetRequiredService<PacketManager>();
			var jobQueueManager = _serviceProvider.GetRequiredService<IJobQueueManager>();

			// GameSession  생성
			var session = new ClientSession(logger, packetManager, this, jobQueueManager, _redisService, sessionId );

			_logger.LogInformation( "Session created: SessionId={SessionId}", sessionId );

			return session;
		}

		#endregion

		#region 세션 등록/해제
		public bool RegisterSession( IClientSession session )
		{
			if(session == null)
			{
				_logger.LogWarning( "RegisterSession: session is null" );
				return false;
			}

			lock ( _lock )
			{
				// 중복 체크
				if(_sessionById.ContainsKey(session.SessionId))
				{
					_logger.LogWarning( "RegisterSession: SessionId={SessionId} already registered", session.SessionId );
					return false;
				}

				bool addedById = _sessionById.TryAdd(session.SessionId, session);

				if(!addedById)
				{
					// 등록 실패
					_logger.LogError( "RegisterSession: Failed to add session atomically" );
					return false;
				}
			}


			_logger.LogInformation( "Session registered: SessionId={SessionId}", session.SessionId );

			// Redis에 세션 정보 저장
			_ = Task.Run( async () =>
			{
				try
				{
					var sessionInfo = new
					{
						SessionId = session.SessionId,
						//PlayerId = session.PlayerId,
						RegisteredAt = DateTime.UtcNow
					};

					await _redisService.SetAsync( $"session:{session.SessionId}", sessionInfo, TimeSpan.FromHours( 2 ) );

					_logger.LogDebug( "Redis에 세션 정보 저장 완료: SessionId={SessionId}", session.SessionId );
				}
				catch(Exception ex)
				{
					_logger.LogError( ex, "Redis 세션 정보 저장 실패: SessionId={SessionId}", session.SessionId );
				}
			} );

			return true;
		}

		public IClientSession BindPlayerToSession( long playerRawId, IClientSession newSession )
		{
			if(playerRawId <= 0)
				throw new ArgumentOutOfRangeException( nameof( playerRawId ) );

			if(newSession == null)
				throw new ArgumentNullException( nameof( newSession ) );

			IClientSession evicted = null;
			lock(_lock)
			{
				if(_sessionById.TryGetValue(newSession.SessionId, out IClientSession registered) == false ||
					ReferenceEquals(registered, newSession) == false)
				{
					throw new InvalidOperationException( "Session must be registered before player binding." );
				}

				newSession.BindPlayerRawId( playerRawId );
				_sessionByPlayerRawId.AddOrUpdate( playerRawId, newSession, ( _, oldSession ) => { evicted = oldSession; return newSession; } );
			}

			// lock 밖에서 이벤트 발생 (이벤트 핸들러에서 deadlock 방지)
			_logger.LogInformation( "Player bound to session: SessionId={SessionId}, PlayerRawId={PlayerRawId}",
				newSession.SessionId, playerRawId );

			// 이벤트 발생
			SessionRegistered?.Invoke( this, new SessionRegisteredEventArgs
			{
				SessionId = newSession.SessionId,
				PlayerRawId = playerRawId,
				RegisteredAt = DateTime.UtcNow
			} );

			return evicted != null && evicted.SessionId != newSession.SessionId ? evicted : null;
		}

		public async Task<bool> UnregisterSessionAsync(long sessionId)
		{
			IClientSession session = null;
			bool removedPlayerBinding = false;

			lock(_lock)
			{
				if(_sessionById.TryRemove( sessionId, out session ) == false)
				{
					_logger.LogWarning( "UnregisterSessionAsync: SessionId={SessionId} not found", sessionId );
					return false;
				}

				if( 0 < session.PlayerRawId )
				{
					// PlayerId 매핑 제거
					// 키+값이 내 세션과 일치할때만 원자적 제거 - 스왑으로 이미 새 세션이 차지한 맵핑을 구세션이 되돌아와 지워버리는 것을 방지
					removedPlayerBinding = ((ICollection<KeyValuePair<long, IClientSession>>)_sessionByPlayerRawId)
						.Remove( new KeyValuePair<long, IClientSession>( session.PlayerRawId, session ) );
				}	
			}

			try
			{
				// Redis 세션 정보 삭제
				await _redisService.DeleteAsync( $"session:{sessionId}" );
				_logger.LogDebug( "Redis에서 세션 정보 삭제 완료: SessionId={SessionId}", sessionId );

				if( removedPlayerBinding && session.Player != null)
				{
					// 플레이어 위치 정보 제거
					await _playerPositionService.RemovePositionAsync( session.PlayerId );
					_logger.LogDebug( "플레이어 위치 정보 삭제 완료: PlayerId={PlayerId}", session.PlayerId );
				}
			}
			catch(Exception ex)
			{
				_logger.LogError( ex, "세션 정리 실패: SessionId={SessionId}, PlayerId={PlayerId}", sessionId, session.PlayerId );
			}

			_logger.LogInformation( "Session unregistered: SessionId={SessionId}, PlayerRawId={PlayerRawId}", session.SessionId,
				session.PlayerRawId );

			// 이벤트 발생
			SessionUnregistered?.Invoke( this, new SessionUnregisteredEventArgs
			{
				SessionId = sessionId,
				PlayerRawId = session.PlayerRawId,
				UnregisteredAt = DateTime.UtcNow,
				Reason = "Disconnected"
			} );

			return true;
		}

		public void NotifyDisconnecting(IClientSession session, DisconnectReason reason)
		{
			if(session == null)
			{
				_logger.LogWarning( "NotifyDisconnecting: session is null" );
				return;
			}

			var handler = SessionDisconnecting;
			if(handler == null) return;

			var args = new SessionDisconnectingEventArgs
			{
				SessionId = session.SessionId,
				PlayerRawId = session.PlayerRawId,
				PlayerObjectId = session.PlayerId,
				Reason = reason,
				DisconnectingAt = DateTime.UtcNow,
			};

			// 한 구독자 throw가 다른 구독자 호출을 막지 않도록 격리.
			// '?.Invoke'는 첫 throw에서 dispatch 중단 + 호출자로 전파되므로 부적합
			foreach(Delegate d in handler.GetInvocationList())
			{
				try
				{
					((EventHandler<SessionDisconnectingEventArgs>)d)( this, args );
				}
				catch(Exception ex)
				{
					_logger.LogError( ex, "SessionDisconnecting handler threw. SessionId={SessionId}, Handler={Handler}",
						session.SessionId, d.Method.DeclaringType?.FullName );
				}
			}
		}

		public async Task DisconnectAllAsync()
		{
			List<IClientSession> sessions = _sessionById.Values.ToList();

			foreach(IClientSession session in sessions)
				session.DisconnectForShutdown();

			await Task.WhenAll( sessions.Select( x => x.DisconnectCompletion ) );
		}
		#endregion

		public IClientSession GetSession( long sessionId )
		{
			_sessionById.TryGetValue( sessionId, out IClientSession session );
			return session;
		}

		public IClientSession GetSessionByPlayerRawId( long playerRawId )
		{
			_sessionByPlayerRawId.TryGetValue( playerRawId, out IClientSession session );
			return session;
		}

		public bool IsCurrentPlayerSession( long playerRawId, IClientSession session )
		{
			return 0 < playerRawId && session != null &&
				_sessionByPlayerRawId.TryGetValue( playerRawId, out IClientSession current ) &&
				ReferenceEquals( current, session );
		}

		public int GetTotalSessionCount()
		{
			return _sessionById.Count;
		}

		public IEnumerable<IClientSession> GetAllActiveSessions()
		{
			return _sessionById.Values;
		}

		#region IHostedService 구현

		public void Shutdown()
		{
			int totalSessions = GetTotalSessionCount();
			if(0 < totalSessions)
			{
				_logger.LogWarning( "SessionManager stopping with {ActiveSessions} active sessions. Cleaning up...", totalSessions );

				lock(_lock)
				{
					_sessionById.Clear();
					_sessionByPlayerRawId.Clear();
				}
			}

			_logger.LogInformation( "SessionManager shutdown completed" );
		}

		private void CheckTimeouts()
		{
			long now = Environment.TickCount64;
			foreach(IClientSession session in _sessionById.Values)
			{
				long elapsed = now - session.LastActiveTime;
				if( _sessionTimoutMs < elapsed)
				{
					_logger.LogWarning( "Session timeout. SessionId={SessionId}, Elapsed={Elapsed}ms",
						session.SessionId, elapsed );
					session.Disconnect();
				}
			}
		}

		#endregion

	}
}
