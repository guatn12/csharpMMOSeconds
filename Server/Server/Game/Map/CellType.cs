using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Server.Game.Map
{
	/// <summary>
	/// 맵의 각 셀(격자)이 가질 수 있는 지형 타입을 정의합니다.
	/// 플레이어/몬스터 이동 시 충돌 판정에 사용됩니다.
	/// </summary>
	public enum CellType
	{
		/// <summary>이동 가능한 일반 지형</summary>
		Walkable = 0,
		/// <summary>벽 - 이동 불가, 시야 차단</summary>
		Wall = 1,
		/// <summary>물 - 이동 불가 (수영 시스템 추가 시 확장 가능)</summary>
		Water = 2,
	}
}
