using System;
using EmberwardCoop.Core;
using EmberwardCoop.Localization;
using EmberwardCoop.UI;
using HarmonyLib;
using TMPro;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.EventSystems;
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

        [HarmonyPatch(typeof(CameraController), nameof(CameraController.IsCameraLocked), new Type[0])]
        [HarmonyPostfix]
        public static void CameraController_IsCameraLocked_Postfix(ref bool __result)
        {
            if (CoopInputGuard.IsCameraLocked)
                __result = true;
        }

        [HarmonyPatch(typeof(CameraController), nameof(CameraController.Update), new Type[0])]
        [HarmonyPrefix]
        public static bool CameraController_Update_Prefix(CameraController __instance)
        {
            if (!CoopInputGuard.IsCameraLocked) return true;
            if (__instance != null)
            {
                __instance.isMovingCamera = false;
                __instance.movingCameraMouseButtonIndex = -1;
                __instance.rightClickDiff = Vector3.zero;
                __instance.targetCameraLerpPosition = __instance.transform.position;
            }
            return false;
        }

        [HarmonyPatch(typeof(CameraController), nameof(CameraController.Update_KeyboardMouseControl), new Type[0])]
        [HarmonyPrefix]
        public static bool CameraController_UpdateKeyboardMouse_Prefix(CameraController __instance)
        {
            if (!CoopInputGuard.IsCameraLocked) return true;
            if (__instance != null)
            {
                __instance.isMovingCamera = false;
                __instance.movingCameraMouseButtonIndex = -1;
                __instance.rightClickDiff = Vector3.zero;
            }
            return false;
        }

        [HarmonyPatch(typeof(CameraController), nameof(CameraController.RotateClockwise), new Type[0])]
        [HarmonyPrefix]
        public static bool CameraController_RotateCW_Prefix() => !CoopInputGuard.IsCameraLocked;

        [HarmonyPatch(typeof(CameraController), nameof(CameraController.RotateCounterClockwise), new Type[0])]
        [HarmonyPrefix]
        public static bool CameraController_RotateCCW_Prefix() => !CoopInputGuard.IsCameraLocked;

        [HarmonyPatch(typeof(CameraController), nameof(CameraController.RequestHorizontalRotation), new[] { typeof(float) })]
        [HarmonyPrefix]
        public static bool CameraController_RequestHorizontalRotation_Prefix() => !CoopInputGuard.IsCameraLocked;

        [HarmonyPatch(typeof(CameraController), nameof(CameraController.UpdateHeldHorizontalRotation), new[] { typeof(bool), typeof(bool) })]
        [HarmonyPrefix]
        public static bool CameraController_UpdateHeldRotation_Prefix() => !CoopInputGuard.IsCameraLocked;

        [HarmonyPatch(typeof(TwoMouseButtonScrollRect), nameof(TwoMouseButtonScrollRect.OnBeginDrag), new[] { typeof(PointerEventData) })]
        [HarmonyPrefix]
        public static bool TwoMouseButtonScrollRect_OnBeginDrag_Prefix() => !CoopInputGuard.IsCameraLocked;

        [HarmonyPatch(typeof(TwoMouseButtonScrollRect), nameof(TwoMouseButtonScrollRect.OnDrag), new[] { typeof(PointerEventData) })]
        [HarmonyPrefix]
        public static bool TwoMouseButtonScrollRect_OnDrag_Prefix() => !CoopInputGuard.IsCameraLocked;

        [HarmonyPatch(typeof(InputController), nameof(InputController.GetAxis), new[] { typeof(eInputAction) })]
        [HarmonyPrefix]
        public static bool InputController_GetAxis_Prefix(ref float __result)
        {
            if (CoopInputGuard.IsCapturingWorldInput)
            {
                __result = 0f;
                return false;
            }
            return true;
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

        [HarmonyPatch(typeof(InputController), nameof(InputController.GetNegativeButtonDown), new[] { typeof(eInputAction) })]
        [HarmonyPrefix]
        public static bool InputController_GetNegativeButtonDown_Prefix(ref bool __result)
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

        [HarmonyPatch(typeof(InputController), nameof(InputController.GetNegativeButtonHold), new[] { typeof(eInputAction) })]
        [HarmonyPrefix]
        public static bool InputController_GetNegativeButtonHold_Prefix(ref bool __result)
        {
            if (CoopInputGuard.IsCapturingWorldInput)
            {
                __result = false;
                return false;
            }
            return true;
        }

        [HarmonyPatch(typeof(InputController), nameof(InputController.GetButtonUp), new[] { typeof(eInputAction) })]
        [HarmonyPrefix]
        public static bool InputController_GetButtonUp_Prefix(ref bool __result)
        {
            if (CoopInputGuard.IsCapturingWorldInput)
            {
                __result = false;
                return false;
            }
            return true;
        }

        [HarmonyPatch(typeof(InputController), nameof(InputController.GetNegativeButtonUp), new[] { typeof(eInputAction) })]
        [HarmonyPrefix]
        public static bool InputController_GetNegativeButtonUp_Prefix(ref bool __result)
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
        public static bool InteractionManager_Update_Prefix(InteractionManager __instance)
        {
            if (!CoopInputGuard.IsCapturingWorldInput) return true;
            try { __instance?.ClearCurrentTargets(); } catch { }
            return false;
        }

        [HarmonyPatch(typeof(InteractionManager), nameof(InteractionManager.HandleRaycast), new Type[0])]
        [HarmonyPrefix]
        public static bool InteractionManager_HandleRaycast_Prefix() => !CoopInputGuard.IsCapturingWorldInput;

        [HarmonyPatch(typeof(InteractionManager), nameof(InteractionManager.HandleInteractionInput), new Type[0])]
        [HarmonyPrefix]
        public static bool InteractionManager_HandleInteractionInput_Prefix() => !CoopInputGuard.IsCapturingWorldInput;

        [HarmonyPatch(typeof(ObjectPlacementHandler), nameof(ObjectPlacementHandler.Update), new Type[0])]
        [HarmonyPrefix]
        public static bool ObjectPlacementHandler_Update_Prefix() => !CoopInputGuard.IsCapturingWorldInput;

        [HarmonyPatch(typeof(UI_TowerControl), nameof(UI_TowerControl.Update), new Type[0])]
        [HarmonyPrefix]
        public static bool UI_TowerControl_Update_Prefix() => !CoopInputGuard.IsCapturingWorldInput;

        [HarmonyPatch(typeof(UI_TowerControl), nameof(UI_TowerControl.OnClickTowerOnField), new[] { typeof(ABaseTower) })]
        [HarmonyPrefix]
        public static bool UI_TowerControl_OnClickTower_Prefix() => !CoopInputGuard.IsCapturingWorldInput;

        [HarmonyPatch(typeof(CardController), nameof(CardController.Update), new Type[0])]
        [HarmonyPrefix]
        public static bool CardController_Update_Prefix() => !CoopInputGuard.IsCapturingWorldInput;

        [HarmonyPatch(typeof(UI_PauseMenu), nameof(UI_PauseMenu.OnTogglePauseMenu), new[] { typeof(bool), typeof(bool) })]
        [HarmonyPrefix]
        public static bool UI_PauseMenu_OnTogglePauseMenu_Prefix(bool isOn, bool isAutoPause)
        {
            // Keep Escape from opening the pause menu on the same frame it cancels a radial wheel.
            if (isOn && !isAutoPause && (CoopInputGuard.IsRadialWheelActive || CoopInputGuard.IsAbsorbingRelease))
                return false;
            return true;
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
                    {
                        if (tmps[i] == null) continue;
                        tmps[i].text = CoopLoc.T("modcfg.pause_btn");
                        tmps[i].enableAutoSizing = true;
                        tmps[i].fontSizeMin = 12f;
                        tmps[i].fontSizeMax = Mathf.Max(16f, tmps[i].fontSizeMax);
                    }
                    return;
                }

                var clone = UnityEngine.Object.Instantiate(__instance.button_Settings.gameObject, parent, false);
                clone.name = "Button_CoopModSettings";
                clone.SetActive(true);
                clone.transform.SetSiblingIndex(__instance.button_Settings.transform.GetSiblingIndex() + 1);

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
                    lbls[i].enableAutoSizing = true;
                    lbls[i].fontSizeMin = 12f;
                    lbls[i].fontSizeMax = Mathf.Max(16f, lbls[i].fontSize);
                    lbls[i].overflowMode = TextOverflowModes.Ellipsis;
                }

                CoopLogger.Info(LogCategory.UI, "Injected native CO-OP SETTINGS button into UI_PauseMenu.");

                InjectHostSaveButton(__instance, parent);
            }
            catch (Exception ex)
            {
                CoopLogger.Warn(LogCategory.UI, $"Could not inject Pause Menu Co-op button: {ex.Message}");
            }
        }

        private static void InjectHostSaveButton(UI_PauseMenu menu, Transform parent)
        {
            var s = Sync.SessionController.Instance;
            bool showSave = s != null && s.Net.IsHost && !GameRefs.IsInTitle;

            Transform existing = parent.Find("Button_CoopSaveRun");
            if (existing != null)
            {
                existing.gameObject.SetActive(showSave);
                var tmps = existing.GetComponentsInChildren<TMP_Text>(true);
                for (int i = 0; i < tmps.Length; i++)
                    if (tmps[i] != null) tmps[i].text = CoopLoc.T("save.pause_save_btn");
                return;
            }

            if (!showSave) return;

            var clone = UnityEngine.Object.Instantiate(menu.button_Settings.gameObject, parent, false);
            clone.name = "Button_CoopSaveRun";
            clone.SetActive(true);
            clone.transform.SetSiblingIndex(menu.button_Settings.transform.GetSiblingIndex() + 2);

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
                    NativeStyleProvider.PlayUiSelectSound();
                    if (Sync.SessionController.Instance?.SaveActiveCoopRun("manual_pause") == true)
                        GameRefs.ShowGameToast(CoopLoc.T("save.saved_toast"), 2.5f);
                }));
                btn.interactable = true;
            }

            var lbls = clone.GetComponentsInChildren<TMP_Text>(true);
            for (int i = 0; i < lbls.Length; i++)
            {
                if (lbls[i] == null) continue;
                lbls[i].text = CoopLoc.T("save.pause_save_btn");
                lbls[i].color = NativeStyleProvider.StatusReady;
                lbls[i].enableAutoSizing = true;
                lbls[i].fontSizeMin = 12f;
                lbls[i].fontSizeMax = Mathf.Max(16f, lbls[i].fontSize);
                lbls[i].overflowMode = TextOverflowModes.Ellipsis;
            }
        }
    }
}
