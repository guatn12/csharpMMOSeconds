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

			// 서버용 PacketManager 생성
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
			sb.AppendLine( "// [자동 생성] Protocol.proto 파일을 기반으로 자동 생성된 코드입니다." );
			sb.AppendLine( $"// Target: {target}" );
			sb.AppendLine();
			sb.AppendLine( "using ServerCore;" );
			sb.AppendLine( "using System;" );
			sb.AppendLine( "using System.Collections.Generic;" );
			sb.AppendLine( "using Google.Protobuf;" );
			sb.AppendLine( "using Protocol;" ); // Added using Protocol;
			sb.AppendLine();
			sb.AppendLine( "public class PacketManager" );
			sb.AppendLine( "{" );
			sb.AppendLine( "\tIPacketHandler _handler;" ); // Changed back to IPacketHandler
			sb.AppendLine( "\tDictionary<ushort, Action<Session, ArraySegment<byte>>> _onRecv = new Dictionary<ushort, Action<Session, ArraySegment<byte>>>();" );
			sb.AppendLine( "\tDictionary<ushort, Func<IMessage, ArraySegment<byte>>> _makePacket = new Dictionary<ushort, Func<IMessage, ArraySegment<byte>>>();" );
			sb.AppendLine( "\tDictionary<Type, PacketID> _packetTypeToId = new Dictionary<Type, PacketID>();" );
			sb.AppendLine();
			sb.AppendLine( "\tpublic PacketManager( IPacketHandler handler )" ); // Changed constructor parameter
			sb.AppendLine( "\t{" );
			sb.AppendLine( "\t\t_handler = handler;" ); // Assign handler
			sb.AppendLine( "\t\tRegister();" );
			sb.AppendLine( "\t}" );
			sb.AppendLine();
			sb.AppendLine( "\t// 핸들러 자동 등록" );
			sb.AppendLine( "\tpublic void Register()" );
			sb.AppendLine( "\t{" );

			foreach(Match match in matches )
			{
				string pName = MakeEnumNameToCamelName(match.Groups[1].Value.Trim());

				// Find packet info for the current packet
				Tuple<string, string, string> currentPacketInfo = packetInfoList.Find(p => p.Item1 == pName); // Corrected comparison
				string handlerName = currentPacketInfo?.Item2;
				string packetType = currentPacketInfo?.Item3;

				// 서버는 S_로 시작하는 패킷을 생성하고, 클라이언트는 C_로 시작하는 패킷을 생성.
				if (target == "Server" && pName.StartsWith("S_"))
				{
					sb.AppendLine( $"\t\t_makePacket.Add((ushort)PacketID.{pName}, MakeSendPacket);" );
					sb.AppendLine( $"\t\t_packetTypeToId.Add( typeof( Protocol.{pName} ), PacketID.{pName} );" );
				}
				else if(target == "Client" && pName.StartsWith("C_"))
				{
					sb.AppendLine( $"\t\t_makePacket.Add((ushort)PacketID.{pName}, MakeSendPacket);" );
					sb.AppendLine( $"\t\t_packetTypeToId.Add( typeof( Protocol.{pName} ), PacketID.{pName} );" );
				}

				// 서버는 C_로 시작하는 패킷을 받고, 클라이언트는 S_로 시작하는 패킷을 받음
				if (target == "Server" && pName.StartsWith("C_"))
				{
					sb.AppendLine( $"\t\t_onRecv.Add( (ushort)PacketID.{pName}, ( s, b ) => HandlePacket<Protocol.{pName}>( s, b, _handler.{handlerName}.On_{pName} ) );" ); // Modified handler call
				}
				else if(target == "Client" && pName.StartsWith("S_"))
				{
					sb.AppendLine( $"\t\t_onRecv.Add( (ushort)PacketID.{pName}, ( s, b ) => HandlePacket<Protocol.{pName}>( s, b, _handler.{handlerName}.On_{pName} ) );" ); // Modified handler call
				}
			}

			sb.AppendLine( "\t}" );
			sb.AppendLine();

			sb.AppendLine( "\t// 패킷 진입 처리점" );
			sb.AppendLine( "\tpublic void HandlePacket( Session session, ArraySegment<byte> buffer )" );
			sb.AppendLine( "\t{" );
			sb.AppendLine( "\t\tushort count = 0;" );
			sb.AppendLine( "\t\tushort size = BitConverter.ToUInt16(buffer.Array, buffer.Offset);" );
			sb.AppendLine( "\t\tcount += 2;" );
			sb.AppendLine( "\t\tushort id = BitConverter.ToUInt16(buffer.Array, buffer.Offset + count);" );
			sb.AppendLine( "\t\tcount += 2;" );
			sb.AppendLine( "" );
			sb.AppendLine( "\t\tif(_onRecv.TryGetValue( id, out var action ))" );
			sb.AppendLine( "\t\t{" );
			sb.AppendLine( "\t\t\taction.Invoke( session, new ArraySegment<byte>( buffer.Array, buffer.Offset + count, size - count ) );" );
			sb.AppendLine( "\t\t}" );
			sb.AppendLine( "\t}" );
			sb.AppendLine( "" );

			sb.AppendLine( "\t // 패킷 처리 로직" );
			sb.AppendLine( "\tprivate void HandlePacket<T>(Session session, ArraySegment<byte> buffer, Action<Session, T> handler) where T : IMessage, new()" );
			sb.AppendLine( "\t{" );
			sb.AppendLine( "\t\tT pkt = new T();" );
			sb.AppendLine( "\t\tpkt.MergeFrom(buffer);" );
			sb.AppendLine( "\t\thandler.Invoke(session, pkt);" );
			sb.AppendLine( "\t}" );
			sb.AppendLine();
			sb.AppendLine( "\t// 신규 패킷 생성 로직" );
			sb.AppendLine( "\tpublic ArraySegment<byte> MakeSendPacket(IMessage Packet)" );
			sb.AppendLine( "\t{" );
			sb.AppendLine( $"\t\tPacketID packetId;" );
			sb.AppendLine( $"\t\tbool getValue = _packetTypeToId.TryGetValue( Packet.GetType(), out packetId );" );
			sb.AppendLine( $"\t\tif (!getValue)" );
			sb.AppendLine( $"\t\t\treturn new ArraySegment<byte>();" );
			sb.AppendLine( "\t\tushort size = (ushort)Packet.CalculateSize();" );
			sb.AppendLine( "\t\tbyte[] buffer = new byte[size+4];" );
			sb.AppendLine( "\t\tArray.Copy(BitConverter.GetBytes((ushort)(size + 4)), 0, buffer, 0, sizeof(ushort));" );
			sb.AppendLine( "\t\tArray.Copy(BitConverter.GetBytes((ushort)packetId), 0, buffer, 2, sizeof(ushort));" );
			sb.AppendLine( "\t\tPacket.WriteTo(new System.IO.MemoryStream(buffer, 4, size));" );
			sb.AppendLine( "\t\treturn new ArraySegment<byte>(buffer);" );
			sb.AppendLine( "\t}" );
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