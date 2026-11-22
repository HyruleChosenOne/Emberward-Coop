using System;
using EmberwardCoop.Core;
using EmberwardCoop.Presence;
using TMPro;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.UI;

namespace EmberwardCoop.UI
{
    public static class NativeStyleProvider
    {
        public static TMP_FontAsset PrimaryFont => NativeAssetRegistry.PrimaryFont;
        public static Material PrimaryFontMaterial => NativeAssetRegistry.PrimaryFontMaterial;

        public static Sprite NativeWindowSprite => NativeAssetRegistry.GetWindowPanelSprite();
        public static Sprite NativeSubPanelSprite => NativeAssetRegistry.GetSubPanelSprite();
        public static Sprite NativeButtonSprite => NativeAssetRegistry.GetButtonSprite();
        public static Sprite NativeTabSprite => NativeAssetRegistry.GetTabSprite();
        public static Sprite NativeCircleSprite => NativeAssetRegistry.GetCircleBadgeSprite();
        public static Sprite NativeCoinSprite => NativeAssetRegistry.GetCoinIconSprite();
        public static Sprite NativeCrownSprite => NativeAssetRegistry.GetCrownIconSprite();
        public static Sprite NativeRefreshSprite => NativeAssetRegistry.GetRefreshIconSprite();
        public static Sprite NativeCheckSprite => NativeAssetRegistry.GetCheckIconSprite();
        public static Sprite NativeGearSprite => NativeAssetRegistry.GetGearIconSprite();
        public static Sprite NativePanelSprite => NativeWindowSprite;

        public static Color PanelBg => EmberwardTheme.WindowSurface;
        public static Color PanelSubBg => EmberwardTheme.SubPanelSurface;
        public static Color PanelWellBg => EmberwardTheme.WellSurface;
        public static Color CardBg => EmberwardTheme.CardSurface;
        public static Color CardHoverBg => EmberwardTheme.CardHoverSurface;
        public static Color HudBg => EmberwardTheme.HudSurface;
        public static Color TooltipBg => EmberwardTheme.TooltipSurface;
        public static Color ChatBg => EmberwardTheme.ChatSurface;

        public static Color EmberGold => EmberwardTheme.EmberGold;
        public static Color EmberGoldSoft => EmberwardTheme.EmberGoldSoft;
        public static Color EmberOrange => EmberwardTheme.EmberAmber;
        public static Color EmberBorder => EmberwardTheme.BronzeBorder;
        public static Color EmberBorderSubtle => EmberwardTheme.BronzeSubtle;

        public static Color ButtonPrimary => EmberwardTheme.ButtonPrimary;
        public static Color ButtonSecondary => EmberwardTheme.ButtonSecondary;
        public static Color ButtonSuccess => EmberwardTheme.ButtonSuccess;
        public static Color ButtonDanger => EmberwardTheme.ButtonDanger;

        public static Color TextLight => EmberwardTheme.TextParchment;
        public static Color TextMuted => EmberwardTheme.TextSand;
        public static Color StatusReady => EmberwardTheme.StatusReady;
        public static Color StatusWarn => EmberwardTheme.StatusWarn;
        public static Color StatusBad => EmberwardTheme.StatusBad;
        public static Color ModeSharedTint => EmberwardTheme.ModeSharedTint;
        public static Color ModeSeparateTint => EmberwardTheme.ModeSeparateTint;
        public static Color ShadowColor => EmberwardTheme.TextShadowBrown;

        private static GameObject _overlayCanvasGo;
        private static Canvas _overlayCanvas;
        private static RectTransform _overlayCanvasRt;
        private static float _lastHoverSoundTime = -1f;

        public static void HarvestFromScene()
        {
            NativeAssetRegistry.EnsureScanned();
        }

        public static Sprite GetPanelSprite() => NativeAssetRegistry.GetWindowPanelSprite();
        public static Sprite GetSubPanelSprite() => NativeAssetRegistry.GetSubPanelSprite();
        public static Sprite GetButtonSprite() => NativeAssetRegistry.GetButtonSprite();
        public static Sprite GetBadgePillSprite() => NativeAssetRegistry.GetBadgePillSprite();
        public static Sprite GetCircleSprite() => NativeAssetRegistry.GetCircleBadgeSprite();

        public static void ApplySpriteSmart(Image img, Sprite sp, bool preferSliced = true, float ppuMultiplier = 1f)
        {
            if (img == null) return;
            Sprite safe = sp != null ? sp : NativeAssetRegistry.GetFallbackIcon();
            img.sprite = safe;
            bool sliced = preferSliced && safe != null && NativeAssetRegistry.HasValid9SliceBorder(safe);
            img.type = sliced ? Image.Type.Sliced : Image.Type.Simple;
            if (sliced)
            {
                img.fillCenter = true;
                img.pixelsPerUnitMultiplier = ppuMultiplier;
            }
        }

        private static float ResolvePpuMultiplier(Sprite sp, PanelSurfaceRole role)
        {
            if (sp == null || string.IsNullOrEmpty(sp.name)) return 1f;
            if (sp.name.StartsWith("DialogBox_v3", StringComparison.OrdinalIgnoreCase))
                return role == PanelSurfaceRole.Window ? 1.45f : 1.75f;
            if (sp.name.StartsWith("doubleOutlineBox", StringComparison.OrdinalIgnoreCase))
                return 2.0f;
            return 1f;
        }

        public static void RefreshFontForActiveLanguage()
        {
            try
            {
                NativeAssetRegistry.EnsureScanned(force: true);
                NativeAssetRegistry.RefreshHeroDisplayNames();

                TMP_FontAsset font = NativeAssetRegistry.PrimaryFont;
                Material mat = NativeAssetRegistry.PrimaryFontMaterial;
                if (font != null && _overlayCanvasGo != null)
                {
                    var modTmps = _overlayCanvasGo.GetComponentsInChildren<TMP_Text>(true);
                    for (int i = 0; i < modTmps.Length; i++)
                    {
                        var t = modTmps[i];
                        if (t == null) continue;
                        t.font = font;
                        if (mat != null && mat.mainTexture == font.atlasTexture)
                            t.fontSharedMaterial = mat;
                        t.SetAllDirty();
                    }
                }
            }
            catch { }
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
            _overlayCanvas.overrideSorting = true;

            var scaler = _overlayCanvasGo.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920f, 1080f);
            scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.Expand;

            _overlayCanvasGo.AddComponent<GraphicRaycaster>();
            _overlayCanvasRt = _overlayCanvasGo.GetComponent<RectTransform>();
            return _overlayCanvas;
        }

        public static Vector2 GetCanvasSize()
        {
            GetOrCreateOverlayCanvas();
            if (_overlayCanvasRt != null && _overlayCanvasRt.rect.width > 100f && _overlayCanvasRt.rect.height > 100f)
                return _overlayCanvasRt.rect.size;
            return new Vector2(1920f, 1080f);
        }

        public static Vector2 ScreenToCanvasLocal(Vector2 screenPos)
        {
            GetOrCreateOverlayCanvas();
            if (_overlayCanvasRt != null &&
                RectTransformUtility.ScreenPointToLocalPointInRectangle(_overlayCanvasRt, screenPos, null, out Vector2 local))
            {
                return local;
            }
            Vector2 sz = GetCanvasSize();
            float nx = Screen.width > 0 ? (screenPos.x / Screen.width) - 0.5f : 0f;
            float ny = Screen.height > 0 ? (screenPos.y / Screen.height) - 0.5f : 0f;
            return new Vector2(nx * sz.x, ny * sz.y);
        }

        public static Vector2 ScreenToCanvasSize(Vector2 screenSize)
        {
            Vector2 cs = GetCanvasSize();
            float sx = Screen.width > 0 ? cs.x / Screen.width : 1f;
            float sy = Screen.height > 0 ? cs.y / Screen.height : 1f;
            return new Vector2(screenSize.x * sx, screenSize.y * sy);
        }

        public static void PlayUiClickSound(float pitch = 1.0f)
        {
            try
            {
                var sm = SoundManager.Instance;
                if (sm == null) return;
                if (string.IsNullOrEmpty(NativeAssetRegistry.ClickSoundName)) NativeAssetRegistry.EnsureScanned();
                if (!string.IsNullOrEmpty(NativeAssetRegistry.ClickSoundName))
                    sm.playSound(NativeAssetRegistry.ClickSoundAssetKey ?? "", NativeAssetRegistry.ClickSoundName, pitch, 0.04f, 0f);
            }
            catch { }
        }

        public static void PlayUiSelectSound(float pitch = 1.16f)
        {
            try
            {
                var sm = SoundManager.Instance;
                if (sm == null) return;
                if (string.IsNullOrEmpty(NativeAssetRegistry.SelectSoundName)) NativeAssetRegistry.EnsureScanned();
                if (!string.IsNullOrEmpty(NativeAssetRegistry.SelectSoundName))
                    sm.playSound(NativeAssetRegistry.SelectSoundAssetKey ?? "", NativeAssetRegistry.SelectSoundName, pitch, 0.04f, 0f);
                else
                    PlayUiClickSound(pitch);
            }
            catch { }
        }

        public static void PlayUiHoverSound(float pitch = 1.08f)
        {
            float now = Time.unscaledTime;
            if (now - _lastHoverSoundTime < 0.055f) return;
            _lastHoverSoundTime = now;

            try
            {
                var sm = SoundManager.Instance;
                if (sm == null) return;
                if (string.IsNullOrEmpty(NativeAssetRegistry.HoverSoundName)) NativeAssetRegistry.EnsureScanned();
                if (!string.IsNullOrEmpty(NativeAssetRegistry.HoverSoundName))
                    sm.playSound(NativeAssetRegistry.HoverSoundAssetKey ?? "", NativeAssetRegistry.HoverSoundName, pitch, 0.04f, 0f);
                else if (!string.IsNullOrEmpty(NativeAssetRegistry.ClickSoundName))
                    sm.playSound(NativeAssetRegistry.ClickSoundAssetKey ?? "", NativeAssetRegistry.ClickSoundName, 1.32f, 0.05f, 0f);
            }
            catch { }
        }

        public static void PlayUiOpenSound(bool opening = true)
        {
            try
            {
                var sm = SoundManager.Instance;
                if (sm == null) return;
                if (string.IsNullOrEmpty(NativeAssetRegistry.OpenSoundName)) NativeAssetRegistry.EnsureScanned();
                if (!string.IsNullOrEmpty(NativeAssetRegistry.OpenSoundName))
                    sm.playSound(NativeAssetRegistry.OpenSoundAssetKey ?? "", NativeAssetRegistry.OpenSoundName, opening ? 1.05f : 0.88f, 0.06f, 0f);
                else
                    PlayUiClickSound(opening ? 1.10f : 0.86f);
            }
            catch { }
        }

        public static void PlayUiCoinSound()
        {
            try
            {
                var sm = SoundManager.Instance;
                if (sm == null) return;
                if (string.IsNullOrEmpty(NativeAssetRegistry.CoinSoundName)) NativeAssetRegistry.EnsureScanned();
                if (!string.IsNullOrEmpty(NativeAssetRegistry.CoinSoundName))
                    sm.playSound(NativeAssetRegistry.CoinSoundAssetKey ?? "", NativeAssetRegistry.CoinSoundName, 1.05f, 0.05f, 0f);
                else
                    PlayUiClickSound(1.24f);
            }
            catch { }
        }

        // UI_SoundOnMouseOver only implements IPointerEnterHandler, so ScrollRect wheel events keep bubbling.
        public static void AttachHoverSound(GameObject go, float pitch = 1.08f)
        {
            if (go == null) return;
            try
            {
                if (string.IsNullOrEmpty(NativeAssetRegistry.HoverSoundName))
                    NativeAssetRegistry.EnsureScanned();

                if (!string.IsNullOrEmpty(NativeAssetRegistry.HoverSoundName))
                {
                    var nativeHover = go.GetComponent<UI_SoundOnMouseOver>() ?? go.AddComponent<UI_SoundOnMouseOver>();
                    if (nativeHover != null)
                    {
                        nativeHover.soundDataName = NativeAssetRegistry.HoverSoundAssetKey ?? "";
                        nativeHover.soundKey = NativeAssetRegistry.HoverSoundName;
                    }
                }
            }
            catch { }
        }

        public static void TickOpenTransition(RectTransform rt, CanvasGroup cg, ref float progress, float dt, float duration = 0.13f)
        {
            if (progress >= 1f) return;
            progress = Mathf.Clamp01(progress + dt / Mathf.Max(0.01f, duration));
            float eased = 1f - Mathf.Pow(1f - progress, 3f);
            if (rt != null)
            {
                float sc = Mathf.Lerp(0.95f, 1f, eased);
                rt.localScale = new Vector3(sc, sc, 1f);
            }
            if (cg != null)
                cg.alpha = Mathf.Lerp(0.15f, 1f, eased);
        }

        public static GameObject CreatePanel(Transform parent, string name, Color bgColor, bool addBorder = true, bool isSubPanel = false, PanelSurfaceRole? roleOverride = null)
        {
            NativeAssetRegistry.EnsureScanned();
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            go.AddComponent<RectTransform>();

            PanelSurfaceRole role = roleOverride ?? InferSurfaceRole(bgColor, isSubPanel);
            var img = go.AddComponent<Image>();

            Sprite sp;
            if (role == PanelSurfaceRole.Window)
                sp = NativeAssetRegistry.GetWindowPanelSprite();
            else if (role == PanelSurfaceRole.Hud || role == PanelSurfaceRole.Tooltip)
                sp = NativeAssetRegistry.GetHudPanelSprite();
            else if (role == PanelSurfaceRole.Well)
                sp = NativeAssetRegistry.GetWellPanelSprite();
            else if (role == PanelSurfaceRole.SubPanel || role == PanelSurfaceRole.Card)
                sp = NativeAssetRegistry.GetSubPanelSprite();
            else
                sp = NativeAssetRegistry.GetTintablePanelSprite();

            ApplySpriteSmart(img, sp, preferSliced: true, ppuMultiplier: ResolvePpuMultiplier(sp, role));
            img.color = EmberwardTheme.ResolveSpriteTint(sp, bgColor, role);
            return go;
        }

        private static PanelSurfaceRole InferSurfaceRole(Color c, bool isSubPanel)
        {
            if (ColorsNear(c, EmberwardTheme.WindowSurface)) return PanelSurfaceRole.Window;
            if (ColorsNear(c, EmberwardTheme.SubPanelSurface)) return PanelSurfaceRole.SubPanel;
            if (ColorsNear(c, EmberwardTheme.CardSurface) || ColorsNear(c, EmberwardTheme.CardHoverSurface)) return PanelSurfaceRole.Card;
            if (ColorsNear(c, EmberwardTheme.WellSurface)) return PanelSurfaceRole.Well;
            if (ColorsNear(c, EmberwardTheme.HudSurface)) return PanelSurfaceRole.Hud;
            if (ColorsNear(c, EmberwardTheme.TooltipSurface)) return PanelSurfaceRole.Tooltip;
            if (ColorsNear(c, EmberwardTheme.ChatSurface)) return PanelSurfaceRole.Chat;
            return isSubPanel ? PanelSurfaceRole.CustomAccent : PanelSurfaceRole.Window;
        }

        private static bool ColorsNear(Color a, Color b)
            => Mathf.Abs(a.r - b.r) < 0.02f && Mathf.Abs(a.g - b.g) < 0.02f && Mathf.Abs(a.b - b.b) < 0.02f;

        public static GameObject CreateWell(Transform parent, string name)
        {
            return CreatePanel(parent, name, PanelWellBg, addBorder: false, isSubPanel: true, roleOverride: PanelSurfaceRole.Well);
        }

        public static GameObject CreateBadgePill(Transform parent, string name, Color bgColor, bool addBorder = false)
        {
            NativeAssetRegistry.EnsureScanned();
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            go.AddComponent<RectTransform>();
            var img = go.AddComponent<Image>();
            Sprite sp = NativeAssetRegistry.GetBadgePillSprite();
            ApplySpriteSmart(img, sp, preferSliced: true);
            img.color = EmberwardTheme.ResolveSpriteTint(sp, bgColor, PanelSurfaceRole.CustomAccent);
            return go;
        }

        public static GameObject CreateSpeechBubblePanel(Transform parent, string name, Color bgColor, Color borderColor)
        {
            NativeAssetRegistry.EnsureScanned();
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            go.AddComponent<RectTransform>();
            var img = go.AddComponent<Image>();
            Sprite sp = NativeAssetRegistry.GetSpeechBubbleSprite();
            ApplySpriteSmart(img, sp, preferSliced: true, ppuMultiplier: ResolvePpuMultiplier(sp, PanelSurfaceRole.Tooltip));
            img.color = EmberwardTheme.ResolveSpriteTint(sp, bgColor, PanelSurfaceRole.Tooltip);
            return go;
        }

        public static GameObject CreateDivider(Transform parent, string name = "Divider")
        {
            NativeAssetRegistry.EnsureScanned();
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            var le = go.AddComponent<LayoutElement>();
            le.minHeight = 2f;
            le.preferredHeight = 2f;
            var img = go.AddComponent<Image>();
            ApplySpriteSmart(img, NativeAssetRegistry.GetDividerSprite(), preferSliced: false);
            img.color = EmberwardTheme.DividerWarm;
            img.raycastTarget = false;
            return go;
        }

        public static TextMeshProUGUI CreateInlinePingLabel(Transform parent, int pingMs, bool isHost, float fontSize = 10.5f)
        {
            string text = isHost ? "Host" : $"{Mathf.Max(0, pingMs)} ms";
            Color col = isHost ? EmberGoldSoft : EmberwardTheme.GetPingColor(pingMs);
            var tmp = CreateLabel(
                parent,
                text,
                fontSize,
                col,
                TextAlignmentOptions.MidlineLeft,
                isHost ? FontStyles.Bold : FontStyles.Normal,
                wordWrap: false,
                autoSize: true,
                minFontSize: Mathf.Max(8f, fontSize - 2f),
                addShadow: true);
            tmp.overflowMode = TextOverflowModes.Overflow;
            var le = tmp.gameObject.AddComponent<LayoutElement>();
            le.minWidth = isHost ? 28f : 34f;
            le.preferredWidth = isHost ? 32f : 40f;
            le.flexibleWidth = 0f;
            return tmp;
        }

        public static TextMeshProUGUI CreateLabel(
            Transform parent,
            string text,
            float fontSize,
            Color color,
            TextAlignmentOptions align = TextAlignmentOptions.Left,
            FontStyles style = FontStyles.Normal,
            bool wordWrap = false,
            bool autoSize = false,
            float minFontSize = 0f,
            bool addShadow = false)
        {
            NativeAssetRegistry.EnsureScanned();
            var go = new GameObject("Label");
            go.transform.SetParent(parent, false);
            go.AddComponent<RectTransform>();

            var tmp = go.AddComponent<TextMeshProUGUI>();
            if (PrimaryFont != null)
            {
                tmp.font = PrimaryFont;
                if (PrimaryFontMaterial != null && PrimaryFontMaterial.mainTexture == PrimaryFont.atlasTexture)
                    tmp.fontSharedMaterial = PrimaryFontMaterial;
            }
            tmp.text = text ?? "";
            tmp.fontSize = fontSize;
            tmp.color = color;
            tmp.alignment = align;
            tmp.fontStyle = style;
            tmp.enableWordWrapping = wordWrap;
            tmp.overflowMode = wordWrap ? TextOverflowModes.Overflow : TextOverflowModes.Ellipsis;
            tmp.raycastTarget = false;

            if (autoSize)
            {
                tmp.enableAutoSizing = true;
                tmp.fontSizeMin = minFontSize > 0f ? minFontSize : Mathf.Max(9.5f, fontSize * 0.72f);
                tmp.fontSizeMax = fontSize;
            }

            if (addShadow)
            {
                var sh = go.AddComponent<Shadow>();
                sh.effectColor = ShadowColor;
                sh.effectDistance = new Vector2(1.1f, -1.1f);
            }

            return tmp;
        }

        // Build clean Unity UI buttons rather than cloning CoinPage templates: CoinPage buttons use UI_BasicButton,
        // Animators, and transparent root Images that interfere with layout and deferred component cleanup.
        public static Button CreateButton(
            Transform parent,
            string label,
            Color tint,
            Action onClick,
            float fontSize = 14.5f,
            float height = 36f,
            bool isTab = false)
        {
            NativeAssetRegistry.EnsureScanned();

            var go = new GameObject($"Btn_{label}");
            go.transform.SetParent(parent, false);
            var rt = go.AddComponent<RectTransform>();
            rt.sizeDelta = new Vector2(140f, height);
            rt.localScale = Vector3.one;

            var le = go.AddComponent<LayoutElement>();
            le.minHeight = height;
            le.preferredHeight = height;

            var img = go.AddComponent<Image>();
            Sprite sp = isTab ? NativeAssetRegistry.GetTabSprite() : NativeAssetRegistry.GetButtonSprite();
            ApplySpriteSmart(img, sp, preferSliced: true);
            img.color = EmberwardTheme.ResolveSpriteTint(sp, tint, PanelSurfaceRole.CustomAccent);
            img.raycastTarget = true;

            var btn = go.AddComponent<Button>();
            btn.targetGraphic = img;
            btn.interactable = true;
            btn.transition = Selectable.Transition.ColorTint;
            var colors = btn.colors;
            colors.normalColor = Color.white;
            colors.highlightedColor = new Color(1.16f, 1.12f, 1.04f, 1f);
            colors.pressedColor = new Color(0.80f, 0.76f, 0.70f, 1f);
            colors.selectedColor = new Color(1.06f, 1.03f, 0.98f, 1f);
            colors.disabledColor = new Color(0.50f, 0.48f, 0.48f, 0.62f);
            colors.colorMultiplier = 1f;
            colors.fadeDuration = 0.08f;
            btn.colors = colors;

            var txt = CreateLabel(
                go.transform,
                label,
                fontSize,
                TextLight,
                TextAlignmentOptions.Center,
                FontStyles.Bold,
                wordWrap: false,
                autoSize: true,
                minFontSize: Mathf.Max(9.5f, fontSize * 0.68f),
                addShadow: true);
            StretchFill(txt.rectTransform, 8f, 3f);

            AttachHoverSound(go, isTab ? 1.14f : 1.08f);

            btn.onClick.AddListener((UnityAction)(() =>
            {
                PlayUiClickSound();
                try { onClick?.Invoke(); }
                catch (Exception ex) { CoopLogger.Error(LogCategory.UI, $"Button '{label}' click error", ex); }
            }));
            return btn;
        }

        public static Button CreateCloseButton(Transform parent, Action onClick, float size = 28f)
        {
            NativeAssetRegistry.EnsureScanned();
            Sprite xSprite = NativeAssetRegistry.GetCloseIconSprite();

            if (xSprite == null)
            {
                var fallbackBtn = CreateButton(parent, "×", ButtonSecondary, onClick, 16f, size);
                var fle = fallbackBtn.GetComponent<LayoutElement>();
                fle.minWidth = size + 6f;
                fle.preferredWidth = size + 6f;
                return fallbackBtn;
            }

            var go = new GameObject("Btn_Close");
            go.transform.SetParent(parent, false);
            var rt = go.AddComponent<RectTransform>();
            rt.sizeDelta = new Vector2(size + 4f, size);
            var le = go.AddComponent<LayoutElement>();
            le.minWidth = size + 4f; le.preferredWidth = size + 4f;
            le.minHeight = size; le.preferredHeight = size;

            var bg = go.AddComponent<Image>();
            Sprite sp = NativeAssetRegistry.GetButtonSprite();
            ApplySpriteSmart(bg, sp, preferSliced: true);
            bg.color = EmberwardTheme.ResolveSpriteTint(sp, ButtonSecondary, PanelSurfaceRole.CustomAccent);
            bg.raycastTarget = true;

            var btn = go.AddComponent<Button>();
            btn.targetGraphic = bg;
            btn.interactable = true;
            btn.transition = Selectable.Transition.ColorTint;
            var colors = btn.colors;
            colors.normalColor = Color.white;
            colors.highlightedColor = new Color(1.18f, 1.12f, 1.04f, 1f);
            colors.pressedColor = new Color(0.80f, 0.76f, 0.70f, 1f);
            colors.fadeDuration = 0.08f;
            btn.colors = colors;

            var iconGo = new GameObject("CloseIcon");
            iconGo.transform.SetParent(go.transform, false);
            StretchFill(iconGo.AddComponent<RectTransform>(), 5f, 5f);
            var icImg = iconGo.AddComponent<Image>();
            icImg.sprite = xSprite;
            icImg.color = TextLight;
            icImg.preserveAspect = true;
            icImg.raycastTarget = false;

            AttachHoverSound(go, 1.12f);
            btn.onClick.AddListener((UnityAction)(() =>
            {
                PlayUiClickSound(0.92f);
                try { onClick?.Invoke(); } catch { }
            }));
            return btn;
        }

        public static Image CreateCheckboxIndicator(Transform parent, bool isOn, float size = 18f)
        {
            NativeAssetRegistry.EnsureScanned();
            var go = new GameObject("Checkbox");
            go.transform.SetParent(parent, false);
            var le = go.AddComponent<LayoutElement>();
            le.minWidth = size; le.preferredWidth = size;
            le.minHeight = size; le.preferredHeight = size;

            var img = go.AddComponent<Image>();
            img.sprite = NativeAssetRegistry.GetCheckboxSprite(isOn);
            img.color = isOn ? StatusReady : TextMuted;
            img.preserveAspect = true;
            img.raycastTarget = false;
            return img;
        }

        public static GameObject CreateHeroAvatarWidget(
            Transform parent,
            string heroId,
            Color playerColor,
            float size,
            bool clickable = false,
            Action onClick = null,
            bool showEditBadge = false,
            bool selected = false)
        {
            NativeAssetRegistry.EnsureScanned();
            HeroAvatarEntry hero = NativeAssetRegistry.GetHeroAvatar(heroId);

            var root = new GameObject($"Avatar_{hero.HeroId}");
            root.transform.SetParent(parent, false);
            var rt = root.AddComponent<RectTransform>();
            rt.sizeDelta = new Vector2(size, size);

            var le = root.AddComponent<LayoutElement>();
            le.minWidth = size; le.preferredWidth = size;
            le.minHeight = size; le.preferredHeight = size;

            var bgImg = root.AddComponent<Image>();
            Sprite wellSp = NativeAssetRegistry.GetWellPanelSprite();
            ApplySpriteSmart(bgImg, wellSp, preferSliced: true);
            bgImg.color = EmberwardTheme.ResolveSpriteTint(wellSp, EmberwardTheme.WellSurface, PanelSurfaceRole.Well);
            bgImg.raycastTarget = clickable;

            var maskGo = new GameObject("PortraitMask");
            maskGo.transform.SetParent(root.transform, false);
            float pad = Mathf.Clamp(size * 0.08f, 2f, 5f);
            StretchFill(maskGo.AddComponent<RectTransform>(), pad, pad);
            maskGo.AddComponent<RectMask2D>();

            var portGo = new GameObject("Portrait");
            portGo.transform.SetParent(maskGo.transform, false);
            StretchFill(portGo.AddComponent<RectTransform>(), 0f, 0f);
            var portImg = portGo.AddComponent<Image>();
            portImg.sprite = hero.PortraitSprite ?? NativeAssetRegistry.GetFallbackIcon();
            portImg.color = Color.white;
            portImg.preserveAspect = true;
            portImg.raycastTarget = false;

            var frameGo = new GameObject("AvatarFrame");
            frameGo.transform.SetParent(root.transform, false);
            StretchFill(frameGo.AddComponent<RectTransform>(), -1f, -1f);
            var frameImg = frameGo.AddComponent<Image>();
            Sprite frameSprite = NativeAssetRegistry.GetAvatarFrameSprite(selected);
            ApplySpriteSmart(frameImg, frameSprite, preferSliced: true);
            frameImg.color = selected
                ? EmberGold
                : Color.Lerp(playerColor, EmberGold, 0.18f);
            frameImg.preserveAspect = !NativeAssetRegistry.HasValid9SliceBorder(frameSprite);
            frameImg.raycastTarget = false;

            if (showEditBadge)
            {
                float badgeSz = Mathf.Clamp(size * 0.36f, 11f, 16f);
                var badgeBg = new GameObject("EditBadge");
                badgeBg.transform.SetParent(root.transform, false);
                var brt = badgeBg.AddComponent<RectTransform>();
                brt.anchorMin = new Vector2(1f, 0f);
                brt.anchorMax = new Vector2(1f, 0f);
                brt.pivot = new Vector2(1f, 0f);
                brt.anchoredPosition = new Vector2(2f, -2f);
                brt.sizeDelta = new Vector2(badgeSz, badgeSz);

                var bbImg = badgeBg.AddComponent<Image>();
                bbImg.sprite = NativeAssetRegistry.GetCircleBadgeSprite();
                bbImg.color = new Color(0.28f, 0.21f, 0.14f, 0.96f);
                bbImg.preserveAspect = true;
                bbImg.raycastTarget = false;

                var icGo = new GameObject("EditIcon");
                icGo.transform.SetParent(badgeBg.transform, false);
                StretchFill(icGo.AddComponent<RectTransform>(), 2f, 2f);
                var icImg = icGo.AddComponent<Image>();
                icImg.sprite = NativeAssetRegistry.GetEditBadgeSprite();
                icImg.color = EmberGold;
                icImg.preserveAspect = true;
                icImg.raycastTarget = false;
            }

            if (clickable)
            {
                var btn = root.AddComponent<Button>();
                btn.targetGraphic = frameImg;
                var cols = btn.colors;
                cols.normalColor = Color.white;
                cols.highlightedColor = new Color(1.28f, 1.22f, 1.08f, 1f);
                cols.pressedColor = new Color(0.84f, 0.80f, 0.74f, 1f);
                cols.fadeDuration = 0.07f;
                btn.colors = cols;

                AttachHoverSound(root, 1.15f);
                btn.onClick.AddListener((UnityAction)(() =>
                {
                    PlayUiSelectSound();
                    try { onClick?.Invoke(); } catch { }
                }));
            }

            return root;
        }

        public static GameObject CreateStickerMedallionWidget(Transform parent, StickerId id, Color rimColor, float size)
        {
            NativeAssetRegistry.EnsureScanned();

            var root = new GameObject($"Medallion_{id}");
            root.transform.SetParent(parent, false);
            var rt = root.AddComponent<RectTransform>();
            rt.sizeDelta = new Vector2(size, size);

            var le = root.AddComponent<LayoutElement>();
            le.minWidth = size; le.preferredWidth = size;
            le.minHeight = size; le.preferredHeight = size;

            var medImg = root.AddComponent<Image>();
            medImg.sprite = NativeStickerCatalog.GetMedallionSprite();
            medImg.color = Color.Lerp(new Color(0.24f, 0.21f, 0.26f, 0.96f), rimColor, 0.34f);
            medImg.preserveAspect = true;
            medImg.raycastTarget = false;

            float pad = Mathf.Max(4f, size * 0.18f);
            var artGo = new GameObject("Artwork");
            artGo.transform.SetParent(root.transform, false);
            StretchFill(artGo.AddComponent<RectTransform>(), pad, pad);
            var artImg = artGo.AddComponent<Image>();
            artImg.sprite = NativeStickerCatalog.GetArtworkSprite(id);
            artImg.color = Color.white;
            artImg.preserveAspect = true;
            artImg.raycastTarget = false;

            return root;
        }

        public static TMP_InputField CreateInputField(Transform parent, string placeholder, string initialValue, float height = 34f, float fontSize = 13.5f)
        {
            NativeAssetRegistry.EnsureScanned();
            var go = new GameObject("InputField");
            go.transform.SetParent(parent, false);
            var rt = go.AddComponent<RectTransform>();
            rt.sizeDelta = new Vector2(200f, height);

            var le = go.AddComponent<LayoutElement>();
            le.minHeight = height;
            le.preferredHeight = height;

            var bg = go.AddComponent<Image>();
            Sprite wellSp = NativeAssetRegistry.GetWellPanelSprite();
            ApplySpriteSmart(bg, wellSp, preferSliced: true);
            bg.color = EmberwardTheme.ResolveSpriteTint(wellSp, PanelWellBg, PanelSurfaceRole.Well);
            bg.raycastTarget = true;

            var area = new GameObject("TextArea");
            area.transform.SetParent(go.transform, false);
            var areaRt = area.AddComponent<RectTransform>();
            StretchFill(areaRt, 9f, 3f);
            area.AddComponent<RectMask2D>();

            var ph = CreateLabel(area.transform, placeholder, fontSize, new Color(0.68f, 0.61f, 0.53f, 0.85f), TextAlignmentOptions.MidlineLeft, FontStyles.Italic);
            StretchFill(ph.rectTransform, 0f, 0f);
            ph.overflowMode = TextOverflowModes.Ellipsis;

            var txt = CreateLabel(area.transform, initialValue ?? "", fontSize, TextLight, TextAlignmentOptions.MidlineLeft);
            StretchFill(txt.rectTransform, 0f, 0f);
            txt.enableWordWrapping = false;
            txt.overflowMode = TextOverflowModes.Overflow;

            var input = go.AddComponent<TMP_InputField>();
            input.textViewport = areaRt;
            input.textComponent = txt;
            input.placeholder = ph;
            input.text = initialValue ?? "";
            input.caretColor = EmberGold;
            input.selectionColor = new Color(EmberOrange.r, EmberOrange.g, EmberOrange.b, 0.42f);
            if (PrimaryFont != null) input.fontAsset = PrimaryFont;
            return input;
        }

        public static ScrollRect CreateScrollView(
            Transform parent,
            string name,
            Color bgColor,
            out RectTransform contentRt,
            float padding = 8f,
            float spacing = 6f,
            bool addBorder = true)
        {
            NativeAssetRegistry.EnsureScanned();
            var root = CreatePanel(parent, name, bgColor, addBorder: false, isSubPanel: true);
            var scroll = root.AddComponent<ScrollRect>();
            scroll.horizontal = false;
            scroll.vertical = true;
            scroll.movementType = ScrollRect.MovementType.Clamped;
            scroll.inertia = true;
            scroll.decelerationRate = 0.12f;
            scroll.scrollSensitivity = 32f;

            var viewport = new GameObject("Viewport");
            viewport.transform.SetParent(root.transform, false);
            var vpRt = viewport.AddComponent<RectTransform>();
            vpRt.anchorMin = Vector2.zero;
            vpRt.anchorMax = Vector2.one;
            vpRt.offsetMin = new Vector2(2f, 2f);
            vpRt.offsetMax = new Vector2(-12f, -2f);
            viewport.AddComponent<RectMask2D>();
            var vpImg = viewport.AddComponent<Image>();
            ApplySpriteSmart(vpImg, NativeAssetRegistry.GetWellPanelSprite(), preferSliced: true);
            vpImg.color = Color.clear;
            vpImg.raycastTarget = true;

            var content = new GameObject("Content");
            content.transform.SetParent(viewport.transform, false);
            contentRt = content.AddComponent<RectTransform>();
            contentRt.anchorMin = new Vector2(0f, 1f);
            contentRt.anchorMax = new Vector2(1f, 1f);
            contentRt.pivot = new Vector2(0.5f, 1f);
            contentRt.anchoredPosition = Vector2.zero;
            contentRt.sizeDelta = Vector2.zero;

            int pad = Mathf.RoundToInt(padding);
            var vlg = content.AddComponent<VerticalLayoutGroup>();
            vlg.padding = new RectOffset(pad, pad, pad, pad);
            vlg.spacing = spacing;
            vlg.childControlWidth = true;
            vlg.childControlHeight = true;
            vlg.childForceExpandWidth = true;
            vlg.childForceExpandHeight = false;

            var fitter = content.AddComponent<ContentSizeFitter>();
            fitter.horizontalFit = ContentSizeFitter.FitMode.Unconstrained;
            fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

            var sbGo = new GameObject("Scrollbar");
            sbGo.transform.SetParent(root.transform, false);
            var sbRt = sbGo.AddComponent<RectTransform>();
            sbRt.anchorMin = new Vector2(1f, 0f);
            sbRt.anchorMax = new Vector2(1f, 1f);
            sbRt.pivot = new Vector2(1f, 0.5f);
            sbRt.offsetMin = new Vector2(-10f, 4f);
            sbRt.offsetMax = new Vector2(-3f, -4f);
            var sbBg = sbGo.AddComponent<Image>();
            ApplySpriteSmart(sbBg, NativeAssetRegistry.GetScrollTrackSprite(), preferSliced: true);
            sbBg.color = new Color(0.24f, 0.19f, 0.17f, 0.82f);

            var slideArea = new GameObject("SlidingArea");
            slideArea.transform.SetParent(sbGo.transform, false);
            StretchFill(slideArea.AddComponent<RectTransform>(), 0f, 2f);

            var handle = new GameObject("Handle");
            handle.transform.SetParent(slideArea.transform, false);
            var handleRt = handle.AddComponent<RectTransform>();
            StretchFill(handleRt, 0f, 0f);
            var handleImg = handle.AddComponent<Image>();
            ApplySpriteSmart(handleImg, NativeAssetRegistry.GetScrollHandleSprite(), preferSliced: true);
            handleImg.color = new Color(0.84f, 0.65f, 0.34f, 0.92f);

            var scrollbar = sbGo.AddComponent<Scrollbar>();
            scrollbar.handleRect = handleRt;
            scrollbar.targetGraphic = handleImg;
            scrollbar.direction = Scrollbar.Direction.BottomToTop;
            var sc = scrollbar.colors;
            sc.normalColor = Color.white;
            sc.highlightedColor = new Color(1.25f, 1.18f, 1.0f, 1f);
            sc.pressedColor = EmberGold;
            scrollbar.colors = sc;

            scroll.viewport = vpRt;
            scroll.content = contentRt;
            scroll.verticalScrollbar = scrollbar;
            scroll.verticalScrollbarVisibility = ScrollRect.ScrollbarVisibility.AutoHideAndExpandViewport;
            scroll.verticalScrollbarSpacing = 2f;

            return scroll;
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
