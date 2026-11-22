using System;
using System.Collections.Generic;
using BepInEx.Logging;
using UnityEngine;

namespace EmberwardCoop.Core
{
    public enum LogCategory
    {
        General,
        Net,
        Sync,
        Hash,
        Quest,
        Modal,
        UI,
        Presence
    }

    public enum ChatEntryKind
    {
        PlayerChat = 0,
        QuickCallout = 1,
        Emote = 2,
        ConnectionSuccess = 3
    }

    public sealed class ChatLogEntry
    {
        public ChatEntryKind Kind;
        public string Timestamp;
        public string PlainText;
        public string RichText;
        public float CreatedUnscaledTime;
    }

    public static class CoopLogger
    {
        private static ManualLogSource _log;
        private static readonly List<ChatLogEntry> _chatEntries = new List<ChatLogEntry>(64);
        private static readonly object _chatLock = new object();
        public const int MaxFeedEntries = 50;

        public static float LastChatActivityTime { get; private set; } = -999f;
        public static event Action<ChatLogEntry> OnFeedEntryAdded;

        public static void Initialize(ManualLogSource log)
        {
            _log = log;
        }

        private static bool IsVerboseCategoryEnabled(LogCategory category) => category switch
        {
            LogCategory.Net => CoopConfig.DebugLogNetwork?.Value ?? false,
            LogCategory.Sync => CoopConfig.DebugLogSync?.Value ?? false,
            LogCategory.Hash => CoopConfig.DebugLogStateHash?.Value ?? false,
            LogCategory.Quest => CoopConfig.DebugLogQuests?.Value ?? false,
            LogCategory.Modal => CoopConfig.DebugLogModals?.Value ?? false,
            _ => false
        };

        public static void Info(LogCategory category, string message)
        {
            _log?.LogInfo($"[{category}] {message}");
        }

        public static void Debug(LogCategory category, string message)
        {
            if (!IsVerboseCategoryEnabled(category)) return;
            _log?.LogInfo($"[{category}:DBG] {message}");
        }

        public static void Warn(LogCategory category, string message)
        {
            _log?.LogWarning($"[{category}] {message}");
        }

        public static void Error(LogCategory category, string message, Exception ex = null)
        {
            string formatted = ex != null ? $"[{category}] {message}: {ex}" : $"[{category}] {message}";
            _log?.LogError(formatted);
        }

        public static void PushChatMessage(ChatEntryKind kind, string text, Color? accentColor = null)
        {
            if (string.IsNullOrWhiteSpace(text)) return;

            string ts = DateTime.Now.ToString("HH:mm");
            string clean = text.Trim();
            string plain = $"[{ts}] {clean}";

            string rich;
            switch (kind)
            {
                case ChatEntryKind.ConnectionSuccess:
                    rich = $"<color=#A89984>[{ts}]</color> <color=#7BE08A>{EscapeTmp(clean)}</color>";
                    break;
                case ChatEntryKind.Emote:
                    rich = accentColor.HasValue
                        ? $"<color=#A89984>[{ts}]</color> <color=#{ColorUtility.ToHtmlStringRGB(accentColor.Value)}>{EscapeTmp(clean)}</color>"
                        : $"<color=#A89984>[{ts}]</color> <color=#F7C95A>{EscapeTmp(clean)}</color>";
                    break;
                case ChatEntryKind.QuickCallout:
                    rich = accentColor.HasValue
                        ? $"<color=#A89984>[{ts}]</color> <color=#{ColorUtility.ToHtmlStringRGB(accentColor.Value)}>{EscapeTmp(clean)}</color>"
                        : $"<color=#A89984>[{ts}]</color> <color=#F5DFB0>{EscapeTmp(clean)}</color>";
                    break;
                default:
                    rich = accentColor.HasValue
                        ? $"<color=#A89984>[{ts}]</color> <color=#{ColorUtility.ToHtmlStringRGB(accentColor.Value)}>{EscapeTmp(clean)}</color>"
                        : $"<color=#A89984>[{ts}]</color> <color=#FAF0DC>{EscapeTmp(clean)}</color>";
                    break;
            }

            float now = 0f;
            try { now = Time.unscaledTime; } catch { }

            var entry = new ChatLogEntry
            {
                Kind = kind,
                Timestamp = ts,
                PlainText = plain,
                RichText = rich,
                CreatedUnscaledTime = now
            };

            lock (_chatLock)
            {
                _chatEntries.Add(entry);
                if (_chatEntries.Count > MaxFeedEntries)
                    _chatEntries.RemoveAt(0);
                LastChatActivityTime = now;
            }

            try { OnFeedEntryAdded?.Invoke(entry); } catch { }
        }

        private static string EscapeTmp(string s)
            => string.IsNullOrEmpty(s) ? "" : s.Replace("<", "\u2039").Replace(">", "\u203A");

        public static List<ChatLogEntry> GetChatSnapshot()
        {
            lock (_chatLock)
            {
                return new List<ChatLogEntry>(_chatEntries);
            }
        }

        public static int ChatCount
        {
            get { lock (_chatLock) { return _chatEntries.Count; } }
        }

        public static void ClearFeed()
        {
            lock (_chatLock)
            {
                _chatEntries.Clear();
                LastChatActivityTime = -999f;
            }
        }
    }
}
