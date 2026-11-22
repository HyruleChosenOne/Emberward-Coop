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

    public sealed class PeerInfo
    {
        public int PlayerId;
        public string Name = "Player";
        public int ColorIndex;
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
        public string HostName;
        public string AddressOrCode;
        public int Port;
        public ulong SteamLobbyId;
        public string SceneName;
        public int PlayerCount;
        public int MaxPlayers;
        public DateTime LastSeenUtc;
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
        string StatusText { get; }
        string LobbyCode { get; }
        int LocalPingMs { get; }

        event Action<int, string, ulong> OnPeerConnected;
        event Action<int, string> OnPeerDisconnected;
        event Action OnConnectedToHost;
        event Action<string> OnDisconnected;
        event Action<string> OnError;

        void Poll(Action<IncomingPacket> onPacket);
        void SendToHost(PacketType type, byte[] payload, DeliveryMode mode);
        void SendToPeer(int peerId, PacketType type, byte[] payload, DeliveryMode mode);
        void BroadcastToPeers(PacketType type, byte[] payload, DeliveryMode mode, int excludePeerId = -1);
        void UpdateLobbyMetadata(string sceneName, int playerCount, int maxPlayers);
        void RefreshLobbyList();
        IReadOnlyList<LobbySummary> GetDiscoveredLobbies();
        void Shutdown();
    }
}
