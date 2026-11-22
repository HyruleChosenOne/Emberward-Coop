using BepInEx;
using BepInEx.Unity.IL2CPP;
using EmberwardCoop.Localization;
using EmberwardCoop.Patches;
using EmberwardCoop.Sync;
using HarmonyLib;
using Il2CppInterop.Runtime;
using Il2CppInterop.Runtime.Injection;
using UnityEngine;

namespace EmberwardCoop.Core
{
    [BepInPlugin(PluginGuid, PluginName, PluginVersion)]
    public sealed class CoopPlugin : BasePlugin
    {
        public const string PluginGuid = "com.emberward.coop";
        public const string PluginName = "Emberward Online P2P & LAN Co-op";
        public const string PluginVersion = "1.0.0";

        public static Harmony HarmonyInstance { get; private set; }
        public static SessionController Session { get; private set; }

        private delegate void SetRunInBackgroundDelegate(bool value);
        private static SetRunInBackgroundDelegate _setRunInBackground;

        public static void ForceRunInBackground()
        {
            try
            {
                _setRunInBackground ??= IL2CPP.ResolveICall<SetRunInBackgroundDelegate>("UnityEngine.Application::set_runInBackground(System.Boolean)");
                _setRunInBackground?.Invoke(true);
            }
            catch { }
        }

        public override void Load()
        {
            CoopLogger.Initialize(Log);
            CoopConfig.Bind(Config);
            CoopLoc.Initialize();
            CoopLogger.Info(LogCategory.General, $"Loading {PluginName} v{PluginVersion}...");

            ForceRunInBackground();

            Session = new SessionController();
            Session.Initialize();

            ClassInjector.RegisterTypeInIl2Cpp<CoopRunner>();
            var runnerGo = new GameObject("EmberwardCoop_Runner");
            UnityEngine.Object.DontDestroyOnLoad(runnerGo);
            runnerGo.hideFlags = HideFlags.HideAndDontSave;
            runnerGo.AddComponent<CoopRunner>();

            HarmonyInstance = new Harmony(PluginGuid);
            PatchInstaller.ApplyAll(HarmonyInstance);

            CoopLogger.Info(LogCategory.General, $"{PluginName} v{PluginVersion} ready! Click CO-OP on the Title Screen or press {CoopConfig.ToggleLobbyKey.Value} anytime.");
        }
    }
}
