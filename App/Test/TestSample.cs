//------------------------------------------------------------
// あなたたちを許すことはできません
// Copyright © 2024 怨靈. All rights reserved.
//------------------------------------------------------------

using System.Text;

namespace asphyxia
{
    public static class TestSample
    {
        public static void Start()
        {
            var server = new TestServer();
            var client = new TestClient();
            server.Start(false, 100, 7777);
            Thread.Sleep(1000);
            client.Start(false, "127.0.0.1", 7777);
            var polling = true;
            Console.CancelKeyPress += (sender, args) =>
            {
                polling = false;
                Thread.Sleep(100);
                server.Shutdown();
                client.Shutdown();
                Thread.Sleep(1000);
            };
            ushort i = 0;
            ushort j = 0;
            ushort k = 0;
            Span<byte> buffer = stackalloc byte[2048];
            while (polling)
            {
                Thread.Sleep(100);
                server.Update();
                client.Update();
                if (client.Connected)
                {
                    var count = Encoding.UTF8.GetBytes($"{i++}", buffer);
                    var flag = j++ % 3 == 0 ? PacketFlag.Reliable : PacketFlag.Sequenced;
                    if (k++ % 2 == 0)
                        client.Send(buffer[..count], flag);
                    else
                        server.Send(0, buffer[..count], flag);
                }
            }
        }

        public static void StartManual()
        {
            var server = new TestServer();
            var client = new TestClient();
            server.Start(true, 100, 7777);
            Thread.Sleep(1000);
            client.Start(true, "127.0.0.1", 7777);
            var polling = true;
            Console.CancelKeyPress += (sender, args) =>
            {
                polling = false;
                Thread.Sleep(100);
                server.Shutdown();
                client.Shutdown();
                Thread.Sleep(1000);
            };
            ushort i = 0;
            ushort j = 0;
            ushort k = 0;
            Span<byte> buffer = stackalloc byte[2048];
            while (polling)
            {
                Thread.Sleep(100);
                server.Service();
                server.Flush();
                server.Update();
                client.Service();
                client.Flush();
                client.Update();
                if (client.Connected)
                {
                    var count = Encoding.UTF8.GetBytes($"{i++}", buffer);
                    var flag = j++ % 3 == 0 ? PacketFlag.Reliable : PacketFlag.Sequenced;
                    if (k++ % 2 == 0)
                        client.Send(buffer[..count], flag);
                    else
                        server.Send(0, buffer[..count], flag);
                }
            }
        }
    }
}