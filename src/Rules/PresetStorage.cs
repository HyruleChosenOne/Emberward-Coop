using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Text.Json;
using EmberwardCoop.Core;

namespace EmberwardCoop.Rules
{
    public static class PresetStorage
    {
        private static string _presetsDir;

        private sealed class PresetDto
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
        }

        public static string GetPresetsDirectory()
        {
            if (!string.IsNullOrEmpty(_presetsDir)) return _presetsDir;
            string dllDir = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location) ?? ".";
            _presetsDir = Path.Combine(dllDir, "Presets");
            try { Directory.CreateDirectory(_presetsDir); } catch { }
            return _presetsDir;
        }

        public static List<string> ListSavedPresets()
        {
            var names = new List<string>();
            try
            {
                string dir = GetPresetsDirectory();
                if (!Directory.Exists(dir)) return names;
                foreach (var file in Directory.GetFiles(dir, "*.json"))
                    names.Add(Path.GetFileNameWithoutExtension(file));
                names.Sort(StringComparer.OrdinalIgnoreCase);
            }
            catch { }
            return names;
        }

        public static bool SavePreset(string rawName, SessionSettings s)
        {
            string safe = SanitizeFileName(rawName);
            if (string.IsNullOrEmpty(safe) || s == null) return false;
            try
            {
                var dto = new PresetDto
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
                    SpeedControl = (int)s.SpeedControl
                };
                string path = Path.Combine(GetPresetsDirectory(), safe + ".json");
                File.WriteAllText(path, JsonSerializer.Serialize(dto, new JsonSerializerOptions { WriteIndented = true }));
                CoopLogger.Info(LogCategory.Sync, $"Saved custom preset '{safe}'.");
                return true;
            }
            catch (Exception ex)
            {
                CoopLogger.Warn(LogCategory.Sync, $"Failed to save preset '{safe}': {ex.Message}");
                return false;
            }
        }

        public static bool LoadPreset(string rawName, SessionSettings target)
        {
            string safe = SanitizeFileName(rawName);
            if (string.IsNullOrEmpty(safe) || target == null) return false;
            try
            {
                string path = Path.Combine(GetPresetsDirectory(), safe + ".json");
                if (!File.Exists(path)) return false;
                var dto = JsonSerializer.Deserialize<PresetDto>(File.ReadAllText(path));
                if (dto == null) return false;

                target.EconomyMode = (CoopEconomyMode)dto.EconomyMode;
                target.Preset = SessionPreset.Custom;
                target.AutoScaleByPlayerCount = dto.AutoScaleByPlayerCount;
                target.EnemyHpMultiplier = Math.Clamp(dto.EnemyHpMultiplier, 1.0f, 2.5f);
                target.WaveDensityMultiplier = Math.Clamp(dto.WaveDensityMultiplier, 1.0f, 2.0f);
                target.EnemySpeedMultiplier = Math.Clamp(dto.EnemySpeedMultiplier, 0.8f, 1.5f);
                target.BossHpArmorMultiplier = Math.Clamp(dto.BossHpArmorMultiplier, 1.0f, 3.0f);
                target.StartingGold = (StartingGoldRule)dto.StartingGold;
                target.WaveIncomeMultiplier = Math.Clamp(dto.WaveIncomeMultiplier, 0.5f, 1.5f);
                target.BlockPacing = (BlockPacingRule)dto.BlockPacing;
                target.SharedTurnBlockCap = Math.Clamp(dto.SharedTurnBlockCap, 1, 12);
                target.CoreHp = (CoreHpRule)dto.CoreHp;
                target.AllowResourceGifting = dto.AllowResourceGifting;
                target.GiftPerWaveCap = Math.Max(0, dto.GiftPerWaveCap);
                target.DraftMode = (DraftSyncMode)dto.DraftMode;
                target.SpeedControl = (SpeedControlRule)dto.SpeedControl;
                CoopLogger.Info(LogCategory.Sync, $"Loaded custom preset '{safe}'.");
                return true;
            }
            catch (Exception ex)
            {
                CoopLogger.Warn(LogCategory.Sync, $"Failed to load preset '{safe}': {ex.Message}");
                return false;
            }
        }

        public static bool DeletePreset(string rawName)
        {
            string safe = SanitizeFileName(rawName);
            if (string.IsNullOrEmpty(safe)) return false;
            try
            {
                string path = Path.Combine(GetPresetsDirectory(), safe + ".json");
                if (File.Exists(path)) File.Delete(path);
                return true;
            }
            catch { return false; }
        }

        private static string SanitizeFileName(string name)
        {
            if (string.IsNullOrWhiteSpace(name)) return "";
            char[] invalid = Path.GetInvalidFileNameChars();
            string clean = name.Trim();
            for (int i = 0; i < invalid.Length; i++) clean = clean.Replace(invalid[i].ToString(), "");
            return clean.Length > 32 ? clean.Substring(0, 32) : clean;
        }
    }
}
