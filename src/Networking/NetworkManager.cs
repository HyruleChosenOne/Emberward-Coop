using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using EmberwardCoop.Core;

namespace EmberwardCoop.Networking
{
    public sealed class NetworkManager
    {
        public readonly LanTransport Lan = new LanTransport();
        public readonly SteamP2PTransport Steam = new SteamP2PTransport();

        private ITransport _active;

        public TransportKind ActiveTransportKind => _active?.Kind ?? TransportKind.None;
        public SessionRole Role => _active?.Role ?? SessionRole.Offline;
        public bool IsHost => Role == SessionRole.Host;
        public bool IsClient => Role == SessionRole.Client;
        public bool IsConnected => Role == SessionRole.Host || Role == SessionRole.Client;
        public bool IsConnecting => Role == SessionRole.Connecting;

        public int LocalPlayerId { get; private set; } = 0;
        public string LocalPlayerName { get; set; } = "Player";
        public string HostPlayerName { get; private set; } = "Host";
        public int LocalColorIndex { get; private set; } = 0;
        public int PingMs => _active?.LocalPingMs ?? 0;
        public string StatusText => _active?.StatusText ?? "Offline";
        public string LobbyCode => _active?.LobbyCode ?? "";

        public readonly Dictionary<int, PeerInfo> Roster = new Dictionary<int, PeerInfo>();

        public event Action<PeerInfo> OnPlayerJoined;
        public event Action<PeerInfo> OnPlayerLeft;
        public event Action OnJoinedHost;
        public event Action<string> OnDisconnectedFromSession;
        public event Action<string> OnConnectionError;
        public event Action OnRosterUpdated;

        public bool LastDisconnectWasIntentional { get; private set; }

        private Action<IncomingPacket> _packetHandler;

        public void Initialize(Action<IncomingPacket> packetHandler)
        {
            _packetHandler = packetHandler;
            LocalPlayerName = SteamP2PTransport.IsSteamReady()
                ? SteamP2PTransport.GetLocalSteamName()
                : (CoopConfig.PlayerName?.Value ?? "Player");
            LocalColorIndex = Math.Clamp(CoopConfig.PreferredColorIndex?.Value ?? 0, 0, CoopConfig.PlayerPalette.Length - 1);

            WireTransport(Lan);
            WireTransport(Steam);
            Steam.OnHostLobbyReady += () =>
            {
                if (_active == Steam) OnRosterUpdated?.Invoke();
            };
            Lan.StartDiscoveryListener();
        }

        private void WireTransport(ITransport transport)
        {
            transport.OnPeerConnected += (peerId, name, steamId) =>
            {
                if (_active != transport) return;
                CoopLogger.Debug(LogCategory.Net, $"Peer #{peerId} ({name}) transport connected; awaiting handshake.");
            };

            transport.OnPeerDisconnected += (peerId, name) =>
            {
                if (_active != transport) return;
                PeerInfo removed = null;
                if (Roster.TryGetValue(peerId, out removed))
                    Roster.Remove(peerId);
                removed ??= new PeerInfo { PlayerId = peerId, Name = name };
                CoopLogger.Info(LogCategory.Net, $"{removed.Name} left the lobby.", addToFeed: true);
                BroadcastRoster();
                OnPlayerLeft?.Invoke(removed);
                OnRosterUpdated?.Invoke();
            };

            transport.OnConnectedToHost += () =>
            {
                if (_active != transport) return;
                CoopLogger.Info(LogCategory.Net, "Transport connected to Host; sending handshake...");
                SendToHost(PacketType.HandshakeHello, w =>
                {
                    w.Write(PacketProtocol.ProtocolVersion);
                    w.WriteSafeString(GameDataManager.instance != null ? GameDataManager.instance.GetCurrentGameVersion() : "1.0");
                    w.WriteSafeString(LocalPlayerName);
                    w.Write(LocalColorIndex);
                    w.Write(SteamP2PTransport.GetLocalSteamId());
                });
            };

            transport.OnDisconnected += reason =>
            {
                if (_active != transport) return;
                LastDisconnectWasIntentional = false;
                Roster.Clear();
                LocalPlayerId = 0;
                CoopLogger.Warn(LogCategory.Net, reason, addToFeed: true);
                OnDisconnectedFromSession?.Invoke(reason);
                OnRosterUpdated?.Invoke();
            };

            transport.OnError += err =>
            {
                CoopLogger.Warn(LogCategory.Net, err, addToFeed: true);
                OnConnectionError?.Invoke(err);
            };
        }

        public bool HostLan(int port)
        {
            Disconnect();
            _active = Lan;
            if (!Lan.StartHost(port, LocalPlayerName, CoopConfig.MaxPlayers.Value))
            {
                _active = null;
                return false;
            }
            SetupHostRoster();
            return true;
        }

        public void JoinLan(string ip, int port)
        {
            Disconnect();
            _active = Lan;
            Lan.ConnectAsync(ip, port);
        }

        public bool HostSteam(SteamLobbyPrivacy privacy)
        {
            Disconnect();
            LocalPlayerName = SteamP2PTransport.GetLocalSteamName();
            _active = Steam;
            if (!Steam.HostLobby(privacy, CoopConfig.MaxPlayers.Value))
            {
                _active = null;
                return false;
            }
            SetupHostRoster();
            return true;
        }

        public bool JoinSteam(ulong lobbyId)
        {
            Disconnect();
            LocalPlayerName = SteamP2PTransport.GetLocalSteamName();
            _active = Steam;
            if (!Steam.JoinLobbyById(lobbyId))
            {
                _active = null;
                return false;
            }
            return true;
        }

        private void SetupHostRoster()
        {
            LocalPlayerId = 0;
            HostPlayerName = LocalPlayerName;
            Roster.Clear();
            Roster[0] = new PeerInfo
            {
                PlayerId = 0,
                Name = LocalPlayerName,
                ColorIndex = LocalColorIndex,
                PingMs = 0,
                IsReady = true,
                SteamId = SteamP2PTransport.GetLocalSteamId()
            };
            OnRosterUpdated?.Invoke();
        }

        public void Disconnect(bool intentional = true)
        {
            bool wasConnected = IsConnected || IsConnecting;
            LastDisconnectWasIntentional = intentional;
            _active?.Shutdown();
            _active = null;
            Roster.Clear();
            LocalPlayerId = 0;
            if (wasConnected)
            {
                OnDisconnectedFromSession?.Invoke(intentional ? "Disconnected." : "Host closed the lobby.");
                OnRosterUpdated?.Invoke();
            }
        }

        public void Poll()
        {
            // Keep both transports polled so background lobby discovery stays fresh.
            if (_active == Lan)
            {
                Lan.Poll(HandleRawPacket);
                Steam.Poll(_ => { });
            }
            else if (_active == Steam)
            {
                Steam.Poll(HandleRawPacket);
                Lan.Poll(_ => { });
            }
            else
            {
                Steam.Poll(_ => { });
                Lan.Poll(_ => { });
            }

            if (IsHost && _active != null)
            {
                foreach (var kv in Roster)
                {
                    if (kv.Key == 0) continue;
                    kv.Value.PingMs = _active == Lan
                        ? Lan.GetPeerPingMs(kv.Key)
                        : Steam.GetPeerPingMs(kv.Key);
                }
            }
        }

        public void UpdateLobbyMetadata(string sceneName)
        {
            if (IsHost && _active != null)
                _active.UpdateLobbyMetadata(sceneName, Math.Max(1, Roster.Count), CoopConfig.MaxPlayers.Value);
        }

        public int RemotePeerCount => Math.Max(0, Roster.Count - (Roster.ContainsKey(LocalPlayerId) ? 1 : 0));

        public PeerInfo GetPeer(int playerId) => Roster.TryGetValue(playerId, out var p) ? p : null;

        public string GetPlayerName(int playerId) => Roster.TryGetValue(playerId, out var p) ? p.Name : $"Player #{playerId}";

        public int GetPlayerColorIndex(int playerId) => Roster.TryGetValue(playerId, out var p) ? p.ColorIndex : (playerId % CoopConfig.PlayerPalette.Length);

        private void HandleRawPacket(IncomingPacket pkt)
        {
            switch (pkt.Type)
            {
                case PacketType.HandshakeHello:
                    if (IsHost) HandleHandshakeHello(pkt.SenderPlayerId, pkt.Payload);
                    return;

                case PacketType.HandshakeWelcome:
                    if (IsClient) HandleHandshakeWelcome(pkt.Payload);
                    return;

                case PacketType.HandshakeReject:
                    HandleHandshakeReject(pkt.Payload);
                    return;

                case PacketType.RosterSync:
                    if (IsClient) HandleRosterSync(pkt.Payload);
                    return;

                case PacketType.CmdSetReady:
                    if (IsHost) HandleCmdSetReady(pkt.SenderPlayerId, pkt.Payload);
                    return;

                case PacketType.CmdSetColor:
                    if (IsHost) HandleCmdSetColor(pkt.SenderPlayerId, pkt.Payload);
                    return;

                default:
                    _packetHandler?.Invoke(pkt);
                    break;
            }
        }

        private void HandleHandshakeHello(int peerId, byte[] payload)
        {
            using var ms = new MemoryStream(payload);
            using var r = new BinaryReader(ms, Encoding.UTF8);
            byte protoVer = r.ReadByte();
            _ = r.ReadString();
            string name = r.ReadString();
            int prefColor = r.ReadInt32();
            ulong steamId = r.ReadUInt64();

            if (protoVer != PacketProtocol.ProtocolVersion)
            {
                SendToPeer(peerId, PacketType.HandshakeReject, w =>
                    w.Write($"Mod protocol mismatch: Host v{PacketProtocol.ProtocolVersion} vs Client v{protoVer}."));
                return;
            }

            string cleanName = string.IsNullOrWhiteSpace(name) ? $"Player {peerId}" : name.Trim();
            int assignedColor = AllocateColor(prefColor);

            var peer = new PeerInfo
            {
                PlayerId = peerId,
                Name = cleanName,
                ColorIndex = assignedColor,
                PingMs = 0,
                IsReady = false,
                SteamId = steamId
            };
            Roster[peerId] = peer;

            SendToPeer(peerId, PacketType.HandshakeWelcome, w =>
            {
                w.Write(peerId);
                w.WriteSafeString(HostPlayerName);
                w.Write(assignedColor);
            });

            CoopLogger.Info(LogCategory.Net, $"{cleanName} (#{peerId}) joined via {ActiveTransportKind}!", addToFeed: true);
            BroadcastRoster();
            OnPlayerJoined?.Invoke(peer);
            OnRosterUpdated?.Invoke();
        }

        private void HandleHandshakeWelcome(byte[] payload)
        {
            using var ms = new MemoryStream(payload);
            using var r = new BinaryReader(ms, Encoding.UTF8);
            LocalPlayerId = r.ReadInt32();
            HostPlayerName = r.ReadString();
            LocalColorIndex = r.ReadInt32();

            CoopLogger.Info(LogCategory.Net, $"Joined Host '{HostPlayerName}' as Player #{LocalPlayerId}!", addToFeed: true);
            OnJoinedHost?.Invoke();
            OnRosterUpdated?.Invoke();
        }

        private void HandleHandshakeReject(byte[] payload)
        {
            string reason = "Rejected by Host.";
            try
            {
                using var ms = new MemoryStream(payload);
                using var r = new BinaryReader(ms, Encoding.UTF8);
                reason = r.ReadString();
            }
            catch { }
            Disconnect();
            OnConnectionError?.Invoke(reason);
        }

        private void HandleCmdSetReady(int peerId, byte[] payload)
        {
            if (!Roster.TryGetValue(peerId, out var peer)) return;
            using var ms = new MemoryStream(payload);
            using var r = new BinaryReader(ms, Encoding.UTF8);
            peer.IsReady = r.ReadBoolean();
            BroadcastRoster();
            OnRosterUpdated?.Invoke();
        }

        private void HandleCmdSetColor(int peerId, byte[] payload)
        {
            if (!Roster.TryGetValue(peerId, out var peer)) return;
            using var ms = new MemoryStream(payload);
            using var r = new BinaryReader(ms, Encoding.UTF8);
            int requested = Math.Clamp(r.ReadInt32(), 0, CoopConfig.PlayerPalette.Length - 1);
            peer.ColorIndex = requested;
            BroadcastRoster();
            OnRosterUpdated?.Invoke();
        }

        public void SetLocalReady(bool ready)
        {
            if (!IsConnected) return;
            if (IsHost)
            {
                if (Roster.TryGetValue(0, out var me)) me.IsReady = ready;
                BroadcastRoster();
                OnRosterUpdated?.Invoke();
            }
            else
            {
                SendToHost(PacketType.CmdSetReady, w => w.Write(ready));
            }
        }

        public bool ResetAllClientsReady()
        {
            if (!IsHost) return false;
            bool anyReset = false;
            foreach (var kv in Roster)
            {
                if (kv.Key == 0) continue;
                if (kv.Value.IsReady)
                {
                    kv.Value.IsReady = false;
                    anyReset = true;
                }
            }
            if (anyReset)
            {
                BroadcastRoster();
                OnRosterUpdated?.Invoke();
            }
            return anyReset;
        }

        public bool AreAllClientsReady(out int readyClients, out int totalClients)
        {
            readyClients = 0;
            totalClients = 0;
            foreach (var kv in Roster)
            {
                if (kv.Key == 0) continue;
                totalClients++;
                if (kv.Value.IsReady) readyClients++;
            }
            return totalClients == 0 || readyClients == totalClients;
        }

        public void SetLocalColor(int colorIndex)
        {
            LocalColorIndex = Math.Clamp(colorIndex, 0, CoopConfig.PlayerPalette.Length - 1);
            if (CoopConfig.PreferredColorIndex != null) CoopConfig.PreferredColorIndex.Value = LocalColorIndex;
            if (!IsConnected) return;
            if (IsHost)
            {
                if (Roster.TryGetValue(0, out var me)) me.ColorIndex = LocalColorIndex;
                BroadcastRoster();
                OnRosterUpdated?.Invoke();
            }
            else
            {
                SendToHost(PacketType.CmdSetColor, w => w.Write(LocalColorIndex));
            }
        }

        public void SetPeerModal(int playerId, string modalName)
        {
            if (!IsHost) return;
            if (Roster.TryGetValue(playerId, out var peer) && peer.ActiveModal != (modalName ?? ""))
            {
                peer.ActiveModal = modalName ?? "";
                BroadcastRoster();
                OnRosterUpdated?.Invoke();
            }
        }

        public void BroadcastRoster()
        {
            if (!IsHost) return;
            BroadcastToClients(PacketType.RosterSync, w =>
            {
                w.Write(Roster.Count);
                foreach (var kv in Roster)
                {
                    var p = kv.Value;
                    w.Write(p.PlayerId);
                    w.WriteSafeString(p.Name);
                    w.Write(p.ColorIndex);
                    w.Write(p.PingMs);
                    w.Write(p.IsReady);
                    w.Write(p.IsWaveReady);
                    w.WriteSafeString(p.ActiveModal);
                    w.Write(p.SteamId);
                }
            });
        }

        private void HandleRosterSync(byte[] payload)
        {
            using var ms = new MemoryStream(payload);
            using var r = new BinaryReader(ms, Encoding.UTF8);
            int count = r.ReadInt32();
            Roster.Clear();
            for (int i = 0; i < count; i++)
            {
                var p = new PeerInfo
                {
                    PlayerId = r.ReadInt32(),
                    Name = r.ReadString(),
                    ColorIndex = r.ReadInt32(),
                    PingMs = r.ReadInt32(),
                    IsReady = r.ReadBoolean(),
                    IsWaveReady = r.ReadBoolean(),
                    ActiveModal = r.ReadString(),
                    SteamId = r.ReadUInt64()
                };
                if (p.PlayerId == LocalPlayerId) LocalColorIndex = p.ColorIndex;
                if (p.PlayerId == 0) HostPlayerName = p.Name;
                Roster[p.PlayerId] = p;
            }
            OnRosterUpdated?.Invoke();
        }

        private int AllocateColor(int preferred)
        {
            int paletteLen = CoopConfig.PlayerPalette.Length;
            int pref = Math.Clamp(preferred, 0, paletteLen - 1);
            bool used = false;
            foreach (var p in Roster.Values)
                if (p.ColorIndex == pref) { used = true; break; }
            if (!used) return pref;

            for (int i = 0; i < paletteLen; i++)
            {
                bool taken = false;
                foreach (var p in Roster.Values)
                    if (p.ColorIndex == i) { taken = true; break; }
                if (!taken) return i;
            }
            return Roster.Count % paletteLen;
        }

        public void SendToHost(PacketType type, Action<BinaryWriter> write = null, DeliveryMode mode = DeliveryMode.ReliableOrdered)
        {
            if (!IsClient || _active == null) return;
            _active.SendToHost(type, PacketProtocol.SerializePayload(write), mode);
        }

        public void SendToPeer(int peerId, PacketType type, Action<BinaryWriter> write = null, DeliveryMode mode = DeliveryMode.ReliableOrdered)
        {
            if (!IsHost || _active == null) return;
            _active.SendToPeer(peerId, type, PacketProtocol.SerializePayload(write), mode);
        }

        public void BroadcastToClients(PacketType type, Action<BinaryWriter> write = null, DeliveryMode mode = DeliveryMode.ReliableOrdered, int excludePeerId = -1)
        {
            if (!IsHost || _active == null) return;
            _active.BroadcastToPeers(type, PacketProtocol.SerializePayload(write), mode, excludePeerId);
        }

        public void SendToAllPeersOrHost(PacketType type, Action<BinaryWriter> write = null, DeliveryMode mode = DeliveryMode.ReliableOrdered)
        {
            if (IsHost) BroadcastToClients(type, write, mode);
            else if (IsClient) SendToHost(type, write, mode);
        }
    }
}
