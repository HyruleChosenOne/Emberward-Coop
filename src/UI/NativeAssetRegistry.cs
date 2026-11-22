using System;
using System.Collections.Generic;
using EmberwardCoop.Core;
using EmberwardCoop.Localization;
using EmberwardCoop.Presence;
using EmberwardCoop.Rules;
using Il2CppInterop.Runtime.InteropTypes.Arrays;
using TMPro;
using UnityEngine;

namespace EmberwardCoop.UI
{
    public sealed class HeroAvatarEntry
    {
        public string HeroId;
        public eCharacterType CharacterType;
        public string DisplayName;
        public Sprite PortraitSprite;
        public Color AccentColor;
    }

    public static class NativeAssetRegistry
    {
        private static readonly Dictionary<string, Sprite> _spritesByName = new Dictionary<string, Sprite>(StringComparer.OrdinalIgnoreCase);
        private static readonly Dictionary<string, Sprite> _slicedSpritesByName = new Dictionary<string, Sprite>(StringComparer.OrdinalIgnoreCase);
        private static readonly Dictionary<string, Sprite> _roleCache = new Dictionary<string, Sprite>(StringComparer.OrdinalIgnoreCase);
        private static readonly List<HeroAvatarEntry> _playableHeroes = new List<HeroAvatarEntry>();
        private static readonly Dictionary<string, HeroAvatarEntry> _heroesById = new Dictionary<string, HeroAvatarEntry>(StringComparer.OrdinalIgnoreCase);

        private static float _lastScanTime = -10f;
        private static string _lastScannedScene = "";
        private static bool _hasCompleteCoreAssets;

        public static TMP_FontAsset PrimaryFont { get; private set; }
        public static Material PrimaryFontMaterial { get; private set; }

        public static string ClickSoundAssetKey { get; private set; } = "";
        public static string ClickSoundName { get; private set; } = "";
        public static string HoverSoundAssetKey { get; private set; } = "";
        public static string HoverSoundName { get; private set; } = "";
        public static string OpenSoundAssetKey { get; private set; } = "";
        public static string OpenSoundName { get; private set; } = "";
        public static string CoinSoundAssetKey { get; private set; } = "";
        public static string CoinSoundName { get; private set; } = "";
        public static string SelectSoundAssetKey { get; private set; } = "";
        public static string SelectSoundName { get; private set; } = "";

        private static readonly string[] FallbackIconNames =
        {
            "EmberStone", "Flame_Basic", "Icon_ColorIcon_Coins", "itemicon_crown_0",
            "UICheckMark", "Ember", "Icon_Star", "White1px"
        };

        // Solid opaque 9-slice sprites verified against sharedassets0 and resources.assets.
        // Hollow frames (frame_stageframe_05_White2, frame_listframe_01_White2) have 0x0 centers and transparent interiors.
        private static readonly string[] WindowPanelNames =
        {
            "DialogBox_v3_Brown", "DialogBox_v3_Sand", "doubleOutlineBox_96x92", "UISprite", "Background", "White1px"
        };

        private static readonly string[] HudPanelNames =
        {
            "DialogBox_v3_Sand", "DialogBox_v3_Brown", "UISprite", "Background", "White1px"
        };

        private static readonly string[] SubPanelNames =
        {
            "UISprite", "Background", "doubleOutlineBox_96x92", "DialogBox_v3_Sand", "White1px"
        };

        private static readonly string[] WellPanelNames =
        {
            "UISprite", "Background", "DialogBox_v3_black", "White1px"
        };

        private static readonly string[] TintablePanelNames =
        {
            "UISprite", "Background", "doubleOutlineBox_96x92", "White1px"
        };

        private static readonly string[] ButtonNormalNames =
        {
            "UISprite", "Background", "doubleOutlineBox_96x92", "Bar_Big_01_Gray", "Button_Light_Brown_01", "White1px"
        };

        private static readonly string[] TabButtonNames =
        {
            "UISprite", "Background", "doubleOutlineBox_96x92", "White1px"
        };

        private static readonly string[] BadgePillNames =
        {
            "UISprite", "Background", "Bar_Big_01_Gray", "White1px"
        };

        private static readonly string[] DividerNames =
        {
            "White1px", "UISprite", "Background"
        };

        private static readonly string[] ScrollTrackNames =
        {
            "UISprite", "Background", "White1px"
        };

        private static readonly string[] ScrollHandleNames =
        {
            "UISprite", "Background", "White1px"
        };

        private static readonly string[] SpeechBubbleNames =
        {
            "DialogBox_v3_Sand", "UISprite", "Background", "White1px"
        };

        private static readonly string[] AvatarFrameNames =
        {
            "frame_iconframe_circle_00", "frame_iconframe_circle_01_n_White3", "frame_itemframe_02_White3_glow",
            "frame_itemframe_01_White2", "frame_iconframe_square_00_frame_WhiteGlow", "OutlineBorder_Octo"
        };

        private static readonly string[] AvatarFrameSelectedNames =
        {
            "frame_itemframe_02_White3_glow", "frame_iconframe_square_00_frame_WhiteGlow",
            "SelectionFrame_96", "frame_stageframe_05_s_yellow", "Frame_Type4_01_Glow"
        };

        private static readonly string[] StickerMedallionNames =
        {
            "Btn_IconButton_Circle_90", "frame_iconframe_circle_00", "RuneIcon_Frame_4",
            "OutlineBorder_Octo", "Btn_OtherButton_Hexagon01_Blue", "frame_itemframe_02_White3_glow"
        };

        private static readonly string[] CircleBadgeNames =
        {
            "Btn_IconButton_Circle_90", "Btn_IconButton_Circle_121_White", "frame_iconframe_circle_00",
            "OutlineBorder_Octo", "fx_glow", "EmberStone"
        };

        private static readonly string[] CheckboxOnNames =
        {
            "toggle_checkbox_on", "UICheckMark", "Pictoicon_symbol_check", "Check"
        };

        private static readonly string[] CheckboxOffNames =
        {
            "toggle_checkbox_off", "frame_basicframe_square_s", "UISprite"
        };

        private static readonly string[] CoinIconNames =
        {
            "Icon_ColorIcon_Coins", "Coin_128", "icon_coin_128", "itemicon_piggy", "Pictoicon_Coin_Star"
        };

        private static readonly string[] CrownIconNames =
        {
            "crown_3_NoBorder_128", "itemicon_crown_0", "Pictoicon_Crown", "AchievementIcon"
        };

        private static readonly string[] RefreshIconNames =
        {
            "function_icon_refresh", "Icon_White_128_Refresh_Png"
        };

        private static readonly string[] CheckIconNames =
        {
            "UICheckMark", "Pictoicon_symbol_check", "Pictoicon_Check", "Check"
        };

        private static readonly string[] GearIconNames =
        {
            "Icon_White_128_Setting_01", "Icon_GearWheels", "function_icon_magic"
        };

        private static readonly string[] EditBadgeNames =
        {
            "Icon_White_128_Setting_01", "Icon_GearWheels", "function_icon_refresh", "Pictoicon_symbol_plus", "Plus"
        };

        private static readonly string[] CloseIconNames =
        {
            "Pictoicon_mark_x", "IconClear"
        };

        private static readonly string[] SketchTileNames =
        {
            "SelectionFrame_64", "OutlineBorder", "frame_iconframe_square_00_frame_WhiteGlow",
            "frame_basicframe_square_s", "frame_itemframe_02_White3_glow"
        };

        private static readonly string[] SketchArrowNames =
        {
            "ArrowPointer", "Icon_WhiteIcon_Arrow_Up", "line_Arrow", "Pictoicon_Arrow_Fw", "set_icon_menu05_arrow"
        };

        private static readonly string[] PingRingNames =
        {
            "magicCircle", "fx_glow", "OutlineBorder_Octo", "frame_iconframe_circle_00", "Btn_IconButton_Circle_90"
        };

        private static readonly string[] PingBeaconNames =
        {
            "Gradient_WhiteToTransparent_3", "fx_glow", "ShineSquare", "Bar_Big_01"
        };

        private static readonly (eCharacterType type, string id, string locKey, Color defaultColor, string[] spriteFallbacks)[] KnownHeroes =
        {
            (eCharacterType.BASIC,         "BASIC",         "hero.basic",         new Color(0.95f, 0.72f, 0.30f, 1f), new[] { "Basic_Head", "Ember", "Flame_Basic" }),
            (eCharacterType.KNIGHT,        "KNIGHT",        "hero.knight",        new Color(0.76f, 0.84f, 0.96f, 1f), new[] { "Knight_Head", "Char_Knight", "Char_Knight_2" }),
            (eCharacterType.SCHOLAR,       "SCHOLAR",       "hero.scholar",       new Color(0.45f, 0.82f, 0.96f, 1f), new[] { "Scholar_Head", "Char_Scholar", "Char_Scholar_2" }),
            (eCharacterType.MERCHANT,      "MERCHANT",      "hero.merchant",      new Color(0.98f, 0.80f, 0.24f, 1f), new[] { "Merchant_Head", "Char_Merchant", "Char_Merchant_2" }),
            (eCharacterType.SORCERER,      "SORCERER",      "hero.sorcerer",      new Color(0.74f, 0.46f, 0.98f, 1f), new[] { "Mage_Head", "Char_Sorcerer", "Char_Sorcerer_2" }),
            (eCharacterType.WITCH,         "WITCH",         "hero.witch",         new Color(0.62f, 0.92f, 0.46f, 1f), new[] { "Witch_Head", "Char_Witch" }),
            (eCharacterType.CHUNK,         "CHUNK",         "hero.chunk",         new Color(0.92f, 0.56f, 0.28f, 1f), new[] { "Chunk_Head", "Char_Chunk", "Char_Chunk_2" }),
            (eCharacterType.TINY,          "TINY",          "hero.tiny",          new Color(0.98f, 0.62f, 0.74f, 1f), new[] { "Tiny_Head", "Char_Tiny", "Char_Tiny_2" }),
            (eCharacterType.TIME_MAGICIAN, "TIME_MAGICIAN", "hero.time_magician", new Color(0.44f, 0.92f, 0.88f, 1f), new[] { "TimeWiz_Head", "Char_Chrono" }),
            (eCharacterType.SCRAP_MASTER,  "SCRAP_MASTER",  "hero.scrap_master",  new Color(0.88f, 0.68f, 0.36f, 1f), new[] { "ScrapMaster_Head", "Char_ScrapMaster" }),
            (eCharacterType.JOKER,         "JOKER",         "hero.joker",         new Color(0.96f, 0.38f, 0.56f, 1f), new[] { "Joker_Head", "Char_Joker" }),
            (eCharacterType.KING,          "KING",          "hero.king",          new Color(0.98f, 0.84f, 0.34f, 1f), new[] { "King_Head", "King" })
        };

        private static readonly Dictionary<StickerId, string[]> StickerSpriteFallbacks = new Dictionary<StickerId, string[]>
        {
            { StickerId.ThumbsUp,  new[] { "Pictoicon_symbol_check", "Pictoicon_Check", "Icon_Trophy", "Icon_Star", "UICheckMark" } },
            { StickerId.Heart,     new[] { "heart_Gold", "Heart", "heart", "Pictoicon_Life_Add", "itemicon_clover_" } },
            { StickerId.Sweat,     new[] { "FireSpiritFace_5", "FireSpiritFace_6", "Chill", "Stun", "AnomalyLevel" } },
            { StickerId.MindBlown, new[] { "function_icon_magic", "Arcane", "Runes", "Rune_Lightning", "magicCircle" } },
            { StickerId.Cry,       new[] { "icon_skull", "Skull", "Pictoicon_Skull", "Vulnerable", "FireSpiritFace_6" } },
            { StickerId.Crown,     new[] { "crown_3_NoBorder_128", "itemicon_crown_0", "Pictoicon_Crown", "AchievementIcon" } },
            { StickerId.Coin,      new[] { "Icon_ColorIcon_Coins", "Coin_128", "icon_coin_128", "itemicon_piggy", "Pictoicon_Coin_Star" } },
            { StickerId.Fire,      new[] { "Flame_Blaze", "Burn", "Icon_Fire", "itemicon_fire", "Flame_Basic" } },
            { StickerId.Clock,     new[] { "Time", "Icon_Hourglass", "Talent_Clock", "Icon_Loading" } },
            { StickerId.Alert,     new[] { "alert", "warning", "IconWarning", "IconError", "itemicon_bomb_red", "hazard_Inv" } }
        };

        private static readonly Dictionary<PingKind, string[]> PingSpriteFallbacks = new Dictionary<PingKind, string[]>
        {
            { PingKind.BuildMazeHere,  new[] { "Pictoicon_block", "BlockCard", "Platform_Stone", "bricks" } },
            { PingKind.PlaceDpsHere,   new[] { "Pictoicon_Tower", "Talent_Tower_128", "Icon_White_128_Sword_02", "Damage" } },
            { PingKind.NeedGold,       new[] { "Icon_ColorIcon_Coins", "Coin_128", "itemicon_pouch_green", "itemicon_piggy" } },
            { PingKind.UpgradeOrFocus, new[] { "function_icon_target_1", "Pictoicon_shoot_focus_1", "Crit", "Icon_Levelup_Top_03" } },
            { PingKind.WatchLane,      new[] { "alert", "warning", "IconWarning", "itemicon_bomb_red", "hazard_Inv" } },
            { PingKind.NiceTower,      new[] { "Icon_Star", "itemicon_crown_0", "AchievementIcon", "UICheckMark" } },
            { PingKind.WatchPortal,    new[] { "itemicon_boss_green", "ClanIcon_Symbol_Boss_Large2", "Monster", "AnomalyLevel" } }
        };

        private static readonly Dictionary<eWorldType, string[]> WorldBannerFallbacks = new Dictionary<eWorldType, string[]>
        {
            { eWorldType.WORLD_1_FOREST,        new[] { "World_1", "World1", "BossStage_World1", "World1_Normal", "AchievementBackgroundUnlocked" } },
            { eWorldType.WORLD_2_DESERT,        new[] { "World_2", "World2", "BossStage_World2", "World2_Normal", "AchievementBackgroundUnlocked" } },
            { eWorldType.WORLD_3_MOUNTAIN,      new[] { "World_3", "World3_2", "BossStage_World3", "World3_Normal", "AchievementBackgroundUnlocked" } },
            { eWorldType.WORLD_4_DEPTH,         new[] { "World_4", "World4", "BossStage_World4", "World4_Normal", "AchievementBackgroundUnlocked" } },
            { eWorldType.WORLD_5_COSMO,         new[] { "World_5", "World5", "BossStage_World5", "World5_Normal", "AchievementBackgroundUnlocked" } },
            { eWorldType.WORLD_100_CHAOS_REALM, new[] { "World_100", "ChaosRealm", "AnomalyLevel", "EndlessMode" } }
        };

        private static readonly string[] EndlessBannerFallbacks =
        {
            "BG_EndlessMode", "RandomEndless", "EndlessMode", "World_1", "AchievementBackgroundUnlocked"
        };

        private static readonly string[] EnigmaSanctumBannerFallbacks =
        {
            "BG_Event_EnigmaSanctum", "EnigmaSanctum", "AnomalyLevel", "World_5", "AchievementBackgroundUnlocked"
        };

        public static void OnSceneChanged()
        {
            PruneDeadReferences();
            _hasCompleteCoreAssets = false;
            _lastScanTime = -10f;
            EnsureScanned(force: true);
        }

        public static void RefreshHeroDisplayNames()
        {
            for (int i = 0; i < _playableHeroes.Count; i++)
            {
                var h = _playableHeroes[i];
                h.DisplayName = ResolveHeroLocalizedName(h.CharacterType, h.HeroId);
            }
        }

        public static void EnsureScanned(bool force = false)
        {
            string scene = GameRefs.ActiveScene ?? "";
            float now = Time.unscaledTime;
            bool sceneChanged = scene != _lastScannedScene;

            if (!force && !sceneChanged && _hasCompleteCoreAssets && (now - _lastScanTime) < 2.0f)
                return;

            _lastScanTime = now;
            _lastScannedScene = scene;

            try
            {
                PruneDeadReferences();
                ScanLoadedSprites();
                ScanResourceManagerAssets();
                ScanFontsAndMaterials();
                ScanNativeSounds();
                BuildPlayableHeroCatalog();

                // Pin core UI panel sprites immediately on first scan so they stay loaded across scene transitions.
                Sprite winSp = ResolveRole("WindowPanel", WindowPanelNames, preferSliced: true);
                Sprite hudSp = ResolveRole("HudPanel", HudPanelNames, preferSliced: true);
                Sprite btnSp = ResolveRole("ButtonNormal", ButtonNormalNames, preferSliced: true);
                ResolveRole("SubPanel", SubPanelNames, preferSliced: true);
                ResolveRole("WellPanel", WellPanelNames, preferSliced: true);

                _hasCompleteCoreAssets =
                    winSp != null &&
                    hudSp != null &&
                    btnSp != null &&
                    PrimaryFont != null &&
                    _playableHeroes.Count > 0;
            }
            catch (Exception ex)
            {
                CoopLogger.Debug(LogCategory.UI, $"NativeAssetRegistry.EnsureScanned warning: {ex.Message}");
            }
        }

        private static void PruneDeadReferences()
        {
            var deadKeys = new List<string>();
            foreach (var kv in _spritesByName)
                if (kv.Value == null) deadKeys.Add(kv.Key);
            for (int i = 0; i < deadKeys.Count; i++) _spritesByName.Remove(deadKeys[i]);

            deadKeys.Clear();
            foreach (var kv in _slicedSpritesByName)
                if (kv.Value == null) deadKeys.Add(kv.Key);
            for (int i = 0; i < deadKeys.Count; i++) _slicedSpritesByName.Remove(deadKeys[i]);

            deadKeys.Clear();
            foreach (var kv in _roleCache)
                if (kv.Value == null) deadKeys.Add(kv.Key);
            for (int i = 0; i < deadKeys.Count; i++) _roleCache.Remove(deadKeys[i]);

            for (int i = _playableHeroes.Count - 1; i >= 0; i--)
            {
                if (_playableHeroes[i] == null || _playableHeroes[i].PortraitSprite == null)
                {
                    if (_playableHeroes[i] != null && !string.IsNullOrEmpty(_playableHeroes[i].HeroId))
                        _heroesById.Remove(_playableHeroes[i].HeroId);
                    _playableHeroes.RemoveAt(i);
                }
            }

            if (PrimaryFont == null) PrimaryFont = null;
            if (PrimaryFontMaterial == null) PrimaryFontMaterial = null;
        }

        private static void PinAssetAlive(Sprite sp)
        {
            if (sp == null) return;
            try
            {
                sp.hideFlags |= HideFlags.DontUnloadUnusedAsset;
                if (sp.texture != null)
                    sp.texture.hideFlags |= HideFlags.DontUnloadUnusedAsset;
            }
            catch { }
        }

        private static void RegisterSprite(Sprite sp, bool pinImmediately = false)
        {
            if (sp == null || string.IsNullOrEmpty(sp.name)) return;
            if (!_spritesByName.ContainsKey(sp.name) || _spritesByName[sp.name] == null)
                _spritesByName[sp.name] = sp;

            if (HasValid9SliceBorder(sp) && (!_slicedSpritesByName.ContainsKey(sp.name) || _slicedSpritesByName[sp.name] == null))
                _slicedSpritesByName[sp.name] = sp;

            if (pinImmediately)
                PinAssetAlive(sp);
        }

        private static void ScanLoadedSprites()
        {
            var allSprites = Resources.FindObjectsOfTypeAll<Sprite>();
            if (allSprites == null) return;

            for (int i = 0; i < allSprites.Length; i++)
            {
                var sp = allSprites[i];
                if (sp == null || string.IsNullOrEmpty(sp.name)) continue;
                RegisterSprite(sp);
            }
        }

        private static void ScanResourceManagerAssets()
        {
            try
            {
                var rm = ResourceManager.Instance;
                var chars = rm?.list_characterSettingData;
                if (chars != null)
                {
                    for (int i = 0; i < chars.Count; i++)
                    {
                        var cd = chars[i];
                        if (cd?.CharacterIcon != null)
                            RegisterSprite(cd.CharacterIcon, pinImmediately: true);
                    }
                }
            }
            catch { }

            try
            {
                var popups = Resources.FindObjectsOfTypeAll<UI_SelectCharacterPopup>();
                if (popups != null)
                {
                    for (int i = 0; i < popups.Length; i++)
                    {
                        var entries = popups[i]?.list_CharacterSelectEntries;
                        if (entries == null) continue;
                        for (int j = 0; j < entries.Count; j++)
                        {
                            var img = entries[j]?.image_Character;
                            if (img?.sprite != null)
                                RegisterSprite(img.sprite, pinImmediately: true);
                        }
                    }
                }
            }
            catch { }
        }

        private static void ScanFontsAndMaterials()
        {
            TMP_Text sourceTmp = null;
            var cp = GameRefs.GetCoinPage();
            if (cp?.text_ContinueProgress != null && cp.text_ContinueProgress.font != null)
                sourceTmp = cp.text_ContinueProgress;
            else if (cp?.text_Talents != null && cp.text_Talents.font != null)
                sourceTmp = cp.text_Talents;

            if (sourceTmp == null)
            {
                var ps = GameRefs.GetPlayerStatus();
                if (ps?.text_Coin != null && ps.text_Coin.font != null)
                    sourceTmp = ps.text_Coin;
            }

            if (sourceTmp == null)
            {
                var allTmps = UnityEngine.Object.FindObjectsByType<TMP_Text>(FindObjectsInactive.Exclude, FindObjectsSortMode.None);
                if (allTmps != null)
                {
                    for (int i = 0; i < allTmps.Length; i++)
                    {
                        var t = allTmps[i];
                        if (t == null || t.font == null) continue;
                        if (t.gameObject.scene.name == "DontDestroyOnLoad") continue;
                        sourceTmp = t;
                        break;
                    }
                }
            }

            if (sourceTmp != null && sourceTmp.font != null)
            {
                PrimaryFont = sourceTmp.font;
                if (sourceTmp.fontSharedMaterial != null &&
                    sourceTmp.fontSharedMaterial.mainTexture == PrimaryFont.atlasTexture)
                {
                    PrimaryFontMaterial = sourceTmp.fontSharedMaterial;
                }
                else
                {
                    PrimaryFontMaterial = PrimaryFont.material;
                }
            }
            else if (PrimaryFont == null)
            {
                PrimaryFont = TMP_Settings.defaultFontAsset;
                if (PrimaryFont != null)
                    PrimaryFontMaterial = PrimaryFont.material;
            }
        }

        private static void ScanNativeSounds()
        {
            try
            {
                if (string.IsNullOrEmpty(HoverSoundName))
                {
                    var hovers = UnityEngine.Object.FindObjectsByType<UI_SoundOnMouseOver>(FindObjectsInactive.Include, FindObjectsSortMode.None);
                    if (hovers != null)
                    {
                        for (int i = 0; i < hovers.Length; i++)
                        {
                            var h = hovers[i];
                            if (h != null && !string.IsNullOrEmpty(h.soundKey))
                            {
                                HoverSoundAssetKey = h.soundDataName ?? "";
                                HoverSoundName = h.soundKey;
                                break;
                            }
                        }
                    }
                }

                var sm = SoundManager.Instance;
                var assets = sm?.list_SoundAssetData;
                if (assets == null) return;

                for (int i = 0; i < assets.Count; i++)
                {
                    var asset = assets[i];
                    if (asset?.SoundFile == null) continue;
                    string aKey = asset.DataKey ?? "";
                    for (int j = 0; j < asset.SoundFile.Length; j++)
                    {
                        var entry = asset.SoundFile[j];
                        string n = entry?.name ?? "";
                        if (string.IsNullOrEmpty(n)) continue;

                        if (string.IsNullOrEmpty(ClickSoundName) &&
                            (n.IndexOf("Fantasy Click", StringComparison.OrdinalIgnoreCase) >= 0 ||
                             n.IndexOf("Card_Game_UI_Button_Light", StringComparison.OrdinalIgnoreCase) >= 0 ||
                             n.IndexOf("click", StringComparison.OrdinalIgnoreCase) >= 0 ||
                             n.IndexOf("button", StringComparison.OrdinalIgnoreCase) >= 0))
                        {
                            ClickSoundAssetKey = aKey;
                            ClickSoundName = n;
                        }

                        if (string.IsNullOrEmpty(HoverSoundName) &&
                            (n.IndexOf("Click_Swish", StringComparison.OrdinalIgnoreCase) >= 0 ||
                             n.IndexOf("Click 02", StringComparison.OrdinalIgnoreCase) >= 0 ||
                             n.IndexOf("Tick", StringComparison.OrdinalIgnoreCase) >= 0))
                        {
                            HoverSoundAssetKey = aKey;
                            HoverSoundName = n;
                        }

                        if (string.IsNullOrEmpty(OpenSoundName) &&
                            (n.IndexOf("Quick Transition", StringComparison.OrdinalIgnoreCase) >= 0 ||
                             n.IndexOf("Bubble_Button", StringComparison.OrdinalIgnoreCase) >= 0 ||
                             n.IndexOf("Backpack_Open", StringComparison.OrdinalIgnoreCase) >= 0))
                        {
                            OpenSoundAssetKey = aKey;
                            OpenSoundName = n;
                        }

                        if (string.IsNullOrEmpty(CoinSoundName) &&
                            (n.IndexOf("Coin_Collect", StringComparison.OrdinalIgnoreCase) >= 0 ||
                             n.IndexOf("Purchase_Coin", StringComparison.OrdinalIgnoreCase) >= 0 ||
                             n.IndexOf("Coins", StringComparison.OrdinalIgnoreCase) >= 0))
                        {
                            CoinSoundAssetKey = aKey;
                            CoinSoundName = n;
                        }

                        if (string.IsNullOrEmpty(SelectSoundName) &&
                            (n.IndexOf("Card_Game_UI_Select", StringComparison.OrdinalIgnoreCase) >= 0 ||
                             n.IndexOf("Select", StringComparison.OrdinalIgnoreCase) >= 0 ||
                             n.IndexOf("Confirm", StringComparison.OrdinalIgnoreCase) >= 0))
                        {
                            SelectSoundAssetKey = aKey;
                            SelectSoundName = n;
                        }
                    }
                }
            }
            catch { }
        }

        private static void BuildPlayableHeroCatalog()
        {
            var foundEntries = new Dictionary<eCharacterType, HeroAvatarEntry>();
            var popupSprites = new Dictionary<eCharacterType, Sprite>();

            try
            {
                var popups = Resources.FindObjectsOfTypeAll<UI_SelectCharacterPopup>();
                if (popups != null)
                {
                    for (int i = 0; i < popups.Length; i++)
                    {
                        var list = popups[i]?.list_CharacterSelectEntries;
                        if (list == null) continue;
                        for (int j = 0; j < list.Count; j++)
                        {
                            var entry = list[j];
                            if (entry == null || entry.CharacterType == eCharacterType.NONE) continue;
                            if (entry.image_Character?.sprite != null && !popupSprites.ContainsKey(entry.CharacterType))
                                popupSprites[entry.CharacterType] = entry.image_Character.sprite;
                        }
                    }
                }
            }
            catch { }

            try
            {
                var rm = ResourceManager.Instance;
                var list = rm?.list_characterSettingData;
                if (list != null)
                {
                    for (int i = 0; i < list.Count; i++)
                    {
                        var cd = list[i];
                        if (cd == null || cd.CharacterType == eCharacterType.NONE) continue;

                        eCharacterType ct = cd.CharacterType;
                        string id = ct.ToString();
                        Sprite sp = popupSprites.TryGetValue(ct, out var popSp) && popSp != null
                            ? popSp
                            : (cd.CharacterIcon != null ? cd.CharacterIcon : FindHeroFallbackSprite(ct));

                        if (sp == null) continue;
                        PinAssetAlive(sp);

                        Color col = cd.CharacterColorBright.a > 0.1f ? cd.CharacterColorBright : cd.CharacterColor;
                        if (col.a < 0.1f || (col.r + col.g + col.b) < 0.15f)
                            col = GetHeroDefaultColor(ct);

                        foundEntries[ct] = new HeroAvatarEntry
                        {
                            HeroId = id,
                            CharacterType = ct,
                            DisplayName = ResolveHeroLocalizedName(ct, id),
                            PortraitSprite = sp,
                            AccentColor = col
                        };
                    }
                }
            }
            catch { }

            for (int i = 0; i < KnownHeroes.Length; i++)
            {
                var kh = KnownHeroes[i];
                if (foundEntries.ContainsKey(kh.type)) continue;

                Sprite sp = popupSprites.TryGetValue(kh.type, out var popSp) && popSp != null
                    ? popSp
                    : FindSpriteByNames(kh.spriteFallbacks, preferSliced: false);

                if (sp == null) continue;
                PinAssetAlive(sp);

                foundEntries[kh.type] = new HeroAvatarEntry
                {
                    HeroId = kh.id,
                    CharacterType = kh.type,
                    DisplayName = ResolveHeroLocalizedName(kh.type, kh.id),
                    PortraitSprite = sp,
                    AccentColor = kh.defaultColor
                };
            }

            if (foundEntries.Count == 0) return;

            _playableHeroes.Clear();
            _heroesById.Clear();

            for (int i = 0; i < KnownHeroes.Length; i++)
            {
                if (foundEntries.TryGetValue(KnownHeroes[i].type, out var entry))
                {
                    _playableHeroes.Add(entry);
                    _heroesById[entry.HeroId] = entry;
                    foundEntries.Remove(KnownHeroes[i].type);
                }
            }

            foreach (var kv in foundEntries)
            {
                _playableHeroes.Add(kv.Value);
                _heroesById[kv.Value.HeroId] = kv.Value;
            }
        }

        private static Sprite FindHeroFallbackSprite(eCharacterType ct)
        {
            for (int i = 0; i < KnownHeroes.Length; i++)
                if (KnownHeroes[i].type == ct)
                    return FindSpriteByNames(KnownHeroes[i].spriteFallbacks, preferSliced: false);
            return null;
        }

        private static Color GetHeroDefaultColor(eCharacterType ct)
        {
            for (int i = 0; i < KnownHeroes.Length; i++)
                if (KnownHeroes[i].type == ct) return KnownHeroes[i].defaultColor;
            return NativeStyleProvider.EmberGold;
        }

        private static string ResolveHeroLocalizedName(eCharacterType ct, string heroId)
        {
            try
            {
                var lm = LocalizationManager.Instance;
                if (lm != null)
                {
                    string[] tables = { "Character", "UI", "Default", "Game" };
                    string[] keys = { $"CHARACTER_NAME_{heroId}", $"CharacterName_{heroId}", $"CHAR_{heroId}", heroId };
                    for (int t = 0; t < tables.Length; t++)
                    {
                        for (int k = 0; k < keys.Length; k++)
                        {
                            if (lm.HasString(tables[t], keys[k]))
                            {
                                string val = lm.GetString(tables[t], keys[k], (Il2CppReferenceArray<Il2CppSystem.Object>)null);
                                if (!string.IsNullOrWhiteSpace(val) && !string.Equals(val, keys[k], StringComparison.OrdinalIgnoreCase))
                                    return val.Trim();
                            }
                        }
                    }
                }
            }
            catch { }

            string locKey = $"hero.{heroId.ToLowerInvariant()}";
            string modLoc = CoopLoc.T(locKey);
            if (!string.Equals(modLoc, locKey, StringComparison.OrdinalIgnoreCase))
                return modLoc;

            string raw = (heroId ?? "BASIC").Replace('_', ' ').ToLowerInvariant();
            if (raw.Length == 0) return "Hero";
            var chars = raw.ToCharArray();
            chars[0] = char.ToUpperInvariant(chars[0]);
            for (int i = 1; i < chars.Length; i++)
                if (chars[i - 1] == ' ') chars[i] = char.ToUpperInvariant(chars[i]);
            return new string(chars);
        }

        // Reject degenerate 9-slice sprites where borders consume the entire interior (0x0 center).
        public static bool HasValid9SliceBorder(Sprite sp)
        {
            if (sp == null) return false;
            Vector4 b = sp.border;
            Rect r = sp.rect;
            if (b.x <= 1f || b.y <= 1f || b.z <= 1f || b.w <= 1f) return false;
            float cx = r.width - (b.x + b.z);
            float cy = r.height - (b.y + b.w);
            return cx >= 8f && cy >= 8f
                && b.x <= r.width * 0.42f && b.z <= r.width * 0.42f
                && b.y <= r.height * 0.42f && b.w <= r.height * 0.42f;
        }

        private static Sprite FindSpriteByNames(string[] names, bool preferSliced)
        {
            if (names == null) return null;
            if (preferSliced)
            {
                for (int i = 0; i < names.Length; i++)
                {
                    if (_slicedSpritesByName.TryGetValue(names[i], out var sp) && sp != null)
                    {
                        PinAssetAlive(sp);
                        return sp;
                    }
                }
            }
            for (int i = 0; i < names.Length; i++)
            {
                if (_spritesByName.TryGetValue(names[i], out var sp) && sp != null)
                {
                    PinAssetAlive(sp);
                    return sp;
                }
            }
            return null;
        }

        private static Sprite ResolveRole(string roleKey, string[] preferredNames, bool preferSliced, bool fallbackToIcon = false)
        {
            if (_roleCache.TryGetValue(roleKey, out var cached) && cached != null)
                return cached;

            Sprite sp = FindSpriteByNames(preferredNames, preferSliced);
            if (sp == null && fallbackToIcon)
                sp = GetFallbackIcon();

            if (sp != null)
                _roleCache[roleKey] = sp;
            return sp;
        }

        public static Sprite GetFallbackIcon()
        {
            if (_roleCache.TryGetValue("_FallbackIcon", out var cached) && cached != null)
                return cached;

            Sprite sp = FindSpriteByNames(FallbackIconNames, preferSliced: false);
            if (sp == null)
            {
                foreach (var kv in _spritesByName)
                {
                    if (kv.Value != null) { sp = kv.Value; break; }
                }
            }
            if (sp != null)
            {
                PinAssetAlive(sp);
                _roleCache["_FallbackIcon"] = sp;
            }
            return sp;
        }

        // True for sprites whose interior pixels are already colored/darkened (must be rendered near Color.white).
        public static bool IsPreColoredSprite(Sprite sp)
        {
            if (sp == null || string.IsNullOrEmpty(sp.name)) return false;
            string n = sp.name;

            if (string.Equals(n, "UISprite", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(n, "Background", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(n, "doubleOutlineBox_96x92", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(n, "OutlineBorderFilled", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(n, "White1px", StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            return n.StartsWith("DialogBox_v3", StringComparison.OrdinalIgnoreCase)
                || n.StartsWith("Button_Light_Brown", StringComparison.OrdinalIgnoreCase)
                || n.StartsWith("Bar_Big_01", StringComparison.OrdinalIgnoreCase)
                || n.StartsWith("AchievementBackground", StringComparison.OrdinalIgnoreCase)
                || n.StartsWith("CardBG_Panel", StringComparison.OrdinalIgnoreCase)
                || n.StartsWith("Popup_Panel", StringComparison.OrdinalIgnoreCase);
        }

        public static Sprite GetWindowPanelSprite() { EnsureScanned(); return ResolveRole("WindowPanel", WindowPanelNames, preferSliced: true) ?? GetFallbackIcon(); }
        public static Sprite GetHudPanelSprite() { EnsureScanned(); return ResolveRole("HudPanel", HudPanelNames, preferSliced: true) ?? GetWindowPanelSprite(); }
        public static Sprite GetSubPanelSprite() { EnsureScanned(); return ResolveRole("SubPanel", SubPanelNames, preferSliced: true) ?? GetWindowPanelSprite(); }
        public static Sprite GetWellPanelSprite() { EnsureScanned(); return ResolveRole("WellPanel", WellPanelNames, preferSliced: true) ?? GetSubPanelSprite(); }
        public static Sprite GetTintablePanelSprite() { EnsureScanned(); return ResolveRole("TintablePanel", TintablePanelNames, preferSliced: true) ?? GetSubPanelSprite(); }
        public static Sprite GetButtonSprite() { EnsureScanned(); return ResolveRole("ButtonNormal", ButtonNormalNames, preferSliced: true) ?? GetSubPanelSprite(); }
        public static Sprite GetTabSprite() { EnsureScanned(); return ResolveRole("TabButton", TabButtonNames, preferSliced: true) ?? GetButtonSprite(); }
        public static Sprite GetBadgePillSprite() { EnsureScanned(); return ResolveRole("BadgePill", BadgePillNames, preferSliced: true) ?? GetSubPanelSprite(); }
        public static Sprite GetDividerSprite() { EnsureScanned(); return ResolveRole("Divider", DividerNames, preferSliced: false) ?? GetFallbackIcon(); }
        public static Sprite GetScrollTrackSprite() { EnsureScanned(); return ResolveRole("ScrollTrack", ScrollTrackNames, preferSliced: true) ?? GetWellPanelSprite(); }
        public static Sprite GetScrollHandleSprite() { EnsureScanned(); return ResolveRole("ScrollHandle", ScrollHandleNames, preferSliced: true) ?? GetButtonSprite(); }
        public static Sprite GetSpeechBubbleSprite() { EnsureScanned(); return ResolveRole("SpeechBubble", SpeechBubbleNames, preferSliced: true) ?? GetHudPanelSprite(); }
        public static Sprite GetAvatarFrameSprite(bool selected = false)
        {
            EnsureScanned();
            return selected
                ? (ResolveRole("AvatarFrameSelected", AvatarFrameSelectedNames, preferSliced: false) ?? GetAvatarFrameSprite(false))
                : (ResolveRole("AvatarFrame", AvatarFrameNames, preferSliced: false) ?? GetFallbackIcon());
        }
        public static Sprite GetStickerMedallionSprite() { EnsureScanned(); return ResolveRole("StickerMedallion", StickerMedallionNames, preferSliced: false) ?? GetAvatarFrameSprite(false); }
        public static Sprite GetCircleBadgeSprite() { EnsureScanned(); return ResolveRole("CircleBadge", CircleBadgeNames, preferSliced: false) ?? GetFallbackIcon(); }
        public static Sprite GetCheckboxSprite(bool isOn)
        {
            EnsureScanned();
            return isOn
                ? ResolveRole("CheckboxOn", CheckboxOnNames, preferSliced: false, fallbackToIcon: true)
                : ResolveRole("CheckboxOff", CheckboxOffNames, preferSliced: false, fallbackToIcon: true);
        }
        public static Sprite GetCoinIconSprite() { EnsureScanned(); return ResolveRole("CoinIcon", CoinIconNames, preferSliced: false, fallbackToIcon: true); }
        public static Sprite GetCrownIconSprite() { EnsureScanned(); return ResolveRole("CrownIcon", CrownIconNames, preferSliced: false, fallbackToIcon: true); }
        public static Sprite GetRefreshIconSprite() { EnsureScanned(); return ResolveRole("RefreshIcon", RefreshIconNames, preferSliced: false, fallbackToIcon: true); }
        public static Sprite GetCheckIconSprite() { EnsureScanned(); return ResolveRole("CheckIcon", CheckIconNames, preferSliced: false, fallbackToIcon: true); }
        public static Sprite GetGearIconSprite() { EnsureScanned(); return ResolveRole("GearIcon", GearIconNames, preferSliced: false, fallbackToIcon: true); }
        public static Sprite GetEditBadgeSprite() { EnsureScanned(); return ResolveRole("EditBadge", EditBadgeNames, preferSliced: false, fallbackToIcon: true); }
        public static Sprite GetCloseIconSprite() { EnsureScanned(); return ResolveRole("CloseIcon", CloseIconNames, preferSliced: false); }
        public static Sprite GetSketchTileSprite() { EnsureScanned(); return ResolveRole("SketchTile", SketchTileNames, preferSliced: false, fallbackToIcon: true); }
        public static Sprite GetSketchArrowSprite() { EnsureScanned(); return ResolveRole("SketchArrow", SketchArrowNames, preferSliced: false, fallbackToIcon: true); }
        public static Sprite GetPingRingSprite() { EnsureScanned(); return ResolveRole("PingRing", PingRingNames, preferSliced: false, fallbackToIcon: true); }
        public static Sprite GetPingBeaconSprite() { EnsureScanned(); return ResolveRole("PingBeacon", PingBeaconNames, preferSliced: false, fallbackToIcon: true); }

        public static Sprite GetStickerIconSprite(StickerId id)
        {
            EnsureScanned();
            string key = $"Sticker_{id}";
            if (StickerSpriteFallbacks.TryGetValue(id, out var list))
                return ResolveRole(key, list, preferSliced: false, fallbackToIcon: true);
            return GetFallbackIcon();
        }

        public static Sprite GetPingIconSprite(PingKind kind)
        {
            EnsureScanned();
            string key = $"Ping_{kind}";
            if (PingSpriteFallbacks.TryGetValue(kind, out var list))
                return ResolveRole(key, list, preferSliced: false, fallbackToIcon: true);
            return GetFallbackIcon();
        }

        public static Sprite GetWorldBannerSprite(eWorldType world)
        {
            EnsureScanned();
            string key = $"World_{world}";
            if (WorldBannerFallbacks.TryGetValue(world, out var list))
                return ResolveRole(key, list, preferSliced: false, fallbackToIcon: true);
            return ResolveRole("World_Default", WorldBannerFallbacks[eWorldType.WORLD_1_FOREST], preferSliced: false, fallbackToIcon: true);
        }

        public static Sprite GetGameModeBannerSprite(CoopRunMode runMode, eWorldType world, eEndlessModeType endlessType = eEndlessModeType.BASIC_ENDLESS)
        {
            EnsureScanned();
            if (runMode == CoopRunMode.EnigmaSanctum)
                return ResolveRole("Banner_EnigmaSanctum", EnigmaSanctumBannerFallbacks, preferSliced: false, fallbackToIcon: true);
            if (runMode == CoopRunMode.Endless)
            {
                if (endlessType == eEndlessModeType.RANDOM_ENDLESS_CHALLENGE)
                    return ResolveRole("Banner_EndlessRandom", EndlessBannerFallbacks, preferSliced: false, fallbackToIcon: true);
                return GetWorldBannerSprite(world) ?? ResolveRole("Banner_Endless", EndlessBannerFallbacks, preferSliced: false, fallbackToIcon: true);
            }
            return GetWorldBannerSprite(world);
        }

        public static IReadOnlyList<HeroAvatarEntry> GetPlayableHeroes()
        {
            EnsureScanned();
            if (_playableHeroes.Count == 0)
                BuildPlayableHeroCatalog();
            return _playableHeroes;
        }

        public static string NormalizeHeroId(string rawHeroId)
        {
            if (string.IsNullOrWhiteSpace(rawHeroId)) return "BASIC";
            string trimmed = rawHeroId.Trim().ToUpperInvariant();
            if (Enum.TryParse<eCharacterType>(trimmed, true, out var ct) && ct != eCharacterType.NONE)
                return ct.ToString();
            return "BASIC";
        }

        public static HeroAvatarEntry GetHeroAvatar(string heroId)
        {
            EnsureScanned();
            string norm = NormalizeHeroId(heroId);
            if (_heroesById.TryGetValue(norm, out var entry) && entry.PortraitSprite != null)
                return entry;
            if (_heroesById.TryGetValue("BASIC", out var defEntry) && defEntry.PortraitSprite != null)
                return defEntry;
            if (_playableHeroes.Count > 0 && _playableHeroes[0].PortraitSprite != null)
                return _playableHeroes[0];

            return new HeroAvatarEntry
            {
                HeroId = "BASIC",
                CharacterType = eCharacterType.BASIC,
                DisplayName = ResolveHeroLocalizedName(eCharacterType.BASIC, "BASIC"),
                PortraitSprite = GetFallbackIcon(),
                AccentColor = NativeStyleProvider.EmberGold
            };
        }
    }
}
