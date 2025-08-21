using Microsoft.Extensions.Options;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using static System.Runtime.InteropServices.JavaScript.JSType;

namespace Server.Configuration.Validators
{
	public class RoomSettingsValidator : IValidateOptions<RoomSettings>
	{
		public ValidateOptionsResult Validate(string name, RoomSettings options)
		{
			List<string> errors = new List<string>();

			// Default 설정 검증
			if(options.Default.MaxPlayers < 1 || options.Default.MaxPlayers > 10000)
				errors.Add( "Default.MaxPlayers must be between 1 and 10000" );

			if(options.Default.MaxRoomNameLength < 1 || options.Default.MaxRoomNameLength > 100)
				errors.Add( "Default.MaxRoomNameLength must be between 1 and 100" );

			// Lobby 설정 검증
			if(options.Lobby.MaxPlayers < 1 || options.Lobby.MaxPlayers > 10000)
				errors.Add( "Lobby.MaxPlayers must be between 1 and 10000" );

			if(string.IsNullOrWhiteSpace( options.Lobby.DefaultLobbyName ))
				errors.Add( "Lobby.DefaultLobbyName cannot be empty" );

			// Battle 설정 검증
			if(options.Battle.MaxPlayers < 2 || options.Battle.MaxPlayers > 1000)
				errors.Add( "Battle.MaxPlayers must be between 2 and 1000" );

			if(options.Battle.TimeLimitMinutes < 1 || options.Battle.TimeLimitMinutes > 1440)
				errors.Add( "Battle.TimeLimitMinutes must be between 1 and 1440" );

			// Cleanup 설정 검증
			if(options.Cleanup.EmptyRoomCleanupIntervalMinutes < 1)
				errors.Add( "Cleanup.EmptyRoomCleanupIntervalMinutes must be at least 1" );

			if(options.Cleanup.EmptyRoomGracePeriodMinutes < 0)
				errors.Add( "Cleanup.EmptyRoomGracePeriodMinutes cannot be negative" );

			// Performance 설정 검증
			if(options.Performance.MaxConcurrentRooms < 1)
				errors.Add( "Performance.MaxConcurrentRooms must be at least 1" );

			if(options.Performance.MaxJobQueueSize < 10)
				errors.Add( "Performance.MaxJobQueueSize must be at least 10" );

			if(options.Performance.BroadcastBatchSize < 1)
				errors.Add( "Performance.BroadcastBatchSize must be at least 1" );

			return errors.Count == 0
				? ValidateOptionsResult.Success
				: ValidateOptionsResult.Fail( errors );
		}
	}
}
