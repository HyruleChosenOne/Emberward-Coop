using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using EmberwardCoop.Core;
using Il2CppInterop.Runtime.InteropTypes.Arrays;
using Steamworks;

namespace EmberwardCoop.Networking
{
    // Polls SteamAPICall_t directly via SteamUtils instead of Steamworks CallResult<T>
    // because generic CallResult marshaling breaks under Il2CppInterop.
    public sealed class SteamP2PTransport : ITransport
    {
        private const int ReliableChannel = 0;
        private const int UnreliableChannel = 1;
        private const string LobbyKeyMod = "ew_coop_mod";
        private const string LobbyValMod = "ew_coop_v1";
        private const string LobbyKeyHost = "ew_host";
        private const string LobbyKeyScene = "ew_scene";
        private const string LobbyKeyMax = "ew_max";

        private const int CallbackId_LobbyCreated = 513;
        private const int CallbackId_LobbyEnter = 504;
        private const int CallbackId_LobbyMatchList = 510;

        public TransportKind Kind => TransportKind.SteamP2P;
        public SessionRole Role => _role;
        public bool IsAvailable => IsSteamReady();
        public string StatusText { get; private set; } = "Steam Ready";
        public string LobbyCode => _currentLobbyId.m_SteamID != 0 ? _currentLobbyId.m_SteamID.ToString() : "";
        public int LocalPingMs { get; private set; }

        public event Action<int, string, ulong> OnPeerConnected;
        public event Action<int, string> OnPeerDisconnected;
        public event Action OnHostLobbyReady;
        public event Action OnConnectedToHost;
        public event Action<string> OnDisconnected;
        public event Action<string> OnError;

        private SessionRole _role = SessionRole.Offline;
        private CSteamID _currentLobbyId;
        private CSteamID _hostSteamId;
        private int _nextPeerId = 1;
        private int _maxPlayers = 4;

        private readonly Dictionary<ulong, int> _steamToPeerId = new Dictionary<ulong, int>();
        private readonly Dictionary<int, ulong> _peerIdToSteam = new Dictionary<int, ulong>();
        private readonly Dictionary<int, string> _peerNames = new Dictionary<int, string>();
        private readonly Dictionary<int, int> _peerPings = new Dictionary<int, int>();
        private readonly Dictionary<ulong, DateTime> _peerLastHeardUtc = new Dictionary<ulong, DateTime>();
        private readonly List<LobbySummary> _discoveredLobbies = new List<LobbySummary>();

        private SteamAPICall_t _pendingCreateLobby;
        private SteamAPICall_t _pendingJoinLobby;
        private SteamAPICall_t _pendingLobbyList;

        private DateTime _lastMemberSyncUtc = DateTime.MinValue;
        private DateTime _lastPingSentUtc = DateTime.MinValue;
        private DateTime _hostLastHeardUtc = DateTime.UtcNow;

        private readonly Il2CppStructArray<byte> _readBuffer = new Il2CppStructArray<byte>(256 * 1024);

        public static bool IsSteamReady()
        {
            try
            {
                if (!SteamManager.Initialized) return false;
                return SteamUser.BLoggedOn();
            }
            catch
            {
                return false;
            }
        }

        public static string GetLocalSteamName()
        {
            try
            {
                if (IsSteamReady())
                {
                    string n = SteamFriends.GetPersonaName();
                    if (!string.IsNullOrWhiteSpace(n)) return n.Trim();
                }
            }
            catch { }
            return CoopConfig.PlayerName?.Value ?? "Player";
        }

        public static ulong GetLocalSteamId()
        {
            try { return IsSteamReady() ? SteamUser.GetSteamID().m_SteamID : 0; }
            catch { return 0; }
        }

        public bool HostLobby(SteamLobbyPrivacy privacy, int maxPlayers)
        {
            if (!IsSteamReady())
            {
                StatusText = "Steam is not initialized or offline.";
                OnError?.Invoke(StatusText);
                return false;
            }

            Shutdown();
            _maxPlayers = Math.Clamp(maxPlayers, 2, 6);
            try
            {
                SteamNetworking.AllowP2PPacketRelay(true);
                ELobbyType lobbyType = privacy switch
                {
                    SteamLobbyPrivacy.Public => ELobbyType.k_ELobbyTypePublic,
                    SteamLobbyPrivacy.Private => ELobbyType.k_ELobbyTypePrivate,
                    _ => ELobbyType.k_ELobbyTypeFriendsOnly
                };
                _pendingCreateLobby = SteamMatchmaking.CreateLobby(lobbyType, _maxPlayers);
                _role = SessionRole.Connecting;
                StatusText = "Creating Steam P2P Lobby...";
                CoopLogger.Info(LogCategory.Net, $"Requesting Steam Lobby ({privacy}, max {_maxPlayers})...", addToFeed: true);
                return true;
            }
            catch (Exception ex)
            {
                StatusText = $"Steam host error: {ex.Message}";
                CoopLogger.Error(LogCategory.Net, "CreateLobby failed", ex);
                OnError?.Invoke(StatusText);
                return false;
            }
        }

        public bool JoinLobbyById(ulong lobbyId)
        {
            if (!IsSteamReady())
            {
                StatusText = "Steam is not initialized or offline.";
                OnError?.Invoke(StatusText);
                return false;
            }
            if (lobbyId == 0)
            {
                StatusText = "Invalid Steam Lobby Code.";
                OnError?.Invoke(StatusText);
                return false;
            }

            Shutdown();
            try
            {
                SteamNetworking.AllowP2PPacketRelay(true);
                _pendingJoinLobby = SteamMatchmaking.JoinLobby(new CSteamID(lobbyId));
                _role = SessionRole.Connecting;
                StatusText = $"Joining Steam Lobby {lobbyId}...";
                CoopLogger.Info(LogCategory.Net, StatusText, addToFeed: true);
                return true;
            }
            catch (Exception ex)
            {
                StatusText = $"Steam join error: {ex.Message}";
                CoopLogger.Error(LogCategory.Net, "JoinLobby failed", ex);
                OnError?.Invoke(StatusText);
                return false;
            }
        }

        public void OpenSteamInviteOverlay()
        {
            if (_currentLobbyId.m_SteamID == 0 || !IsSteamReady()) return;
            try
            {
                SteamFriends.ActivateGameOverlayInviteDialog(_currentLobbyId);
            }
            catch (Exception ex)
            {
                CoopLogger.Warn(LogCategory.Net, $"Could not open Steam invite overlay: {ex.Message}");
            }
        }

        public void RefreshLobbyList()
        {
            if (!IsSteamReady()) return;
            try
            {
                _discoveredLobbies.Clear();
                ScanFriendLobbies();

                SteamMatchmaking.AddRequestLobbyListStringFilter(LobbyKeyMod, LobbyValMod, ELobbyComparison.k_ELobbyComparisonEqual);
                SteamMatchmaking.AddRequestLobbyListDistanceFilter(ELobbyDistanceFilter.k_ELobbyDistanceFilterWorldwide);
                SteamMatchmaking.AddRequestLobbyListResultCountFilter(25);
                _pendingLobbyList = SteamMatchmaking.RequestLobbyList();
            }
            catch (Exception ex)
            {
                CoopLogger.Warn(LogCategory.Net, $"Steam lobby list refresh failed: {ex.Message}");
            }
        }

        private void ScanFriendLobbies()
        {
            try
            {
                int count = SteamFriends.GetFriendCount(EFriendFlags.k_EFriendFlagImmediate);
                for (int i = 0; i < count; i++)
                {
                    CSteamID friend = SteamFriends.GetFriendByIndex(i, EFriendFlags.k_EFriendFlagImmediate);
                    if (SteamFriends.GetFriendGamePlayed(friend, out FriendGameInfo_t info) &&
                        info.m_steamIDLobby.m_SteamID != 0 &&
                        info.m_steamIDLobby.m_SteamID != _currentLobbyId.m_SteamID)
                    {
                        CSteamID lid = info.m_steamIDLobby;
                        SteamMatchmaking.RequestLobbyData(lid);
                        string hostName = SteamMatchmaking.GetLobbyData(lid, LobbyKeyHost);
                        if (string.IsNullOrEmpty(hostName)) hostName = SteamFriends.GetFriendPersonaName(friend);
                        string scene = SteamMatchmaking.GetLobbyData(lid, LobbyKeyScene);
                        if (string.IsNullOrEmpty(scene)) scene = "Friend Lobby";
                        int pCount = Math.Max(1, SteamMatchmaking.GetNumLobbyMembers(lid));
                        int maxP = int.TryParse(SteamMatchmaking.GetLobbyData(lid, LobbyKeyMax), out int m) ? m : 4;

                        AddOrUpdateLobby(new LobbySummary
                        {
                            Transport = TransportKind.SteamP2P,
                            LobbyKey = lid.m_SteamID.ToString(),
                            HostName = hostName,
                            AddressOrCode = lid.m_SteamID.ToString(),
                            SteamLobbyId = lid.m_SteamID,
                            SceneName = scene,
                            PlayerCount = pCount,
                            MaxPlayers = maxP,
                            LastSeenUtc = DateTime.UtcNow
                        });
                    }
                }
            }
            catch { }
        }

        private void AddOrUpdateLobby(LobbySummary summary)
        {
            for (int i = 0; i < _discoveredLobbies.Count; i++)
            {
                if (_discoveredLobbies[i].SteamLobbyId == summary.SteamLobbyId)
                {
                    _discoveredLobbies[i] = summary;
                    return;
                }
            }
            _discoveredLobbies.Add(summary);
        }

        public IReadOnlyList<LobbySummary> GetDiscoveredLobbies() => _discoveredLobbies;

        public void UpdateLobbyMetadata(string sceneName, int playerCount, int maxPlayers)
        {
            if (_role != SessionRole.Host || _currentLobbyId.m_SteamID == 0 || !IsSteamReady()) return;
            try
            {
                SteamMatchmaking.SetLobbyData(_currentLobbyId, LobbyKeyScene, string.IsNullOrEmpty(sceneName) ? "CoinPage" : sceneName);
                SteamMatchmaking.SetLobbyData(_currentLobbyId, LobbyKeyMax, maxPlayers.ToString());
            }
            catch { }
        }

        public void Poll(Action<IncomingPacket> onPacket)
        {
            if (!IsSteamReady()) return;

            PollPendingApiCalls();

            if (_role == SessionRole.Host || _role == SessionRole.Client)
            {
                DateTime utcNow = DateTime.UtcNow;

                if ((utcNow - _lastMemberSyncUtc).TotalSeconds >= 0.5)
                {
                    _lastMemberSyncUtc = utcNow;
                    SyncLobbyMembers(utcNow);
                }

                DrainChannel(ReliableChannel, onPacket);
                DrainChannel(UnreliableChannel, onPacket);

                if ((utcNow - _lastPingSentUtc).TotalSeconds >= 2.0)
                {
                    _lastPingSentUtc = utcNow;
                    TickHeartbeat(utcNow);
                }
            }
        }

        private void PollPendingApiCalls()
        {
            if (_pendingCreateLobby.m_SteamAPICall != 0 && TryCompleteApiCall<LobbyCreated_t>(_pendingCreateLobby, CallbackId_LobbyCreated, out var created, out bool failed))
            {
                _pendingCreateLobby.m_SteamAPICall = 0;
                if (failed || created.m_eResult != EResult.k_EResultOK || created.m_ulSteamIDLobby == 0)
                {
                    _role = SessionRole.Offline;
                    StatusText = $"Failed to create Steam lobby ({created.m_eResult}).";
                    OnError?.Invoke(StatusText);
                }
                else
                {
                    _currentLobbyId = new CSteamID(created.m_ulSteamIDLobby);
                    _hostSteamId = SteamUser.GetSteamID();
                    _role = SessionRole.Host;
                    string hostName = GetLocalSteamName();

                    SteamMatchmaking.SetLobbyData(_currentLobbyId, LobbyKeyMod, LobbyValMod);
                    SteamMatchmaking.SetLobbyData(_currentLobbyId, LobbyKeyHost, hostName);
                    SteamMatchmaking.SetLobbyData(_currentLobbyId, LobbyKeyScene, GameRefs.ActiveScene);
                    SteamMatchmaking.SetLobbyData(_currentLobbyId, LobbyKeyMax, _maxPlayers.ToString());
                    try { SteamFriends.SetRichPresence("connect", $"+connect_lobby {_currentLobbyId.m_SteamID}"); } catch { }

                    StatusText = $"Steam Lobby Hosted ({_currentLobbyId.m_SteamID})";
                    CoopLogger.Info(LogCategory.Net, $"Steam P2P Lobby ready! Code: {_currentLobbyId.m_SteamID}", addToFeed: true);
                    OnHostLobbyReady?.Invoke();
                }
            }

            if (_pendingJoinLobby.m_SteamAPICall != 0 && TryCompleteApiCall<LobbyEnter_t>(_pendingJoinLobby, CallbackId_LobbyEnter, out var entered, out bool joinFailed))
            {
                _pendingJoinLobby.m_SteamAPICall = 0;
                if (joinFailed || entered.m_EChatRoomEnterResponse != (uint)EChatRoomEnterResponse.k_EChatRoomEnterResponseSuccess || entered.m_ulSteamIDLobby == 0)
                {
                    _role = SessionRole.Offline;
                    StatusText = $"Failed to join Steam lobby (code {entered.m_EChatRoomEnterResponse}).";
                    OnError?.Invoke(StatusText);
                }
                else
                {
                    _currentLobbyId = new CSteamID(entered.m_ulSteamIDLobby);
                    _hostSteamId = SteamMatchmaking.GetLobbyOwner(_currentLobbyId);
                    if (_hostSteamId.m_SteamID == 0 || _hostSteamId.m_SteamID == GetLocalSteamId())
                    {
                        Shutdown();
                        StatusText = "Cannot join your own lobby as a client.";
                        OnError?.Invoke(StatusText);
                        return;
                    }

                    SteamNetworking.AcceptP2PSessionWithUser(_hostSteamId);
                    _hostLastHeardUtc = DateTime.UtcNow;
                    _role = SessionRole.Client;
                    StatusText = $"Connected to Steam Lobby {_currentLobbyId.m_SteamID}";
                    OnConnectedToHost?.Invoke();
                }
            }

            if (_pendingLobbyList.m_SteamAPICall != 0 && TryCompleteApiCall<LobbyMatchList_t>(_pendingLobbyList, CallbackId_LobbyMatchList, out var listRes, out bool listFailed))
            {
                _pendingLobbyList.m_SteamAPICall = 0;
                if (!listFailed)
                {
                    for (int i = 0; i < (int)listRes.m_nLobbiesMatching; i++)
                    {
                        CSteamID lid = SteamMatchmaking.GetLobbyByIndex(i);
                        if (lid.m_SteamID == 0 || lid.m_SteamID == _currentLobbyId.m_SteamID) continue;

                        string hostName = SteamMatchmaking.GetLobbyData(lid, LobbyKeyHost);
                        if (string.IsNullOrEmpty(hostName)) hostName = "Emberward Host";
                        string scene = SteamMatchmaking.GetLobbyData(lid, LobbyKeyScene);
                        if (string.IsNullOrEmpty(scene)) scene = "CoinPage";
                        int pCount = Math.Max(1, SteamMatchmaking.GetNumLobbyMembers(lid));
                        int maxP = int.TryParse(SteamMatchmaking.GetLobbyData(lid, LobbyKeyMax), out int m) ? m : 4;

                        AddOrUpdateLobby(new LobbySummary
                        {
                            Transport = TransportKind.SteamP2P,
                            LobbyKey = lid.m_SteamID.ToString(),
                            HostName = hostName,
                            AddressOrCode = lid.m_SteamID.ToString(),
                            SteamLobbyId = lid.m_SteamID,
                            SceneName = scene,
                            PlayerCount = pCount,
                            MaxPlayers = maxP,
                            LastSeenUtc = DateTime.UtcNow
                        });
                    }
                }
            }
        }

        private static bool TryCompleteApiCall<T>(SteamAPICall_t handle, int expectedCallbackId, out T result, out bool failed) where T : struct
        {
            result = default;
            failed = false;
            try
            {
                if (!SteamUtils.IsAPICallCompleted(handle, out bool ioFail))
                    return false;

                int size = Marshal.SizeOf<T>();
                IntPtr ptr = Marshal.AllocHGlobal(size);
                try
                {
                    bool ok = SteamUtils.GetAPICallResult(handle, ptr, size, expectedCallbackId, out bool callFailed);
                    failed = ioFail || callFailed || !ok;
                    if (!failed)
                        result = Marshal.PtrToStructure<T>(ptr);
                }
                finally
                {
                    Marshal.FreeHGlobal(ptr);
                }
                return true;
            }
            catch (Exception ex)
            {
                CoopLogger.Error(LogCategory.Net, $"Steam APICall<{typeof(T).Name}> poll error", ex);
                failed = true;
                return true;
            }
        }

        private void SyncLobbyMembers(DateTime utcNow)
        {
            if (_currentLobbyId.m_SteamID == 0) return;

            int count = SteamMatchmaking.GetNumLobbyMembers(_currentLobbyId);
            if (_role == SessionRole.Client)
            {
                CSteamID owner = SteamMatchmaking.GetLobbyOwner(_currentLobbyId);
                if (count <= 0 || owner.m_SteamID == 0 || owner.m_SteamID != _hostSteamId.m_SteamID)
                {
                    Shutdown();
                    OnDisconnected?.Invoke("Host closed the Steam lobby.");
                    return;
                }
                SteamNetworking.AcceptP2PSessionWithUser(_hostSteamId);
                return;
            }

            ulong myId = GetLocalSteamId();
            var present = new HashSet<ulong>();
            for (int i = 0; i < count; i++)
            {
                CSteamID member = SteamMatchmaking.GetLobbyMemberByIndex(_currentLobbyId, i);
                if (member.m_SteamID == 0 || member.m_SteamID == myId) continue;
                present.Add(member.m_SteamID);

                if (!_steamToPeerId.ContainsKey(member.m_SteamID))
                {
                    int pid = _nextPeerId++;
                    string name = SteamFriends.GetFriendPersonaName(member);
                    if (string.IsNullOrWhiteSpace(name)) name = $"SteamPlayer {pid}";
                    _steamToPeerId[member.m_SteamID] = pid;
                    _peerIdToSteam[pid] = member.m_SteamID;
                    _peerNames[pid] = name;
                    _peerLastHeardUtc[member.m_SteamID] = utcNow;
                    SteamNetworking.AcceptP2PSessionWithUser(member);
                    OnPeerConnected?.Invoke(pid, name, member.m_SteamID);
                }
            }

            var departed = new List<ulong>();
            foreach (var kv in _steamToPeerId)
            {
                if (!present.Contains(kv.Key)) departed.Add(kv.Key);
            }
            for (int i = 0; i < departed.Count; i++)
            {
                ulong sid = departed[i];
                int pid = _steamToPeerId[sid];
                string name = _peerNames.TryGetValue(pid, out var n) ? n : $"Player {pid}";
                _steamToPeerId.Remove(sid);
                _peerIdToSteam.Remove(pid);
                _peerNames.Remove(pid);
                _peerPings.Remove(pid);
                _peerLastHeardUtc.Remove(sid);
                try { SteamNetworking.CloseP2PSessionWithUser(new CSteamID(sid)); } catch { }
                OnPeerDisconnected?.Invoke(pid, name);
            }
        }

        private void DrainChannel(int channel, Action<IncomingPacket> onPacket)
        {
            int guard = 64;
            while (guard-- > 0 && SteamNetworking.IsP2PPacketAvailable(out uint msgSize, channel))
            {
                if (msgSize == 0 || msgSize > _readBuffer.Length)
                {
                    SteamNetworking.ReadP2PPacket(_readBuffer, (uint)_readBuffer.Length, out _, out _, channel);
                    continue;
                }

                if (!SteamNetworking.ReadP2PPacket(_readBuffer, msgSize, out uint bytesRead, out CSteamID remoteId, channel))
                    break;

                if (bytesRead < PacketProtocol.HeaderSize) continue;

                byte[] managed = new byte[bytesRead];
                for (int i = 0; i < (int)bytesRead; i++) managed[i] = _readBuffer[i];

                if (!PacketProtocol.TryParseHeader(managed, 0, out var type, out var delivery, out int len, out byte version))
                    continue;
                if (bytesRead < PacketProtocol.HeaderSize + len) continue;

                if (version != PacketProtocol.ProtocolVersion)
                {
                    if (_role == SessionRole.Host)
                    {
                        byte[] rej = PacketProtocol.Frame(PacketType.HandshakeReject, DeliveryMode.ReliableOrdered,
                            PacketProtocol.SerializePayload(w => w.Write($"Protocol mismatch (Host v{PacketProtocol.ProtocolVersion}, Client v{version}).")));
                        SendRawSteam(remoteId, rej, DeliveryMode.ReliableOrdered);
                    }
                    continue;
                }

                byte[] payload = len > 0 ? new byte[len] : Array.Empty<byte>();
                if (len > 0) Buffer.BlockCopy(managed, PacketProtocol.HeaderSize, payload, 0, len);

                if (_role == SessionRole.Host)
                {
                    if (!_steamToPeerId.TryGetValue(remoteId.m_SteamID, out int pid))
                    {
                        pid = _nextPeerId++;
                        string name = SteamFriends.GetFriendPersonaName(remoteId);
                        if (string.IsNullOrWhiteSpace(name)) name = $"SteamPlayer {pid}";
                        _steamToPeerId[remoteId.m_SteamID] = pid;
                        _peerIdToSteam[pid] = remoteId.m_SteamID;
                        _peerNames[pid] = name;
                        SteamNetworking.AcceptP2PSessionWithUser(remoteId);
                        OnPeerConnected?.Invoke(pid, name, remoteId.m_SteamID);
                    }
                    _peerLastHeardUtc[remoteId.m_SteamID] = DateTime.UtcNow;

                    if (type == PacketType.Ping && len >= 8)
                    {
                        long sentTicks = BitConverter.ToInt64(payload, 0);
                        int reportedRtt = len >= 12 ? BitConverter.ToInt32(payload, 8) : 0;
                        _peerPings[pid] = reportedRtt;
                        SendRawSteam(remoteId, PacketProtocol.Frame(PacketType.Pong, DeliveryMode.ReliableOrdered,
                            PacketProtocol.SerializePayload(w => w.Write(sentTicks))), DeliveryMode.ReliableOrdered);
                        continue;
                    }
                    onPacket?.Invoke(new IncomingPacket(pid, type, delivery, payload));
                }
                else if (_role == SessionRole.Client && remoteId.m_SteamID == _hostSteamId.m_SteamID)
                {
                    _hostLastHeardUtc = DateTime.UtcNow;
                    if (type == PacketType.Pong && len >= 8)
                    {
                        long sentTicks = BitConverter.ToInt64(payload, 0);
                        double ms = TimeSpan.FromTicks(DateTime.UtcNow.Ticks - sentTicks).TotalMilliseconds;
                        if (ms >= 0 && ms < 9999) LocalPingMs = (int)Math.Round(ms);
                        continue;
                    }
                    onPacket?.Invoke(new IncomingPacket(0, type, delivery, payload));
                }
            }
        }

        private void TickHeartbeat(DateTime utcNow)
        {
            if (_role == SessionRole.Client)
            {
                SendToHost(PacketType.Ping, PacketProtocol.SerializePayload(w =>
                {
                    w.Write(utcNow.Ticks);
                    w.Write(LocalPingMs);
                }), DeliveryMode.ReliableOrdered);

                if ((utcNow - _hostLastHeardUtc).TotalSeconds > 18.0)
                {
                    Shutdown();
                    OnDisconnected?.Invoke("Connection to Steam Host timed out.");
                }
            }
        }

        public void SendToHost(PacketType type, byte[] payload, DeliveryMode mode)
        {
            if (_role != SessionRole.Client || _hostSteamId.m_SteamID == 0) return;
            SendRawSteam(_hostSteamId, PacketProtocol.Frame(type, mode, payload), mode);
        }

        public void SendToPeer(int peerId, PacketType type, byte[] payload, DeliveryMode mode)
        {
            if (_role != SessionRole.Host) return;
            if (!_peerIdToSteam.TryGetValue(peerId, out ulong sid)) return;
            SendRawSteam(new CSteamID(sid), PacketProtocol.Frame(type, mode, payload), mode);
        }

        public void BroadcastToPeers(PacketType type, byte[] payload, DeliveryMode mode, int excludePeerId = -1)
        {
            if (_role != SessionRole.Host || _peerIdToSteam.Count == 0) return;
            byte[] framed = PacketProtocol.Frame(type, mode, payload);
            foreach (var kv in _peerIdToSteam)
            {
                if (kv.Key == excludePeerId) continue;
                SendRawSteam(new CSteamID(kv.Value), framed, mode);
            }
        }

        private static void SendRawSteam(CSteamID target, byte[] data, DeliveryMode mode)
        {
            try
            {
                var arr = new Il2CppStructArray<byte>(data.Length);
                for (int i = 0; i < data.Length; i++) arr[i] = data[i];
                EP2PSend sendType = mode == DeliveryMode.Unreliable
                    ? EP2PSend.k_EP2PSendUnreliableNoDelay
                    : EP2PSend.k_EP2PSendReliable;
                int channel = mode == DeliveryMode.Unreliable ? UnreliableChannel : ReliableChannel;
                SteamNetworking.SendP2PPacket(target, arr, (uint)data.Length, sendType, channel);
            }
            catch (Exception ex)
            {
                CoopLogger.Debug(LogCategory.Net, $"Steam SendP2PPacket error: {ex.Message}");
            }
        }

        public int GetPeerPingMs(int peerId) => _peerPings.TryGetValue(peerId, out int p) ? p : 0;

        public void Shutdown()
        {
            _pendingCreateLobby.m_SteamAPICall = 0;
            _pendingJoinLobby.m_SteamAPICall = 0;
            _pendingLobbyList.m_SteamAPICall = 0;

            if (IsSteamReady())
            {
                try
                {
                    foreach (var sid in _peerIdToSteam.Values)
                        SteamNetworking.CloseP2PSessionWithUser(new CSteamID(sid));
                    if (_hostSteamId.m_SteamID != 0)
                        SteamNetworking.CloseP2PSessionWithUser(_hostSteamId);
                    if (_currentLobbyId.m_SteamID != 0)
                        SteamMatchmaking.LeaveLobby(_currentLobbyId);
                    SteamFriends.ClearRichPresence();
                }
                catch { }
            }

            _steamToPeerId.Clear();
            _peerIdToSteam.Clear();
            _peerNames.Clear();
            _peerPings.Clear();
            _peerLastHeardUtc.Clear();
            _currentLobbyId = default;
            _hostSteamId = default;
            _role = SessionRole.Offline;
            LocalPingMs = 0;
            StatusText = IsSteamReady() ? "Steam Ready" : "Steam Offline";
        }
    }
}
