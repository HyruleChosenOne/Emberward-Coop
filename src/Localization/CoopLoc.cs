using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Text.Json;
using EmberwardCoop.Core;
using UnityEngine;

namespace EmberwardCoop.Localization
{
    public static class CoopLoc
    {
        private static readonly Dictionary<string, string> _fallbackEn = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        private static readonly Dictionary<string, string> _active = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        public static string ActiveLangCode { get; private set; } = "en";
        public static string LanguagesDirectory { get; private set; } = "";
        public static event Action OnLanguageChanged;

        public static void Initialize()
        {
            try
            {
                string dllDir = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location) ?? "";
                LanguagesDirectory = Path.Combine(dllDir, "Languages");
            }
            catch
            {
                LanguagesDirectory = "Languages";
            }

            LoadDictionaryInto("en", _fallbackEn);
            SyncFromGameLanguage();
        }

        public static void SyncFromGameLanguage()
        {
            SystemLanguage lang = SystemLanguage.English;
            try
            {
                var lm = LocalizationManager.Instance;
                if (lm != null) lang = lm.GetCurrentLanguage();
                else lang = Application.systemLanguage;
            }
            catch { }

            SetLanguage(MapSystemLanguage(lang));
        }

        public static void OnGameLanguageChanged(SystemLanguage lang)
        {
            SetLanguage(MapSystemLanguage(lang));
        }

        public static void SetLanguage(string langCode)
        {
            string normalized = string.IsNullOrWhiteSpace(langCode) ? "en" : langCode.Trim();
            _active.Clear();
            ActiveLangCode = normalized;

            if (!string.Equals(normalized, "en", StringComparison.OrdinalIgnoreCase))
            {
                if (!LoadDictionaryInto(normalized, _active))
                {
                    // Try base language prefix (e.g. "pt" -> "pt-BR" or "es-MX" -> "es")
                    int dash = normalized.IndexOf('-');
                    if (dash > 0) LoadDictionaryInto(normalized.Substring(0, dash), _active);
                }
            }

            CoopLogger.Info(LogCategory.UI, $"Localization synced to '{ActiveLangCode}' ({_active.Count} overrides, {_fallbackEn.Count} en keys).");
            try { OnLanguageChanged?.Invoke(); } catch { }
        }

        public static string T(string key)
        {
            if (string.IsNullOrEmpty(key)) return "";
            if (_active.TryGetValue(key, out string val) && !string.IsNullOrEmpty(val)) return val;
            if (_fallbackEn.TryGetValue(key, out string en) && !string.IsNullOrEmpty(en)) return en;
            return key;
        }

        public static string F(string key, params object[] args)
        {
            string fmt = T(key);
            if (args == null || args.Length == 0) return fmt;
            try { return string.Format(fmt, args); }
            catch { return fmt; }
        }

        public static string MapSystemLanguage(SystemLanguage lang) => lang switch
        {
            SystemLanguage.ChineseSimplified => "zh-CN",
            SystemLanguage.Chinese => "zh-CN",
            SystemLanguage.ChineseTraditional => "zh-TW",
            SystemLanguage.Japanese => "ja",
            SystemLanguage.Korean => "ko",
            SystemLanguage.French => "fr",
            SystemLanguage.German => "de",
            SystemLanguage.Spanish => "es",
            SystemLanguage.Portuguese => "pt-BR",
            SystemLanguage.Russian => "ru",
            _ => "en"
        };

        private static bool LoadDictionaryInto(string code, Dictionary<string, string> target)
        {
            try
            {
                if (string.IsNullOrEmpty(LanguagesDirectory)) return false;
                string path = Path.Combine(LanguagesDirectory, $"{code}.json");
                if (!File.Exists(path)) return false;

                string json = File.ReadAllText(path);
                using var doc = JsonDocument.Parse(json);
                if (doc.RootElement.ValueKind != JsonValueKind.Object) return false;

                foreach (var prop in doc.RootElement.EnumerateObject())
                {
                    if (prop.Value.ValueKind == JsonValueKind.String)
                        target[prop.Name] = prop.Value.GetString() ?? "";
                }
                return true;
            }
            catch (Exception ex)
            {
                CoopLogger.Warn(LogCategory.UI, $"Failed to load language file '{code}.json': {ex.Message}");
                return false;
            }
        }
    }
}
