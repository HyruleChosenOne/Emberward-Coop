using System;
using EmberwardCoop.Core;
using HarmonyLib;

namespace EmberwardCoop.Patches
{
    public static class PatchInstaller
    {
        public static void ApplyAll(Harmony harmony)
        {
            Type[] modules =
            {
                typeof(LifecyclePatches),
                typeof(EconomyHandPatches),
                typeof(BoardActionPatches),
                typeof(WaveCombatPatches),
                typeof(ModalQuestPatches),
                typeof(InputAndLocPatches)
            };

            int ok = 0;
            int fail = 0;

            for (int i = 0; i < modules.Length; i++)
            {
                var mod = modules[i];
                try
                {
                    harmony.PatchAll(mod);
                    ok++;
                    CoopLogger.Info(LogCategory.General, $"Patch module applied: {mod.Name}");
                }
                catch (Exception ex)
                {
                    fail++;
                    CoopLogger.Error(LogCategory.General, $"Patch module FAILED: {mod.Name}", ex);
                }
            }

            CoopLogger.Info(LogCategory.General, $"Harmony patching finished: {ok}/{modules.Length} modules active ({fail} failed).");
        }
    }
}
