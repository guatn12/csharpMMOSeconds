using Microsoft.Extensions.Logging;
using Protocol;
using Server.Data;
using Server.Data.Models;
using Server.Game.Objects;
using Server.Room;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace Server.Game.Monsters
{
	/// <summary>
	/// 몬스터 스폰 지점 정보
	/// </summary>
	public class SpawnPoint
	{
		public int TemplateId { get; set; }				// 몬스터 템플릿 ID
		public PosInfo Position { get; set; }			// 스폰 위치
		public DateTime LastSpawnTime { get; set; }		// 마지막 스폰 시간
		public long CurrentMonsterId { get; set; }		// 현재 스폰된 몬스터 ID
		public TimeSpan RespawnInterval { get; set; }	// 리스폰 대기 시간

		public SpawnPoint(int templateId, PosInfo position, TimeSpan respawnInterval)
		{
			TemplateId = templateId;
			Position = position;
			RespawnInterval = respawnInterval;
			LastSpawnTime = DateTime.MinValue;
			CurrentMonsterId = 0;
		}

		public bool CanSpawn()
		{
			return CurrentMonsterId == 0 &&
				RespawnInterval <= DateTime.UtcNow - LastSpawnTime;
		}
	}

	/// <summary>
	/// 몬스터 스폰 및 관리 시스템
	/// </summary>
	public class MonsterSpawner
	{
		private readonly IRoom _room;
		private readonly DataManager _dataManager;
		private readonly ObjectManager _objectManager;
		private readonly ILogger _logger;

		// 스폰 포인트 및 몬스터 관리
		private readonly List<SpawnPoint> _spawnPoints = new List<SpawnPoint>();
		private readonly HashSet<long> _monsterIds = new HashSet<long>();
		private readonly ConcurrentDictionary<long, MonsterAI> _monsterAis = new ConcurrentDictionary<long, MonsterAI>();

		// 스폰 설정
		private int _maxMonsters = 50;
		private readonly TimeSpan _defaultRespawnInterval = TimeSpan.FromSeconds(5);

		// 이벤트 : Room에서 Despawn 알림
		public event Action<long, Monster> OnMonsterDespawned;
		public event Action<Monster> OnMonsterSpawned;

		public int MonsterCount => _monsterIds.Count;
		public int SpawnPointCount => _spawnPoints.Count;

		public MonsterSpawner(IRoom room, DataManager dataManager, ObjectManager objectManager, ILogger logger)
		{
			_room = room;
			_dataManager = dataManager;
			_objectManager = objectManager;
			_logger = logger;
		}

		/// <summary>
		/// 스폰 포인트 추가
		/// </summary>
		public void AddSpawnPoint(int templateId, PosInfo position, TimeSpan? respawnInterval = null)
		{
			if(position == null)
				throw new ArgumentNullException(nameof(position));

			// 템플릿 데이터 검증
			MonsterData monsterData = _dataManager.GetMonster(templateId);
			if(monsterData == null)
			{
				_logger.LogError( "Failed to add spawn point: Monster template {TemplateId} not found", templateId );
				return;
			}

			var spawnPoint = new SpawnPoint(
				templateId,
				position,
				respawnInterval ?? _defaultRespawnInterval);

			_spawnPoints.Add(spawnPoint);

			_logger.LogInformation("Spawn point added: Template={TemplateId} at ({X}, {Y}, {Z})",
				templateId, position.PosX, position.PosY, position.PosZ);
		}

		/// <summary>
		/// 초기 몬스터 스폰 (Room 초기화 시 호출)
		/// </summary>
		public void SpawnInitialMonsters()
		{
			_logger.LogInformation( "Spawning initial monster for room {RoomId}...", _room.RoomId );

			int spawnedCount = 0;
			foreach(var spawnPoint in _spawnPoints)
			{
				if(_maxMonsters <= spawnedCount) break;

				Monster monster = SpawnMonster(spawnPoint);
				if(monster != null)
				{
					spawnedCount++;
					OnMonsterSpawned?.Invoke( monster );
				}
			}

			_logger.LogInformation( "Initial spawn completed: {Count} monsters spawned", spawnedCount );
		}

		/// <summary>
		/// 특정 스폰 포인트에서 몬스터 스폰
		/// </summary>
		public Monster SpawnMonster(SpawnPoint spawnPoint)
		{
			if(spawnPoint == null) return null;
			if(_maxMonsters <= _monsterIds.Count) return null;

			// 몬스터 정적 데이터 로드
			MonsterData monsterData = _dataManager.GetMonster(spawnPoint.TemplateId);
			if(monsterData == null)
			{
				_logger.LogError("Cannot spawn monster: Template {TemplateId} not found", spawnPoint.TemplateId);
				return null;
			}

			// 몬스터 ID 생성
			long monsterId = GameObjectId.GenerateMonsterId();

			// 몬스터 생성
			Monster monster = new Monster(monsterId, spawnPoint.TemplateId, spawnPoint.Position, monsterData);

			// 몬스터 이벤트 구독
			monster.OnDeath += OnMonsterDeath;
			monster.OnHealthChanged += OnMonsterHealthChanged;
			monster.OnStateChanged += OnMonsterStateChanged;

			// 몬스터 추가
			_monsterIds.Add(monsterId);
			if(!_objectManager.Register( monster ))
			{
				_logger.LogError( "Failed to add monster {MonsterId} to dictionary", monsterId );
				monster.OnDeath -= OnMonsterDeath;
				monster.OnHealthChanged -= OnMonsterHealthChanged;
				monster.OnStateChanged -= OnMonsterStateChanged;
				_monsterIds.Remove(monsterId);
				return null;
			}

			// AI 생성
			MonsterAI monsterAi = new MonsterAI(monster, _room, _logger);
			if(!_monsterAis.TryAdd(monsterId, monsterAi))
			{
				_monsterIds.Remove(monsterId);
				_objectManager.Unregister( monsterId );
				_logger.LogError( "Failed to add monster AI for monster {MonsterId}", monsterId );
				monster.OnDeath -= OnMonsterDeath;
				monster.OnHealthChanged -= OnMonsterHealthChanged;
				monster.OnStateChanged -= OnMonsterStateChanged;
				return null;
			}

			// 스폰 포인트 업데이트
			spawnPoint.CurrentMonsterId = monsterId;
			spawnPoint.LastSpawnTime = DateTime.UtcNow;

			_logger.LogInformation("Monster spawned: {MonsterId} ({Name}) at ({X}, {Y}, {Z})",
				monsterId, monster.Name, monster.PosInfo.PosX, monster.PosInfo.PosY,  monster.PosInfo.PosZ);

			return monster;
		}

		/// <summary>
		/// 몬스터 제거
		/// </summary>
		public bool DespawnMonster(long monsterId, bool raiseEvent = false)
		{
			Monster monster = _objectManager.GetObject<Monster>(monsterId);
			if(monster == null)
			{
				_logger.LogWarning( "Cannot despawn monster {MonsterId}: not found", monsterId );
				return false;
			}

			_monsterIds.Remove(monsterId);
			if(_objectManager.Unregister( monsterId ) == false)
			{
				_logger.LogWarning( "Failed to remove monster {MonsterId} from dictionary", monsterId );
				_monsterIds.Add(monsterId);
				return false;
			}

			// AI 제거
			_monsterAis.TryRemove( monsterId, out _ );

			// 이벤트 구독 해제
			monster.OnDeath -= OnMonsterDeath;
			monster.OnHealthChanged -= OnMonsterHealthChanged;
			monster.OnStateChanged -= OnMonsterStateChanged;

			// 스폰 포인트 업데이트
			var spawnPoint = _spawnPoints.FirstOrDefault(sp => sp.CurrentMonsterId == monsterId);
			if(spawnPoint != null)
			{
				spawnPoint.CurrentMonsterId = 0;
				spawnPoint.LastSpawnTime = DateTime.UtcNow;
			}

			_logger.LogInformation( "Monster despawned: {MonsterId}", monsterId );

			// 이벤트 발생(raiseEvent가 true인 경우)
			if(raiseEvent)
			{
				OnMonsterDespawned?.Invoke( monsterId, monster );
			}

			return true;
		}

		/// <summary>
		/// 몬스터를 지정된 시간 후에 Despawn하도록 예약
		/// JobQueue Worker 스레드에서 안전하게 처리됨
		/// </summary>
		public void ScheduleDespawn(long monsterId, TimeSpan delay)
		{
			// 지금 시점에 SpawnPoint 캡처 (람다에서 클로저로 사용)
			var spawnPoint = _spawnPoints.FirstOrDefault(sp => sp.CurrentMonsterId == monsterId);

			// 딜레이된 디스폰 작업 예약
			_room.ScheduleJob( () =>
			{
				bool success = DespawnMonster( monsterId, raiseEvent: true );

				if(success && spawnPoint != null)
				{
					int respawnMs = (int)spawnPoint.RespawnInterval.TotalMilliseconds;
					// 디스폰 후 리스폰 작업 예약
					_room.ScheduleJob( () =>
					{
						Monster respawnedMonster = SpawnMonster(spawnPoint);
						if((respawnedMonster != null))
						{
							OnMonsterSpawned.Invoke( respawnedMonster );
						}
					}, respawnMs );
				}
			}, (int)delay.TotalMilliseconds );

			_logger.LogInformation( "Scheduled despawn for Monster {MonsterId} after {Delay}s", monsterId, delay.TotalSeconds );
		}

		/// <summary>
		/// 주기적 업데이트 (Room에서 호출)
		/// </summary>
		public void Update()
		{
			// 모든 몬스터 AI 업데이트
			foreach(var kvp in _monsterAis)
			{
				try
				{
					kvp.Value.Update();
				}
				catch(Exception ex)
				{
					_logger.LogError(ex, "Error updating monster AI for monster {MonsterId}", kvp.Key);
				}
			}
		}

		/// <summary>
		/// 특정 몬스터 조회
		/// </summary>
		public Monster GetMonster(long monsterId)
		{
			return _objectManager.GetObject<Monster>( monsterId );
		}

		/// <summary>
		/// 모든 몬스터 조회
		/// </summary>
		public List<Monster> GetAllMonsters()
		{
			List<Monster> monsters = new List<Monster>();
			foreach(long monsterId in _monsterIds)
			{
				Monster monster = _objectManager.GetObject<Monster>( monsterId );
				if(monster != null)
				{
					monsters.Add( monster );
				}
			}

			return monsters;
		}

		/// <summary>
		/// 살아있는 몬스터만 조회
		/// </summary>
		public List<Monster> GetAliveMonsters()
		{
			var monsters = GetAllMonsters();

			return monsters.Where(m => m.IsAlive).ToList();
		}

		/// <summary>
		/// 최대 몬스터 수 설정
		/// </summary>
		public void SetMaxMonsters(int maxMonsters)
		{
			_maxMonsters = Math.Max( 1, maxMonsters );
			_logger.LogInformation( "Max monsters set to {MaxMonsters}", _maxMonsters );
		}

		/// <summary>
		/// 모든 몬스터 제거 (Room 정리 시)
		/// </summary>
		public void ClearAllMonsters()
		{
			_logger.LogInformation( "Clearing all monsters in room {RoomId}...", _room.RoomId );

			foreach(long monsterId in _monsterIds.ToList())
			{
				DespawnMonster( monsterId );
			}

			_spawnPoints.Clear();

			_logger.LogInformation( "All monsters cleared" );
		}

		// 이벤트 핸들러
		private void OnMonsterDeath(IGameObject monster)
		{
			_logger.LogInformation( "Monster {MonsterId} ({Name}) died", monster.ObjectId, monster.Name );

			// 몬스터 제거 (리스폰 가능하도록)
			//DespawnMonster( monster.MonsterId );

			// TODO: BaseRoom에서 보상 처리 및 S_MonsterDie 브로드캐스트
		}

		private void OnMonsterHealthChanged(IGameObject monster, int oldHP, int newHP)
		{
			// TODO: HP 변화가 클 경우 S_MonsterUpdate 브로드캐스트
		}

		private void OnMonsterStateChanged(IGameObject monster, int oldState, int newState)
		{
			_logger.LogDebug( "Monster {MonsterId} state changed: {OldState} -> {NewState}",
				  monster.ObjectId, oldState, newState );

			// 중요한 상태 변경만 브로드캐스트
			bool shouldBroadcast = (State)newState switch
			{
				State.Chase => true,		// 추적 시작
				State.Return => true,		// 귀환 시작
				State.Idle => true,			// 대기 상태
				State.Dead => false,		// S_MonsterDie로 별도 처리
				_ => false
			};

			if(shouldBroadcast)
			{
				var packet = new S_MonsterUpdate { Monsters = { monster.ToObjectInfo() } };
				_room.BroadcastInRange( packet, monster.PosInfo );
			}
		}
	}
}
