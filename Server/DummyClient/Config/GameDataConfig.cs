using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace DummyClient.Config
{
	public class GameDataConfig
	{
		public string DataPath { get; set; } = "GameData";
		public string GetDataFilePath( string tableName ) => Path.Combine( DataPath, $"{tableName}.json" );
	}
}
