using System;
using EmberwardCoop.Core;
using EmberwardCoop.Localization;
using EmberwardCoop.UI;
using HarmonyLib;
using TMPro;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.UI;

namespace EmberwardCoop.Patches
{
    [HarmonyPatch]
    internal static class InputAndLocPatches
    {
        [HarmonyPatch(typeof(LocalizationManager), nameof(LocalizationManager.Start), new Type[0])]
        [HarmonyPostfix]
        public static void LocalizationManager_Start_Postfix()
        {
            CoopLoc.SyncFromGameLanguage();
            NativeStyleProvider.RefreshFontForActiveLanguage();
        }

        [HarmonyPatch(typeof(LocalizationManager), nameof(LocalizationManager.SetLanguage), new[] { typeof(SystemLanguage) })]
        [HarmonyPostfix]
        public static void LocalizationManager_SetLanguage_Postfix(SystemLanguage language)
        {
            CoopLoc.OnGameLanguageChanged(language);
            NativeStyleProvider.RefreshFontForActiveLanguage();
        }

        [HarmonyPatch(typeof(InputController), nameof(InputController.GetButtonDown), new[] { typeof(eInputAction) })]
        [HarmonyPrefix]
        public static bool InputController_GetButtonDown_Prefix(ref bool __result)
        {
            if (CoopInputGuard.IsCapturingWorldInput)
            {
                __result = false;
                return false;
            }
            return true;
        }

        [HarmonyPatch(typeof(InputController), nameof(InputController.GetButtonHold), new[] { typeof(eInputAction) })]
        [HarmonyPrefix]
        public static bool InputController_GetButtonHold_Prefix(ref bool __result)
        {
            if (CoopInputGuard.IsCapturingWorldInput)
            {
                __result = false;
                return false;
            }
            return true;
        }

        [HarmonyPatch(typeof(InteractionManager), nameof(InteractionManager.Update), new Type[0])]
        [HarmonyPrefix]
        public static bool InteractionManager_Update_Prefix()
        {
            return !CoopInputGuard.IsCapturingWorldInput;
        }

        [HarmonyPatch(typeof(UI_PauseMenu), nameof(UI_PauseMenu.OnTogglePauseMenu), new[] { typeof(bool), typeof(bool) })]
        [HarmonyPostfix]
        public static void UI_PauseMenu_OnTogglePauseMenu_Postfix(UI_PauseMenu __instance, bool isOn)
        {
            if (!isOn || __instance == null || __instance.button_Settings == null) return;

            try
            {
                Transform parent = __instance.button_Settings.transform.parent;
                if (parent == null) return;

                Transform existing = parent.Find("Button_CoopModSettings");
                if (existing != null)
                {
                    var tmps = existing.GetComponentsInChildren<TMP_Text>(true);
                    for (int i = 0; i < tmps.Length; i++)
                        if (tmps[i] != null) tmps[i].text = CoopLoc.T("modcfg.pause_btn");
                    return;
                }

                var clone = UnityEngine.Object.Instantiate(__instance.button_Settings.gameObject, parent, false);
                clone.name = "Button_CoopModSettings";
                clone.SetActive(true);
                clone.transform.SetSiblingIndex(__instance.button_Settings.transform.GetSiblingIndex() + 1);

                // Remove native localization scripts so they don't overwrite our label
                var mbs = clone.GetComponentsInChildren<MonoBehaviour>(true);
                for (int i = 0; i < mbs.Length; i++)
                {
                    if (mbs[i] == null) continue;
                    string tn = mbs[i].GetIl2CppType()?.Name ?? "";
                    if (tn.IndexOf("Localiz", StringComparison.OrdinalIgnoreCase) >= 0)
                        UnityEngine.Object.Destroy(mbs[i]);
                }

                var btn = clone.GetComponent<Button>();
                if (btn != null)
                {
                    btn.onClick = new Button.ButtonClickedEvent();
                    btn.onClick.AddListener((UnityAction)(() =>
                    {
                        NativeStyleProvider.PlayUiClickSound();
                        CoopSettingsModal.Instance?.Open();
                    }));
                    btn.interactable = true;
                }

                var lbls = clone.GetComponentsInChildren<TMP_Text>(true);
                for (int i = 0; i < lbls.Length; i++)
                {
                    if (lbls[i] == null) continue;
                    lbls[i].text = CoopLoc.T("modcfg.pause_btn");
                    lbls[i].color = NativeStyleProvider.EmberGold;
                }

                CoopLogger.Info(LogCategory.UI, "Injected native CO-OP SETTINGS button into UI_PauseMenu.");
            }
            catch (Exception ex)
            {
                CoopLogger.Warn(LogCategory.UI, $"Could not inject Pause Menu Co-op button: {ex.Message}");
            }
        }
    }
}
