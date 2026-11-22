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

        public Func<int, Vector2?> ResolvePlayerHudAnchor;

        private const int StickerCount = 10;
        private const int CalloutCount = 6;
        private const float RateWindowSeconds = 3.0f;
        private const int MaxPerWindow = 2;

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
        private Vector3 _wheelAnchorWorld;
        private bool _hasAnchorWorld;
        private int _hoveredSlice = -1;

        private GameObject _wheelRoot;
        private RectTransform _wheelCenterRect;
        private GameObject _stickerRingGroup;
        private GameObject _calloutListGroup;
        private Image _tabStickerBg;
        private Image _tabCalloutBg;
        private TMP_Text _tabStickerTxt;
        private TMP_Text _tabCalloutTxt;
        private TMP_Text _centerPreviewLabel;
        private readonly Image[] _sliceBgs = new Image[StickerCount];
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
            _hasAnchorWorld = TryRaycastGround(out _wheelAnchorWorld);
            CoopInputGuard.IsEmoteWheelOpen = true;

            _wheelRoot.SetActive(true);
            _wheelCenterRect.position = new Vector3(Screen.width * 0.5f, Screen.height * 0.5f, 0f);
            ApplyTabVisibility();
            RefreshLocalizedWheelLabels();
        }

        public void CloseWheel()
        {
            _wheelOpen = false;
            _openedViaHotkeyHold = false;
            _hoveredSlice = -1;
            CoopInputGuard.IsEmoteWheelOpen = false;
            if (_wheelRoot != null && _wheelRoot.activeSelf)
                _wheelRoot.SetActive(false);
        }

        public void ToggleWheel()
        {
            if (_wheelOpen) CloseWheel();
            else OpenWheel(false);
        }

        public void Tick(float now, float dt)
        {
            if (!_net.IsConnected || GameRefs.IsLoading)
            {
                if (_wheelOpen) CloseWheel();
                TickPopups(dt);
                return;
            }

            HandleWheelInput(now);
            TickPopups(dt);
        }

        private void HandleWheelInput(float now)
        {
            if (CoopInputGuard.IsCoopModalOpen || IsTypingInInputField())
            {
                if (_wheelOpen) CloseWheel();
                return;
            }

            KeyCode key = CoopConfig.EmoteWheelKey.Value;
            if (Input.GetKeyDown(key))
            {
                if (_wheelOpen)
                {
                    CloseWheel();
                }
                else
                {
                    EnsureWheelBuilt();
                    _wheelOpen = true;
                    _openedViaHotkeyHold = true;
                    _hotkeyPressTime = now;
                    _calloutTabActive = false;
                    _hoveredSlice = -1;
                    _hasAnchorWorld = TryRaycastGround(out _wheelAnchorWorld);
                    CoopInputGuard.IsEmoteWheelOpen = true;

                    _wheelRoot.SetActive(true);
                    Vector3 mPos = Input.mousePosition;
                    float cx = Mathf.Clamp(mPos.x, 220f, Screen.width - 220f);
                    float cy = Mathf.Clamp(mPos.y, 220f, Screen.height - 220f);
                    _wheelCenterRect.position = new Vector3(cx, cy, 0f);
                    ApplyTabVisibility();
                    RefreshLocalizedWheelLabels();
                }
            }

            if (!_wheelOpen) return;

            if (Input.GetKeyDown(KeyCode.Escape) || Input.GetMouseButtonDown(1))
            {
                CloseWheel();
                return;
            }

            if (Input.GetKeyDown(KeyCode.Tab))
            {
                _calloutTabActive = !_calloutTabActive;
                ApplyTabVisibility();
            }

            for (int i = 0; i < CalloutCount; i++)
            {
                if (Input.GetKeyDown(KeyCode.Alpha1 + i) || Input.GetKeyDown(KeyCode.Keypad1 + i))
                {
                    FireLocalCallout((CalloutId)i);
                    CloseWheel();
                    return;
                }
            }

            if (!_calloutTabActive)
            {
                Vector2 delta = (Vector2)(Input.mousePosition - _wheelCenterRect.position);
                if (delta.sqrMagnitude >= 1225f) // 35px deadzone
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

                UpdateSliceHighlights();
            }

            // Hold >= 0.20s fires on release; short tap leaves the wheel open for clicking.
            if (_openedViaHotkeyHold && Input.GetKeyUp(key))
            {
                _openedViaHotkeyHold = false;
                if (now - _hotkeyPressTime >= 0.20f)
                {
                    if (!_calloutTabActive && _hoveredSlice >= 0)
                        FireLocalSticker((StickerId)_hoveredSlice);
                    CloseWheel();
                }
            }
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
            string label = CoopLoc.T(StickerLocKeys[(int)id]);
            Sprite icon = StickerArtGenerator.GetSprite(id);
            float scale = Mathf.Clamp(CoopConfig.StickerScale.Value, 0.6f, 1.6f);

            if (hasWorld && Camera.main != null)
            {
                var pop = BuildStickerPopup(canvas.transform, icon, label, senderName, playerCol, compact: false);
                pop.IsWorldAnchored = true;
                pop.WorldPos = worldPos;
                pop.SenderPlayerId = playerId;
                pop.Duration = 2.6f;
                pop.BaseScale = scale;
                _popups.Add(pop);
            }

            var hudPop = BuildStickerPopup(canvas.transform, icon, label, senderName, playerCol, compact: true);
            hudPop.IsWorldAnchored = false;
            hudPop.SenderPlayerId = playerId;
            hudPop.Duration = 2.6f;
            hudPop.BaseScale = Mathf.Lerp(1f, scale, 0.5f);
            _popups.Add(hudPop);

            PlayEmoteCue(StickerPitches[(int)id]);
        }

        private void RenderCalloutDualAnchor(int playerId, CalloutId id, bool hasWorld, Vector3 worldPos)
        {
            var canvas = NativeStyleProvider.GetOrCreateOverlayCanvas();
            if (canvas == null) return;

            Color playerCol = CoopConfig.GetPlayerColor(_net.GetPlayerColorIndex(playerId));
            string senderName = _net.GetPlayerName(playerId);
            string phrase = CoopLoc.T(CalloutLocKeys[(int)id]);
            float scale = Mathf.Clamp(CoopConfig.StickerScale.Value, 0.6f, 1.6f);

            CoopLogger.PushFeed($"{senderName}: \"{phrase}\"");

            var hudBubble = BuildSpeechBubble(canvas.transform, senderName, phrase, playerCol);
            hudBubble.IsWorldAnchored = false;
            hudBubble.SenderPlayerId = playerId;
            hudBubble.Duration = 3.4f;
            hudBubble.BaseScale = Mathf.Lerp(1f, scale, 0.4f);
            _popups.Add(hudBubble);

            if (hasWorld && Camera.main != null)
            {
                var worldBubble = BuildSpeechBubble(canvas.transform, senderName, phrase, playerCol);
                worldBubble.IsWorldAnchored = true;
                worldBubble.WorldPos = worldPos;
                worldBubble.SenderPlayerId = playerId;
                worldBubble.Duration = 3.0f;
                worldBubble.BaseScale = scale * 0.92f;
                _popups.Add(worldBubble);
            }

            PlayEmoteCue(1.06f + (int)id * 0.04f);
        }

        private static ActivePopup BuildStickerPopup(Transform parent, Sprite icon, string caption, string senderName, Color borderCol, bool compact)
        {
            var go = new GameObject(compact ? "EWCoop_HudSticker" : "EWCoop_WorldSticker");
            go.transform.SetParent(parent, false);
            var rt = go.AddComponent<RectTransform>();
            rt.sizeDelta = compact ? new Vector2(150f, 52f) : new Vector2(118f, 128f);

            var cg = go.AddComponent<CanvasGroup>();
            cg.blocksRaycasts = false;
            cg.interactable = false;

            var bg = go.AddComponent<Image>();
            bg.sprite = NativeStyleProvider.GetWhiteSprite();
            bg.color = new Color(0.09f, 0.08f, 0.13f, 0.90f);
            bg.raycastTarget = false;

            var outline = go.AddComponent<Outline>();
            outline.effectColor = borderCol;
            outline.effectDistance = new Vector2(2f, -2f);

            if (compact)
            {
                var hlg = go.AddComponent<HorizontalLayoutGroup>();
                hlg.padding = new RectOffset(6, 8, 5, 5);
                hlg.spacing = 6f;
                hlg.childAlignment = TextAnchor.MiddleLeft;
                hlg.childControlWidth = true;
                hlg.childControlHeight = true;
                hlg.childForceExpandWidth = false;

                var iconGo = new GameObject("Icon");
                iconGo.transform.SetParent(go.transform, false);
                var ile = iconGo.AddComponent<LayoutElement>();
                ile.minWidth = 40f; ile.preferredWidth = 40f; ile.minHeight = 40f; ile.preferredHeight = 40f;
                var iImg = iconGo.AddComponent<Image>();
                iImg.sprite = icon;
                iImg.preserveAspect = true;
                iImg.raycastTarget = false;

                var lbl = NativeStyleProvider.CreateLabel(go.transform, caption, 13f, borderCol, TextAlignmentOptions.MidlineLeft, FontStyles.Bold);
                lbl.gameObject.AddComponent<LayoutElement>().flexibleWidth = 1f;
            }
            else
            {
                var vlg = go.AddComponent<VerticalLayoutGroup>();
                vlg.padding = new RectOffset(8, 8, 6, 6);
                vlg.spacing = 2f;
                vlg.childAlignment = TextAnchor.MiddleCenter;
                vlg.childControlWidth = true;
                vlg.childControlHeight = true;
                vlg.childForceExpandWidth = true;
                vlg.childForceExpandHeight = false;

                var iconGo = new GameObject("Icon");
                iconGo.transform.SetParent(go.transform, false);
                var ile = iconGo.AddComponent<LayoutElement>();
                ile.minHeight = 76f; ile.preferredHeight = 76f;
                var iImg = iconGo.AddComponent<Image>();
                iImg.sprite = icon;
                iImg.preserveAspect = true;
                iImg.raycastTarget = false;

                var capLbl = NativeStyleProvider.CreateLabel(go.transform, caption, 14f, Color.white, TextAlignmentOptions.Center, FontStyles.Bold);
                capLbl.gameObject.AddComponent<LayoutElement>().minHeight = 18f;

                var nameLbl = NativeStyleProvider.CreateLabel(go.transform, senderName, 11f, borderCol, TextAlignmentOptions.Center, FontStyles.Normal);
                nameLbl.gameObject.AddComponent<LayoutElement>().minHeight = 15f;
            }

            return new ActivePopup { Root = go, Rect = rt, Group = cg };
        }

        private static ActivePopup BuildSpeechBubble(Transform parent, string senderName, string phrase, Color borderCol)
        {
            var go = new GameObject("EWCoop_SpeechBubble");
            go.transform.SetParent(parent, false);
            var rt = go.AddComponent<RectTransform>();
            rt.sizeDelta = new Vector2(250f, 54f);

            var cg = go.AddComponent<CanvasGroup>();
            cg.blocksRaycasts = false;
            cg.interactable = false;

            var bg = go.AddComponent<Image>();
            bg.sprite = NativeStyleProvider.GetWhiteSprite();
            bg.color = new Color(0.15f, 0.12f, 0.10f, 0.94f);
            bg.raycastTarget = false;

            var outline = go.AddComponent<Outline>();
            outline.effectColor = borderCol;
            outline.effectDistance = new Vector2(2f, -2f);

            var vlg = go.AddComponent<VerticalLayoutGroup>();
            vlg.padding = new RectOffset(10, 10, 5, 5);
            vlg.spacing = 2f;
            vlg.childControlWidth = true;
            vlg.childControlHeight = true;
            vlg.childForceExpandWidth = true;
            vlg.childForceExpandHeight = false;

            NativeStyleProvider.CreateLabel(go.transform, senderName, 11f, borderCol, TextAlignmentOptions.MidlineLeft, FontStyles.Bold);
            var body = NativeStyleProvider.CreateLabel(go.transform, $"\"{phrase}\"", 13f, NativeStyleProvider.TextLight, TextAlignmentOptions.MidlineLeft, FontStyles.Normal);
            body.enableWordWrapping = true;

            return new ActivePopup { Root = go, Rect = rt, Group = cg };
        }

        private void TickPopups(float dt)
        {
            var cam = Camera.main;
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

                float elastic = p.Age < 0.22f
                    ? ElasticOut(p.Age / 0.22f)
                    : 1f;
                p.Rect.localScale = Vector3.one * (p.BaseScale * elastic);

                p.Group.alpha = t > 0.72f ? Mathf.Clamp01(1f - (t - 0.72f) / 0.28f) : 1f;

                if (p.IsWorldAnchored)
                {
                    if (cam == null) { p.Root.SetActive(false); continue; }
                    Vector3 sp = cam.WorldToScreenPoint(p.WorldPos + new Vector3(0f, 1.35f + t * 0.85f, 0f));
                    bool visible = sp.z > 0f && sp.x > -120f && sp.x < Screen.width + 120f && sp.y > -120f && sp.y < Screen.height + 120f;
                    if (p.Root.activeSelf != visible) p.Root.SetActive(visible);
                    if (visible) p.Rect.position = new Vector3(sp.x, sp.y, 0f);
                }
                else
                {
                    Vector2? anchor = ResolvePlayerHudAnchor?.Invoke(p.SenderPlayerId);
                    Vector2 target = anchor ?? new Vector2(Screen.width - 420f, Screen.height - 95f - (p.SenderPlayerId * 58f));
                    p.Rect.position = new Vector3(target.x, target.y + t * 14f, 0f);
                }
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
            var canvas = NativeStyleProvider.GetOrCreateOverlayCanvas();

            _wheelRoot = new GameObject("EWCoop_EmoteWheel");
            _wheelRoot.transform.SetParent(canvas.transform, false);
            var fullRt = _wheelRoot.AddComponent<RectTransform>();
            NativeStyleProvider.StretchFill(fullRt);

            var backdropImg = _wheelRoot.AddComponent<Image>();
            backdropImg.color = new Color(0.02f, 0.02f, 0.05f, 0.28f);
            var backdropBtn = _wheelRoot.AddComponent<Button>();
            backdropBtn.onClick.AddListener((UnityEngine.Events.UnityAction)CloseWheel);

            var centerGo = new GameObject("WheelCenter");
            centerGo.transform.SetParent(_wheelRoot.transform, false);
            _wheelCenterRect = centerGo.AddComponent<RectTransform>();
            _wheelCenterRect.sizeDelta = new Vector2(400f, 400f);

            var tabsBar = NativeStyleProvider.CreatePanel(centerGo.transform, "TabsBar", NativeStyleProvider.PanelBg, addBorder: true);
            var tabsRt = tabsBar.GetComponent<RectTransform>();
            tabsRt.anchoredPosition = new Vector2(0f, 206f);
            tabsRt.sizeDelta = new Vector2(310f, 36f);
            var hlg = tabsBar.AddComponent<HorizontalLayoutGroup>();
            hlg.padding = new RectOffset(4, 4, 4, 4);
            hlg.spacing = 6f;
            hlg.childControlWidth = true;
            hlg.childControlHeight = true;
            hlg.childForceExpandWidth = true;

            var sTabBtn = NativeStyleProvider.CreateButton(tabsBar.transform, CoopLoc.T("wheel.tab_stickers"), NativeStyleProvider.ButtonPrimary, () =>
            {
                _calloutTabActive = false;
                ApplyTabVisibility();
            }, 12f, 28f);
            _tabStickerBg = sTabBtn.GetComponent<Image>();
            _tabStickerTxt = sTabBtn.GetComponentInChildren<TMP_Text>();

            var cTabBtn = NativeStyleProvider.CreateButton(tabsBar.transform, CoopLoc.T("wheel.tab_callouts"), NativeStyleProvider.ButtonSecondary, () =>
            {
                _calloutTabActive = true;
                ApplyTabVisibility();
            }, 12f, 28f);
            _tabCalloutBg = cTabBtn.GetComponent<Image>();
            _tabCalloutTxt = cTabBtn.GetComponentInChildren<TMP_Text>();

            _stickerRingGroup = new GameObject("StickerRing");
            _stickerRingGroup.transform.SetParent(centerGo.transform, false);
            NativeStyleProvider.StretchFill(_stickerRingGroup.AddComponent<RectTransform>());

            var hub = NativeStyleProvider.CreatePanel(_stickerRingGroup.transform, "Hub", NativeStyleProvider.PanelBg, addBorder: true);
            hub.GetComponent<RectTransform>().sizeDelta = new Vector2(124f, 48f);
            _centerPreviewLabel = NativeStyleProvider.CreateLabel(hub.transform, CoopLoc.T("wheel.tab_stickers"), 13f, NativeStyleProvider.EmberGold, TextAlignmentOptions.Center, FontStyles.Bold);
            NativeStyleProvider.StretchFill(_centerPreviewLabel.rectTransform, 6f, 4f);

            const float radius = 148f;
            for (int i = 0; i < StickerCount; i++)
            {
                var sid = (StickerId)i;
                float rad = (i * 36f) * Mathf.Deg2Rad;
                Vector2 pos = new Vector2(Mathf.Sin(rad) * radius, Mathf.Cos(rad) * radius);

                var slot = NativeStyleProvider.CreatePanel(_stickerRingGroup.transform, $"Sticker_{sid}", NativeStyleProvider.PanelSubBg, addBorder: true);
                var srt = slot.GetComponent<RectTransform>();
                srt.anchoredPosition = pos;
                srt.sizeDelta = new Vector2(80f, 80f);

                _sliceBgs[i] = slot.GetComponent<Image>();
                var outl = slot.GetComponent<Outline>();
                if (outl != null) outl.effectColor = StickerArtGenerator.GetAccentColor(sid);

                var btn = slot.AddComponent<Button>();
                btn.onClick.AddListener((UnityEngine.Events.UnityAction)(() =>
                {
                    FireLocalSticker(sid);
                    CloseWheel();
                }));

                var vlg = slot.AddComponent<VerticalLayoutGroup>();
                vlg.padding = new RectOffset(5, 5, 4, 4);
                vlg.spacing = 2f;
                vlg.childAlignment = TextAnchor.MiddleCenter;
                vlg.childControlWidth = true;
                vlg.childControlHeight = true;
                vlg.childForceExpandWidth = true;
                vlg.childForceExpandHeight = false;

                var iconGo = new GameObject("Icon");
                iconGo.transform.SetParent(slot.transform, false);
                var ile = iconGo.AddComponent<LayoutElement>();
                ile.minHeight = 50f; ile.preferredHeight = 50f;
                var img = iconGo.AddComponent<Image>();
                img.sprite = StickerArtGenerator.GetSprite(sid);
                img.preserveAspect = true;
                img.raycastTarget = false;

                var lbl = NativeStyleProvider.CreateLabel(slot.transform, CoopLoc.T(StickerLocKeys[i]), 10.5f, NativeStyleProvider.TextLight, TextAlignmentOptions.Center, FontStyles.Bold);
                lbl.gameObject.AddComponent<LayoutElement>().minHeight = 16f;
                _sliceLabels[i] = lbl;
            }

            _calloutListGroup = NativeStyleProvider.CreatePanel(centerGo.transform, "CalloutPanel", NativeStyleProvider.PanelBg, addBorder: true);
            var crt = _calloutListGroup.GetComponent<RectTransform>();
            crt.sizeDelta = new Vector2(340f, 276f);
            var cv = _calloutListGroup.AddComponent<VerticalLayoutGroup>();
            cv.padding = new RectOffset(12, 12, 10, 10);
            cv.spacing = 6f;
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
                    CloseWheel();
                }, 13.5f, 38f);
                var txt = btn.GetComponentInChildren<TMP_Text>();
                if (txt != null) txt.alignment = TextAlignmentOptions.MidlineLeft;
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

        private void UpdateSliceHighlights()
        {
            for (int i = 0; i < StickerCount; i++)
            {
                bool active = i == _hoveredSlice;
                Color acc = StickerArtGenerator.GetAccentColor((StickerId)i);
                if (_sliceBgs[i] != null)
                {
                    _sliceBgs[i].color = active
                        ? new Color(acc.r * 0.45f, acc.g * 0.45f, acc.b * 0.45f, 0.98f)
                        : NativeStyleProvider.PanelSubBg;
                }
                if (_sliceLabels[i] != null)
                    _sliceLabels[i].color = active ? Color.white : NativeStyleProvider.TextLight;
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
            CloseWheel();
            for (int i = 0; i < _popups.Count; i++)
                if (_popups[i].Root != null) UnityEngine.Object.Destroy(_popups[i].Root);
            _popups.Clear();
            _rateBuckets.Clear();
        }
    }
}
