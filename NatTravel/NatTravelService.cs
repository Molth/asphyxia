﻿//------------------------------------------------------------
// あなたたちを許すことはできません
// Copyright © 2024 怨靈. All rights reserved.
//------------------------------------------------------------

using System.Collections.Concurrent;
using System.Net;
using static asphyxia.Settings;
using static System.Runtime.CompilerServices.Unsafe;

namespace asphyxia
{
    /// <summary>
    ///     Nat travel service
    /// </summary>
    public sealed unsafe class NatTravelService
    {
        /// <summary>
        ///     Service thread count
        /// </summary>
        public const int SERVICE_THREAD_COUNT = 2;

        /// <summary>
        ///     Host
        /// </summary>
        private readonly Host _host = new();

        /// <summary>
        ///     Peers
        /// </summary>
        private readonly ConcurrentDictionary<IPEndPoint, Peer> _peers = new(Math.Min(SERVICE_THREAD_COUNT, Environment.ProcessorCount), MAX_PEERS);

        /// <summary>
        ///     Outgoing commands
        /// </summary>
        private readonly ConcurrentQueue<NetworkOutgoing> _outgoings = new();

        /// <summary>
        ///     NetworkEvents
        /// </summary>
        private readonly ConcurrentQueue<NetworkEvent> _networkEvents = new();

        /// <summary>
        ///     State
        /// </summary>
        private int _state;

        /// <summary>
        ///     State lock
        /// </summary>
        private int _stateLock;

        /// <summary>
        ///     Create
        /// </summary>
        /// <param name="maxPeers">Max peers</param>
        /// <param name="port">Port</param>
        public void Create(int maxPeers, ushort port)
        {
            if (Interlocked.CompareExchange(ref _state, 1, 0) != 0)
                throw new InvalidOperationException("Service has created.");
            try
            {
                _host.Create(maxPeers, port);
            }
            catch
            {
                Interlocked.Exchange(ref _state, 0);
                throw;
            }

            Console.WriteLine($"Service startup: Port[{port}] MaxPeers[{maxPeers}]] ");
            Interlocked.Exchange(ref _stateLock, 0);
            new Thread(Polling) { IsBackground = true }.Start();
        }

        /// <summary>
        ///     Dispose
        /// </summary>
        public void Dispose() => Interlocked.Exchange(ref _state, 0);

        /// <summary>
        ///     Polling
        /// </summary>
        private void Polling()
        {
            while (_state == 1)
            {
                while (_outgoings.TryDequeue(out var outgoing))
                    outgoing.Send();
                _host.Flush();
                _host.Service();
                _host.Flush();
                while (_host.CheckEvents(out var networkEvent))
                    _networkEvents.Enqueue(networkEvent);
                Thread.Sleep(1);
            }

            foreach (var peer in _peers.Values)
                peer.DisconnectNow();
            _peers.Clear();
            _host.Flush();
            _host.Dispose();
            while (_networkEvents.TryDequeue(out var networkEvent))
            {
                if (networkEvent.EventType != NetworkEventType.Data)
                    continue;
                networkEvent.Packet.Dispose();
            }

            while (_outgoings.TryDequeue(out var outgoing))
                outgoing.Dispose();
        }

        /// <summary>
        ///     Service
        /// </summary>
        public void Service()
        {
            if (Interlocked.CompareExchange(ref _stateLock, 1, 0) != 0)
                return;
            try
            {
                while (_networkEvents.TryDequeue(out var networkEvent))
                {
                    switch (networkEvent.EventType)
                    {
                        case NetworkEventType.Connect:
                            Console.WriteLine($"Connected: [{networkEvent.Peer.Id}] [{networkEvent.Peer.IPEndPoint}]");
                            _peers[networkEvent.Peer.IPEndPoint] = networkEvent.Peer;
                            var dataPacket = networkEvent.Peer.IPEndPoint.CreateDataPacket(1);
                            ((byte*)dataPacket.Data)[0] = 0;
                            _outgoings.Enqueue(new NetworkOutgoing(networkEvent.Peer, dataPacket));
                            continue;
                        case NetworkEventType.Data:
                            var packet = networkEvent.Packet;
                            var span = packet.AsSpan();
                            IPAddress address;
                            try
                            {
                                address = new IPAddress(span[..^4]);
                            }
                            catch
                            {
                                packet.Dispose();
                                break;
                            }

                            var port = ReadUnaligned<int>(ref span[^4]);
                            var ipEndPoint = new IPEndPoint(address, port);
                            if (!_peers.TryGetValue(ipEndPoint, out var peer) || networkEvent.Peer == peer)
                            {
                                packet.Dispose();
                                continue;
                            }

                            Console.WriteLine($"Data: [{networkEvent.Peer.Id}] [{networkEvent.Peer.IPEndPoint}] to [{peer.Id}] [{peer.IPEndPoint}]");
                            packet.Dispose();
                            packet = networkEvent.Peer.IPEndPoint.CreateDataPacket(1);
                            ((byte*)packet.Data)[0] = 1;
                            var outgoing = new NetworkOutgoing(peer, packet);
                            _outgoings.Enqueue(outgoing);
                            continue;
                        case NetworkEventType.Timeout:
                        case NetworkEventType.Disconnect:
                            Console.WriteLine($"Disconnected: [{networkEvent.Peer.Id}] [{networkEvent.Peer.IPEndPoint}]");
                            _peers.TryRemove(networkEvent.Peer.IPEndPoint, out _);
                            continue;
                        case NetworkEventType.None:
                            break;
                    }
                }
            }
            catch (Exception e)
            {
                Console.WriteLine(e);
            }
            finally
            {
                Interlocked.Exchange(ref _stateLock, 0);
            }
        }
    }
}