using System;
using System.Collections.Generic;

namespace EmberwardCoop.Networking
{
    public enum TransportKind : byte
    {
        None = 0,
        Lan = 1,
        SteamP2P = 2
    }

    public enum SessionRole : byte
    {
        Offline = 0,
        Connecting = 1,
        Host = 2,
        Client = 3
    }

    public enum DeliveryMode : byte
    {
        ReliableOrdered = 0,
        Unreliable = 1
    }

    public enum SteamLobbyPrivacy : byte
    {
        FriendsOnly = 0,
        Public = 1,
        Private = 2
    }

    public static class LobbyMetadataKeys
    {
        public const string GameTagKey = "game_tag";
        public const string GameTagValue = "Emberward";

        public const string ModSignatureKey = "mod_signature";
        public const string ModSignatureValue = "EmberwardCoop_v1";

        public const string ProtocolVersionKey = "protocol_version";
        public const string ModVersionKey = "mod_version";
        public const string HostNameKey = "host_name";
        public const string CoopModeKey = "coop_mode";
        public const string PlayerCountKey = "player_count";
        public const string MaxPlayersKey = "max_players";
        public const string SceneNameKey = "scene_name";
        public const string InProgressKey = "in_progress";
        public const string JoinableInProgressKey = "joinable_in_progress";

        public const string LanDiscoveryMagic = "EMBERWARD_COOP_DISCOVERY";

        public const string ModeShared = "Shared";
        public const string ModeSeparate = "Separate";

        public static bool IsValidCoopMode(string mode) =>
            string.Equals(mode, ModeShared, StringComparison.Ordinal) ||
            string.Equals(mode, ModeSeparate, StringComparison.Ordinal);
    }

    public sealed class PeerInfo
    {
        public int PlayerId;
        public string Name = "Player";
        public int ColorIndex;
        public string AvatarHeroId = "BASIC";
        public int PingMs;
        public bool IsReady;
        public bool IsWaveReady;
        public string ActiveModal = "";
        public ulong SteamId;
        public bool IsHost => PlayerId == 0;
    }

    public sealed class LobbySummary
    {
        public TransportKind Transport;
        public string LobbyKey;
        public string GameTag;
        public string ModSignature;
        public int ProtocolVersion;
        public string ModVersion;
        public string HostName;
        public string CoopMode;
        public string AddressOrCode;
        public int Port;
        public ulong SteamLobbyId;
        public string SceneName;
        public int PlayerCount;
        public int MaxPlayers;
        public bool InProgress;
        public bool JoinableInProgress;
        public int PingMs = -1;
        public DateTime LastSeenUtc;

        public bool IsVersionMismatch => ProtocolVersion != PacketProtocol.ProtocolVersion;
        public bool IsJoinable => !IsVersionMismatch && PlayerCount >= 1 && PlayerCount < MaxPlayers && (!InProgress || JoinableInProgress);
    }

    public readonly struct IncomingPacket
    {
        public readonly int SenderPlayerId;
        public readonly PacketType Type;
        public readonly DeliveryMode Delivery;
        public readonly byte[] Payload;

        public IncomingPacket(int senderPlayerId, PacketType type, DeliveryMode delivery, byte[] payload)
        {
            SenderPlayerId = senderPlayerId;
            Type = type;
            Delivery = delivery;
            Payload = payload;
        }
    }

    public interface ITransport
    {
        TransportKind Kind { get; }
        SessionRole Role { get; }
        bool IsAvailable { get; }
        bool IsRefreshingLobbies { get; }
        string StatusText { get; }
        string LobbyCode { get; }
        int LocalPingMs { get; }

        event Action<int, string, ulong> OnPeerConnected;
        event Action<int, string> OnPeerDisconnected;
        event Action OnConnectedToHost;
        event Action<string> OnDisconnected;
        event Action<string> OnError;
        event Action OnLobbyListUpdated;

        void Poll(Action<IncomingPacket> onPacket);
        void SendToHost(PacketType type, byte[] payload, DeliveryMode mode);
        void SendToPeer(int peerId, PacketType type, byte[] payload, DeliveryMode mode);
        void BroadcastToPeers(PacketType type, byte[] payload, DeliveryMode mode, int excludePeerId = -1);
        void UpdateLobbyMetadata(string sceneName, int playerCount, int maxPlayers, string coopMode, bool inProgress, bool joinableInProgress = false);
        void RefreshLobbyList();
        IReadOnlyList<LobbySummary> GetDiscoveredLobbies();
        void Shutdown();
    }
}
