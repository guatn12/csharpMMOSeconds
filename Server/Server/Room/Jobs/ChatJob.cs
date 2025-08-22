using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Server.Room.Jobs
{
	public class ChatJob : AsyncRoomJob
	{
		private readonly Protocol.C_Chat _chatPacket;

		public override string JobType => "PlayerChat";
		public override int Priority => RoomJobPriority.Normal;
		public override int TimeoutMs => 2000;

		public ChatJob(GameSession session, IRoom room, Protocol.C_Chat chatPacket, ILogger logger)
			: base(session, room, logger)
		{
			_chatPacket = chatPacket ?? throw new ArgumentNullException(nameof(chatPacket));
		}

		protected override async Task ExecuteAsync()
		{
			try
			{
				_logger.LogInformation( "Processing chat from Player {SessionId} in Room {RoomId}: '{Message}'",
					  _session.SessionId, _room.RoomId, _chatPacket.Message );

				// Room의 HandlePlayerChatAsync 호출
				await _room.HandlePlayerChatAsync( _session, _chatPacket );

				_logger.LogDebug( "Chat processed successfully for Player {SessionId} in Room {RoomId}",
					  _session.SessionId, _room.RoomId );
			}
			catch ( Exception ex )
			{
				_logger.LogError( ex, "Failed to process chat for Player {SessionId} in Room {RoomId}: '{Message}'",
					 _session.SessionId, _room.RoomId, _chatPacket.Message );
				throw; // 예외를 다시 던져서 기반 클래스에서 처리
			}
		}

		protected override bool PreExecutionValidation()
		{
			if(!base.PreExecutionValidation())
				return false;

			// ChatPacket 유효성 검증
			if( _chatPacket == null )
			{
				_logger.LogWarning( "Null chat packet for Player {SessionId} in Room {RoomId}",
					  _session.SessionId, _room.RoomId );
				return false;
			}

			// 메시지 내용 검증
			if(string.IsNullOrWhiteSpace( _chatPacket.Message ))
			{
				_logger.LogDebug( "Empty chat message from Player {SessionId} in Room {RoomId}",
					_session.SessionId, _room.RoomId );
				return false;
			}

			// 메시지 길이 검증 (기본 제한)
			if(_chatPacket.Message.Length > 500)
			{
				_logger.LogWarning( "Chat message too long from Player {SessionId} in Room {RoomId}: {Length} characters",
					_session.SessionId, _room.RoomId, _chatPacket.Message.Length );
				return false;
			}

			// 기본 스팸 필터링 (단순한 예시)
			if(ContainsSpam( _chatPacket.Message ))
			{
				_logger.LogWarning( "Spam detected in chat from Player {SessionId} in Room {RoomId}: '{Message}'",
					_session.SessionId, _room.RoomId, _chatPacket.Message );
				return false;
			}

			return true;
		}

		protected override void HandleException( Exception exception )
		{
			base.HandleException( exception );
		}

		private bool ContainsSpam(string message )
		{
			if(string.IsNullOrWhiteSpace( message ))
				return false;

			string lowerMessage = message.ToLowerInvariant();

			// 기본 스팸 키워드
			string[] spamKeywords = {
				  "spam", "advertisement", "buy now", "click here",
				  "free money", "get rich", "miracle cure"
			  };

			foreach(var keyword in spamKeywords)
			{
				if(lowerMessage.Contains( keyword ))
				{
					return true;
				}
			}

			// 반복 문자 검증 (예: "aaaaaaaaaa")
			if(HasExcessiveRepeatingCharacters( message ))
			{
				return true;
			}

			// 연속된 대문자 검증 (예: "HELLOOOOOOO")
			if(HasExcessiveCaps( message ))
			{
				return true;
			}

			return false;
		}

		/// <summary>
		/// 과도한 반복 문자 검사
		/// </summary>
		private bool HasExcessiveRepeatingCharacters( string message )
		{
			if(string.IsNullOrEmpty( message ) || message.Length < 6)
				return false;

			int consecutiveCount = 1;
			char previousChar = message[0];

			for(int i = 1; i < message.Length; i++)
			{
				if(message[ i ] == previousChar)
				{
					consecutiveCount++;
					if(consecutiveCount >= 6) // 6개 이상 연속 문자
					{
						return true;
					}
				}
				else
				{
					consecutiveCount = 1;
					previousChar = message[ i ];
				}
			}

			return false;
		}

		/// <summary>
		/// 과도한 대문자 검사
		/// </summary>
		private bool HasExcessiveCaps( string message )
		{
			if(string.IsNullOrEmpty( message ) || message.Length < 10)
				return false;

			int capsCount = 0;
			int letterCount = 0;

			foreach(char c in message)
			{
				if(char.IsLetter( c ))
				{
					letterCount++;
					if(char.IsUpper( c ))
					{
						capsCount++;
					}
				}
			}

			// 전체 글자의 70% 이상이 대문자인 경우
			return letterCount > 0 && (double)capsCount / letterCount > 0.7;
		}

		public override string ToString()
		{
			var messagePreview = _chatPacket?.Message?.Length > 50
				  ? _chatPacket.Message.Substring(0, 47) + "..."
				  : _chatPacket?.Message ?? "null";

			return $"{base.ToString()} - Message: \"{messagePreview}\"";
		}
	}
}
