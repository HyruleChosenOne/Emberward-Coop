using System;
using System.IO;
using EmberwardCoop.Networking;

namespace EmberwardCoop.Rules
{
    public enum CoopRunMode : byte
    {
        Campaign = 0,
        Endless = 1,
        EnigmaSanctum = 2
    }

    [Flags]
    public enum LobbySetupChangeMask : byte
    {
        None = 0,
        CoopMode = 1 << 0,
        World = 1 << 1,
        Difficulty = 1 << 2,
        SessionRules = 1 << 3,
        LaunchState = 1 << 4,
        GameMode = 1 << 5,
        SaveMode = 1 << 6,
        Unlocks = 1 << 7,
        MajorSetup = CoopMode | World | Difficulty | GameMode | SaveMode
    }

    public sealed class LobbySetupState
    {
        public uint Revision = 1;
        public CoopRunMode SelectedRunMode = CoopRunMode.Campaign;
        public eWorldType SelectedWorld = eWorldType.WORLD_1_FOREST;
        public eGameDifficultyType DifficultyTier = eGameDifficultyType.NORMAL;
        public int SelectedSubStageIndex = 0;
        public string SelectedEndlessSceneName = "EnvScene_018";
        public eEndlessModeType SelectedEndlessType = eEndlessModeType.BASIC_ENDLESS;
        public int MasterSeed = 0; // 0 = auto-generate at run start
        public bool IsLaunchingRun = false;

        public bool IsLoadRunMode = false;
        public string SelectedSaveId = "";
        public string SelectedSaveSummary = "";

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
            w.Write((byte)SelectedRunMode);
            w.Write((int)SelectedWorld);
            w.Write((int)DifficultyTier);
            w.Write(SelectedSubStageIndex);
            w.WriteSafeString(SelectedEndlessSceneName);
            w.Write((int)SelectedEndlessType);
            w.Write(MasterSeed);
            w.Write(IsLaunchingRun);
            w.Write(IsLoadRunMode);
            w.WriteSafeString(SelectedSaveId);
            w.WriteSafeString(SelectedSaveSummary);
            (SyncedPartyUnlocks ?? new PlayerUnlockSummary()).Write(w);
            w.Write(SyncedMostProgressedPlayerId);
            w.WriteSafeString(SyncedMostProgressedPlayerName);
            Settings.Write(w);
        }

        public LobbySetupChangeMask ReadFrom(BinaryReader r)
        {
            uint rev = r.ReadUInt32();
            var reportedMask = (LobbySetupChangeMask)r.ReadByte();
            var nextRunMode = (CoopRunMode)r.ReadByte();
            var nextWorld = (eWorldType)r.ReadInt32();
            var nextDiff = (eGameDifficultyType)r.ReadInt32();
            int nextSubIdx = r.ReadInt32();
            string nextEndlessScene = r.ReadString();
            var nextEndlessType = (eEndlessModeType)r.ReadInt32();
            int nextSeed = r.ReadInt32();
            bool nextLaunch = r.ReadBoolean();
            bool nextLoadMode = r.ReadBoolean();
            string nextSaveId = r.ReadString();
            string nextSaveSummary = r.ReadString();
            var nextPartyUnlocks = PlayerUnlockSummary.Read(r);
            int nextBestPid = r.ReadInt32();
            string nextBestName = r.ReadString();

            var prevEcoMode = Settings.EconomyMode;
            var prevRunMode = SelectedRunMode;
            var prevWorld = SelectedWorld;
            var prevDiff = DifficultyTier;
            int prevSubIdx = SelectedSubStageIndex;
            string prevEndlessScene = SelectedEndlessSceneName;
            bool prevLaunch = IsLaunchingRun;
            bool prevLoadMode = IsLoadRunMode;
            string prevSaveId = SelectedSaveId;

            Settings.ReadFrom(r);
            Revision = rev;
            SelectedRunMode = nextRunMode;
            SelectedWorld = nextWorld;
            DifficultyTier = nextDiff;
            SelectedSubStageIndex = nextSubIdx;
            SelectedEndlessSceneName = string.IsNullOrWhiteSpace(nextEndlessScene) ? "EnvScene_018" : nextEndlessScene;
            SelectedEndlessType = nextEndlessType;
            MasterSeed = nextSeed;
            IsLaunchingRun = nextLaunch;
            IsLoadRunMode = nextLoadMode;
            SelectedSaveId = nextSaveId ?? "";
            SelectedSaveSummary = nextSaveSummary ?? "";
            SyncedPartyUnlocks = nextPartyUnlocks ?? new PlayerUnlockSummary();
            SyncedMostProgressedPlayerId = nextBestPid;
            SyncedMostProgressedPlayerName = nextBestName ?? "";

            LobbySetupChangeMask diff = reportedMask;
            if (prevEcoMode != Settings.EconomyMode) diff |= LobbySetupChangeMask.CoopMode;
            if (prevRunMode != SelectedRunMode) diff |= LobbySetupChangeMask.GameMode;
            if (prevWorld != SelectedWorld || prevSubIdx != SelectedSubStageIndex || prevEndlessScene != SelectedEndlessSceneName)
                diff |= LobbySetupChangeMask.World;
            if (prevDiff != DifficultyTier) diff |= LobbySetupChangeMask.Difficulty;
            if (prevLaunch != IsLaunchingRun) diff |= LobbySetupChangeMask.LaunchState;
            if (prevLoadMode != IsLoadRunMode || prevSaveId != SelectedSaveId) diff |= LobbySetupChangeMask.SaveMode;
            return diff;
        }
    }
}
