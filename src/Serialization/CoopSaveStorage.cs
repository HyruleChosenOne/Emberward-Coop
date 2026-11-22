using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Text;
using System.Text.Json;
using EmberwardCoop.Core;
using EmberwardCoop.Networking;
using EmberwardCoop.Rules;
using EmberwardCoop.Sync;
using UnityEngine;

namespace EmberwardCoop.Serialization
{
    // Isolates active co-op runs onto GameDataManager's temp session for BOTH Host and Clients
    // while keeping the player's solo active-run GameplayData untouched in memory and on disk.
    public static class SoloSaveGuard
    {
        private static GameplayData _soloGameplayBackup;
        private static IntermediateData _soloIntermediateBackup;
        private static bool _inCoopSession;
        private static bool _hasBackup;

        public static bool IsInIsolatedCoopSession => _inCoopSession;

        public static void EnterCoopTempSession()
        {
            var gdm = GameDataManager.instance;
            if (gdm == null) return;

            if (!_hasBackup)
            {
                _soloGameplayBackup = gdm.gameplayData;
                _soloIntermediateBackup = gdm.intermediateData;
                _hasBackup = true;
            }

            if (!gdm.doUseTempSessionData)
            {
                gdm.SwitchSession(true);
                CoopLogger.Info(LogCategory.Sync, "Entered isolated co-op session (solo save protected).");
            }
            _inCoopSession = true;
        }

        public static void InitializeFreshTempRun(int seed, eWorldType world, eGameDifficultyType difficulty)
        {
            var gdm = GameDataManager.instance;
            if (gdm == null) return;

            if (!_hasBackup)
            {
                _soloGameplayBackup = gdm.gameplayData;
                _soloIntermediateBackup = gdm.intermediateData;
                _hasBackup = true;
            }

            // Temporarily swap gdm.gameplayData to a fresh object so vanilla StartNewGame initializes
            // all run fields cleanly without mutating the solo GameplayData instance.
            var freshGameplay = new GameplayData();
            var freshInterm = new IntermediateData();
            try
            {
                gdm.doUseTempSessionData = false;
                gdm.gameplayData = freshGameplay;
                gdm.intermediateData = freshInterm;
                gdm.StartNewGame(seed, world, difficulty);

                gdm.gameplayData_tempSession = gdm.gameplayData;
                gdm.intermediateData_tempSession = gdm.intermediateData;
            }
            finally
            {
                gdm.gameplayData = _soloGameplayBackup;
                gdm.intermediateData = _soloIntermediateBackup;
                gdm.doUseTempSessionData = true;
                _inCoopSession = true;
            }
        }

        public static void ExitCoopTempSession()
        {
            PartyUnlockResolver.CoopUnlockAllActive = false;

            var gdm = GameDataManager.instance;
            if (gdm != null)
            {
                if (_hasBackup)
                {
                    gdm.gameplayData = _soloGameplayBackup;
                    gdm.intermediateData = _soloIntermediateBackup;
                }
                if (gdm.doUseTempSessionData)
                    gdm.SwitchSession(false);
            }

            _inCoopSession = false;
            _hasBackup = false;
            _soloGameplayBackup = null;
            _soloIntermediateBackup = null;
        }

        public static void BeginSoloSafeSerialize(GameDataManager gdm, out bool wasUsingTemp)
        {
            wasUsingTemp = gdm != null && gdm.doUseTempSessionData;
            if (gdm == null || !_inCoopSession || !_hasBackup) return;

            gdm.gameplayData = _soloGameplayBackup;
            gdm.intermediateData = _soloIntermediateBackup;
            gdm.doUseTempSessionData = false;
        }

        public static void EndSoloSafeSerialize(GameDataManager gdm, bool wasUsingTemp)
        {
            if (gdm == null || !_inCoopSession) return;
            gdm.doUseTempSessionData = wasUsingTemp || _inCoopSession;
        }

        // Commits only PlayerLifetimeData (EXP, stage clears, boss flags, codex, achievements)
        // to the local player's solo savefile while keeping solo GameplayData untouched.
        public static void CommitSoloProfileMetaSave()
        {
            var gdm = GameDataManager.instance;
            if (gdm == null) return;

            using (NetworkScope.EnterClientMetaSave())
            {
                BeginSoloSafeSerialize(gdm, out bool wasTemp);
                try
                {
                    gdm.SaveData(true);
                }
                finally
                {
                    EndSoloSafeSerialize(gdm, wasTemp);
                }
            }
        }
    }

    public sealed class SavedTowerDto
    {
        public int ItemType { get; set; }
        public int Level { get; set; } = 1;
    }

    public sealed class SavedCardDto
    {
        public string RuntimeId { get; set; } = "";
        public int CardType { get; set; }
        public int ItemType { get; set; }
        public bool IsCorrupted { get; set; }
        public bool IsBanned { get; set; }
        public int UseCountLimit { get; set; }
        public bool IsTetris { get; set; }
        public int Seed { get; set; }
        public int SpecialRune { get; set; }
        public bool DoOverrideSocket { get; set; }
        public int OverriddenSocketCount { get; set; }
        public int[] Runes { get; set; } = Array.Empty<int>();

        public static SavedCardDto FromNet(NetCardInfo c) => new SavedCardDto
        {
            RuntimeId = c.RuntimeId ?? "",
            CardType = c.CardType,
            ItemType = c.ItemType,
            IsCorrupted = c.IsCorrupted,
            IsBanned = c.IsBanned,
            UseCountLimit = c.UseCountLimit,
            IsTetris = c.IsTetris,
            Seed = c.Seed,
            SpecialRune = c.SpecialRune,
            DoOverrideSocket = c.DoOverrideSocket,
            OverriddenSocketCount = c.OverriddenSocketCount,
            Runes = c.Runes ?? Array.Empty<int>()
        };

        public NetCardInfo ToNet() => new NetCardInfo
        {
            RuntimeId = RuntimeId ?? "",
            CardType = CardType,
            ItemType = ItemType,
            IsCorrupted = IsCorrupted,
            IsBanned = IsBanned,
            UseCountLimit = UseCountLimit,
            IsTetris = IsTetris,
            Seed = Seed,
            SpecialRune = SpecialRune,
            DoOverrideSocket = DoOverrideSocket,
            OverriddenSocketCount = OverriddenSocketCount,
            Runes = Runes ?? Array.Empty<int>()
        };
    }

    public sealed class SavedPlayerStateDto
    {
        public string PlayerKey { get; set; } = "";
        public int SlotIndex { get; set; }
        public string PlayerName { get; set; } = "";
        public int ColorIndex { get; set; }
        public string AvatarHeroId { get; set; } = "BASIC";
        public int Gem { get; set; }
        public int RerollChance { get; set; }
        public int ReviveItem { get; set; }
        public int Coin { get; set; }
        public List<SavedTowerDto> LoadoutTowers { get; set; } = new List<SavedTowerDto>();
        public List<SavedTowerDto> CollectedTowers { get; set; } = new List<SavedTowerDto>();
        public List<SavedCardDto> StorageCards { get; set; } = new List<SavedCardDto>();
        public List<int> Relics { get; set; } = new List<int>();
    }

    public sealed class SavedSettingsDto
    {
        public int EconomyMode { get; set; }
        public int Preset { get; set; }
        public bool AutoScaleByPlayerCount { get; set; }
        public float EnemyHpMultiplier { get; set; }
        public float WaveDensityMultiplier { get; set; }
        public float EnemySpeedMultiplier { get; set; }
        public float BossHpArmorMultiplier { get; set; }
        public int StartingGold { get; set; }
        public float WaveIncomeMultiplier { get; set; }
        public int BlockPacing { get; set; }
        public int SharedTurnBlockCap { get; set; }
        public int CoreHp { get; set; }
        public bool AllowResourceGifting { get; set; }
        public int GiftPerWaveCap { get; set; }
        public int DraftMode { get; set; }
        public int SpeedControl { get; set; }
        public bool SharedLoadoutsInSharedMode { get; set; } = true;
        public bool UnlockAllMapsModes { get; set; }
    }

    public sealed class CoopSaveFile
    {
        public int SaveFormatVersion { get; set; } = 1;
        public string SaveId { get; set; } = "";
        public string DisplayName { get; set; } = "";
        public string SavedAtUtc { get; set; } = "";
        public int WorldType { get; set; }
        public int Difficulty { get; set; }
        public int HostHero { get; set; } = (int)eCharacterType.BASIC;
        public int MasterSeed { get; set; }
        public int CurMapStep { get; set; }
        public int CurMapNodeIndex { get; set; }
        public int CoreHp { get; set; }
        public int CoreMaxHp { get; set; }
        public string TargetScene { get; set; } = "MapScene";
        public int WaveRound { get; set; }
        public bool UnlockAllMapsModesActive { get; set; }

        public SavedSettingsDto Settings { get; set; } = new SavedSettingsDto();
        public string RunSnapshotBase64 { get; set; } = "";
        public string BoardSnapshotBase64 { get; set; } = "";
        public List<SavedPlayerStateDto> Players { get; set; } = new List<SavedPlayerStateDto>();

        public string BuildCompactSummary()
        {
            string wName = WorldCatalog.GetWorldDisplayName((eWorldType)WorldType);
            string dName = WorldCatalog.GetDifficultyDisplayName((eGameDifficultyType)Difficulty);
            string mode = Settings.EconomyMode == (int)CoopEconomyMode.SeparateResources ? "Separate" : "Shared";
            string timeStr = "";
            if (DateTime.TryParse(SavedAtUtc, out DateTime dt))
                timeStr = dt.ToLocalTime().ToString("MM/dd HH:mm");
            return $"{wName} · {dName} · Step {CurMapStep + 1} · {mode}" + (string.IsNullOrEmpty(timeStr) ? "" : $" · {timeStr}");
        }
    }

    public static class CoopSaveStorage
    {
        private static string _savesDir;
        private static readonly JsonSerializerOptions JsonOpts = new JsonSerializerOptions { WriteIndented = true };

        public static string GetSavesDirectory()
        {
            if (!string.IsNullOrEmpty(_savesDir)) return _savesDir;
            string dllDir = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location) ?? ".";
            _savesDir = Path.Combine(dllDir, "CoopSaves");
            try { Directory.CreateDirectory(_savesDir); } catch { }
            return _savesDir;
        }

        public static string MakeStablePlayerKey(PeerInfo p)
        {
            if (p == null) return "Slot:0";
            if (p.SteamId != 0) return $"Steam:{p.SteamId}";
            return $"Name:{(p.Name ?? "Player").Trim().ToLowerInvariant()}";
        }

        public static string CreateNewSaveId(eWorldType world)
            => $"coop_{DateTime.UtcNow:yyyyMMdd_HHmmss}_{(int)world}";

        public static List<CoopSaveFile> ListSaves()
        {
            var list = new List<CoopSaveFile>();
            try
            {
                string dir = GetSavesDirectory();
                if (!Directory.Exists(dir)) return list;

                foreach (string file in Directory.GetFiles(dir, "*.json"))
                {
                    try
                    {
                        var save = JsonSerializer.Deserialize<CoopSaveFile>(File.ReadAllText(file, Encoding.UTF8));
                        if (save != null && !string.IsNullOrWhiteSpace(save.SaveId) && !string.IsNullOrWhiteSpace(save.RunSnapshotBase64))
                            list.Add(save);
                    }
                    catch { }
                }

                list.Sort((a, b) => string.CompareOrdinal(b.SavedAtUtc ?? "", a.SavedAtUtc ?? ""));
            }
            catch (Exception ex)
            {
                CoopLogger.Warn(LogCategory.Sync, $"ListSaves warning: {ex.Message}");
            }
            return list;
        }

        public static CoopSaveFile LoadSave(string saveId)
        {
            if (string.IsNullOrWhiteSpace(saveId)) return null;
            try
            {
                string path = Path.Combine(GetSavesDirectory(), SanitizeId(saveId) + ".json");
                if (!File.Exists(path)) return null;
                return JsonSerializer.Deserialize<CoopSaveFile>(File.ReadAllText(path, Encoding.UTF8));
            }
            catch (Exception ex)
            {
                CoopLogger.Warn(LogCategory.Sync, $"LoadSave({saveId}) failed: {ex.Message}");
                return null;
            }
        }

        public static bool DeleteSave(string saveId)
        {
            if (string.IsNullOrWhiteSpace(saveId)) return false;
            try
            {
                string path = Path.Combine(GetSavesDirectory(), SanitizeId(saveId) + ".json");
                if (File.Exists(path)) File.Delete(path);
                return true;
            }
            catch { return false; }
        }

        public static bool SaveActiveRun(SessionController session, string saveId, string reason = "")
        {
            if (session == null || !session.Net.IsHost) return false;
            var gdm = GameDataManager.instance;
            var gd = gdm?.GameplayData;
            if (gd == null || !gd.isGameStarted || gd.isGameEnded) return false;

            try
            {
                string id = string.IsNullOrWhiteSpace(saveId) ? CreateNewSaveId(gd.curWorld) : SanitizeId(saveId);
                string scene = GameRefs.IsInBattle ? "MainGame" : "MapScene";

                byte[] runBytes = session.Run.CaptureRunSnapshotBytes(scene);
                byte[] boardBytes = GameRefs.IsInBattle ? session.Board.CaptureBoardSnapshotBytes() : Array.Empty<byte>();

                var s = session.Settings;
                var save = new CoopSaveFile
                {
                    SaveFormatVersion = 1,
                    SaveId = id,
                    DisplayName = $"{WorldCatalog.GetWorldDisplayName(gd.curWorld)} ({WorldCatalog.GetDifficultyDisplayName(gd.difficulty)})",
                    SavedAtUtc = DateTime.UtcNow.ToString("O"),
                    WorldType = (int)gd.curWorld,
                    Difficulty = (int)gd.difficulty,
                    HostHero = (int)gd.characterType,
                    MasterSeed = gd.randomSeed,
                    CurMapStep = gd.curMapStep,
                    CurMapNodeIndex = gd.curMapNodeIndex,
                    CoreHp = gd.curHP,
                    CoreMaxHp = gd.maxHP,
                    TargetScene = scene,
                    WaveRound = MainGame.Instance != null ? MainGame.Instance.Round : 0,
                    UnlockAllMapsModesActive = s.UnlockAllMapsModes,
                    Settings = new SavedSettingsDto
                    {
                        EconomyMode = (int)s.EconomyMode,
                        Preset = (int)s.Preset,
                        AutoScaleByPlayerCount = s.AutoScaleByPlayerCount,
                        EnemyHpMultiplier = s.EnemyHpMultiplier,
                        WaveDensityMultiplier = s.WaveDensityMultiplier,
                        EnemySpeedMultiplier = s.EnemySpeedMultiplier,
                        BossHpArmorMultiplier = s.BossHpArmorMultiplier,
                        StartingGold = (int)s.StartingGold,
                        WaveIncomeMultiplier = s.WaveIncomeMultiplier,
                        BlockPacing = (int)s.BlockPacing,
                        SharedTurnBlockCap = s.SharedTurnBlockCap,
                        CoreHp = (int)s.CoreHp,
                        AllowResourceGifting = s.AllowResourceGifting,
                        GiftPerWaveCap = s.GiftPerWaveCap,
                        DraftMode = (int)s.DraftMode,
                        SpeedControl = (int)s.SpeedControl,
                        SharedLoadoutsInSharedMode = s.SharedLoadoutsInSharedMode,
                        UnlockAllMapsModes = s.UnlockAllMapsModes
                    },
                    RunSnapshotBase64 = Convert.ToBase64String(runBytes),
                    BoardSnapshotBase64 = boardBytes.Length > 0 ? Convert.ToBase64String(boardBytes) : ""
                };

                int slot = 0;
                foreach (var kv in session.Net.Roster)
                {
                    var peer = kv.Value;
                    var ps = new SavedPlayerStateDto
                    {
                        PlayerKey = MakeStablePlayerKey(peer),
                        SlotIndex = slot++,
                        PlayerName = peer.Name ?? $"Player {peer.PlayerId}",
                        ColorIndex = peer.ColorIndex,
                        AvatarHeroId = peer.AvatarHeroId ?? "BASIC"
                    };

                    if (peer.PlayerId == 0)
                    {
                        CaptureGameplayInventoryInto(gd, MainGame.Instance?.IngameData, ps);
                    }
                    else if (session.Run.PeerSavedInventories.TryGetValue(peer.PlayerId, out var cachedPeerInv))
                    {
                        ps.Gem = cachedPeerInv.Gem;
                        ps.RerollChance = cachedPeerInv.RerollChance;
                        ps.ReviveItem = cachedPeerInv.ReviveItem;
                        ps.Coin = cachedPeerInv.Coin;
                        ps.LoadoutTowers = new List<SavedTowerDto>(cachedPeerInv.LoadoutTowers);
                        ps.CollectedTowers = new List<SavedTowerDto>(cachedPeerInv.CollectedTowers);
                        ps.StorageCards = new List<SavedCardDto>(cachedPeerInv.StorageCards);
                        ps.Relics = new List<int>(cachedPeerInv.Relics);
                    }
                    else
                    {
                        var resolved = LoadoutCatalog.ResolveEffectiveLoadout(peer.Loadout, gd.characterType, gd.randomSeed ^ (peer.PlayerId * 1337));
                        for (int i = 0; i < resolved.Towers.Count; i++)
                        {
                            ps.LoadoutTowers.Add(new SavedTowerDto { ItemType = (int)resolved.Towers[i], Level = 1 });
                            ps.CollectedTowers.Add(new SavedTowerDto { ItemType = (int)resolved.Towers[i], Level = 1 });
                        }
                        for (int i = 0; i < resolved.Blocks.Count; i++)
                        {
                            ps.StorageCards.Add(new SavedCardDto
                            {
                                ItemType = (int)resolved.Blocks[i],
                                CardType = (int)eCardType.PANEL_CARD,
                                IsTetris = true,
                                Seed = unchecked(gd.randomSeed ^ (i * 9973))
                            });
                        }
                    }
                    save.Players.Add(ps);
                }

                string path = Path.Combine(GetSavesDirectory(), id + ".json");
                File.WriteAllText(path, JsonSerializer.Serialize(save, JsonOpts), Encoding.UTF8);
                CoopLogger.Info(LogCategory.Sync, $"Saved co-op run '{id}' ({scene}, Step {save.CurMapStep + 1}){(string.IsNullOrEmpty(reason) ? "" : $" [{reason}]")}.");
                return true;
            }
            catch (Exception ex)
            {
                CoopLogger.Error(LogCategory.Sync, "SaveActiveRun failed", ex);
                return false;
            }
        }

        public static void CaptureGameplayInventoryInto(GameplayData gd, IngameData ig, SavedPlayerStateDto dst)
        {
            if (gd == null || dst == null) return;
            dst.Gem = gd.gem;
            dst.RerollChance = gd.rerollChance;
            dst.ReviveItem = gd.reviveItem;
            dst.Coin = ig != null ? ig.Coin : 0;

            dst.LoadoutTowers.Clear();
            if (gd.list_LoadoutTowerData != null)
            {
                for (int i = 0; i < gd.list_LoadoutTowerData.Count; i++)
                {
                    var t = gd.list_LoadoutTowerData[i];
                    if (t != null) dst.LoadoutTowers.Add(new SavedTowerDto { ItemType = (int)t.ItemType, Level = t.Level });
                }
            }

            dst.CollectedTowers.Clear();
            if (gd.list_CollectedTowerData != null)
            {
                for (int i = 0; i < gd.list_CollectedTowerData.Count; i++)
                {
                    var t = gd.list_CollectedTowerData[i];
                    if (t != null) dst.CollectedTowers.Add(new SavedTowerDto { ItemType = (int)t.ItemType, Level = t.Level });
                }
            }

            dst.StorageCards.Clear();
            if (gd.list_ItemStorage != null)
            {
                for (int i = 0; i < gd.list_ItemStorage.Count; i++)
                    dst.StorageCards.Add(SavedCardDto.FromNet(GameSerializers.CaptureCard(gd.list_ItemStorage[i])));
            }

            dst.Relics.Clear();
            if (gd.list_CollectedRelic != null)
            {
                for (int i = 0; i < gd.list_CollectedRelic.Count; i++)
                    dst.Relics.Add((int)gd.list_CollectedRelic[i]);
            }
        }

        public static void ApplySavedInventoryToGameplay(GameplayData gd, SavedPlayerStateDto src)
        {
            if (gd == null || src == null) return;
            using (NetworkScope.Enter())
            {
                gd.gem = src.Gem;
                gd.rerollChance = src.RerollChance;
                gd.reviveItem = src.ReviveItem;

                if (src.LoadoutTowers != null && src.LoadoutTowers.Count > 0)
                {
                    gd.list_LoadoutTowerData ??= new Il2CppSystem.Collections.Generic.List<TowerIngameData>();
                    gd.list_LoadoutTowerData.Clear();
                    for (int i = 0; i < src.LoadoutTowers.Count; i++)
                        gd.list_LoadoutTowerData.Add(new TowerIngameData((eItemType)src.LoadoutTowers[i].ItemType, Math.Max(1, src.LoadoutTowers[i].Level)));
                }

                if (src.CollectedTowers != null && src.CollectedTowers.Count > 0)
                {
                    gd.list_CollectedTowerData ??= new Il2CppSystem.Collections.Generic.List<TowerIngameData>();
                    gd.list_CollectedTowerData.Clear();
                    for (int i = 0; i < src.CollectedTowers.Count; i++)
                        gd.list_CollectedTowerData.Add(new TowerIngameData((eItemType)src.CollectedTowers[i].ItemType, Math.Max(1, src.CollectedTowers[i].Level)));
                }

                if (src.StorageCards != null && src.StorageCards.Count > 0)
                {
                    gd.list_ItemStorage ??= new Il2CppSystem.Collections.Generic.List<CardData>();
                    gd.list_ItemStorage.Clear();
                    for (int i = 0; i < src.StorageCards.Count; i++)
                    {
                        var cd = GameSerializers.MaterializeCard(src.StorageCards[i].ToNet());
                        if (cd != null) gd.list_ItemStorage.Add(cd);
                    }
                }

                if (src.Relics != null)
                {
                    gd.list_CollectedRelic ??= new Il2CppSystem.Collections.Generic.List<eItemType>();
                    gd.list_CollectedRelic.Clear();
                    for (int i = 0; i < src.Relics.Count; i++)
                        gd.list_CollectedRelic.Add((eItemType)src.Relics[i]);
                }
            }
        }

        public static void ApplySettingsFromSave(CoopSaveFile save, SessionSettings target)
        {
            if (save?.Settings == null || target == null) return;
            var d = save.Settings;
            target.EconomyMode = (CoopEconomyMode)d.EconomyMode;
            target.Preset = (SessionPreset)d.Preset;
            target.AutoScaleByPlayerCount = d.AutoScaleByPlayerCount;
            target.EnemyHpMultiplier = Math.Clamp(d.EnemyHpMultiplier, 1.0f, 2.5f);
            target.WaveDensityMultiplier = Math.Clamp(d.WaveDensityMultiplier, 1.0f, 2.0f);
            target.EnemySpeedMultiplier = Math.Clamp(d.EnemySpeedMultiplier, 0.8f, 1.5f);
            target.BossHpArmorMultiplier = Math.Clamp(d.BossHpArmorMultiplier, 1.0f, 3.0f);
            target.StartingGold = (StartingGoldRule)d.StartingGold;
            target.WaveIncomeMultiplier = Math.Clamp(d.WaveIncomeMultiplier, 0.5f, 1.5f);
            target.BlockPacing = (BlockPacingRule)d.BlockPacing;
            target.SharedTurnBlockCap = Math.Clamp(d.SharedTurnBlockCap, 1, 12);
            target.CoreHp = (CoreHpRule)d.CoreHp;
            target.AllowResourceGifting = d.AllowResourceGifting;
            target.GiftPerWaveCap = Math.Max(0, d.GiftPerWaveCap);
            target.DraftMode = (DraftSyncMode)d.DraftMode;
            target.SpeedControl = (SpeedControlRule)d.SpeedControl;
            target.SharedLoadoutsInSharedMode = d.SharedLoadoutsInSharedMode;
            target.UnlockAllMapsModes = d.UnlockAllMapsModes;
        }

        public static SavedPlayerStateDto MatchSavedPlayer(CoopSaveFile save, PeerInfo peer, HashSet<int> usedSlots)
        {
            if (save?.Players == null || save.Players.Count == 0 || peer == null) return null;
            string key = MakeStablePlayerKey(peer);

            for (int i = 0; i < save.Players.Count; i++)
            {
                if (!usedSlots.Contains(i) && string.Equals(save.Players[i].PlayerKey, key, StringComparison.OrdinalIgnoreCase))
                {
                    usedSlots.Add(i);
                    return save.Players[i];
                }
            }

            string cleanName = (peer.Name ?? "").Trim();
            for (int i = 0; i < save.Players.Count; i++)
            {
                if (!usedSlots.Contains(i) && string.Equals(save.Players[i].PlayerName?.Trim(), cleanName, StringComparison.OrdinalIgnoreCase))
                {
                    usedSlots.Add(i);
                    return save.Players[i];
                }
            }

            for (int i = 0; i < save.Players.Count; i++)
            {
                if (!usedSlots.Contains(i))
                {
                    usedSlots.Add(i);
                    return save.Players[i];
                }
            }
            return null;
        }

        public static void WritePlayerState(BinaryWriter w, SavedPlayerStateDto dto)
        {
            w.Write(dto != null);
            if (dto == null) return;
            w.Write(dto.Gem);
            w.Write(dto.RerollChance);
            w.Write(dto.ReviveItem);
            w.Write(dto.Coin);

            w.Write(dto.LoadoutTowers?.Count ?? 0);
            if (dto.LoadoutTowers != null)
            {
                for (int i = 0; i < dto.LoadoutTowers.Count; i++)
                {
                    w.Write(dto.LoadoutTowers[i].ItemType);
                    w.Write(dto.LoadoutTowers[i].Level);
                }
            }

            w.Write(dto.CollectedTowers?.Count ?? 0);
            if (dto.CollectedTowers != null)
            {
                for (int i = 0; i < dto.CollectedTowers.Count; i++)
                {
                    w.Write(dto.CollectedTowers[i].ItemType);
                    w.Write(dto.CollectedTowers[i].Level);
                }
            }

            w.Write(dto.StorageCards?.Count ?? 0);
            if (dto.StorageCards != null)
            {
                for (int i = 0; i < dto.StorageCards.Count; i++)
                    dto.StorageCards[i].ToNet().Write(w);
            }

            w.Write(dto.Relics?.Count ?? 0);
            if (dto.Relics != null)
            {
                for (int i = 0; i < dto.Relics.Count; i++)
                    w.Write(dto.Relics[i]);
            }
        }

        public static SavedPlayerStateDto ReadPlayerState(BinaryReader r)
        {
            if (!r.ReadBoolean()) return null;
            var dto = new SavedPlayerStateDto
            {
                Gem = r.ReadInt32(),
                RerollChance = r.ReadInt32(),
                ReviveItem = r.ReadInt32(),
                Coin = r.ReadInt32()
            };

            int lc = r.ReadInt32();
            for (int i = 0; i < lc; i++)
                dto.LoadoutTowers.Add(new SavedTowerDto { ItemType = r.ReadInt32(), Level = r.ReadInt32() });

            int cc = r.ReadInt32();
            for (int i = 0; i < cc; i++)
                dto.CollectedTowers.Add(new SavedTowerDto { ItemType = r.ReadInt32(), Level = r.ReadInt32() });

            int sc = r.ReadInt32();
            for (int i = 0; i < sc; i++)
                dto.StorageCards.Add(SavedCardDto.FromNet(NetCardInfo.Read(r)));

            int rc = r.ReadInt32();
            for (int i = 0; i < rc; i++)
                dto.Relics.Add(r.ReadInt32());

            return dto;
        }

        private static string SanitizeId(string id)
        {
            char[] invalid = Path.GetInvalidFileNameChars();
            string clean = (id ?? "").Trim();
            for (int i = 0; i < invalid.Length; i++) clean = clean.Replace(invalid[i].ToString(), "_");
            return clean.Length > 64 ? clean.Substring(0, 64) : clean;
        }
    }
}
