using Microsoft.Extensions.Logging;
using Server.Game;
using ServerCore;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Server.Core.Jobs
{
	/// <summary>
	/// 몬스터 업데이트를 JobQueue에서 처리하기 위한 Job 클래스
	/// Thread-safe한 Monster AI 업데이트 및 리스폰 처리
	/// </summary>
	public class MonsterUpdateJob : IJob
	{
		private MonsterSpawner _monsterSpawner;
		private ILogger _logger;
		private int _roomId;

		/// <summary>
		/// Job 실행에 필요한 컨텍스트 초기화
		/// </summary>
		public void Initialize(MonsterSpawner monsterSpawner, int roomId, ILogger logger)
		{
			_monsterSpawner = monsterSpawner;
			_roomId = roomId;
			_logger = logger;
		}

		/// <summary>
		/// JobQueueManager 워커 스레드에서 호출됨
		/// MonsterSpawner.Update()를 통해 모든 몬스터 AI 및 리스폰 처리
		/// </summary>
		public void Execute()
		{
			try
			{
				_monsterSpawner.Update();
			}
			catch(Exception ex)
			{
				_logger.LogError( ex, "Error updating monsters in room {roomId}", _roomId );
			}
		}

		/// <summary>
		/// JobPool 반환 전 메모리 정리
		/// </summary>
		public void Clear()
		{
			_monsterSpawner = null;
			_roomId = 0;
			_logger = null;
		}
	}
}
