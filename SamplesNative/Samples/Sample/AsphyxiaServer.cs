//------------------------------------------------------------
// あなたたちを許すことはできません
// Copyright © 2024 怨靈. All rights reserved.
//------------------------------------------------------------

using System.Collections.Concurrent;
using System.Net.Sockets;

// ReSharper disable CommentTypo

namespace asphyxia
{
    /// <summary>
    ///     Network server
    /// </summary>
    public abstract class AsphyxiaServer
    {
        /// <summary>
        ///     Host
        /// </summary>
        private readonly Host _host = new();

        /// <summary>
        ///     Peers
        /// </summary>
        private readonly ConcurrentDictionary<uint, Peer> _peers = new();

        /// <summary>
        ///     NetworkEvents
        /// </summary>
        private readonly ConcurrentQueue<NetworkEvent> _networkEvents = new();

        /// <summary>
        ///     Removed Peers
        /// </summary>
        private readonly ConcurrentQueue<Peer> _removedPeers = new();

        /// <summary>
        ///     Outgoings
        /// </summary>
        private readonly ConcurrentQueue<NetworkOutgoing> _outgoings = new();

        /// <summary>
        ///     State lock
        /// </summary>
        private int _stateLock;

        /// <summary>
        ///     Is created
        /// </summary>
        public bool IsSet => _host.IsSet;

        /// <summary>
        ///     Create
        /// </summary>
        /// <param name="manual">Manual</param>
        /// <param name="maxPeers">Max peers</param>
        /// <param name="port">Port</param>
        /// <param name="ipv6">IPv6</param>
        public SocketError Start(bool manual, int maxPeers, ushort port = 0, bool ipv6 = false)
        {
            var error = _host.Create(maxPeers, port, ipv6);
            if (error != SocketError.Success)
                return error;
            if (!manual)
                new Thread(Polling) { IsBackground = true }.Start();
            return SocketError.Success;
        }

        /// <summary>
        ///     Polling
        /// </summary>
        private void Polling()
        {
            var spinWait = new SpinWait();
            Interlocked.Exchange(ref _stateLock, 1);
            while (_stateLock == 1)
            {
                Service();
                Flush();
                spinWait.SpinOnce();
            }

            OnShutdown();
        }

        /// <summary>
        ///     Shutdown
        /// </summary>
        private void OnShutdown()
        {
            while (_removedPeers.TryDequeue(out var peer))
                peer.DisconnectNow();
            while (_outgoings.TryDequeue(out var outgoing))
                outgoing.Dispose();
            _peers.Clear();
            _host.Flush();
            _host.Dispose();
        }

        /// <summary>
        ///     Shutdown
        /// </summary>
        public void Shutdown()
        {
            if (Interlocked.CompareExchange(ref _stateLock, 0, 1) == 1)
                return;
            OnShutdown();
        }

        /// <summary>
        ///     Service
        /// </summary>
        public void Service()
        {
            _host.Service();
            while (_host.CheckEvents(out var networkEvent))
                _networkEvents.Enqueue(networkEvent);
        }

        /// <summary>
        ///     Flush
        /// </summary>
        public void Flush()
        {
            while (_removedPeers.TryDequeue(out var peer))
                peer.DisconnectNow();
            while (_outgoings.TryDequeue(out var outgoing))
                outgoing.Send();
            _host.Flush();
        }

        /// <summary>
        ///     Update
        /// </summary>
        public void Update()
        {
            while (_networkEvents.TryDequeue(out var networkEvent))
            {
                var id = networkEvent.Peer.Id;
                switch (networkEvent.EventType)
                {
                    case NetworkEventType.Data:
                        var packet = networkEvent.Packet;
                        OnData(id, packet.AsSpan(), packet.Flags);
                        packet.Dispose();
                        continue;
                    case NetworkEventType.Connect:
                        _peers[id] = networkEvent.Peer;
                        OnConnected(id);
                        continue;
                    case NetworkEventType.Disconnect:
                    case NetworkEventType.Timeout:
                        OnDisconnected(id);
                        _peers.TryRemove(id, out _);
                        continue;
                    default:
                        continue;
                }
            }
        }

        /// <summary>
        ///     Send
        /// </summary>
        /// <param name="id">Id</param>
        /// <param name="data">Data</param>
        /// <param name="flags">Flags</param>
        public void Send(uint id, Span<byte> data, PacketFlag flags)
        {
            if (!_peers.TryGetValue(id, out var peer))
                return;
            _outgoings.Enqueue(NetworkOutgoing.Create(peer, data, flags));
        }

        /// <summary>
        ///     Disconnect
        /// </summary>
        /// <param name="id">Id</param>
        public void Disconnect(uint id)
        {
            if (!_peers.TryRemove(id, out var peer))
                return;
            _removedPeers.Enqueue(peer);
        }

        /// <summary>
        ///     Connected callback
        /// </summary>
        /// <param name="id">Id</param>
        protected abstract void OnConnected(uint id);

        /// <summary>
        ///     Diconnected callback
        /// </summary>
        /// <param name="id">Id</param>
        protected abstract void OnDisconnected(uint id);

        /// <summary>
        ///     Data callback
        /// </summary>
        /// <param name="id">Id</param>
        /// <param name="data">Data</param>
        /// <param name="flags">Flags</param>
        protected abstract void OnData(uint id, Span<byte> data, PacketFlag flags);
    }
}