using DummyClient.Data.Models;
using Protocol;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace DummyClient
{
	/// <summary>
	/// 클라이언트별 독립 상태. 다중 클라이언트 실행 시 각 Task가 자신만의 인스턴스를 보유합니다.
	/// </summary>
	public class ClientContext
	{
		// 플레이어
		public ClientPlayerInfo MyPlayer { get; set; } = new ClientPlayerInfo();
		public MapData CurrentMapData { get; set; }

		// 주변 오브젝트
		public Dictionary<long, ObjectInfo> NearbyObjects { get; set; } = new();
		public long TargetMonsterId { get; set; } = 0;

		// 자동 이동
		public List<PosInfo> AutoMoveWaypoints { get; set; } = new();
		public PosInfo LastAutoMoveDestination { get; set; }
		public int AutoMoveIndex { get; set; } = 0;

		// 인벤토리
		public bool InventoryRequested { get; set; } = false;
		public DateTime LastInventoryRequestTime { get; set; } = DateTime.MinValue;

		// Ping
		public DateTime LastPingTime { get; set; } = DateTime.MinValue;

		// 포션
		public DateTime LastPotionUseTime { get; set; } = DateTime.MinValue;
		public int HealthPotionSlot { get; set; } = -1;

		// 스킬 쿨타임
		public Dictionary<int, DateTime> SkillCooldowns { get; set; } = new();
	}
}
