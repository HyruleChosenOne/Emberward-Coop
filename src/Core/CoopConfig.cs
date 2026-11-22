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

    public static class CoopConfig
    {
        public static ConfigEntry<string> PlayerName;
        public static ConfigEntry<int> DefaultLanPort;
        public static ConfigEntry<int> MaxPlayers;
        public static ConfigEntry<int> PreferredColorIndex;
        public static ConfigEntry<KeyCode> ToggleLobbyKey;
        public static ConfigEntry<KeyCode> PingHotkey;
        public static ConfigEntry<KeyCode> EmoteWheelKey;
        public static ConfigEntry<KeyCode> SketchModifierKey;
        public static ConfigEntry<KeyCode> UndoBuildKey;
        public static ConfigEntry<WaveStartPolicy> WavePolicy;
        public static ConfigEntry<bool> ShowCoopHud;
        public static ConfigEntry<bool> CompactHudMode;
        public static ConfigEntry<bool> EnableGhostPreviews;
        public static ConfigEntry<bool> EnableSpatialFeedback;
        public static ConfigEntry<bool> MuteTeammateStickers;
        public static ConfigEntry<float> StickerScale;
        public static ConfigEntry<float> StickerAudioVolume;
        public static ConfigEntry<bool> AutoPauseOnDisconnect;
        public static ConfigEntry<bool> AllowClientMetaProgression;

        public static ConfigEntry<bool> DebugLogNetwork;
        public static ConfigEntry<bool> DebugLogSync;
        public static ConfigEntry<bool> DebugLogStateHash;
        public static ConfigEntry<bool> DebugLogQuests;
        public static ConfigEntry<bool> DebugLogModals;

        public static readonly Color[] PlayerPalette =
        {
            new Color(1.00f, 0.78f, 0.22f, 1f),
            new Color(0.26f, 0.85f, 1.00f, 1f),
            new Color(0.45f, 0.95f, 0.52f, 1f),
            new Color(0.95f, 0.45f, 0.88f, 1f),
            new Color(1.00f, 0.50f, 0.30f, 1f),
            new Color(0.75f, 0.65f, 1.00f, 1f)
        };

        public static void Bind(ConfigFile config)
        {
            string defaultName = Environment.UserName;
            if (string.IsNullOrWhiteSpace(defaultName)) defaultName = "EmberMage";

            PlayerName = config.Bind("General", "PlayerName", defaultName, "Display name used in LAN lobbies (Steam persona is used automatically when hosting/joining via Steam).");
            DefaultLanPort = config.Bind("Networking", "DefaultLanPort", 27777, "TCP/UDP port for Direct LAN sessions.");
            MaxPlayers = config.Bind("Networking", "MaxPlayers", 4, "Maximum players allowed in a hosted co-op lobby (2-6).");
            PreferredColorIndex = config.Bind("Presence", "PreferredColorIndex", 0, "Preferred player accent color index (0-5).");
            ToggleLobbyKey = config.Bind("Controls", "ToggleLobbyKey", KeyCode.F8, "Hotkey to toggle the Co-op Lobby panel.");
            PingHotkey = config.Bind("Controls", "PingHotkey", KeyCode.Mouse2, "Hotkey to trigger Smart Ping or hold for Tactical Ping Wheel.");
            EmoteWheelKey = config.Bind("Controls", "EmoteWheelKey", KeyCode.T, "Hotkey to open the Sticker & Quick-Callout Wheel.");
            SketchModifierKey = config.Bind("Controls", "SketchModifierKey", KeyCode.LeftAlt, "Hold this key + Left-Drag on the grid to sketch a tactical maze path for teammates.");
            UndoBuildKey = config.Bind("Controls", "UndoBuildKey", KeyCode.Z, "Press Ctrl + this key during Build Phase to quick-undo your last placement.");
            WavePolicy = config.Bind("Gameplay", "WaveStartPolicy", WaveStartPolicy.ReadyCheckWithHostOverride, "Controls how waves start in co-op: AnyPlayer, ReadyCheckWithHostOverride, or HostOnly.");
            ShowCoopHud = config.Bind("UI", "ShowCoopHud", true, "Show the teammate status widget while in a co-op session.");
            CompactHudMode = config.Bind("UI", "CompactHudMode", false, "Use a slimmer decluttered Co-op HUD layout.");
            EnableGhostPreviews = config.Bind("Presence", "EnableGhostPreviews", true, "Render translucent ghost previews of teammates' hovered tetrominoes and towers.");
            EnableSpatialFeedback = config.Bind("Presence", "EnableSpatialFeedback", true, "Play particles, floating callouts, and audio cues for teammate actions and pings.");
            MuteTeammateStickers = config.Bind("Presence", "MuteTeammateStickers", false, "Mute incoming teammate stickers and quick-callout popups.");
            StickerScale = config.Bind("Presence", "StickerScale", 1.0f, "Scale multiplier for world-space and HUD stickers (0.6 to 1.6).");
            StickerAudioVolume = config.Bind("Presence", "StickerAudioVolume", 0.85f, "Relative audio cue intensity for stickers and callouts (0.0 to 1.0).");
            AutoPauseOnDisconnect = config.Bind("Gameplay", "AutoPauseOnDisconnect", true, "Automatically pause combat and show a reconnect modal if a peer disconnects mid-wave.");
            AllowClientMetaProgression = config.Bind("Progression", "AllowClientMetaProgression", true, "Award XP, achievements, and unlocks earned in co-op to Client profiles safely without overwriting solo runs.");

            DebugLogNetwork = config.Bind("Diagnostics", "DebugLogNetwork", false, "Log low-level packet traffic and transport events.");
            DebugLogSync = config.Bind("Diagnostics", "DebugLogSync", true, "Log gameplay synchronization actions and reconciliations.");
            DebugLogStateHash = config.Bind("Diagnostics", "DebugLogStateHash", false, "Log periodic state hash comparisons for desync diagnosis.");
            DebugLogQuests = config.Bind("Diagnostics", "DebugLogQuests", true, "Log quest progress and meta-progression sync events.");
            DebugLogModals = config.Bind("Diagnostics", "DebugLogModals", true, "Log modal/popup synchronization events.");
        }

        public static Color GetPlayerColor(int colorIndex)
        {
            if (PlayerPalette.Length == 0) return Color.white;
            int idx = ((colorIndex % PlayerPalette.Length) + PlayerPalette.Length) % PlayerPalette.Length;
            return PlayerPalette[idx];
        }
    }
}
