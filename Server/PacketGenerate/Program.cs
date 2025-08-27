using System;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using System.Collections.Generic; // Added for List<Tuple> and HashSet
using System.Linq; // Added for Distinct

namespace PacketGenerator
{
	public class Program
	{
		static void Main( string[] args )
		{
			// 현재 프로젝트의 경로를 기준으로 상대 경로 설정
			string generatorDir = Directory.GetCurrentDirectory();

			// 아웃풋 경로 변경.
			string outputDir = Path.Combine(generatorDir, "Generated");

			string solutionDir = FindSolutionDirectory(generatorDir);

			// proto 파일 경로
			string protoPath = args[0];

			//파일 읽기 및 파싱
			string protoText = File.ReadAllText(protoPath);
			ParseProto( protoText, outputDir );

			Console.WriteLine( "PacketGenerator Executed Successfully" );
		}

		static string FindSolutionDirectory( string currentDir )
		{
			while(currentDir != null)
			{
				if(0 < Directory.GetFiles(currentDir, "*.sln").Length)
				{
					currentDir = Directory.GetParent(currentDir).FullName;
					return currentDir;
				}
				currentDir = Directory.GetParent( currentDir )?.FullName;
			}

			return null;
		}

		static void ParseProto(string protoText, string outputDir)
		{
			// 정규식을 사용하여 MsgId enum 블록 추출
			Match idMatch = Regex.Match(protoText, @"enum\s+MsgId\s*\{([\s\S]*?)\}");
			if (!idMatch.Success)
			{
				Console.WriteLine("Could not find MsgId enum in .proto file.");
				return;
			}

			string idBlock = idMatch.Groups[1].Value;

			// enum 멤버들 추출(이름과 번호)
			var idRegex = new Regex(@"(\w+)\s*=\s*(\d+);");
			var matches = idRegex.Matches(idBlock);

			// Extract message names and their handler_name options
			List<Tuple<string, string, string>> packetInfoList = new List<Tuple<string, string, string>>();
			Regex messageRegex = new Regex(@"message\s+(\w+)\s*\{[^}]*?option\s+\(handler_name\)\s*=\s*""(\w+)"";", RegexOptions.Singleline);
			MatchCollection messageMatches = messageRegex.Matches(protoText);

			foreach (Match messageMatch in messageMatches)
			{
				string messageName = messageMatch.Groups[1].Value.Trim(); // Add .Trim() here
				string handlerName = messageMatch.Groups[2].Value;
				string packetType = "";
				if (messageName.StartsWith("C_"))
				{
					packetType = "C_";
				}
				else if (messageName.StartsWith("S_"))
				{
					packetType = "S_";
				}
				packetInfoList.Add(Tuple.Create(messageName, handlerName, packetType));
			}

			// 서버용 PacketID enum 생성
			StringBuilder serverPacketEnum = new StringBuilder();
			GeneratePacketEnum(matches, serverPacketEnum, "Server");
			File.WriteAllText("Gen_Server_PacketID.cs", serverPacketEnum.ToString());

			// 클라이언트(unity)용 PacketID enum 생성
			StringBuilder clientPacketEnum = new StringBuilder();
			GeneratePacketEnum(matches, clientPacketEnum, "Client");
			File.WriteAllText("Gen_Client_PacketID.cs", clientPacketEnum.ToString());

			// 서버용 PacketManager 생성 ( 새로운 구조 )
			StringBuilder ServerManager = new StringBuilder();
			GeneratePacketManager(matches, ServerManager, "Server", packetInfoList);
			File.WriteAllText("Gen_Server_PacketManager.cs", ServerManager.ToString());

			// 클라이언트(unity)용 Packetmanager 생성
			StringBuilder ClientManager = new StringBuilder();
			GeneratePacketManager(matches, ClientManager, "Client", packetInfoList);
			File.WriteAllText("Gen_Client_PacketManager.cs", ClientManager.ToString());

			// 클라이언트(unreal)용 c++ 헤더 생성
			StringBuilder unrealHeader = new StringBuilder();
			GenerateUnrealHeader(matches, unrealHeader);
			File.WriteAllText("Gen_PacketID.h", unrealHeader.ToString());

			// Generate IPacketHandler interfaces (Removed calls to GenerateIPacketHandler)
		}

		static void GeneratePacketEnum (MatchCollection matches, StringBuilder sb, string target)
		{
			sb.AppendLine( "// [자동 생성] Protocol.proto 파일을 기반으로 자동 생성된 코드입니다." );
			sb.AppendLine( $"// Target: {target}" );
			sb.AppendLine();
			sb.AppendLine( "public enum PacketID" );
			sb.AppendLine( "{" );
			foreach( Match match in matches )
			{
				string pName = MakeEnumNameToCamelName(match.Groups[1].Value.Trim());

				sb.AppendLine( $"\t{pName} = {match.Groups[ 2 ].Value.Trim()}," );
			}
			sb.AppendLine( "}" );
		}

		static void GeneratePacketManager(MatchCollection matches, StringBuilder sb, string target, List<Tuple<string, string, string>> packetInfoList)
		{
			if (target == "Server")
			{
				GenerateNewServerPacketManager( matches, sb, packetInfoList );
			}
			else // client
			{
				GenerateOldClientPacketManager( matches, sb, packetInfoList );
			}
		}

		private static void GenerateNewServerPacketManager(MatchCollection matches, StringBuilder sb, List<Tuple<string, string, string>> packetInfoList)
		{
			sb.AppendLine( "// [자동 생성] 새로운 제네릭 Job 시스템용 PacketManager" );
			sb.AppendLine( "// Target: Server" );
			sb.AppendLine();
			sb.AppendLine( "using Google.Protobuf;" );
			sb.AppendLine( "using Protocol;" );
			sb.AppendLine( "using Microsoft.Extensions.Logging;" );
			sb.AppendLine( "using Server.Jobs;" );
			sb.AppendLine( "using Server.Room;" );
			sb.AppendLine( "using ServerCore;" );
			sb.AppendLine( "using System;" );
			sb.AppendLine( "using System.Collections.Generic;" );
			sb.AppendLine( "using System.Threading.Tasks;" );
			sb.AppendLine();
			sb.AppendLine( "namespace Server.Packet" );
			sb.AppendLine( "{" );
			sb.AppendLine( "    public class PacketManager" );
			sb.AppendLine( "    {" );

			// 필드 선언
			sb.AppendLine( "        private readonly JobPool _jobPool;" );
			sb.AppendLine( "        private readonly JobQueueManager _jobQueueManager;" );
			sb.AppendLine( "        private readonly ILogger<PacketManager> _logger;" );
			sb.AppendLine( "        private readonly Dictionary<ushort, Func<GameSession, ArraySegment<byte>, ValueTask>> _onRecv;" );
			sb.AppendLine( "        private static readonly Dictionary<Type, Func<GameSession, IRoom, IMessage, ILogger, ValueTask>> _packetLogicMap;" );
			sb.AppendLine( "        private readonly Dictionary<Type, PacketID> _packetTypeToId;" );
			sb.AppendLine();

			// 정적 생성자 - 패킷 로직 매핑
			sb.AppendLine( "        static PacketManager()" );
			sb.AppendLine( "        {" );
			sb.AppendLine( "            _packetLogicMap = new Dictionary<Type, Func<GameSession, IRoom, IMessage, ILogger, ValueTask>>" );
			sb.AppendLine( "            {" );

			foreach(Match match in matches)
			{
				string pName = MakeEnumNameToCamelName(match.Groups[1].Value.Trim());

				if(pName.StartsWith( "C_" ))
				{
					// C_Move → HandlePlayerMoveAsync, C_Chat → HandlePlayerChatAsync
					string methodName = pName.Replace("C_", "HandlePlayer") + "Async";
					sb.AppendLine( $"                [typeof({pName})] = async (session, room, packet, logger) =>" );
					sb.AppendLine( $"                    await (room?.{methodName}(session, ({pName})packet, logger) ?? Task.CompletedTask)," );
				}
			}

			sb.AppendLine( "            };" );
			sb.AppendLine( "        }" );
			sb.AppendLine();

			// 생성자
			sb.AppendLine( "        public PacketManager(JobPool jobPool, JobQueueManager jobQueueManager, ILogger<PacketManager> logger)" );
			sb.AppendLine( "        {" );
			sb.AppendLine( "            _jobPool = jobPool ?? throw new ArgumentNullException(nameof(jobPool));" );
			sb.AppendLine( "            _jobQueueManager = jobQueueManager ?? throw new ArgumentNullException(nameof(jobQueueManager));" );
			sb.AppendLine( "            _logger = logger ?? throw new ArgumentNullException(nameof(logger));" );
			sb.AppendLine( "            _onRecv = new Dictionary<ushort, Func<GameSession, ArraySegment<byte>, ValueTask>>();" );
			sb.AppendLine( "            _packetTypeToId = new Dictionary<Type, PacketID>();" );
			sb.AppendLine( "            Register();" );
			sb.AppendLine( "        }" );
			sb.AppendLine();

			// Register 메서드
			sb.AppendLine( "        private void Register()" );
			sb.AppendLine( "        {" );

			foreach(Match match in matches)
			{
				string pName = MakeEnumNameToCamelName(match.Groups[1].Value.Trim());

				if(pName.StartsWith( "C_" ))
				{
					sb.AppendLine( $"            _onRecv.Add((ushort)PacketID.{pName}, Handle{pName}Async);" );
				}
			}

			// S_ 패킷들을 _packetTypeToId에 등록 (서버가 클라이언트에게 전송하는 패킷)
			foreach(Match match in matches)
			{
				string pName = MakeEnumNameToCamelName(match.Groups[1].Value.Trim());

				if(pName.StartsWith( "S_" ))
				{
					sb.AppendLine( $"            _packetTypeToId.Add(typeof({pName}), PacketID.{pName});" );
				}
			}

			sb.AppendLine( "        }" );
			sb.AppendLine();

			// 개별 핸들러 메서드들 생성
			foreach(Match match in matches)
			{
				string pName = MakeEnumNameToCamelName(match.Groups[1].Value.Trim());

				if(pName.StartsWith( "C_" ))
				{
					sb.AppendLine( $"        private async ValueTask Handle{pName}Async(GameSession session, ArraySegment<byte> buffer)" );
					sb.AppendLine( "        {" );
					sb.AppendLine( $"            var packet = new {pName}();" );
					sb.AppendLine( "            packet.MergeFrom(buffer.Array, buffer.Offset, buffer.Count);" );
					sb.AppendLine( $"            await HandlePacketLogic<{pName}>(session, packet);" );
					sb.AppendLine( "        }" );
					sb.AppendLine();
				}
			}

			// 제네릭 핸들러 메서드
			sb.AppendLine( "        private async ValueTask HandlePacketLogic<T>(GameSession session, T packet) where T : IMessage" );
			sb.AppendLine( "        {" );
			sb.AppendLine( "            try" );
			sb.AppendLine( "            {" );
			sb.AppendLine( "                var room = session.CurrentRoom;" );
			sb.AppendLine( "                " );
			sb.AppendLine( "                // 핸들러 검색" );
			sb.AppendLine( "                if (!_packetLogicMap.TryGetValue(typeof(T), out var handler))" );
			sb.AppendLine( "                {" );
			sb.AppendLine( "                    _logger.LogWarning(\"No handler found for packet type: {PacketType}\", typeof(T).Name);" );
			sb.AppendLine( "                    return;" );
			sb.AppendLine( "                }" );
			sb.AppendLine();
			sb.AppendLine( "                // PacketJob 생성 및 설정" );
			sb.AppendLine( "                var job = _jobPool.Get<PacketJob<T>>();" );
			sb.AppendLine( "                job.Initialize(session, room, packet, _logger);" );
			sb.AppendLine( "                job.SetHandler(handler);" );
			sb.AppendLine();
			sb.AppendLine( "                // Job Queue에 추가" );
			sb.AppendLine( "                await _jobQueueManager.PushAsync(job);" );
			sb.AppendLine( "            }" );
			sb.AppendLine( "            catch (Exception ex)" );
			sb.AppendLine( "            {" );
			sb.AppendLine( "                _logger.LogError(ex, \"Error handling packet {PacketType} from session {SessionId}\"," );
			sb.AppendLine( "                    typeof(T).Name, session.SessionId);" );
			sb.AppendLine( "            }" );
			sb.AppendLine( "        }" );
			sb.AppendLine();

			// HandlePacket 메서드
			sb.AppendLine( "        public async ValueTask HandlePacket(GameSession session, ArraySegment<byte> buffer)" );
			sb.AppendLine( "        {" );
			sb.AppendLine( "            ushort count = 0;" );
			sb.AppendLine( "            ushort size = BitConverter.ToUInt16(buffer.Array, buffer.Offset);" );
			sb.AppendLine( "            count += 2;" );
			sb.AppendLine( "            ushort id = BitConverter.ToUInt16(buffer.Array, buffer.Offset + count);" );
			sb.AppendLine( "            count += 2;" );
			sb.AppendLine();
			sb.AppendLine( "            if (_onRecv.TryGetValue(id, out var handler))" );
			sb.AppendLine( "            {" );
			sb.AppendLine( "                await handler(session, new ArraySegment<byte>(buffer.Array, buffer.Offset + count, size - count));" );
			sb.AppendLine( "            }" );
			sb.AppendLine( "            else" );
			sb.AppendLine( "            {" );
			sb.AppendLine( "                _logger.LogWarning(\"Unknown packet ID: {PacketId} from session {SessionId}\", id, session.SessionId);" );
			sb.AppendLine( "            }" );
			sb.AppendLine( "        }" );
			sb.AppendLine();
			sb.AppendLine( "        // 패킷 직렬화 및 전송용 버퍼 생성" );
			sb.AppendLine( "        public ArraySegment<byte> MakeSendPacket(IMessage packet)" );
			sb.AppendLine( "        {" );
			sb.AppendLine( "            PacketID packetId;" );
			sb.AppendLine( "            bool getValue = _packetTypeToId.TryGetValue(packet.GetType(), out packetId);" );
			sb.AppendLine( "            if (!getValue)" );
			sb.AppendLine( "            {" );
			sb.AppendLine( "                _logger.LogWarning(\"Unknown packet type for MakeSendPacket: {PacketType}\", packet.GetType().Name);" );
			sb.AppendLine( "                return new ArraySegment<byte>();" );
			sb.AppendLine( "            }" );
			sb.AppendLine();
			sb.AppendLine( "            ushort size = (ushort)packet.CalculateSize();" );
			sb.AppendLine( "            byte[] buffer = new byte[size + 4];" );
			sb.AppendLine( "            Array.Copy(BitConverter.GetBytes((ushort)(size + 4)), 0, buffer, 0, sizeof(ushort));" );
			sb.AppendLine( "            Array.Copy(BitConverter.GetBytes((ushort)packetId), 0, buffer, 2, sizeof(ushort));" );
			sb.AppendLine( "            packet.WriteTo(new System.IO.MemoryStream(buffer, 4, size));" );
			sb.AppendLine( "            return new ArraySegment<byte>(buffer);" );
			sb.AppendLine( "        }" );
			sb.AppendLine( "    }" );
			sb.AppendLine( "}" );
		}

		private static void GenerateOldClientPacketManager(MatchCollection matches, StringBuilder sb, List<Tuple<string, string, string>> packetInfoList)
		{
			// 기존 GeneratePacketManager 로직을 그대로 유지
			sb.AppendLine( "// [자동 생성] Protocol.proto 파일을 기반으로 자동 생성된 코드입니다." );
			sb.AppendLine( "// Target: Client (기존 구조 유지 - DummyClient 호환성)" );
			sb.AppendLine();
			sb.AppendLine( "using ServerCore;" );
			sb.AppendLine( "using System;" );
			sb.AppendLine( "using System.Collections.Generic;" );
			sb.AppendLine( "using Google.Protobuf;" );
			sb.AppendLine( "using Protocol;" );
			sb.AppendLine();
			sb.AppendLine( "public class PacketManager" );
			sb.AppendLine( "{" );
			sb.AppendLine( "    IPacketHandler _handler;" );
			sb.AppendLine( "    Dictionary<ushort, Action<Session, ArraySegment<byte>>> _onRecv = new Dictionary<ushort, Action<Session, ArraySegment<byte>>>(); ");
			sb.AppendLine( "    Dictionary<ushort, Func<IMessage, ArraySegment<byte>>> _makePacket = new Dictionary<ushort, Func<IMessage, ArraySegment<byte>>>(); ");
			sb.AppendLine( "    Dictionary<Type, PacketID> _packetTypeToId = new Dictionary<Type, PacketID>();" );
			sb.AppendLine();
			sb.AppendLine( "    public PacketManager(IPacketHandler handler)" );
			sb.AppendLine( "    {" );
			sb.AppendLine( "        _handler = handler;" );
			sb.AppendLine( "        Register();" );
			sb.AppendLine( "    }" );
			sb.AppendLine();
			sb.AppendLine( "    // 핸들러 자동 등록" );
			sb.AppendLine( "    public void Register()" );
			sb.AppendLine( "    {" );

			foreach(Match match in matches)
			{
				string pName = MakeEnumNameToCamelName(match.Groups[1].Value.Trim());

				// Find packet info for the current packet
				Tuple<string, string, string> currentPacketInfo = packetInfoList.Find(p => p.Item1 == pName);
				string handlerName = currentPacketInfo?.Item2;
				string packetType = currentPacketInfo?.Item3;

				// 클라이언트는 S_로 시작하는 패킷을 생성하고, C_로 시작하는 패킷을 받음
				if(pName.StartsWith( "C_" ))
				{
					sb.AppendLine( $"        _makePacket.Add((ushort)PacketID.{pName}, MakeSendPacket);" );
					sb.AppendLine( $"        _packetTypeToId.Add(typeof(Protocol.{pName}), PacketID.{pName});" );
				}
				else if(pName.StartsWith( "S_" ))
				{
					sb.AppendLine( $"        _onRecv.Add((ushort)PacketID.{pName}, (s, b) => HandlePacket<Protocol.{pName}>(s, b, _handler.{ handlerName}.On_{ pName})); ");
	  
				}
			}

			sb.AppendLine( "    }" );
			sb.AppendLine();

			// 기존 HandlePacket, MakeSendPacket 메서드들 유지
			sb.AppendLine( "    // 패킷 진입 처리점" );
			sb.AppendLine( "    public void HandlePacket(Session session, ArraySegment<byte> buffer)" );
			sb.AppendLine( "    {" );
			sb.AppendLine( "        ushort count = 0;" );
			sb.AppendLine( "        ushort size = BitConverter.ToUInt16(buffer.Array, buffer.Offset);" );
			sb.AppendLine( "        count += 2;" );
			sb.AppendLine( "        ushort id = BitConverter.ToUInt16(buffer.Array, buffer.Offset + count);" );
			sb.AppendLine( "        count += 2;" );
			sb.AppendLine();
			sb.AppendLine( "        if (_onRecv.TryGetValue(id, out var action))" );
			sb.AppendLine( "        {" );
			sb.AppendLine( "            action.Invoke(session, new ArraySegment<byte>(buffer.Array, buffer.Offset + count, size - count));" );
			sb.AppendLine( "        }" );
			sb.AppendLine( "    }" );
			sb.AppendLine();

			sb.AppendLine( "    // 패킷 처리 로직" );
			sb.AppendLine( "    private void HandlePacket<T>(Session session, ArraySegment<byte> buffer, Action<Session, T> handler) where T : IMessage, new()");
	  
			sb.AppendLine( "    {" );
			sb.AppendLine( "        T pkt = new T();" );
			sb.AppendLine( "        pkt.MergeFrom(buffer);" );
			sb.AppendLine( "        handler.Invoke(session, pkt);" );
			sb.AppendLine( "    }" );
			sb.AppendLine();

			sb.AppendLine( "    // 신규 패킷 생성 로직" );
			sb.AppendLine( "    public ArraySegment<byte> MakeSendPacket(IMessage Packet)" );
			sb.AppendLine( "    {" );
			sb.AppendLine( "        PacketID packetId;" );
			sb.AppendLine( "        bool getValue = _packetTypeToId.TryGetValue(Packet.GetType(), out packetId);" );
			sb.AppendLine( "        if (!getValue)" );
			sb.AppendLine( "            return new ArraySegment<byte>();" );
			sb.AppendLine( "        ushort size = (ushort)Packet.CalculateSize();" );
			sb.AppendLine( "        byte[] buffer = new byte[size+4];" );
			sb.AppendLine( "        Array.Copy(BitConverter.GetBytes((ushort)(size + 4)), 0, buffer, 0, sizeof(ushort));" );
			sb.AppendLine( "        Array.Copy(BitConverter.GetBytes((ushort)packetId), 0, buffer, 2, sizeof(ushort));" );
			sb.AppendLine( "        Packet.WriteTo(new System.IO.MemoryStream(buffer, 4, size));" );
			sb.AppendLine( "        return new ArraySegment<byte>(buffer);" );
			sb.AppendLine( "    }" );
			sb.AppendLine();
			sb.AppendLine( "}" );
		}

		static void GenerateUnrealHeader(MatchCollection matches, StringBuilder sb)
		{
			sb.AppendLine( "// [자동 생성] Protocol.proto 파일을 기반으로 자동 생성된 코드입니다." );
			sb.AppendLine( "// Target: Unreal Engine (C++)" );
			sb.AppendLine();
			sb.AppendLine( "#pragma once" );
			sb.AppendLine();
			sb.AppendLine( "$include \"CoreMinimal.h\"" );
			sb.AppendLine();
			sb.AppendLine( "enum class EPacketID : uint16" );
			sb.AppendLine( "{" );

			foreach(Match match in matches )
			{
				string name = match.Groups[1].Value.Trim();
				string pName = MakeEnumNameToCamelName(name);

				sb.AppendLine( $"\t{pName} = {match.Groups[ 2 ].Value.Trim()}," );
			}

			sb.AppendLine("};");
		}

		static void GenerateIPacketHandler(List<Tuple<string, string, string>> packetInfoList, string target)
		{
			// Collect unique handler names
			HashSet<string> handlerNames = new HashSet<string>();
			foreach (var packetInfo in packetInfoList)
			{
				// Only consider packets relevant to the target (Server receives C_, Client receives S_)
				if ((target == "Server" && packetInfo.Item3 == "C_") || (target == "Client" && packetInfo.Item3 == "S_"))
				{
					handlerNames.Add(packetInfo.Item2);
				}
			}

			// Generate individual handler interfaces
			foreach (string handlerName in handlerNames)
			{
				StringBuilder sb = new StringBuilder();
				sb.AppendLine("// [자동 생성] Protocol.proto 파일을 기반으로 자동 생성된 코드입니다.");
				sb.AppendLine($"// Target: {target}");
				sb.AppendLine();
				sb.AppendLine("using ServerCore;");
				sb.AppendLine("using Protocol;");
				sb.AppendLine();
				sb.AppendLine($"public interface I{handlerName}");
				sb.AppendLine("{");

				foreach (var packetInfo in packetInfoList)
				{
					if (packetInfo.Item2 == handlerName)
					{
						string pName = MakeEnumNameToCamelName(packetInfo.Item1);
						// Only generate handler methods for packets relevant to the target
						if ((target == "Server" && pName.StartsWith("C_")) || (target == "Client" && pName.StartsWith("S_")))
						{
							sb.AppendLine($"\tvoid On_{pName}(Session session, {pName} packet);" );
						}
					}
				}
				sb.AppendLine("}");
				File.WriteAllText($"Gen_{handlerName}.cs", sb.ToString());
			}

			// Generate main IPacketHandler interface
			StringBuilder mainSb = new StringBuilder();
			mainSb.AppendLine("// [자동 생성] Protocol.proto 파일을 기반으로 자동 생성된 코드입니다.");
			mainSb.AppendLine($"// Target: {target}");
			mainSb.AppendLine();
			mainSb.AppendLine("using ServerCore;");
			mainSb.AppendLine("using Protocol;");
			mainSb.AppendLine();
			mainSb.AppendLine("public interface IPacketHandler");
			mainSb.AppendLine("{");
			foreach (string handlerName in handlerNames)
			{
				mainSb.AppendLine($"\tI{handlerName} {handlerName} {{ get; }}"); // Properties for each specific handler interface
			}
			mainSb.AppendLine("}");
			File.WriteAllText($"Gen_IPacketHandler.cs", mainSb.ToString());
		}

		static private string MakeEnumNameToCamelName( string name )
		{
			if (string.IsNullOrEmpty(name))
				return string.Empty;

			string cName = "";
			string[] parts = name.Split('_');
			bool first = true;
			foreach(string part in parts)
			{
				if(first)
				{
					first = false;
					cName += char.ToUpper( part[ 0 ] ) + "_";
				}
				else
					cName += char.ToUpper( part[ 0 ] ) + part.Substring( 1 ).ToLower();
			}

			return cName;
		}
	}
}