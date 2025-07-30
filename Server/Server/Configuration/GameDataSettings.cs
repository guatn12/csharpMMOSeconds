using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Server.Configuration
{
	public class GameDataSettings
	{
		public string DataPath { get; set; } = "GameData";
		public string FileExtension { get; set; } = ".json";
		public bool EnableHotReload { get; set; } = false;
		public int HotReloadDebounceMs { get; set; } = 500;

		// 파일 경로 동적 생성 메서드
		public string GetDataFilePath( string tableName ) =>
			Path.Combine( DataPath, $"{tableName}{FileExtension}" );
	}
}
