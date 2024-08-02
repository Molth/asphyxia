//------------------------------------------------------------
// あなたたちを許すことはできません
// Copyright © 2024 怨靈. All rights reserved.
//------------------------------------------------------------

using System.Collections.Concurrent;
using System.Net;
using System.Net.Sockets;
using Host = LiteNetLib.NetManager;
using Peer = LiteNetLib.NetPeer;

namespace LiteNetLib
{
    /// <summary>
    ///     Nat travel service
    /// </summary>
    public sealed unsafe class NatTravelService : INetEventListener
    {
        /// <summary>
        ///     Host
        /// </summary>
        private readonly Host _host;

        /// <summary>
        ///     Peers
        /// </summary>
        private readonly ConcurrentDictionary<int, Peer> _peers = new();

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
        ///     MaxPeers
        /// </summary>
        private int _maxPeers;

        /// <summary>
        ///     Structure
        /// </summary>
        public NatTravelService() => _host = new Host(this);

        public void OnPeerConnected(Peer peer) => _networkEvents.Enqueue(new NetworkEvent(NetworkEventType.Connect, peer));

        public void OnPeerDisconnected(Peer peer, DisconnectInfo disconnectInfo) => _networkEvents.Enqueue(new NetworkEvent(NetworkEventType.Disconnect, peer));

        public void OnNetworkError(IPEndPoint endPoint, SocketError socketError)
        {
        }

        public void OnNetworkReceive(Peer peer, NetPacketReader reader, byte channelNumber, DeliveryMethod deliveryMethod)
        {
            _networkEvents.Enqueue(new NetworkEvent(NetworkEventType.Data, peer, DataPacket.Create(reader.RawData.AsSpan(reader.Position, reader.AvailableBytes), deliveryMethod)));
            reader.Recycle();
        }

        public void OnNetworkReceiveUnconnected(IPEndPoint remoteEndPoint, NetPacketReader reader, UnconnectedMessageType messageType)
        {
        }

        public void OnNetworkLatencyUpdate(Peer peer, int latency)
        {
        }

        public void OnConnectionRequest(ConnectionRequest request)
        {
            if (_host.ConnectedPeersCount < _maxPeers)
                request.Accept();
            else
                request.Reject();
        }

        /// <summary>
        ///     Create
        /// </summary>
        /// <param name="maxPeers">Max peers</param>
        /// <param name="port">Port</param>
        public void Create(int maxPeers, ushort port)
        {
            if (Interlocked.CompareExchange(ref _state, 1, 0) != 0)
                throw new InvalidOperationException("Service has created.");
            _maxPeers = maxPeers;
            try
            {
                _host.Start(port);
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
                _host.PollEvents();
                Thread.Sleep(1);
            }

            _peers.Clear();
            _host.DisconnectAll();
            _host.Stop();
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
            var buffer = stackalloc byte[20];
            try
            {
                while (_networkEvents.TryDequeue(out var networkEvent))
                {
                    switch (networkEvent.EventType)
                    {
                        case NetworkEventType.Connect:
                            Console.WriteLine($"Connected: [{networkEvent.Peer.Id}] [{(IPEndPoint)networkEvent.Peer}]");
                            _peers[networkEvent.Peer.HashCode(buffer)] = networkEvent.Peer;
                            var dataPacket = networkEvent.Peer.CreateDataPacket(1);
                            dataPacket.Data[0] = 0;
                            _outgoings.Enqueue(new NetworkOutgoing(networkEvent.Peer, dataPacket));
                            continue;
                        case NetworkEventType.Data:
                            var packet = networkEvent.Packet;
                            var hashCode = new HashCode();
                            hashCode.AddBytes(packet.AsSpan());
                            packet.Dispose();
                            if (!_peers.TryGetValue(hashCode.ToHashCode(), out var peer) || networkEvent.Peer.Id == peer.Id)
                                continue;
                            Console.WriteLine($"Data: [{networkEvent.Peer.Id}] [{(IPEndPoint)networkEvent.Peer}] to [{peer.Id}] [{(IPEndPoint)peer}]");
                            packet = networkEvent.Peer.CreateDataPacket(1);
                            packet.Data[0] = 1;
                            var outgoing = new NetworkOutgoing(peer, packet);
                            _outgoings.Enqueue(outgoing);
                            continue;
                        case NetworkEventType.Disconnect:
                            Console.WriteLine($"Disconnected: [{networkEvent.Peer.Id}] [{(IPEndPoint)networkEvent.Peer}]");
                            _peers.TryRemove(networkEvent.Peer.HashCode(buffer), out _);
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