using System;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using Microsoft.Extensions.Configuration;
using System.Collections.Generic;

namespace DummyClient
{
    public class ConnectionSettings
    {
        public string Host { get; set; }
        public int Port { get; set; }
    }

    internal class Program
    {
        static List<byte> _clientRecvBuffer = new List<byte>();

        static void Main(string[] args)
        {
            var environmentName = Environment.GetEnvironmentVariable("DOTNET_ENVIRONMENT") ?? "Production";
            var builder = new ConfigurationBuilder()
                .SetBasePath(AppDomain.CurrentDomain.BaseDirectory)
                .AddJsonFile("appsettings.json", optional: false, reloadOnChange: true)
                .AddJsonFile($"appsettings.{environmentName}.json", optional: true, reloadOnChange: true)
                .AddEnvironmentVariables();

            IConfiguration configuration = builder.Build();
            ConnectionSettings settings = configuration.GetSection("ConnectionSettings").Get<ConnectionSettings>();

            if (settings == null || !IPAddress.TryParse(settings.Host, out var ipAddr))
            {
                Console.WriteLine("appsettings.json에서 유효한 ConnectionSettings를 찾을 수 없습니다.");
                return;
            }

            while (true)
            {
                Console.WriteLine("\n------------------------------------");
                Console.WriteLine("테스트를 선택하세요:");
                Console.WriteLine("1. 작은 패킷 5개 동시 전송 (1회)");
                Console.WriteLine("2. 큰 패킷 1개 전송 (1회)");
                Console.WriteLine("3. [반복] 전체 테스트 랜덤 실행 (중지: 아무 키)");
                Console.WriteLine("Q. 종료");
                Console.Write("> ");

                string input = Console.ReadLine();
                if (string.Equals(input, "q", StringComparison.OrdinalIgnoreCase))
                {
                    break;
                }

                switch (input)
                {
                    case "1":
                        RunTest(settings, TestScenario.SmallPackets);
                        break;

                    case "2":
                        RunTest(settings, TestScenario.LargePacket);
                        break;

                    case "3":
                        Random rand = new Random();
                        Console.WriteLine("\n'전체 테스트'를 랜덤하게 반복합니다. 중지하려면 아무 키나 누르세요...");
                        while (!Console.KeyAvailable)
                        {
                            // 0 또는 1을 랜덤하게 생성하여 테스트 선택
                            if (rand.Next(0, 2) == 0)
                            {
                                RunTest(settings, TestScenario.SmallPackets);
                            }
                            else
                            {
                                RunTest(settings, TestScenario.LargePacket);
                            }
                            Thread.Sleep(500); // 0.5초 간격
                        }
                        // 입력 버퍼 비우기
                        while (Console.KeyAvailable) Console.ReadKey(true);
                        Console.WriteLine("반복 테스트를 중지했습니다.");
                        break;

                    default:
                        Console.WriteLine("잘못된 입력입니다.");
                        break;
                }
            }
            Console.WriteLine("클라이언트를 종료합니다.");
        }

        enum TestScenario
        {
            SmallPackets,
            LargePacket
        }

        static void RunTest(ConnectionSettings settings, TestScenario scenario)
        {
            IPEndPoint endPoint = new IPEndPoint(IPAddress.Parse(settings.Host), settings.Port);
            Socket socket = new Socket(endPoint.AddressFamily, SocketType.Stream, ProtocolType.Tcp);
            _clientRecvBuffer.Clear();

            try
            {
                socket.Connect(endPoint);
                Console.WriteLine($"\n[연결] 서버에 연결되었습니다: {socket.RemoteEndPoint}");
                
                Thread.Sleep(100);
                ReceiveAndProcess(socket);

                switch (scenario)
                {
                    case TestScenario.SmallPackets:
                        Console.WriteLine("[테스트 1] 5개의 작은 패킷을 한 번에 전송합니다...");
                        byte[] combinedBuffer = MakeCombinedPackets();
                        socket.Send(combinedBuffer);
                        Console.WriteLine($" -> {combinedBuffer.Length} 바이트 전송 완료.");
                        break;

                    case TestScenario.LargePacket:
                        Console.WriteLine("[테스트 2] 1개의 큰 패킷(33바이트)을 전송합니다...");
                        byte[] largePacket = MakePacket(100, "This is a very big packet data!");
                        socket.Send(largePacket);
                        Console.WriteLine($" -> {largePacket.Length} 바이트 전송 완료.");
                        break;
                }
                
                Thread.Sleep(100);
                ReceiveAndProcess(socket);
            }
            catch (Exception e)
            {
                Console.WriteLine($"[오류] {e.Message}");
            }
            finally
            {
                socket.Close();
                Console.WriteLine("[종료] 연결을 종료합니다.");
            }
        }

        static byte[] MakeCombinedPackets()
        {
            byte[] packet1 = MakePacket(1, "Test1");
            byte[] packet2 = MakePacket(2, "Test2");
            byte[] packet3 = MakePacket(3, "Test3");
            byte[] packet4 = MakePacket(4, "Test4");
            byte[] packet5 = MakePacket(5, "Test5");

            byte[] combined = new byte[packet1.Length + packet2.Length + packet3.Length + packet4.Length + packet5.Length];
            int offset = 0;
            Buffer.BlockCopy(packet1, 0, combined, offset, packet1.Length);
            offset += packet1.Length;
            Buffer.BlockCopy(packet2, 0, combined, offset, packet2.Length);
            offset += packet2.Length;
            Buffer.BlockCopy(packet3, 0, combined, offset, packet3.Length);
            offset += packet3.Length;
            Buffer.BlockCopy(packet4, 0, combined, offset, packet4.Length);
            offset += packet4.Length;
            Buffer.BlockCopy(packet5, 0, combined, offset, packet5.Length);
            
            return combined;
        }

        static byte[] MakePacket(ushort packetId, string msg)
        {
            byte[] msgBytes = Encoding.UTF8.GetBytes(msg);
            ushort packetSize = (ushort)(msgBytes.Length + 4);

            byte[] packet = new byte[packetSize];
            Buffer.BlockCopy(BitConverter.GetBytes(packetSize), 0, packet, 0, sizeof(ushort));
            Buffer.BlockCopy(BitConverter.GetBytes(packetId), 0, packet, 2, sizeof(ushort));
            Buffer.BlockCopy(msgBytes, 0, packet, 4, msgBytes.Length);

            return packet;
        }

        static void ReceiveAndProcess(Socket socket)
        {
            byte[] recvBuffer = new byte[1024];
            int bytesRecv = socket.Receive(recvBuffer);

            if (bytesRecv > 0)
            {
                Console.WriteLine($" <- {bytesRecv} 바이트 수신.");
                _clientRecvBuffer.AddRange(new ArraySegment<byte>(recvBuffer, 0, bytesRecv));

                while (true)
                {
                    if (_clientRecvBuffer.Count < 2)
                        break;

                    byte[] header = _clientRecvBuffer.GetRange(0, 2).ToArray();
                    ushort packetSize = BitConverter.ToUInt16(header, 0);

                    if (_clientRecvBuffer.Count < packetSize)
                        break;

                    byte[] packetBytes = _clientRecvBuffer.GetRange(0, packetSize).ToArray();
                    _clientRecvBuffer.RemoveRange(0, packetSize);

                    ushort id = BitConverter.ToUInt16(packetBytes, 2);
                    string msg = Encoding.UTF8.GetString(packetBytes, 4, packetSize - 4);
                    Console.WriteLine($"   [수신] ID: {id}, Size: {packetSize}, Msg: \"{msg}\"");
                }
            }
        }
    }
}