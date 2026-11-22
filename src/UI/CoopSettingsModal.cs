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
        private RectTransform _winRt;
        private CanvasGroup _winCg;
        private float _openAnimProgress = 1f;
        private bool _isOpen;

        private ConfigEntry<KeyCode> _rebindingTarget;

        private TMP_Text _titleText;
        private TMP_Text _langText;
        private TMP_Text _closeBtnText;

        private readonly List<(string locKey, TMP_Text label)> _staticLabels = new List<(string, TMP_Text)>();
        private readonly List<(ConfigEntry<KeyCode> entry, TMP_Text btnText)> _keybindButtons = new List<(ConfigEntry<KeyCode>, TMP_Text)>();

        private TMP_Text _valStickerScale;
        private TMP_Text _valStickerVol;
        private (TMP_Text txt, Image chk) _valMuteStickers;
        private (TMP_Text txt, Image chk) _valCompactHud;
        private (TMP_Text txt, Image chk) _valAutoPause;
        private readonly List<(Image swatchBg, TMP_Text checkMark)> _colorSwatches = new List<(Image, TMP_Text)>();
        private TMP_Text _colorRowLabel;

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
            _openAnimProgress = 0f;
            _root.SetActive(true);
            RefreshLabels();
            NativeStyleProvider.PlayUiOpenSound(true);
        }

        public void Close()
        {
            bool wasOpen = _isOpen;
            _isOpen = false;
            _rebindingTarget = null;
            if (_root != null && _root.activeSelf)
                _root.SetActive(false);
            if (wasOpen) NativeStyleProvider.PlayUiOpenSound(false);
        }

        public void Toggle()
        {
            if (_isOpen) Close();
            else Open();
        }

        public void Tick()
        {
            if (!_isOpen) return;

            NativeStyleProvider.TickOpenTransition(_winRt, _winCg, ref _openAnimProgress, Time.unscaledDeltaTime, 0.12f);

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
            _root.AddComponent<Image>().color = EmberwardTheme.ModalScrim;

            var win = NativeStyleProvider.CreatePanel(_root.transform, "Window", NativeStyleProvider.PanelBg, addBorder: true);
            _winRt = win.GetComponent<RectTransform>();
            _winRt.anchorMin = new Vector2(0.5f, 0.5f);
            _winRt.anchorMax = new Vector2(0.5f, 0.5f);
            _winRt.sizeDelta = new Vector2(488f, 440f);
            _winCg = win.AddComponent<CanvasGroup>();

            var winFit = win.AddComponent<ContentSizeFitter>();
            winFit.horizontalFit = ContentSizeFitter.FitMode.Unconstrained;
            winFit.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

            var vlg = win.AddComponent<VerticalLayoutGroup>();
            vlg.padding = new RectOffset(20, 20, 17, 18);
            vlg.spacing = 7f;
            vlg.childControlWidth = true;
            vlg.childControlHeight = true;
            vlg.childForceExpandWidth = true;
            vlg.childForceExpandHeight = false;

            var hdr = CreateRow(win.transform, "Header", 30f);
            _titleText = NativeStyleProvider.CreateLabel(hdr.transform, CoopLoc.T("modcfg.title"), 17.5f, NativeStyleProvider.EmberGold, TextAlignmentOptions.MidlineLeft, FontStyles.Bold, autoSize: true, minFontSize: 13f, addShadow: true);
            _titleText.gameObject.AddComponent<LayoutElement>().flexibleWidth = 1f;
            NativeStyleProvider.CreateCloseButton(hdr.transform, Close, 26f);

            NativeStyleProvider.CreateDivider(win.transform, "HdrDiv");

            var scroll = NativeStyleProvider.CreateScrollView(win.transform, "ModSettingsScroll", NativeStyleProvider.PanelWellBg, out RectTransform contentRt, padding: 8f, spacing: 5f);
            var scrollLe = scroll.gameObject.AddComponent<LayoutElement>();
            scrollLe.minHeight = 260f;
            scrollLe.preferredHeight = 360f;

            AddColorPickerRow(contentRt);
            NativeStyleProvider.CreateDivider(contentRt, "ColorDiv");

            AddKeybindRow(contentRt, "modcfg.key_lobby", CoopConfig.ToggleLobbyKey);
            AddKeybindRow(contentRt, "modcfg.key_hud", CoopConfig.ToggleHudKey);
            AddKeybindRow(contentRt, "modcfg.key_emote", CoopConfig.EmoteWheelKey);
            AddKeybindRow(contentRt, "modcfg.key_ping", CoopConfig.PingHotkey);
            AddKeybindRow(contentRt, "modcfg.key_sketch", CoopConfig.SketchModifierKey);
            AddKeybindRow(contentRt, "modcfg.key_undo", CoopConfig.UndoBuildKey);

            NativeStyleProvider.CreateDivider(contentRt, "MidDiv");

            _valStickerScale = AddStepperRow(contentRt, "modcfg.sticker_scale",
                () => { CoopConfig.StickerScale.Value = Mathf.Clamp((float)Math.Round(CoopConfig.StickerScale.Value - 0.1f, 1), 0.6f, 1.6f); RefreshLabels(); },
                () => { CoopConfig.StickerScale.Value = Mathf.Clamp((float)Math.Round(CoopConfig.StickerScale.Value + 0.1f, 1), 0.6f, 1.6f); RefreshLabels(); });

            _valStickerVol = AddStepperRow(contentRt, "modcfg.sticker_vol",
                () => { CoopConfig.StickerAudioVolume.Value = Mathf.Clamp((float)Math.Round(CoopConfig.StickerAudioVolume.Value - 0.15f, 2), 0f, 1f); RefreshLabels(); },
                () => { CoopConfig.StickerAudioVolume.Value = Mathf.Clamp((float)Math.Round(CoopConfig.StickerAudioVolume.Value + 0.15f, 2), 0f, 1f); RefreshLabels(); });

            _valMuteStickers = AddToggleRow(contentRt, "modcfg.mute_stickers",
                () => { CoopConfig.MuteTeammateStickers.Value = !CoopConfig.MuteTeammateStickers.Value; RefreshLabels(); });

            _valCompactHud = AddToggleRow(contentRt, "modcfg.compact_hud",
                () => { CoopConfig.CompactHudMode.Value = !CoopConfig.CompactHudMode.Value; RefreshLabels(); });

            _valAutoPause = AddToggleRow(contentRt, "modcfg.auto_pause",
                () => { CoopConfig.AutoPauseOnDisconnect.Value = !CoopConfig.AutoPauseOnDisconnect.Value; RefreshLabels(); });

            var foot = CreateRow(win.transform, "Footer", 36f);
            _langText = NativeStyleProvider.CreateLabel(foot.transform, "", 12f, NativeStyleProvider.TextMuted, TextAlignmentOptions.MidlineLeft);
            _langText.gameObject.AddComponent<LayoutElement>().flexibleWidth = 1f;

            var closeBtn = NativeStyleProvider.CreateButton(foot.transform, CoopLoc.T("modcfg.close"), NativeStyleProvider.ButtonPrimary, Close, 13.5f, 34f);
            closeBtn.GetComponent<LayoutElement>().preferredWidth = 118f;
            _closeBtnText = closeBtn.GetComponentInChildren<TMP_Text>();

            _root.SetActive(false);
        }

        private void AddColorPickerRow(Transform parent)
        {
            var row = CreateRow(parent, "PlayerColorRow", 34f);
            _colorRowLabel = NativeStyleProvider.CreateLabel(row.transform, CoopLoc.T("modcfg.player_color"), 13f, NativeStyleProvider.TextLight, TextAlignmentOptions.MidlineLeft, autoSize: true, minFontSize: 10.5f);
            _colorRowLabel.gameObject.AddComponent<LayoutElement>().flexibleWidth = 1f;

            var swatchesWrap = new GameObject("Swatches");
            swatchesWrap.transform.SetParent(row.transform, false);
            var hlg = swatchesWrap.AddComponent<HorizontalLayoutGroup>();
            hlg.spacing = 6f;
            hlg.childAlignment = TextAnchor.MiddleRight;
            hlg.childControlWidth = true;
            hlg.childControlHeight = true;
            hlg.childForceExpandWidth = false;

            for (int i = 0; i < CoopConfig.PlayerPalette.Length; i++)
            {
                int colorIdx = i;
                Color c = CoopConfig.PlayerPalette[i];
                var btn = NativeStyleProvider.CreateButton(swatchesWrap.transform, "", c, () =>
                {
                    var session = Sync.SessionController.Instance;
                    if (session != null) session.Net.SetLocalColor(colorIdx);
                    else if (CoopConfig.PreferredColorIndex != null) CoopConfig.PreferredColorIndex.Value = colorIdx;
                    NativeStyleProvider.PlayUiSelectSound(1.12f);
                    RefreshLabels();
                }, 13f, 26f);
                var ble = btn.GetComponent<LayoutElement>();
                ble.minWidth = 28f;
                ble.preferredWidth = 28f;

                var bg = btn.GetComponent<Image>();
                bg.color = c;
                var txt = btn.GetComponentInChildren<TMP_Text>();
                if (txt != null)
                {
                    txt.color = new Color(0.10f, 0.08f, 0.06f, 1f);
                    txt.text = "";
                }
                _colorSwatches.Add((bg, txt));
            }
        }

        private void AddKeybindRow(Transform parent, string locKey, ConfigEntry<KeyCode> entry)
        {
            var row = CreateRow(parent, locKey, 32f);
            var lbl = NativeStyleProvider.CreateLabel(row.transform, CoopLoc.T(locKey), 13f, NativeStyleProvider.TextLight, TextAlignmentOptions.MidlineLeft, autoSize: true, minFontSize: 10.5f);
            lbl.gameObject.AddComponent<LayoutElement>().flexibleWidth = 1f;
            _staticLabels.Add((locKey, lbl));

            TMP_Text btnTxt = null;
            var btn = NativeStyleProvider.CreateButton(row.transform, entry.Value.ToString(), NativeStyleProvider.CardBg, () =>
            {
                _rebindingTarget = entry;
                if (btnTxt != null) btnTxt.text = CoopLoc.T("modcfg.press_any_key");
            }, 12.5f, 28f);
            btn.GetComponent<LayoutElement>().preferredWidth = 160f;
            btnTxt = btn.GetComponentInChildren<TMP_Text>();
            _keybindButtons.Add((entry, btnTxt));
        }

        private TMP_Text AddStepperRow(Transform parent, string locKey, Action onMinus, Action onPlus)
        {
            var row = CreateRow(parent, locKey, 32f);
            var lbl = NativeStyleProvider.CreateLabel(row.transform, CoopLoc.T(locKey), 13f, NativeStyleProvider.TextLight, TextAlignmentOptions.MidlineLeft, autoSize: true, minFontSize: 10.5f);
            lbl.gameObject.AddComponent<LayoutElement>().flexibleWidth = 1f;
            _staticLabels.Add((locKey, lbl));

            var minus = NativeStyleProvider.CreateButton(row.transform, "-", NativeStyleProvider.ButtonSecondary, onMinus, 14f, 28f);
            minus.GetComponent<LayoutElement>().preferredWidth = 30f;

            var val = NativeStyleProvider.CreateLabel(row.transform, "", 13f, NativeStyleProvider.EmberGold, TextAlignmentOptions.Center, FontStyles.Bold);
            val.gameObject.AddComponent<LayoutElement>().preferredWidth = 88f;

            var plus = NativeStyleProvider.CreateButton(row.transform, "+", NativeStyleProvider.ButtonSecondary, onPlus, 14f, 28f);
            plus.GetComponent<LayoutElement>().preferredWidth = 30f;
            return val;
        }

        private (TMP_Text txt, Image chk) AddToggleRow(Transform parent, string locKey, Action onToggle)
        {
            var row = CreateRow(parent, locKey, 32f);
            var lbl = NativeStyleProvider.CreateLabel(row.transform, CoopLoc.T(locKey), 13f, NativeStyleProvider.TextLight, TextAlignmentOptions.MidlineLeft, autoSize: true, minFontSize: 10.5f);
            lbl.gameObject.AddComponent<LayoutElement>().flexibleWidth = 1f;
            _staticLabels.Add((locKey, lbl));

            var chk = NativeStyleProvider.CreateCheckboxIndicator(row.transform, false, 18f);

            var btn = NativeStyleProvider.CreateButton(row.transform, "", NativeStyleProvider.ButtonSecondary, onToggle, 12.5f, 28f);
            btn.GetComponent<LayoutElement>().preferredWidth = 134f;
            return (btn.GetComponentInChildren<TMP_Text>(), chk);
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
            UpdateToggleVisual(_valMuteStickers, CoopConfig.MuteTeammateStickers.Value);
            UpdateToggleVisual(_valCompactHud, CoopConfig.CompactHudMode.Value);
            UpdateToggleVisual(_valAutoPause, CoopConfig.AutoPauseOnDisconnect.Value);

            var session = Sync.SessionController.Instance;
            bool isHost = session != null && session.Net.IsHost;
            if (_colorRowLabel != null)
                _colorRowLabel.text = CoopLoc.T(isHost ? "modcfg.host_color" : "modcfg.player_color");

            int activeColor = session != null && session.Net.IsConnected
                ? session.Net.LocalColorIndex
                : Math.Clamp(CoopConfig.PreferredColorIndex?.Value ?? 0, 0, CoopConfig.PlayerPalette.Length - 1);

            for (int i = 0; i < _colorSwatches.Count; i++)
            {
                var (bg, chk) = _colorSwatches[i];
                bool selected = i == activeColor;
                if (bg != null)
                    bg.color = selected ? CoopConfig.PlayerPalette[i] : Color.Lerp(CoopConfig.PlayerPalette[i], new Color(0.28f, 0.24f, 0.20f, 1f), 0.28f);
                if (chk != null)
                    chk.text = selected ? "✓" : "";
            }
        }

        private static void UpdateToggleVisual((TMP_Text txt, Image chk) pair, bool isOn)
        {
            if (pair.txt != null) pair.txt.text = isOn ? CoopLoc.T("modcfg.on") : CoopLoc.T("modcfg.off");
            if (pair.chk != null)
            {
                pair.chk.sprite = NativeAssetRegistry.GetCheckboxSprite(isOn);
                pair.chk.color = isOn ? NativeStyleProvider.StatusReady : NativeStyleProvider.TextMuted;
            }
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
