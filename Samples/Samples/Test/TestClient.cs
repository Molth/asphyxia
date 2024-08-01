//------------------------------------------------------------
// あなたたちを許すことはできません
// Copyright © 2024 怨靈. All rights reserved.
//------------------------------------------------------------

using System.Text;

namespace asphyxia
{
    public sealed class TestClient : AsphyxiaClient
    {
        protected override void OnConnected()
        {
            Console.ForegroundColor = ConsoleColor.Yellow;
            Console.WriteLine("Connected");
            Console.ForegroundColor = ConsoleColor.White;
        }

        protected override void OnDisconnected()
        {
            Console.ForegroundColor = ConsoleColor.Green;
            Console.WriteLine("Disconnected");
            Console.ForegroundColor = ConsoleColor.White;
        }

        protected override void OnData(Span<byte> data, PacketFlag flags)
        {
            Console.ForegroundColor = ConsoleColor.Green;
            Console.WriteLine($"Data: {flags} {Encoding.UTF8.GetString(data)}");
            Console.ForegroundColor = ConsoleColor.White;
        }
    }
}