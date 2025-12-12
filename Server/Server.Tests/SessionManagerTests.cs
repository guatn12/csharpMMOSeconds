using Microsoft.Extensions.Logging;
using Moq;
using Server.Core.Session;
using Server.Game;
using Xunit;
using System.Linq;
using System.Reflection;

namespace Server.Tests
{
    /// <summary>
    /// SessionManager 단위 테스트
    /// 테스트 대상: RegisterSession, UnregisterSession, GetSession, Thread Safety
    /// </summary>
    public class SessionManagerTests : IDisposable
    {
        private readonly SessionManager _sessionManager;

        public SessionManagerTests()
        {
            // SessionManager 생성 (모든 의존성을 null로 전달)
            var mockLogger = new Mock<ILogger<SessionManager>>();
            var mockServiceProvider = new Mock<IServiceProvider>();

            _sessionManager = new SessionManager(
                mockLogger.Object,
                mockServiceProvider.Object,
                redisService: null,
                playerPositionService: null
            );
        }

        public void Dispose()
        {
            // Cleanup
        }

        #region RegisterSession Tests

        [Fact]
        public void RegisterSession_ShouldAddSessionToActiveSessions()
        {
            // Arrange
            var session = CreateTestGameSession(sessionId: 1, playerId: 1001);

            // Act
            bool result = _sessionManager.RegisterSession(session);

            // Assert
            Assert.True(result);
            var retrievedSession = _sessionManager.GetSession(session.SessionId);
            Assert.NotNull(retrievedSession);
            Assert.Equal(session.SessionId, retrievedSession.SessionId);
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
            var session1 = CreateTestGameSession(sessionId: 1, playerId: 1001);
            _sessionManager.RegisterSession(session1);

            var session2 = CreateTestGameSession(sessionId: 1, playerId: 1002); // 같은 SessionId

            // Act
            bool result = _sessionManager.RegisterSession(session2);

            // Assert
            Assert.False(result);
        }

        [Fact]
        public void RegisterSession_WithDuplicatePlayerId_ShouldReturnFalse()
        {
            // Arrange
            var session1 = CreateTestGameSession(sessionId: 1, playerId: 1001);
            _sessionManager.RegisterSession(session1);

            var session2 = CreateTestGameSession(sessionId: 2, playerId: 1001); // 같은 PlayerId

            // Act
            bool result = _sessionManager.RegisterSession(session2);

            // Assert
            Assert.False(result);
        }

        [Fact]
        public void RegisterSession_ShouldRaiseSessionRegisteredEvent()
        {
            // Arrange
            var session = CreateTestGameSession(sessionId: 1, playerId: 1001);

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
            _sessionManager.RegisterSession(session);

            // Assert
            Assert.True(eventRaised);
            Assert.Equal(session.SessionId, capturedSessionId);
            Assert.Equal(session.PlayerId, capturedPlayerId);
        }

        #endregion

        #region UnregisterSession Tests

        [Fact]
        public void UnregisterSession_ShouldRemoveSessionFromActiveSessions()
        {
            // Arrange
            var session = CreateTestGameSession(sessionId: 1, playerId: 1001);
            _sessionManager.RegisterSession(session);

            // Act
            bool result = _sessionManager.UnregisterSession(session.SessionId);

            // Assert
            Assert.True(result);
            var retrievedSession = _sessionManager.GetSession(session.SessionId);
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
            var session = CreateTestGameSession(sessionId: 1, playerId: 1001);
            _sessionManager.RegisterSession(session);

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
            _sessionManager.UnregisterSession(session.SessionId);

            // Assert
            Assert.True(eventRaised);
            Assert.Equal(session.SessionId, capturedSessionId);
            Assert.Equal(session.PlayerId, capturedPlayerId);
        }

        #endregion

        #region GetSession Tests

        [Fact]
        public void GetSession_WithNonExistentSessionId_ShouldReturnNull()
        {
            // Arrange
            long nonExistentId = 99999;

            // Act
            var session = _sessionManager.GetSession(nonExistentId);

            // Assert
            Assert.Null(session);
        }

        [Fact]
        public void GetSession_WithExistingSessionId_ShouldReturnCorrectSession()
        {
            // Arrange
            var session = CreateTestGameSession(sessionId: 1, playerId: 1001);
            _sessionManager.RegisterSession(session);

            // Act
            var retrievedSession = _sessionManager.GetSession(session.SessionId);

            // Assert
            Assert.NotNull(retrievedSession);
            Assert.Equal(session.SessionId, retrievedSession.SessionId);
        }

        [Fact]
        public void GetSessionByPlayerId_WithExistingPlayerId_ShouldReturnCorrectSession()
        {
            // Arrange
            var session = CreateTestGameSession(sessionId: 1, playerId: 1001);
            _sessionManager.RegisterSession(session);

            // Act
            var retrievedSession = _sessionManager.GetSessionByPlayerId(1001);

            // Assert
            Assert.NotNull(retrievedSession);
            Assert.Equal(1001, retrievedSession.PlayerId);
        }

        [Fact]
        public void GetSessionByPlayerId_WithNonExistentPlayerId_ShouldReturnNull()
        {
            // Arrange & Act
            var session = _sessionManager.GetSessionByPlayerId(99999);

            // Assert
            Assert.Null(session);
        }

        #endregion

        #region GetAllActiveSessions Tests

        [Fact]
        public void GetAllActiveSessions_ShouldReturnAllRegisteredSessions()
        {
            // Arrange
            var session1 = CreateTestGameSession(sessionId: 1, playerId: 1001);
            var session2 = CreateTestGameSession(sessionId: 2, playerId: 1002);
            var session3 = CreateTestGameSession(sessionId: 3, playerId: 1003);

            _sessionManager.RegisterSession(session1);
            _sessionManager.RegisterSession(session2);
            _sessionManager.RegisterSession(session3);

            // Act
            var allSessions = _sessionManager.GetAllActiveSessions();

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
            var session1 = CreateTestGameSession(sessionId: 1, playerId: 1001);
            var session2 = CreateTestGameSession(sessionId: 2, playerId: 1002);

            _sessionManager.RegisterSession(session1);
            _sessionManager.RegisterSession(session2);

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
            var sessions = new System.Collections.Concurrent.ConcurrentBag<GameSession>();

            // 세션 생성
            for (int i = 0; i < sessionCount; i++)
            {
                var session = CreateTestGameSession(sessionId: i + 1, playerId: 2000 + i);
                sessions.Add(session);
            }

            // Act - 동시에 세션 등록
            Parallel.ForEach(sessions, session =>
            {
                _sessionManager.RegisterSession(session);
            });

            // Assert - 모든 세션이 등록되었는지 확인
            int registeredCount = _sessionManager.GetTotalSessionCount();
            Assert.Equal(sessionCount, registeredCount);

            // Act - 동시에 세션 해제
            Parallel.ForEach(sessions, session =>
            {
                _sessionManager.UnregisterSession(session.SessionId);
            });

            // Assert - 모든 세션이 제거되었는지 확인
            int remainingCount = _sessionManager.GetTotalSessionCount();
            Assert.Equal(0, remainingCount);
        }

        #endregion

        #region Helper Methods

        /// <summary>
        /// 테스트용 GameSession 객체 생성 (리플렉션 사용)
        /// </summary>
        private GameSession CreateTestGameSession(long sessionId, long playerId)
        {
            // GameSession 생성자는 ILogger, PacketManager, ISessionManager, sessionId 필요
            var mockLogger = new Mock<ILogger<GameSession>>();

            // GameSession 생성
            var session = (GameSession)Activator.CreateInstance(
                typeof(GameSession),
                BindingFlags.Instance | BindingFlags.Public,
                null,
                new object[] { mockLogger.Object, null, _sessionManager, sessionId },
                null
            );

            // Player 초기화 (InitializePlayer private 메서드 호출)
            var initializePlayerMethod = typeof(GameSession).GetMethod(
                "InitializePlayer",
                BindingFlags.Instance | BindingFlags.NonPublic
            );
            initializePlayerMethod.Invoke(session, null);

            // Player의 PlayerId 변경 (리플렉션)
            var playerProperty = typeof(GameSession).GetProperty("Player");
            var player = (Player)playerProperty.GetValue(session);

            // Player.Info.PlayerId 변경
            var infoProperty = typeof(Player).GetProperty("Info");
            var info = infoProperty.GetValue(player);
            var playerIdField = info.GetType().GetProperty("PlayerId");
            playerIdField.SetValue(info, playerId);

            return session;
        }

        #endregion
    }
}
