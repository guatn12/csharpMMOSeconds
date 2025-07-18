using System;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;

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

		static void ParseProto(string protoText, string outputDir )
		{
			// 정규식을 사용하여 MsgId enum 블록 추출
			Match idMatch = Regex.Match(protoText, @"enum\s+MsgId\s*\{([\s\S]*?)\}");
			if(!idMatch.Success)
			{
				Console.WriteLine( "Could not find MsgId enum in .proto file." );
				return;
			}

			string idBlock = idMatch.Groups[1].Value;

			// enum 멤버들 추출(이름과 번호)
			var idRegex = new Regex(@"(\w+)\s*=\s*(\d+);");
			var matches = idRegex.Matches(idBlock);

			// 서버용 PacketID enum 생성
			StringBuilder serverPacketEnum = new StringBuilder();
			GeneratePacketEnum( matches, serverPacketEnum, "Server" );
			File.WriteAllText( "Gen_Server_PacketID.cs", serverPacketEnum.ToString() );

			// 클라이언트(unity)용 PacketID enum 생성
			StringBuilder clientPacketEnum = new StringBuilder();
			GeneratePacketEnum( matches, clientPacketEnum, "Client" );
			File.WriteAllText( "Gen_Client_PacketID.cs", clientPacketEnum.ToString() );

			// 서버용 PacketManager 생성
			StringBuilder ServerManager = new StringBuilder();
			GeneratePacketManager( matches, ServerManager, "Server" );
			File.WriteAllText ( "Gen_Server_PacketManager.cs", ServerManager.ToString() );

			// 클라이언트(unity)용 Packetmanager 생성
			StringBuilder ClientManager = new StringBuilder();
			GeneratePacketManager( matches, ClientManager, "Client" );
			File.WriteAllText( "Gen_Client_PacketManager.cs", ClientManager.ToString() );

			// 클라이언트(unreal)용 c++ 헤더 생성
			StringBuilder unrealHeader = new StringBuilder();
			GenerateUnrealHeader( matches, unrealHeader );
			File.WriteAllText( "Gen_PacketID.h", unrealHeader.ToString() );
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
				sb.AppendLine( $"\t{match.Groups[ 1 ].Value.Trim()} = {match.Groups[ 2 ].Value.Trim()}," );
			}
			sb.AppendLine( "}" );
		}

		static void GeneratePacketManager(MatchCollection matches, StringBuilder sb, string target)
		{
			sb.AppendLine( "// [자동 생성] Protocol.proto 파일을 기반으로 자동 생성된 코드입니다." );
			sb.AppendLine( $"// Target: {target}" );
			sb.AppendLine();
			sb.AppendLine( "using ServerCore;" );
			sb.AppendLine( "using System;" );
			sb.AppendLine( "using System.Collections.Generic;" );
			sb.AppendLine( "using Google.Protobuf;" );
			sb.AppendLine();
			sb.AppendLine( "public partial class PacketManager" );
			sb.AppendLine( "{" );
			sb.AppendLine( "\tpublic static PacketManager Instance { get; } = new PacketManager();" );
			sb.AppendLine( "\tDictionary<ushort, Action<PacketSession, ArraySegment<byte>>> _onRecv = new Dictionary<ushort, Action<PacketSession, ArraySegment<byte>>>();" );
			sb.AppendLine( "\tDictionary<ushort, Func<IMessage, ArraySegment<byte>>> _makePacket = new Dictionary<ushort, Func<IMessage, ArraySegment<byte>>>();" );
			sb.AppendLine();
			sb.AppendLine( "\tPacketManager() { Register(); }" );
			sb.AppendLine( "\t// 핸들러 자동 등록" );
			sb.AppendLine( "\tpublic void Register()" );
			sb.AppendLine( "\t{" );

			foreach(Match match in matches )
			{
				string name = match.Groups[1].Value.Trim();

				sb.AppendLine( $"\t\t_makePacket.Add((ushort)PacketID.{name}, MakeSendPacket);" );

				// 서버는 C_로 시작하는 패킷을 받고, 클라이언트는 S_로 시작하는 패킷을 받음
				if (target == "Server" && name.StartsWith("C_"))
				{
					sb.AppendLine( $"\t\t_onRecv.Add((ushort)PacketID.{name}, (s, b) => HandlePacket<Protocol.{name}>(s, b, On_{name}));" );
				}
				else if(target == "Client" && name.StartsWith("S_"))
				{
					sb.AppendLine( $"\t\t_onRecv.Add((ushort)PacketID.{name}, (s, b) => HandlePacket<Protocol.{name}>(s, b, On_{name}));" );
				}
			}

			sb.AppendLine( "\t}" );
			sb.AppendLine();
			sb.AppendLine( "\t // 패킷 처리 로직" );
			sb.AppendLine( "\tvoid HandlePacket<T>(PacketSession session, ArraySegment<byte> buffer, Action<PacketSession, T> handler) where T : Imessage, new()" );
			sb.AppendLine( "\t{" );
			sb.AppendLine( "\t\tT pkt = new T();" );
			sb.AppendLine( "\t\tpkt.MergeFrom(buffer);" );
			sb.AppendLine( "\t\thandler.Invoke(session, pkt);" );
			sb.AppendLine( "\t}" );
			sb.AppendLine();
			sb.AppendLine( "\t// 신규 패킷 생성 로직" );
			sb.AppendLine( "\tArraySegment<byte> MakeSendPacket(IMessage Packet)" );
			sb.AppendLine( "\t{" );
			sb.AppendLine( "\t\tushort packetId = (ushort)Enum.Parse<PacketID>(packet.Descriptor.Name);" );
			sb.AppendLine( "\t\tushort size = (ushort)packet.CalculateSize();" );
			sb.AppendLine( "\t\tbyte[] buffer = new byte[size+4];" );
			sb.AppendLine( "\t\tArray.Copy(BitConverter.GetBytes((ushort)(size + 4)), 0, buffer, 0, sizeof(ushort));" );
			sb.AppendLine( "\t\tArray.Copy(BitConverter.GetBytes(packetId), 0, buffer, 2, sizeof(ushort));" );
			sb.AppendLine( "\t\tpacket.WriteTo(new System.IO.MemoryStream(buffer, 4, size));" );
			sb.AppendLine( "\t\treturn new ArraySegment<byte>(buffer);" );
			sb.AppendLine( "\t}" );
			sb.AppendLine();

			// partial 함수 선언부 생성
			sb.AppendLine( "\t // partial 함수 선언부" );
			foreach(Match match in matches)
			{
				string name = match.Groups[1].Value.Trim();
				string packetType = $"Protocol.{name}";

				// 서버는 C_로 시작하는 패킷을 받고, 클라이언트는 S_로 시작하는 패킷을 받음
				if(target == "Server" && name.StartsWith( "C_" ))
				{
					sb.AppendLine( $"\tpartial void On_{name}(PacketSession session, {packetType} packet);" );
				}
				else if(target == "Client" && name.StartsWith( "S_" ))
				{
					sb.AppendLine( $"\tpartial void On_{name}(PacketSession session, {packetType} packet);" );
				}
			}

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
				sb.AppendLine( $"\t{match.Groups[ 1 ].Value.Trim()} = {match.Groups[ 2 ].Value.Trim()}," );
			}

			sb.AppendLine("};");
		}
	}
}
