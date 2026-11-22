using System;
using System.Collections.Generic;
using System.IO;
using EmberwardCoop.Core;
using EmberwardCoop.Localization;
using EmberwardCoop.Networking;
using EmberwardCoop.UI;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace EmberwardCoop.Presence
{
    public enum CalloutId : byte
    {
        SavingForUpgrade = 0,
        NeedGoldNextWave = 1,
        ExtendingMaze = 2,
        FocusBossElites = 3,
        ReadyWhenever = 4,
        CheckSynergy = 5
    }

    public sealed class EmoteAndCalloutManager
    {
        private readonly NetworkManager _net;

        // Returns canvas-local anchor immediately to the left of the sender's HUD row
        public Func<int, Vector2?> ResolvePlayerHudAnchor;

        private const int StickerCount = 10;
        private const int CalloutCount = 6;
        private const float RateWindowSeconds = 3.0f;
        private const int MaxPerWindow = 2;
        private const float WheelRadius = 108f;

        private static readonly string[] StickerLocKeys =
        {
            "sticker.thumbs_up",
            "sticker.heart",
            "sticker.sweat",
            "sticker.mind_blown",
            "sticker.cry",
            "sticker.crown",
            "sticker.coin",
            "sticker.fire",
            "sticker.clock",
            "sticker.alert"
        };

        private static readonly string[] CalloutLocKeys =
        {
            "callout.saving_upgrade",
            "callout.need_gold",
            "callout.extending_maze",
            "callout.focus_boss",
            "callout.ready_whenever",
            "callout.check_synergy"
        };

        private static readonly float[] StickerPitches =
        {
            1.18f, 1.32f, 0.86f, 1.42f, 0.74f, 1.36f, 1.26f, 1.12f, 0.94f, 1.48f
        };

        private sealed class ActivePopup
        {
            public GameObject Root;
            public RectTransform Rect;
            public CanvasGroup Group;
            public Vector3 WorldPos;
            public bool IsWorldAnchored;
            public int SenderPlayerId;
            public float Age;
            public float Duration;
            public float BaseScale;
        }

        private readonly List<ActivePopup> _popups = new List<ActivePopup>();
        private readonly Dictionary<int, Queue<float>> _rateBuckets = new Dictionary<int, Queue<float>>();

        private bool _wheelOpen;
        private bool _calloutTabActive;
        private bool _openedViaHotkeyHold;
        private float _hotkeyPressTime;
        private float _openAnimProgress = 1f;
        private Vector3 _wheelAnchorWorld;
        private bool _hasAnchorWorld;
        private int _hoveredSlice = -1;

        private GameObject _wheelRoot;
        private RectTransform _wheelCenterRect;
        private CanvasGroup _wheelCenterGroup;
        private GameObject _stickerRingGroup;
        private GameObject _calloutListGroup;
        private Image _tabStickerBg;
        private Image _tabCalloutBg;
        private TMP_Text _tabStickerTxt;
        private TMP_Text _tabCalloutTxt;
        private TMP_Text _centerPreviewLabel;
        private TMP_Text _centerHintLabel;
        private readonly RectTransform[] _sliceRects = new RectTransform[StickerCount];
        private readonly Image[] _sliceBgs = new Image[StickerCount];
        private readonly Image[] _sliceMedallions = new Image[StickerCount];
        private readonly Image[] _sliceArtworks = new Image[StickerCount];
        private readonly TMP_Text[] _sliceLabels = new TMP_Text[StickerCount];
        private readonly TMP_Text[] _calloutLabels = new TMP_Text[CalloutCount];

        public bool IsWheelOpen => _wheelOpen;

        public EmoteAndCalloutManager(NetworkManager net)
        {
            _net = net;
            CoopLoc.OnLanguageChanged += RefreshLocalizedWheelLabels;
        }

        public void OpenWheel(bool showCallouts = false)
        {
            if (!_net.IsConnected) return;
            EnsureWheelBuilt();
            _wheelOpen = true;
            _openedViaHotkeyHold = false;
            _calloutTabActive = showCallouts;
            _hoveredSlice = -1;
            _openAnimProgress = 0f;
            _hasAnchorWorld = TryRaycastGround(out _wheelAnchorWorld);
            CoopInputGuard.IsEmoteWheelOpen = true;

            _wheelRoot.SetActive(true);
            _wheelCenterRect.anchoredPosition = Vector2.zero;
            RefreshWheelStickerSprites();
            ApplyTabVisibility();
            RefreshLocalizedWheelLabels();
            UpdateSliceHighlights();
            NativeStyleProvider.PlayUiOpenSound(true);
        }

        public void CloseWheel(bool absorbRelease = true)
        {
            bool wasOpen = _wheelOpen;
            _wheelOpen = false;
            _openedViaHotkeyHold = false;
            _hoveredSlice = -1;
            CoopInputGuard.IsEmoteWheelOpen = false;
            if (wasOpen && absorbRelease)
                CoopInputGuard.NotifyWheelClosed(waitForMouseUp: true);
            if (_wheelRoot != null && _wheelRoot.activeSelf)
                _wheelRoot.SetActive(false);
        }

        public void ToggleWheel()
        {
            if (_wheelOpen) CloseWheel(absorbRelease: true);
            else OpenWheel(false);
        }

        public void Tick(float now, float dt)
        {
            if (!_net.IsConnected || GameRefs.IsLoading || GameRefs.IsVanillaPauseMenuOpen())
            {
                if (_wheelOpen) CloseWheel(absorbRelease: false);
                TickPopups(dt);
                return;
            }

            HandleWheelInput(now, dt);
            TickPopups(dt);
        }

        private void HandleWheelInput(float now, float dt)
        {
            if (CoopInputGuard.IsCoopModalOpen || IsTypingInInputField())
            {
                if (_wheelOpen) CloseWheel(absorbRelease: false);
                return;
            }

            KeyCode key = CoopConfig.EmoteWheelKey.Value;
            if (Input.GetKeyDown(key))
            {
                if (_wheelOpen)
                {
                    CloseWheel(absorbRelease: true);
                    return;
                }
                else
                {
                    EnsureWheelBuilt();
                    _wheelOpen = true;
                    _openedViaHotkeyHold = true;
                    _hotkeyPressTime = now;
                    _calloutTabActive = false;
                    _hoveredSlice = -1;
                    _openAnimProgress = 0f;
                    _hasAnchorWorld = TryRaycastGround(out _wheelAnchorWorld);
                    CoopInputGuard.IsEmoteWheelOpen = true;

                    _wheelRoot.SetActive(true);
                    PositionWheelNearCursor();
                    RefreshWheelStickerSprites();
                    ApplyTabVisibility();
                    RefreshLocalizedWheelLabels();
                    UpdateSliceHighlights();
                    NativeStyleProvider.PlayUiOpenSound(true);
                }
            }

            if (!_wheelOpen) return;

            NativeStyleProvider.TickOpenTransition(_wheelCenterRect, _wheelCenterGroup, ref _openAnimProgress, dt, 0.11f);

            if (Input.GetKeyDown(KeyCode.Escape) || Input.GetMouseButtonDown(1))
            {
                NativeStyleProvider.PlayUiOpenSound(false);
                CloseWheel(absorbRelease: true);
                return;
            }

            // Tab key OR Mouse Scroll-Wheel cycles between Stickers and Quick-Chat tabs (camera zoom stays locked!)
            float scrollY = Input.mouseScrollDelta.y;
            if (Input.GetKeyDown(KeyCode.Tab) || Mathf.Abs(scrollY) > 0.05f)
            {
                _calloutTabActive = !_calloutTabActive;
                ApplyTabVisibility();
                NativeStyleProvider.PlayUiHoverSound(1.16f);
            }

            for (int i = 0; i < CalloutCount; i++)
            {
                if (Input.GetKeyDown(KeyCode.Alpha1 + i) || Input.GetKeyDown(KeyCode.Keypad1 + i))
                {
                    FireLocalCallout((CalloutId)i);
                    CloseWheel(absorbRelease: true);
                    return;
                }
            }

            if (!_calloutTabActive)
            {
                Vector2 mouseCanvas = NativeStyleProvider.ScreenToCanvasLocal(Input.mousePosition);
                Vector2 delta = mouseCanvas - _wheelCenterRect.anchoredPosition;
                int prevSlice = _hoveredSlice;

                if (delta.sqrMagnitude >= 961f) // 31px canvas-unit deadzone
                {
                    float angle = Mathf.Atan2(delta.x, delta.y) * Mathf.Rad2Deg;
                    if (angle < 0f) angle += 360f;
                    int idx = Mathf.FloorToInt(((angle + 18f) % 360f) / 36f);
                    _hoveredSlice = Math.Clamp(idx, 0, StickerCount - 1);
                }
                else
                {
                    _hoveredSlice = -1;
                }

                if (_hoveredSlice != prevSlice)
                {
                    if (_hoveredSlice >= 0)
                        NativeStyleProvider.PlayUiHoverSound(1.06f + _hoveredSlice * 0.015f);
                    UpdateSliceHighlights();
                }
            }

            // Hold >= 0.20s fires on release; short tap leaves the wheel open for clicking.
            if (_openedViaHotkeyHold && Input.GetKeyUp(key))
            {
                _openedViaHotkeyHold = false;
                if (now - _hotkeyPressTime >= 0.20f)
                {
                    if (!_calloutTabActive && _hoveredSlice >= 0)
                        FireLocalSticker((StickerId)_hoveredSlice);
                    CloseWheel(absorbRelease: true);
                }
            }
        }

        private void PositionWheelNearCursor()
        {
            Vector2 canvasSz = NativeStyleProvider.GetCanvasSize();
            Vector2 cursorLocal = NativeStyleProvider.ScreenToCanvasLocal(Input.mousePosition);
            float padX = 168f;
            float padBottom = 160f;
            float padTop = 185f;
            float halfW = canvasSz.x * 0.5f;
            float halfH = canvasSz.y * 0.5f;

            float cx = Mathf.Clamp(cursorLocal.x, -halfW + padX, halfW - padX);
            float cy = Mathf.Clamp(cursorLocal.y, -halfH + padBottom, halfH - padTop);
            _wheelCenterRect.anchoredPosition = new Vector2(cx, cy);
        }

        public void FireLocalSticker(StickerId id)
        {
            if (!_net.IsConnected) return;
            if (!TryConsumeRateToken(_net.LocalPlayerId, Time.unscaledTime))
            {
                GameRefs.ShowGameToast(CoopLoc.T("wheel.rate_limited"), 1.6f);
                return;
            }

            Vector3 worldPos = _hasAnchorWorld ? _wheelAnchorWorld : Vector3.zero;
            bool hasWorld = _hasAnchorWorld && GameRefs.IsInBattle;
            if (!hasWorld && GameRefs.IsInBattle && TryRaycastGround(out Vector3 fallback))
            {
                worldPos = fallback;
                hasWorld = true;
            }

            RenderStickerDualAnchor(_net.LocalPlayerId, id, hasWorld, worldPos);

            _net.SendToAllPeersOrHost(PacketType.StickerEmote, w =>
            {
                w.Write(_net.LocalPlayerId);
                w.Write((byte)id);
                w.Write(hasWorld);
                w.WriteVector3(worldPos);
            });
        }

        public void FireLocalCallout(CalloutId id)
        {
            if (!_net.IsConnected) return;
            if (!TryConsumeRateToken(_net.LocalPlayerId, Time.unscaledTime))
            {
                GameRefs.ShowGameToast(CoopLoc.T("wheel.rate_limited"), 1.6f);
                return;
            }

            Vector3 worldPos = _hasAnchorWorld ? _wheelAnchorWorld : Vector3.zero;
            bool hasWorld = _hasAnchorWorld && GameRefs.IsInBattle;
            if (!hasWorld && GameRefs.IsInBattle && TryRaycastGround(out Vector3 fallback))
            {
                worldPos = fallback;
                hasWorld = true;
            }

            RenderCalloutDualAnchor(_net.LocalPlayerId, id, hasWorld, worldPos);

            _net.SendToAllPeersOrHost(PacketType.QuickCallout, w =>
            {
                w.Write(_net.LocalPlayerId);
                w.Write((byte)id);
                w.Write(hasWorld);
                w.WriteVector3(worldPos);
            });
        }

        public void HandleStickerPacket(int senderId, BinaryReader r)
        {
            int actorId = r.ReadInt32();
            if (_net.IsHost) actorId = senderId;
            var sticker = (StickerId)Math.Clamp((int)r.ReadByte(), 0, StickerCount - 1);
            bool hasWorld = r.ReadBoolean();
            Vector3 worldPos = r.ReadVector3();

            if (!TryConsumeRateToken(actorId, Time.unscaledTime))
                return;

            if (actorId != _net.LocalPlayerId && !CoopConfig.MuteTeammateStickers.Value)
                RenderStickerDualAnchor(actorId, sticker, hasWorld && GameRefs.IsInBattle, worldPos);

            if (_net.IsHost)
            {
                _net.BroadcastToClients(PacketType.StickerEmote, w =>
                {
                    w.Write(actorId);
                    w.Write((byte)sticker);
                    w.Write(hasWorld);
                    w.WriteVector3(worldPos);
                }, DeliveryMode.ReliableOrdered, excludePeerId: senderId);
            }
        }

        public void HandleCalloutPacket(int senderId, BinaryReader r)
        {
            int actorId = r.ReadInt32();
            if (_net.IsHost) actorId = senderId;
            var callout = (CalloutId)Math.Clamp((int)r.ReadByte(), 0, CalloutCount - 1);
            bool hasWorld = r.ReadBoolean();
            Vector3 worldPos = r.ReadVector3();

            if (!TryConsumeRateToken(actorId, Time.unscaledTime))
                return;

            if (actorId != _net.LocalPlayerId && !CoopConfig.MuteTeammateStickers.Value)
                RenderCalloutDualAnchor(actorId, callout, hasWorld && GameRefs.IsInBattle, worldPos);

            if (_net.IsHost)
            {
                _net.BroadcastToClients(PacketType.QuickCallout, w =>
                {
                    w.Write(actorId);
                    w.Write((byte)callout);
                    w.Write(hasWorld);
                    w.WriteVector3(worldPos);
                }, DeliveryMode.ReliableOrdered, excludePeerId: senderId);
            }
        }

        private bool TryConsumeRateToken(int playerId, float now)
        {
            if (!_rateBuckets.TryGetValue(playerId, out var q))
            {
                q = new Queue<float>();
                _rateBuckets[playerId] = q;
            }
            while (q.Count > 0 && now - q.Peek() > RateWindowSeconds)
                q.Dequeue();

            if (q.Count >= MaxPerWindow) return false;
            q.Enqueue(now);
            return true;
        }

        private void RenderStickerDualAnchor(int playerId, StickerId id, bool hasWorld, Vector3 worldPos)
        {
            var canvas = NativeStyleProvider.GetOrCreateOverlayCanvas();
            if (canvas == null) return;

            Color playerCol = CoopConfig.GetPlayerColor(_net.GetPlayerColorIndex(playerId));
            string senderName = _net.GetPlayerName(playerId);
            string heroId = _net.GetPlayerAvatarHeroId(playerId);
            string label = CoopLoc.T(StickerLocKeys[(int)id]);
            float scale = CoopConfig.GetEffectiveStickerScale();

            if (hasWorld && Camera.main != null)
            {
                var pop = BuildStickerPopup(canvas.transform, id, label, senderName, heroId, playerCol, compact: false);
                pop.IsWorldAnchored = true;
                pop.WorldPos = worldPos;
                pop.SenderPlayerId = playerId;
                pop.Duration = 2.5f;
                pop.BaseScale = scale * 0.90f;
                _popups.Add(pop);
            }

            var hudPop = BuildStickerPopup(canvas.transform, id, label, senderName, heroId, playerCol, compact: true);
            hudPop.IsWorldAnchored = false;
            hudPop.SenderPlayerId = playerId;
            hudPop.Duration = 2.6f;
            hudPop.BaseScale = Mathf.Lerp(1f, scale, 0.35f);
            _popups.Add(hudPop);

            CoopLogger.PushChatMessage(ChatEntryKind.Emote, $"{senderName} [{label}]", playerCol);
            PlayEmoteCue(StickerPitches[(int)id]);
        }

        public void ShowGoldTransferToast(int playerId, string message)
        {
            var canvas = NativeStyleProvider.GetOrCreateOverlayCanvas();
            if (canvas == null || string.IsNullOrWhiteSpace(message)) return;

            var pop = BuildSpeechBubble(canvas.transform, _net.GetPlayerName(playerId), _net.GetPlayerAvatarHeroId(playerId), message, NativeStyleProvider.EmberGold);
            pop.IsWorldAnchored = false;
            pop.SenderPlayerId = playerId;
            pop.Duration = 2.6f;
            pop.BaseScale = 1f;
            _popups.Add(pop);
        }

        private void RenderCalloutDualAnchor(int playerId, CalloutId id, bool hasWorld, Vector3 worldPos)
        {
            var canvas = NativeStyleProvider.GetOrCreateOverlayCanvas();
            if (canvas == null) return;

            Color playerCol = CoopConfig.GetPlayerColor(_net.GetPlayerColorIndex(playerId));
            string senderName = _net.GetPlayerName(playerId);
            string heroId = _net.GetPlayerAvatarHeroId(playerId);
            string phrase = CoopLoc.T(CalloutLocKeys[(int)id]);
            float scale = CoopConfig.GetEffectiveStickerScale();

            CoopLogger.PushChatMessage(ChatEntryKind.QuickCallout, $"{senderName}: \"{phrase}\"", playerCol);

            var hudBubble = BuildSpeechBubble(canvas.transform, senderName, heroId, phrase, playerCol);
            hudBubble.IsWorldAnchored = false;
            hudBubble.SenderPlayerId = playerId;
            hudBubble.Duration = 3.4f;
            hudBubble.BaseScale = Mathf.Lerp(1f, scale, 0.3f);
            _popups.Add(hudBubble);

            if (hasWorld && Camera.main != null)
            {
                var worldBubble = BuildSpeechBubble(canvas.transform, senderName, heroId, phrase, playerCol);
                worldBubble.IsWorldAnchored = true;
                worldBubble.WorldPos = worldPos;
                worldBubble.SenderPlayerId = playerId;
                worldBubble.Duration = 2.9f;
                worldBubble.BaseScale = scale * 0.86f;
                _popups.Add(worldBubble);
            }

            PlayEmoteCue(1.06f + (int)id * 0.04f);
        }

        private static ActivePopup BuildStickerPopup(Transform parent, StickerId id, string caption, string senderName, string heroId, Color borderCol, bool compact)
        {
            var go = NativeStyleProvider.CreatePanel(parent, compact ? "EWCoop_HudSticker" : "EWCoop_WorldSticker", NativeStyleProvider.TooltipBg, addBorder: true, isSubPanel: true);
            var rt = go.GetComponent<RectTransform>();
            rt.anchorMin = new Vector2(0.5f, 0.5f);
            rt.anchorMax = new Vector2(0.5f, 0.5f);
            rt.pivot = compact ? new Vector2(1f, 0.5f) : new Vector2(0.5f, 0.5f);

            var cg = go.AddComponent<CanvasGroup>();
            cg.blocksRaycasts = false;
            cg.interactable = false;

            if (compact)
            {
                rt.sizeDelta = new Vector2(174f, 52f);
                var fit = go.AddComponent<ContentSizeFitter>();
                fit.horizontalFit = ContentSizeFitter.FitMode.PreferredSize;
                fit.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

                var hlg = go.AddComponent<HorizontalLayoutGroup>();
                hlg.padding = new RectOffset(11, 12, 8, 8);
                hlg.spacing = 6f;
                hlg.childAlignment = TextAnchor.MiddleLeft;
                hlg.childControlWidth = true;
                hlg.childControlHeight = true;
                hlg.childForceExpandWidth = false;

                NativeStyleProvider.CreateHeroAvatarWidget(go.transform, heroId, borderCol, 24f, clickable: false);
                NativeStyleProvider.CreateStickerMedallionWidget(go.transform, id, borderCol, 34f);

                var lbl = NativeStyleProvider.CreateLabel(go.transform, caption, 12f, borderCol, TextAlignmentOptions.MidlineLeft, FontStyles.Bold, autoSize: true, minFontSize: 10f);
                var lle = lbl.gameObject.AddComponent<LayoutElement>();
                lle.minWidth = 48f;
                lle.preferredWidth = Mathf.Clamp(caption.Length * 7.2f + 8f, 52f, 128f);
            }
            else
            {
                rt.sizeDelta = new Vector2(114f, 114f);
                var vlg = go.AddComponent<VerticalLayoutGroup>();
                vlg.padding = new RectOffset(6, 6, 6, 5);
                vlg.spacing = 3f;
                vlg.childAlignment = TextAnchor.MiddleCenter;
                vlg.childControlWidth = false;
                vlg.childControlHeight = true;
                vlg.childForceExpandWidth = false;
                vlg.childForceExpandHeight = false;

                NativeStyleProvider.CreateStickerMedallionWidget(go.transform, id, borderCol, 60f);

                var capLbl = NativeStyleProvider.CreateLabel(go.transform, caption, 12.5f, NativeStyleProvider.TextLight, TextAlignmentOptions.Center, FontStyles.Bold, autoSize: true, minFontSize: 9.5f);
                var capLe = capLbl.gameObject.AddComponent<LayoutElement>();
                capLe.minWidth = 96f; capLe.preferredWidth = 100f; capLe.minHeight = 16f;

                var nameLbl = NativeStyleProvider.CreateLabel(go.transform, senderName, 10f, borderCol, TextAlignmentOptions.Center);
                var nLe = nameLbl.gameObject.AddComponent<LayoutElement>();
                nLe.minWidth = 96f; nLe.preferredWidth = 100f; nLe.minHeight = 14f;
            }

            return new ActivePopup { Root = go, Rect = rt, Group = cg };
        }

        private static ActivePopup BuildSpeechBubble(Transform parent, string senderName, string heroId, string phrase, Color borderCol)
        {
            var go = NativeStyleProvider.CreateSpeechBubblePanel(parent, "EWCoop_SpeechBubble", NativeStyleProvider.TooltipBg, borderCol);
            var rt = go.GetComponent<RectTransform>();
            rt.anchorMin = new Vector2(0.5f, 0.5f);
            rt.anchorMax = new Vector2(0.5f, 0.5f);
            rt.pivot = new Vector2(1f, 0.5f);
            rt.sizeDelta = new Vector2(246f, 52f);

            var fit = go.AddComponent<ContentSizeFitter>();
            fit.horizontalFit = ContentSizeFitter.FitMode.Unconstrained;
            fit.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

            var cg = go.AddComponent<CanvasGroup>();
            cg.blocksRaycasts = false;
            cg.interactable = false;

            var hlg = go.AddComponent<HorizontalLayoutGroup>();
            hlg.padding = new RectOffset(12, 13, 9, 10);
            hlg.spacing = 7f;
            hlg.childAlignment = TextAnchor.MiddleLeft;
            hlg.childControlWidth = true;
            hlg.childControlHeight = true;
            hlg.childForceExpandWidth = false;
            hlg.childForceExpandHeight = false;

            NativeStyleProvider.CreateHeroAvatarWidget(go.transform, heroId, borderCol, 28f, clickable: false);

            var textCol = new GameObject("TextCol");
            textCol.transform.SetParent(go.transform, false);
            textCol.AddComponent<LayoutElement>().flexibleWidth = 1f;
            var vlg = textCol.AddComponent<VerticalLayoutGroup>();
            vlg.spacing = 2f;
            vlg.childControlWidth = true;
            vlg.childControlHeight = true;
            vlg.childForceExpandWidth = true;
            vlg.childForceExpandHeight = false;

            NativeStyleProvider.CreateLabel(textCol.transform, senderName, 10.5f, borderCol, TextAlignmentOptions.MidlineLeft, FontStyles.Bold);
            NativeStyleProvider.CreateLabel(textCol.transform, $"\"{phrase}\"", 12f, NativeStyleProvider.TextLight, TextAlignmentOptions.TopLeft, wordWrap: true);

            return new ActivePopup { Root = go, Rect = rt, Group = cg };
        }

        private void TickPopups(float dt)
        {
            var cam = Camera.main;
            bool hasTowerPanel = GameRefs.TryGetActiveTowerInspectionScreenRect(out Rect towerRect);
            Vector2 canvasSz = NativeStyleProvider.GetCanvasSize();
            float halfW = canvasSz.x * 0.5f;
            float halfH = canvasSz.y * 0.5f;

            int hudStackIndex = 0;

            for (int i = _popups.Count - 1; i >= 0; i--)
            {
                var p = _popups[i];
                p.Age += dt;
                float t = Mathf.Clamp01(p.Age / p.Duration);
                if (t >= 1f || p.Root == null)
                {
                    if (p.Root != null) UnityEngine.Object.Destroy(p.Root);
                    _popups.RemoveAt(i);
                    continue;
                }

                float elastic = p.Age < 0.20f ? ElasticOut(p.Age / 0.20f) : 1f;
                p.Rect.localScale = Vector3.one * (p.BaseScale * elastic);

                float alpha = t > 0.72f ? Mathf.Clamp01(1f - (t - 0.72f) / 0.28f) : 1f;

                if (p.IsWorldAnchored)
                {
                    if (cam == null) { p.Root.SetActive(false); continue; }
                    Vector3 sp = cam.WorldToScreenPoint(p.WorldPos + new Vector3(0f, 1.30f + t * 0.75f, 0f));
                    bool visible = sp.z > 0f && sp.x > -80f && sp.x < Screen.width + 80f && sp.y > -80f && sp.y < Screen.height + 80f;
                    if (p.Root.activeSelf != visible) p.Root.SetActive(visible);
                    if (visible)
                    {
                        if (hasTowerPanel && towerRect.Contains(new Vector2(sp.x, sp.y)))
                            alpha *= 0.22f;
                        p.Rect.anchoredPosition = NativeStyleProvider.ScreenToCanvasLocal(new Vector2(sp.x, sp.y));
                    }
                }
                else
                {
                    Vector2? anchor = ResolvePlayerHudAnchor?.Invoke(p.SenderPlayerId);
                    Vector2 target = anchor ?? new Vector2(halfW - 260f, 10f);
                    float yPos = target.y + hudStackIndex * 54f + t * 10f;
                    yPos = Mathf.Clamp(yPos, -halfH + 120f, halfH - 120f);
                    float xPos = Mathf.Clamp(target.x, -halfW + 250f, halfW - 14f);

                    // Avoid overlapping active tower inspection drawer
                    if (hasTowerPanel)
                    {
                        Vector2 canvasTowerCenter = NativeStyleProvider.ScreenToCanvasLocal(towerRect.center);
                        Vector2 canvasTowerSz = NativeStyleProvider.ScreenToCanvasSize(towerRect.size);
                        Rect cTower = new Rect(canvasTowerCenter - canvasTowerSz * 0.5f, canvasTowerSz);
                        Rect bubbleRect = new Rect(xPos - p.Rect.rect.width, yPos - p.Rect.rect.height * 0.5f, p.Rect.rect.width, p.Rect.rect.height);
                        if (cTower.Overlaps(bubbleRect))
                        {
                            float shiftedY = cTower.yMax + p.Rect.rect.height * 0.5f + 12f;
                            if (shiftedY < halfH - 90f) yPos = shiftedY;
                            else alpha *= 0.22f;
                        }
                    }

                    p.Rect.anchoredPosition = new Vector2(xPos, yPos);
                    hudStackIndex++;
                }

                p.Group.alpha = alpha;
            }
        }

        private static float ElasticOut(float t)
        {
            t = Mathf.Clamp01(t);
            if (t <= 0f) return 0f;
            if (t >= 1f) return 1f;
            return Mathf.Pow(2f, -10f * t) * Mathf.Sin((t - 0.075f) * (2f * Mathf.PI) / 0.32f) + 1f;
        }

        private static void PlayEmoteCue(float pitch)
        {
            if (CoopConfig.StickerAudioVolume.Value <= 0.02f) return;
            NativeStyleProvider.PlayUiClickSound(pitch);
        }

        private void EnsureWheelBuilt()
        {
            if (_wheelRoot != null) return;
            NativeStyleProvider.HarvestFromScene();
            var canvas = NativeStyleProvider.GetOrCreateOverlayCanvas();

            _wheelRoot = new GameObject("EWCoop_EmoteWheel");
            _wheelRoot.transform.SetParent(canvas.transform, false);
            var fullRt = _wheelRoot.AddComponent<RectTransform>();
            NativeStyleProvider.StretchFill(fullRt);

            // Full-screen raycast-blocking backdrop guarantees zero click-through / hover-through to world or vanilla UI
            var backdropImg = _wheelRoot.AddComponent<Image>();
            backdropImg.color = EmberwardTheme.WheelScrim;
            backdropImg.raycastTarget = true;
            var backdropBtn = _wheelRoot.AddComponent<Button>();
            backdropBtn.transition = Selectable.Transition.None;
            backdropBtn.onClick.AddListener((UnityEngine.Events.UnityAction)(() =>
            {
                NativeStyleProvider.PlayUiOpenSound(false);
                CloseWheel(absorbRelease: true);
            }));

            var centerGo = new GameObject("WheelCenter");
            centerGo.transform.SetParent(_wheelRoot.transform, false);
            _wheelCenterRect = centerGo.AddComponent<RectTransform>();
            _wheelCenterRect.anchorMin = new Vector2(0.5f, 0.5f);
            _wheelCenterRect.anchorMax = new Vector2(0.5f, 0.5f);
            _wheelCenterRect.sizeDelta = new Vector2(300f, 300f);
            _wheelCenterGroup = centerGo.AddComponent<CanvasGroup>();

            var tabsBar = NativeStyleProvider.CreatePanel(centerGo.transform, "TabsBar", NativeStyleProvider.PanelSubBg, addBorder: false, isSubPanel: true);
            var tabsRt = tabsBar.GetComponent<RectTransform>();
            tabsRt.anchoredPosition = new Vector2(0f, 160f);
            tabsRt.sizeDelta = new Vector2(256f, 32f);
            var hlg = tabsBar.AddComponent<HorizontalLayoutGroup>();
            hlg.padding = new RectOffset(4, 4, 4, 4);
            hlg.spacing = 4f;
            hlg.childControlWidth = true;
            hlg.childControlHeight = true;
            hlg.childForceExpandWidth = true;

            var sTabBtn = NativeStyleProvider.CreateButton(tabsBar.transform, CoopLoc.T("wheel.tab_stickers"), NativeStyleProvider.ButtonPrimary, () =>
            {
                _calloutTabActive = false;
                ApplyTabVisibility();
            }, 11f, 24f, isTab: true);
            _tabStickerBg = sTabBtn.GetComponent<Image>();
            _tabStickerTxt = sTabBtn.GetComponentInChildren<TMP_Text>();

            var cTabBtn = NativeStyleProvider.CreateButton(tabsBar.transform, CoopLoc.T("wheel.tab_callouts"), NativeStyleProvider.ButtonSecondary, () =>
            {
                _calloutTabActive = true;
                ApplyTabVisibility();
            }, 11f, 24f, isTab: true);
            _tabCalloutBg = cTabBtn.GetComponent<Image>();
            _tabCalloutTxt = cTabBtn.GetComponentInChildren<TMP_Text>();

            _stickerRingGroup = new GameObject("StickerRing");
            _stickerRingGroup.transform.SetParent(centerGo.transform, false);
            NativeStyleProvider.StretchFill(_stickerRingGroup.AddComponent<RectTransform>());

            var hub = NativeStyleProvider.CreatePanel(_stickerRingGroup.transform, "Hub", NativeStyleProvider.PanelSubBg, addBorder: false, isSubPanel: true);
            var hubRt = hub.GetComponent<RectTransform>();
            hubRt.sizeDelta = new Vector2(106f, 48f);
            var hubVlg = hub.AddComponent<VerticalLayoutGroup>();
            hubVlg.padding = new RectOffset(6, 6, 4, 4);
            hubVlg.spacing = 1f;
            hubVlg.childAlignment = TextAnchor.MiddleCenter;
            hubVlg.childControlWidth = true;
            hubVlg.childControlHeight = true;

            _centerPreviewLabel = NativeStyleProvider.CreateLabel(hub.transform, CoopLoc.T("wheel.tab_stickers"), 11.5f, NativeStyleProvider.EmberGold, TextAlignmentOptions.Center, FontStyles.Bold, autoSize: true, minFontSize: 9f, addShadow: true);
            _centerHintLabel = NativeStyleProvider.CreateLabel(hub.transform, CoopLoc.T("wheel.scroll_hint"), 8.5f, NativeStyleProvider.TextMuted, TextAlignmentOptions.Center, autoSize: true, minFontSize: 7.5f);

            for (int i = 0; i < StickerCount; i++)
            {
                var sid = (StickerId)i;
                float rad = (i * 36f) * Mathf.Deg2Rad;
                Vector2 pos = new Vector2(Mathf.Sin(rad) * WheelRadius, Mathf.Cos(rad) * WheelRadius);

                var slot = NativeStyleProvider.CreatePanel(_stickerRingGroup.transform, $"Sticker_{sid}", NativeStyleProvider.PanelSubBg, addBorder: false, isSubPanel: true);
                var srt = slot.GetComponent<RectTransform>();
                srt.anchoredPosition = pos;
                srt.sizeDelta = new Vector2(66f, 66f);
                _sliceRects[i] = srt;

                Color accCol = NativeStickerCatalog.GetAccentColor(sid);
                _sliceBgs[i] = slot.GetComponent<Image>();

                var btn = slot.AddComponent<Button>();
                btn.transition = Selectable.Transition.None;
                btn.onClick.AddListener((UnityEngine.Events.UnityAction)(() =>
                {
                    FireLocalSticker(sid);
                    CloseWheel(absorbRelease: true);
                }));

                var vlg = slot.AddComponent<VerticalLayoutGroup>();
                vlg.padding = new RectOffset(4, 4, 4, 3);
                vlg.spacing = 2f;
                vlg.childAlignment = TextAnchor.MiddleCenter;
                vlg.childControlWidth = false;
                vlg.childControlHeight = true;
                vlg.childForceExpandWidth = false;
                vlg.childForceExpandHeight = false;

                var medGo = NativeStyleProvider.CreateStickerMedallionWidget(slot.transform, sid, accCol, 38f);
                _sliceMedallions[i] = medGo.GetComponent<Image>();
                var artTf = medGo.transform.Find("Artwork");
                if (artTf != null) _sliceArtworks[i] = artTf.GetComponent<Image>();

                var lbl = NativeStyleProvider.CreateLabel(slot.transform, CoopLoc.T(StickerLocKeys[i]), 9.5f, NativeStyleProvider.TextLight, TextAlignmentOptions.Center, FontStyles.Bold, autoSize: true, minFontSize: 7.5f);
                var lLe = lbl.gameObject.AddComponent<LayoutElement>();
                lLe.minWidth = 56f; lLe.preferredWidth = 58f; lLe.minHeight = 14f;
                _sliceLabels[i] = lbl;
            }

            _calloutListGroup = NativeStyleProvider.CreatePanel(centerGo.transform, "CalloutPanel", NativeStyleProvider.PanelBg, addBorder: false, roleOverride: PanelSurfaceRole.Window);
            var crt = _calloutListGroup.GetComponent<RectTransform>();
            crt.anchoredPosition = new Vector2(0f, -8f);
            crt.sizeDelta = new Vector2(316f, 232f);
            var cFit = _calloutListGroup.AddComponent<ContentSizeFitter>();
            cFit.horizontalFit = ContentSizeFitter.FitMode.Unconstrained;
            cFit.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

            var cv = _calloutListGroup.AddComponent<VerticalLayoutGroup>();
            cv.padding = new RectOffset(18, 18, 15, 16);
            cv.spacing = 5f;
            cv.childControlWidth = true;
            cv.childControlHeight = true;
            cv.childForceExpandWidth = true;
            cv.childForceExpandHeight = false;

            for (int i = 0; i < CalloutCount; i++)
            {
                var cid = (CalloutId)i;
                var btn = NativeStyleProvider.CreateButton(_calloutListGroup.transform, $"[{i + 1}] {CoopLoc.T(CalloutLocKeys[i])}", NativeStyleProvider.CardBg, () =>
                {
                    FireLocalCallout(cid);
                    CloseWheel(absorbRelease: true);
                }, 12.5f, 32f);
                var txt = btn.GetComponentInChildren<TMP_Text>();
                if (txt != null)
                {
                    txt.alignment = TextAlignmentOptions.MidlineLeft;
                    txt.enableWordWrapping = true;
                    txt.enableAutoSizing = true;
                    txt.fontSizeMin = 9.5f;
                    txt.fontSizeMax = 12.5f;
                }
                _calloutLabels[i] = txt;
            }

            _wheelRoot.SetActive(false);
        }

        private void ApplyTabVisibility()
        {
            if (_stickerRingGroup != null) _stickerRingGroup.SetActive(!_calloutTabActive);
            if (_calloutListGroup != null) _calloutListGroup.SetActive(_calloutTabActive);
            if (_tabStickerBg != null) _tabStickerBg.color = !_calloutTabActive ? NativeStyleProvider.ButtonPrimary : NativeStyleProvider.ButtonSecondary;
            if (_tabCalloutBg != null) _tabCalloutBg.color = _calloutTabActive ? NativeStyleProvider.ButtonPrimary : NativeStyleProvider.ButtonSecondary;
        }

        private void RefreshWheelStickerSprites()
        {
            if (_wheelRoot == null) return;
            NativeAssetRegistry.EnsureScanned();
            Sprite medSp = NativeStickerCatalog.GetMedallionSprite();
            for (int i = 0; i < StickerCount; i++)
            {
                if (_sliceMedallions[i] != null)
                {
                    _sliceMedallions[i].sprite = medSp;
                    _sliceMedallions[i].preserveAspect = true;
                }
                if (_sliceArtworks[i] != null)
                {
                    _sliceArtworks[i].sprite = NativeStickerCatalog.GetArtworkSprite((StickerId)i);
                    _sliceArtworks[i].preserveAspect = true;
                }
            }
        }

        private void UpdateSliceHighlights()
        {
            for (int i = 0; i < StickerCount; i++)
            {
                bool active = i == _hoveredSlice;
                Color acc = NativeStickerCatalog.GetAccentColor((StickerId)i);
                if (_sliceBgs[i] != null)
                {
                    _sliceBgs[i].color = active
                        ? new Color(acc.r * 0.42f + 0.12f, acc.g * 0.42f + 0.10f, acc.b * 0.42f + 0.08f, 0.98f)
                        : NativeStyleProvider.PanelSubBg;
                }
                if (_sliceRects[i] != null)
                    _sliceRects[i].localScale = active ? new Vector3(1.12f, 1.12f, 1f) : Vector3.one;
                if (_sliceLabels[i] != null)
                    _sliceLabels[i].color = active ? NativeStyleProvider.EmberGold : NativeStyleProvider.TextLight;
            }
            if (_centerPreviewLabel != null)
            {
                _centerPreviewLabel.text = _hoveredSlice >= 0
                    ? CoopLoc.T(StickerLocKeys[_hoveredSlice])
                    : CoopLoc.T("wheel.tab_stickers");
            }
        }

        private void RefreshLocalizedWheelLabels()
        {
            if (_wheelRoot == null) return;
            if (_tabStickerTxt != null) _tabStickerTxt.text = CoopLoc.T("wheel.tab_stickers");
            if (_tabCalloutTxt != null) _tabCalloutTxt.text = CoopLoc.T("wheel.tab_callouts");
            if (_centerHintLabel != null) _centerHintLabel.text = CoopLoc.T("wheel.scroll_hint");
            for (int i = 0; i < StickerCount; i++)
                if (_sliceLabels[i] != null) _sliceLabels[i].text = CoopLoc.T(StickerLocKeys[i]);
            for (int i = 0; i < CalloutCount; i++)
                if (_calloutLabels[i] != null) _calloutLabels[i].text = $"[{i + 1}] {CoopLoc.T(CalloutLocKeys[i])}";
        }

        private static bool IsTypingInInputField()
        {
            var es = UnityEngine.EventSystems.EventSystem.current;
            if (es == null || es.currentSelectedGameObject == null) return false;
            return es.currentSelectedGameObject.GetComponent<TMP_InputField>() != null
                || es.currentSelectedGameObject.GetComponent<InputField>() != null;
        }

        private static bool TryRaycastGround(out Vector3 worldPos)
        {
            worldPos = Vector3.zero;
            var cam = Camera.main;
            if (cam == null) return false;
            Ray ray = cam.ScreenPointToRay(Input.mousePosition);
            Plane ground = new Plane(Vector3.up, Vector3.zero);
            if (!ground.Raycast(ray, out float dist)) return false;
            worldPos = ray.GetPoint(dist);
            return true;
        }

        public void ClearAll()
        {
            CloseWheel(absorbRelease: false);
            for (int i = 0; i < _popups.Count; i++)
                if (_popups[i].Root != null) UnityEngine.Object.Destroy(_popups[i].Root);
            _popups.Clear();
            _rateBuckets.Clear();
        }
    }
}
