using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Server.Config;
using Server.Core.Session;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace Server.Room
{
	// Room 생성, 관리, 검색을 담당하는 매니저
	public class RoomManager : IRoomManager, IHostedService, IDisposable
	{
		private readonly ILogger<RoomManager> _logger;
		private readonly IOptionsMonitor<ServerSettings> _serverSettings;
		private readonly ILoggerFactory _loggerFactory;
		private readonly ConcurrentDictionary<int, IRoom> _rooms;
		private readonly object _lock = new object();

		private Timer _cleanupTimer;
		private bool _disposed = false;
		private int _nextRoomId = 1;
		private IRoom _defaultLobby;

		public int TotalRoomCount => _rooms.Count;
		public int TotalPlayerCount => _rooms.Values.Sum( r => r.CurrentPlayerCount );

		public event EventHandler<RoomCreatedEventArgs> RoomCreated;
		public event EventHandler<RoomDestoryedEventArgs> RoomDestoryed;
		public event EventHandler<PlayerRoomChangedEventArgs> PlayerRoomChanged;

		public RoomManager( ILogger<RoomManager> logger, IOptionsMonitor<ServerSettings> serverSettings,
			ILoggerFactory loggerFactory )
		{
			_logger = logger ?? throw new ArgumentNullException( nameof( logger ) );
			_serverSettings = serverSettings ?? throw new ArgumentNullException( nameof( serverSettings ) );
			_loggerFactory = loggerFactory ?? throw new ArgumentNullException( nameof( loggerFactory ) );

			_rooms = new ConcurrentDictionary<int, IRoom>();

			_logger.LogInformation( "RoomManager created with settings monitoring enabled" );
		}

		public async Task StartAsync( CancellationToken cancellationToken )
		{
			_logger.LogInformation( "RoomManager starting up..." );

			await InitializeAsync();

			// 정리 타이머 시작
			TimeSpan cleanupInterval = TimeSpan.FromMinutes(_serverSettings.CurrentValue.Room.EmptyRoomCleanupIntervalMinutes);
			_cleanupTimer = new Timer( async _ => await PerformCleanupAsync(), null, cleanupInterval, cleanupInterval );

			_logger.LogInformation( "RoomManager started successfully with {RoomCount} rooms", _rooms.Count );
		}

		public async Task StopAsync( CancellationToken cancellationToken )
		{
			_logger.LogInformation( "RoomManager shutting down..." );

			await ShutdownAsync();

			_cleanupTimer?.Dispose();
			_cleanupTimer = null;

			_logger.LogInformation( "RoomManager stopped" );
		}



		public async Task<IRoom> CreateRoomAsync( RoomType roomType, string roomName, int maxPlayers, GameSession creatorSession = null )
		{
			try
			{
				// 룸 수 제한 확인
				if(_serverSettings.CurrentValue.Room.MaxRooms <= _rooms.Count)
				{
					_logger.LogWarning( "Cannot create room '{RoomName}': Maximum concurrent rooms ({MaxRooms}) reached",
						  roomName, _serverSettings.CurrentValue.Room.MaxRooms );
					return null;
				}

				// 룸 이름 검증
				if(string.IsNullOrWhiteSpace( roomName ) ||
					_serverSettings.CurrentValue.Room.MaxRoomNameLength < roomName.Length)
				{
					_logger.LogWarning( "Invalid room name: {RoomName}", roomName );
					return null;
				}

				// 룸 타입별 플레이어 수 검증
				int maxAllowedPlayers = roomType switch
				{
					RoomType.Lobby => _serverSettings.CurrentValue.Room.Lobby.MaxPlayers,
					RoomType.Battle => _serverSettings.CurrentValue.Room.Battle.MaxPlayers,
					RoomType.Dungeon => _serverSettings.CurrentValue.Room.Dungeon.MaxPlayers, // 아직 미구현
					RoomType.Guild => _serverSettings.CurrentValue.Room.Guild.MaxPlayers,   // 아직 미구현
					RoomType.Private => _serverSettings.CurrentValue.Room.Private.MaxPlayers, // 아직 미구현
					_ => _serverSettings.CurrentValue.Room.Lobby.MaxPlayers
				};

				if(maxPlayers <= 0 || maxAllowedPlayers < maxPlayers)
				{
					_logger.LogWarning( "Invalid max players: {MaxPlayers} for room type {RoomType} (limit: {MaxAllowedPlayers})", 
						maxPlayers, roomType, maxAllowedPlayers );
					return null;
				}

				IRoom room = roomType switch
				{
					RoomType.Lobby => new LobbyRoom(
						_loggerFactory.CreateLogger<LobbyRoom>(),
						Options.Create(_serverSettings.CurrentValue),
						roomName, false),
					RoomType.Battle => throw new NotImplementedException("BattleRoom not implemented yet"),
					RoomType.Dungeon => throw new NotImplementedException("DungeonRoom not implemented yet"),
					RoomType.Guild => throw new NotImplementedException("GuildRoom not implemented yet"),
					RoomType.Private => throw new NotImplementedException("PrivateRoom not implemented yet"),
					_ => throw new ArgumentException($"Unknown room type: {roomType}")
				};

				// 룸 초기화
				await room.InitializeAsync();

				// 룸 등록
				if(_rooms.TryAdd( room.RoomId, room ))
				{
					_logger.LogInformation( "Room created successfully: {RoomType} '{RoomName}' (ID: {RoomId}, Creator: {CreatorId})",
						roomType, roomName, room.RoomId, creatorSession?.SessionId );

					// 이벤트 발생
					RoomCreated?.Invoke( this, new RoomCreatedEventArgs( room, creatorSession ) );
					return room;
				}
				else
				{
					_logger.LogError( "Failed to register room: {RoomType} '{RoomName}' (ID: {RoomId})",
						 roomType, roomName, room.RoomId );
					await room.CleanupAsync();
					return null;
				}
			}
			catch(Exception ex)
			{
				_logger.LogError( ex, "Failed to create room: {RoomType} '{RoomName}'", roomType, roomName );
				return null;
			}
		}

		public async Task<IRoom> CreateDefaultLobbyAsync()
		{
			try
			{
				LobbyConfig lobbySettings = _serverSettings.CurrentValue.Room.Lobby;
				IRoom lobby = await CreateRoomAsync(RoomType.Lobby, lobbySettings.DefaultName,
					lobbySettings.MaxPlayers);

				if(lobby is LobbyRoom lobbyRoom)
				{
					// 기본 로비로 설정 (리플렉션 또는 별도 생성자 사용)
					_defaultLobby = lobby;
					_logger.LogInformation( "Default lobby created: '{LobbyName}' (ID: {RoomId})",
						lobby.RoomName, lobby.RoomId );


				}
				return lobby;
			}
			catch(Exception ex)
			{
				_logger.LogError( ex, "Failed to create default lobby" );
				throw;
			}
		}

		public async Task<bool> DestoryRoomAsync( int roomId )
		{
			try
			{
				if(_rooms.TryRemove( roomId, out IRoom room ))
				{
					// 기본 로비는 삭제 불가
					if(room == _defaultLobby)
					{
						_logger.LogWarning( "Cannot destory default lobby (ID: {RoomId}", roomId );
						_rooms.TryAdd( roomId, room ); // 다시 추가
						return false;
					}

					_logger.LogInformation( "Destroying room: '{RoomName}' (ID: {RoomId}, Type: {RoomType})",
						  room.RoomName, room.RoomId, room.RoomType );

					await room.CleanupAsync();

					// 이벤트 발생
					RoomDestoryed?.Invoke( this, new RoomDestoryedEventArgs( room.RoomId, room.RoomName, room.RoomType,
						"Manual destruction" ) );

					return true;
				}

				_logger.LogWarning( "Room not found for destruction: ID {RoomId}", roomId );
				return false;
			}
			catch(Exception ex)
			{
				_logger.LogError( ex, "Failed to destroy room: ID {RoomId}", roomId );
				return false;
			}
		}

		public async Task<int> CleanupEmptyRoomsAsync()
		{
			try
			{
				List<IRoom> emptyRooms = _rooms.Values.Where(room => room.IsEmpty && room != _defaultLobby).ToList();

				if(emptyRooms.Count == 0)
				{
					_logger.LogDebug( "No Empty rooms to cleanup" );
					return 0;
				}

				_logger.LogInformation( "Cleaning up {EmptyRoomCount} empty rooms", emptyRooms.Count );

				int cleanedCount = 0;
				foreach(var room in emptyRooms)
				{
					if(await DestoryRoomAsync( room.RoomId ))
					{
						cleanedCount++;
					}
				}

				_logger.LogInformation( "Cleaned up {CleanedCount}/{EmptyRoomCount} empty rooms",
					cleanedCount, emptyRooms.Count );

				return cleanedCount;
			}
			catch(Exception ex)
			{
				_logger.LogError( ex, "Failed to cleanup empty rooms" );
				return 0;
			}
		}

		public Task<IRoom> FindRoomAsync( int roomId )
		{
			_rooms.TryGetValue( roomId, out var room );
			return Task.FromResult( room );
		}

		public Task<IRoom> FindRoomByNameAsync( string roomName )
		{
			var room = _rooms.Values.FirstOrDefault(r => string.Equals(r.RoomName, roomName,
				StringComparison.OrdinalIgnoreCase));
			return Task.FromResult( room );
		}

		public Task<IRoom> FindAvailableRoomAsync( RoomType roomType = RoomType.Lobby )
		{
			var avaliableRoom = _rooms.Values.Where(r => r.RoomType == roomType &&
			!r.IsFull && r.State == RoomState.Active)
				.OrderBy(r => r.CurrentPlayerCount) // 플레이어 수가 적은 순
				.FirstOrDefault();

			return Task.FromResult( avaliableRoom  ?? (roomType == RoomType.Lobby ? _defaultLobby : null) );
		}

		public Task<IReadOnlyList<IRoom>> GetRoomsByTypeAsync( RoomType roomType )
		{
			var rooms = _rooms.Values
				.Where(r => r.RoomType == roomType)
				.ToList();

			return Task.FromResult<IReadOnlyList<IRoom>>( rooms );
		}

		public IReadOnlyList<IRoom> GetActiveRooms()
		{
			return _rooms.Values
				.Where(r => r.State == RoomState.Active || r.State == RoomState.Full)
				.ToList ();
		}

		public async Task<RoomEnterResult> JoinDefaultLobbyAsync( GameSession session )
		{
			if(_defaultLobby == null)
			{
				_logger.LogError( "Default lobby not available for Player {SessionId}", session.SessionId );
				return RoomEnterResult.RoomClosed;
			}

			return await _defaultLobby.TryEnterAsync( session );
		}

		public async Task<RoomEnterResult> MovePlayerToRoomAsync( GameSession session, int targetRoomId )
		{
			try
			{
				// 현재 룸에서 퇴장
				IRoom currentRoom = await FindPlayerCurrentRoomAsync(session);
				if(currentRoom != null)
				{
					await currentRoom.TryLeaveAsync( session );
				}

				// 새 룸 입장
				IRoom targetRoom = await FindRoomAsync(targetRoomId);
				if(targetRoom == null)
				{
					_logger.LogWarning( "Target room not found: {RoomId} for Player {SessionId}",
						targetRoomId, session.SessionId );
					return RoomEnterResult.InvalidState;
				}

				var result = await targetRoom.TryEnterAsync(session);

				// 룸 변경 이벤트 발생
				if(result == RoomEnterResult.Success)
				{
					PlayerRoomChanged?.Invoke( this, new PlayerRoomChangedEventArgs( session, currentRoom, targetRoom ) );
				}

				return result;
			}
			catch( Exception ex )
			{
				_logger.LogError( ex, "Failed to move Player {SessionId} to Room {RoomId}",
					session.SessionId, targetRoomId );
				return RoomEnterResult.UnknownError;
			}
		}

		public Task<IRoom> FindPlayerCurrentRoomAsync( GameSession session )
		{
			IRoom currentRoom = _rooms.Values.FirstOrDefault(r => r.ContainsPlayer(session));
			return Task.FromResult( currentRoom );
		}

		public async Task<bool> RemovePlayerFromAllRoomsAsync( GameSession session )
		{
			try
			{
				bool removed = false;
				List<IRoom> roomsWithPlayer = _rooms.Values.Where(r => r.ContainsPlayer(session)).ToList();

				foreach(var room in roomsWithPlayer)
				{
					if(await room.TryLeaveAsync(session))
					{
						removed = true;
						_logger.LogDebug( "Player {SessionId} removed from Room {RoomId}",
							session.SessionId, room.RoomId );
					}
				}

				return removed;
			}
			catch(Exception ex)
			{
				_logger.LogError( ex, "Failed to remove Player {SessionId} from all rooms", session.SessionId );
				return false;
			}
		}

		public async Task<Dictionary<RoomType, RoomStatistics>> GetRoomStatisticsAsync()
		{
			var statistics = new Dictionary<RoomType, RoomStatistics>();

			foreach (RoomType roomType in Enum.GetValues( typeof( RoomType )))
			{
				var roomsOfType = await GetRoomsByTypeAsync( roomType );
				statistics[ roomType ] = new RoomStatistics
				{
					RoomCount = roomsOfType.Count,
					PlayerCount = roomsOfType.Sum( r => r.CurrentPlayerCount ),
					AvailableRooms = roomsOfType.Count( r => !r.IsFull ),
					FullRooms = roomsOfType.Count( r => r.IsFull ),
					AveragePlayersPerRoom = 0 < roomsOfType.Count
					? (double)roomsOfType.Sum( r => r.CurrentPlayerCount ) / roomsOfType.Count
					: 0
				};
			}

			return statistics;
		}

		public RoomManagerMemoryInfo GetMemoryInfo()
		{
			Dictionary<RoomType, int> roomCountByType = new Dictionary<RoomType, int>();
			foreach(RoomType roomType in Enum.GetValues(typeof(RoomType)))
			{
				roomCountByType[ roomType ] = _rooms.Values.Count( r => r.RoomType == roomType );
			}

			return new RoomManagerMemoryInfo
			{
				EstimatedMemoryUsage = EstimateMemoryUsage(),
				ActiveRoomCount = GetActiveRooms().Count,
				TotalPlayerCount = TotalPlayerCount,
				RoomCountByType = roomCountByType,
				LastCleanupTime = DateTime.UtcNow
			};
		}

		public async Task InitializeAsync()
		{
			try
			{
				// 기본 로비 생성
				LobbyConfig lobbyConfig = _serverSettings.CurrentValue.Room.Lobby;
				_defaultLobby = await CreateRoomAsync(RoomType.Lobby, lobbyConfig.DefaultName,
					lobbyConfig.MaxPlayers);
				
				if( _defaultLobby == null)
				{
					throw new InvalidOperationException( "Failed to create default lobby" );
				}

				_logger.LogInformation( "RoomManager Initialized successfully" );
			}
			catch(Exception ex)
			{
				_logger.LogError( ex, "Failed to Initialized RoomManager" );
				throw;
			}
		}

		public async Task ShutdownAsync()
		{
			try
			{
				List<IRoom> roomsToClose = _rooms.Values.ToList();
				_logger.LogInformation( "Shutting down {RoomCount} rooms...", roomsToClose.Count );

				// 모든 룸 종료
				var shutdownTask = roomsToClose.Select(room=>room.CleanupAsync());
				await Task.WhenAll( shutdownTask );

				_rooms.Clear();
				_defaultLobby = null;

				_logger.LogInformation( "All rooms shutdown successfully" );
			}
			catch(Exception ex)
			{
				_logger.LogError( ex, "Error during RoomManager Shutdown" );
				throw;
			}
		}

		private async Task PerformCleanupAsync()
		{
			try
			{
				await CleanupEmptyRoomsAsync();
			}
			catch(Exception ex)
			{
				_logger.LogError( ex, "Error during scheduled cleanup" );
			}
		}

		private long EstimateMemoryUsage()
		{
			// 대략적인 메모리 사용량 계산 (바이트 단위)
			// 실제 구현에서는 더 정확한 계산 로직 구현
			return _rooms.Count * 1024 * 10 + TotalPlayerCount * 1024;	// 룸 당 10KB + 플레이어당 1KB 가정
		}

		public void Dispose()
		{
			if(!_disposed)
			{
				_cleanupTimer?.Dispose();
				ShutdownAsync().GetAwaiter().GetResult();
				_disposed = true;
			}
			GC.SuppressFinalize(this);
		}
	}
}
