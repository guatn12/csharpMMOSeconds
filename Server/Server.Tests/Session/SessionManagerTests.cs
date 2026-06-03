using Microsoft.Extensions.Logging;
using Moq;
using Server.Core.Session;
using Microsoft.Extensions.Options;
using Server.Config;
using Server.Tests.TestHelpers;
using Server.Services;

namespace Server.Tests.Session
{
    /// <summary>
    /// SessionManager 단위 테스트
    /// 테스트 대상: RegisterSession, UnregisterSession, GetSession, Thread Safety
    /// </summary>
    public class SessionManagerTests
    {
        private readonly SessionManager _sessionManager;

		private static Mock<IClientSession> CreateMockSession(long sessionId, long playerId)
		{
			var mockSession = new Mock<IClientSession>();
			mockSession.Setup( s => s.SessionId ).Returns( sessionId );
			mockSession.Setup( s => s.PlayerId ).Returns( playerId );
			mockSession.Setup( s => s.LastActiveTime ).Returns( Environment.TickCount64 );
			return mockSession;
		}

        public SessionManagerTests()
        {
            var mockLogger = new Mock<ILogger<SessionManager>>();
            var mockServiceProvider = new Mock<IServiceProvider>();
			var mockPositionService = new Mock<IPlayerPositionService>();
			var settings = Options.Create(new ServerSettings
			{
				Tick = new TickConfig{BaseTickMs = 100},
				Session = new SessionConfig {TimeoutMs = 60000, HeartbeatIntervalMs = 30000 }
			});
			var tickService = MockFactoryHelper.CreateTickService();

            _sessionManager = new SessionManager(
                mockLogger.Object,
                mockServiceProvider.Object,
                redisService: null,
                playerPositionService: mockPositionService.Object,
				tickService, settings
            );
        }

        #region RegisterSession Tests

        [Fact]
        public void RegisterSession_ShouldAddSessionToActiveSessions()
        {
            // Arrange
            var session = CreateMockSession(sessionId: 1, playerId: 1001);

            // Act	
            bool result = _sessionManager.RegisterSession(session.Object);

            // Assert
            Assert.True(result);
            var retrievedSession = _sessionManager.GetSession(1);
            Assert.NotNull(retrievedSession);
            Assert.Equal(1L, retrievedSession.SessionId);
        }

        [Fact]
        public void RegisterSession_WithNullSession_ShouldReturnFalse()
        {
            // Arrange & Act
            bool result = _sessionManager.RegisterSession(null);

            // Assert
            Assert.False(result);
        }

        [Fact]
        public void RegisterSession_WithDuplicateSessionId_ShouldReturnFalse()
        {
            // Arrange
            var session1 = CreateMockSession(sessionId: 1, playerId: 1001);
            _sessionManager.RegisterSession(session1.Object);

            var session2 = CreateMockSession(sessionId: 1, playerId: 1002); // 같은 SessionId

            // Act
            bool result = _sessionManager.RegisterSession(session2.Object);

            // Assert
            Assert.False(result);
        }

        [Fact]
        public void RegisterSession_WithDuplicatePlayerId_ShouldReturnFalse()
        {
			// 추가 권장:
			// TODO (Phase 4 §10): Last Wins 정책 도입 시 *반대 invariant*로 재작성
			//   - production 변경: PlayerId 중복 시 *기존 세션 강제 종료 + 신규 허용*
			//   - 본 Fact는 *현재 production (false 반환)* 행동 유지

			// Arrange
			var session1 = CreateMockSession(sessionId: 1, playerId: 1001);
            _sessionManager.RegisterSession(session1.Object);

            var session2 = CreateMockSession(sessionId: 2, playerId: 1001); // 같은 PlayerId

            // Act
            bool result = _sessionManager.RegisterSession(session2.Object);

            // Assert
            Assert.False(result);
        }

        [Fact]
        public void RegisterSession_ShouldRaiseSessionRegisteredEvent()
        {
            // Arrange
            var session = CreateMockSession(sessionId: 1, playerId: 1001);

            bool eventRaised = false;
            long capturedSessionId = 0;
            long capturedPlayerId = 0;

            _sessionManager.SessionRegistered += (sender, args) =>
            {
                eventRaised = true;
                capturedSessionId = args.SessionId;
                capturedPlayerId = args.PlayerId;
            };

            // Act
            _sessionManager.RegisterSession(session.Object);

            // Assert
            Assert.True(eventRaised);
            Assert.Equal(1L, capturedSessionId);
            Assert.Equal(1001L, capturedPlayerId);
        }

        #endregion

        #region UnregisterSession Tests

        [Fact]
        public void UnregisterSession_ShouldRemoveSessionFromActiveSessions()
        {
            // Arrange
            var session = CreateMockSession(sessionId: 1, playerId: 1001);
            _sessionManager.RegisterSession(session.Object);

            // Act
            bool result = _sessionManager.UnregisterSession(1L);

            // Assert
            Assert.True(result);
            var retrievedSession = _sessionManager.GetSession(1L);
            Assert.Null(retrievedSession);
        }

        [Fact]
        public void UnregisterSession_WithNonExistentSessionId_ShouldReturnFalse()
        {
            // Arrange
            long nonExistentId = 99999;

            // Act
            bool result = _sessionManager.UnregisterSession(nonExistentId);

            // Assert
            Assert.False(result);
        }

        [Fact]
        public void UnregisterSession_ShouldRaiseSessionUnregisteredEvent()
        {
            // Arrange
            var session = CreateMockSession(sessionId: 1, playerId: 1001);
            _sessionManager.RegisterSession(session.Object);

            bool eventRaised = false;
            long capturedSessionId = 0;
            long capturedPlayerId = 0;

            _sessionManager.SessionUnregistered += (sender, args) =>
            {
                eventRaised = true;
                capturedSessionId = args.SessionId;
                capturedPlayerId = args.PlayerId;
            };

            // Act
            _sessionManager.UnregisterSession(1L);

            // Assert
            Assert.True(eventRaised);
            Assert.Equal(1L, capturedSessionId);
            Assert.Equal(1001L, capturedPlayerId);
        }

		#endregion

		#region GetSession Tests

		[Theory]
		[InlineData(1L, true)]
		[InlineData(99999L, false)]
		public void GetSession_ReturnsExpectedResult(long sessionId, bool shouldFind)
		{
			// Arrange
			var session = CreateMockSession(sessionId: 1, playerId: 1001);
			_sessionManager.RegisterSession( session.Object );

			// Act
			var retrieved = _sessionManager.GetSession(sessionId);

			// Assert
			if(shouldFind)
			{
				Assert.NotNull( retrieved );
				Assert.Equal( sessionId, retrieved.SessionId );
			}
			else
			{
				Assert.Null( retrieved );
			}
		}

		[Theory]
		[InlineData(1001L, true )]
		[InlineData(99999L, false)]
		public void GetSessionByPlayerId_ReturnsExpectedResult( long playerId, bool shouldFind )
		{
			// Arrange
			var session = CreateMockSession(sessionId: 1, playerId: 1001);
			_sessionManager.RegisterSession( session.Object );

			// Act
			var retrieved = _sessionManager.GetSessionByPlayerId(playerId);
			
			// Assert
			if(shouldFind)
			{
				Assert.NotNull( retrieved );
				Assert.Equal( playerId, retrieved.PlayerId );
			}
			else
			{
				Assert.Null( retrieved );
			}
		}

        #endregion

        #region GetAllActiveSessions Tests

        [Fact]
        public void GetAllActiveSessions_ShouldReturnAllRegisteredSessions()
        {
            // Arrange
            var session1 = CreateMockSession(sessionId: 1, playerId: 1001);
            var session2 = CreateMockSession(sessionId: 2, playerId: 1002);
            var session3 = CreateMockSession(sessionId: 3, playerId: 1003);

            _sessionManager.RegisterSession(session1.Object);
            _sessionManager.RegisterSession(session2.Object );
            _sessionManager.RegisterSession(session3.Object );

            // Act
            var allSessions = _sessionManager.GetAllActiveSessions().ToList();

            // Assert
            Assert.Equal(3, allSessions.Count());
            Assert.Contains(allSessions, s => s.SessionId == 1);
            Assert.Contains(allSessions, s => s.SessionId == 2);
            Assert.Contains(allSessions, s => s.SessionId == 3);
        }

        [Fact]
        public void GetAllActiveSessions_WhenNoSessions_ShouldReturnEmptyCollection()
        {
            // Act
            var allSessions = _sessionManager.GetAllActiveSessions();

            // Assert
            Assert.Empty(allSessions);
        }

        [Fact]
        public void GetTotalSessionCount_ShouldReturnCorrectCount()
        {
            // Arrange
            var session1 = CreateMockSession(sessionId: 1, playerId: 1001);
            var session2 = CreateMockSession(sessionId: 2, playerId: 1002);

            _sessionManager.RegisterSession(session1.Object);
            _sessionManager.RegisterSession(session2.Object);

            // Act
            int count = _sessionManager.GetTotalSessionCount();

            // Assert
            Assert.Equal(2, count);
        }

		#endregion

		#region Thread Safety Tests

		[Fact]
		public void ConcurrentRegisterAndUnregister_ShouldBeThreadSafe()
		{
			// Arrange
			const int sessionCount = 100;
			var sessions = Enumerable.Range(0, sessionCount)
				.Select(i => CreateMockSession(sessionId: i + 1, playerId: 2000 + i))
				.ToList();

			// 사전 등록 - 동시에 등록 시도 시 충돌 방지 위해 먼저 등록
			// Act && Assert - 동시에 세션 등록
			Parallel.ForEach( sessions, mockSession =>
			{
				_sessionManager.RegisterSession( mockSession.Object );
			} );

			Assert.Equal( sessionCount, _sessionManager.GetTotalSessionCount() );

			// Act && Assert - 동시에 세션 해제
			Parallel.ForEach( sessions, mockSession =>
			{
				_sessionManager.UnregisterSession( mockSession.Object.SessionId );
			} );

			Assert.Equal( 0, _sessionManager.GetTotalSessionCount() );
		}
		#endregion

		#region SessionDisconnecting Test (TM-1)

		[Fact]
		public void NotifyDisconnecting_ShouldRaiseSessionDisconnectingEvent()
		{
			// Arrange
			var session = CreateMockSession(sessionId: 1, playerId: 1001);
			_sessionManager.RegisterSession( session.Object );

			bool eventRaised = false;
			long capturedSessionId = 0;
			DisconnectReason reason = DisconnectReason.ClientDisconnect;
			DisconnectReason expectedReason = DisconnectReason.Forced;

			_sessionManager.SessionDisconnecting += ( sender, args ) =>
			{
				eventRaised = true;
				capturedSessionId = args.SessionId;
				expectedReason = args.Reason;
			};

			_sessionManager.NotifyDisconnecting( session.Object, reason );

			Assert.True( eventRaised );
			Assert.Equal( 1L, capturedSessionId );
			Assert.Equal( expectedReason, reason );
		}

		#endregion
	}
}
