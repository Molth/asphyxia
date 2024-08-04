//------------------------------------------------------------
// あなたたちを許すことはできません
// Copyright © 2024 怨靈. All rights reserved.
//------------------------------------------------------------

using System.Collections.Concurrent;

namespace ENet
{
    /// <summary>
    ///     Nat travel service
    /// </summary>
    public sealed unsafe class NatTravelService
    {
        /// <summary>
        ///     Host
        /// </summary>
        private readonly Host _host = new();

        /// <summary>
        ///     Peers
        /// </summary>
        private readonly ConcurrentDictionary<ENetIPEndPoint, Peer> _peers = new();

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
            Library.Initialize();
            var address = new Address { Port = port };
            try
            {
                _host.Create(address, maxPeers);
            }
            catch
            {
                Library.Deinitialize();
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
                var polled = false;
                while (!polled)
                {
                    if (_host.CheckEvents(out var netEvent) <= 0)
                    {
                        if (_host.Service(0, out netEvent) <= 0)
                            break;
                        polled = true;
                    }

                    var peer = netEvent.Peer;
                    switch (netEvent.Type)
                    {
                        case EventType.None:
                            break;
                        case EventType.Connect:
                            _networkEvents.Enqueue(new NetworkEvent(NetworkEventType.Connect, peer));
                            break;
                        case EventType.Disconnect:
                            _networkEvents.Enqueue(new NetworkEvent(NetworkEventType.Disconnect, peer));
                            break;
                        case EventType.Receive:
                            _networkEvents.Enqueue(new NetworkEvent(NetworkEventType.Data, peer, netEvent.Packet));
                            break;
                        case EventType.Timeout:
                            peer.DisconnectNow(0);
                            _networkEvents.Enqueue(new NetworkEvent(NetworkEventType.Disconnect, peer));
                            break;
                    }
                }

                Thread.Sleep(1);
            }

            foreach (var peer in _peers.Values)
                peer.DisconnectNow(0);
            _peers.Clear();
            _host.Flush();
            _host.Dispose();
            Library.Deinitialize();
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
            var buffer = stackalloc byte[128];
            try
            {
                while (_networkEvents.TryDequeue(out var networkEvent))
                {
                    ENetIPEndPoint endPoint;
                    switch (networkEvent.EventType)
                    {
                        case NetworkEventType.Connect:
                            endPoint = new ENetIPEndPoint(networkEvent.Peer);
                            Console.WriteLine($"Connected: [{networkEvent.Peer.ID}] [{endPoint}]");
                            _peers[endPoint] = networkEvent.Peer;
                            var dataPacket = endPoint.CreateDataPacket(buffer, 0, PacketFlags.Reliable);
                            _outgoings.Enqueue(new NetworkOutgoing(networkEvent.Peer, dataPacket));
                            continue;
                        case NetworkEventType.Data:
                            var packet = networkEvent.Packet;
                            endPoint = new ENetIPEndPoint(packet);
                            packet.Dispose();
                            if (!_peers.TryGetValue(endPoint, out var peer) || networkEvent.Peer.ID == peer.ID)
                                continue;
                            var from = new ENetIPEndPoint(networkEvent.Peer);
                            Console.WriteLine($"Data: [{networkEvent.Peer.ID}] [{from}] to [{peer.ID}] [{endPoint}]");
                            packet = from.CreateDataPacket(buffer, 1, PacketFlags.Reliable);
                            var outgoing = new NetworkOutgoing(peer, packet);
                            _outgoings.Enqueue(outgoing);
                            continue;
                        case NetworkEventType.Disconnect:
                            endPoint = new ENetIPEndPoint(networkEvent.Peer);
                            Console.WriteLine($"Disconnected: [{networkEvent.Peer.ID}] [{endPoint}]");
                            _peers.TryRemove(endPoint, out _);
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