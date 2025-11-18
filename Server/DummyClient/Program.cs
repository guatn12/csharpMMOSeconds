using System;
using System.Net;
using System.Net.Sockets;
using System.Text;
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
		public int Level { get; set; }

		public PlayerStats Stats { get; set; }

		// 경험치
		public long CurrentExp { get; set; } = 0;
		public long MaxExp { get; set; } = 100;
		public long Gold {  get; set; } = 0;

		public PosInfo Position { get; set; }

		// 전투 관련 계산 프로퍼티
		public bool IsAlive => 0 < Stats.CurrentHP;
		public float HPPercent => 0 < Stats.MaxHP ? (float)Stats.CurrentHP / Stats.MaxHP * 100 : 0;
		public float MPPercent => 0 < Stats.MaxMP ? (float)Stats.CurrentMP / Stats.MaxMP * 100 : 0;
		public float ExpPercent => 0 < MaxExp ? (float)CurrentExp / MaxExp * 100 : 0;

		public ClientPlayerInfo()
		{
			// Stats 초기화
			Stats = new PlayerStats
			{
				Attack = 10,
				Defense = 5,
				MaxHP = 100,
				MaxMP = 50,
				CurrentHP = 100,
				CurrentMP = 50
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
		private static IServiceProvider _serviceProvider;
		private static ILogger<Program> _logger;

		// 플레이어 정보 추가
		public static ClientPlayerInfo MyPlayer = new ClientPlayerInfo();

		public static Dictionary<long, ClientPlayerInfo> Players = new();

		// 몬스터 추적
		public static Dictionary<long, MonsterInfo> NearbyMonsters = new Dictionary<long, MonsterInfo>();

		public static long TargetMonsterId = 0;
		public static bool AutoAttackEnabled = true;		// 자동 공격 활성화.
		public static readonly float AttackRange = 5.0f;    // 공격 범위(서버와 동일)
		public static readonly float MoveSpeed = 5.0f;      // 이동 속도 (초당 5 유닛)

		// 포션 자동 사용 설정
		public static bool AutoPotionEnabled = true;
		public static float AutoPotionThreshold = 0.5f;	// 50% 이하 시 사용
		public static DateTime LastPotionUseTime = DateTime.MinValue;
		public static readonly TimeSpan PotionCooldown = TimeSpan.FromSeconds(5);

		// 포션 정보
		public static int HealthPotionSlot = -1;
		public static int HealthPotionItemId = 1001;

		// 인벤토리 자동 조회
		public static bool InventoryRequested = false;
		public static DateTime LastInventoryRequestTime = DateTime.MinValue;

		static void Main( string[] args )
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
				_serviceProvider = services.BuildServiceProvider();
				_logger = _serviceProvider.GetRequiredService<ILogger<Program>>();

				RunClient();
			}
			catch(Exception ex)
			{
				Log.Fatal( ex, "DummyClient 실행 중 치명적 오류 발생" );
			}
			finally
			{
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
		}

		private static void RunClient()
		{
			IClientConfigurationService configService = _serviceProvider.GetRequiredService<IClientConfigurationService>();
			ClientConfiguration config = configService.Current;

			// 설정 변경 감지 등록
			configService.RegisterChangeCallBack( newConfig =>
			{
				_logger.LogInformation( "클라이언트 설정이 변경되었습니다: {ServerHost}:{ServerPort}", newConfig.Connection.ServerHost,
					newConfig.Connection.ServerPort );
			} );

			IPEndPoint endPoint = new IPEndPoint(
				IPAddress.Parse(config.Connection.ServerHost),
				config.Connection.ServerPort);

			int clientCount = config.Simulation.ClientCount;

			if(1 < clientCount)
			{
				_logger.LogInformation( "다중 클라이언트 모드 시작 - 클라이언트 수: {ClientCount}", clientCount );
				RunMultipleClients( clientCount, endPoint, config );
			}
			else
			{
				_logger.LogInformation( "단일 클라이언트 모드 시작" );
				RunSingleClient( endPoint, config );
			}
		}

		private static void RunMultipleClients( int clientCount, IPEndPoint endPoint, ClientConfiguration config )
		{
			_logger.LogInformation( "=== 다중 클라이언트 테스트 시작 ===" );
			_logger.LogInformation( "서버: {EndPoint}, 클라이언트 수: {ClientCount}, 메시지 간격: {IntervalMs}ms",
				endPoint, clientCount, config.Simulation.MessageIntervalMs );

			List<Task> clientTasks = new List<Task>();
			CancellationTokenSource cancellationTokenSource = new CancellationTokenSource();

			// 각 클라이언트를 별도 Task로 실행
			for(int i = 0; i < clientCount; i++)
			{
				int clientId = i + 1;
				Task clientTask = Task.Run(() => RunClientInstance(clientId, endPoint, config, cancellationTokenSource.Token));
				clientTasks.Add( clientTask );

				// 연결 간격 (서버 부하 방지)
				Thread.Sleep( 200 );
			}

			_logger.LogInformation( "모든 클라이언트 시작 완료. 15초 후 자동 종료됩니다..." );

			// 15초 대기 (테스트를 위해)
			Thread.Sleep( 15000 );

			cancellationTokenSource.Cancel();

			bool allCompleted = Task.WaitAll(clientTasks.ToArray(), TimeSpan.FromSeconds(5));

			if(!allCompleted)
			{
				_logger.LogWarning( "일부 클라이언트가 정상 종료되지 않았습니다." );
			}

			_logger.LogInformation( "=== 다중 클라이언트 테스트 종료 ===" );
		}

		// 다중 클라이언트 실행
		private static void RunClientInstance( int clientId, IPEndPoint endPoint, ClientConfiguration config, CancellationToken cancellationToken )
		{
			try
			{
				var logger = _serviceProvider.GetRequiredService<ILogger<Program>>();
				logger.LogInformation( "클라이언트 {ClientId} 시작", clientId );

				// PacketManager를 DI 컨테이너에서 직접 가져옴
				var packetManager = _serviceProvider.GetRequiredService<PacketManager>();

				// 연결 재시도 로직(최대 5초, 1초마다 재시도)
				int maxRetries = 5;
				int retryCount = 0;
				bool connected = false;

				Connector connector = null;

				while(retryCount < maxRetries && !cancellationToken.IsCancellationRequested)
				{
					retryCount++;

					if(connector != null)
					{
						try
						{
							connector = null;
						}
						catch { }
					}

					logger.LogInformation( "클라이언트 {ClientId} 연결 시도 {RetryCount}/{MaxRetries}...",
						clientId, retryCount, maxRetries );

					// connector 생성.
					connector = new Connector();

					connector.Connect( endPoint, () =>
					{
						var sessionLogger = _serviceProvider.GetRequiredService<ILogger<ServerSession>>();
						// ServerSession 생성자에 DI에서 관리되는 PacketManager 인스턴스 주입
						return new ServerSession( sessionLogger, packetManager );
					} );

					// 연결 완료 대기(최대 1초)
					bool signaled = connector.ConnectDone.WaitOne(3000);

					if(cancellationToken.IsCancellationRequested)
					{
						logger.LogInformation( "클라이언트 {ClientId} 연결 취소됨.", clientId );
						return;
					}

					if(signaled && connector.IsConnected)
					{
						connected = true;
						logger.LogInformation( "클라이언트 {ClientId} 연결 성공 ({RetryCount} 번째 시도)",
							clientId, retryCount );
						break;
					}
					else if(signaled)
					{
						logger.LogWarning( "클라이언트 {ClientId} 연결 실패: {Error} - 재시도 대기...",
							clientId, connector.LastError );
					}
					else
					{
						logger.LogWarning( "클라이언트 {ClientId} 연결 타임아웃 - 재시도 대기...", clientId );
					}

					// 재시도 간격(마지막 시도가 아닐 때만)
					if(retryCount < maxRetries && !cancellationToken.IsCancellationRequested)
					{
						Thread.Sleep( 1500 );
					}
				}

				if(cancellationToken.IsCancellationRequested)
				{
					logger.LogInformation( "클라이언트 {ClientId} 연결 취소됨.", clientId );
					return;
				}

				if(!connected)
				{
					logger.LogError( "클라이언트 {ClientId} 연결 실패 - {MaxRetries}초 타임아웃",
							clientId, maxRetries );
					return;
				}

				logger.LogInformation( "클라이언트 {ClientId} 연결 성공", clientId );

				if(connector.ConnectionSession == null)
				{
					logger.LogError( "클라이언트 {ClientId} 연결 성공했지만 세션이 null 입니다.", clientId );
					return;
				}

				//개별 클라이언트 루프 실행 (통합 MainLoop 호출)
				MainLoop( clientId, (ServerSession)connector.ConnectionSession, config, logger, cancellationToken );
			}
			catch(Exception ex)
			{
				var logger = _serviceProvider.GetService<ILogger<Program>>();
				logger.LogError( ex, "클라이언트 {ClientId} 실행 중 오류", clientId );
			}
		}

		// 단일 클라이언트 실행 (기존 로직을 메서드로 분리)
		private static void RunSingleClient( IPEndPoint endPoint, ClientConfiguration config )
		{
			var logger = _serviceProvider.GetRequiredService<ILogger<Program>>();
			long clientId = 1;
			logger.LogInformation( "클라이언트 {ClientId} 시작", clientId );

			// 연결 재시도 로직(최대 5초, 1초마다 재시도)
			int maxRetries = 5;
			int retryCount = 0;
			bool connected = false;

			Connector connector = null;

			while(retryCount < maxRetries )
			{
				retryCount++;

				if(connector != null)
				{
					try
					{
						connector = null;
					}
					catch { }
				}

				logger.LogInformation( "클라이언트 {ClientId} 연결 시도 {RetryCount}/{MaxRetries}...",
					clientId, retryCount, maxRetries );

				// connector 생성.
				connector = new Connector();

				connector.Connect( endPoint, () =>
				{
					var sessionLogger = _serviceProvider.GetRequiredService<ILogger<ServerSession>>();
					PacketManager packetManager = _serviceProvider.GetRequiredService<PacketManager>();
					// ServerSession 생성자에 DI에서 관리되는 PacketManager 인스턴스 주입
					return new ServerSession( sessionLogger, packetManager ); 
				} );

				// 연결 완료 대기(최대 3초)
				bool signaled = connector.ConnectDone.WaitOne(3000);

				if(signaled && connector.IsConnected)
				{
					Session = (ServerSession)connector.ConnectionSession;
					connected = true;
					logger.LogInformation( "클라이언트 {ClientId} 연결 성공 ({RetryCount} 번째 시도)",
						clientId, retryCount );
					break;
				}
				else if(signaled)
				{
					logger.LogWarning( "클라이언트 {ClientId} 연결 실패: {Error} - 재시도 대기...",
						clientId, connector.LastError );
				}
				else
				{
					logger.LogWarning( "클라이언트 {ClientId} 연결 타임아웃 - 재시도 대기...", clientId );
				}

				// 재시도 간격(마지막 시도가 아닐 때만)
				if(retryCount < maxRetries)
				{
					Thread.Sleep( 1500 );
				}
			}

			if(!connected)
			{
				logger.LogError( "클라이언트 {ClientId} 연결 실패 - {MaxRetries}초 타임아웃",
						clientId, maxRetries );
				return;
			}

			logger.LogInformation( "클라이언트 {ClientId} 연결 성공", clientId );

			// 연결된 session 확인 (전역 Session 사용)
			if(Session == null)
			{
				logger.LogError( "클라이언트 {ClientId} 세션이 null입니다.", clientId );
				return;
			}

			// 통합 MainLoop 호출
			MainLoop( (int)clientId, Session, config, logger );
		}

		// 통합 메인 루프 (단일/다중 클라이언트 공용)
		private static void MainLoop(int clientId, ServerSession session, ClientConfiguration config,
			ILogger<Program> logger, CancellationToken cancellationToken = default)
		{
			int moveCount = 0;
			int messageInterval = config.Simulation.MessageIntervalMs;

			logger.LogInformation( "[Client {ClientId}] 루프 시작 - 메시지 간격: {IntervalMs}ms", clientId, messageInterval );
			logger.LogInformation( "[Client {ClientId}] 자동 공격 모드: {AutoAttack}", clientId, AutoAttackEnabled ? "활성화" : "비활성화" );

			// 로그 카운터
			int logCounter = 0;

			while(session.IsConnected() && !cancellationToken.IsCancellationRequested)
			{
				try
				{

					// ===== 인벤토리 자동 조회 =====
					// 1. 초기 조회 (5초 후 1회)
					if(!InventoryRequested && 5 <= moveCount)
					{
						C_InventoryRequest inventoryPacket = new C_InventoryRequest();
						session.Send( inventoryPacket );
						InventoryRequested = true;
						LastInventoryRequestTime = DateTime.UtcNow;

						logger.LogInformation( "[Client {ClientId}] [Send] C_InventoryRequest - 초기 인벤토리 조회", clientId );
					}

					// 2. 주기적 재조회 (30초마다)
					if(InventoryRequested && 30 <= (DateTime.UtcNow - LastInventoryRequestTime).TotalSeconds)
					{
						C_InventoryRequest inventoryPacket = new C_InventoryRequest();
						session.Send( inventoryPacket );
						LastInventoryRequestTime = DateTime.UtcNow;

						logger.LogInformation( "[Client {ClientId}] [Send] C_InventoryRequest - 주기적 조회 (30초)", clientId );
					}
					// ===== 인벤토리 자동 조회 끝 =====

					// ===== 포션 자동 사용 =====
					if(AutoPotionEnabled && 0 <= HealthPotionSlot)
					{
						float hpPercent = MyPlayer.HPPercent;
						bool cooldownReady = PotionCooldown <= (DateTime.UtcNow - LastPotionUseTime);

						if(hpPercent < AutoPotionThreshold * 100 && cooldownReady)
						{
							C_UseItem useItemPacket = new C_UseItem
							{
								Slot = HealthPotionSlot,
								Quantity = 1
							};

							session.Send( useItemPacket );
							LastPotionUseTime = DateTime.UtcNow;

							logger.LogWarning( "[Client {ClientId}] [Send] C_UseItem - HP 포션 자동 사용 (HP:{Percent: F1}%)",
								clientId, hpPercent);
						}
					}
					// ===== 포션 자동 끝 =====

					// 1. 타겟 몬스터 선택 및 위치 업데이트
					if(0 < NearbyMonsters.Count)
					{
						// 타겟이 없거나, 현재 타겟이 사라졌으면 새로 선택
						if(TargetMonsterId == 0 || !NearbyMonsters.ContainsKey( TargetMonsterId ))
						{
							// 가장 가까운 몬스터 선택
							var aliveMonsters = NearbyMonsters.Values
								.Where(m => m.State != MonsterState.MonsterDie).ToList();

							if(0 < aliveMonsters.Count)
							{
								var nearestMonster = aliveMonsters.First();
								TargetMonsterId = nearestMonster.MonsterId;

								logger.LogInformation( "[Client {ClientId}] [타겟 변경] 새 타겟: {Name} (ID:{MonsterId})",
								clientId, nearestMonster.Name, TargetMonsterId );
							}
							else
							{
								TargetMonsterId = 0;
							}
						}
					}
					else
					{
						TargetMonsterId = 0;

						if(logCounter % 10 == 0)
						{
							logger.LogWarning( "[Client {ClientId}] 몬스터가 스폰되지 않았습니다.", clientId );
						}
					}

					// 2. 타겟이 있으면 이동 및 공격 처리.
					if(0 < TargetMonsterId && NearbyMonsters.TryGetValue( TargetMonsterId, out var targetMonster ))
					{
						float targetX = targetMonster.PosInfo.PosX;
						float targetZ = targetMonster.PosInfo.PosZ;

						// 현재 위치에서 타겟까지 거리 계산
						float distanceToTarget = MyPlayer.DistanceTo(targetMonster.PosInfo);

						string targetName = targetMonster.Name;

						// 공격 범위 밖 -> 타겟을 향해 이동.
						if(AttackRange < distanceToTarget)
						{
							float dx = targetX - MyPlayer.Position.PosX;
							float dz = targetZ - MyPlayer.Position.PosZ;
							float distance2D = (float)Math.Sqrt(dz * dz + dx * dx);

							// 방향 벡터 정규화
							float dirX = dx / distanceToTarget;
							float dirZ = dz / distanceToTarget;

							// 이동 거리 계산 (초당 MoveSpeed)
							float moveDistance = MoveSpeed * (messageInterval / 1000.0f);

							// 목표 위치보다 가까우면 목표 위치로, 아니면 moveDistance만큼 이동.
							if(distanceToTarget <= moveDistance)
							{
								MyPlayer.Position.PosX = targetX;
								MyPlayer.Position.PosZ = targetZ;
							}
							else
							{
								MyPlayer.Position.PosX += dirX * moveDistance;
								MyPlayer.Position.PosZ += dirZ * moveDistance;
							}

							// Y 좌표를 타겟 몬스터와 동일하게 설정.
							MyPlayer.Position.PosY = targetMonster.PosInfo.PosY;

							// 회전 방향 계산 (타겟을 바라보도록)
							MyPlayer.Position.RotationY = (float)Math.Atan2( dirX, dirZ ) * (180f / (float)Math.PI);

							// 이동 패킷 전송
							C_Move movePacket = new C_Move()
							{
								PosInfo = new PosInfo()
								{
									PosX = MyPlayer.Position.PosX,
									PosY = MyPlayer.Position.PosY,
									PosZ = MyPlayer.Position.PosZ,
									RotationX = MyPlayer.Position.RotationX,
									RotationY = MyPlayer.Position.RotationY,
									RotationZ = MyPlayer.Position.RotationZ,
									Timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()
								},
							};
							session.Send( movePacket );

							if(moveCount % 5 == 0)
							{
								logger.LogInformation( "[Client {ClientId}] [이동] → {Target} | 거리: {Distance:F2}m | 위치: ({X:F2}, {Y:F2},{Z:F2}) | 내 HP: {HP:F1}%",
									clientId, targetName, distanceToTarget,
									MyPlayer.Position.PosX, MyPlayer.Position.PosY, MyPlayer.Position.PosZ, MyPlayer.HPPercent );
							}
						}
						// 공격 범위 내 -> 정지 후 공격
						else
						{
							// 공격(2초마다)
							if(AutoAttackEnabled && moveCount % 2 == 0)
							{
								var attackPacket = new C_AttackMonster
								{
									MonsterId = TargetMonsterId,
									SkillId = 0
								};

								session.Send( attackPacket );

								logger.LogInformation( "[Client {ClientId}] [공격] {Target} | 거리: {Distance:F2}m | 내 HP: {MyHP:F1}% | 타겟 HP: {TargetHP}/{MaxHP}",
									clientId, targetName, distanceToTarget, MyPlayer.HPPercent, targetMonster.CurrentHP, targetMonster.MaxHP );
							}

							// 공격 범위 내에서는 이동하지 않음 (위치 패킷 전송 안 함)
							if(moveCount % 10 == 0)
							{
								logger.LogDebug( "[Client {ClientId}] [대기] 공격 범위 내 정지 | 거리: {Distance:F2}m | HP: {HP:F1}%",
									clientId, distanceToTarget, MyPlayer.HPPercent );
							}
						}
					}
					// 3. 타겟이 없으면 Room 중앙으로 천천히 이동
					else
					{
						float centerX = 50.0f;
						float centerY = 0.0f;
						float centerZ = 50.0f;

						float dx = centerX - MyPlayer.Position.PosX;
						float dz = centerZ - MyPlayer.Position.PosZ;
						float distanceToCenter = (float)Math.Sqrt( dx * dx + dz * dz );

						if(1.0f < distanceToCenter) // 중앙에서 1M 이상 떨어진 경우
						{
							float dirX = dx / distanceToCenter;
							float dirZ = dz / distanceToCenter;

							float moveDisntace = MoveSpeed * 0.5f * (messageInterval / 1000.0f); // 절반 속도

							MyPlayer.Position.PosX += dirX * moveDisntace;
							MyPlayer.Position.PosY = centerY;
							MyPlayer.Position.PosZ += dirZ * moveDisntace;
							MyPlayer.Position.RotationY = (float)Math.Atan2( dirX, dirZ ) * (180f / (float)Math.PI);

							C_Move movePacket = new C_Move()
							{
								PosInfo = new PosInfo()
								{
									PosX = MyPlayer.Position.PosX,
									PosY = MyPlayer.Position.PosY,
									PosZ = MyPlayer.Position.PosZ,
									RotationX = MyPlayer.Position.RotationX,
									RotationY = MyPlayer.Position.RotationY,
									RotationZ = MyPlayer.Position.RotationZ,
									Timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()
								}
							};
							session.Send( movePacket );

							if(moveCount % 10 == 0)
							{
								logger.LogDebug( "[Client {ClientId}] [이동] Room 중앙으로 이동 중... 거리: {Distance:F2}m",
									clientId, distanceToCenter );
							}
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
	}
}