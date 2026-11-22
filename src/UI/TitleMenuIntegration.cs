using System;
using EmberwardCoop.Core;
using EmberwardCoop.Localization;
using TMPro;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.UI;

namespace EmberwardCoop.UI
{
    public static class TitleMenuIntegration
    {
        private static GameObject _injectedButton;
        private static bool _subscribed;

        public static void TryInjectTitleButton(Action onClickCoop)
        {
            if (!_subscribed)
            {
                _subscribed = true;
                CoopLoc.OnLanguageChanged += RefreshInjectedLabel;
            }

            if (!GameRefs.IsInTitle)
            {
                _injectedButton = null;
                return;
            }
            if (_injectedButton != null) return;

            var cp = GameRefs.GetCoinPage();
            if (cp == null || cp.verticalLayoutGroup_LeftButtons == null) return;

            NativeStyleProvider.HarvestFromScene();

            Button template = cp.button_Options != null ? cp.button_Options : cp.button_StartGame;
            if (template == null) return;

            try
            {
                var clone = UnityEngine.Object.Instantiate(template.gameObject, cp.verticalLayoutGroup_LeftButtons.transform, false);
                clone.name = "Button_CoopLobby";
                clone.SetActive(true);

                int targetSibling = Math.Min(2, cp.verticalLayoutGroup_LeftButtons.transform.childCount - 1);
                clone.transform.SetSiblingIndex(targetSibling);

                // Strip localization scripts that might overwrite our localized mod label
                var mbs = clone.GetComponentsInChildren<MonoBehaviour>(true);
                for (int i = 0; i < mbs.Length; i++)
                {
                    if (mbs[i] == null) continue;
                    string tName = mbs[i].GetIl2CppType()?.Name ?? "";
                    if (tName.IndexOf("Localiz", StringComparison.OrdinalIgnoreCase) >= 0)
                        UnityEngine.Object.Destroy(mbs[i]);
                }

                var btn = clone.GetComponent<Button>();
                if (btn != null)
                {
                    btn.onClick = new Button.ButtonClickedEvent();
                    btn.onClick.AddListener((UnityAction)(() =>
                    {
                        NativeStyleProvider.PlayUiClickSound();
                        onClickCoop?.Invoke();
                    }));
                    btn.interactable = true;
                }

                var cg = clone.GetComponent<CanvasGroup>();
                if (cg != null) { cg.alpha = 1f; cg.interactable = true; cg.blocksRaycasts = true; }

                _injectedButton = clone;
                RefreshInjectedLabel();
                CoopLogger.Info(LogCategory.UI, "Injected native CO-OP button into CoinPage title menu.");
            }
            catch (Exception ex)
            {
                CoopLogger.Warn(LogCategory.UI, $"Could not clone native title button: {ex.Message}");
            }
        }

        private static void RefreshInjectedLabel()
        {
            if (_injectedButton == null) return;
            try
            {
                var tmps = _injectedButton.GetComponentsInChildren<TMP_Text>(true);
                for (int i = 0; i < tmps.Length; i++)
                {
                    if (tmps[i] == null) continue;
                    tmps[i].text = CoopLoc.T("title.coop_button");
                    tmps[i].color = NativeStyleProvider.EmberGold;
                    tmps[i].enableAutoSizing = true;
                    tmps[i].fontSizeMin = 14f;
                    tmps[i].fontSizeMax = Mathf.Max(22f, tmps[i].fontSize);
                    tmps[i].overflowMode = TextOverflowModes.Ellipsis;
                    if (NativeStyleProvider.PrimaryFont != null) tmps[i].font = NativeStyleProvider.PrimaryFont;
                }
            }
            catch { }
        }

        public static void SetMainMenuButtonsVisible(bool visible)
        {
            if (!GameRefs.IsInTitle) return;
            try
            {
                var cp = GameRefs.GetCoinPage();
                if (cp?.verticalLayoutGroup_LeftButtons == null) return;
                var go = cp.verticalLayoutGroup_LeftButtons.gameObject;
                var cg = go.GetComponent<CanvasGroup>();
                if (cg == null) cg = go.AddComponent<CanvasGroup>();
                cg.alpha = visible ? 1f : 0f;
                cg.interactable = visible;
                cg.blocksRaycasts = visible;
            }
            catch { }
        }
    }
}
