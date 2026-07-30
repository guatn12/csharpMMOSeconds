namespace DatabaseLib.Redis
{
	/// <summary>
	/// 서비스 경계를 넘는 Redis 키 형식의 단일 정의 - (2개 이상의 소비처(ex-token의 AuthServer / GameServer)인 경우에만 추가한다.
	/// 전역 "MMO:" prefix는 RedisService가 부여하므로 여기 포함하지 않는다.
	/// </summary>
	public static class RedisKeys
	{
		public static string TokenUserId( string token ) => $"token:{token}:userId";
	}
}
