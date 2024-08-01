//------------------------------------------------------------
// あなたたちを許すことはできません
// Copyright © 2024 怨靈. All rights reserved.
//------------------------------------------------------------

using System.Collections.Concurrent;
using System.Net.Sockets;

#pragma warning disable CS8602
#pragma warning disable CS8625

// ReSharper disable CommentTypo

namespace asphyxia
{
    /// <summary>
    ///     Network client
    /// </summary>
    public abstract class AsphyxiaClient
    {
        /// <summary>
        ///     Host
        /// </summary>
        private readonly Host _host = new();

        /// <summary>
        ///     Peer
        /// </summary>
        private Peer? _peer;

        /// <summary>
        ///     NetworkEvents
        /// </summary>
        private readonly ConcurrentQueue<NetworkEvent> _networkEvents = new();

        /// <summary>
        ///     Removed Peers
        /// </summary>
        private int _removedPeer;

        /// <summary>
        ///     Outgoings
        /// </summary>
        private readonly ConcurrentQueue<DataPacket> _outgoings = new();

        /// <summary>
        ///     State lock
        /// </summary>
        private int _stateLock;

        /// <summary>
        ///     Is created
        /// </summary>
        public bool IsSet => _host.IsSet;

        /// <summary>
        ///     Connected
        /// </summary>
        public bool Connected => _peer != null && _peer.State == PeerState.Connected;

        /// <summary>
        ///     Create
        /// </summary>
        /// <param name="manual">Manual</param>
        /// <param name="ipAddress">IPAddress</param>
        /// <param name="port">Port</param>
        /// <param name="ipv6">IPv6</param>
        public SocketError Start(bool manual, string ipAddress, ushort port, bool ipv6 = false)
        {
            var error = _host.Create(1, 0, ipv6);
            if (error != SocketError.Success)
                return error;
            if (!manual)
                new Thread(Polling) { IsBackground = true }.Start();
            _host.Connect(ipAddress, port);
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
            if (_removedPeer == 1)
                _peer.DisconnectNow();
            while (_outgoings.TryDequeue(out var outgoing))
                outgoing.Dispose();
            _peer = null;
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
            if (_removedPeer == 1)
            {
                Interlocked.Exchange(ref _removedPeer, 0);
                _peer.DisconnectNow();
            }

            if (_peer != null && _peer.State == PeerState.Connected)
            {
                while (_outgoings.TryDequeue(out var outgoing))
                {
                    try
                    {
                        _peer.Send(outgoing);
                    }
                    finally
                    {
                        outgoing.Dispose();
                    }
                }
            }
            else
            {
                while (_outgoings.TryDequeue(out var outgoing))
                    outgoing.Dispose();
            }

            _host.Flush();
        }

        /// <summary>
        ///     Update
        /// </summary>
        public void Update()
        {
            while (_networkEvents.TryDequeue(out var networkEvent))
            {
                switch (networkEvent.EventType)
                {
                    case NetworkEventType.Data:
                        var packet = networkEvent.Packet;
                        OnData(packet.AsSpan(), packet.Flags);
                        packet.Dispose();
                        continue;
                    case NetworkEventType.Connect:
                        _peer = networkEvent.Peer;
                        OnConnected();
                        continue;
                    case NetworkEventType.Disconnect:
                    case NetworkEventType.Timeout:
                        OnDisconnected();
                        _peer = null;
                        continue;
                    default:
                        continue;
                }
            }
        }

        /// <summary>
        ///     Send
        /// </summary>
        /// <param name="data">Data</param>
        /// <param name="flags">Flags</param>
        public void Send(Span<byte> data, PacketFlag flags)
        {
            if (_peer == null || _peer.State != PeerState.Connected)
                return;
            _outgoings.Enqueue(DataPacket.Create(data, flags));
        }

        /// <summary>
        ///     Disconnect
        /// </summary>
        public void Disconnect()
        {
            if (_peer == null)
                return;
            Interlocked.Exchange(ref _removedPeer, 1);
        }

        /// <summary>
        ///     Connected callback
        /// </summary>
        protected abstract void OnConnected();

        /// <summary>
        ///     Diconnected callback
        /// </summary>
        protected abstract void OnDisconnected();

        /// <summary>
        ///     Data callback
        /// </summary>
        /// <param name="data">Data</param>
        /// <param name="flags">Flags</param>
        protected abstract void OnData(Span<byte> data, PacketFlag flags);
    }
}