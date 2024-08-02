//------------------------------------------------------------
// あなたたちを許すことはできません
// Copyright © 2024 怨靈. All rights reserved.
//------------------------------------------------------------

namespace asphyxia
{
    public sealed class Program
    {
        private static void Main() => StartLiteNetLibNatTravelService();

        private static void StartNatTravelService()
        {
            var service = new NatTravelService();
            service.Create(4096, 7778);
            Console.CancelKeyPress += (sender, args) =>
            {
                service.Dispose();
                Thread.Sleep(1000);
            };
            while (true)
            {
                service.Service();
                Thread.Sleep(1);
            }
        }

        private static void StartLiteNetLibNatTravelService()
        {
            var service = new LiteNetLib.NatTravelService();
            service.Create(4096, 7778);
            Console.CancelKeyPress += (sender, args) =>
            {
                service.Dispose();
                Thread.Sleep(1000);
            };
            while (true)
            {
                service.Service();
                Thread.Sleep(1);
            }
        }
    }
}