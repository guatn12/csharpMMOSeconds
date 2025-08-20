using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Server.Data.Storage
{
	/// <summary>
	/// 스레드 안전한 데이터 저장도 인터페이스
	/// 제네릭을 사용하여 타입 안전성 보장
	/// </summary>
	public interface IThreadSafeDataStorage<TKey, TValue> where TKey : notnull
	{
		// 단일 데이터 조회
		TValue? Get(TKey key);

		// 전체 데이터 조회
		IReadOnlyDictionary<TKey, TValue> GetAll();

		// 데이터 전체 교체(원자적 교체)
		void Update(Dictionary<TKey, TValue> newData);

		// 데이터 개수
		int Count { get; }

		// 특정 키 존재 여부
		bool ContainsKey(TKey key);
	}
}
