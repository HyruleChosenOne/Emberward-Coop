using System;
using System.Collections.Generic;
using BepInEx.Logging;

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

    public static class CoopLogger
    {
        private static ManualLogSource _log;
        private static readonly List<string> _feed = new List<string>(64);
        private static readonly object _feedLock = new object();
        public const int MaxFeedEntries = 60;

        public static event Action<string> OnFeedEntryAdded;

        public static void Initialize(ManualLogSource log)
        {
            _log = log;
        }

        private static bool IsVerboseCategoryEnabled(LogCategory category) => category switch
        {
            LogCategory.Net => CoopConfig.DebugLogNetwork?.Value ?? false,
            LogCategory.Sync => CoopConfig.DebugLogSync?.Value ?? true,
            LogCategory.Hash => CoopConfig.DebugLogStateHash?.Value ?? false,
            LogCategory.Quest => CoopConfig.DebugLogQuests?.Value ?? true,
            LogCategory.Modal => CoopConfig.DebugLogModals?.Value ?? true,
            _ => true
        };

        public static void Info(LogCategory category, string message, bool addToFeed = false)
        {
            string formatted = $"[{category}] {message}";
            _log?.LogInfo(formatted);
            if (addToFeed)
                PushFeed(message);
        }

        public static void Debug(LogCategory category, string message)
        {
            if (!IsVerboseCategoryEnabled(category)) return;
            _log?.LogInfo($"[{category}:DBG] {message}");
        }

        public static void Warn(LogCategory category, string message, bool addToFeed = false)
        {
            string formatted = $"[{category}] {message}";
            _log?.LogWarning(formatted);
            if (addToFeed)
                PushFeed($"! {message}");
        }

        public static void Error(LogCategory category, string message, Exception ex = null)
        {
            string formatted = ex != null ? $"[{category}] {message}: {ex}" : $"[{category}] {message}";
            _log?.LogError(formatted);
        }

        public static void PushFeed(string message)
        {
            string entry = $"[{DateTime.Now:HH:mm:ss}] {message}";
            lock (_feedLock)
            {
                _feed.Add(entry);
                if (_feed.Count > MaxFeedEntries)
                    _feed.RemoveAt(0);
            }
            try { OnFeedEntryAdded?.Invoke(entry); } catch { }
        }

        public static List<string> GetFeedSnapshot()
        {
            lock (_feedLock)
            {
                return new List<string>(_feed);
            }
        }

        public static void ClearFeed()
        {
            lock (_feedLock)
            {
                _feed.Clear();
            }
        }
    }
}
