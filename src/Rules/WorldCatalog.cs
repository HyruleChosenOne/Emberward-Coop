using System;
using System.Collections.Generic;
using EmberwardCoop.Localization;
using Il2CppInterop.Runtime.InteropTypes.Arrays;
using UnityEngine;

namespace EmberwardCoop.Rules
{
    public sealed class WorldEntryInfo
    {
        public eWorldType WorldType;
        public string NameKey;
        public string DescKey;
        public string HazardsKey;
        public string WavesLabel;
        public int MapSteps;
        public Color FallbackColor;
        public bool IsUnlocked;
        public bool IsCleared;
    }

    public sealed class EndlessStageEntry
    {
        public string SceneName;
        public eWorldType WorldType;
        public eEndlessModeType EndlessType;
        public string DisplayName;
        public string Subtitle;
        public string Description;
        public int SubStageNumber;
    }

    public static class WorldCatalog
    {
        private static readonly WorldEntryInfo[] BaseWorlds =
        {
            new WorldEntryInfo
            {
                WorldType = eWorldType.WORLD_1_FOREST,
                NameKey = "world.forest.name",
                DescKey = "world.forest.desc",
                HazardsKey = "world.forest.hazards",
                WavesLabel = "6–8",
                MapSteps = 7,
                FallbackColor = new Color(0.28f, 0.78f, 0.42f, 1f)
            },
            new WorldEntryInfo
            {
                WorldType = eWorldType.WORLD_2_DESERT,
                NameKey = "world.desert.name",
                DescKey = "world.desert.desc",
                HazardsKey = "world.desert.hazards",
                WavesLabel = "7–9",
                MapSteps = 7,
                FallbackColor = new Color(0.94f, 0.66f, 0.24f, 1f)
            },
            new WorldEntryInfo
            {
                WorldType = eWorldType.WORLD_3_MOUNTAIN,
                NameKey = "world.mountain.name",
                DescKey = "world.mountain.desc",
                HazardsKey = "world.mountain.hazards",
                WavesLabel = "8–10",
                MapSteps = 7,
                FallbackColor = new Color(0.36f, 0.78f, 0.98f, 1f)
            },
            new WorldEntryInfo
            {
                WorldType = eWorldType.WORLD_4_DEPTH,
                NameKey = "world.depth.name",
                DescKey = "world.depth.desc",
                HazardsKey = "world.depth.hazards",
                WavesLabel = "9–11",
                MapSteps = 7,
                FallbackColor = new Color(0.62f, 0.44f, 0.96f, 1f)
            },
            new WorldEntryInfo
            {
                WorldType = eWorldType.WORLD_5_COSMO,
                NameKey = "world.cosmo.name",
                DescKey = "world.cosmo.desc",
                HazardsKey = "world.cosmo.hazards",
                WavesLabel = "10–12",
                MapSteps = 7,
                FallbackColor = new Color(0.92f, 0.36f, 0.82f, 1f)
            },
            new WorldEntryInfo
            {
                WorldType = eWorldType.WORLD_100_CHAOS_REALM,
                NameKey = "world.chaos.name",
                DescKey = "world.chaos.desc",
                HazardsKey = "world.chaos.hazards",
                WavesLabel = "12+",
                MapSteps = 9,
                FallbackColor = new Color(0.98f, 0.28f, 0.28f, 1f)
            }
        };

        private static readonly (eWorldType world, string[] scenes)[] DefaultEndlessWorldStages =
        {
            (eWorldType.WORLD_1_FOREST,   new[] { "EnvScene_018", "EnvScene_019", "EnvScene_020", "EnvScene_017" }),
            (eWorldType.WORLD_2_DESERT,   new[] { "EnvScene_109", "EnvScene_112", "EnvScene_107", "EnvScene_116" }),
            (eWorldType.WORLD_3_MOUNTAIN, new[] { "EnvScene_206", "EnvScene_211", "EnvScene_214", "EnvScene_216" }),
            (eWorldType.WORLD_4_DEPTH,    new[] { "EnvScene_309", "EnvScene_313", "EnvScene_315", "EnvScene_312" }),
            (eWorldType.WORLD_5_COSMO,    new[] { "EnvScene_408", "EnvScene_403", "EnvScene_405", "EnvScene_407" })
        };

        public static readonly CoopRunMode[] AllRunModes =
        {
            CoopRunMode.Campaign,
            CoopRunMode.Endless,
            CoopRunMode.EnigmaSanctum
        };

        public static readonly eWorldType[] AllBaseWorldTypes =
        {
            eWorldType.WORLD_1_FOREST,
            eWorldType.WORLD_2_DESERT,
            eWorldType.WORLD_3_MOUNTAIN,
            eWorldType.WORLD_4_DEPTH,
            eWorldType.WORLD_5_COSMO,
            eWorldType.WORLD_100_CHAOS_REALM
        };

        public static readonly eGameDifficultyType[] AllDifficulties =
        {
            eGameDifficultyType.CASUAL,
            eGameDifficultyType.NORMAL,
            eGameDifficultyType.HEROIC,
            eGameDifficultyType.NIGHTMARE
        };

        public static int GetBaseWorldIndex(eWorldType world)
        {
            for (int i = 0; i < AllBaseWorldTypes.Length; i++)
                if (AllBaseWorldTypes[i] == world) return i;
            return -1;
        }

        public static List<CoopRunMode> GetPartySelectableRunModes(PartyUnlockResolver resolver)
        {
            var result = new List<CoopRunMode>();
            for (int i = 0; i < AllRunModes.Length; i++)
            {
                var m = AllRunModes[i];
                if (resolver == null || resolver.IsRunModeUnlockedForParty(m))
                    result.Add(m);
            }
            if (result.Count == 0)
                result.Add(CoopRunMode.Campaign);
            return result;
        }

        public static string GetRunModeDisplayName(CoopRunMode mode)
        {
            if (mode == CoopRunMode.EnigmaSanctum)
            {
                string native = TryGetGameLoc("SELECT_DIFFICULTY_EVENT_SANCTUM_OF_ENIGMA");
                if (!string.IsNullOrWhiteSpace(native)) return native;
                return CoopLoc.T("mode.enigma_sanctum");
            }
            if (mode == CoopRunMode.Endless)
            {
                string native = TryGetGameLoc("SELECT_DIFFICULTY_ENDLESS_MODE_TAB_TITLE")
                             ?? TryGetGameLoc("ENDLESS_MODE");
                if (!string.IsNullOrWhiteSpace(native)) return native;
                return CoopLoc.T("mode.endless");
            }
            return CoopLoc.T("mode.campaign");
        }

        public static string GetRunModeDescription(CoopRunMode mode)
        {
            if (mode == CoopRunMode.EnigmaSanctum)
            {
                string native = TryGetGameLoc("SELECT_DIFFICULTY_EVENT_SANCTUM_OF_ENIGMA_DESC");
                if (!string.IsNullOrWhiteSpace(native)) return native;
                return CoopLoc.T("mode.enigma_sanctum_desc");
            }
            if (mode == CoopRunMode.Endless)
            {
                return CoopLoc.T("mode.endless_desc");
            }
            return CoopLoc.T("mode.campaign_desc");
        }

        public static List<EndlessStageEntry> GetEndlessStagesForWorld(eWorldType world)
        {
            var result = new List<EndlessStageEntry>();
            string worldName = GetWorldDisplayName(world);
            string worldDesc = CoopLoc.T(GetWorldInfo(world).DescKey);

            string[] scenes = null;
            for (int i = 0; i < DefaultEndlessWorldStages.Length; i++)
            {
                if (DefaultEndlessWorldStages[i].world == world)
                {
                    scenes = DefaultEndlessWorldStages[i].scenes;
                    break;
                }
            }
            if (scenes == null || scenes.Length == 0)
                scenes = DefaultEndlessWorldStages[0].scenes;

            for (int i = 0; i < scenes.Length; i++)
            {
                int stageNum = i + 1;
                result.Add(new EndlessStageEntry
                {
                    SceneName = scenes[i],
                    WorldType = world,
                    EndlessType = eEndlessModeType.BASIC_ENDLESS,
                    SubStageNumber = stageNum,
                    DisplayName = $"{worldName} — {CoopLoc.F("mode.stage_num", stageNum)}",
                    Subtitle = CoopLoc.T("mode.endless_waves"),
                    Description = worldDesc
                });
            }

            // Include Random Endless Challenge option at the end of the stage list
            result.Add(new EndlessStageEntry
            {
                SceneName = scenes[0],
                WorldType = world,
                EndlessType = eEndlessModeType.RANDOM_ENDLESS_CHALLENGE,
                SubStageNumber = scenes.Length + 1,
                DisplayName = $"{worldName} — {CoopLoc.T("mode.random_endless")}",
                Subtitle = CoopLoc.T("mode.endless_waves"),
                Description = CoopLoc.T("mode.random_endless_desc")
            });

            return result;
        }

        public static EndlessStageEntry GetEnigmaSanctumEntry()
        {
            return new EndlessStageEntry
            {
                SceneName = "EnvScene_907",
                WorldType = eWorldType.WORLD_1_FOREST,
                EndlessType = eEndlessModeType.ENIGMA_SANCTUM_CASUAL,
                SubStageNumber = 1,
                DisplayName = GetRunModeDisplayName(CoopRunMode.EnigmaSanctum),
                Subtitle = CoopLoc.T("mode.enigma_sanctum_waves"),
                Description = GetRunModeDescription(CoopRunMode.EnigmaSanctum)
            };
        }

        private static string TryGetGameLoc(string key)
        {
            try
            {
                var lm = LocalizationManager.Instance;
                if (lm == null) return null;
                string[] tables = { "UI", "Default", "Game", "Map" };
                for (int i = 0; i < tables.Length; i++)
                {
                    if (lm.HasString(tables[i], key))
                    {
                        string val = lm.GetString(tables[i], key, (Il2CppReferenceArray<Il2CppSystem.Object>)null);
                        if (!string.IsNullOrWhiteSpace(val) && !string.Equals(val, key, StringComparison.OrdinalIgnoreCase))
                            return val.Trim();
                    }
                }
            }
            catch { }
            return null;
        }

        public static string GetItemShortName(eItemType t)
        {
            try
            {
                var item = ResourceManager.Instance?.GetItemDataByType(t);
                string loc = item?.GetLocNameString(false);
                if (!string.IsNullOrWhiteSpace(loc)) return loc.Trim();
            }
            catch { }

            string s = t.ToString();
            if (s.StartsWith("TOWER_", StringComparison.OrdinalIgnoreCase)) s = s.Substring(6);
            else if (s.StartsWith("TETRIS_", StringComparison.OrdinalIgnoreCase)) s = s.Substring(7);
            s = s.Replace('_', ' ').ToLowerInvariant();
            if (s.Length == 0) return "?";
            var chars = s.ToCharArray();
            chars[0] = char.ToUpperInvariant(chars[0]);
            for (int i = 1; i < chars.Length; i++)
                if (chars[i - 1] == ' ') chars[i] = char.ToUpperInvariant(chars[i]);
            return new string(chars);
        }

        public static List<eGameDifficultyType> GetPartySelectableDifficulties(PartyUnlockResolver resolver)
        {
            var result = new List<eGameDifficultyType>();
            for (int i = 0; i < AllDifficulties.Length; i++)
            {
                var d = AllDifficulties[i];
                if (resolver == null || resolver.IsDifficultyUnlockedForParty(d))
                    result.Add(d);
            }
            if (result.Count == 0)
            {
                result.Add(eGameDifficultyType.CASUAL);
                result.Add(eGameDifficultyType.NORMAL);
            }
            return result;
        }

        public static List<WorldEntryInfo> GetHostSelectableWorlds(eGameDifficultyType difficulty, PartyUnlockResolver resolver = null, CoopRunMode runMode = CoopRunMode.Campaign)
        {
            var result = new List<WorldEntryInfo>();
            var pld = GameDataManager.instance?.Playerdata;

            for (int i = 0; i < BaseWorlds.Length; i++)
            {
                var bw = BaseWorlds[i];
                // Endless Mode only has stage pools for Worlds 1..5
                if (runMode == CoopRunMode.Endless && bw.WorldType == eWorldType.WORLD_100_CHAOS_REALM)
                    continue;

                bool implemented = true;
                bool unlocked = bw.WorldType == eWorldType.WORLD_1_FOREST;
                bool cleared = false;

                if (pld != null)
                {
                    using (PartyUnlockResolver.EnterRealUnlockRead())
                    {
                        try
                        {
                            implemented = pld.IsWorldImplemented(bw.WorldType);
                            unlocked = unlocked
                                || pld.IsWorldUnlocked(difficulty, bw.WorldType)
                                || pld.IsWorldUnlockedInAnyDifficulty(bw.WorldType);
                            cleared = pld.IsWorldCleared(difficulty, bw.WorldType)
                                || pld.IsWorldClearedInAnyDifficulty(bw.WorldType);
                        }
                        catch { }
                    }
                }

                if (resolver != null)
                {
                    unlocked = unlocked || resolver.IsWorldUnlockedForParty(bw.WorldType);
                    cleared = cleared || resolver.PartyBaseline.IsWorldClearedBitSet(i);
                }

                if (!implemented) continue;

                if (unlocked)
                {
                    result.Add(new WorldEntryInfo
                    {
                        WorldType = bw.WorldType,
                        NameKey = bw.NameKey,
                        DescKey = bw.DescKey,
                        HazardsKey = bw.HazardsKey,
                        WavesLabel = bw.WavesLabel,
                        MapSteps = bw.MapSteps,
                        FallbackColor = bw.FallbackColor,
                        IsUnlocked = true,
                        IsCleared = cleared
                    });
                }
            }

            if (result.Count == 0)
            {
                var first = BaseWorlds[0];
                result.Add(new WorldEntryInfo
                {
                    WorldType = first.WorldType,
                    NameKey = first.NameKey,
                    DescKey = first.DescKey,
                    HazardsKey = first.HazardsKey,
                    WavesLabel = first.WavesLabel,
                    MapSteps = first.MapSteps,
                    FallbackColor = first.FallbackColor,
                    IsUnlocked = true,
                    IsCleared = false
                });
            }
            return result;
        }

        public static WorldEntryInfo GetWorldInfo(eWorldType world)
        {
            for (int i = 0; i < BaseWorlds.Length; i++)
                if (BaseWorlds[i].WorldType == world) return BaseWorlds[i];
            return BaseWorlds[0];
        }

        public static string GetWorldDisplayName(eWorldType world)
        {
            var info = GetWorldInfo(world);
            return CoopLoc.T(info.NameKey);
        }

        public static Color GetWorldAccentColor(eWorldType world)
        {
            try
            {
                Color c = eWorldTypeExtensions.GetWorldColor(world);
                if (c.a > 0.1f && (c.r + c.g + c.b) > 0.15f) return c;
            }
            catch { }
            return GetWorldInfo(world).FallbackColor;
        }

        public static string GetDifficultyDisplayName(eGameDifficultyType diff) => diff switch
        {
            eGameDifficultyType.CASUAL    => CoopLoc.T("diff.casual"),
            eGameDifficultyType.NORMAL    => CoopLoc.T("diff.normal"),
            eGameDifficultyType.HEROIC    => CoopLoc.T("diff.heroic"),
            eGameDifficultyType.NIGHTMARE => CoopLoc.T("diff.nightmare"),
            _                             => diff.ToString()
        };

        public static Color GetDifficultyColor(eGameDifficultyType diff)
        {
            try
            {
                Color c = GameDifficultyTypeExtension.GetColor(diff);
                if (c.a > 0.1f && (c.r + c.g + c.b) > 0.15f) return c;
            }
            catch { }
            return diff switch
            {
                eGameDifficultyType.CASUAL    => new Color(0.42f, 0.88f, 0.56f, 1f),
                eGameDifficultyType.NORMAL    => new Color(0.96f, 0.78f, 0.28f, 1f),
                eGameDifficultyType.HEROIC    => new Color(0.98f, 0.45f, 0.20f, 1f),
                eGameDifficultyType.NIGHTMARE => new Color(0.92f, 0.24f, 0.44f, 1f),
                _                             => Color.white
            };
        }

        public static string GetPresetDisplayName(SessionPreset preset) => preset switch
        {
            SessionPreset.BalancedCoop     => CoopLoc.T("preset.balanced"),
            SessionPreset.Casual           => CoopLoc.T("preset.casual"),
            SessionPreset.TacticalHardcore => CoopLoc.T("preset.hardcore"),
            SessionPreset.Chaos            => CoopLoc.T("preset.chaos"),
            _                              => "Custom"
        };

        public static string BuildActiveRulesSummary(LobbySetupState state)
        {
            if (state == null) return "";
            var s = state.Settings;
            var parts = new List<string>
            {
                $"{CoopLoc.T("lobby.game_mode")} {GetRunModeDisplayName(state.SelectedRunMode)}"
            };

            if (state.SelectedRunMode == CoopRunMode.Campaign)
                parts.Add($"{CoopLoc.T("lobby.select_difficulty")} {GetDifficultyDisplayName(state.DifficultyTier)}");

            parts.Add($"{CoopLoc.T("settings.enemy_hp")} {Mathf.RoundToInt(s.EnemyHpMultiplier * 100)}%");

            if (Math.Abs(s.WaveDensityMultiplier - 1.0f) > 0.01f)
                parts.Add($"{CoopLoc.T("settings.wave_density")} {s.WaveDensityMultiplier:0.00}x");

            if (Math.Abs(s.EnemySpeedMultiplier - 1.0f) > 0.01f)
                parts.Add($"{CoopLoc.T("settings.enemy_speed")} {s.EnemySpeedMultiplier:0.00}x");

            if (Math.Abs(s.BossHpArmorMultiplier - 1.0f) > 0.01f)
                parts.Add($"{CoopLoc.T("settings.boss_mult")} {s.BossHpArmorMultiplier:0.00}x");

            if (s.EconomyMode == CoopEconomyMode.SeparateResources)
            {
                parts.Add($"{CoopLoc.T("settings.wave_income")} {Mathf.RoundToInt(s.WaveIncomeMultiplier * 100)}%");
                string giftStr = !s.AllowResourceGifting
                    ? CoopLoc.T("settings.val_disabled")
                    : (s.GiftPerWaveCap > 0 ? $"{s.GiftPerWaveCap}g" : CoopLoc.T("settings.val_unlimited"));
                parts.Add($"{CoopLoc.T("settings.gifting")} {giftStr}");
            }

            if (s.UnlockAllMapsModes || PartyUnlockResolver.IsCoopUnlockAllEffective())
                parts.Add(CoopLoc.T("lobby.unlock_all_badge"));

            if (s.BlockPacing == BlockPacingRule.SharedTurnBlockCap)
                parts.Add($"{CoopLoc.T("settings.block_pacing")} {s.SharedTurnBlockCap}");

            if (s.SpeedControl == SpeedControlRule.HostOnly)
                parts.Add($"{CoopLoc.T("settings.speed_ctrl")} {CoopLoc.T("settings.val_host_only")}");

            return string.Join("  |  ", parts);
        }
    }
}
