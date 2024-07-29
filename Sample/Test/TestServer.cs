//------------------------------------------------------------
// あなたたちを許すことはできません
// Copyright © 2024 怨靈. All rights reserved.
//------------------------------------------------------------

using System.Text;

namespace asphyxia
{
    public sealed class TestServer : AsphyxiaServer
    {
        protected override void OnConnected(uint id)
        {
            Console.ForegroundColor = ConsoleColor.Red;
            Console.WriteLine($"Connected: {id}");
            Console.ForegroundColor = ConsoleColor.White;
        }

        protected override void OnDisconnected(uint id)
        {
            Console.ForegroundColor = ConsoleColor.Cyan;
            Console.WriteLine($"Disconnected: {id}");
            Console.ForegroundColor = ConsoleColor.White;
        }

        protected override void OnData(uint id, Span<byte> data, PacketFlag flags)
        {
            Console.ForegroundColor = ConsoleColor.Cyan;
            Console.WriteLine($"Data: {id} {flags} {Encoding.UTF8.GetString(data)}");
            Console.ForegroundColor = ConsoleColor.White;
        }
    }
}