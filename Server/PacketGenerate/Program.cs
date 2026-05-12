using System;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using System.Collections.Generic;
using System.Linq;

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
				if(0 < Directory.GetFiles( currentDir, "*.sln" ).Length)
				{
					currentDir = Directory.GetParent( currentDir ).FullName;
					return currentDir;
				}
				currentDir = Directory.GetParent( currentDir )?.FullName;
			}

			return null;
		}

		static void ParseProto( string protoText, string outputDir )
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

			List<Tuple<string, string, string>> packetCategoryList = new List<Tuple<string, string, string>>();
			Regex categoryRegex = new Regex(@"message\s+(\w+)\s*\{[^}]*?option\s+\(category\)\s*=\s*(\w+);", RegexOptions.Singleline);
			MatchCollection categoryMatches = categoryRegex.Matches(protoText);

			foreach(Match categoryMatch in categoryMatches)
			{
				string messageName = categoryMatch.Groups[1].Value.Trim();
				string categoryName = categoryMatch.Groups[2].Value.Trim();
				string packetType = "";

				if(messageName.StartsWith( "C_" ))
					packetType = "C_";
				else if(messageName.StartsWith( "S_" ))
					packetType = "S_";

				packetCategoryList.Add( Tuple.Create( messageName, categoryName, packetType ) );
			}

			// 서버용 PacketID enum 생성
			StringBuilder serverPacketEnum = new StringBuilder();
			GeneratePacketEnum( matches, serverPacketEnum, "Server" );
			File.WriteAllText( "Gen_Server_PacketID.cs", serverPacketEnum.ToString() );

			// 클라이언트(unity)용 PacketID enum 생성
			StringBuilder clientPacketEnum = new StringBuilder();
			GeneratePacketEnum( matches, clientPacketEnum, "Client" );
			File.WriteAllText( "Gen_Client_PacketID.cs", clientPacketEnum.ToString() );

			// 서버용 PacketManager 생성 ( 새로운 구조 )
			StringBuilder ServerManager = new StringBuilder();
			GeneratePacketManager( matches, ServerManager, "Server", packetCategoryList );
			File.WriteAllText( "Gen_Server_PacketManager.cs", ServerManager.ToString() );

			// 클라이언트(unity)용 Packetmanager 생성
			StringBuilder ClientManager = new StringBuilder();
			GeneratePacketManager( matches, ClientManager, "Client", packetCategoryList );
			File.WriteAllText( "Gen_Client_PacketManager.cs", ClientManager.ToString() );

			// 클라이언트(unreal)용 c++ 헤더 생성
			StringBuilder unrealHeader = new StringBuilder();
			GenerateUnrealHeader( matches, unrealHeader );
			File.WriteAllText( "Gen_PacketID.h", unrealHeader.ToString() );

			// IPacketHandler 인터페이스 생성
			GenerateIPacketHandler();

			// Category별 핸들러 생성
			var packetHandlerList = packetCategoryList.Where( p => p.Item3 == "C_" )
				.Select(p => p.Item2)
				.Distinct()
				.ToList();

			Console.WriteLine( $"\n{packetHandlerList.Count}개 카테고리의 핸들러 생성 중..." );
			foreach ( var category in packetHandlerList )
			{
				string handlerProperty = ToCamelCase(category) + "PacketHandler";  // "RoomPacketHandler"
				GeneratePacketHandlers( packetCategoryList, category, handlerProperty );
			}

			Console.WriteLine( "모든 핸들러 생성 완료\n" );

			Console.WriteLine( $"\n=== 생성 완료 요약 ===" );
			Console.WriteLine( $"- IPacketHandler.cs" );
			Console.WriteLine( $"- Gen_Server_PacketManager.cs" );
			Console.WriteLine( $"- Gen_Client_PacketManager.cs" );
			foreach(var category in packetHandlerList)
			{
				string handlerProperty = ToCamelCase(category) + "PacketHandler";
				Console.WriteLine( $"- {handlerProperty}.Generated.cs" );
			}
			Console.WriteLine( $"=====================\n" );
		}

		static void GeneratePacketEnum( MatchCollection matches, StringBuilder sb, string target )
		{
			sb.AppendLine( "// [자동 생성] Protocol.proto 파일을 기반으로 자동 생성된 코드입니다." );
			sb.AppendLine( $"// Target: {target}" );
			sb.AppendLine();
			sb.AppendLine( "public enum PacketID" );
			sb.AppendLine( "{" );
			foreach(Match match in matches)
			{
				string pName = MakeEnumNameToCamelName(match.Groups[1].Value.Trim());

				sb.AppendLine( $"\t{pName} = {match.Groups[ 2 ].Value.Trim()}," );
			}
			sb.AppendLine( "}" );
		}

		static void GeneratePacketManager( MatchCollection matches, StringBuilder sb, string target,
			List<Tuple<string, string, string>> packetCategoryList )
		{
			if(target == "Server")
			{
				GenerateNewServerPacketManager( matches, sb, packetCategoryList );
			}
			else // client
			{
				GenerateNewClientPacketManager( matches, sb, packetCategoryList );
			}
		}

		private static void GenerateNewServerPacketManager( MatchCollection matches, StringBuilder sb,
			List<Tuple<string, string, string>> packetCategoryList )
		{
			// 1. 헤더 및 using 문
			sb.Append( """
				// [자동 생성] Category 핸들러 시스템용 PacketManager
				// Target: Server

				using Google.Protobuf;
				using Protocol;
				using Microsoft.Extensions.Logging;
				using Server.Core.Session;
				using System;
				using System.Collections.Generic;
				using System.Threading.Tasks;
				using Server.Packet.Handlers;
				using ServerCore;
				using Server.Core.Jobs;
				using Server.Room;

				namespace Server.Packet
				{
					public class PacketManager
					{
						private readonly ILogger<PacketManager> _logger;
						private readonly Dictionary<Type, PacketID> _packetTypeToId;
						private readonly Dictionary<PacketID, PacketCategory> _packetCategoryCache = new();
						private readonly SystemPacketHandler _systemPacketHandler;
						private readonly IJobQueueManager _jobQueueManager;
						private readonly Dictionary<PacketID, SessionState[]> _packetAllowedStates = new();

				""" );

			// 2. 생성자
			sb.Append( """
						public PacketManager(ILogger<PacketManager> logger, IJobQueueManager jobQueueManager, SystemPacketHandler systemHandler)
						{
							_logger = logger;
							_packetTypeToId = new Dictionary<Type, PacketID>();
							_systemPacketHandler = systemHandler;
							_jobQueueManager = jobQueueManager;
							Register();
							RegisterStateFilter();
						}
				""" );
			sb.AppendLine();

			// 3. Register 메서드 시작
			sb.AppendLine( "        private void Register()" );
			sb.AppendLine( "        {" );

			// S_ 패킷 타입 등록
			foreach(Match match in matches)
			{
				string pName = MakeEnumNameToCamelName(match.Groups[1].Value.Trim());
				if(pName.StartsWith( "S_" ))
				{
					sb.AppendLine( $"            _packetTypeToId.Add(typeof({pName}), PacketID.{pName});" );
				}
			}

			var categoryList = packetCategoryList
				.Where(p => p.Item3 == "C_")
				.GroupBy(p => p.Item2)
				.ToList();

			foreach(var categoryGroup in categoryList)
			{
				string categoryName = ToCamelCase(categoryGroup.Key);
				sb.AppendLine( $"            // {categoryName} 카테고리" );

				foreach(var categoryData in categoryGroup)
				{
					sb.AppendLine( $"            _packetCategoryCache.Add(PacketID.{categoryData.Item1}, PacketCategory.{categoryName});" );
				}
			}

			sb.AppendLine( "        }" );
			sb.AppendLine();

			// 6. HandlePacket 메서드
			sb.Append( """
						public async ValueTask HandlePacket(IClientSession session, ArraySegment<byte> buffer)
						{
							ushort count = 0;
							ushort size = BitConverter.ToUInt16(buffer.Array, buffer.Offset);
							count += 2;
							ushort id = BitConverter.ToUInt16(buffer.Array, buffer.Offset + count);
							count += 2;

							PacketID packetId = (PacketID)id;
							// [SM-1] 패킷 허용 상태 검사
							if(_packetAllowedStates.TryGetValue(packetId, out var allowedStates))
							{
								SessionState currentState = session.State;
								bool allowed = false;
								foreach(var s in allowedStates)
								{
									if(s == currentState) 
									{
										allowed = true;
										break;
									}
								}

								if(!allowed)
								{
									_logger.LogDebug("Packet {PacketId} dropped: session {SessionId} in state {State}",
												packetId, session.SessionId, currentState);
									return;
								}
							}


							PacketCategory packetCategory = GetPacketCategory(packetId);
							_logger.LogDebug("Packet received: ID={PacketId}, Category={Category}", id, packetCategory);

							if( packetCategory == PacketCategory.NoneCategory )
							{
								_logger.LogWarning("PacketId:{PacketId} not found Category", id);
								return;
							}

							IPacketHandler packetHandler = null;
							ArraySegment<byte> packetBuffer = new ArraySegment<byte>(buffer.Array, buffer.Offset + count, size - count);
							if ( packetCategory == PacketCategory.System )
							{
								packetHandler = _systemPacketHandler;
								await packetHandler.HandleAsync( session, id, packetBuffer );
							}
							else
							{
								// CurrentRoom 확인
								if(session.CurrentRoom == null)
								{
									_logger.LogWarning( "Player {PlayerId} not in any room for packet {PacketId}", session.PlayerId, id.ToString() );
									return;
								}

								var room = session.CurrentRoom;

								packetHandler = packetCategory switch
								{
									PacketCategory.Inventory => room?.InventoryPacketHandler,
									PacketCategory.Room => room?.RoomPacketHandler,
									PacketCategory.Combat => room?.CombatPacketHandler,
									_ => null
								};

								var packetJob = _jobQueueManager.JobPool.Get<PacketJob>();
								packetJob.Initialize( packetHandler, session, id, packetBuffer );

								BaseRoom baseRoom = room as BaseRoom;
								if(baseRoom == null)
								{
									_logger.LogWarning( "Player {PlayerId} current room is not BaseRoom for packet {PacketId}",
									session.PlayerId, id.ToString() );
									return;
								}

								baseRoom.Push( packetJob );
							}
						}

				""" );

			// 7. MakeSendPacket 메서드
			sb.Append( """
						public ArraySegment<byte> MakeSendPacket(IMessage packet)
						{
							if (!_packetTypeToId.TryGetValue(packet.GetType(), out var packetId))
							{
								_logger.LogWarning("Unknown packet type for MakeSendPacket: {PacketType}", packet.GetType().Name);
								return new ArraySegment<byte>();
							}

							ushort size = (ushort)packet.CalculateSize();
							byte[] buffer = new byte[size + 4];
							Array.Copy(BitConverter.GetBytes((ushort)(size + 4)), 0, buffer, 0, sizeof(ushort));
							Array.Copy(BitConverter.GetBytes((ushort)packetId), 0, buffer, 2, sizeof(ushort));
							packet.WriteTo(new System.IO.MemoryStream(buffer, 4, size));
							return new ArraySegment<byte>(buffer);
						}


				""" );

			sb.Append( """
						private PacketCategory GetPacketCategory(PacketID id)
						{
							return _packetCategoryCache.TryGetValue(id, out var category) ? category : PacketCategory.NoneCategory;
						}

				""" );

			sb.Append( """

						private void RegisterStateFilter()
						{

				""" );

			foreach(var (messageName, categoryName, packetType) in packetCategoryList)
			{
				if(packetType != "C_")
					continue;

				string allowed = categoryName switch
				{
					"SYSTEM" when messageName == "C_EnterGame" => "SessionState.Connected",
					"SYSTEM" when messageName == "C_ChangeRoom" => "SessionState.InRoom",
					"SYSTEM" when messageName == "C_Ping" => null, // 생략
					"SYSTEM" => null, // 신규 SYSTEM은 주석 + TODO
					"ROOM" or "COMBAT" or "INVENTORY" => "SessionState.InRoom",
					_ => null
				};

				if(allowed != null)
					sb.AppendLine( $"			_packetAllowedStates[PacketID.{messageName}] = new[] {{{allowed} }};" );
				else if(categoryName == "SYSTEM" && messageName != "C_Ping")
					sb.AppendLine( $"			// TODO: {messageName} 패킷의 허용 상태 설정 필요" );
			}

			sb.Append( """
						}
					}
				}

				""" );
		}

		private static void GenerateNewClientPacketManager( MatchCollection matches, StringBuilder sb,
			List<Tuple<string, string, string>> packetCategoryList )
		{
			sb.AppendLine( "// [자동 생성] Protocol.proto 파일을 기반으로 자동 생성된 코드입니다." );
			sb.AppendLine( "// Target: Client (신규 구조 - Inheritance Model)" );
			sb.AppendLine();
			sb.AppendLine( "using ServerCore;" );
			sb.AppendLine( "using System;" );
			sb.AppendLine( "using System.Collections.Generic;" );
			sb.AppendLine( "using System.Threading.Tasks;" );
			sb.AppendLine( "using Google.Protobuf;" );
			sb.AppendLine( "using Protocol;" );
			sb.AppendLine( "using Microsoft.Extensions.Logging;" );
			sb.AppendLine();
			sb.AppendLine( "namespace DummyClient.Packet" );
			sb.AppendLine( "{" );
			sb.AppendLine( "    public abstract class BaseClientPacketHandler" );
			sb.AppendLine( "    {" );

			// Generate handler method stubs
			foreach(Match match in matches)
			{
				string pName = MakeEnumNameToCamelName(match.Groups[1].Value.Trim());
				if(pName.StartsWith( "S_" ))
				{
					sb.AppendLine( $"        public virtual ValueTask On_{pName}(NetworkSession session, {pName} packet) {{ Console.WriteLine(\"Received but not handled: {pName}\"); return ValueTask.CompletedTask; }}" );
				}
			}

			sb.AppendLine( "    }" );
			sb.AppendLine();

			sb.AppendLine( "    public class PacketManager" );
			sb.AppendLine( "    {" );
			sb.AppendLine( "        private readonly ILogger<PacketManager> _logger;" );
			sb.AppendLine( "        private readonly BaseClientPacketHandler _handler;" );
			sb.AppendLine( "        private readonly Dictionary<ushort, Func<NetworkSession, ArraySegment<byte>, ValueTask>> _onRecv;" );
			sb.AppendLine( "        private readonly Dictionary<Type, PacketID> _packetTypeToId;" );
			sb.AppendLine();
			sb.AppendLine( "        public PacketManager(ILogger<PacketManager> logger, BaseClientPacketHandler handler)" );
			sb.AppendLine( "        {" );
			sb.AppendLine( "            _logger = logger ?? throw new ArgumentNullException(nameof(logger));" );
			sb.AppendLine( "            _handler = handler ?? throw new ArgumentNullException(nameof(handler));" );
			sb.AppendLine( "            _onRecv = new Dictionary<ushort, Func<NetworkSession, ArraySegment<byte>, ValueTask>>();" );
			sb.AppendLine( "            _packetTypeToId = new Dictionary<Type, PacketID>();" );
			sb.AppendLine( "            Register();" );
			sb.AppendLine( "        }" );
			sb.AppendLine();
			sb.AppendLine( "        private void Register()" );
			sb.AppendLine( "        {" );

			foreach(Match match in matches)
			{
				string pName = MakeEnumNameToCamelName(match.Groups[1].Value.Trim());
				if(pName.StartsWith( "S_" ))
				{
					sb.AppendLine( $"            _onRecv.Add((ushort)PacketID.{pName}, HandlePacket<{pName}>(_handler.On_{pName}));" );
				}
				else if(pName.StartsWith( "C_" ))
				{
					sb.AppendLine( $"            _packetTypeToId.Add(typeof(Protocol.{pName}), PacketID.{pName});" );
				}
			}

			sb.AppendLine( "        }" );
			sb.AppendLine();
			sb.AppendLine( "        public async ValueTask HandlePacket(NetworkSession session, ArraySegment<byte> buffer)" );
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
			sb.AppendLine( "                _logger.LogWarning(\"Unknown packet ID: {PacketId}\", id);" );
			sb.AppendLine( "            }" );
			sb.AppendLine( "        }" );
			sb.AppendLine();
			sb.AppendLine( "        private Func<NetworkSession, ArraySegment<byte>, ValueTask> HandlePacket<T>(Func<NetworkSession, T, ValueTask> handler) where T : IMessage, new()" );
			sb.AppendLine( "        {" );
			sb.AppendLine( "            return async (session, buffer) =>" );
			sb.AppendLine( "            {" );
			sb.AppendLine( "                try" );
			sb.AppendLine( "                {" );
			sb.AppendLine( "                    var packet = new T();" );
			sb.AppendLine( "                    packet.MergeFrom(buffer.Array, buffer.Offset, buffer.Count);" );
			sb.AppendLine( "                    await handler(session, packet);" );
			sb.AppendLine( "                }" );
			sb.AppendLine( "                catch (Exception ex)" );
			sb.AppendLine( "                {" );
			sb.AppendLine( "                    _logger.LogError(ex, \"Error handling packet {PacketType}\", typeof(T).Name);" );
			sb.AppendLine( "                }" );
			sb.AppendLine( "            };" );
			sb.AppendLine( "        }" );
			sb.AppendLine();
			sb.AppendLine( "        public ArraySegment<byte> MakeSendPacket(IMessage packet)" );
			sb.AppendLine( "        {" );
			sb.AppendLine( "            if (!_packetTypeToId.TryGetValue(packet.GetType(), out var packetId))" );
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

		static void GenerateUnrealHeader( MatchCollection matches, StringBuilder sb )
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

			foreach(Match match in matches)
			{
				string name = match.Groups[1].Value.Trim();
				string pName = MakeEnumNameToCamelName(name);

				sb.AppendLine( $"\t{pName} = {match.Groups[ 2 ].Value.Trim()}," );
			}

			sb.AppendLine( "};" );
		}

		static void GenerateIPacketHandler()
		{
			var sb = new StringBuilder();

			sb.Append( """
				// [자동 생성] IPacketHandler 인터페이스
				using System;
				using System.Threading.Tasks;
				using Server.Core.Session;

				namespace Server.Packet.Handlers
				{
					public interface IPacketHandler
					{
						ValueTask HandleAsync(IClientSession session, ushort id, ArraySegment<byte> buffer);
					}
				}
				""" );

			File.WriteAllText( "IPacketHandler.cs", sb.ToString() );
			Console.WriteLine( "IPacketHandler.cs 생성 완료" );
		}

		static void GeneratePacketHandlers( List<Tuple<string, string, string>> packetCategoryList, string category, string handlerName )
		{
			var sb = new StringBuilder();

			// 카테고리별 그룹화
			var categoryPackets = packetCategoryList
				.Where( p => p.Item2 == category && p.Item3 == "C_")
				.Select(p => p.Item1)
				.ToList();

			if(categoryPackets.Count == 0)
			{
				Console.WriteLine( $"{handlerName}: {category} 카테고리에 클라이언트 패킷 없음" );
				return;
			}

			// Dictionary 초기화 코드 생성
			sb.AppendLine( $"// [자동 생성] {handlerName} Dictionary 초기화" );
			sb.AppendLine( "using Protocol;" );
			sb.AppendLine( "using Google.Protobuf;" );
			sb.AppendLine( "using System;" );
			sb.AppendLine( "using System.Threading.Tasks;" );
			sb.AppendLine( "using System.Collections.Generic;" );
			sb.AppendLine( "using Server.Core.Session;" );
			sb.AppendLine( "using Microsoft.Extensions.Logging;" );
			sb.AppendLine( "//test" );
			sb.AppendLine();
			sb.AppendLine( "namespace Server.Packet.Handlers" );
			sb.AppendLine("{");
			sb.AppendLine( $"\tpublic partial class {handlerName} : IPacketHandler" );
			sb.AppendLine( "\t{" );
			sb.AppendLine( "\t\t/// <summary>" );
			sb.AppendLine( "\t\t/// 테스트 및 디버깅용 Dictionary." );
			sb.AppendLine( "\t\t/// 역직렬화된 IMessage 객체를 직접 처리할 때 사용." );
			sb.AppendLine( "\t\t/// 런타임 패킷 처리는 _onRecv Dictionary 사용." );
			sb.AppendLine( "\t\t/// </summary>" );
			sb.AppendLine( "\t\tpublic Dictionary<Type, Func<IClientSession, IMessage, Task>> Handlers {  get; private set; }" );
			sb.AppendLine( "\t\tprivate Dictionary<ushort, Func<IClientSession, ArraySegment<byte>, ValueTask>> _onRecv;" );
			sb.AppendLine();
			sb.AppendLine( "\t\tprivate void InitializeHandlers()" );
			sb.AppendLine( "\t\t{" );
			sb.AppendLine( "\t\t\tHandlers = new Dictionary<Type, Func<IClientSession, IMessage, Task>>();" );
			sb.AppendLine( "\t\t\t_onRecv = new Dictionary<ushort, Func<IClientSession, ArraySegment<byte>, ValueTask>>();" );
			sb.AppendLine();

			foreach(var packetName in categoryPackets)
			{
				sb.AppendLine( $"\t\t\tHandlers.Add(typeof({packetName}), async (s, p) => await Handle{packetName}Async( s, ({packetName})p));" );
				sb.AppendLine( $"\t\t\t_onRecv.Add((ushort)PacketID.{packetName}, Handle{packetName}Async);" );
			}
			
			sb.AppendLine( "\t\t}" );
			sb.AppendLine();
			sb.Append( $$"""
						public async ValueTask HandleAsync(IClientSession session, ushort id, ArraySegment<byte> buffer)
						{
							if(_onRecv.TryGetValue(id, out var handler))
							{
								await handler(session, buffer);
							}
							else
							{
								_logger.LogWarning( "{{handlerName}} _onRecv Dictionary Not Found id {id.ToString()}"  );
							}
						}
				""" );

			// 카테고리 추가 시 수정 필요 여부 확인.
			// TODO: 카테고리 누락이 발생하기 쉽다. 수정 필요.
			bool shouldInjectGuard = category is "ROOM" or "COMBAT" or "INVENTORY";
			// 개별 핸들러 메서드들 (C_ 패킷)
			foreach(var packetName in categoryPackets)
			{
				sb.Append( $$"""


							private async ValueTask Handle{{packetName}}Async(IClientSession session, ArraySegment<byte> buffer)
							{

					""" );

				if(shouldInjectGuard)
				{
					sb.Append( """
									if(session.State != SessionState.InRoom)
									{
										_logger.LogDebug("Packet dropped in handler: SessionId={SessionId}, State={State}", session.SessionId, session.State);
										return;
									}

						""" );
				}

				sb.Append( $$"""
								var packet = new {{packetName}}();
								packet.MergeFrom(buffer.Array, buffer.Offset, buffer.Count);
								await Handle{{packetName}}Async(session, packet);
							}
					""" );
			}

			sb.AppendLine();
			sb.AppendLine( "\t}" );
			sb.AppendLine( "}" );

			// 파일 저장
			string fileName = $"{handlerName}.Generated.cs";
			File.WriteAllText( fileName, sb.ToString() );
			Console.WriteLine( $"{handlerName}.Generated.cs 생성 완료 ({categoryPackets.Count}개 패킷 )" );
		}

		static private string MakeEnumNameToCamelName( string name )
		{
			if(string.IsNullOrEmpty( name ))
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

		static string ToCamelCase( string input )
		{
			if(string.IsNullOrEmpty( input ))
				return input;

			// "SYSTEM" → "System", "ROOM" → "Room"
			return char.ToUpper( input[ 0 ] ) + input.Substring( 1 ).ToLower();
		}
	}
}