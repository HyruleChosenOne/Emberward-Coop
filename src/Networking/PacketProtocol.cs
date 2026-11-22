using System;
using System.IO;
using System.Text;
using UnityEngine;

namespace EmberwardCoop.Networking
{
    public enum PacketType : ushort
    {
        HandshakeHello = 1,
        HandshakeWelcome = 2,
        HandshakeReject = 3,
        Ping = 4,
        Pong = 5,
        RosterSync = 6,
        CmdSetReady = 7,
        CmdSetColor = 8,
        ChatMessage = 9,
        CmdSetAvatar = 10,
        CmdSetLoadout = 11,
        CmdSyncUnlockSummary = 12,

        RunSnapshot = 20,
        RequestFullSync = 21,
        EconomyAndHandSnapshot = 22,
        BoardSnapshot = 23,
        EvtStateHash = 24,
        CmdDesyncReport = 25,
        EvtPeerInventoryRestore = 26,

        CmdPlaceTetris = 40,
        EvtPlaceTetris = 41,
        CmdRecallTetris = 42,
        EvtRecallTetris = 43,
        CmdActivateRuneStone = 44,
        EvtActivateRuneStone = 45,

        CmdPlaceTower = 60,
        EvtPlaceTower = 61,
        CmdUpgradeTower = 62,
        EvtUpgradeTower = 63,
        CmdSellTower = 64,
        EvtSellTower = 65,
        CmdTowerPriority = 66,
        EvtTowerPriority = 67,
        CmdTowerSpecialAction = 68,
        EvtTowerSpecialAction = 69,

        CmdDrawCard = 80,
        CmdMulligan = 81,
        CmdUseBuff = 82,
        EvtUseBuff = 83,
        CmdOpenChest = 84,
        EvtOpenChest = 85,
        CmdFocusMonster = 86,
        EvtFocusMonster = 87,

        CmdToggleWaveReady = 100,
        CmdStartWave = 101,
        EvtStartWave = 102,
        EvtWaveEnd = 103,
        CmdSetGameSpeed = 104,
        EvtSetGameSpeed = 105,
        EvtPauseState = 106,

        CmdVoteMapNode = 120,
        EvtMapNodeVotes = 121,
        CmdSelectMapNode = 122,
        EvtModalState = 123,
        EvtMapNodeResolved = 124,
        EvtModalClosed = 125,
        EvtVictoryOrDefeat = 126,
        EvtOpenPeerNodeModal = 127,
        EvtSharedDraftState = 128,
        CmdDraftVote = 129,
        CmdDraftReroll = 130,
        EvtSharedDraftClosed = 131,
        CmdDraftBarrierState = 132,
        EvtDraftBarrierState = 133,

        EvtQuestSnapshot = 150,
        EvtQuestProgress = 151,
        CmdClientQuestTrigger = 152,
        EvtMetaRewardGrant = 153,
        EvtAchievementTrigger = 154,

        CursorAndGhostState = 170,
        MapPing = 171,

        SessionSettingsSync = 190,
        CmdGiftGold = 191,
        EvtGiftGold = 192,
        PlayerPeekUpdate = 193,
        PlayerStatsSync = 194,
        EvtTurnBlockCount = 195,
        LobbySetupSync = 197,
        EvtHostClosedLobby = 198,
        EvtStartCoopRun = 199,

        StickerEmote = 216,
        QuickCallout = 217,
        MazeSketchTrail = 218,
        CmdUndoBuild = 219,
        EvtUndoBuild = 220
    }

    // Wire format (10-byte header): [2B magic 'EW'][1B version][2B type][1B delivery][4B length][payload]
    public static class PacketProtocol
    {
        public const ushort Magic = 0x5745;
        public const byte ProtocolVersion = 1;
        public const int HeaderSize = 10;
        public const int MaxPayloadBytes = 4 * 1024 * 1024;

        public static byte[] SerializePayload(Action<BinaryWriter> writeAction)
        {
            if (writeAction == null) return Array.Empty<byte>();
            using var ms = new MemoryStream(128);
            using var bw = new BinaryWriter(ms, Encoding.UTF8, leaveOpen: true);
            writeAction(bw);
            bw.Flush();
            return ms.ToArray();
        }

        public static byte[] Frame(PacketType type, DeliveryMode mode, byte[] payload)
        {
            int len = payload?.Length ?? 0;
            byte[] framed = new byte[HeaderSize + len];
            framed[0] = (byte)(Magic & 0xFF);
            framed[1] = (byte)((Magic >> 8) & 0xFF);
            framed[2] = ProtocolVersion;
            ushort pt = (ushort)type;
            framed[3] = (byte)(pt & 0xFF);
            framed[4] = (byte)((pt >> 8) & 0xFF);
            framed[5] = (byte)mode;
            framed[6] = (byte)(len & 0xFF);
            framed[7] = (byte)((len >> 8) & 0xFF);
            framed[8] = (byte)((len >> 16) & 0xFF);
            framed[9] = (byte)((len >> 24) & 0xFF);
            if (len > 0)
                Buffer.BlockCopy(payload, 0, framed, HeaderSize, len);
            return framed;
        }

        public static bool TryParseHeader(byte[] header, int offset, out PacketType type, out DeliveryMode mode, out int payloadLen, out byte version)
        {
            type = 0;
            mode = DeliveryMode.ReliableOrdered;
            payloadLen = 0;
            version = 0;
            if (header == null || header.Length - offset < HeaderSize) return false;

            ushort magic = (ushort)(header[offset] | (header[offset + 1] << 8));
            if (magic != Magic) return false;

            version = header[offset + 2];
            type = (PacketType)(header[offset + 3] | (header[offset + 4] << 8));
            mode = (DeliveryMode)header[offset + 5];
            payloadLen = header[offset + 6]
                       | (header[offset + 7] << 8)
                       | (header[offset + 8] << 16)
                       | (header[offset + 9] << 24);
            return payloadLen >= 0 && payloadLen <= MaxPayloadBytes;
        }

        public static void WriteVector3(this BinaryWriter w, Vector3 v)
        {
            w.Write(v.x);
            w.Write(v.y);
            w.Write(v.z);
        }

        public static Vector3 ReadVector3(this BinaryReader r)
            => new Vector3(r.ReadSingle(), r.ReadSingle(), r.ReadSingle());

        public static void WriteSafeString(this BinaryWriter w, string s)
            => w.Write(s ?? "");
    }
}
