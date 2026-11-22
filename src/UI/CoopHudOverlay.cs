using System;
using System.Collections.Generic;
using System.Text;
using EmberwardCoop.Core;
using EmberwardCoop.Localization;
using EmberwardCoop.Networking;
using EmberwardCoop.Rules;
using EmberwardCoop.Sync;
using TMPro;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.UI;

namespace EmberwardCoop.UI
{
    public sealed class CoopHudOverlay
    {
        private readonly SessionController _session;
        private readonly CoopLobbyPanel _lobbyPanel;
        private NetworkManager Net => _session.Net;

        // Expanded Players HUD
        private GameObject _hudRoot;
        private RectTransform _hudRect;
        private CanvasGroup _hudCg;
        private Transform _rowsContainer;
        private TMP_Text _headerText;
        private GameObject _syncDotObj;
        private GameObject _undoBtnObj;
        private TMP_Text _emoteBtnText;
        private TMP_Text _undoBtnText;
        private float _hudExpandAnim = 1f;

        // Compact Collapsed Toggle Pill
        private GameObject _collapsedPillObj;
        private RectTransform _collapsedPillRect;
        private TMP_Text _collapsedCountText;
        private bool _isCollapsed;

        // Hover Peek Tooltip
        private GameObject _peekCardObj;
        private RectTransform _peekRect;
        private CanvasGroup _peekGroup;
        private Image _peekAvatarPortrait;
        private Image _peekAvatarFrame;
        private TMP_Text _peekTitleText;
        private TMP_Text _peekSubText;
        private TMP_Text _peekBodyText;

        // Non-Obstructive Host Modal Banner
        private GameObject _modalBannerObj;
        private RectTransform _modalBannerRect;
        private TMP_Text _modalBannerText;

        // Auto-Hiding Clean In-Run Chat Feed (Player Chat, Quick-Callouts, Emotes, Connections ONLY)
        private GameObject _inRunChatObj;
        private RectTransform _inRunChatRect;
        private CanvasGroup _inRunChatCg;
        private TMP_Text _inRunChatText;
        private bool _chatDirty = true;
        private const float ChatHoldSeconds = 6.5f;
        private const float ChatFadeSeconds = 2.0f;
        private const int MaxInRunChatLines = 5;

        private readonly List<(int playerId, RectTransform rect)> _rowRects = new List<(int, RectTransform)>();
        private float _lastRefreshTime;
        private bool _dirty = true;
        private bool _lastCompactState;
        private bool _lastInBattlePhase;

        private int _hoveredPeerId = -1;
        private float _hoverDwellTimer;

        public bool IsCollapsed => _isCollapsed;

        public CoopHudOverlay(SessionController session, CoopLobbyPanel lobbyPanel)
        {
            _session = session;
            _lobbyPanel = lobbyPanel;
            Net.OnRosterUpdated += () => _dirty = true;
            session.OnSettingsChanged += () => _dirty = true;
            session.Modals.OnHostModalChanged += (_, _) => _dirty = true;
            session.Run.OnMapVotesChanged += () => _dirty = true;
            session.Ownership.OnPeekUpdated += () => _dirty = true;
            CoopLoc.OnLanguageChanged += () => _dirty = true;
            CoopLogger.OnFeedEntryAdded += _ => _chatDirty = true;

            session.Emotes.ResolvePlayerHudAnchor = GetPlayerRowCanvasAnchor;
        }

        public void SetCollapsed(bool collapsed, bool playSound = true)
        {
            if (_isCollapsed == collapsed) return;
            _isCollapsed = collapsed;
            _dirty = true;
            if (!collapsed) _hudExpandAnim = 0f;
            else HidePeekCard();

            if (playSound)
                NativeStyleProvider.PlayUiOpenSound(opening: !collapsed);
        }

        public void ToggleCollapsed()
        {
            SetCollapsed(!_isCollapsed, playSound: true);
            CoopInputGuard.NotifyWheelClosed(waitForMouseUp: true);
        }

        // Returns canvas-local position immediately to the left of the sender's HUD row (or collapsed pill).
        public Vector2? GetPlayerRowCanvasAnchor(int playerId)
        {
            if (_isCollapsed && _collapsedPillObj != null && _collapsedPillObj.activeInHierarchy && _collapsedPillRect != null)
            {
                Vector2 pillScreen = RectTransformUtility.WorldToScreenPoint(null, _collapsedPillRect.position);
                Vector2 pillCanvas = NativeStyleProvider.ScreenToCanvasLocal(pillScreen);
                float pillW = _collapsedPillRect.rect.width > 10f ? _collapsedPillRect.rect.width : 86f;
                return new Vector2(pillCanvas.x - pillW - 10f, pillCanvas.y);
            }

            if (_hudRoot == null || !_hudRoot.activeInHierarchy || _hudRect == null) return null;
            for (int i = 0; i < _rowRects.Count; i++)
            {
                var (pid, rt) = _rowRects[i];
                if (pid == playerId && rt != null)
                {
                    Vector2 rowScreen = RectTransformUtility.WorldToScreenPoint(null, rt.position);
                    Vector2 rowCanvas = NativeStyleProvider.ScreenToCanvasLocal(rowScreen);
                    float hudHalfW = _hudRect.rect.width > 10f ? _hudRect.rect.width * 0.5f : 120f;
                    return new Vector2(rowCanvas.x - hudHalfW - 14f, rowCanvas.y);
                }
            }
            return null;
        }

        public void Tick(float now)
        {
            bool pauseOpen = GameRefs.IsVanillaPauseMenuOpen();
            bool modSettingsOpen = CoopSettingsModal.Instance?.IsOpen ?? false;
            bool shouldShow = CoopConfig.ShowCoopHud.Value
                              && Net.IsConnected
                              && !GameRefs.IsLoading
                              && !GameRefs.IsInTitle
                              && !_lobbyPanel.IsOpen
                              && !pauseOpen
                              && !modSettingsOpen;

            if (!shouldShow)
            {
                if (_hudRoot != null && _hudRoot.activeSelf) _hudRoot.SetActive(false);
                if (_collapsedPillObj != null && _collapsedPillObj.activeSelf) _collapsedPillObj.SetActive(false);
                if (_modalBannerObj != null && _modalBannerObj.activeSelf) _modalBannerObj.SetActive(false);
                if (_peekCardObj != null && _peekCardObj.activeSelf) _peekCardObj.SetActive(false);
                if (_inRunChatObj != null && _inRunChatObj.activeSelf) _inRunChatObj.SetActive(false);
                _hoveredPeerId = -1;
                _hoverDwellTimer = 0f;
                return;
            }

            EnsureBuilt();

            if (!CoopInputGuard.IsCapturingWorldInput && !IsTypingInInputField() &&
                CoopConfig.ToggleHudKey != null && Input.GetKeyDown(CoopConfig.ToggleHudKey.Value))
            {
                SetCollapsed(!_isCollapsed, playSound: true);
            }

            if (_hudRoot.activeSelf == _isCollapsed)
                _hudRoot.SetActive(!_isCollapsed);
            if (_collapsedPillObj.activeSelf != _isCollapsed)
                _collapsedPillObj.SetActive(_isCollapsed);

            float dt = Time.unscaledDeltaTime;
            if (!_isCollapsed)
                NativeStyleProvider.TickOpenTransition(_hudRect, _hudCg, ref _hudExpandAnim, dt, 0.11f);

            bool compact = CoopConfig.CompactHudMode.Value;
            if (compact != _lastCompactState)
            {
                _lastCompactState = compact;
                _dirty = true;
            }

            bool inCombatWave = GameRefs.IsInBattle && (GameStateController.Instance?.IsInBattle ?? false);
            if (inCombatWave != _lastInBattlePhase)
            {
                _lastInBattlePhase = inCombatWave;
                _dirty = true;
            }

            if (_undoBtnObj != null)
            {
                bool canUndo = GameRefs.IsInBattle && _session.Undo.HasValidLocalUndo;
                if (_undoBtnObj.activeSelf != canUndo) _undoBtnObj.SetActive(canUndo);
            }

            if (_dirty || now - _lastRefreshTime >= 0.75f)
            {
                _lastRefreshTime = now;
                _dirty = false;
                RefreshHud();
            }

            UpdateSafeZonePosition();
            if (!_isCollapsed)
                UpdateHoverPeekCard(dt);
            else
                HidePeekCard();

            UpdateInRunChatFeed(now);
        }

        private void EnsureBuilt()
        {
            if (_hudRoot != null) return;

            NativeStyleProvider.HarvestFromScene();
            var canvas = NativeStyleProvider.GetOrCreateOverlayCanvas();

            _hudRoot = NativeStyleProvider.CreatePanel(canvas.transform, "EWCoop_PlayersHud", NativeStyleProvider.HudBg, addBorder: false, roleOverride: PanelSurfaceRole.Hud);
            _hudRect = _hudRoot.GetComponent<RectTransform>();
            _hudRect.anchorMin = new Vector2(1f, 0.5f);
            _hudRect.anchorMax = new Vector2(1f, 0.5f);
            _hudRect.pivot = new Vector2(1f, 0.5f);
            _hudRect.anchoredPosition = new Vector2(-12f, -10f);
            _hudRect.sizeDelta = new Vector2(246f, 64f);
            _hudCg = _hudRoot.AddComponent<CanvasGroup>();

            var fitter = _hudRoot.AddComponent<ContentSizeFitter>();
            fitter.horizontalFit = ContentSizeFitter.FitMode.Unconstrained;
            fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

            var vlg = _hudRoot.AddComponent<VerticalLayoutGroup>();
            vlg.padding = new RectOffset(11, 11, 10, 10);
            vlg.spacing = 4f;
            vlg.childControlWidth = true;
            vlg.childControlHeight = true;
            vlg.childForceExpandWidth = true;
            vlg.childForceExpandHeight = false;

            // Minimal header row + collapse toggle button
            var hdr = new GameObject("HudHeader");
            hdr.transform.SetParent(_hudRoot.transform, false);
            var hlg = hdr.AddComponent<HorizontalLayoutGroup>();
            hlg.spacing = 4f;
            hlg.childAlignment = TextAnchor.MiddleLeft;
            hlg.childControlWidth = true;
            hlg.childControlHeight = true;
            hlg.childForceExpandWidth = false;
            var hle = hdr.AddComponent<LayoutElement>();
            hle.minHeight = 20f;
            hle.preferredHeight = 20f;

            _headerText = NativeStyleProvider.CreateLabel(
                hdr.transform,
                CoopLoc.T("hud.party_title"),
                11f,
                NativeStyleProvider.EmberGold,
                TextAlignmentOptions.MidlineLeft,
                FontStyles.Bold,
                autoSize: true,
                minFontSize: 9.5f,
                addShadow: true);
            _headerText.gameObject.AddComponent<LayoutElement>().flexibleWidth = 1f;

            // Tiny amber sync dot (hidden unless desync warning active)
            _syncDotObj = new GameObject("SyncWarningDot");
            _syncDotObj.transform.SetParent(hdr.transform, false);
            var sdLe = _syncDotObj.AddComponent<LayoutElement>();
            sdLe.minWidth = 8f; sdLe.preferredWidth = 8f; sdLe.minHeight = 8f; sdLe.preferredHeight = 8f;
            var sdImg = _syncDotObj.AddComponent<Image>();
            sdImg.sprite = NativeStyleProvider.GetCircleSprite();
            sdImg.color = NativeStyleProvider.StatusWarn;
            sdImg.preserveAspect = true;
            sdImg.raycastTarget = false;
            _syncDotObj.SetActive(false);

            var undoBtn = NativeStyleProvider.CreateButton(hdr.transform, CoopLoc.T("hud.undo_btn"), NativeStyleProvider.ButtonDanger, () =>
            {
                _session.Undo.TryTriggerLocalUndo();
                CoopInputGuard.NotifyWheelClosed(waitForMouseUp: true);
            }, 9.5f, 19f);
            var ule = undoBtn.GetComponent<LayoutElement>();
            ule.minWidth = 38f; ule.preferredWidth = 42f;
            _undoBtnObj = undoBtn.gameObject;
            _undoBtnText = undoBtn.GetComponentInChildren<TMP_Text>();
            _undoBtnObj.SetActive(false);

            var emoteBtn = NativeStyleProvider.CreateButton(hdr.transform, CoopLoc.T("hud.emotes_btn"), NativeStyleProvider.ButtonPrimary, () =>
            {
                _session.Emotes.ToggleWheel();
            }, 9.5f, 19f);
            var ele = emoteBtn.GetComponent<LayoutElement>();
            ele.minWidth = 44f; ele.preferredWidth = 50f;
            _emoteBtnText = emoteBtn.GetComponentInChildren<TMP_Text>();

            var collapseBtn = NativeStyleProvider.CreateButton(hdr.transform, "▸", NativeStyleProvider.ButtonSecondary, ToggleCollapsed, 11f, 19f);
            var clLe = collapseBtn.GetComponent<LayoutElement>();
            clLe.minWidth = 22f; clLe.preferredWidth = 22f;

            NativeStyleProvider.CreateDivider(_hudRoot.transform, "HeaderDivider");

            var rowsGo = new GameObject("Rows");
            rowsGo.transform.SetParent(_hudRoot.transform, false);
            var rv = rowsGo.AddComponent<VerticalLayoutGroup>();
            rv.spacing = 3f;
            rv.childControlWidth = true;
            rv.childControlHeight = true;
            rv.childForceExpandWidth = true;
            rv.childForceExpandHeight = false;
            _rowsContainer = rowsGo.transform;

            _collapsedPillObj = NativeStyleProvider.CreatePanel(canvas.transform, "EWCoop_PlayersHudCollapsed", NativeStyleProvider.HudBg, addBorder: false, roleOverride: PanelSurfaceRole.Hud);
            _collapsedPillRect = _collapsedPillObj.GetComponent<RectTransform>();
            _collapsedPillRect.anchorMin = new Vector2(1f, 0.5f);
            _collapsedPillRect.anchorMax = new Vector2(1f, 0.5f);
            _collapsedPillRect.pivot = new Vector2(1f, 0.5f);
            _collapsedPillRect.anchoredPosition = new Vector2(-10f, 0f);
            _collapsedPillRect.sizeDelta = new Vector2(82f, 28f);

            var pillFit = _collapsedPillObj.AddComponent<ContentSizeFitter>();
            pillFit.horizontalFit = ContentSizeFitter.FitMode.PreferredSize;
            pillFit.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

            var pillHlg = _collapsedPillObj.AddComponent<HorizontalLayoutGroup>();
            pillHlg.padding = new RectOffset(11, 11, 8, 8);
            pillHlg.spacing = 5f;
            pillHlg.childAlignment = TextAnchor.MiddleCenter;
            pillHlg.childControlWidth = true;
            pillHlg.childControlHeight = true;
            pillHlg.childForceExpandWidth = false;

            var expandChev = NativeStyleProvider.CreateLabel(_collapsedPillObj.transform, "◂", 11f, NativeStyleProvider.EmberGold, TextAlignmentOptions.Center, FontStyles.Bold, addShadow: true);
            expandChev.gameObject.AddComponent<LayoutElement>().preferredWidth = 10f;

            var partyIcGo = new GameObject("PartyIcon");
            partyIcGo.transform.SetParent(_collapsedPillObj.transform, false);
            var piLe = partyIcGo.AddComponent<LayoutElement>();
            piLe.minWidth = 14f; piLe.preferredWidth = 14f; piLe.minHeight = 14f; piLe.preferredHeight = 14f;
            var piImg = partyIcGo.AddComponent<Image>();
            piImg.sprite = NativeAssetRegistry.GetCrownIconSprite();
            piImg.color = NativeStyleProvider.EmberGold;
            piImg.preserveAspect = true;
            piImg.raycastTarget = false;

            _collapsedCountText = NativeStyleProvider.CreateLabel(_collapsedPillObj.transform, "1/4", 11.5f, NativeStyleProvider.TextLight, TextAlignmentOptions.Center, FontStyles.Bold, addShadow: true);

            var pillBtn = _collapsedPillObj.AddComponent<Button>();
            pillBtn.targetGraphic = _collapsedPillObj.GetComponent<Image>();
            var pCols = pillBtn.colors;
            pCols.normalColor = Color.white;
            pCols.highlightedColor = new Color(1.18f, 1.13f, 1.04f, 1f);
            pCols.pressedColor = new Color(0.86f, 0.82f, 0.76f, 1f);
            pCols.fadeDuration = 0.08f;
            pillBtn.colors = pCols;
            NativeStyleProvider.AttachHoverSound(_collapsedPillObj, 1.12f);
            pillBtn.onClick.AddListener((UnityAction)ToggleCollapsed);
            _collapsedPillObj.SetActive(false);

            _peekCardObj = NativeStyleProvider.CreatePanel(canvas.transform, "EWCoop_TeammateTooltip", NativeStyleProvider.TooltipBg, addBorder: false, roleOverride: PanelSurfaceRole.Tooltip);
            _peekRect = _peekCardObj.GetComponent<RectTransform>();
            _peekRect.anchorMin = new Vector2(0.5f, 0.5f);
            _peekRect.anchorMax = new Vector2(0.5f, 0.5f);
            _peekRect.pivot = new Vector2(1f, 0.5f);
            _peekRect.sizeDelta = new Vector2(246f, 84f);

            var pFitter = _peekCardObj.AddComponent<ContentSizeFitter>();
            pFitter.horizontalFit = ContentSizeFitter.FitMode.Unconstrained;
            pFitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

            _peekGroup = _peekCardObj.AddComponent<CanvasGroup>();
            _peekGroup.blocksRaycasts = false;
            _peekGroup.interactable = false;

            var pv = _peekCardObj.AddComponent<VerticalLayoutGroup>();
            pv.padding = new RectOffset(12, 12, 10, 11);
            pv.spacing = 4f;
            pv.childControlWidth = true;
            pv.childControlHeight = true;
            pv.childForceExpandWidth = true;
            pv.childForceExpandHeight = false;

            var peekHdr = new GameObject("PeekHeader");
            peekHdr.transform.SetParent(_peekCardObj.transform, false);
            var phHlg = peekHdr.AddComponent<HorizontalLayoutGroup>();
            phHlg.spacing = 7f;
            phHlg.childAlignment = TextAnchor.MiddleLeft;
            phHlg.childControlWidth = true;
            phHlg.childControlHeight = true;
            phHlg.childForceExpandWidth = false;

            var peekAv = NativeStyleProvider.CreateHeroAvatarWidget(peekHdr.transform, "BASIC", NativeStyleProvider.EmberGold, 28f, clickable: false);
            var pMask = peekAv.transform.Find("PortraitMask/Portrait");
            if (pMask != null) _peekAvatarPortrait = pMask.GetComponent<Image>();
            var pFrm = peekAv.transform.Find("AvatarFrame");
            if (pFrm != null) _peekAvatarFrame = pFrm.GetComponent<Image>();

            var peekTitleCol = new GameObject("TitleCol");
            peekTitleCol.transform.SetParent(peekHdr.transform, false);
            peekTitleCol.AddComponent<LayoutElement>().flexibleWidth = 1f;
            var ptVlg = peekTitleCol.AddComponent<VerticalLayoutGroup>();
            ptVlg.spacing = 1f;
            ptVlg.childControlWidth = true;
            ptVlg.childControlHeight = true;
            ptVlg.childForceExpandWidth = true;
            ptVlg.childForceExpandHeight = false;

            _peekTitleText = NativeStyleProvider.CreateLabel(peekTitleCol.transform, "", 13f, NativeStyleProvider.EmberGold, TextAlignmentOptions.MidlineLeft, FontStyles.Bold, autoSize: true, minFontSize: 10.5f, addShadow: true);
            _peekSubText = NativeStyleProvider.CreateLabel(peekTitleCol.transform, "", 10.5f, NativeStyleProvider.TextMuted, TextAlignmentOptions.MidlineLeft);
            NativeStyleProvider.CreateDivider(_peekCardObj.transform, "PeekDivider");
            _peekBodyText = NativeStyleProvider.CreateLabel(_peekCardObj.transform, "", 11.5f, NativeStyleProvider.TextLight, TextAlignmentOptions.TopLeft, wordWrap: true);
            _peekCardObj.SetActive(false);

            // 4. Non-Obstructive Host Modal Banner (below vanilla TopCenter wave/stage bars)
            _modalBannerObj = NativeStyleProvider.CreatePanel(canvas.transform, "EWCoop_ModalBanner", NativeStyleProvider.TooltipBg, addBorder: true, roleOverride: PanelSurfaceRole.Tooltip);
            _modalBannerRect = _modalBannerObj.GetComponent<RectTransform>();
            _modalBannerRect.anchorMin = new Vector2(0.5f, 1f);
            _modalBannerRect.anchorMax = new Vector2(0.5f, 1f);
            _modalBannerRect.pivot = new Vector2(0.5f, 1f);
            _modalBannerRect.anchoredPosition = new Vector2(0f, -188f);
            _modalBannerRect.sizeDelta = new Vector2(420f, 30f);

            var mbHlg = _modalBannerObj.AddComponent<HorizontalLayoutGroup>();
            mbHlg.padding = new RectOffset(16, 16, 8, 8);
            mbHlg.childControlWidth = true;
            mbHlg.childControlHeight = true;
            var mbFit = _modalBannerObj.AddComponent<ContentSizeFitter>();
            mbFit.horizontalFit = ContentSizeFitter.FitMode.PreferredSize;
            mbFit.verticalFit = ContentSizeFitter.FitMode.PreferredSize;
            var mbCg = _modalBannerObj.AddComponent<CanvasGroup>();
            mbCg.blocksRaycasts = false;
            mbCg.interactable = false;

            _modalBannerText = NativeStyleProvider.CreateLabel(_modalBannerObj.transform, "", 12.5f, NativeStyleProvider.EmberGold, TextAlignmentOptions.Center, FontStyles.Bold, addShadow: true);
            var mbLe = _modalBannerText.gameObject.AddComponent<LayoutElement>();
            mbLe.minWidth = 180f;
            _modalBannerObj.SetActive(false);

            _inRunChatObj = NativeStyleProvider.CreatePanel(canvas.transform, "EWCoop_InRunChat", EmberwardTheme.ChatSurface, addBorder: false, isSubPanel: true, roleOverride: PanelSurfaceRole.Chat);
            _inRunChatRect = _inRunChatObj.GetComponent<RectTransform>();
            _inRunChatRect.anchorMin = new Vector2(0f, 0f);
            _inRunChatRect.anchorMax = new Vector2(0f, 0f);
            _inRunChatRect.pivot = new Vector2(0f, 0f);
            _inRunChatRect.anchoredPosition = new Vector2(14f, 368f);
            _inRunChatRect.sizeDelta = new Vector2(296f, 26f);

            var chatFit = _inRunChatObj.AddComponent<ContentSizeFitter>();
            chatFit.horizontalFit = ContentSizeFitter.FitMode.Unconstrained;
            chatFit.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

            _inRunChatCg = _inRunChatObj.AddComponent<CanvasGroup>();
            _inRunChatCg.blocksRaycasts = false;
            _inRunChatCg.interactable = false;

            var chatVlg = _inRunChatObj.AddComponent<VerticalLayoutGroup>();
            chatVlg.padding = new RectOffset(8, 8, 5, 5);
            chatVlg.spacing = 2f;
            chatVlg.childControlWidth = true;
            chatVlg.childControlHeight = true;
            chatVlg.childForceExpandWidth = true;
            chatVlg.childForceExpandHeight = false;

            _inRunChatText = NativeStyleProvider.CreateLabel(_inRunChatObj.transform, "", 11.5f, NativeStyleProvider.TextLight, TextAlignmentOptions.TopLeft, wordWrap: true, addShadow: true);
            _inRunChatText.richText = true;
            _inRunChatObj.SetActive(false);
        }

        private void UpdateInRunChatFeed(float now)
        {
            if (_inRunChatObj == null) return;

            if (_inRunChatRect != null)
            {
                // Keep completely clear of vanilla BottomLeft build/relic bars in MainGame and Deco_BottomLeft in MapScene
                float yFromBottom = GameRefs.IsInBattle ? 368f : 162f;
                _inRunChatRect.anchoredPosition = new Vector2(14f, yFromBottom);
            }

            int totalCount = CoopLogger.ChatCount;
            float idle = now - CoopLogger.LastChatActivityTime;
            if (totalCount == 0 || idle >= ChatHoldSeconds + ChatFadeSeconds)
            {
                if (_inRunChatObj.activeSelf) _inRunChatObj.SetActive(false);
                return;
            }

            if (_chatDirty)
            {
                _chatDirty = false;
                var entries = CoopLogger.GetChatSnapshot();
                if (entries.Count == 0)
                {
                    if (_inRunChatObj.activeSelf) _inRunChatObj.SetActive(false);
                    return;
                }

                int start = Math.Max(0, entries.Count - MaxInRunChatLines);
                var sb = new StringBuilder();
                for (int i = start; i < entries.Count; i++)
                {
                    if (i > start) sb.Append('\n');
                    sb.Append(entries[i].RichText);
                }
                _inRunChatText.text = sb.ToString();
                LayoutRebuilder.ForceRebuildLayoutImmediate(_inRunChatRect);
            }

            if (!_inRunChatObj.activeSelf) _inRunChatObj.SetActive(true);
            float alpha = idle <= ChatHoldSeconds
                ? 1f
                : Mathf.Clamp01(1f - (idle - ChatHoldSeconds) / ChatFadeSeconds);
            if (_inRunChatCg != null) _inRunChatCg.alpha = alpha;
        }

        private void UpdateSafeZonePosition()
        {
            if (_hudRect == null) return;

            Vector2 canvasSize = NativeStyleProvider.GetCanvasSize();
            float halfH = canvasSize.y * 0.5f;
            float hudH = Mathf.Max(56f, _hudRect.rect.height);

            if (GameRefs.IsInBattle)
            {
                float topLimitFromCenter = halfH - 426f;
                float botLimitFromCenter = -halfH + 356f;

                try
                {
                    var nwe = GameRefs.GetNextWaveEnemy();
                    if (nwe != null && nwe.gameObject.activeInHierarchy && nwe.layoutRectTransform != null &&
                        GameRefs.TryGetRectTransformScreenBounds(nwe.layoutRectTransform, out Rect nweScreen) &&
                        nweScreen.height > 10f && nweScreen.yMin > 0f && nweScreen.yMin < Screen.height)
                    {
                        Vector2 localLow = NativeStyleProvider.ScreenToCanvasLocal(new Vector2(Screen.width, nweScreen.yMin));
                        topLimitFromCenter = Mathf.Min(topLimitFromCenter, localLow.y - 12f);
                    }

                    var deck = GameRefs.GetPlayerDeck();
                    if (deck != null && deck.gameObject.activeInHierarchy)
                    {
                        var deckRt = deck.GetComponent<RectTransform>();
                        if (GameRefs.TryGetRectTransformScreenBounds(deckRt, out Rect deckScreen) &&
                            deckScreen.height > 10f && deckScreen.yMax > 0f && deckScreen.yMax < Screen.height)
                        {
                            Vector2 localHigh = NativeStyleProvider.ScreenToCanvasLocal(new Vector2(Screen.width, deckScreen.yMax));
                            botLimitFromCenter = Mathf.Max(botLimitFromCenter, localHigh.y + 14f);
                        }
                    }
                }
                catch { }

                float idealCenterY = (topLimitFromCenter + botLimitFromCenter) * 0.5f;
                float maxCenterY = topLimitFromCenter - hudH * 0.5f;
                float minCenterY = botLimitFromCenter + hudH * 0.5f;
                float clampedY = minCenterY <= maxCenterY
                    ? Mathf.Clamp(idealCenterY, minCenterY, maxCenterY)
                    : maxCenterY;

                _hudRect.anchoredPosition = new Vector2(-12f, clampedY);
                if (_collapsedPillRect != null)
                    _collapsedPillRect.anchoredPosition = new Vector2(-10f, Mathf.Clamp(topLimitFromCenter - 16f, botLimitFromCenter + 16f, topLimitFromCenter - 16f));

                if (_modalBannerRect != null)
                    _modalBannerRect.anchoredPosition = new Vector2(0f, -188f);
            }
            else
            {
                float idealMapY = halfH - 210f - hudH * 0.5f;
                float clampedY = Mathf.Clamp(idealMapY, -120f, halfH - 196f - hudH * 0.5f);
                _hudRect.anchoredPosition = new Vector2(-12f, clampedY);
                if (_collapsedPillRect != null)
                    _collapsedPillRect.anchoredPosition = new Vector2(-10f, halfH - 212f);

                if (_modalBannerRect != null)
                    _modalBannerRect.anchoredPosition = new Vector2(0f, -112f);
            }
        }

        private void RefreshHud()
        {
            bool compact = CoopConfig.CompactHudMode.Value;
            bool isSeparate = _session.Settings.EconomyMode == CoopEconomyMode.SeparateResources;
            bool showEconomy = isSeparate && GameRefs.IsInBattle;
            bool showGiftBtns = showEconomy && _session.Ruleset.SupportsResourceGifting;
            bool inCombatWave = GameRefs.IsInBattle && (GameStateController.Instance?.IsInBattle ?? false);

            int maxP = Math.Max(Net.Roster.Count, Math.Clamp(CoopConfig.MaxPlayers?.Value ?? 4, 2, 6));
            if (_collapsedCountText != null)
                _collapsedCountText.text = $"{Net.Roster.Count}/{maxP}";

            float targetWidth = compact
                ? (showEconomy ? 246f : 216f)
                : (showEconomy ? 286f : 248f);
            if (_hudRect != null)
                _hudRect.sizeDelta = new Vector2(targetWidth, _hudRect.sizeDelta.y);

            if (_headerText != null) _headerText.text = CoopLoc.T("hud.party_title");
            if (_emoteBtnText != null) _emoteBtnText.text = CoopLoc.T("hud.emotes_btn");
            if (_undoBtnText != null) _undoBtnText.text = CoopLoc.T("hud.undo_btn");
            if (_syncDotObj != null) _syncDotObj.SetActive(_session.Economy.IsDesyncWarningActive);

            _rowRects.Clear();
            for (int i = _rowsContainer.childCount - 1; i >= 0; i--)
                UnityEngine.Object.Destroy(_rowsContainer.GetChild(i).gameObject);

            foreach (var kv in Net.Roster)
            {
                var p = kv.Value;
                Color playerCol = CoopConfig.GetPlayerColor(p.ColorIndex);

                var card = NativeStyleProvider.CreatePanel(_rowsContainer, $"HudPeer_{p.PlayerId}", NativeStyleProvider.CardBg, addBorder: false, isSubPanel: true, roleOverride: PanelSurfaceRole.Card);
                var rowRt = card.GetComponent<RectTransform>();
                var cardLe = card.AddComponent<LayoutElement>();
                float rowH = compact ? 27f : 31f;
                cardLe.minHeight = rowH;
                cardLe.preferredHeight = rowH;

                var hlg = card.AddComponent<HorizontalLayoutGroup>();
                hlg.padding = new RectOffset(4, 6, 3, 3);
                hlg.spacing = 5f;
                hlg.childAlignment = TextAnchor.MiddleLeft;
                hlg.childControlWidth = true;
                hlg.childControlHeight = true;
                hlg.childForceExpandWidth = false;

                // 1. Synced Hero Avatar Portrait inside Native Emberward Frame accented with Player Color
                float avatarSz = compact ? 21f : 25f;
                NativeStyleProvider.CreateHeroAvatarWidget(card.transform, p.AvatarHeroId, playerCol, avatarSz, clickable: false);

                if (p.IsHost && NativeStyleProvider.NativeCrownSprite != null)
                {
                    var crownGo = new GameObject("HostCrown");
                    crownGo.transform.SetParent(card.transform, false);
                    var cle = crownGo.AddComponent<LayoutElement>();
                    cle.minWidth = 11f; cle.preferredWidth = 11f; cle.minHeight = 11f; cle.preferredHeight = 11f;
                    var cImg = crownGo.AddComponent<Image>();
                    cImg.sprite = NativeStyleProvider.NativeCrownSprite;
                    cImg.color = NativeStyleProvider.EmberGold;
                    cImg.preserveAspect = true;
                    cImg.raycastTarget = false;
                }

                // 2. Player Name + Inline Latency immediately to its right (never wraps or collides!)
                var namePingRow = new GameObject("NamePingRow");
                namePingRow.transform.SetParent(card.transform, false);
                var npLe = namePingRow.AddComponent<LayoutElement>();
                npLe.flexibleWidth = 1f;
                npLe.minWidth = 74f;
                var npHlg = namePingRow.AddComponent<HorizontalLayoutGroup>();
                npHlg.spacing = 4f;
                npHlg.childAlignment = TextAnchor.MiddleLeft;
                npHlg.childControlWidth = true;
                npHlg.childControlHeight = true;
                npHlg.childForceExpandWidth = false;

                var nameTmp = NativeStyleProvider.CreateLabel(
                    namePingRow.transform,
                    p.Name,
                    compact ? 11.5f : 12.5f,
                    p.PlayerId == Net.LocalPlayerId ? NativeStyleProvider.TextLight : new Color(0.95f, 0.91f, 0.84f, 1f),
                    TextAlignmentOptions.MidlineLeft,
                    p.IsHost ? FontStyles.Bold : FontStyles.Normal,
                    wordWrap: false,
                    addShadow: true);
                nameTmp.overflowMode = TextOverflowModes.Ellipsis;
                var nLe = nameTmp.gameObject.AddComponent<LayoutElement>();
                nLe.flexibleWidth = 1f;
                nLe.minWidth = 34f;

                NativeStyleProvider.CreateInlinePingLabel(namePingRow.transform, p.PingMs, p.IsHost, compact ? 9.5f : 10f);

                // 3. Mode-Specific Economy (Separate Resources Mode ONLY)
                if (showEconomy)
                {
                    int gold = 0;
                    if (p.PlayerId == Net.LocalPlayerId && MainGame.Instance?.IngameData != null)
                        gold = MainGame.Instance.IngameData.Coin;
                    else if (_session.Ownership.PeekSnapshots.TryGetValue(p.PlayerId, out var peek))
                        gold = peek.Coin;

                    var goldWrap = new GameObject("GoldWrap");
                    goldWrap.transform.SetParent(card.transform, false);
                    var gwH = goldWrap.AddComponent<HorizontalLayoutGroup>();
                    gwH.spacing = 2f;
                    gwH.childAlignment = TextAnchor.MiddleRight;
                    gwH.childControlWidth = true;
                    gwH.childControlHeight = true;
                    gwH.childForceExpandWidth = false;

                    if (NativeStyleProvider.NativeCoinSprite != null)
                    {
                        var coinIc = new GameObject("CoinIc");
                        coinIc.transform.SetParent(goldWrap.transform, false);
                        var icLe = coinIc.AddComponent<LayoutElement>();
                        icLe.minWidth = 11f; icLe.preferredWidth = 11f; icLe.minHeight = 11f; icLe.preferredHeight = 11f;
                        var icImg = coinIc.AddComponent<Image>();
                        icImg.sprite = NativeStyleProvider.NativeCoinSprite;
                        icImg.preserveAspect = true;
                        icImg.raycastTarget = false;
                    }

                    var gTmp = NativeStyleProvider.CreateLabel(goldWrap.transform, $"{gold}", 11f, NativeStyleProvider.EmberGold, TextAlignmentOptions.MidlineRight, FontStyles.Bold, addShadow: true);
                    var gLe = gTmp.gameObject.AddComponent<LayoutElement>();
                    gLe.minWidth = 22f;

                    if (showGiftBtns && p.PlayerId != Net.LocalPlayerId)
                    {
                        int targetId = p.PlayerId;
                        var giftBtn = NativeStyleProvider.CreateButton(card.transform, CoopLoc.T("hud.send_gold_btn"), NativeStyleProvider.ButtonSuccess, () =>
                        {
                            var ig = MainGame.Instance?.IngameData;
                            if (ig == null) return;
                            int amt = Math.Max(25, Mathf.RoundToInt(ig.Coin * 0.25f));
                            if (ig.Coin < amt) amt = ig.Coin;
                            if (amt > 0)
                                _session.Gifting.SendGoldToPlayer(targetId, amt);
                        }, 9.5f, rowH - 9f);
                        var gle = giftBtn.GetComponent<LayoutElement>();
                        gle.minWidth = 28f; gle.preferredWidth = 32f;
                    }
                }

                // 4. Lightweight Status Badge (Ready / Building / Drafting / In Shop)
                string statusText = ResolvePlayerStatusBadge(p, inCombatWave, out Color statusCol);
                if (!string.IsNullOrEmpty(statusText))
                {
                    var pill = NativeStyleProvider.CreateBadgePill(card.transform, "StatusBadge", new Color(statusCol.r * 0.34f + 0.14f, statusCol.g * 0.34f + 0.12f, statusCol.b * 0.34f + 0.10f, 0.94f));
                    pill.GetComponent<Image>().raycastTarget = false;
                    var pHlg = pill.AddComponent<HorizontalLayoutGroup>();
                    pHlg.padding = new RectOffset(5, 5, 1, 1);
                    pHlg.childControlWidth = true;
                    pHlg.childControlHeight = true;
                    var pLe = pill.AddComponent<LayoutElement>();
                    pLe.minHeight = rowH - 11f;
                    pLe.preferredHeight = rowH - 11f;

                    var sTmp = NativeStyleProvider.CreateLabel(pill.transform, statusText, 9.5f, statusCol, TextAlignmentOptions.Center, FontStyles.Bold, autoSize: true, minFontSize: 8.5f, addShadow: true);
                    var sLe = sTmp.gameObject.AddComponent<LayoutElement>();
                    sLe.minWidth = 34f;
                    sLe.preferredWidth = Mathf.Clamp(statusText.Length * 5.6f + 4f, 36f, 60f);
                }

                _rowRects.Add((p.PlayerId, rowRt));
            }

            if (_hudRect != null)
                LayoutRebuilder.ForceRebuildLayoutImmediate(_hudRect);

            bool independentModals = _session.Ruleset.AllowsClientIndependentDraftsAndShops;
            bool showBanner = Net.IsClient && !independentModals && _session.Modals.ActiveHostModal != CoopModalKind.None && !string.IsNullOrEmpty(_session.Modals.ActiveHostModalBanner);
            if (showBanner)
                _modalBannerText.text = _session.Modals.ActiveHostModalBanner;

            if (_modalBannerObj.activeSelf != showBanner)
                _modalBannerObj.SetActive(showBanner);
        }

        private static string ResolvePlayerStatusBadge(PeerInfo p, bool inCombatWave, out Color color)
        {
            if (!string.IsNullOrEmpty(p.ActiveModal))
            {
                string m = p.ActiveModal;
                color = NativeStyleProvider.StatusWarn;
                if (m.Equals("Shop", StringComparison.OrdinalIgnoreCase))
                    return CoopLoc.T("hud.status_in_shop");
                if (m.Equals("Draft", StringComparison.OrdinalIgnoreCase) ||
                    m.Equals("Reward", StringComparison.OrdinalIgnoreCase) ||
                    m.Equals("Perk", StringComparison.OrdinalIgnoreCase))
                    return CoopLoc.T("hud.status_drafting");
                return m;
            }

            if (p.IsWaveReady)
            {
                color = NativeStyleProvider.StatusReady;
                return CoopLoc.T("hud.status_ready");
            }

            if (GameRefs.IsInBattle && !inCombatWave)
            {
                color = NativeStyleProvider.TextMuted;
                return CoopLoc.T("hud.status_building");
            }

            color = NativeStyleProvider.TextMuted;
            return "";
        }

        private void UpdateHoverPeekCard(float dt)
        {
            if (_peekCardObj == null || _lobbyPanel.IsOpen || CoopInputGuard.IsCapturingWorldInput)
            {
                HidePeekCard();
                return;
            }

            Vector2 mouse = Input.mousePosition;
            int hoveredPeer = -1;
            RectTransform hoveredRt = null;

            for (int i = 0; i < _rowRects.Count; i++)
            {
                var (pid, rt) = _rowRects[i];
                if (rt != null && RectTransformUtility.RectangleContainsScreenPoint(rt, mouse, null))
                {
                    hoveredPeer = pid;
                    hoveredRt = rt;
                    break;
                }
            }

            if (hoveredPeer < 0)
            {
                HidePeekCard();
                return;
            }

            if (hoveredPeer != _hoveredPeerId)
            {
                _hoveredPeerId = hoveredPeer;
                _hoverDwellTimer = 0f;
            }
            else
            {
                _hoverDwellTimer += dt;
            }

            if (_hoverDwellTimer < 0.14f)
            {
                if (_peekCardObj.activeSelf) _peekCardObj.SetActive(false);
                return;
            }

            if (!_peekCardObj.activeSelf) _peekCardObj.SetActive(true);

            var peer = Net.GetPeer(hoveredPeer);
            Color col = CoopConfig.GetPlayerColor(Net.GetPlayerColorIndex(hoveredPeer));
            HeroAvatarEntry hero = NativeAssetRegistry.GetHeroAvatar(Net.GetPlayerAvatarHeroId(hoveredPeer));
            if (_peekAvatarPortrait != null)
            {
                _peekAvatarPortrait.sprite = hero.PortraitSprite ?? NativeAssetRegistry.GetFallbackIcon();
                _peekAvatarPortrait.preserveAspect = true;
            }
            if (_peekAvatarFrame != null)
                _peekAvatarFrame.color = Color.Lerp(col, NativeStyleProvider.EmberGold, 0.18f);

            _peekTitleText.text = Net.GetPlayerName(hoveredPeer);
            _peekTitleText.color = col;

            string rolePing = (peer != null && peer.IsHost)
                ? "Host"
                : $"{peer?.PingMs ?? 0} ms";
            bool inCombat = GameRefs.IsInBattle && (GameStateController.Instance?.IsInBattle ?? false);
            string stBadge = peer != null ? ResolvePlayerStatusBadge(peer, inCombat, out _) : "";
            string heroPart = !string.IsNullOrWhiteSpace(hero?.DisplayName) ? $"{hero.DisplayName}  ·  " : "";
            _peekSubText.text = string.IsNullOrEmpty(stBadge) ? $"{heroPart}{rolePing}" : $"{heroPart}{rolePing}  ·  {stBadge}";

            bool isSeparate = _session.Settings.EconomyMode == CoopEconomyMode.SeparateResources;
            var sb = new StringBuilder();

            _session.Ownership.PeekSnapshots.TryGetValue(hoveredPeer, out var snap);
            _session.Ownership.RunStats.TryGetValue(hoveredPeer, out var stats);

            if (isSeparate && snap != null)
                sb.AppendLine($"{CoopLoc.T("hud.peek_gold")}: {snap.Coin}g   ·   {CoopLoc.T("hud.peek_gems")}: {snap.Gems}");

            int relics = snap?.RelicCount ?? 0;
            int towersBuilt = stats?.TowersBuilt ?? 0;
            sb.AppendLine($"{CoopLoc.T("hud.peek_relics")}: {relics}   ·   {CoopLoc.T("hud.peek_towers")}: {towersBuilt}");

            if (snap != null && snap.LoadoutTowers != null && snap.LoadoutTowers.Length > 0)
            {
                sb.Append($"{CoopLoc.T("hud.peek_loadout")}: ");
                int limit = Math.Min(4, snap.LoadoutTowers.Length);
                for (int i = 0; i < limit; i++)
                {
                    if (i > 0) sb.Append(", ");
                    sb.Append(ShortItem((eItemType)snap.LoadoutTowers[i]));
                }
            }

            _peekBodyText.text = sb.ToString().TrimEnd();

            if (hoveredRt != null && _hudRect != null)
            {
                Vector2 rowScreen = RectTransformUtility.WorldToScreenPoint(null, hoveredRt.position);
                Vector2 rowCanvas = NativeStyleProvider.ScreenToCanvasLocal(rowScreen);
                float hudHalfW = _hudRect.rect.width > 10f ? _hudRect.rect.width * 0.5f : 120f;
                Vector2 canvasSz = NativeStyleProvider.GetCanvasSize();
                float halfH = canvasSz.y * 0.5f;
                float tipH = Mathf.Max(84f, _peekRect.rect.height);

                float x = rowCanvas.x - hudHalfW - 10f;
                float y = Mathf.Clamp(rowCanvas.y, -halfH + tipH * 0.5f + 24f, halfH - tipH * 0.5f - 24f);
                _peekRect.anchoredPosition = new Vector2(x, y);
            }

            if (_peekGroup != null)
                _peekGroup.alpha = Mathf.Clamp01((_hoverDwellTimer - 0.14f) / 0.10f);
        }

        private void HidePeekCard()
        {
            _hoveredPeerId = -1;
            _hoverDwellTimer = 0f;
            if (_peekCardObj != null && _peekCardObj.activeSelf)
                _peekCardObj.SetActive(false);
        }

        private static bool IsTypingInInputField()
        {
            var es = UnityEngine.EventSystems.EventSystem.current;
            if (es == null || es.currentSelectedGameObject == null) return false;
            return es.currentSelectedGameObject.GetComponent<TMP_InputField>() != null
                || es.currentSelectedGameObject.GetComponent<InputField>() != null;
        }

        private static string ShortItem(eItemType t)
        {
            try
            {
                string loc = ResourceManager.Instance?.GetItemDataByType(t)?.GetLocNameString(false);
                if (!string.IsNullOrWhiteSpace(loc)) return loc;
            }
            catch { }
            string s = t.ToString();
            int idx = s.IndexOf('_', 1);
            return idx > 0 && idx + 1 < s.Length ? s.Substring(idx + 1).Replace("_TOWER", "").Replace("PANEL_", "").Replace('_', ' ') : s;
        }
    }
}
