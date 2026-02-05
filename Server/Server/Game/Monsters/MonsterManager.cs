using Microsoft.Extensions.Logging;
using Protocol;
using Server.Data;
using Server.Room;
using Server.Utils;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;


namespace Server.Game.Monsters
{
	/// <summary>
	/// 몬스터 관리자 구현체
	/// </summary>
	public class MonsterManager : IMonsterManager
	{
		private readonly IRoom _room;
		private readonly DataManager _dataManager;
		private readonly ILogger _logger;
		private MonsterSpawner _monsterSpawner;
		private MonsterSpawnPolicy _monsterSpawnPolicy;
		private bool _isAIPaused = false;
		private bool _disposed = false;

		public event Action<long, Monster> OnMonsterDespawned;
		public event Action<Monster> OnMonsterSpawned;

		public MonsterManager(IRoom room, DataManager dataManager, ILogger logger, 
			MonsterSpawnPolicy monsterSpawnPolicy = null )
		{
			_room=room;
			_dataManager=dataManager;
			_logger=logger;
			_monsterSpawnPolicy=monsterSpawnPolicy ?? MonsterSpawnPolicy.Default;

			_logger.LogInformation( "MonsterManager created for Room {RoomId} with policy: MaxMonsters={MaxMonsters}, AutoRespawn={AutoRespawn}",
				_room.RoomId, _monsterSpawnPolicy.MaxMonsters, _monsterSpawnPolicy.AutoRespawn );
		}

		#region Lifecycle Management

		public async Task InitializeAsync()
		{
			if(_monsterSpawner != null)
			{
				_logger.LogWarning("MonsterManager already initialized for Room {RoomId}", _room.RoomId);
				return;
			}

			// MonsterSpawner 생성
			_monsterSpawner = new MonsterSpawner( _room, _dataManager, _logger );

			_monsterSpawner.OnMonsterDespawned += HandleMonsterDespawned;
			_monsterSpawner.OnMonsterSpawned += HandleMonsterSpawned;

			// Policy 적용
			_monsterSpawner.SetMaxMonsters(_monsterSpawnPolicy.MaxMonsters);

			_logger.LogInformation( "MonsterManager Initialized for Room {RoomId}", _room.RoomId );

			await Task.CompletedTask;
		}

		private void HandleMonsterDespawned(long monsterId, Monster monster)
		{
			OnMonsterDespawned?.Invoke( monsterId, monster );
		}

		private void HandleMonsterSpawned(Monster monster)
		{
			OnMonsterSpawned?.Invoke( monster );
		}

		public void AddSpawnPoint( int templateId, PosInfo position, TimeSpan? respawnInterval = null )
		{
			if(_monsterSpawner == null)
			{
				_logger.LogError( "MonsterSpawner not initialized in Room {RoomId}", _room.RoomId );
				return;
			}

			_monsterSpawner.AddSpawnPoint( templateId, position, respawnInterval ?? _monsterSpawnPolicy.DefaultRespawnInterval );
		}

		public void SpawnInitialMonsters()
		{
			if(_monsterSpawner == null)
			{
				_logger.LogError( "MonsterSpawner not initialized in Room {RoomId}", _room.RoomId );
				return;
			}

			if(_monsterSpawnPolicy.AutoRespawn)
			{
				_monsterSpawner.SpawnInitialMonsters();
			}
		}

		public Monster SpawnMonster( int templateId, PosInfo position )
		{
			if(_monsterSpawner == null)
			{
				_logger.LogError( "MonsterSpawner not initialized in Room {RoomId}", _room.RoomId );
				return null;
			}

			if(position == null)
			{
				_logger.LogError( "Position is null for psawning monster in Room {RoomId}", _room.RoomId );
				return null;
			}

			// SpawnPoint 생성
			var spawnPoint = new SpawnPoint(templateId, position, _monsterSpawnPolicy.DefaultRespawnInterval);

			Monster monster = _monsterSpawner.SpawnMonster(spawnPoint);
			if(monster != null)
			{
				_logger.LogInformation( "Monster {MonsterId} (Template: {TemplateId} spawned at ({X}, {Y}, {Z}) in Room {RoomId}",
					monster.MonsterId, templateId, position.PosX, position.PosY, position.PosZ, _room.RoomId );
			}

			return monster;
		}

		public void DespawnMonster( long monsterId, TimeSpan? delay = null )
		{
			if(_monsterSpawner == null)
				return;

			if(delay.HasValue)
			{
				_monsterSpawner.ScheduleDespawn(monsterId, delay.Value);
			}
			else
			{
				_monsterSpawner.DespawnMonster(monsterId);
			}
		}

		public void Update()
		{
			if(_monsterSpawner == null )
			{
				_logger.LogWarning( "MonsterSpawner is null for room {RoomId}", _room.RoomId );
				return;
			}

			if(_isAIPaused)
			{
				// AI가 일시정지 상태면 Update 스킵
				return;
			}

			_monsterSpawner.Update();
		}

		#endregion

		#region High-Level Search APIs

		public List<Monster> GetMonstersByTemplateId( int templateId )
		{
			if(_monsterSpawner == null)
				return new List<Monster>();

			return _monsterSpawner.GetAllMonsters()
				.Where(m => m.IsAlive && m.TemplateId == templateId)
				.ToList();
		}

		public List<Monster> GetMonstersInCombat()
		{
			if(_monsterSpawner == null)
				return new List<Monster>();

			return _monsterSpawner.GetAllMonsters()
				.Where(m => m.IsAlive && m.IsInCombat )
				.ToList();
		}

		public Monster GetMonster( long monsterId )
		{
			return _monsterSpawner.GetMonster( monsterId );
		}

		public List<Monster> GetAliveMonsters()
		{
			if(_monsterSpawner == null)
				return new List<Monster>();

			return _monsterSpawner.GetAllMonsters();
		}

		#endregion

		#region Centralized State Management

		public bool UpdateMonsterHP( long monsterId, int newHP )
		{
			Monster monster = GetMonster( monsterId );
			if(monster == null || !monster.IsAlive)
				return false;

			int currentHP = monster.CurrentHP;
			int difference = newHP - currentHP;

			if(difference < 0)
			{
				// 데미지
				return monster.TakeDamage( -difference, 0 );
			}
			else if(0 < difference)
			{
				// 힐
				return monster.Heal( difference );
			}

			return false;
		}

		public bool SetMonsterTarget( long monsterId, long targetPlayerId )
		{
			Monster monster = GetMonster(monsterId);
			if(monster == null || !monster.IsAlive)
				return false;

			monster.SetTarget(targetPlayerId);
			_logger.LogDebug("Monster {MonsterId} target set to Player {PlayerId}", monsterId, targetPlayerId);
			return true;
		}

		public void ClearTargetsForPlayer( long playerId )
		{
			if(_monsterSpawner == null)
				return;

			var monsters = _monsterSpawner.GetAllMonsters().Where(m => m.IsAlive && m.TargetPlayerId == playerId);

			foreach(var monster in monsters )
			{
				monster.ClearTarget();
			}

			_logger.LogInformation( "Cleared all monster targets for Player {PlayerId} in Room {RoomId}", playerId, _room.RoomId );
		}

		#endregion

		#region AI Control

		public void PauseAllMonsterAI()
		{
			_isAIPaused = true;
			_logger.LogInformation( "All monster AI paused in Room {RoomId}", _room.RoomId );
		}

		public void ResumeAllMonsterAI()
		{
			_isAIPaused = false;
			_logger.LogInformation( "All monster AI resumed in Room {RoomId}", _room.RoomId );
		}

		#endregion

		#region Policy Management

		public void SetSpawnPolicy( MonsterSpawnPolicy policy )
		{
			if(_monsterSpawnPolicy == null)
			{
				_logger.LogWarning( "Attempted to set null spawn policy in Room {RoomId}", _room.RoomId );
				return;
			}

			_monsterSpawnPolicy = policy;
			_monsterSpawner?.SetMaxMonsters(_monsterSpawnPolicy.MaxMonsters);

			_logger.LogInformation(
				"Spawn policy updated for Room {RoomId}: MaxMonsters={MaxMonsters}, AutoRespawn={AutoRespawn}",
				_room.RoomId, _monsterSpawnPolicy.MaxMonsters, _monsterSpawnPolicy.AutoRespawn );
		}

		public void SetMaxMonsters( int maxCount )
		{
			_monsterSpawnPolicy.MaxMonsters = Math.Max( 1, maxCount );
			_monsterSpawner.SetMaxMonsters( _monsterSpawnPolicy.MaxMonsters );

			_logger.LogInformation( "Max monsters set to {MaxMonsters} in Room {RoomId}", 
				_monsterSpawnPolicy.MaxMonsters, _room.RoomId );
		}

		#endregion

		#region Statistics

		public MonsterStatistics GetStatistics()
		{
			if(_monsterSpawner == null)
			{
				return new MonsterStatistics();
			}

			var allMonsters = _monsterSpawner.GetAllMonsters();

			return new MonsterStatistics
			{
				TotalMonsters = allMonsters.Count,
				AliveMonsters = allMonsters.Count( m => m.IsAlive ),
				DeadMonsters = allMonsters.Count( m => !m.IsAlive ),
				MonstersInCombat = allMonsters.Count( m => m.IsAlive && m.IsInCombat ),
				SpawnPointCount = _monsterSpawner.SpawnPointCount,
				AverageHP = allMonsters.Where( m => m.IsAlive ).Any()
							? allMonsters.Where( m => m.IsAlive ).Average( m => m.HPPercentage )
							: 0f
			};
		}

		public int GetMonsterCount()
		{
			return _monsterSpawner?.MonsterCount ?? 0;
		}

		#endregion

		#region IDisposable

		public void Dispose()
		{
			Dispose( true );
			GC.SuppressFinalize( this );
		}

		protected virtual void Dispose(bool disposing )
		{
			if(_disposed)
				return;

			if(disposing)
			{
				if(_monsterSpawner != null)
				{
					_monsterSpawner.OnMonsterDespawned -= (monsterId, monster) => OnMonsterDespawned?.Invoke(monsterId, monster);
					_monsterSpawner.OnMonsterSpawned -= (monster) => OnMonsterSpawned?.Invoke(monster);
				}
				_monsterSpawner?.ClearAllMonsters();
				_monsterSpawner = null;
				_logger.LogInformation( "MonsterManager disposed for Room {RoomId}", _room.RoomId );
			}

			_disposed = true;
		}

		#endregion

















	}
}
