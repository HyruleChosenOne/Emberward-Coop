using System;
using System.Collections.Generic;
using System.IO;
using EmberwardCoop.Core;
using EmberwardCoop.Localization;
using EmberwardCoop.Networking;

namespace EmberwardCoop.Rules
{
    public sealed class PlayerUnlockSummary
    {
        public uint UnlockedWorldMask = 1u;
        public uint ClearedWorldMask = 0u;
        public byte UnlockedDifficultyMask = 0x03; // Casual + Normal baseline
        public uint UnlockedHeroMask = 1u << (int)eCharacterType.BASIC;
        public bool EndlessUnlocked = false;
        public bool EnigmaSanctumUnlocked = false;
        public int ProgressScore = 1;

        public static PlayerUnlockSummary CaptureLocal()
        {
            var summary = new PlayerUnlockSummary
            {
                UnlockedWorldMask = 1u,
                ClearedWorldMask = 0u,
                UnlockedDifficultyMask = 0x03,
                UnlockedHeroMask = 1u << (int)eCharacterType.BASIC,
                EndlessUnlocked = false,
                EnigmaSanctumUnlocked = false,
                ProgressScore = 1
            };

            var pld = GameDataManager.instance?.Playerdata;
            if (pld == null) return summary;

            // Bypass any in-memory co-op unlock override so temporary unlocks never leak into local summary.
            using (PartyUnlockResolver.EnterRealUnlockRead())
            {
                try
                {
                    var worlds = WorldCatalog.AllBaseWorldTypes;
                    int score = 0;

                    for (int i = 0; i < worlds.Length; i++)
                    {
                        var wt = worlds[i];
                        bool unlocked = wt == eWorldType.WORLD_1_FOREST;
                        bool cleared = false;
                        try
                        {
                            if (!pld.IsWorldImplemented(wt)) continue;
                            unlocked = unlocked || pld.IsWorldUnlockedInAnyDifficulty(wt);
                            cleared = pld.IsWorldClearedInAnyDifficulty(wt);
                        }
                        catch { }

                        if (unlocked)
                        {
                            summary.UnlockedWorldMask |= (1u << i);
                            score += 10;
                        }
                        if (cleared)
                        {
                            summary.ClearedWorldMask |= (1u << i);
                            score += 25;
                        }
                    }

                    byte diffMask = 0x03; // Casual (bit 0) + Normal (bit 1)
                    try
                    {
                        bool heroicUnlocked = pld.isDefeatedNormalModeBoss
                            || pld.IsWorldCleared(eGameDifficultyType.NORMAL, eWorldType.WORLD_1_FOREST)
                            || pld.IsWorldUnlocked(eGameDifficultyType.HEROIC, eWorldType.WORLD_1_FOREST);
                        if (heroicUnlocked)
                        {
                            diffMask |= (1 << 2);
                            score += 40;
                        }

                        bool nightmareUnlocked = pld.isDefeatedHeroicModeBoss
                            || pld.IsWorldCleared(eGameDifficultyType.HEROIC, eWorldType.WORLD_1_FOREST)
                            || pld.IsWorldUnlocked(eGameDifficultyType.NIGHTMARE, eWorldType.WORLD_1_FOREST);
                        if (nightmareUnlocked)
                        {
                            diffMask |= (1 << 3);
                            score += 80;
                        }
                    }
                    catch { }
                    summary.UnlockedDifficultyMask = diffMask;

                    uint heroMask = 1u << (int)eCharacterType.BASIC;
                    for (int c = 1; c <= 12; c++)
                    {
                        var ct = (eCharacterType)c;
                        try
                        {
                            if (ct == eCharacterType.BASIC || (pld.IsCharacterImplemented(ct) && pld.IsCharacterUnlocked(ct)))
                            {
                                heroMask |= (1u << c);
                                score += 5;
                            }
                        }
                        catch { }
                    }
                    summary.UnlockedHeroMask = heroMask;

                    try
                    {
                        summary.EndlessUnlocked = pld.IsAnyEndlessModeUnlocked()
                            || (summary.ClearedWorldMask & 1u) != 0;
                        if (summary.EndlessUnlocked) score += 20;
                    }
                    catch { }

                    try
                    {
                        summary.EnigmaSanctumUnlocked = pld.IsEnigmaSanctumUnlocked();
                        if (summary.EnigmaSanctumUnlocked) score += 30;
                    }
                    catch { }

                    summary.ProgressScore = Math.Max(1, score);
                }
                catch (Exception ex)
                {
                    CoopLogger.Debug(LogCategory.Sync, $"CaptureLocal unlock summary warning: {ex.Message}");
                }
            }

            return summary;
        }

        public bool IsWorldBitSet(int index) => index >= 0 && index < 32 && (UnlockedWorldMask & (1u << index)) != 0;
        public bool IsWorldClearedBitSet(int index) => index >= 0 && index < 32 && (ClearedWorldMask & (1u << index)) != 0;
        public bool IsDifficultyBitSet(eGameDifficultyType diff)
        {
            int idx = (int)diff;
            if (idx < 0 || idx > 7) return false;
            return (UnlockedDifficultyMask & (1 << idx)) != 0;
        }

        public bool IsHeroBitSet(eCharacterType ct)
        {
            int idx = (int)ct;
            if (idx <= 0 || idx >= 32) return ct == eCharacterType.BASIC;
            return (UnlockedHeroMask & (1u << idx)) != 0;
        }

        public bool IsRunModeBitSet(CoopRunMode mode) => mode switch
        {
            CoopRunMode.Campaign => true,
            CoopRunMode.Endless => EndlessUnlocked,
            CoopRunMode.EnigmaSanctum => EnigmaSanctumUnlocked,
            _ => true
        };

        public PlayerUnlockSummary Clone() => new PlayerUnlockSummary
        {
            UnlockedWorldMask = UnlockedWorldMask,
            ClearedWorldMask = ClearedWorldMask,
            UnlockedDifficultyMask = UnlockedDifficultyMask,
            UnlockedHeroMask = UnlockedHeroMask,
            EndlessUnlocked = EndlessUnlocked,
            EnigmaSanctumUnlocked = EnigmaSanctumUnlocked,
            ProgressScore = ProgressScore
        };

        public void Write(BinaryWriter w)
        {
            w.Write(UnlockedWorldMask);
            w.Write(ClearedWorldMask);
            w.Write(UnlockedDifficultyMask);
            w.Write(UnlockedHeroMask);
            w.Write(EndlessUnlocked);
            w.Write(EnigmaSanctumUnlocked);
            w.Write(ProgressScore);
        }

        public static PlayerUnlockSummary Read(BinaryReader r) => new PlayerUnlockSummary
        {
            UnlockedWorldMask = r.ReadUInt32(),
            ClearedWorldMask = r.ReadUInt32(),
            UnlockedDifficultyMask = r.ReadByte(),
            UnlockedHeroMask = r.ReadUInt32(),
            EndlessUnlocked = r.ReadBoolean(),
            EnigmaSanctumUnlocked = r.ReadBoolean(),
            ProgressScore = r.ReadInt32()
        };
    }

    public sealed class PartyUnlockResolver
    {
        // Strict in-memory override for Co-op-only "Unlock All Maps/Modes". Never written to solo saves.
        public static bool CoopUnlockAllActive { get; set; }

        private static int _realReadDepth;
        public static bool IsBypassingOverrideForRealRead => _realReadDepth > 0;

        public readonly struct RealReadScope : IDisposable
        {
            public void Dispose()
            {
                if (_realReadDepth > 0) _realReadDepth--;
            }
        }

        public static RealReadScope EnterRealUnlockRead()
        {
            _realReadDepth++;
            return new RealReadScope();
        }

        // Unlock override applies only inside an open Co-op Lobby or an isolated Co-op session.
        public static bool IsInCoopMenuOrCoopRun
        {
            get
            {
                var s = Sync.SessionController.Instance;
                if (s == null || !s.Net.IsConnected) return false;
                if (GameRefs.IsInTitle)
                    return UI.CoopLobbyPanel.IsAnyOpen || Serialization.SoloSaveGuard.IsInIsolatedCoopSession;
                return Serialization.SoloSaveGuard.IsInIsolatedCoopSession;
            }
        }

        public static bool IsCoopUnlockAllEffective()
        {
            if (IsBypassingOverrideForRealRead) return false;
            if (!CoopUnlockAllActive) return false;
            return IsInCoopMenuOrCoopRun;
        }

        public PlayerUnlockSummary PartyBaseline { get; private set; } = new PlayerUnlockSummary();
        public PlayerUnlockSummary LocalSummary { get; private set; } = new PlayerUnlockSummary();
        public int MostProgressedPlayerId { get; private set; } = 0;
        public string MostProgressedPlayerName { get; private set; } = "";

        private readonly Dictionary<eWorldType, string> _worldUnlockedBy = new Dictionary<eWorldType, string>();
        private readonly Dictionary<eGameDifficultyType, string> _diffUnlockedBy = new Dictionary<eGameDifficultyType, string>();
        private readonly Dictionary<CoopRunMode, string> _modeUnlockedBy = new Dictionary<CoopRunMode, string>();

        public void RefreshLocalSummary()
        {
            LocalSummary = PlayerUnlockSummary.CaptureLocal();
        }

        public void RecomputeFromRoster(NetworkManager net)
        {
            RefreshLocalSummary();
            _worldUnlockedBy.Clear();
            _diffUnlockedBy.Clear();
            _modeUnlockedBy.Clear();

            if (net == null || !net.IsConnected || net.Roster.Count == 0)
            {
                PartyBaseline = LocalSummary.Clone();
                MostProgressedPlayerId = 0;
                MostProgressedPlayerName = "";
                return;
            }

            if (net.Roster.TryGetValue(net.LocalPlayerId, out var localPeer))
                localPeer.Unlocks = LocalSummary.Clone();

            var combined = new PlayerUnlockSummary
            {
                UnlockedWorldMask = 1u,
                ClearedWorldMask = 0u,
                UnlockedDifficultyMask = 0x03,
                UnlockedHeroMask = 1u << (int)eCharacterType.BASIC,
                EndlessUnlocked = false,
                EnigmaSanctumUnlocked = false,
                ProgressScore = 0
            };

            int bestPid = net.LocalPlayerId;
            string bestName = net.LocalPlayerName ?? "";
            int bestScore = -1;

            var worlds = WorldCatalog.AllBaseWorldTypes;
            var diffs = WorldCatalog.AllDifficulties;

            foreach (var kv in net.Roster)
            {
                var p = kv.Value;
                var u = p.Unlocks ?? new PlayerUnlockSummary();

                combined.UnlockedWorldMask |= u.UnlockedWorldMask;
                combined.ClearedWorldMask |= u.ClearedWorldMask;
                combined.UnlockedDifficultyMask |= u.UnlockedDifficultyMask;
                combined.UnlockedHeroMask |= u.UnlockedHeroMask;
                combined.EndlessUnlocked |= u.EndlessUnlocked;
                combined.EnigmaSanctumUnlocked |= u.EnigmaSanctumUnlocked;

                if (u.ProgressScore > bestScore)
                {
                    bestScore = u.ProgressScore;
                    bestPid = p.PlayerId;
                    bestName = p.Name ?? "";
                }

                bool isOther = p.PlayerId != net.LocalPlayerId;
                for (int i = 0; i < worlds.Length; i++)
                {
                    if (u.IsWorldBitSet(i) && !LocalSummary.IsWorldBitSet(i) && isOther && !_worldUnlockedBy.ContainsKey(worlds[i]))
                        _worldUnlockedBy[worlds[i]] = p.Name ?? "";
                }
                for (int i = 0; i < diffs.Length; i++)
                {
                    if (u.IsDifficultyBitSet(diffs[i]) && !LocalSummary.IsDifficultyBitSet(diffs[i]) && isOther && !_diffUnlockedBy.ContainsKey(diffs[i]))
                        _diffUnlockedBy[diffs[i]] = p.Name ?? "";
                }
                if (u.EndlessUnlocked && !LocalSummary.EndlessUnlocked && isOther && !_modeUnlockedBy.ContainsKey(CoopRunMode.Endless))
                    _modeUnlockedBy[CoopRunMode.Endless] = p.Name ?? "";
                if (u.EnigmaSanctumUnlocked && !LocalSummary.EnigmaSanctumUnlocked && isOther && !_modeUnlockedBy.ContainsKey(CoopRunMode.EnigmaSanctum))
                    _modeUnlockedBy[CoopRunMode.EnigmaSanctum] = p.Name ?? "";
            }

            combined.ProgressScore = Math.Max(1, bestScore);
            PartyBaseline = combined;
            MostProgressedPlayerId = bestPid;
            MostProgressedPlayerName = bestName;
        }

        public void ApplySyncedBaseline(PlayerUnlockSummary synced, int bestPid, string bestName)
        {
            RefreshLocalSummary();
            PartyBaseline = synced != null ? synced.Clone() : LocalSummary.Clone();
            MostProgressedPlayerId = bestPid;
            MostProgressedPlayerName = bestName ?? "";
        }

        public bool IsWorldUnlockedForParty(eWorldType world)
        {
            if (world == eWorldType.WORLD_1_FOREST) return true;
            if (IsCoopUnlockAllEffective()) return true;
            int idx = WorldCatalog.GetBaseWorldIndex(world);
            return idx >= 0 && PartyBaseline.IsWorldBitSet(idx);
        }

        public bool IsDifficultyUnlockedForParty(eGameDifficultyType diff)
        {
            if (diff == eGameDifficultyType.CASUAL || diff == eGameDifficultyType.NORMAL) return true;
            if (IsCoopUnlockAllEffective()) return true;
            return PartyBaseline.IsDifficultyBitSet(diff);
        }

        public bool IsRunModeUnlockedForParty(CoopRunMode mode)
        {
            if (mode == CoopRunMode.Campaign) return true;
            if (IsCoopUnlockAllEffective()) return true;
            return PartyBaseline.IsRunModeBitSet(mode);
        }

        public string GetWorldUnlockNote(eWorldType world)
        {
            int idx = WorldCatalog.GetBaseWorldIndex(world);
            if (idx <= 0) return "";
            if (LocalSummary.IsWorldBitSet(idx)) return "";
            if (PartyBaseline.IsWorldBitSet(idx))
            {
                if (_worldUnlockedBy.TryGetValue(world, out string who) && !string.IsNullOrWhiteSpace(who))
                    return CoopLoc.F("lobby.unlocked_via_peer", who);
                if (!string.IsNullOrWhiteSpace(MostProgressedPlayerName))
                    return CoopLoc.F("lobby.unlocked_via_peer", MostProgressedPlayerName);
                return CoopLoc.T("lobby.unlocked_via_party");
            }
            if (IsCoopUnlockAllEffective())
                return CoopLoc.T("lobby.unlocked_via_all_toggle");
            return "";
        }

        public string GetDifficultyUnlockNote(eGameDifficultyType diff)
        {
            if (diff == eGameDifficultyType.CASUAL || diff == eGameDifficultyType.NORMAL) return "";
            if (LocalSummary.IsDifficultyBitSet(diff)) return "";
            if (PartyBaseline.IsDifficultyBitSet(diff))
            {
                if (_diffUnlockedBy.TryGetValue(diff, out string who) && !string.IsNullOrWhiteSpace(who))
                    return CoopLoc.F("lobby.unlocked_via_peer", who);
                if (!string.IsNullOrWhiteSpace(MostProgressedPlayerName))
                    return CoopLoc.F("lobby.unlocked_via_peer", MostProgressedPlayerName);
                return CoopLoc.T("lobby.unlocked_via_party");
            }
            if (IsCoopUnlockAllEffective())
                return CoopLoc.T("lobby.unlocked_via_all_toggle");
            return "";
        }

        public string GetRunModeUnlockNote(CoopRunMode mode)
        {
            if (mode == CoopRunMode.Campaign) return "";
            if (LocalSummary.IsRunModeBitSet(mode)) return "";
            if (PartyBaseline.IsRunModeBitSet(mode))
            {
                if (_modeUnlockedBy.TryGetValue(mode, out string who) && !string.IsNullOrWhiteSpace(who))
                    return CoopLoc.F("lobby.unlocked_via_peer", who);
                if (!string.IsNullOrWhiteSpace(MostProgressedPlayerName))
                    return CoopLoc.F("lobby.unlocked_via_peer", MostProgressedPlayerName);
                return CoopLoc.T("lobby.unlocked_via_party");
            }
            if (IsCoopUnlockAllEffective())
                return CoopLoc.T("lobby.unlocked_via_all_toggle");
            return "";
        }
    }
}
