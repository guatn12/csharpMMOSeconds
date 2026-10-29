using System;
using System.Net;
using System.Threading;
using Microsoft.Extensions.Configuration;
using System.Collections.Generic;
using ServerCore;
using Protocol;
using Microsoft.Extensions.Logging;
using Serilog;
using Microsoft.Extensions.DependencyInjection;
using DummyClient.Configuration;
using Microsoft.Extensions.Options;
using DummyClient.Configuration.Services;
using System.Threading.Tasks;
using DummyClient.Packet;
using System.Linq;
using DummyClient.Data;
using DummyClient.Config;
using DummyClient.Data.Models;
using DummyClient.Services;
using System.IO;
using System.Net.Sockets;

namespace DummyClient
{
	public class ConnectionSettings
	{
		public string Host { get; set; }
		public int Port { get; set; }
	}

	public class ClientPlayerInfo
	{
		public long PlayerId { get; set; }
		public string PlayerName { get; set; } = "Unknown";
		
		public StatInfo Stats { get; set; }

		public long MaxExp { get; set; } = 100;
		public long Gold {  get; set; } = 0;
		
		// 인벤토리
		public Dictionary<long, InventoryItemInfo> Inventory { get; set; } = new();
		// 장비
		public Dictionary<int, long> EquipInfo { get; set; } = new();

		public PosInfo Position { get; set; }

		public int CurrentMapId { get; set; } = 0;

		// 경험치
		public int Level => Stats.Level;
		public long CurrentExp => Stats.Experience;

		// 전투 관련 계산 프로퍼티
		public bool IsAlive => 0 < Stats.CurrentHP;
		public float HPPercent => 0 < Stats.MaxHP ? (float)Stats.CurrentHP / Stats.MaxHP * 100 : 0;
		public float MPPercent => 0 < Stats.MaxMP ? (float)Stats.CurrentMP / Stats.MaxMP * 100 : 0;
		public float ExpPercent => 0 < MaxExp ? (float)CurrentExp / MaxExp * 100 : 0;

		public ClientPlayerInfo()
		{
			// Stats 초기화
			Stats = new StatInfo
			{
				Attack = 10,
				Defense = 5,
				MaxHP = 100,
				MaxMP = 50,
				CurrentHP = 100,
				CurrentMP = 50,
				Experience = 0,
				Level = 0,
				Speed = 5.0f
			};

			Position = new PosInfo
			{
				PosX = 50.0f,
				PosY = 1.0f,
				PosZ = 50.0f,
				RotationX = 0.0f,
				RotationY = 0.0f,
				RotationZ = 0.0f
			};
		}

		public void Clear()
		{
			PlayerId = 0;
			PlayerName = "Unknown";
			Stats.Level = 0;
			Stats.MaxMP = 0;
			Stats.MaxHP = 0;
			Stats.CurrentHP = 0;
			Stats.CurrentMP = 0;
			Stats.Experience = 0;
			Stats.Attack = 0;
			Stats.Defense = 0;
			Stats.Speed = 0;
			MaxExp = 0;
			Gold = 0;
			Position.PosX = 0;
			Position.PosY = 0;
			Position.PosZ = 0;
			Position.RotationX = 0;
			Position.RotationY = 0;
			Position.RotationZ = 0;
			CurrentMapId = 0;

			Inventory.Clear();
			EquipInfo.Clear();
		}

		public float DistanceTo(PosInfo target)
		{
			if(target == null || Position == null) return float.MaxValue;

			float dx = target.PosX - Position.PosX;
			float dy = target.PosY - Position.PosY;
			float dz = target.PosZ - Position.PosZ;

			return (float)Math.Sqrt(dx * dx + dy * dy + dz * dz);
		}

		public void LogStatus(Microsoft.Extensions.Logging.ILogger logger)
		{
			logger.LogInformation( "========================================" );
			logger.LogInformation( "플레이어 상태" );
			logger.LogInformation( "========================================" );
			logger.LogInformation( "이름: {Name} (ID: {PlayerId})", PlayerName, PlayerId );
			logger.LogInformation( "레벨: {Level}", Level );
			logger.LogInformation( "HP: {CurrentHP}/{MaxHP} ({HPPercent:F1}%)",
				Stats.CurrentHP, Stats.MaxHP, HPPercent );
			logger.LogInformation( "MP: {CurrentMP}/{MaxMP} ({MPPercent:F1}%)",
				Stats.CurrentMP, Stats.MaxMP, MPPercent );
			logger.LogInformation( "공격력: {Attack} | 방어력: {Defense}", Stats.Attack, Stats.Defense );
			logger.LogInformation( "경험치: {CurrentExp}/{MaxExp} ({ExpPercent:F1}%)",
				CurrentExp, MaxExp, ExpPercent );
			logger.LogInformation( "골드: {Gold}", Gold );
			logger.LogInformation( "위치: ({X:F2}, {Y:F2}, {Z:F2})",
				Position.PosX, Position.PosY, Position.PosZ );
			logger.LogInformation( "========================================" );
		}
	}

	internal class Program
	{
		public static ServerSession Session;
		public static PacketManager PacketManagerInstance { get; private set; }
		public static DataManager DataManagerInstance { get; private set; }
		private static IServiceProvider _serviceProvider;
		private static ILogger<Program> _logger;

		public static bool AutoAttackEnabled = true;		// 자동 공격 활성화.
		public static float AttackRange = 5.0f;    // 공격 범위(서버와 동일)
		public static float MoveSpeed = 5.0f;      // 이동 속도 (초당 5 유닛)

		// 포션 자동 사용 설정
		public static bool AutoPotionEnabled = true;
		public static float AutoPotionThreshold = 0.5f;	// 50% 이하 시 사용
		public static readonly TimeSpan PotionCooldown = TimeSpan.FromSeconds(5);

		// 포션 정보
		public static int HealthPotionItemId = 1001;

		// 스킬 설정
		public static bool AutoSkillEnabled = false;
		public static int PrimarySkillId = 1;	// 기본 공격 스킬.

		// ping 전송
		private const int PingIntervalSeconds = 20;

		static async Task Main( string[] args )
		{
			var environmentName = Environment.GetEnvironmentVariable("DOTNET_ENVIRONMENT") ?? "Production";
			var builder = new ConfigurationBuilder()
				.SetBasePath(AppDomain.CurrentDomain.BaseDirectory)
				.AddJsonFile("appsettings.json", optional: false, reloadOnChange: true)
				.AddJsonFile($"appsettings.{environmentName}.json", optional: true, reloadOnChange: true)
				.AddEnvironmentVariables();

			IConfiguration configuration = builder.Build();

			// Serilog 설정
			Log.Logger = new LoggerConfiguration()
				.ReadFrom.Configuration( configuration )
				.CreateLogger();

			try
			{
				// DI 컨테이너 설정
				ServiceCollection services = new ServiceCollection();
				ConfigureServices( services, configuration );
				services.AddSingleton<IConfiguration>( configuration );
				services.AddSingleton<AuthApiClient>();
				_serviceProvider = services.BuildServiceProvider();
				_logger = _serviceProvider.GetRequiredService<ILogger<Program>>();

				await RunClientAsync();
			}
			catch(Exception ex)
			{
				Log.Fatal( ex, "DummyClient 실행 중 치명적 오류 발생" );
			}
			finally
			{
				(_serviceProvider as IDisposable)?.Dispose();
				Log.CloseAndFlush();
				//_serviceProvider?.Dispose();
			}
		}

		private static void ConfigureServices( IServiceCollection services, IConfiguration configuration )
		{
			// Configuration 등록
			services.Configure<ClientConfiguration>( configuration.GetSection( "ClientConfiguration" ) );

			// Validator 등록
			services.AddSingleton<IValidateOptions<ClientConfiguration>, ClientConfigurationValidator>();

			// Services 등록
			services.AddSingleton<IClientConfigurationService, ClientConfigurationService>();

			// Logger 등록
			services.AddLogging( builder =>
			{
				builder.ClearProviders();
				builder.AddSerilog();
			} );

			// New PacketManager and Handler Registration
			services.AddSingleton<BaseClientPacketHandler, ClientPacketHandler>();
			services.AddSingleton<PacketManager>();

			services.AddSingleton<GameDataConfig>( sp => new GameDataConfig { DataPath = "GameData" } );
			services.AddSingleton<DataManager>();
		}

		private static async Task RunClientAsync()
		{
			IClientConfigurationService configService = _serviceProvider.GetRequiredService<IClientConfigurationService>();
			ClientConfiguration config = configService.Current;

			// 설정 변경 감지 등록
			configService.RegisterChangeCallBack( newConfig =>
			{
				_logger.LogInformation( "클라이언트 설정이 변경되었습니다: {ServerHost}:{ServerPort}", newConfig.Connection.ServerHost,
					newConfig.Connection.ServerPort );
			} );

			// DataManager 초기화 및 데이터 로드 추가.
			DataManagerInstance = _serviceProvider.GetRequiredService<DataManager>();
			_logger.LogInformation( "게임 데이터 로드 중..." );
			bool loadSuccess = await DataManagerInstance.LoadAllDataAsync();

			if(!loadSuccess)
			{
				_logger.LogError( "게임 데이터 로드 실패. 프로그램을 종료합니다." );
				return;
			}

			_logger.LogInformation( "게임 데이터 로드 완료:" );
			_logger.LogInformation( " - 아이템: {ItemCount}개", DataManagerInstance.GetTotalItemCount() );
			_logger.LogInformation( " - 몬스터: {MonsterCount}개", DataManagerInstance.GetTotalMonsterCount() );
			_logger.LogInformation( " - 스킬: {SkillCount}개", DataManagerInstance.GetTotalSkillCount() );
			_logger.LogInformation( " - 맵: {MapCount}개", DataManagerInstance.GetTotalMapCount() );
			// DataManager 초기화 끝.

			IPEndPoint endPoint = new IPEndPoint(
				IPAddress.Parse(config.Connection.ServerHost),
				config.Connection.ServerPort);

			int clientCount = config.Simulation.ClientCount;
			MoveSpeed = DataManagerInstance.GameConfigData.PlayerDefaultMoveSpeed;

			if(1 < clientCount)
			{
				_logger.LogInformation( "다중 클라이언트 모드 시작 - 클라이언트 수: {ClientCount}", clientCount );
				await RunMultipleClientsAsync( clientCount, endPoint, config );
			}
			else
			{
				_logger.LogInformation( "단일 클라이언트 모드 시작" );
				await RunSingleClientAsync( endPoint, config );
			}
		}

		private static async Task<ServerSession> ConnectAuthenticatedAsync( int clientId, IPEndPoint endPoint,
			ClientConfiguration config, CancellationToken token )
		{
			AuthApiClient auth = _serviceProvider.GetRequiredService<AuthApiClient>();
			for(int attempt = 1; attempt <= config.Connection.MaxReconnectAttempts; attempt++)
			{
				// TCP 시도마다 새 로그인 티켓. REST의 명시적 거부는 이 메서드 밖으로 전파
				string ticket = await auth.AcquireTicketAsync( clientId, token );
				var socket = new Socket( endPoint.AddressFamily, SocketType.Stream, ProtocolType.Tcp );
				ServerSession session = null;
				try
				{
					using(var connect = CancellationTokenSource.CreateLinkedTokenSource( token ))
					{
						connect.CancelAfter( config.Connection.ConnectionTimeoutMs );
						try
						{
							await socket.ConnectAsync( endPoint, connect.Token );
						}
						catch(OperationCanceledException) when(!token.IsCancellationRequested)
						{
							throw new TimeoutException( "TCP connection time out" );
						}
					}

					session = new ServerSession( _serviceProvider.GetRequiredService<ILogger<ServerSession>>(),
						_serviceProvider.GetRequiredService<PacketManager>(), ticket )
					{
						Context = new ClientContext()
					};
					session.Start( socket );
					session.OnConnected( endPoint );
					HandshakeResult login =
						await session.LoginCompletion.WaitAsync( TimeSpan.FromSeconds( 10 ), token );
					if(login == HandshakeResult.Rejected)
						throw new InvalidOperationException( "Game login was rejected." );
					if(login == HandshakeResult.Disconnected)
						throw new IOException( "Connection closed before login confirmation" );

					HandshakeResult entry =
						await session.EntryCompletion.WaitAsync( TimeSpan.FromSeconds( 20 ), token );
					if(entry == HandshakeResult.Rejected)
						throw new InvalidOperationException( "Game entry was rejected." );
					if(entry == HandshakeResult.Disconnected)
						throw new IOException( "Connection closed before entry confirmation." );

					return session;
				}
				catch(Exception ex) when(ex is SocketException or IOException or TimeoutException)
				{
					session?.Close();
					socket.Dispose();
					if(attempt == config.Connection.MaxReconnectAttempts)
						throw;
				}
				catch
				{
					session?.Close();
					socket.Dispose();
					throw;
				}

				await Task.Delay( config.Connection.ReconnectIntervalMs, token );
			}

			throw new InvalidOperationException( "NO TCP connection attempt was configured." );
		}

		private static async Task RunMultipleClientsAsync( int clientCount, IPEndPoint endPoint, ClientConfiguration config )
		{
			_logger.LogInformation( "=== 다중 클라이언트 테스트 시작 ===" );
			_logger.LogInformation( "서버: {EndPoint}, 클라이언트 수: {ClientCount}, 메시지 간격: {IntervalMs}ms",
				endPoint, clientCount, config.Simulation.MessageIntervalMs );

			using var stopping = new CancellationTokenSource();
			var senders = new List<CancellationTokenSource>();
			var tasks = new List<Task>();
			ConsoleCancelEventHandler cancel = ( _, args ) =>
			{
				args.Cancel = true;
				stopping.Cancel();
			};
			Console.CancelKeyPress += cancel;

			try
			{
				// 각 클라이언트를 별도 Task로 실행 (개별 CTS 사용)
				for(int i = 0; i < clientCount; i++)
				{
					var sender = CancellationTokenSource.CreateLinkedTokenSource( stopping.Token );
					senders.Add( sender );
					tasks.Add( RunClientInstanceAsync( i + 1, endPoint, config, sender.Token, stopping.Token ) );
					// 연결 간격 (서버 부하 방지)
					await Task.Delay( 200, stopping.Token );
				}
				
				_logger.LogInformation( "모든 클라이언트 시작 완료." );
				
				// 정상 전송 후 절반은 전송만 중단 - 서버 idle 종료 관찰용.
				await Task.Delay( 20000, stopping.Token );
				for(int i = 0; i < senders.Count / 2; i++)
				{
					_logger.LogInformation( "[테스트] 클라이언트 {ClientId} 패킷 전송 중단(타임아웃 테스트)", i + 1 );
					senders[ i ].Cancel();
				}
				await Task.Delay( 80000, stopping.Token );
			}
			catch(OperationCanceledException) when(stopping.IsCancellationRequested){}
			finally
			{
				stopping.Cancel();
				await Task.WhenAll(tasks);
				foreach(CancellationTokenSource sender in senders)
					sender.Dispose();
				Console.CancelKeyPress -= cancel;
			}

			_logger.LogInformation( "=== 다중 클라이언트 테스트 종료 ===" );
		}

		// 다중 클라이언트 실행
		private static async Task RunClientInstanceAsync( int clientId, IPEndPoint endPoint, ClientConfiguration config, 
			CancellationToken sendingToken, CancellationToken stoppingToken )
		{
			ServerSession session = null;
			try
			{
				// 접속까지는 sending 취소도 반영하되, 연결 후에는 전송 중단과 종료를 구분.
				using(var connecting = CancellationTokenSource.CreateLinkedTokenSource( sendingToken, stoppingToken ))
					session = await ConnectAuthenticatedAsync( clientId, endPoint, config, connecting.Token );

				await Task.Run( () => MainLoop( clientId, session, config, _logger, session.Context, sendingToken ) );
				if(sendingToken.IsCancellationRequested && !stoppingToken.IsCancellationRequested)
					await session.Disconnected.WaitAsync( stoppingToken );
			}
			catch(OperationCanceledException) when(sendingToken.IsCancellationRequested ||
			                                       stoppingToken.IsCancellationRequested) { }
			catch(Exception ex)
			{
				_logger.LogError( "클라이언트 실행 실패. ClientId={ClientId}, ErrorType={ErrorType}", clientId,
					ex.GetType().Name );
			}
			finally
			{
				session?.Close();
			}
		}

		// 단일 클라이언트 실행 (기존 로직을 메서드로 분리)
		private static async Task RunSingleClientAsync( IPEndPoint endPoint, ClientConfiguration config )
		{
			var logger = _serviceProvider.GetRequiredService<ILogger<Program>>();
			int clientId = 1;
			using var stopping = new CancellationTokenSource();
			ConsoleCancelEventHandler cancel = ( _, args ) =>
			{
				args.Cancel = true;
				stopping.Cancel();
			};
			Console.CancelKeyPress += cancel;

			try
			{
				Session = await ConnectAuthenticatedAsync( clientId, endPoint, config, stopping.Token );
				// 통합 MainLoop 호출
				await Task.Run( () => MainLoop( clientId, Session, config, _logger, Session.Context, stopping.Token ) );
			}
			catch(OperationCanceledException) when(stopping.IsCancellationRequested) { }
			finally
			{
				Session?.Close();
				Console.CancelKeyPress -= cancel;
			}

			logger.LogInformation( "클라이언트 {ClientId} 시작", clientId );
		}

		// 통합 메인 루프 (단일/다중 클라이언트 공용)
		private static void MainLoop(int clientId, ServerSession session, ClientConfiguration config,
			ILogger<Program> logger, ClientContext ctx, CancellationToken cancellationToken = default)
		{
			int moveCount = 0;
			int messageInterval = config.Simulation.MessageIntervalMs;

			logger.LogInformation( "[Client {ClientId}] 루프 시작 - 메시지 간격: {IntervalMs}ms", clientId, messageInterval );
			logger.LogInformation( "[Client {ClientId}] 자동 공격 모드: {AutoAttack}", clientId, AutoAttackEnabled ? "활성화" : "비활성화" );

			// 로그 카운터
			int logCounter = 0;
			bool isDungeon = false;

			while(session.IsConnected() && !cancellationToken.IsCancellationRequested)
			{
				try
				{
					if(ctx.CurrentMapData == null)
					{
						Thread.Sleep( messageInterval );
						continue;
					}

					if(!isDungeon && 10 <= moveCount)
					{
						logger.LogInformation( "[Client {ClientId}] 3초 후 던전으로 이동합니다...", clientId );
						Thread.Sleep( 3000 );
						isDungeon = true;
						session.Send( new C_ChangeRoom { RoomType = (int)RoomType.Dungeon, TargetId = 0 } );

						const int ChangeRoomMaxWaitMs = 10000;
						const int PollIntervalMs = 100;
						int waitedMs = 0;
						while(waitedMs < ChangeRoomMaxWaitMs)
						{
							if(ctx.MyPlayer.CurrentMapId == 3)
								break;
							Thread.Sleep( PollIntervalMs );
							waitedMs += PollIntervalMs;
						}

						if(ctx.MyPlayer.CurrentMapId != 3)
						{
							logger.LogWarning( "[Client {ClientId}] 던전 입장 실패 = {WaitedMs}ms 대기 후 timeout. 재시도 허용.",
								clientId, waitedMs );
							isDungeon = false;	// 다음 사이클에서 재시도 가능
							continue;			// moveCount 유지 - 다음 iteration에서 다시 시도.
						}

						logger.LogInformation( "[Client {ClientId}] 던전 입장 완료. (mapId=3)", clientId );
					}

					// ===== 인벤토리 자동 조회 =====
					// 1. 초기 조회 (5초 후 1회)
					if(!ctx.InventoryRequested && 5 <= moveCount)
					{
						C_InventoryRequest inventoryPacket = new C_InventoryRequest();
						session.Send( inventoryPacket );
						ctx.InventoryRequested = true;
						ctx.LastInventoryRequestTime = DateTime.UtcNow;

						logger.LogInformation( "[Client {ClientId}] [Send] C_InventoryRequest - 초기 인벤토리 조회", clientId );

						C_EquipmentRequest equipmentPacket = new C_EquipmentRequest();
						session.Send( equipmentPacket );
						logger.LogInformation( "[Client {ClientId}] [Send] C_EquipmentRequest - 초기 장비 조회", clientId );

					}

					// 2. 주기적 재조회 (30초마다)
					if(ctx.InventoryRequested && 30 <= (DateTime.UtcNow - ctx.LastInventoryRequestTime).TotalSeconds)
					{
						C_InventoryRequest inventoryPacket = new C_InventoryRequest();
						session.Send( inventoryPacket );
						ctx.LastInventoryRequestTime = DateTime.UtcNow;

						logger.LogInformation( "[Client {ClientId}] [Send] C_InventoryRequest - 주기적 조회 (30초)", clientId );

						C_EquipmentRequest equipmentPacket = new C_EquipmentRequest();
						session.Send( equipmentPacket );
						logger.LogInformation( "[Client {ClientId}] [Send] C_EquipmentRequest - 주기적 조회 (30초)", clientId );
					}
					// ===== 인벤토리 자동 조회 끝 =====

					// ===== Ping 자동 전송 =====
					if(PingIntervalSeconds <= (DateTime.UtcNow - ctx.LastPingTime).TotalSeconds)
					{
						session.Send( new C_Ping { Timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() } );
						ctx.LastPingTime = DateTime.UtcNow;
						logger.LogInformation( "[Client {ClientId}] [Send] C_Ping - 서버에 Ping 전송", clientId );
					}
					// ===== Ping 자동 전송 끝 =====

					// ===== 포션 자동 사용 =====
					if(AutoPotionEnabled && 0 <= ctx.HealthPotionInstanceId)
					{
						float hpPercent = ctx.MyPlayer.HPPercent;
						bool cooldownReady = PotionCooldown <= (DateTime.UtcNow - ctx.LastPotionUseTime);

						if(hpPercent < AutoPotionThreshold * 100 && cooldownReady)
						{
							C_UseItem useItemPacket = new C_UseItem
							{
								InstanceId = ctx.HealthPotionInstanceId,
								Quantity = 1
							};

							session.Send( useItemPacket );
							ctx.LastPotionUseTime = DateTime.UtcNow;

							logger.LogWarning( "[Client {ClientId}] [Send] C_UseItem - HP 포션 자동 사용 (HP:{Percent:F1}%)",
								clientId, hpPercent);
						}
					}
					// ===== 포션 자동 끝 =====

					// 1. 주변 오브젝트에 몬스터가 있는지 확인.
					if(ctx.NearbyObjects.Values.Any(o => o.Type == ObjectType.ObjectMonster))
					{
						// 타겟이 없거나, 현재 타겟이 사라졌으면 새로 선택
						if(ctx.TargetMonsterId == 0 || !ctx.NearbyObjects.ContainsKey( ctx.TargetMonsterId ) ||
							ctx.NearbyObjects[ ctx.TargetMonsterId].State == State.Dead)
						{
							// 가장 가까운 몬스터 선택
							// TODO : 현재는 리스트에서 첫번째 몬스터 선택중 수정 필요.
							var aliveMonsters = ctx.NearbyObjects.Values
								.Where(m => m.Type == ObjectType.ObjectMonster && m.State != State.Dead).ToList();

							if(0 < aliveMonsters.Count)
							{
								var nearestMonster = aliveMonsters.First();
								ctx.TargetMonsterId = nearestMonster.ObjectId;

								var packet = new C_AutoMove
								{
									Destination = nearestMonster.PosInfo
								};

								session.Send( packet );
								ctx.LastAutoMoveDestination = nearestMonster.PosInfo;

								logger.LogInformation( "[Client {ClientId}] [타겟 변경] 새 타겟: {Name} (ID:{MonsterId})",
								clientId, nearestMonster.Name, ctx.TargetMonsterId );
							}
							else
							{
								ctx.TargetMonsterId = 0;
							}
						}
					}
					else
					{
						ctx.TargetMonsterId = 0;

						if(logCounter % 10 == 0)
						{
							logger.LogWarning( "[Client {ClientId}] 몬스터가 스폰되지 않았습니다.", clientId );
						}
					}

					// 2. 타겟이 있으면 이동 및 공격 처리.
					if(0 < ctx.TargetMonsterId && ctx.NearbyObjects.TryGetValue( ctx.TargetMonsterId, out var targetMonster ))
					{
						// 몬스터가 일정 거리 이상 이동하면 C_AutoMove 재전송
						if(ctx.LastAutoMoveDestination != null)
						{
							float movedDist = CalculateDistance3D(targetMonster.PosInfo, ctx.LastAutoMoveDestination);
							if(1.0f < movedDist)
							{
								ctx.LastAutoMoveDestination = targetMonster.PosInfo.Clone();
								session.Send( new C_AutoMove { Destination = targetMonster.PosInfo } );
								logger.LogInformation( "[Client {ClientId}] [AutoMove 재전송] 몬스터 이동 감지 ({Dist:F1}m)",
									clientId, movedDist );
							}
						}

						// 타겟과의 거리가 얼마나 되는지 확인. 짧은 거리(CellSize 이하 거리)는 직선 이동.
						float targetX = targetMonster.PosInfo.PosX;
						float targetZ = targetMonster.PosInfo.PosZ;

						// 현재 위치에서 타겟까지 거리 계산
						float distanceToTarget = ctx.MyPlayer.DistanceTo(targetMonster.PosInfo);

						string targetName = targetMonster.Name;

						// 공격 범위 밖 -> 타겟을 향해 이동.
						if(AttackRange < distanceToTarget)
						{
							if( 0 < ctx.AutoMoveWaypoints.Count && ctx.AutoMoveIndex < ctx.AutoMoveWaypoints.Count)
							{
								var waypoint = ctx.AutoMoveWaypoints[ctx.AutoMoveIndex];

								float dx = waypoint.PosX - ctx.MyPlayer.Position.PosX;
								float dz = waypoint.PosZ - ctx.MyPlayer.Position.PosZ;
								float wayDist = CalculateDistance3D(waypoint, ctx.MyPlayer.Position);

								// 웨이포인트 도착
								if(wayDist < 0.5f)
								{
									ctx.AutoMoveIndex++;
									logger.LogInformation( "[Client {ClientId}] [웨이포인트 도달] {Index}/{Total}",
										clientId, ctx.AutoMoveIndex, ctx.AutoMoveWaypoints.Count );
								}
								// 웨이포인트
								else
								{
									float dirX = dx / wayDist;
									float dirZ = dz / wayDist;

									// 이동 거리 계산 (초당 MoveSpeed)
									float moveDistance = MoveSpeed * (messageInterval / 1000.0f);
									moveDistance = Math.Min( moveDistance, wayDist );

									float newPosX = ctx.MyPlayer.Position.PosX + dirX * moveDistance;
									float newPosZ = ctx.MyPlayer.Position.PosZ + dirZ * moveDistance;

									// 회전 방향 계산 (타겟을 바라보도록)
									ctx.MyPlayer.Position.RotationY = (float)Math.Atan2( dirX, dirZ ) * (180f / (float)Math.PI);

									// 높이 계산
									float newHeight = ctx.CurrentMapData.GetWorldHeight(newPosX, newPosZ);

									ctx.MyPlayer.Position.PosX = newPosX;
									ctx.MyPlayer.Position.PosZ = newPosZ;
									ctx.MyPlayer.Position.PosY = newHeight;

									C_Move movePacket = new C_Move()
									{
										PosInfo = new PosInfo()
										{
											PosX = ctx.MyPlayer.Position.PosX,
											PosY = ctx.MyPlayer.Position.PosY,
											PosZ = ctx.MyPlayer.Position.PosZ,
											RotationX = ctx.MyPlayer.Position.RotationX,
											RotationY = ctx.MyPlayer.Position.RotationY,
											RotationZ = ctx.MyPlayer.Position.RotationZ,
											Timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()
										},
									};
									session.Send( movePacket );

									if(moveCount % 5 == 0)
									{
										logger.LogInformation( "[Client {ClientId}] [이동] → {Target} | 거리: {Distance:F2}m | 위치: ({X:F2}, {Y:F2},{Z:F2}) | 내 HP: {HP:F1}%",
											clientId, targetName, distanceToTarget,
											ctx.MyPlayer.Position.PosX, ctx.MyPlayer.Position.PosY, ctx.MyPlayer.Position.PosZ, ctx.MyPlayer.HPPercent );
									}
								}
							}
							else if ( 0 < ctx.AutoMoveWaypoints.Count && ctx.AutoMoveWaypoints.Count <= ctx.AutoMoveIndex)
							{
								logger.LogInformation( "[Client {ClientId}] [경로 완료] 웨이포인트 전체 소진", clientId );
								ctx.AutoMoveIndex = 0;
								ctx.AutoMoveWaypoints.Clear();
								ctx.LastAutoMoveDestination = null;
							}

						}
						// 공격 범위 내 -> 정지 후 공격
						else
						{
							// 공격(2초마다)
							if(AutoAttackEnabled && moveCount % 2 == 0 && 0 < ctx.TargetMonsterId)
							{
								// 이동 데이터 제거
								ctx.AutoMoveWaypoints.Clear();
								ctx.AutoMoveIndex = 0;
								ctx.LastAutoMoveDestination = null;

								// 스킬 시스템 사용
								int currentMP = ctx.MyPlayer.Stats.CurrentMP;
								int skillId = SelectAttackSkill(currentMP, ctx, logger);

								if(0 < skillId)
								{
									C_UseSkill useSkillPacket = new C_UseSkill
									{
										SkillId = skillId,
										TargetId = ctx.TargetMonsterId
									};

									session.Send( useSkillPacket );
									ctx.SkillCooldowns[ skillId ] = DateTime.UtcNow;

									var skillData = DataManagerInstance.GetSkill(skillId);
									logger.LogInformation( "[Client {ClientId}] [Send] C_UseSkill - {SkillName}(ID:{SkillId}) -> {Target}(MP:-{ManaCost}, 쿨다운:{Cooldown}초)",
										clientId, skillData?.Name ?? "Unknown", skillId, targetName, skillData?.ManaCost ?? 0, skillData?.CooldownSeconds ?? 0 );
								}
								else
								{
									logger.LogDebug( "[Client {ClientId}] 스킬 사용 불가", clientId );
								}
							}
							else
							{
								// 기존 일반 공격
								var attackPacket = new C_UseSkill
								{
									TargetId = ctx.TargetMonsterId,
									SkillId = 0
								};

								session.Send( attackPacket );

								logger.LogInformation( "[Client {ClientId}] [공격] {Target} | 거리: {Distance:F2}m | 내 HP: {MyHP:F1}% | 타겟 HP: {TargetHP}/{MaxHP}",
									clientId, targetName, distanceToTarget, ctx.MyPlayer.HPPercent, targetMonster.StatInfo.CurrentHP, targetMonster.StatInfo.MaxHP );
							}

							// 공격 범위 내에서는 이동하지 않음 (위치 패킷 전송 안 함)
							if(moveCount % 10 == 0)
							{
								logger.LogDebug( "[Client {ClientId}] [대기] 공격 범위 내 정지 | 거리: {Distance:F2}m | HP: {HP:F1}%",
									clientId, distanceToTarget, ctx.MyPlayer.HPPercent );
							}
						}
					}
					// 3. 타겟이 없으면 Room 중앙으로 천천히 이동
					else
					{
						if(ctx.LastAutoMoveDestination == null)
						{
							// 랜덤 목표
							float destX = Random.Shared.NextSingle() * ctx.CurrentMapData.Width * ctx.CurrentMapData.CellSize;
							float destZ = Random.Shared.NextSingle() % ctx.CurrentMapData.Depth * ctx.CurrentMapData.CellSize;
							float destY = ctx.CurrentMapData.GetWorldHeight( destX, destZ );

							var packet = new C_AutoMove
							{
								Destination = new PosInfo
								{
									PosX = destX,
									PosY = destY,
									PosZ = destZ
								}
							};

							session.Send( packet );
							logger.LogInformation( "[Client {ClientId}] [AutoMove 랜덤 포인트 설정] 랜덤 포인트 : ({X},{Y},{Z})",
									clientId, destX, destY, destZ );
						}

						if(0 < ctx.AutoMoveWaypoints.Count && ctx.AutoMoveIndex < ctx.AutoMoveWaypoints.Count)
						{
							var waypoint = ctx.AutoMoveWaypoints[ctx.AutoMoveIndex];

							float dx = waypoint.PosX - ctx.MyPlayer.Position.PosX;
							float dz = waypoint.PosZ - ctx.MyPlayer.Position.PosZ;
							float wayDist = CalculateDistance3D(waypoint, ctx.MyPlayer.Position);

							if(wayDist < 0.5f)
							{
								ctx.AutoMoveIndex++;
							}
							else
							{
								float dirX = dx / wayDist;
								float dirZ = dz / wayDist;

								// 이동 거리 계산 (초당 MoveSpeed)
								float moveDistance = MoveSpeed * (messageInterval / 1000.0f);
								moveDistance = Math.Min( moveDistance, wayDist );

								float newPosX = ctx.MyPlayer.Position.PosX + dirX * moveDistance;
								float newPosZ = ctx.MyPlayer.Position.PosZ + dirZ * moveDistance;

								// 회전 방향 계산 (타겟을 바라보도록)
								ctx.MyPlayer.Position.RotationY = (float)Math.Atan2( dirX, dirZ ) * (180f / (float)Math.PI);

								// 높이 계산
								float newHeight = ctx.CurrentMapData.GetWorldHeight(newPosX, newPosZ);

								ctx.MyPlayer.Position.PosX = newPosX;
								ctx.MyPlayer.Position.PosZ = newPosZ;
								ctx.MyPlayer.Position.PosY = newHeight;

								C_Move movePacket = new C_Move()
								{
									PosInfo = new PosInfo()
									{
										PosX = ctx.MyPlayer.Position.PosX,
										PosY = ctx.MyPlayer.Position.PosY,
										PosZ = ctx.MyPlayer.Position.PosZ,
										RotationX = ctx.MyPlayer.Position.RotationX,
										RotationY = ctx.MyPlayer.Position.RotationY,
										RotationZ = ctx.MyPlayer.Position.RotationZ,
										Timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()
									},
								};
								session.Send( movePacket );

								if(moveCount % 10 == 0)
								{
									logger.LogDebug( "[Client {ClientId}] [이동] 랜덤 위치로 이동 중... 거리: {Distance:F2}m",
										clientId, wayDist );
								}
							}
						}
						else if(0 < ctx.AutoMoveWaypoints.Count && ctx.AutoMoveWaypoints.Count <= ctx.AutoMoveIndex)
						{
							logger.LogInformation( "[Client {ClientId}] [경로 완료] 웨이포인트 전체 소진", clientId );
							ctx.AutoMoveIndex = 0;
							ctx.AutoMoveWaypoints.Clear();
							ctx.LastAutoMoveDestination = null;
						}
					}


					// 10초마다 채팅
					if(moveCount % 10 == 0)
					{
						C_Chat chatPacket = new C_Chat() {Message = $"[Client-{clientId:D2}] 안녕하세요! {moveCount / 5}번째 채팅입니다." };
						session.Send( chatPacket );
						logger.LogInformation( "[Client {ClientId}] [Send] C_Chat: {Message}", clientId, chatPacket.Message );
					}

					moveCount++;
					logCounter++;

					// CancellationToken 지원
					try
					{
						Task.Delay( messageInterval, cancellationToken ).Wait();
					}
					catch(OperationCanceledException)
					{
						break;
					}
				}
				catch(Exception ex)
				{
					logger.LogError( ex, "[Client {ClientId}] MainLoop 중 오류 발생", clientId );
					break;
				}
			}
		}

		/// <summary>
		/// 3D 거리 계산
		/// </summary>
		private static float CalculateDistance3D( PosInfo pos1, PosInfo pos2 )
		{
			if(pos1 == null || pos2 == null)
				return float.MaxValue;

			float deltaX = pos1.PosX - pos2.PosX;
			float deltaY = pos1.PosY - pos2.PosY;
			float deltaZ = pos1.PosZ - pos2.PosZ;

			return (float)Math.Sqrt( deltaX * deltaX + deltaY * deltaY + deltaZ * deltaZ );
		}

		private static int SelectAttackSkill(int currentMP, ClientContext ctx, Microsoft.Extensions.Logging.ILogger logger)
		{
			if(DataManagerInstance == null || !DataManagerInstance.IsDataLoaded)
			{
				logger.LogWarning( "[Skill] 게임 데이터가 로드되지 않았습니다." );
				return 0;
			}

			DateTime now = DateTime.UtcNow;

			// 공격 스킬을 데미지 내림차순으로 가져오기
			var attackSkills = DataManagerInstance.GetSkillsByType("Attack")
				.OrderByDescending(s => s.Damage);

			foreach(var skill in attackSkills)
			{
				// MP 체크
				if(currentMP < skill.ManaCost)
				{
					logger.LogDebug( "[Skill] {SkillName}(ID:{SkillId}) - MP 부족 (필요:{Required}, 현재:{Current})",
								skill.Name, skill.Id, skill.ManaCost, currentMP );
					continue;
				}

				// 쿨다운 체크
				if(ctx.SkillCooldowns.TryGetValue(skill.Id, out DateTime lastUseTime))
				{
					TimeSpan cooldown = TimeSpan.FromSeconds(skill.CooldownSeconds);
					TimeSpan elapsed = now - lastUseTime;

					if(elapsed < cooldown)
					{
						TimeSpan remaining = cooldown - elapsed;
						logger.LogDebug( "[Skill] {SkillName}(ID:{SkillId}) - 쿨다운중 (남은:{Remaining:F1}초)",
							skill.Name, skill.Id, remaining.TotalSeconds );
						continue;
					}
				}

				// 사용 가능
				logger.LogDebug("[Skill] {SkillName}(ID:{SkillId}) 선택 (데미지:{Damage}, MP:{ManaCost})",
					skill.Name, skill.Id, skill.Damage, skill.ManaCost );
				return skill.Id;
			}

			logger.LogDebug( "[Skill] 사용 가능한 스킬 없음" );
			return 0;
		}
	}
}
