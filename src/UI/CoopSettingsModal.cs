using System;
using System.Collections.Generic;
using BepInEx.Configuration;
using EmberwardCoop.Core;
using EmberwardCoop.Localization;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace EmberwardCoop.UI
{
    public sealed class CoopSettingsModal
    {
        public static CoopSettingsModal Instance { get; private set; }

        private GameObject _root;
        private bool _isOpen;

        private ConfigEntry<KeyCode> _rebindingTarget;

        private TMP_Text _titleText;
        private TMP_Text _langText;
        private TMP_Text _closeBtnText;

        private readonly List<(string locKey, TMP_Text label)> _staticLabels = new List<(string, TMP_Text)>();
        private readonly List<(ConfigEntry<KeyCode> entry, TMP_Text btnText)> _keybindButtons = new List<(ConfigEntry<KeyCode>, TMP_Text)>();

        private TMP_Text _valStickerScale;
        private TMP_Text _valStickerVol;
        private TMP_Text _valMuteStickers;
        private TMP_Text _valCompactHud;
        private TMP_Text _valAutoPause;

        public bool IsOpen => _isOpen;

        public CoopSettingsModal()
        {
            Instance = this;
            CoopLoc.OnLanguageChanged += RefreshLabels;
        }

        public void Open()
        {
            EnsureBuilt();
            _isOpen = true;
            _rebindingTarget = null;
            _root.SetActive(true);
            RefreshLabels();
        }

        public void Close()
        {
            _isOpen = false;
            _rebindingTarget = null;
            if (_root != null && _root.activeSelf)
                _root.SetActive(false);
        }

        public void Toggle()
        {
            if (_isOpen) Close();
            else Open();
        }

        public void Tick()
        {
            if (!_isOpen) return;

            if (_rebindingTarget != null)
            {
                if (Input.GetKeyDown(KeyCode.Escape))
                {
                    _rebindingTarget = null;
                    RefreshLabels();
                    return;
                }

                foreach (KeyCode kc in CaptureCandidates)
                {
                    if (Input.GetKeyDown(kc))
                    {
                        _rebindingTarget.Value = kc;
                        _rebindingTarget = null;
                        NativeStyleProvider.PlayUiClickSound(1.25f);
                        RefreshLabels();
                        return;
                    }
                }
                return;
            }

            if (Input.GetKeyDown(KeyCode.Escape))
                Close();
        }

        private static readonly KeyCode[] CaptureCandidates = BuildCaptureCandidates();

        private static KeyCode[] BuildCaptureCandidates()
        {
            var list = new List<KeyCode>();
            for (KeyCode k = KeyCode.A; k <= KeyCode.Z; k++) list.Add(k);
            for (KeyCode k = KeyCode.Alpha0; k <= KeyCode.Alpha9; k++) list.Add(k);
            for (KeyCode k = KeyCode.F1; k <= KeyCode.F12; k++) list.Add(k);
            list.Add(KeyCode.LeftAlt);
            list.Add(KeyCode.RightAlt);
            list.Add(KeyCode.LeftShift);
            list.Add(KeyCode.RightShift);
            list.Add(KeyCode.LeftControl);
            list.Add(KeyCode.Tab);
            list.Add(KeyCode.BackQuote);
            list.Add(KeyCode.Space);
            list.Add(KeyCode.Mouse2);
            list.Add(KeyCode.Mouse3);
            list.Add(KeyCode.Mouse4);
            return list.ToArray();
        }

        private void EnsureBuilt()
        {
            if (_root != null) return;
            NativeStyleProvider.HarvestFromScene();
            var canvas = NativeStyleProvider.GetOrCreateOverlayCanvas();

            _root = new GameObject("EWCoop_ModSettingsModal");
            _root.transform.SetParent(canvas.transform, false);
            NativeStyleProvider.StretchFill(_root.AddComponent<RectTransform>());
            _root.AddComponent<Image>().color = new Color(0.02f, 0.02f, 0.04f, 0.72f);

            var win = NativeStyleProvider.CreatePanel(_root.transform, "Window", NativeStyleProvider.PanelBg, addBorder: true);
            var wrt = win.GetComponent<RectTransform>();
            wrt.anchorMin = new Vector2(0.5f, 0.5f);
            wrt.anchorMax = new Vector2(0.5f, 0.5f);
            wrt.sizeDelta = new Vector2(560f, 590f);

            var vlg = win.AddComponent<VerticalLayoutGroup>();
            vlg.padding = new RectOffset(22, 22, 16, 16);
            vlg.spacing = 8f;
            vlg.childControlWidth = true;
            vlg.childControlHeight = true;
            vlg.childForceExpandWidth = true;
            vlg.childForceExpandHeight = false;

            var hdr = CreateRow(win.transform, "Header", 36f);
            _titleText = NativeStyleProvider.CreateLabel(hdr.transform, CoopLoc.T("modcfg.title"), 21f, NativeStyleProvider.EmberGold, TextAlignmentOptions.MidlineLeft, FontStyles.Bold);
            _titleText.gameObject.AddComponent<LayoutElement>().flexibleWidth = 1f;
            var xBtn = NativeStyleProvider.CreateButton(hdr.transform, "X", NativeStyleProvider.ButtonSecondary, Close, 15f, 32f);
            xBtn.GetComponent<LayoutElement>().preferredWidth = 38f;

            AddKeybindRow(win.transform, "modcfg.key_lobby", CoopConfig.ToggleLobbyKey);
            AddKeybindRow(win.transform, "modcfg.key_emote", CoopConfig.EmoteWheelKey);
            AddKeybindRow(win.transform, "modcfg.key_ping", CoopConfig.PingHotkey);
            AddKeybindRow(win.transform, "modcfg.key_sketch", CoopConfig.SketchModifierKey);
            AddKeybindRow(win.transform, "modcfg.key_undo", CoopConfig.UndoBuildKey);

            _valStickerScale = AddStepperRow(win.transform, "modcfg.sticker_scale",
                () => { CoopConfig.StickerScale.Value = Mathf.Clamp((float)Math.Round(CoopConfig.StickerScale.Value - 0.1f, 1), 0.6f, 1.6f); RefreshLabels(); },
                () => { CoopConfig.StickerScale.Value = Mathf.Clamp((float)Math.Round(CoopConfig.StickerScale.Value + 0.1f, 1), 0.6f, 1.6f); RefreshLabels(); });

            _valStickerVol = AddStepperRow(win.transform, "modcfg.sticker_vol",
                () => { CoopConfig.StickerAudioVolume.Value = Mathf.Clamp((float)Math.Round(CoopConfig.StickerAudioVolume.Value - 0.15f, 2), 0f, 1f); RefreshLabels(); },
                () => { CoopConfig.StickerAudioVolume.Value = Mathf.Clamp((float)Math.Round(CoopConfig.StickerAudioVolume.Value + 0.15f, 2), 0f, 1f); RefreshLabels(); });

            _valMuteStickers = AddToggleRow(win.transform, "modcfg.mute_stickers",
                () => { CoopConfig.MuteTeammateStickers.Value = !CoopConfig.MuteTeammateStickers.Value; RefreshLabels(); });

            _valCompactHud = AddToggleRow(win.transform, "modcfg.compact_hud",
                () => { CoopConfig.CompactHudMode.Value = !CoopConfig.CompactHudMode.Value; RefreshLabels(); });

            _valAutoPause = AddToggleRow(win.transform, "modcfg.auto_pause",
                () => { CoopConfig.AutoPauseOnDisconnect.Value = !CoopConfig.AutoPauseOnDisconnect.Value; RefreshLabels(); });

            var foot = CreateRow(win.transform, "Footer", 42f);
            _langText = NativeStyleProvider.CreateLabel(foot.transform, "", 13f, NativeStyleProvider.TextMuted, TextAlignmentOptions.MidlineLeft);
            _langText.gameObject.AddComponent<LayoutElement>().flexibleWidth = 1f;

            var closeBtn = NativeStyleProvider.CreateButton(foot.transform, CoopLoc.T("modcfg.close"), NativeStyleProvider.ButtonPrimary, Close, 15f, 38f);
            closeBtn.GetComponent<LayoutElement>().preferredWidth = 130f;
            _closeBtnText = closeBtn.GetComponentInChildren<TMP_Text>();

            _root.SetActive(false);
        }

        private void AddKeybindRow(Transform parent, string locKey, ConfigEntry<KeyCode> entry)
        {
            var row = CreateRow(parent, locKey, 36f);
            var lbl = NativeStyleProvider.CreateLabel(row.transform, CoopLoc.T(locKey), 14f, NativeStyleProvider.TextLight, TextAlignmentOptions.MidlineLeft);
            lbl.gameObject.AddComponent<LayoutElement>().flexibleWidth = 1f;
            _staticLabels.Add((locKey, lbl));

            TMP_Text btnTxt = null;
            var btn = NativeStyleProvider.CreateButton(row.transform, entry.Value.ToString(), NativeStyleProvider.CardBg, () =>
            {
                _rebindingTarget = entry;
                if (btnTxt != null) btnTxt.text = CoopLoc.T("modcfg.press_any_key");
            }, 13f, 32f);
            btn.GetComponent<LayoutElement>().preferredWidth = 175f;
            btnTxt = btn.GetComponentInChildren<TMP_Text>();
            _keybindButtons.Add((entry, btnTxt));
        }

        private TMP_Text AddStepperRow(Transform parent, string locKey, Action onMinus, Action onPlus)
        {
            var row = CreateRow(parent, locKey, 36f);
            var lbl = NativeStyleProvider.CreateLabel(row.transform, CoopLoc.T(locKey), 14f, NativeStyleProvider.TextLight, TextAlignmentOptions.MidlineLeft);
            lbl.gameObject.AddComponent<LayoutElement>().flexibleWidth = 1f;
            _staticLabels.Add((locKey, lbl));

            var minus = NativeStyleProvider.CreateButton(row.transform, "-", NativeStyleProvider.ButtonSecondary, onMinus, 16f, 30f);
            minus.GetComponent<LayoutElement>().preferredWidth = 34f;

            var val = NativeStyleProvider.CreateLabel(row.transform, "", 14f, NativeStyleProvider.EmberGold, TextAlignmentOptions.Center, FontStyles.Bold);
            val.gameObject.AddComponent<LayoutElement>().preferredWidth = 96f;

            var plus = NativeStyleProvider.CreateButton(row.transform, "+", NativeStyleProvider.ButtonSecondary, onPlus, 16f, 30f);
            plus.GetComponent<LayoutElement>().preferredWidth = 34f;
            return val;
        }

        private TMP_Text AddToggleRow(Transform parent, string locKey, Action onToggle)
        {
            var row = CreateRow(parent, locKey, 36f);
            var lbl = NativeStyleProvider.CreateLabel(row.transform, CoopLoc.T(locKey), 14f, NativeStyleProvider.TextLight, TextAlignmentOptions.MidlineLeft);
            lbl.gameObject.AddComponent<LayoutElement>().flexibleWidth = 1f;
            _staticLabels.Add((locKey, lbl));

            var btn = NativeStyleProvider.CreateButton(row.transform, "", NativeStyleProvider.ButtonSecondary, onToggle, 13f, 30f);
            btn.GetComponent<LayoutElement>().preferredWidth = 175f;
            return btn.GetComponentInChildren<TMP_Text>();
        }

        private void RefreshLabels()
        {
            if (_root == null) return;
            if (_titleText != null) _titleText.text = CoopLoc.T("modcfg.title");
            if (_closeBtnText != null) _closeBtnText.text = CoopLoc.T("modcfg.close");
            if (_langText != null) _langText.text = CoopLoc.F("modcfg.language", CoopLoc.ActiveLangCode);

            for (int i = 0; i < _staticLabels.Count; i++)
                if (_staticLabels[i].label != null) _staticLabels[i].label.text = CoopLoc.T(_staticLabels[i].locKey);

            for (int i = 0; i < _keybindButtons.Count; i++)
            {
                var (entry, txt) = _keybindButtons[i];
                if (txt == null) continue;
                txt.text = _rebindingTarget == entry ? CoopLoc.T("modcfg.press_any_key") : entry.Value.ToString();
            }

            if (_valStickerScale != null) _valStickerScale.text = $"{CoopConfig.StickerScale.Value:0.0}x";
            if (_valStickerVol != null) _valStickerVol.text = $"{Mathf.RoundToInt(CoopConfig.StickerAudioVolume.Value * 100)}%";
            if (_valMuteStickers != null) _valMuteStickers.text = CoopConfig.MuteTeammateStickers.Value ? CoopLoc.T("modcfg.on") : CoopLoc.T("modcfg.off");
            if (_valCompactHud != null) _valCompactHud.text = CoopConfig.CompactHudMode.Value ? CoopLoc.T("modcfg.on") : CoopLoc.T("modcfg.off");
            if (_valAutoPause != null) _valAutoPause.text = CoopConfig.AutoPauseOnDisconnect.Value ? CoopLoc.T("modcfg.on") : CoopLoc.T("modcfg.off");
        }

        private static GameObject CreateRow(Transform parent, string name, float height)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            var hlg = go.AddComponent<HorizontalLayoutGroup>();
            hlg.spacing = 8f;
            hlg.childAlignment = TextAnchor.MiddleLeft;
            hlg.childControlWidth = true;
            hlg.childControlHeight = true;
            hlg.childForceExpandWidth = false;
            var le = go.AddComponent<LayoutElement>();
            le.minHeight = height;
            le.preferredHeight = height;
            return go;
        }
    }
}
