using System;
using System.IO;
using EmberwardCoop.Networking;

namespace EmberwardCoop.Rules
{
    [Flags]
    public enum LobbySetupChangeMask : byte
    {
        None = 0,
        CoopMode = 1 << 0,
        World = 1 << 1,
        Difficulty = 1 << 2,
        SessionRules = 1 << 3,
        LaunchState = 1 << 4,
        Hero = 1 << 5,
        SaveMode = 1 << 6,
        Unlocks = 1 << 7,
        MajorSetup = CoopMode | World | Difficulty | Hero | SaveMode
    }

    public sealed class LobbySetupState
    {
        public uint Revision = 1;
        public eWorldType SelectedWorld = eWorldType.WORLD_1_FOREST;
        public eGameDifficultyType DifficultyTier = eGameDifficultyType.NORMAL;
        public eCharacterType HostHero = eCharacterType.BASIC;
        public int MasterSeed = 0; // 0 = auto-generate at run start
        public bool IsLaunchingRun = false;

        public bool IsLoadRunMode = false;
        public string SelectedSaveId = "";
        public string SelectedSaveSummary = "";
        public PlayerLoadoutChoice HostSharedLoadout = new PlayerLoadoutChoice();

        public PlayerUnlockSummary SyncedPartyUnlocks = new PlayerUnlockSummary();
        public int SyncedMostProgressedPlayerId = 0;
        public string SyncedMostProgressedPlayerName = "";

        public readonly SessionSettings Settings;

        public LobbySetupState(SessionSettings settings)
        {
            Settings = settings ?? new SessionSettings();
        }

        public void Write(BinaryWriter w, LobbySetupChangeMask changeMask)
        {
            w.Write(Revision);
            w.Write((byte)changeMask);
            w.Write((int)SelectedWorld);
            w.Write((int)DifficultyTier);
            w.Write((int)HostHero);
            w.Write(MasterSeed);
            w.Write(IsLaunchingRun);
            w.Write(IsLoadRunMode);
            w.WriteSafeString(SelectedSaveId);
            w.WriteSafeString(SelectedSaveSummary);
            (HostSharedLoadout ?? new PlayerLoadoutChoice()).Write(w);
            (SyncedPartyUnlocks ?? new PlayerUnlockSummary()).Write(w);
            w.Write(SyncedMostProgressedPlayerId);
            w.WriteSafeString(SyncedMostProgressedPlayerName);
            Settings.Write(w);
        }

        public LobbySetupChangeMask ReadFrom(BinaryReader r)
        {
            uint rev = r.ReadUInt32();
            var reportedMask = (LobbySetupChangeMask)r.ReadByte();
            var nextWorld = (eWorldType)r.ReadInt32();
            var nextDiff = (eGameDifficultyType)r.ReadInt32();
            var nextHero = (eCharacterType)r.ReadInt32();
            int nextSeed = r.ReadInt32();
            bool nextLaunch = r.ReadBoolean();
            bool nextLoadMode = r.ReadBoolean();
            string nextSaveId = r.ReadString();
            string nextSaveSummary = r.ReadString();
            var nextSharedLoadout = PlayerLoadoutChoice.Read(r);
            var nextPartyUnlocks = PlayerUnlockSummary.Read(r);
            int nextBestPid = r.ReadInt32();
            string nextBestName = r.ReadString();

            var prevMode = Settings.EconomyMode;
            var prevWorld = SelectedWorld;
            var prevDiff = DifficultyTier;
            var prevHero = HostHero;
            bool prevLaunch = IsLaunchingRun;
            bool prevLoadMode = IsLoadRunMode;
            string prevSaveId = SelectedSaveId;

            Settings.ReadFrom(r);
            Revision = rev;
            SelectedWorld = nextWorld;
            DifficultyTier = nextDiff;
            HostHero = nextHero == eCharacterType.NONE ? eCharacterType.BASIC : nextHero;
            MasterSeed = nextSeed;
            IsLaunchingRun = nextLaunch;
            IsLoadRunMode = nextLoadMode;
            SelectedSaveId = nextSaveId ?? "";
            SelectedSaveSummary = nextSaveSummary ?? "";
            HostSharedLoadout = nextSharedLoadout ?? new PlayerLoadoutChoice();
            SyncedPartyUnlocks = nextPartyUnlocks ?? new PlayerUnlockSummary();
            SyncedMostProgressedPlayerId = nextBestPid;
            SyncedMostProgressedPlayerName = nextBestName ?? "";

            LobbySetupChangeMask diff = reportedMask;
            if (prevMode != Settings.EconomyMode) diff |= LobbySetupChangeMask.CoopMode;
            if (prevWorld != SelectedWorld) diff |= LobbySetupChangeMask.World;
            if (prevDiff != DifficultyTier) diff |= LobbySetupChangeMask.Difficulty;
            if (prevHero != HostHero) diff |= LobbySetupChangeMask.Hero;
            if (prevLaunch != IsLaunchingRun) diff |= LobbySetupChangeMask.LaunchState;
            if (prevLoadMode != IsLoadRunMode || prevSaveId != SelectedSaveId) diff |= LobbySetupChangeMask.SaveMode;
            return diff;
        }
    }
}
