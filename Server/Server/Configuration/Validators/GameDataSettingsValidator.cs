using Microsoft.Extensions.Options;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Server.Configuration.Validators
{
	public class GameDataSettingsValidator : IValidateOptions<GameDataSettings>
	{
		public ValidateOptionsResult Validate(string name, GameDataSettings options)
		{
			if(string.IsNullOrWhiteSpace( options.DataPath ))
				return ValidateOptionsResult.Fail( "DataPath는 필수 데이터 입니다." );

			if(string.IsNullOrWhiteSpace( options.FileExtension ))
				return ValidateOptionsResult.Fail( "FileExtension은 필수 데이터 입니다." );

			if(options.HotReloadDebounceMs < 0)
				return ValidateOptionsResult.Fail( "HotReloadDebounceMs는 0보다 큰 값이어야 합니다." );

			return ValidateOptionsResult.Success;
		}
	}
}
