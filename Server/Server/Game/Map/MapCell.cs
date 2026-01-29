using Server.Core.Session;
using Server.Game.Monsters;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Server.Game.Map
{
	/// <summary>
	/// 맵의 단일 격자(Cell)를 나타내며, 해당 위치의 모든 엔티티를 타입별로 관리합니다.
	/// 
	/// 설계 의도:
	/// - 타입별 분리 저장으로 검색 시 불필요한 타입 캐스팅 제거
	/// - HashSet 사용으로 O(1) 추가/제거/존재 확인
	/// </summary>
	public class MapCell
	{
		/// <summary> 이 셀에 위치한 플레이어 목록 </summary>
		public HashSet<IClientSession> Players { get; } = new HashSet<IClientSession>();
		/// <summary> 이 셀에 위치한 몬스터 목록 </summary>
		public HashSet<Monster> Monsters { get; } = new HashSet<Monster>();

		/// <summary> 이 셀이 비어있는지 확인 </summary>
		public bool IsEmpty() => Players.Count == 0 && Monsters.Count == 0;

		/// <summary> 이 셀의 총 엔티티 수 </summary>
		public int TotalCount => Players.Count + Monsters.Count;
	}
}
