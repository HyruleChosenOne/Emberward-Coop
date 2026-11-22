using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using EmberwardCoop.Core;
using EmberwardCoop.Localization;
using EmberwardCoop.Networking;
using UnityEngine;

namespace EmberwardCoop.Rules
{
    public sealed class PlayerLoadoutChoice
    {
        public const int MaxStartingTowers = 3;
        public const int MinStartingBlocks = 6;
        public const int MaxStartingBlocks = 10;

        public bool UseHeroDefault = true;
        public bool IsConfirmed = true;
        public List<eItemType> Towers = new List<eItemType>();
        public List<eItemType> Blocks = new List<eItemType>();

        public bool IsValid => UseHeroDefault || (Towers != null && Towers.Count > 0 && Blocks != null && Blocks.Count > 0);

        public PlayerLoadoutChoice Clone() => new PlayerLoadoutChoice
        {
            UseHeroDefault = UseHeroDefault,
            IsConfirmed = IsConfirmed,
            Towers = Towers != null ? new List<eItemType>(Towers) : new List<eItemType>(),
            Blocks = Blocks != null ? new List<eItemType>(Blocks) : new List<eItemType>()
        };

        public void Write(BinaryWriter w)
        {
            w.Write(UseHeroDefault);
            w.Write(IsConfirmed);
            int tc = Towers?.Count ?? 0;
            w.Write(tc);
            for (int i = 0; i < tc; i++) w.Write((int)Towers[i]);
            int bc = Blocks?.Count ?? 0;
            w.Write(bc);
            for (int i = 0; i < bc; i++) w.Write((int)Blocks[i]);
        }

        public static PlayerLoadoutChoice Read(BinaryReader r)
        {
            var c = new PlayerLoadoutChoice
            {
                UseHeroDefault = r.ReadBoolean(),
                IsConfirmed = r.ReadBoolean()
            };
            int tc = Math.Clamp(r.ReadInt32(), 0, 12);
            for (int i = 0; i < tc; i++) c.Towers.Add((eItemType)r.ReadInt32());
            int bc = Math.Clamp(r.ReadInt32(), 0, 24);
            for (int i = 0; i < bc; i++) c.Blocks.Add((eItemType)r.ReadInt32());
            return c;
        }

        public string BuildCompactSummary(eCharacterType hostHero)
        {
            var resolved = LoadoutCatalog.ResolveEffectiveLoadout(this, hostHero, 1337);
            if (resolved.Towers.Count == 0)
                return CoopLoc.T("loadout.hero_default_short");

            var sb = new StringBuilder();
            int shown = Math.Min(3, resolved.Towers.Count);
            for (int i = 0; i < shown; i++)
            {
                if (i > 0) sb.Append(" · ");
                sb.Append(LoadoutCatalog.GetItemShortName(resolved.Towers[i]));
            }
            if (resolved.Blocks.Count > 0)
                sb.Append($" ({resolved.Blocks.Count})");
            return sb.ToString();
        }
    }

    public sealed class SelectableItemInfo
    {
        public eItemType ItemType;
        public string DisplayName;
        public Sprite Icon;
        public string Tag;
    }

    public static class LoadoutCatalog
    {
        public static readonly eCharacterType[] AllHeroes =
        {
            eCharacterType.BASIC,
            eCharacterType.KNIGHT,
            eCharacterType.SCHOLAR,
            eCharacterType.MERCHANT,
            eCharacterType.SORCERER,
            eCharacterType.WITCH,
            eCharacterType.CHUNK,
            eCharacterType.TINY,
            eCharacterType.TIME_MAGICIAN,
            eCharacterType.SCRAP_MASTER,
            eCharacterType.JOKER,
            eCharacterType.KING
        };

        public static List<eCharacterType> GetSelectableHeroes(PartyUnlockResolver resolver)
        {
            var list = new List<eCharacterType>();
            var pld = GameDataManager.instance?.Playerdata;
            for (int i = 0; i < AllHeroes.Length; i++)
            {
                var ct = AllHeroes[i];
                bool implemented = true;
                if (pld != null)
                {
                    try { implemented = pld.IsCharacterImplemented(ct); } catch { }
                }
                if (!implemented) continue;
                if (resolver == null || resolver.IsHeroUnlockedForParty(ct))
                    list.Add(ct);
            }
            if (list.Count == 0) list.Add(eCharacterType.BASIC);
            return list;
        }

        public static string GetHeroTraitsSummary(eCharacterType hero)
        {
            try
            {
                var rm = ResourceManager.Instance;
                var cd = rm?.GetCharacterDataByType(hero);
                if (cd == null) return "";

                var parts = new List<string>();
                if (cd.InitialHPChange != 0)
                    parts.Add($"HP {(cd.InitialHPChange > 0 ? "+" : "")}{cd.InitialHPChange}");

                int skills = Math.Min(2, cd.GetSkillCount());
                for (int i = 0; i < skills; i++)
                {
                    string sName = cd.GetSkillLocalizationName(i);
                    if (!string.IsNullOrWhiteSpace(sName))
                        parts.Add(sName.Trim());
                }

                var startTowers = cd.GetStartingTower();
                if (startTowers != null && startTowers.Count > 0)
                    parts.Add(GetItemShortName(startTowers[0]));

                return string.Join("  ·  ", parts);
            }
            catch
            {
                return "";
            }
        }

        public static List<SelectableItemInfo> GetSelectableTowers()
        {
            var list = new List<SelectableItemInfo>();
            try
            {
                var rm = ResourceManager.Instance;
                var towers = rm?.GetAllTowerSettingData();
                if (towers != null)
                {
                    var seen = new HashSet<eItemType>();
                    for (int i = 0; i < towers.Count; i++)
                    {
                        var td = towers[i];
                        if (td == null || td.itemType == eItemType.NONE || !td.IsInGame()) continue;
                        if (!seen.Add(td.itemType)) continue;
                        list.Add(new SelectableItemInfo
                        {
                            ItemType = td.itemType,
                            DisplayName = GetItemShortName(td.itemType),
                            Icon = td.GetCardIcon(),
                            Tag = FormatTowerSize(td.TowerSizeType)
                        });
                    }
                }
            }
            catch (Exception ex)
            {
                CoopLogger.Debug(LogCategory.Sync, $"GetSelectableTowers warning: {ex.Message}");
            }
            return list;
        }

        public static List<SelectableItemInfo> GetSelectableBlocks()
        {
            var list = new List<SelectableItemInfo>();
            try
            {
                var rm = ResourceManager.Instance;
                var panels = rm?.GetAllPanelSettingData();
                if (panels != null)
                {
                    var seen = new HashSet<eItemType>();
                    for (int i = 0; i < panels.Count; i++)
                    {
                        var pd = panels[i];
                        if (pd == null || pd.itemType == eItemType.NONE || !pd.IsInGame() || pd.IsMechanicOnly || pd.IsTwisted) continue;
                        if (!seen.Add(pd.itemType)) continue;
                        list.Add(new SelectableItemInfo
                        {
                            ItemType = pd.itemType,
                            DisplayName = GetItemShortName(pd.itemType),
                            Icon = pd.GetCardIcon(),
                            Tag = $"{Math.Max(1, pd.GetBaseBlockCount())}x"
                        });
                    }
                }
            }
            catch (Exception ex)
            {
                CoopLogger.Debug(LogCategory.Sync, $"GetSelectableBlocks warning: {ex.Message}");
            }
            return list;
        }

        public static PlayerLoadoutChoice BuildHeroDefaultLoadout(eCharacterType hero, int seed)
        {
            var result = new PlayerLoadoutChoice
            {
                UseHeroDefault = true,
                IsConfirmed = true
            };

            try
            {
                var rm = ResourceManager.Instance;
                if (rm != null)
                {
                    var cd = rm.GetCharacterDataByType(hero) ?? rm.GetCharacterDataByType(eCharacterType.BASIC);
                    if (cd != null)
                    {
                        var fixedTowers = cd.GetStartingTower();
                        if (fixedTowers != null)
                        {
                            for (int i = 0; i < fixedTowers.Count && result.Towers.Count < PlayerLoadoutChoice.MaxStartingTowers; i++)
                            {
                                var tt = fixedTowers[i];
                                if (tt != eItemType.NONE && !result.Towers.Contains(tt))
                                    result.Towers.Add(tt);
                            }
                        }

                        var prevState = UnityEngine.Random.state;
                        try
                        {
                            UnityEngine.Random.InitState(seed != 0 ? seed : 424242);
                            var exclude = new Il2CppSystem.Collections.Generic.List<eItemType>();
                            for (int i = 0; i < result.Towers.Count; i++) exclude.Add(result.Towers[i]);

                            var startingSet = cd.GetStartingTowerSet(exclude);
                            if (startingSet != null)
                            {
                                for (int i = 0; i < startingSet.Count && result.Towers.Count < PlayerLoadoutChoice.MaxStartingTowers; i++)
                                {
                                    var td = startingSet[i];
                                    if (td != null && td.itemType != eItemType.NONE && !result.Towers.Contains(td.itemType))
                                        result.Towers.Add(td.itemType);
                                }
                            }

                            var tetrisSet = cd.GetStartingTetrisSet(seed != 0 ? seed : 424242);
                            if (tetrisSet != null)
                            {
                                for (int i = 0; i < tetrisSet.Count && result.Blocks.Count < PlayerLoadoutChoice.MaxStartingBlocks; i++)
                                {
                                    var tc = tetrisSet[i];
                                    if (tc != null && tc.ItemType != eItemType.NONE)
                                        result.Blocks.Add(tc.ItemType);
                                }
                            }
                        }
                        finally
                        {
                            UnityEngine.Random.state = prevState;
                        }
                    }

                    // Fill fallbacks from ResourceManager if CharacterSettingData returned fewer items
                    if (result.Towers.Count == 0)
                    {
                        var allT = rm.GetAllTowerSettingData();
                        if (allT != null)
                        {
                            for (int i = 0; i < allT.Count && result.Towers.Count < 3; i++)
                            {
                                var td = allT[i];
                                if (td != null && td.itemType != eItemType.NONE && td.IsInGame() && !td.IsUtilityTower && !result.Towers.Contains(td.itemType))
                                    result.Towers.Add(td.itemType);
                            }
                        }
                    }

                    if (result.Blocks.Count < PlayerLoadoutChoice.MinStartingBlocks)
                    {
                        var allP = rm.GetAllPanelSettingData();
                        if (allP != null && allP.Count > 0)
                        {
                            int idx = 0;
                            int guard = 0;
                            while (result.Blocks.Count < 8 && guard++ < 48)
                            {
                                var pd = allP[idx % allP.Count];
                                idx++;
                                if (pd != null && pd.itemType != eItemType.NONE && pd.IsInGame() && !pd.IsMechanicOnly && !pd.IsTwisted)
                                    result.Blocks.Add(pd.itemType);
                            }
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                CoopLogger.Warn(LogCategory.Sync, $"BuildHeroDefaultLoadout fallback: {ex.Message}");
            }

            return result;
        }

        public static PlayerLoadoutChoice ResolveEffectiveLoadout(PlayerLoadoutChoice chosen, eCharacterType hostHero, int seed)
        {
            if (chosen == null || chosen.UseHeroDefault || chosen.Towers.Count == 0 || chosen.Blocks.Count == 0)
                return BuildHeroDefaultLoadout(hostHero, seed);

            var copy = chosen.Clone();
            if (copy.Towers.Count == 0 || copy.Blocks.Count == 0)
            {
                var def = BuildHeroDefaultLoadout(hostHero, seed);
                if (copy.Towers.Count == 0) copy.Towers.AddRange(def.Towers);
                if (copy.Blocks.Count == 0) copy.Blocks.AddRange(def.Blocks);
            }
            return copy;
        }

        public static void HydrateGameplayData(GameplayData gd, eCharacterType hostHero, PlayerLoadoutChoice chosenLoadout, int seed, bool forceOverwriteInventory = true)
        {
            if (gd == null) return;
            var rm = ResourceManager.Instance;
            if (rm == null) return;

            var effective = ResolveEffectiveLoadout(chosenLoadout, hostHero, seed);

            using (NetworkScope.Enter())
            {
                try
                {
                    if (hostHero != eCharacterType.NONE)
                        gd.characterType = hostHero;

                    var cd = rm.GetCharacterDataByType(gd.characterType) ?? rm.GetCharacterDataByType(eCharacterType.BASIC);

                    if (forceOverwriteInventory || gd.list_LoadoutTowerData == null || gd.list_LoadoutTowerData.Count == 0)
                    {
                        gd.list_LoadoutTowerData ??= new Il2CppSystem.Collections.Generic.List<TowerIngameData>();
                        gd.list_CollectedTowerData ??= new Il2CppSystem.Collections.Generic.List<TowerIngameData>();
                        gd.list_LoadoutTowerData.Clear();
                        gd.list_CollectedTowerData.Clear();

                        for (int i = 0; i < effective.Towers.Count; i++)
                        {
                            var tType = effective.Towers[i];
                            if (tType == eItemType.NONE) continue;
                            gd.list_LoadoutTowerData.Add(new TowerIngameData(tType, 1));
                            gd.list_CollectedTowerData.Add(new TowerIngameData(tType, 1));
                        }
                    }

                    if (forceOverwriteInventory || gd.list_ItemStorage == null || gd.list_ItemStorage.Count == 0)
                    {
                        gd.list_ItemStorage ??= new Il2CppSystem.Collections.Generic.List<CardData>();
                        gd.list_ItemStorage.Clear();

                        var createdTetris = new Il2CppSystem.Collections.Generic.List<TetrisCardData>();
                        for (int i = 0; i < effective.Blocks.Count; i++)
                        {
                            var bType = effective.Blocks[i];
                            if (bType == eItemType.NONE) continue;
                            var setting = rm.GetItemDataByType(bType);
                            if (setting == null) continue;
                            var tc = TetrisCardData.CreateCardData(setting, false);
                            if (tc != null)
                            {
                                tc.seed = unchecked(seed ^ (int)(((uint)i * 2654435761u) & 0x7FFFFFFFu));
                                tc.EnsureRuntimeCardId();
                                gd.list_ItemStorage.Add(tc);
                                createdTetris.Add(tc);
                            }
                        }

                        if (cd != null && createdTetris.Count > 0)
                        {
                            try
                            {
                                var runes = cd.GetStartingRunes();
                                if (runes != null)
                                {
                                    for (int i = 0; i < runes.Count; i++)
                                    {
                                        if (runes[i] != eItemType.NONE)
                                            cd.AddRuneToSelectedBlock(createdTetris, i % createdTetris.Count, runes[i]);
                                    }
                                }
                            }
                            catch { }
                        }
                    }

                    if (forceOverwriteInventory && cd != null)
                    {
                        try
                        {
                            var relics = cd.GetStartingRelic();
                            if (relics != null)
                            {
                                gd.list_CollectedRelic ??= new Il2CppSystem.Collections.Generic.List<eItemType>();
                                for (int i = 0; i < relics.Count; i++)
                                {
                                    var rel = relics[i];
                                    if (rel != eItemType.NONE && !gd.IsHaveRelic(rel))
                                        gd.list_CollectedRelic.Add(rel);
                                }
                            }
                        }
                        catch { }

                        try { gd.InitializeHP(); } catch { }
                    }

                    CoopLogger.Info(LogCategory.Sync, $"Hydrated loadout ({gd.characterType}): {gd.list_LoadoutTowerData?.Count ?? 0} towers, {gd.list_ItemStorage?.Count ?? 0} cards.");
                }
                catch (Exception ex)
                {
                    CoopLogger.Warn(LogCategory.Sync, $"HydrateGameplayData warning: {ex.Message}");
                }
            }
        }

        // Safety net on MainGame load so no player can enter Wave 1 with an empty deck or tower bar.
        public static void EnsureBattleDeckAndTowersHydrated(eCharacterType hostHero, PlayerLoadoutChoice fallbackLoadout, int seed, bool allowLocalInitialDraw)
        {
            var gd = GameDataManager.instance?.GameplayData;
            var mg = MainGame.Instance;
            var ig = mg?.IngameData;
            if (gd == null) return;

            bool needsTowers = gd.list_LoadoutTowerData == null || gd.list_LoadoutTowerData.Count == 0;
            bool needsStorage = gd.list_ItemStorage == null || gd.list_ItemStorage.Count == 0;

            if (needsTowers || needsStorage)
            {
                CoopLogger.Warn(LogCategory.Sync, $"Detected empty battle loadout (towers={gd.list_LoadoutTowerData?.Count ?? 0}, storage={gd.list_ItemStorage?.Count ?? 0}); hydrating from co-op loadout.");
                HydrateGameplayData(gd, hostHero, fallbackLoadout, seed, forceOverwriteInventory: false);
            }

            using (NetworkScope.Enter())
            {
                try
                {
                    var towerListUi = GameRefs.GetPlayerTowerList();
                    if (towerListUi != null && gd.list_LoadoutTowerData != null && gd.list_LoadoutTowerData.Count > 0)
                        towerListUi.UpdateCards(gd.list_LoadoutTowerData);
                    GameRefs.SendEvent0(eGameEvents.OnTowerCardChanged);
                }
                catch { }

                if (ig != null)
                {
                    try
                    {
                        int totalCards = ig.GetDeckCardCount() + ig.GetHandCardCount() + ig.GetDiscardCardCount();
                        if (totalCards == 0 && gd.list_ItemStorage != null && gd.list_ItemStorage.Count > 0)
                        {
                            ig.OnRequestResetDeckFromStorage();
                            ig.ShuffleDeck();
                            CoopLogger.Info(LogCategory.Sync, $"Hydrated battle deck from storage ({ig.GetDeckCardCount()} cards).");
                        }

                        if (allowLocalInitialDraw && ig.GetHandCardCount() == 0 && ig.GetDeckCardCount() > 0)
                        {
                            int drawCount = Math.Clamp(gd.itemCardLimit > 0 ? gd.itemCardLimit : 4, 2, 6);
                            ig.DrawCardFromDeck(drawCount);
                            CoopLogger.Info(LogCategory.Sync, $"Drew initial hand ({ig.GetHandCardCount()} cards) for local player.");
                        }
                    }
                    catch (Exception ex)
                    {
                        CoopLogger.Warn(LogCategory.Sync, $"EnsureBattleDeckAndTowersHydrated deck warning: {ex.Message}");
                    }
                }
            }
        }

        public static string GetItemShortName(eItemType t)
        {
            if (t == eItemType.NONE) return "-";
            try
            {
                string loc = ResourceManager.Instance?.GetItemDataByType(t)?.GetLocNameString(false);
                if (!string.IsNullOrWhiteSpace(loc)) return loc.Trim();
            }
            catch { }

            string raw = t.ToString();
            int idx = raw.IndexOf('_', 1);
            return idx > 0 && idx + 1 < raw.Length
                ? raw.Substring(idx + 1).Replace("_TOWER", "").Replace("PANEL_", "").Replace('_', ' ')
                : raw;
        }

        private static string FormatTowerSize(eTowerSizeType sz) => sz switch
        {
            eTowerSizeType._1x1 => "1x1",
            eTowerSizeType._2x2 => "2x2",
            eTowerSizeType._1x2 => "1x2",
            eTowerSizeType._1x3 => "1x3",
            eTowerSizeType._3x3 => "3x3",
            _ => sz.ToString().TrimStart('_')
        };
    }
}
