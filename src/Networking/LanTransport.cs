using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using System.Threading;
using EmberwardCoop.Core;

namespace EmberwardCoop.Networking
{
    // Reliable traffic uses TCP; unreliable updates use UDP on port + 1 with TCP fallback.
    public sealed class LanTransport : ITransport
    {
        public const int DiscoveryPort = 27778;
        private const string DiscoveryHeader = LobbyMetadataKeys.LanDiscoveryMagic + "\n";
        private const double StaleLobbySeconds = 5.5;

        private sealed class DiscoveryBeaconPayload
        {
            public string magic { get; set; }
            public string game_tag { get; set; }
            public string mod_signature { get; set; }
            public int protocol_version { get; set; }
            public string mod_version { get; set; }
            public string host_name { get; set; }
            public int port { get; set; }
            public string coop_mode { get; set; }
            public int player_count { get; set; }
            public int max_players { get; set; }
            public string scene_name { get; set; }
            public bool in_progress { get; set; }
            public bool joinable_in_progress { get; set; }
        }

        public TransportKind Kind => TransportKind.Lan;
        public SessionRole Role => _role;
        public bool IsAvailable => true;
        public bool IsRefreshingLobbies => DateTime.UtcNow < _refreshIndicatorUntilUtc;
        public string StatusText { get; private set; } = "LAN Ready";
        public string LobbyCode { get; private set; } = "";
        public int LocalPingMs { get; private set; }
        public int ListenPort { get; private set; } = 27777;

        public event Action<int, string, ulong> OnPeerConnected;
        public event Action<int, string> OnPeerDisconnected;
        public event Action OnConnectedToHost;
        public event Action<string> OnDisconnected;
        public event Action<string> OnError;
        public event Action OnLobbyListUpdated;

        private sealed class TcpPeer
        {
            public int PeerId;
            public string Name = "Player";
            public TcpClient Client;
            public NetworkStream Stream;
            public IPEndPoint RemoteUdpEndPoint;
            public volatile bool Connected;
            public long LastHeardTicks = DateTime.UtcNow.Ticks;
            public int PingMs;
            public readonly ConcurrentQueue<byte[]> SendQueue = new ConcurrentQueue<byte[]>();
            public readonly AutoResetEvent SendSignal = new AutoResetEvent(false);

            public void Enqueue(byte[] data, bool droppable)
            {
                if (!Connected) return;
                if (droppable && SendQueue.Count > 10) return;
                SendQueue.Enqueue(data);
                try { SendSignal.Set(); } catch { }
            }

            public void Close()
            {
                Connected = false;
                try { SendSignal.Set(); } catch { }
                try { Client?.Close(); } catch { }
            }
        }

        private volatile SessionRole _role = SessionRole.Offline;
        private volatile bool _running;
        private int _connectAttemptSeq;
        private int _nextPeerId;

        private TcpListener _tcpListener;
        private UdpClient _udpDataSocket;
        private IPEndPoint _hostUdpEndPoint;
        private readonly List<TcpPeer> _peers = new List<TcpPeer>();
        private TcpPeer _hostLink;

        private UdpClient _discoveryListener;
        private UdpClient _discoveryBroadcaster;
        private readonly object _broadcasterLock = new object();
        private volatile bool _discoveryActive;

        private readonly ConcurrentQueue<IncomingPacket> _incoming = new ConcurrentQueue<IncomingPacket>();
        private readonly ConcurrentQueue<Action> _mainThreadActions = new ConcurrentQueue<Action>();
        private readonly Dictionary<string, LobbySummary> _discoveredLobbies = new Dictionary<string, LobbySummary>(StringComparer.Ordinal);

        private string _hostName = "Host";
        private string _advertisedScene = "CoinPage";
        private int _advertisedPlayers = 1;
        private int _advertisedMaxPlayers = 4;
        private string _advertisedCoopMode = LobbyMetadataKeys.ModeShared;
        private bool _advertisedInProgress;
        private bool _advertisedJoinableInProgress;

        private DateTime _lastDiscoveryBroadcastUtc = DateTime.MinValue;
        private DateTime _lastPingSentUtc = DateTime.MinValue;
        private DateTime _lastStalePruneUtc = DateTime.MinValue;
        private DateTime _refreshIndicatorUntilUtc = DateTime.MinValue;

        public void StartDiscoveryListener()
        {
            if (_discoveryActive) return;
            try
            {
                _discoveryListener = new UdpClient();
                _discoveryListener.Client.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.ReuseAddress, true);
                _discoveryListener.Client.Bind(new IPEndPoint(IPAddress.Any, DiscoveryPort));
                _discoveryListener.EnableBroadcast = true;
                _discoveryActive = true;
                new Thread(DiscoveryListenLoop) { IsBackground = true, Name = "EWCoop_LanDiscovery" }.Start();
            }
            catch (Exception ex)
            {
                CoopLogger.Warn(LogCategory.Net, $"LAN discovery listener could not bind port {DiscoveryPort}: {ex.Message}");
            }
        }

        public bool StartHost(int port, string hostName, int maxPlayers, string coopMode = LobbyMetadataKeys.ModeShared)
        {
            Shutdown();
            try
            {
                Interlocked.Exchange(ref _nextPeerId, 0);
                ListenPort = port;
                _hostName = string.IsNullOrWhiteSpace(hostName) ? "Host" : hostName.Trim();
                _advertisedMaxPlayers = Math.Clamp(maxPlayers, 2, 6);
                _advertisedPlayers = 1;
                _advertisedCoopMode = LobbyMetadataKeys.IsValidCoopMode(coopMode) ? coopMode : LobbyMetadataKeys.ModeShared;
                _advertisedScene = string.IsNullOrEmpty(GameRefs.ActiveScene) ? "CoinPage" : GameRefs.ActiveScene;
                _advertisedInProgress = !GameRefs.IsInTitle;
                _advertisedJoinableInProgress = false;

                _tcpListener = new TcpListener(IPAddress.Any, port);
                _tcpListener.Start();

                try
                {
                    _udpDataSocket = new UdpClient(port + 1);
                    new Thread(UdpReceiveLoop) { IsBackground = true, Name = "EWCoop_LanUdpHost" }.Start();
                }
                catch (Exception ex)
                {
                    CoopLogger.Warn(LogCategory.Net, $"LAN UDP channel :{port + 1} unavailable (using TCP fallback): {ex.Message}");
                    _udpDataSocket = null;
                }

                _running = true;
                _role = SessionRole.Host;
                string localIp = GetBestLocalIPv4();
                LobbyCode = $"{localIp}:{port}";
                StatusText = $"Hosting LAN ({LobbyCode})";

                new Thread(AcceptLoop) { IsBackground = true, Name = "EWCoop_LanAccept" }.Start();
                CoopLogger.Info(LogCategory.Net, $"LAN Host started on {LobbyCode}");
                return true;
            }
            catch (Exception ex)
            {
                StatusText = $"Host failed: {ex.Message}";
                CoopLogger.Error(LogCategory.Net, $"Failed to start LAN host on :{port}", ex);
                Shutdown();
                OnError?.Invoke(StatusText);
                return false;
            }
        }

        public void ConnectAsync(string hostAddress, int port)
        {
            Shutdown();
            int attempt = Interlocked.Increment(ref _connectAttemptSeq);
            _role = SessionRole.Connecting;
            StatusText = $"Connecting to {hostAddress}:{port}...";
            LobbyCode = $"{hostAddress}:{port}";

            new Thread(() =>
            {
                TcpClient tcp = null;
                try
                {
                    tcp = new TcpClient { NoDelay = true };
                    var iar = tcp.BeginConnect(hostAddress, port, null, null);
                    bool ok = iar.AsyncWaitHandle.WaitOne(TimeSpan.FromSeconds(5.0));
                    if (!ok || !tcp.Connected)
                    {
                        try { tcp.Close(); } catch { }
                        if (_connectAttemptSeq == attempt && _role == SessionRole.Connecting)
                        {
                            _role = SessionRole.Offline;
                            StatusText = $"Timed out connecting to {hostAddress}:{port}";
                            _mainThreadActions.Enqueue(() => OnError?.Invoke(StatusText));
                        }
                        return;
                    }
                    tcp.EndConnect(iar);
                    if (_connectAttemptSeq != attempt)
                    {
                        try { tcp.Close(); } catch { }
                        return;
                    }

                    var remoteIp = (tcp.Client.RemoteEndPoint as IPEndPoint)?.Address;
                    if (remoteIp != null)
                    {
                        try
                        {
                            _hostUdpEndPoint = new IPEndPoint(remoteIp, port + 1);
                            _udpDataSocket = new UdpClient(0);
                            new Thread(UdpReceiveLoop) { IsBackground = true, Name = "EWCoop_LanUdpClient" }.Start();
                        }
                        catch
                        {
                            _udpDataSocket = null;
                            _hostUdpEndPoint = null;
                        }
                    }

                    _running = true;
                    var link = new TcpPeer
                    {
                        PeerId = 0,
                        Name = "Host",
                        Client = tcp,
                        Stream = tcp.GetStream(),
                        Connected = true,
                        LastHeardTicks = DateTime.UtcNow.Ticks
                    };
                    _hostLink = link;
                    _role = SessionRole.Client;
                    StatusText = $"Connected to {hostAddress}:{port}";

                    new Thread(() => TcpReadLoop(link, isClientSide: true)) { IsBackground = true, Name = "EWCoop_LanClientRead" }.Start();
                    new Thread(() => TcpWriteLoop(link)) { IsBackground = true, Name = "EWCoop_LanClientWrite" }.Start();

                    _mainThreadActions.Enqueue(() => OnConnectedToHost?.Invoke());
                }
                catch (Exception ex)
                {
                    try { tcp?.Close(); } catch { }
                    if (_connectAttemptSeq == attempt)
                    {
                        _role = SessionRole.Offline;
                        StatusText = $"Connect failed: {ex.Message}";
                        _mainThreadActions.Enqueue(() => OnError?.Invoke(StatusText));
                    }
                }
            })
            { IsBackground = true, Name = "EWCoop_LanConnect" }.Start();
        }

        public void Poll(Action<IncomingPacket> onPacket)
        {
            while (_mainThreadActions.TryDequeue(out var act))
            {
                try { act?.Invoke(); }
                catch (Exception ex) { CoopLogger.Error(LogCategory.Net, "LAN callback error", ex); }
            }

            int budget = 96;
            while (budget-- > 0 && _incoming.TryDequeue(out var pkt))
            {
                onPacket?.Invoke(pkt);
            }

            DateTime utcNow = DateTime.UtcNow;

            if ((utcNow - _lastStalePruneUtc).TotalSeconds >= 1.5)
            {
                _lastStalePruneUtc = utcNow;
                if (PruneStaleLobbies(utcNow))
                    OnLobbyListUpdated?.Invoke();
            }

            if (_role == SessionRole.Host)
            {
                if ((utcNow - _lastDiscoveryBroadcastUtc).TotalSeconds >= 1.4)
                {
                    _lastDiscoveryBroadcastUtc = utcNow;
                    SendDiscoveryBeacon();
                }

                if ((utcNow - _lastPingSentUtc).TotalSeconds >= 2.0)
                {
                    _lastPingSentUtc = utcNow;
                    CheckPeerTimeouts(utcNow.Ticks);
                }
            }
            else if (_role == SessionRole.Client)
            {
                if ((utcNow - _lastPingSentUtc).TotalSeconds >= 2.0)
                {
                    _lastPingSentUtc = utcNow;
                    SendClientPing(utcNow.Ticks);
                }
            }
        }

        public void SendToHost(PacketType type, byte[] payload, DeliveryMode mode)
        {
            if (_role != SessionRole.Client) return;
            byte[] framed = PacketProtocol.Frame(type, mode, payload);
            if (mode == DeliveryMode.Unreliable && _udpDataSocket != null && _hostUdpEndPoint != null && framed.Length <= 1300)
            {
                try
                {
                    _udpDataSocket.Send(framed, framed.Length, _hostUdpEndPoint);
                    return;
                }
                catch { }
            }
            _hostLink?.Enqueue(framed, mode == DeliveryMode.Unreliable);
        }

        public void SendToPeer(int peerId, PacketType type, byte[] payload, DeliveryMode mode)
        {
            if (_role != SessionRole.Host) return;
            TcpPeer target = null;
            lock (_peers)
            {
                for (int i = 0; i < _peers.Count; i++)
                {
                    if (_peers[i].PeerId == peerId && _peers[i].Connected)
                    {
                        target = _peers[i];
                        break;
                    }
                }
            }
            if (target == null) return;
            byte[] framed = PacketProtocol.Frame(type, mode, payload);
            SendFramedToPeer(target, framed, mode);
        }

        public void BroadcastToPeers(PacketType type, byte[] payload, DeliveryMode mode, int excludePeerId = -1)
        {
            if (_role != SessionRole.Host) return;
            byte[] framed = PacketProtocol.Frame(type, mode, payload);
            lock (_peers)
            {
                for (int i = 0; i < _peers.Count; i++)
                {
                    var p = _peers[i];
                    if (p.Connected && p.PeerId != excludePeerId)
                        SendFramedToPeer(p, framed, mode);
                }
            }
        }

        private void SendFramedToPeer(TcpPeer peer, byte[] framed, DeliveryMode mode)
        {
            if (mode == DeliveryMode.Unreliable && _udpDataSocket != null && peer.RemoteUdpEndPoint != null && framed.Length <= 1300)
            {
                try
                {
                    _udpDataSocket.Send(framed, framed.Length, peer.RemoteUdpEndPoint);
                    return;
                }
                catch { }
            }
            peer.Enqueue(framed, mode == DeliveryMode.Unreliable);
        }

        public void UpdateLobbyMetadata(string sceneName, int playerCount, int maxPlayers, string coopMode, bool inProgress, bool joinableInProgress = false)
        {
            _advertisedScene = string.IsNullOrEmpty(sceneName) ? "CoinPage" : sceneName;
            _advertisedPlayers = Math.Max(1, playerCount);
            _advertisedMaxPlayers = Math.Clamp(maxPlayers, 2, 6);
            if (LobbyMetadataKeys.IsValidCoopMode(coopMode))
                _advertisedCoopMode = coopMode;
            _advertisedInProgress = inProgress;
            _advertisedJoinableInProgress = joinableInProgress;
        }

        public void RefreshLobbyList()
        {
            StartDiscoveryListener();
            _refreshIndicatorUntilUtc = DateTime.UtcNow.AddSeconds(1.2);
            lock (_discoveredLobbies)
            {
                _discoveredLobbies.Clear();
            }
            OnLobbyListUpdated?.Invoke();
        }

        private bool PruneStaleLobbies(DateTime utcNow)
        {
            DateTime cutoff = utcNow.AddSeconds(-StaleLobbySeconds);
            bool removedAny = false;
            lock (_discoveredLobbies)
            {
                var stale = new List<string>();
                foreach (var kv in _discoveredLobbies)
                    if (kv.Value.LastSeenUtc < cutoff) stale.Add(kv.Key);
                for (int i = 0; i < stale.Count; i++)
                {
                    _discoveredLobbies.Remove(stale[i]);
                    removedAny = true;
                }
            }
            return removedAny;
        }

        public IReadOnlyList<LobbySummary> GetDiscoveredLobbies()
        {
            DateTime cutoff = DateTime.UtcNow.AddSeconds(-StaleLobbySeconds);
            var list = new List<LobbySummary>();
            lock (_discoveredLobbies)
            {
                foreach (var kv in _discoveredLobbies)
                    if (kv.Value.LastSeenUtc >= cutoff)
                        list.Add(kv.Value);
            }
            return list;
        }

        public int GetPeerPingMs(int peerId)
        {
            lock (_peers)
            {
                for (int i = 0; i < _peers.Count; i++)
                    if (_peers[i].PeerId == peerId) return _peers[i].PingMs;
            }
            return 0;
        }

        public void Shutdown()
        {
            Interlocked.Increment(ref _connectAttemptSeq);
            _running = false;
            _role = SessionRole.Offline;
            LocalPingMs = 0;
            LobbyCode = "";
            StatusText = "Offline";

            try { _tcpListener?.Stop(); } catch { }
            _tcpListener = null;

            try { _udpDataSocket?.Close(); } catch { }
            _udpDataSocket = null;
            _hostUdpEndPoint = null;

            lock (_broadcasterLock)
            {
                try { _discoveryBroadcaster?.Close(); } catch { }
                _discoveryBroadcaster = null;
            }

            lock (_peers)
            {
                for (int i = 0; i < _peers.Count; i++) _peers[i].Close();
                _peers.Clear();
            }

            if (_hostLink != null)
            {
                _hostLink.Close();
                _hostLink = null;
            }

            while (_incoming.TryDequeue(out _)) { }
        }

        private void AcceptLoop()
        {
            while (_running && _tcpListener != null)
            {
                try
                {
                    TcpClient tcp = _tcpListener.AcceptTcpClient();
                    tcp.NoDelay = true;

                    int currentPeers;
                    lock (_peers) currentPeers = _peers.Count;
                    if (currentPeers + 1 >= _advertisedMaxPlayers)
                    {
                        try
                        {
                            byte[] reject = PacketProtocol.Frame(PacketType.HandshakeReject, DeliveryMode.ReliableOrdered,
                                PacketProtocol.SerializePayload(w => w.Write("Lobby is full.")));
                            tcp.GetStream().Write(reject, 0, reject.Length);
                            tcp.Close();
                        }
                        catch { }
                        continue;
                    }

                    int pid = Interlocked.Increment(ref _nextPeerId);
                    var peer = new TcpPeer
                    {
                        PeerId = pid,
                        Name = $"Player {pid}",
                        Client = tcp,
                        Stream = tcp.GetStream(),
                        Connected = true,
                        LastHeardTicks = DateTime.UtcNow.Ticks
                    };
                    lock (_peers) _peers.Add(peer);

                    new Thread(() => TcpReadLoop(peer, isClientSide: false)) { IsBackground = true, Name = $"EWCoop_LanRead_{pid}" }.Start();
                    new Thread(() => TcpWriteLoop(peer)) { IsBackground = true, Name = $"EWCoop_LanWrite_{pid}" }.Start();

                    _mainThreadActions.Enqueue(() => OnPeerConnected?.Invoke(pid, peer.Name, 0));
                }
                catch
                {
                    if (!_running) break;
                }
            }
        }

        private void TcpWriteLoop(TcpPeer peer)
        {
            try
            {
                while (_running && peer.Connected)
                {
                    peer.SendSignal.WaitOne(80);
                    bool wrote = false;
                    while (peer.Connected && peer.SendQueue.TryDequeue(out var buf))
                    {
                        if (peer.Stream != null)
                        {
                            peer.Stream.Write(buf, 0, buf.Length);
                            wrote = true;
                        }
                    }
                    if (wrote && peer.Connected && peer.Stream != null)
                        peer.Stream.Flush();
                }
            }
            catch
            {
                peer.Close();
            }
        }

        private void TcpReadLoop(TcpPeer peer, bool isClientSide)
        {
            byte[] header = new byte[PacketProtocol.HeaderSize];
            try
            {
                while (_running && peer.Connected && peer.Client != null && peer.Client.Connected)
                {
                    if (!ReadExact(peer.Stream, header, PacketProtocol.HeaderSize))
                        break;

                    if (!PacketProtocol.TryParseHeader(header, 0, out var type, out var delivery, out int len, out byte version))
                        break;

                    if (version != PacketProtocol.ProtocolVersion)
                    {
                        if (!isClientSide)
                        {
                            byte[] rej = PacketProtocol.Frame(PacketType.HandshakeReject, DeliveryMode.ReliableOrdered,
                                PacketProtocol.SerializePayload(w => w.Write($"Protocol mismatch (Host v{PacketProtocol.ProtocolVersion}, Client v{version}).")));
                            peer.Enqueue(rej, false);
                            Thread.Sleep(50);
                        }
                        break;
                    }

                    byte[] payload = len > 0 ? new byte[len] : Array.Empty<byte>();
                    if (len > 0 && !ReadExact(peer.Stream, payload, len))
                        break;

                    Interlocked.Exchange(ref peer.LastHeardTicks, DateTime.UtcNow.Ticks);

                    if (type == PacketType.Ping && !isClientSide && len >= 8)
                    {
                        long sentTicks = BitConverter.ToInt64(payload, 0);
                        int reportedRtt = len >= 12 ? BitConverter.ToInt32(payload, 8) : 0;
                        peer.PingMs = reportedRtt;
                        peer.Enqueue(PacketProtocol.Frame(PacketType.Pong, DeliveryMode.ReliableOrdered,
                            PacketProtocol.SerializePayload(w => w.Write(sentTicks))), false);
                        continue;
                    }
                    if (type == PacketType.Pong && isClientSide && len >= 8)
                    {
                        long sentTicks = BitConverter.ToInt64(payload, 0);
                        double ms = TimeSpan.FromTicks(DateTime.UtcNow.Ticks - sentTicks).TotalMilliseconds;
                        if (ms >= 0 && ms < 9999) LocalPingMs = (int)Math.Round(ms);
                        continue;
                    }

                    _incoming.Enqueue(new IncomingPacket(peer.PeerId, type, delivery, payload));
                }
            }
            catch { }
            finally
            {
                peer.Close();
                if (isClientSide)
                {
                    if (_role == SessionRole.Client)
                    {
                        _role = SessionRole.Offline;
                        _hostLink = null;
                        StatusText = "Disconnected from LAN Host.";
                        _mainThreadActions.Enqueue(() => OnDisconnected?.Invoke("Disconnected from LAN Host."));
                    }
                }
                else
                {
                    lock (_peers) _peers.Remove(peer);
                    _mainThreadActions.Enqueue(() => OnPeerDisconnected?.Invoke(peer.PeerId, peer.Name));
                }
            }
        }

        private void UdpReceiveLoop()
        {
            var ep = new IPEndPoint(IPAddress.Any, 0);
            while (_running && _udpDataSocket != null)
            {
                try
                {
                    byte[] data = _udpDataSocket.Receive(ref ep);
                    if (data == null || data.Length < PacketProtocol.HeaderSize) continue;
                    if (!PacketProtocol.TryParseHeader(data, 0, out var type, out var delivery, out int len, out byte version)) continue;
                    if (version != PacketProtocol.ProtocolVersion || data.Length < PacketProtocol.HeaderSize + len) continue;

                    byte[] payload = len > 0 ? new byte[len] : Array.Empty<byte>();
                    if (len > 0) Buffer.BlockCopy(data, PacketProtocol.HeaderSize, payload, 0, len);

                    int senderId = 0;
                    if (_role == SessionRole.Host)
                    {
                        senderId = ResolveOrBindUdpPeer(ep, payload, type);
                        if (senderId <= 0) continue;
                    }
                    _incoming.Enqueue(new IncomingPacket(senderId, type, delivery, payload));
                }
                catch
                {
                    if (!_running || _udpDataSocket == null) break;
                }
            }
        }

        private int ResolveOrBindUdpPeer(IPEndPoint remoteEp, byte[] payload, PacketType type)
        {
            int claimedId = (type == PacketType.CursorAndGhostState && payload.Length >= 4)
                ? BitConverter.ToInt32(payload, 0) : -1;

            lock (_peers)
            {
                for (int i = 0; i < _peers.Count; i++)
                {
                    var p = _peers[i];
                    if (p.RemoteUdpEndPoint != null && p.RemoteUdpEndPoint.Equals(remoteEp))
                        return p.PeerId;
                }
                for (int i = 0; i < _peers.Count; i++)
                {
                    var p = _peers[i];
                    var tcpEp = p.Client?.Client?.RemoteEndPoint as IPEndPoint;
                    if ((claimedId > 0 && p.PeerId == claimedId) ||
                        (tcpEp != null && tcpEp.Address.Equals(remoteEp.Address) && p.RemoteUdpEndPoint == null))
                    {
                        p.RemoteUdpEndPoint = new IPEndPoint(remoteEp.Address, remoteEp.Port);
                        return p.PeerId;
                    }
                }
            }
            return -1;
        }

        private void DiscoveryListenLoop()
        {
            var ep = new IPEndPoint(IPAddress.Any, 0);
            while (_discoveryActive && _discoveryListener != null)
            {
                try
                {
                    byte[] data = _discoveryListener.Receive(ref ep);
                    if (data == null || data.Length <= DiscoveryHeader.Length || data.Length > 2048) continue;

                    string msg = Encoding.UTF8.GetString(data);
                    if (!msg.StartsWith(DiscoveryHeader, StringComparison.Ordinal))
                        continue;

                    string json = msg.Substring(DiscoveryHeader.Length);
                    DiscoveryBeaconPayload beacon;
                    try
                    {
                        beacon = JsonSerializer.Deserialize<DiscoveryBeaconPayload>(json);
                    }
                    catch
                    {
                        continue;
                    }

                    if (beacon == null) continue;

                    // Strict validation: magic header, game tag, mod signature, matching protocol_version, non-empty host, valid counts
                    if (!string.Equals(beacon.magic, LobbyMetadataKeys.LanDiscoveryMagic, StringComparison.Ordinal)) continue;
                    if (!string.Equals(beacon.game_tag, LobbyMetadataKeys.GameTagValue, StringComparison.Ordinal)) continue;
                    if (!string.Equals(beacon.mod_signature, LobbyMetadataKeys.ModSignatureValue, StringComparison.Ordinal)) continue;
                    if (beacon.protocol_version != PacketProtocol.ProtocolVersion) continue;
                    if (string.IsNullOrWhiteSpace(beacon.host_name)) continue;
                    if (beacon.port < 1 || beacon.port > 65535) continue;
                    if (!LobbyMetadataKeys.IsValidCoopMode(beacon.coop_mode)) continue;
                    if (beacon.max_players < 2 || beacon.max_players > 6) continue;

                    string key = $"{ep.Address}:{beacon.port}";

                    // Hide full or non-joinable in-progress runs immediately
                    if (beacon.player_count < 1 || beacon.player_count >= beacon.max_players || (beacon.in_progress && !beacon.joinable_in_progress))
                    {
                        bool removed;
                        lock (_discoveredLobbies) removed = _discoveredLobbies.Remove(key);
                        if (removed) _mainThreadActions.Enqueue(() => OnLobbyListUpdated?.Invoke());
                        continue;
                    }

                    var summary = new LobbySummary
                    {
                        Transport = TransportKind.Lan,
                        LobbyKey = key,
                        GameTag = beacon.game_tag,
                        ModSignature = beacon.mod_signature,
                        ProtocolVersion = beacon.protocol_version,
                        ModVersion = string.IsNullOrWhiteSpace(beacon.mod_version) ? $"v{beacon.protocol_version}" : beacon.mod_version.Trim(),
                        HostName = beacon.host_name.Trim(),
                        CoopMode = beacon.coop_mode,
                        AddressOrCode = key,
                        Port = beacon.port,
                        SceneName = string.IsNullOrWhiteSpace(beacon.scene_name) ? "CoinPage" : beacon.scene_name.Trim(),
                        PlayerCount = beacon.player_count,
                        MaxPlayers = beacon.max_players,
                        InProgress = beacon.in_progress,
                        JoinableInProgress = beacon.joinable_in_progress,
                        PingMs = 1,
                        LastSeenUtc = DateTime.UtcNow
                    };

                    bool isNewOrChanged = false;
                    lock (_discoveredLobbies)
                    {
                        if (!_discoveredLobbies.TryGetValue(key, out var prev) ||
                            prev.PlayerCount != summary.PlayerCount ||
                            prev.MaxPlayers != summary.MaxPlayers ||
                            prev.CoopMode != summary.CoopMode ||
                            prev.HostName != summary.HostName)
                        {
                            isNewOrChanged = true;
                        }
                        _discoveredLobbies[key] = summary;
                    }

                    if (isNewOrChanged)
                        _mainThreadActions.Enqueue(() => OnLobbyListUpdated?.Invoke());
                }
                catch
                {
                    if (!_discoveryActive || _discoveryListener == null) break;
                    Thread.Sleep(150);
                }
            }
        }

        private void SendDiscoveryBeacon()
        {
            var beacon = new DiscoveryBeaconPayload
            {
                magic = LobbyMetadataKeys.LanDiscoveryMagic,
                game_tag = LobbyMetadataKeys.GameTagValue,
                mod_signature = LobbyMetadataKeys.ModSignatureValue,
                protocol_version = PacketProtocol.ProtocolVersion,
                mod_version = CoopPlugin.PluginVersion,
                host_name = string.IsNullOrWhiteSpace(_hostName) ? "Host" : _hostName.Trim(),
                port = ListenPort,
                coop_mode = _advertisedCoopMode,
                player_count = _advertisedPlayers,
                max_players = _advertisedMaxPlayers,
                scene_name = string.IsNullOrWhiteSpace(_advertisedScene) ? "CoinPage" : _advertisedScene,
                in_progress = _advertisedInProgress,
                joinable_in_progress = _advertisedJoinableInProgress
            };

            string payload = DiscoveryHeader + JsonSerializer.Serialize(beacon);
            ThreadPool.QueueUserWorkItem(_ =>
            {
                lock (_broadcasterLock)
                {
                    try
                    {
                        if (_role != SessionRole.Host) return;
                        _discoveryBroadcaster ??= new UdpClient { EnableBroadcast = true };
                        byte[] bytes = Encoding.UTF8.GetBytes(payload);
                        _discoveryBroadcaster.Send(bytes, bytes.Length, new IPEndPoint(IPAddress.Broadcast, DiscoveryPort));
                    }
                    catch { }
                }
            });
        }

        private void SendClientPing(long nowTicks)
        {
            var link = _hostLink;
            if (link == null || !link.Connected) return;
            byte[] payload = PacketProtocol.SerializePayload(w =>
            {
                w.Write(nowTicks);
                w.Write(LocalPingMs);
            });
            link.Enqueue(PacketProtocol.Frame(PacketType.Ping, DeliveryMode.ReliableOrdered, payload), false);

            double silent = TimeSpan.FromTicks(nowTicks - Interlocked.Read(ref link.LastHeardTicks)).TotalSeconds;
            if (silent > 14.0) link.Close();
        }

        private void CheckPeerTimeouts(long nowTicks)
        {
            lock (_peers)
            {
                for (int i = _peers.Count - 1; i >= 0; i--)
                {
                    double silent = TimeSpan.FromTicks(nowTicks - Interlocked.Read(ref _peers[i].LastHeardTicks)).TotalSeconds;
                    if (silent > 16.0) _peers[i].Close();
                }
            }
        }

        private static bool ReadExact(NetworkStream stream, byte[] buffer, int count)
        {
            int offset = 0;
            while (offset < count)
            {
                int r = stream.Read(buffer, offset, count - offset);
                if (r <= 0) return false;
                offset += r;
            }
            return true;
        }

        private static string GetBestLocalIPv4()
        {
            try
            {
                using var sock = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, 0);
                sock.Connect("8.8.8.8", 65530);
                if (sock.LocalEndPoint is IPEndPoint ep)
                    return ep.Address.ToString();
            }
            catch { }
            return "127.0.0.1";
        }
    }
}
