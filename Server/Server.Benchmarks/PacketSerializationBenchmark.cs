using BenchmarkDotNet.Attributes;
using Google.Protobuf;
using Protocol;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.Threading.Tasks;

namespace Server.Benchmarks
{
	/// <summary>
	/// 패킷 직렬화/역직렬화 성능 벤치마크
	/// - Mock 없이 독립 실행 가능
	/// - 순수 Protobuf 성능 측정
	/// </summary>
	[MemoryDiagnoser]       // 메모리 사용량 측정
	[SimpleJob(launchCount: 1, warmupCount: 3, iterationCount: 10 )]	// 실행 설정
	public class PacketSerializationBenchmark
	{
		private C_Move? _movePacket;
		private byte[]? _serializedMove;

		private C_Chat? _chatPacket;
		private byte[]? _serializedChat;

		private C_InventoryRequest? _inventoryPacket;
		private byte[]? _serializedInventory;

		/// <summary>
		/// 벤치마크 시작 전 초기화 (1회 실행)
		/// </summary>
		[GlobalSetup]
		public void Setup()
		{
			// TODO: 테스트용 패킷 생성
			_movePacket = new C_Move
			{
				PosInfo = new PosInfo
				{
					PosX = 50.0f,
					PosY = 10.0f,
					PosZ = 50.0f,
					RotationX = 0.0f,
					RotationY = 90.0f,
					RotationZ = 0.0f,
					Timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()
				}
			};

			_chatPacket = new C_Chat
			{
				Message = "Benchmark test message for performance testing"
			};

			_inventoryPacket = new C_InventoryRequest();

			// TODO: 역직렬화용 데이터 준비
			using(var ms = new MemoryStream())
			{
				_movePacket.WriteTo( ms );
				_serializedMove = ms.ToArray();
			}

			using (var ms = new MemoryStream())
			{
				_chatPacket.WriteTo(ms);
				_serializedChat = ms.ToArray();
			}

			using (var ms = new MemoryStream())
			{
				_inventoryPacket.WriteTo(ms);
				_serializedInventory = ms.ToArray();
			}
		}

		// ============================= 직렬화 벤치마크 =============================

		[Benchmark]	// 벤치마크 대상 메서드
		public byte[] SerializeMovePacket()
		{
			// TODO: C_Move 패킷을 byte[]로 직렬화
			using var ms = new MemoryStream();
			_movePacket!.WriteTo( ms );
			return ms.ToArray();
		}

		[Benchmark]
		public byte[] SerializeChatPacket()
		{
			// TODO: C_Chat 패킷을 byte[]로 직렬화
			using var ms = new MemoryStream();
			_chatPacket!.WriteTo( ms );
			return ms.ToArray();
		}

		[Benchmark]
		public byte[] SerializeInventoryPacket()
		{
			using var ms = new MemoryStream();
			_inventoryPacket!.WriteTo( ms );
			return ms.ToArray();
		}

			// ==================== 역직렬화 벤치마크 ====================

			[Benchmark]
		public C_Move DeserializeMovePacket()
		{
			// TODO: byte[]를 C_Move로 역직렬화
			return C_Move.Parser.ParseFrom( _serializedMove! );
		}

		[Benchmark]
		public C_Chat DeserializeChatPacket()
		{
			// TODO: byte[]를 C_Chat로 역직렬화
			return C_Chat.Parser.ParseFrom( _serializedChat! );
		}

		// ==================== ArraySegment vs Span 비교 ====================

		[Benchmark]
		public C_Move DeserializeWithArraySegment()
		{
			// TODO: ArraySegment를 사용한 역직렬화
			var segment = new ArraySegment<byte>(_serializedMove!, 0, _serializedMove!.Length);
			return C_Move.Parser.ParseFrom( segment.Array, segment.Offset, segment.Count );
		}

		[Benchmark]
		public C_Move DeserializeWithSpan()
		{
			// TODO: ReadOnlySpan를 사용한 역직렬화
			ReadOnlySpan<byte> span = _serializedMove;
			return C_Move.Parser.ParseFrom( span.ToArray() ); // Protobuf는 Span 직접 지원 안 함
		}

		[GlobalCleanup] // 벤치마크 종료 후 정리 (1회 실행)
		public void Cleanup()
		{
			// TODO: 리소스 정리 (필요시)
		}
	}
}
