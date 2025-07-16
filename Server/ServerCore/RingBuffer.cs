using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace ServerCore
{
	public class RingBuffer
	{
		private readonly int _capacity; // 버퍼의 총 량
		private readonly byte[] _buffer; // 실제 데이터를 저장할 바이트 배열
		private int _readPos;
		private int _writePos;

		public RingBuffer( int capacity )
		{
			_capacity = capacity + 1; // 비어있는 _readPos == _writePos와 버퍼가 가득 찬 상황을 구분하기 위해 1을 추가.
			_buffer = new byte[ _capacity ];
			_readPos = 0;
			_writePos = 0;
		}

		public int Capacity { get { return _capacity; } }

		public int DataSize
		{
			get
			{
				if(_readPos <= _writePos) return _writePos - _readPos; // 일반 적인 상황
				else return _capacity - _readPos + _writePos; // 순환이 발생하여 _writePos가 앞으로 이동했을 때.
			}
		}
		public int FreeSize { get { return _capacity - DataSize - 1; } }

		public ArraySegment<byte> WriteSegment()
		{
			if(_writePos < _readPos)
			{
				// 순환이 발생하여, 남은 공간 다시 확인.
				return new ArraySegment<byte>( _buffer, _writePos, _readPos - _writePos - 1 );
			}
			else
			{
				int remainSpace = _capacity - _writePos;
				return new ArraySegment<byte>( _buffer, _writePos, Math.Min( remainSpace, FreeSize ) );
			}
		}

		public ArraySegment<byte> ReadSegment()
		{
			if(_readPos <= _writePos)
			{
				return new ArraySegment<byte>( _buffer, _readPos, DataSize );
			}
			else
			{
				// 순환이 발생했으면 _readPos에서 부터 마지막까지만 전달한다.
				return new ArraySegment<byte>( _buffer, _readPos, _capacity - _readPos );
			}
		}

		public void CommitRead( int bytesToCommit )
		{
			if(bytesToCommit > DataSize)
				throw new InvalidOperationException( "Trying to commit more bytes than available data." );

			_readPos = (_readPos + bytesToCommit) % _capacity;
		}

		public void CommitWrite( int bytesToCommit )
		{
			if(bytesToCommit > FreeSize)
				throw new InvalidOperationException( "Trying to commit more byte than free space" );

			_writePos = (_writePos + bytesToCommit) % _capacity;
		}

		public void Clear()
		{
			_readPos = 0;
			_writePos = 0;
		}

		public void Peek( byte[] destination, int offset, int count )
		{
			if(DataSize < count)
				throw new InvalidOperationException( "Trying to peek more bytes than available data." );

			int tempReadPos = _readPos;
			int bytesPeeked = 0;
			while(bytesPeeked < count)
			{
				// 임시로 읽기 세그먼트 계산
				ArraySegment<byte> segment;
				if(tempReadPos <= _writePos)
					segment = new ArraySegment<byte>( _buffer, tempReadPos, _writePos - tempReadPos );
				else
					// 순환한 경우.끝까지만 읽는다.
					segment = new ArraySegment<byte>(_buffer, tempReadPos, _capacity - tempReadPos );

				int bytesToCopy = Math.Min(count - bytesPeeked, segment.Count);
				Buffer.BlockCopy( segment.Array, segment.Offset, destination, offset + bytesPeeked, bytesToCopy );

				tempReadPos = (tempReadPos + bytesToCopy) % _capacity;
				bytesPeeked += bytesToCopy;
			}
		}

		public void Read( byte[] destination, int offset, int count )
		{
			if(DataSize < count)
				throw new InvalidOperationException( "Trying to peek more bytes than available data." );

			int bytesRead = 0;
			while(bytesRead < count)
			{
				ArraySegment<byte> segment = ReadSegment();
				int bytesToCopy = Math.Min(count - bytesRead, segment.Count);
				Buffer.BlockCopy(segment.Array, segment.Offset, destination, offset + bytesRead, bytesToCopy );

				CommitRead( bytesToCopy );
				bytesRead += bytesToCopy;
			}
		}
	}
}
