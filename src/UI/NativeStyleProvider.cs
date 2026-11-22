using System;
using EmberwardCoop.Core;
using TMPro;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.UI;

namespace EmberwardCoop.UI
{
    public static class NativeStyleProvider
    {
        public static TMP_FontAsset PrimaryFont { get; private set; }
        public static Sprite NativeButtonSprite { get; private set; }
        public static Image.Type NativeButtonImageType { get; private set; } = Image.Type.Sliced;
        public static Sprite NativePanelSprite { get; private set; }

        public static readonly Color PanelBg = new Color(0.10f, 0.09f, 0.13f, 0.94f);
        public static readonly Color PanelSubBg = new Color(0.14f, 0.12f, 0.18f, 0.92f);
        public static readonly Color CardBg = new Color(0.17f, 0.15f, 0.22f, 0.90f);
        public static readonly Color EmberGold = new Color(0.96f, 0.74f, 0.28f, 1f);
        public static readonly Color EmberOrange = new Color(0.94f, 0.44f, 0.16f, 1f);
        public static readonly Color EmberBorder = new Color(0.68f, 0.49f, 0.20f, 0.85f);
        public static readonly Color ButtonPrimary = new Color(0.76f, 0.36f, 0.12f, 1f);
        public static readonly Color ButtonSecondary = new Color(0.22f, 0.20f, 0.28f, 1f);
        public static readonly Color ButtonSuccess = new Color(0.18f, 0.56f, 0.30f, 1f);
        public static readonly Color ButtonDanger = new Color(0.66f, 0.20f, 0.20f, 1f);
        public static readonly Color TextLight = new Color(0.96f, 0.93f, 0.87f, 1f);
        public static readonly Color TextMuted = new Color(0.70f, 0.66f, 0.62f, 1f);

        private static GameObject _overlayCanvasGo;
        private static Canvas _overlayCanvas;
        private static Sprite _fallbackWhiteSprite;

        public static void HarvestFromScene()
        {
            try
            {
                if (PrimaryFont == null)
                    RefreshFontForActiveLanguage();

                if (NativeButtonSprite == null)
                {
                    var cp = GameRefs.GetCoinPage();
                    var btnImg = cp?.button_StartGame?.GetComponent<Image>() ?? cp?.button_Options?.GetComponent<Image>();
                    if (btnImg != null && btnImg.sprite != null)
                    {
                        NativeButtonSprite = btnImg.sprite;
                        NativeButtonImageType = btnImg.type;
                    }
                }
            }
            catch { }
        }

        // Updates live overlay TMP_Text fonts when the game language changes so CJK/Cyrillic glyphs stay intact.
        public static void RefreshFontForActiveLanguage()
        {
            try
            {
                TMP_FontAsset found = null;

                var cp = GameRefs.GetCoinPage();
                if (cp?.text_ContinueProgress?.font != null)
                    found = cp.text_ContinueProgress.font;
                else if (cp?.text_Talents?.font != null)
                    found = cp.text_Talents.font;

                if (found == null)
                {
                    var allTmps = UnityEngine.Object.FindObjectsOfType<TMP_Text>();
                    if (allTmps != null)
                    {
                        for (int i = 0; i < allTmps.Length; i++)
                        {
                            var t = allTmps[i];
                            if (t == null || t.font == null) continue;
                            // Prefer game-owned scene TMP elements outside our own overlay canvas
                            if (_overlayCanvasGo != null && t.transform.IsChildOf(_overlayCanvasGo.transform)) continue;
                            found = t.font;
                            break;
                        }
                    }
                }

                if (found == null)
                    found = TMP_Settings.defaultFontAsset;

                if (found != null)
                {
                    PrimaryFont = found;
                    if (_overlayCanvasGo != null)
                    {
                        var modTmps = _overlayCanvasGo.GetComponentsInChildren<TMP_Text>(true);
                        for (int i = 0; i < modTmps.Length; i++)
                        {
                            if (modTmps[i] != null)
                            {
                                modTmps[i].font = PrimaryFont;
                                modTmps[i].SetAllDirty();
                            }
                        }
                    }
                }
            }
            catch { }
        }

        public static Sprite GetWhiteSprite()
        {
            if (_fallbackWhiteSprite != null) return _fallbackWhiteSprite;
            var tex = Texture2D.whiteTexture;
            _fallbackWhiteSprite = Sprite.Create(tex, new Rect(0, 0, tex.width, tex.height), new Vector2(0.5f, 0.5f));
            return _fallbackWhiteSprite;
        }

        public static Canvas GetOrCreateOverlayCanvas()
        {
            if (_overlayCanvas != null && _overlayCanvasGo != null)
                return _overlayCanvas;

            _overlayCanvasGo = new GameObject("EmberwardCoop_Canvas");
            UnityEngine.Object.DontDestroyOnLoad(_overlayCanvasGo);
            _overlayCanvasGo.hideFlags = HideFlags.HideAndDontSave;

            _overlayCanvas = _overlayCanvasGo.AddComponent<Canvas>();
            _overlayCanvas.renderMode = RenderMode.ScreenSpaceOverlay;
            _overlayCanvas.sortingOrder = 550;

            var scaler = _overlayCanvasGo.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920f, 1080f);
            scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;
            scaler.matchWidthOrHeight = 0.5f;

            _overlayCanvasGo.AddComponent<GraphicRaycaster>();
            return _overlayCanvas;
        }

        private static string _cachedClickAssetKey;
        private static string _cachedClickSndName;

        public static void PlayUiClickSound(float pitch = 1.0f)
        {
            try
            {
                var sm = SoundManager.Instance;
                if (sm == null) return;

                if (!string.IsNullOrEmpty(_cachedClickSndName))
                {
                    sm.playSound(_cachedClickAssetKey ?? "", _cachedClickSndName, pitch, 0.05f, 0f);
                    return;
                }

                var assets = sm.list_SoundAssetData;
                if (assets == null) return;
                for (int i = 0; i < assets.Count; i++)
                {
                    var asset = assets[i];
                    if (asset?.SoundFile == null) continue;
                    for (int j = 0; j < asset.SoundFile.Length; j++)
                    {
                        var entry = asset.SoundFile[j];
                        string n = entry?.name ?? "";
                        if (n.IndexOf("click", StringComparison.OrdinalIgnoreCase) >= 0 ||
                            n.IndexOf("button", StringComparison.OrdinalIgnoreCase) >= 0)
                        {
                            _cachedClickAssetKey = asset.DataKey ?? "";
                            _cachedClickSndName = n;
                            sm.playSound(_cachedClickAssetKey, _cachedClickSndName, pitch, 0.05f, 0f);
                            return;
                        }
                    }
                }
            }
            catch { }
        }

        public static GameObject CreatePanel(Transform parent, string name, Color bgColor, bool addBorder = true)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            go.AddComponent<RectTransform>();

            var img = go.AddComponent<Image>();
            img.sprite = NativePanelSprite != null ? NativePanelSprite : GetWhiteSprite();
            img.type = NativePanelSprite != null ? Image.Type.Sliced : Image.Type.Simple;
            img.color = bgColor;

            if (addBorder)
            {
                var outline = go.AddComponent<Outline>();
                outline.effectColor = EmberBorder;
                outline.effectDistance = new Vector2(2f, -2f);
            }
            return go;
        }

        public static TextMeshProUGUI CreateLabel(Transform parent, string text, float fontSize, Color color,
            TextAlignmentOptions align = TextAlignmentOptions.Left, FontStyles style = FontStyles.Normal)
        {
            HarvestFromScene();
            var go = new GameObject("Label");
            go.transform.SetParent(parent, false);
            go.AddComponent<RectTransform>();

            var tmp = go.AddComponent<TextMeshProUGUI>();
            if (PrimaryFont != null) tmp.font = PrimaryFont;
            tmp.text = text ?? "";
            tmp.fontSize = fontSize;
            tmp.color = color;
            tmp.alignment = align;
            tmp.fontStyle = style;
            tmp.raycastTarget = false;
            return tmp;
        }

        public static Button CreateButton(Transform parent, string label, Color tint, Action onClick, float fontSize = 18f, float height = 42f)
        {
            HarvestFromScene();
            var go = new GameObject($"Btn_{label}");
            go.transform.SetParent(parent, false);
            var rt = go.AddComponent<RectTransform>();
            rt.sizeDelta = new Vector2(180f, height);

            var le = go.AddComponent<LayoutElement>();
            le.minHeight = height;
            le.preferredHeight = height;

            var img = go.AddComponent<Image>();
            img.sprite = NativeButtonSprite != null ? NativeButtonSprite : GetWhiteSprite();
            img.type = NativeButtonSprite != null ? NativeButtonImageType : Image.Type.Simple;
            img.color = tint;

            var outline = go.AddComponent<Outline>();
            outline.effectColor = new Color(EmberGold.r, EmberGold.g, EmberGold.b, 0.55f);
            outline.effectDistance = new Vector2(1.5f, -1.5f);

            var btn = go.AddComponent<Button>();
            var colors = btn.colors;
            colors.normalColor = Color.white;
            colors.highlightedColor = new Color(1.15f, 1.12f, 1.05f, 1f);
            colors.pressedColor = new Color(0.82f, 0.78f, 0.72f, 1f);
            colors.disabledColor = new Color(0.45f, 0.45f, 0.45f, 0.65f);
            btn.colors = colors;
            btn.targetGraphic = img;

            var txt = CreateLabel(go.transform, label, fontSize, TextLight, TextAlignmentOptions.Center, FontStyles.Bold);
            StretchFill(txt.rectTransform, 10f, 4f);

            btn.onClick.AddListener((UnityAction)(() =>
            {
                PlayUiClickSound();
                try { onClick?.Invoke(); }
                catch (Exception ex) { CoopLogger.Error(LogCategory.UI, $"Button '{label}' click error", ex); }
            }));
            return btn;
        }

        public static TMP_InputField CreateInputField(Transform parent, string placeholder, string initialValue, float height = 38f)
        {
            HarvestFromScene();
            var go = new GameObject("InputField");
            go.transform.SetParent(parent, false);
            var rt = go.AddComponent<RectTransform>();
            rt.sizeDelta = new Vector2(220f, height);

            var le = go.AddComponent<LayoutElement>();
            le.minHeight = height;
            le.preferredHeight = height;

            var bg = go.AddComponent<Image>();
            bg.sprite = GetWhiteSprite();
            bg.color = new Color(0.07f, 0.06f, 0.10f, 0.95f);

            var outline = go.AddComponent<Outline>();
            outline.effectColor = new Color(EmberBorder.r, EmberBorder.g, EmberBorder.b, 0.6f);
            outline.effectDistance = new Vector2(1.2f, -1.2f);

            var area = new GameObject("TextArea");
            area.transform.SetParent(go.transform, false);
            var areaRt = area.AddComponent<RectTransform>();
            StretchFill(areaRt, 10f, 4f);
            area.AddComponent<RectMask2D>();

            var ph = CreateLabel(area.transform, placeholder, 16f, new Color(0.52f, 0.50f, 0.56f, 0.85f), TextAlignmentOptions.MidlineLeft);
            StretchFill(ph.rectTransform, 0f, 0f);

            var txt = CreateLabel(area.transform, initialValue ?? "", 16f, TextLight, TextAlignmentOptions.MidlineLeft);
            StretchFill(txt.rectTransform, 0f, 0f);
            txt.enableWordWrapping = false;

            var input = go.AddComponent<TMP_InputField>();
            input.textViewport = areaRt;
            input.textComponent = txt;
            input.placeholder = ph;
            input.text = initialValue ?? "";
            input.caretColor = EmberGold;
            input.selectionColor = new Color(EmberOrange.r, EmberOrange.g, EmberOrange.b, 0.45f);
            if (PrimaryFont != null) input.fontAsset = PrimaryFont;
            return input;
        }

        public static void StretchFill(RectTransform rt, float padX = 0f, float padY = 0f)
        {
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.one;
            rt.offsetMin = new Vector2(padX, padY);
            rt.offsetMax = new Vector2(-padX, -padY);
        }
    }
}
