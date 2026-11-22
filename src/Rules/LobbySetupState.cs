using System;
using System.IO;

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
        MajorSetup = CoopMode | World | Difficulty
    }

    public sealed class LobbySetupState
    {
        public uint Revision = 1;
        public eWorldType SelectedWorld = eWorldType.WORLD_1_FOREST;
        public eGameDifficultyType DifficultyTier = eGameDifficultyType.NORMAL;
        public int MasterSeed = 0; // 0 = auto-generate at run start
        public bool IsLaunchingRun = false;

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
            w.Write(MasterSeed);
            w.Write(IsLaunchingRun);
            Settings.Write(w);
        }

        public LobbySetupChangeMask ReadFrom(BinaryReader r)
        {
            uint rev = r.ReadUInt32();
            var reportedMask = (LobbySetupChangeMask)r.ReadByte();
            var nextWorld = (eWorldType)r.ReadInt32();
            var nextDiff = (eGameDifficultyType)r.ReadInt32();
            int nextSeed = r.ReadInt32();
            bool nextLaunch = r.ReadBoolean();

            var prevMode = Settings.EconomyMode;
            var prevWorld = SelectedWorld;
            var prevDiff = DifficultyTier;
            bool prevLaunch = IsLaunchingRun;

            Settings.ReadFrom(r);
            Revision = rev;
            SelectedWorld = nextWorld;
            DifficultyTier = nextDiff;
            MasterSeed = nextSeed;
            IsLaunchingRun = nextLaunch;

            LobbySetupChangeMask diff = reportedMask;
            if (prevMode != Settings.EconomyMode) diff |= LobbySetupChangeMask.CoopMode;
            if (prevWorld != SelectedWorld) diff |= LobbySetupChangeMask.World;
            if (prevDiff != DifficultyTier) diff |= LobbySetupChangeMask.Difficulty;
            if (prevLaunch != IsLaunchingRun) diff |= LobbySetupChangeMask.LaunchState;
            return diff;
        }
    }
}
