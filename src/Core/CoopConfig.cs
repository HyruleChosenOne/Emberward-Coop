using System;
using BepInEx.Configuration;
using UnityEngine;

namespace EmberwardCoop.Core
{
    public enum WaveStartPolicy
    {
        AnyPlayer = 0,
        ReadyCheckWithHostOverride = 1,
        HostOnly = 2
    }

    public enum OwnershipDisplayMode
    {
        Always = 0,
        HoverOnly = 1,
        Off = 2
    }

    public enum VisualScalePreset
    {
        Small = 0,
        Normal = 1,
        Large = 2
    }

    public static class CoopConfig
    {
        public static ConfigEntry<string> PlayerName;
        public static ConfigEntry<int> DefaultLanPort;
        public static ConfigEntry<int> MaxPlayers;
        public static ConfigEntry<int> PreferredColorIndex;
        public static ConfigEntry<string> AvatarHeroId;

        public static ConfigEntry<KeyCode> ToggleLobbyKey;
        public static ConfigEntry<KeyCode> ToggleHudKey;
        public static ConfigEntry<KeyCode> PingHotkey;
        public static ConfigEntry<KeyCode> EmoteWheelKey;
        public static ConfigEntry<KeyCode> SketchModifierKey;

        public static ConfigEntry<WaveStartPolicy> WavePolicy;
        public static ConfigEntry<bool> ShowCoopHud;
        public static ConfigEntry<bool> CompactHudMode;
        public static ConfigEntry<bool> AutoCollapseHudDuringCombat;
        public static ConfigEntry<bool> HideLobbyCodeByDefault;

        public static ConfigEntry<bool> ShowTeammateCursors;
        public static ConfigEntry<bool> EnableGhostPreviews;
        public static ConfigEntry<OwnershipDisplayMode> OwnershipIndicatorMode;
        public static ConfigEntry<bool> EnableSpatialFeedback;
        public static ConfigEntry<bool> MuteTeammateStickers;
        public static ConfigEntry<VisualScalePreset> PingStickerScalePreset;
        public static ConfigEntry<float> StickerScale;
        public static ConfigEntry<float> StickerAudioVolume;

        public static ConfigEntry<bool> AutoPauseOnDisconnect;
        public static ConfigEntry<bool> ReadyCheckSoundCue;
        public static ConfigEntry<bool> LockCameraDuringRadialMenus;
        public static ConfigEntry<bool> AllowClientMetaProgression;
        public static ConfigEntry<bool> UnlockAllMapsModes;

        public static ConfigEntry<bool> DebugLogNetwork;
        public static ConfigEntry<bool> DebugLogSync;
        public static ConfigEntry<bool> DebugLogStateHash;
        public static ConfigEntry<bool> DebugLogQuests;
        public static ConfigEntry<bool> DebugLogModals;

        public static readonly Color[] PlayerPalette =
        {
            new Color(1.00f, 0.78f, 0.22f, 1f), // Amber Gold
            new Color(0.26f, 0.85f, 1.00f, 1f), // Sky Cyan
            new Color(0.45f, 0.95f, 0.52f, 1f), // Emerald Green
            new Color(0.95f, 0.45f, 0.88f, 1f), // Violet Rose
            new Color(1.00f, 0.50f, 0.30f, 1f), // Coral Flame
            new Color(0.75f, 0.65f, 1.00f, 1f)  // Royal Lavender
        };

        private static readonly string[] PlayerColorLocKeys =
        {
            "color.amber",
            "color.sky",
            "color.emerald",
            "color.violet",
            "color.coral",
            "color.lavender"
        };

        public static void Bind(ConfigFile config)
        {
            string defaultName = Environment.UserName;
            if (string.IsNullOrWhiteSpace(defaultName)) defaultName = "EmberMage";

            PlayerName = config.Bind("General", "PlayerName", defaultName, "Display name used in LAN lobbies (Steam persona is used automatically when hosting/joining via Steam).");
            DefaultLanPort = config.Bind("Networking", "DefaultLanPort", 27777, "TCP/UDP port for Direct LAN sessions.");
            MaxPlayers = config.Bind("Networking", "MaxPlayers", 4, "Maximum players allowed in a hosted co-op lobby (2-6).");
            PreferredColorIndex = config.Bind("Presence", "PreferredColorIndex", 0, "Preferred player accent color index (0-5).");
            AvatarHeroId = config.Bind("Presence", "AvatarHeroId", "BASIC", "Selected playable Emberward hero ID used for your lobby and in-game HUD portrait.");

            ToggleLobbyKey = config.Bind("Controls", "ToggleLobbyKey", KeyCode.F8, "Hotkey to toggle the Co-op Lobby panel.");
            ToggleHudKey = config.Bind("Controls", "ToggleHudKey", KeyCode.F7, "Hotkey to collapse or expand the in-run Co-op Players HUD.");
            PingHotkey = config.Bind("Controls", "PingHotkey", KeyCode.Mouse2, "Hotkey to trigger Smart Ping or hold for Tactical Ping Wheel.");
            EmoteWheelKey = config.Bind("Controls", "EmoteWheelKey", KeyCode.T, "Hotkey to open the Sticker & Quick-Callout Wheel.");
            SketchModifierKey = config.Bind("Controls", "SketchModifierKey", KeyCode.LeftAlt, "Hold this key + Left-Drag on the grid to sketch a tactical maze path for teammates.");
            LockCameraDuringRadialMenus = config.Bind("Controls", "LockCameraDuringRadialMenus", true, "Lock camera panning and zoom while radial emote or ping wheels are open.");

            WavePolicy = config.Bind("Gameplay", "WaveStartPolicy", WaveStartPolicy.ReadyCheckWithHostOverride, "Controls how waves start in co-op: AnyPlayer, ReadyCheckWithHostOverride, or HostOnly.");
            AutoPauseOnDisconnect = config.Bind("Gameplay", "AutoPauseOnDisconnect", true, "Automatically pause combat and show a reconnect modal if a peer disconnects mid-wave.");
            ReadyCheckSoundCue = config.Bind("Gameplay", "ReadyCheckSoundCue", true, "Play an audio cue when all teammates mark ready in the lobby or for the next wave.");

            ShowCoopHud = config.Bind("UI", "ShowCoopHud", true, "Show the teammate status widget while in a co-op session.");
            CompactHudMode = config.Bind("UI", "CompactHudMode", false, "Use a slimmer decluttered Co-op HUD layout.");
            AutoCollapseHudDuringCombat = config.Bind("UI", "AutoCollapseHudDuringCombat", false, "Automatically collapse the Players HUD during active combat waves and re-expand during Build Phase.");
            HideLobbyCodeByDefault = config.Bind("UI", "HideLobbyCodeByDefault", false, "Mask lobby codes by default when opening the Co-op Lobby (Streamer Mode).");

            ShowTeammateCursors = config.Bind("Presence", "ShowTeammateCursors", true, "Render 3D ground cursors and nameplates for connected teammates.");
            EnableGhostPreviews = config.Bind("Presence", "EnableGhostPreviews", true, "Render translucent ghost previews of teammates' hovered tetrominoes and towers.");
            OwnershipIndicatorMode = config.Bind("Presence", "OwnershipIndicatorMode", OwnershipDisplayMode.Always, "Controls when tower ownership base rings are shown in Separate Resources mode: Always, HoverOnly, or Off.");
            EnableSpatialFeedback = config.Bind("Presence", "EnableSpatialFeedback", true, "Play particles, floating callouts, and audio cues for teammate actions and pings.");
            MuteTeammateStickers = config.Bind("Presence", "MuteTeammateStickers", false, "Mute incoming teammate stickers, pings, and quick-callout popups.");
            PingStickerScalePreset = config.Bind("Presence", "PingStickerScalePreset", VisualScalePreset.Normal, "Size preset for world-space and HUD pings and stickers (Small, Normal, Large).");
            StickerScale = config.Bind("Presence", "StickerScale", 1.0f, "Scale multiplier for world-space and HUD stickers (0.6 to 1.6).");
            StickerAudioVolume = config.Bind("Presence", "StickerAudioVolume", 0.85f, "Relative audio cue intensity for stickers and callouts (0.0 to 1.0).");

            AllowClientMetaProgression = config.Bind("Progression", "AllowClientMetaProgression", true, "Award XP, achievements, and unlocks earned in co-op to Client profiles safely without overwriting solo runs.");
            UnlockAllMapsModes = config.Bind("Progression", "UnlockAllMapsModes", false, "Unlock all maps, difficulties, and game modes (including Endless and Enigma Sanctum) strictly inside Co-op sessions without modifying solo savefiles.");

            DebugLogNetwork = config.Bind("Diagnostics", "DebugLogNetwork", false, "Log low-level packet traffic and transport events.");
            DebugLogSync = config.Bind("Diagnostics", "DebugLogSync", false, "Log gameplay synchronization actions and reconciliations.");
            DebugLogStateHash = config.Bind("Diagnostics", "DebugLogStateHash", false, "Log periodic state hash comparisons for desync diagnosis.");
            DebugLogQuests = config.Bind("Diagnostics", "DebugLogQuests", false, "Log quest progress and meta-progression sync events.");
            DebugLogModals = config.Bind("Diagnostics", "DebugLogModals", false, "Log modal/popup synchronization events.");

            SyncStickerScaleFromPreset();
        }

        public static float GetEffectiveStickerScale()
        {
            if (PingStickerScalePreset == null) return StickerScale?.Value ?? 1.0f;
            return PingStickerScalePreset.Value switch
            {
                VisualScalePreset.Small => 0.78f,
                VisualScalePreset.Large => 1.28f,
                _ => 1.00f
            };
        }

        public static void SyncStickerScaleFromPreset()
        {
            if (StickerScale != null)
                StickerScale.Value = GetEffectiveStickerScale();
        }

        public static void CycleScalePreset(int delta)
        {
            if (PingStickerScalePreset == null) return;
            int next = (((int)PingStickerScalePreset.Value + delta) % 3 + 3) % 3;
            PingStickerScalePreset.Value = (VisualScalePreset)next;
            SyncStickerScaleFromPreset();
        }

        public static void CycleOwnershipIndicatorMode(int delta)
        {
            if (OwnershipIndicatorMode == null) return;
            int next = (((int)OwnershipIndicatorMode.Value + delta) % 3 + 3) % 3;
            OwnershipIndicatorMode.Value = (OwnershipDisplayMode)next;
        }

        public static void ResetKeybindsToDefault()
        {
            if (ToggleLobbyKey != null) ToggleLobbyKey.Value = KeyCode.F8;
            if (ToggleHudKey != null) ToggleHudKey.Value = KeyCode.F7;
            if (PingHotkey != null) PingHotkey.Value = KeyCode.Mouse2;
            if (EmoteWheelKey != null) EmoteWheelKey.Value = KeyCode.T;
            if (SketchModifierKey != null) SketchModifierKey.Value = KeyCode.LeftAlt;
        }

        public static Color GetPlayerColor(int colorIndex)
        {
            if (PlayerPalette.Length == 0) return Color.white;
            int idx = ((colorIndex % PlayerPalette.Length) + PlayerPalette.Length) % PlayerPalette.Length;
            return PlayerPalette[idx];
        }

        public static string GetPlayerColorNameKey(int colorIndex)
        {
            if (PlayerColorLocKeys.Length == 0) return "color.amber";
            int idx = ((colorIndex % PlayerColorLocKeys.Length) + PlayerColorLocKeys.Length) % PlayerColorLocKeys.Length;
            return PlayerColorLocKeys[idx];
        }
    }
}
