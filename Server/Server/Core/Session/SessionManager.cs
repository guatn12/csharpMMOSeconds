
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.DependencyInjection;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Server.Room;
using Server.Infra;
using Server.Packet;
using Server.Services;
using Microsoft.Extensions.Options;
using Server.Config;

namespace Server.Core.Session
{
	/// <summary>
	/// 전역 세션 관리자
	/// </summary>
	public class SessionManager : ISessionManager
	{
		private readonly ILogger<SessionManager> _logger;
		private readonly IServiceProvider _serviceProvider;
		private readonly RedisService _redisService;
		private readonly PlayerPositionService _playerPositionService;
		private readonly long _sessionTimoutMs;
		private long _nextSessionId = 1;
		private readonly object _lock = new object();

		private readonly ConcurrentDictionary<long, IClientSession> _sessionById;
		private readonly ConcurrentDictionary<long, IClientSession> _sessionByPlayerId;

		#region 이벤트
		public event EventHandler<SessionRegisteredEventArgs> SessionRegistered;
		public event EventHandler<SessionUnregisteredEventArgs> SessionUnregistered;
		#endregion

		public SessionManager( ILogger<SessionManager> logger, IServiceProvider serviceProvider,
			RedisService redisService, PlayerPositionService playerPositionService, 
			TickService tickService, IOptions<ServerSettings> settings )
		{
			_logger = logger ?? throw new ArgumentNullException( nameof( logger ) );
			_serviceProvider = serviceProvider ?? throw new ArgumentNullException( nameof( serviceProvider ) );
			_redisService=redisService;
			_playerPositionService=playerPositionService;
			_sessionById = new ConcurrentDictionary<long, IClientSession>();
			_sessionByPlayerId = new ConcurrentDictionary<long, IClientSession>();

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

			// GameSession  생성
			var session = new ClientSession(logger,  packetManager, this, sessionId );

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

				if(_sessionByPlayerId.ContainsKey(session.PlayerId))
				{
					_logger.LogWarning("RegisterSession: PlayerId={PlayerId} already registered", session.PlayerId );
					return false;
				}

				bool addedById = _sessionById.TryAdd(session.SessionId, session);
				bool addedByPlayerId = _sessionByPlayerId.TryAdd(session.PlayerId, session);

				if(!addedById || !addedByPlayerId)
				{
					// 등록 실패
					_sessionById.TryRemove( session.SessionId, out _ );
					_sessionByPlayerId.TryRemove( session.PlayerId, out _ );
					_logger.LogError( "RegisterSession: Failed to add session atomically" );
					return false;
				}
			}

			// lock 밖에서 이벤트 발생 (이벤트 핸들러에서 deadlock 방지)
			_logger.LogInformation( "Session registered: SessionId={SessionId}, PlayerId={PlayerId}",
					session.SessionId, session.PlayerId );

			// 이벤트 발생
			SessionRegistered?.Invoke( this, new SessionRegisteredEventArgs
			{
				SessionId = session.SessionId,
				PlayerId = session.PlayerId,
				RegisteredAt = DateTime.UtcNow
			} );

			// Redis에 세션 정보 저장
			_ = Task.Run( async () =>
			{
				try
				{
					var sessionInfo = new
					{
						SessionId = session.SessionId,
						PlayerId = session.PlayerId,
						RegisteredAt = DateTime.UtcNow
					};

					await _redisService.SetSessionAsync( session.SessionId, sessionInfo, TimeSpan.FromHours( 2 ) );

					_logger.LogDebug( "Redis에 세션 정보 저장 완료: SessionId={SessionId}", session.SessionId );
				}
				catch(Exception ex)
				{
					_logger.LogError( ex, "Redis 세션 정보 저장 실패: SessionId={SessionId}", session.SessionId );
				}
			} );

			return true;
		}

		public bool UnregisterSession(long sessionId)
		{
			IClientSession session = null;
			lock(_lock)
			{
				if(!_sessionById.TryRemove( sessionId, out session ))
				{
					_logger.LogWarning( "UnregisterSession: SessionId={SessionId} not found", sessionId );
					return false;
				}

				// PlayerId 매핑 제거
				bool removedByPlayerId = _sessionByPlayerId.TryRemove( session.PlayerId, out _ );

				if(!removedByPlayerId)
				{
					_logger.LogError("UnregisterSession: PlayerId={PlayerId} mapping not found", session.PlayerId );

					if(!_sessionById.TryAdd(sessionId, session))
				{
					_logger.LogCritical(
						"CRITICAL: Rollback failed! SessionId={SessionId}, PlayerId={PlayerId}",
						sessionId, session.PlayerId);
				}
					return false;
				}
			}

			_logger.LogInformation( "Session unregistered: SessionId={SessionId}, PlayerId={PlayerId}", session.SessionId,
				session.PlayerId );

			// 이벤트 발생
			SessionUnregistered?.Invoke( this, new SessionUnregisteredEventArgs
			{
				SessionId = sessionId,
				PlayerId = session.PlayerId,
				UnregisteredAt = DateTime.UtcNow,
				Reason = "Disconnected"
			} );

			// Redis 삭제 + PlayerPosition 제거
			_ = Task.Run( async () =>
			{
				try
				{
					// Redis 세션 정보 삭제
					await _redisService.DeleteSessionAsync( sessionId );
					_logger.LogDebug( "Redis에서 세션 정보 삭제 완료: SessionId={SessionId}", sessionId );

					// 플레이어 위치 정보 제거
					await _playerPositionService.RemovePositionAsync( session.PlayerId );
					_logger.LogDebug( "플레이어 위치 정보 삭제 완료: PlayerId={PlayerId}", session.PlayerId );
				}
				catch(Exception ex)
				{
					_logger.LogError( ex, "세션 정리 실패: SessionId={SessionId}, PlayerId={PlayerId}", sessionId, session.PlayerId );
				}
			} );

			return true;
		}
		#endregion

		public IClientSession GetSession( long sessionId )
		{
			_sessionById.TryGetValue( sessionId, out IClientSession session );
			return session;
		}

		public IClientSession GetSessionByPlayerId( long playerId )
		{
			_sessionByPlayerId.TryGetValue( playerId, out IClientSession session );
			return session;
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
					_sessionByPlayerId.Clear();
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
					_logger.LogWarning( "Session timeout. SessionId={SessionId}, PlayerId={PlayerId}, Elapsed={Elapsed}ms",
						session.SessionId, session.PlayerId, elapsed );
					session.Disconnect();
				}
			}
		}

		#endregion

	}
}
