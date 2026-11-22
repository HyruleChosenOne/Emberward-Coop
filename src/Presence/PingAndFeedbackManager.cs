using System;
using System.Collections.Generic;
using System.IO;
using EmberwardCoop.Core;
using EmberwardCoop.Localization;
using EmberwardCoop.Networking;
using EmberwardCoop.Sync;
using EmberwardCoop.UI;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace EmberwardCoop.Presence
{
    public enum PingKind : byte
    {
        BuildMazeHere = 0,
        PlaceDpsHere = 1,
        NeedGold = 2,
        UpgradeOrFocus = 3,
        WatchLane = 4,
        NiceTower = 5,
        WatchPortal = 6
    }

    public sealed class PingAndFeedbackManager
    {
        private readonly NetworkManager _net;

        private const float WheelRadius = 96f;

        private sealed class ActiveEffect
        {
            public GameObject RingObj;
            public SpriteRenderer RingRenderer;
            public GameObject BeaconObj;
            public SpriteRenderer BeaconRenderer;
            public GameObject LabelObj;
            public RectTransform LabelRect;
            public CanvasGroup LabelGroup;
            public TMP_Text LabelText;
            public Color BaseColor;
            public Vector3 WorldPos;
            public string BaseLabel;
            public float Age;
            public float Duration;
            public bool IsPing;
        }

        private readonly List<ActiveEffect> _effects = new List<ActiveEffect>();
        private static Material _sharedParticleMat;
        private float _lastLocalPingTime;

        private bool _holdingPingKey;
        private float _pingHoldStartTime;
        private Vector2 _pingAnchorCanvasPos;
        private Vector3 _pingAnchorWorldPos;
        private bool _wheelVisible;
        private int _highlightedSlice = 0;
        private float _openAnimProgress = 1f;

        private GameObject _wheelOverlayRoot;
        private RectTransform _wheelCenterRect;
        private CanvasGroup _wheelCenterGroup;
        private TMP_Text _centerLabel;
        private readonly RectTransform[] _sliceRects = new RectTransform[5];
        private readonly Image[] _sliceBgs = new Image[5];
        private readonly Image[] _sliceIcons = new Image[5];
        private readonly TMP_Text[] _sliceLabels = new TMP_Text[5];

        private static readonly (PingKind kind, string locKey, Color accent)[] WheelSlices =
        {
            (PingKind.BuildMazeHere,  "ping.build_maze",    new Color(0.95f, 0.77f, 0.32f, 1f)),
            (PingKind.PlaceDpsHere,   "ping.place_dps",     new Color(0.92f, 0.48f, 0.22f, 1f)),
            (PingKind.NeedGold,       "ping.need_gold",     new Color(0.98f, 0.84f, 0.34f, 1f)),
            (PingKind.UpgradeOrFocus, "ping.upgrade_focus", new Color(0.36f, 0.82f, 0.96f, 1f)),
            (PingKind.WatchLane,      "ping.watch_lane",    new Color(0.94f, 0.34f, 0.32f, 1f))
        };

        public PingAndFeedbackManager(NetworkManager net)
        {
            _net = net;
            CoopLoc.OnLanguageChanged += RefreshLocalizedWheelLabels;
        }

        private static string GetPingLocalizedLabel(PingKind kind) => kind switch
        {
            PingKind.BuildMazeHere  => CoopLoc.T("ping.build_maze"),
            PingKind.PlaceDpsHere   => CoopLoc.T("ping.place_dps"),
            PingKind.NeedGold       => CoopLoc.T("ping.need_gold"),
            PingKind.UpgradeOrFocus => CoopLoc.T("ping.upgrade_focus"),
            PingKind.WatchLane      => CoopLoc.T("ping.watch_lane"),
            PingKind.NiceTower      => CoopLoc.T("ping.nice_tower"),
            PingKind.WatchPortal    => CoopLoc.T("ping.watch_portal"),
            _                       => CoopLoc.T("ping.build_maze")
        };

        private static Color GetPingAccentColor(PingKind kind) => kind switch
        {
            PingKind.BuildMazeHere  => new Color(0.95f, 0.77f, 0.32f, 1f),
            PingKind.PlaceDpsHere   => new Color(0.92f, 0.48f, 0.22f, 1f),
            PingKind.NeedGold       => new Color(0.98f, 0.84f, 0.34f, 1f),
            PingKind.UpgradeOrFocus => new Color(0.36f, 0.82f, 0.96f, 1f),
            PingKind.WatchLane      => new Color(0.94f, 0.34f, 0.32f, 1f),
            PingKind.NiceTower      => new Color(0.42f, 0.88f, 0.54f, 1f),
            PingKind.WatchPortal    => new Color(0.86f, 0.38f, 0.92f, 1f),
            _                       => Color.white
        };

        public void CancelAndCloseWheel(bool absorbRelease = true)
        {
            bool wasActive = _holdingPingKey || _wheelVisible;
            _holdingPingKey = false;
            CoopInputGuard.IsPingKeyHolding = false;
            HideWheel(absorbRelease: false);
            if (wasActive && absorbRelease)
                CoopInputGuard.NotifyWheelClosed(waitForMouseUp: true);
        }

        public void TryHandleLocalPingInput()
        {
            if (!_net.IsConnected || !GameRefs.IsInBattle || GameRefs.IsVanillaPauseMenuOpen()
                || CoopInputGuard.IsEmoteWheelOpen || CoopInputGuard.IsCoopModalOpen || CoopInputGuard.IsMazeSketching)
            {
                if (_holdingPingKey || _wheelVisible)
                    CancelAndCloseWheel(absorbRelease: false);
                return;
            }

            if (Input.GetKeyDown(KeyCode.F1)) { TryQuickHotkeyPing(PingKind.BuildMazeHere); return; }
            if (Input.GetKeyDown(KeyCode.F2)) { TryQuickHotkeyPing(PingKind.PlaceDpsHere); return; }
            if (Input.GetKeyDown(KeyCode.F3)) { TryQuickHotkeyPing(PingKind.NeedGold); return; }
            if (Input.GetKeyDown(KeyCode.F4)) { TryQuickHotkeyPing(PingKind.UpgradeOrFocus); return; }
            if (Input.GetKeyDown(KeyCode.F5)) { TryQuickHotkeyPing(PingKind.WatchLane); return; }

            KeyCode pingKey = CoopConfig.PingHotkey.Value;
            bool pressed = Input.GetKeyDown(pingKey);
            bool held = Input.GetKey(pingKey);

            if (pressed && !_holdingPingKey)
            {
                if (TryRaycastGround(out Vector3 hitPos))
                {
                    _holdingPingKey = true;
                    // Immediately lock camera pan/drag from frame 0 so Mouse2 never drags the camera before the wheel pops
                    CoopInputGuard.IsPingKeyHolding = true;
                    _pingHoldStartTime = Time.unscaledTime;
                    _pingAnchorCanvasPos = ClampWheelAnchorToCanvas(NativeStyleProvider.ScreenToCanvasLocal(Input.mousePosition));
                    _pingAnchorWorldPos = hitPos;
                    _highlightedSlice = Math.Clamp((int)InferContextualPing(hitPos), 0, WheelSlices.Length - 1);
                }
            }

            if (_holdingPingKey)
            {
                // Allow Escape or Right-Click (when ping key isn't Right-Click) to cancel cleanly without firing a ping
                if (Input.GetKeyDown(KeyCode.Escape) || (pingKey != KeyCode.Mouse1 && Input.GetMouseButtonDown(1)))
                {
                    NativeStyleProvider.PlayUiOpenSound(false);
                    CancelAndCloseWheel(absorbRelease: true);
                    return;
                }

                float holdDur = Time.unscaledTime - _pingHoldStartTime;
                if (held)
                {
                    if (holdDur >= 0.16f)
                        ShowOrUpdateWheel();
                }
                else
                {
                    PingKind chosen = _wheelVisible
                        ? WheelSlices[Math.Clamp(_highlightedSlice, 0, WheelSlices.Length - 1)].kind
                        : InferContextualPing(_pingAnchorWorldPos);

                    _holdingPingKey = false;
                    CoopInputGuard.IsPingKeyHolding = false;
                    HideWheel(absorbRelease: true);
                    FireLocalPing(_pingAnchorWorldPos, chosen);
                }
            }
        }

        private static Vector2 ClampWheelAnchorToCanvas(Vector2 rawLocal)
        {
            Vector2 canvasSz = NativeStyleProvider.GetCanvasSize();
            float halfW = canvasSz.x * 0.5f;
            float halfH = canvasSz.y * 0.5f;
            const float padX = 186f;
            const float padY = 148f;
            return new Vector2(
                Mathf.Clamp(rawLocal.x, -halfW + padX, halfW - padX),
                Mathf.Clamp(rawLocal.y, -halfH + padY, halfH - padY));
        }

        private void TryQuickHotkeyPing(PingKind kind)
        {
            if (TryRaycastGround(out Vector3 pos))
                FireLocalPing(pos, kind);
        }

        private PingKind InferContextualPing(Vector3 worldPos)
        {
            try
            {
                var gs = GridSystem.Instance;
                if (gs != null)
                {
                    var cell = gs.GetGridCell(worldPos);
                    if (gs.IsHaveMonsterPortalAtPosition(cell))
                        return PingKind.WatchPortal;
                }
            }
            catch { }

            var tower = GameRefs.FindTowerAt(worldPos, 0.85f);
            if (tower != null)
            {
                var s = SessionController.Instance;
                if (s != null)
                {
                    int ownerId = s.Ownership.GetTowerOwner(tower);
                    if (ownerId != _net.LocalPlayerId)
                        return PingKind.NiceTower;
                }
                return PingKind.UpgradeOrFocus;
            }

            if (GameRefs.FindMonsterNear(worldPos, 1.8f) != null) return PingKind.WatchLane;
            if (GameRefs.FindTetrisAt(worldPos, 0.85f) != null) return PingKind.PlaceDpsHere;
            return PingKind.BuildMazeHere;
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

        private void FireLocalPing(Vector3 worldPos, PingKind kind)
        {
            if (Time.unscaledTime - _lastLocalPingTime < 0.35f) return;
            _lastLocalPingTime = Time.unscaledTime;

            SpawnPingVisual(_net.LocalPlayerId, worldPos, kind);
            _net.SendToAllPeersOrHost(PacketType.MapPing, w =>
            {
                w.Write(_net.LocalPlayerId);
                w.WriteVector3(worldPos);
                w.Write((byte)kind);
            });
        }

        private void ShowOrUpdateWheel()
        {
            EnsureWheelBuilt();
            if (!_wheelVisible)
            {
                _wheelVisible = true;
                _openAnimProgress = 0f;
                CoopInputGuard.IsPingWheelOpen = true;
                _wheelOverlayRoot.SetActive(true);
                _wheelCenterRect.anchoredPosition = _pingAnchorCanvasPos;
                RefreshWheelSprites();
                RefreshLocalizedWheelLabels();
                NativeStyleProvider.PlayUiOpenSound(true);
            }

            NativeStyleProvider.TickOpenTransition(_wheelCenterRect, _wheelCenterGroup, ref _openAnimProgress, Time.unscaledDeltaTime, 0.10f);

            Vector2 mouseCanvas = NativeStyleProvider.ScreenToCanvasLocal(Input.mousePosition);
            Vector2 delta = mouseCanvas - _pingAnchorCanvasPos;
            int prev = _highlightedSlice;

            if (delta.sqrMagnitude > 324f) // 18px deadzone
            {
                float angle = Mathf.Atan2(delta.x, delta.y) * Mathf.Rad2Deg;
                if (angle < 0f) angle += 360f;
                int idx = Mathf.FloorToInt(((angle + 36f) % 360f) / 72f);
                _highlightedSlice = Math.Clamp(idx, 0, WheelSlices.Length - 1);
            }

            if (_highlightedSlice != prev)
                NativeStyleProvider.PlayUiHoverSound(1.10f + _highlightedSlice * 0.03f);

            for (int i = 0; i < WheelSlices.Length; i++)
            {
                bool active = i == _highlightedSlice;
                Color accent = WheelSlices[i].accent;
                if (_sliceBgs[i] != null)
                {
                    _sliceBgs[i].color = active
                        ? new Color(accent.r * 0.48f + 0.14f, accent.g * 0.48f + 0.12f, accent.b * 0.48f + 0.10f, 0.98f)
                        : NativeStyleProvider.PanelSubBg;
                }
                if (_sliceRects[i] != null)
                    _sliceRects[i].localScale = active ? new Vector3(1.08f, 1.08f, 1f) : Vector3.one;
                if (_sliceLabels[i] != null)
                    _sliceLabels[i].color = active ? Color.white : accent;
            }
        }

        private void HideWheel(bool absorbRelease = true)
        {
            bool wasVis = _wheelVisible;
            _wheelVisible = false;
            CoopInputGuard.IsPingWheelOpen = false;
            if (wasVis && absorbRelease)
                CoopInputGuard.NotifyWheelClosed(waitForMouseUp: true);
            if (_wheelOverlayRoot != null && _wheelOverlayRoot.activeSelf)
                _wheelOverlayRoot.SetActive(false);
        }

        private void RefreshWheelSprites()
        {
            if (_wheelOverlayRoot == null) return;
            NativeAssetRegistry.EnsureScanned();
            for (int i = 0; i < WheelSlices.Length; i++)
            {
                if (_sliceIcons[i] != null)
                {
                    _sliceIcons[i].sprite = NativeAssetRegistry.GetPingIconSprite(WheelSlices[i].kind);
                    _sliceIcons[i].preserveAspect = true;
                }
            }
        }

        private void RefreshLocalizedWheelLabels()
        {
            if (_wheelOverlayRoot == null) return;
            if (_centerLabel != null) _centerLabel.text = CoopLoc.T("ping.center_label");
            for (int i = 0; i < WheelSlices.Length; i++)
                if (_sliceLabels[i] != null)
                    _sliceLabels[i].text = CoopLoc.T(WheelSlices[i].locKey);
        }

        private void EnsureWheelBuilt()
        {
            if (_wheelOverlayRoot != null) return;
            NativeStyleProvider.HarvestFromScene();
            var canvas = NativeStyleProvider.GetOrCreateOverlayCanvas();

            // Full-screen raycast-blocking overlay consumes hover & click events so world objects never react beneath
            _wheelOverlayRoot = new GameObject("EWCoop_PingWheelOverlay");
            _wheelOverlayRoot.transform.SetParent(canvas.transform, false);
            NativeStyleProvider.StretchFill(_wheelOverlayRoot.AddComponent<RectTransform>());
            var blocker = _wheelOverlayRoot.AddComponent<Image>();
            blocker.color = new Color(0.11f, 0.08f, 0.06f, 0.16f);
            blocker.raycastTarget = true;

            var centerHolder = new GameObject("PingWheelCenter");
            centerHolder.transform.SetParent(_wheelOverlayRoot.transform, false);
            _wheelCenterRect = centerHolder.AddComponent<RectTransform>();
            _wheelCenterRect.anchorMin = new Vector2(0.5f, 0.5f);
            _wheelCenterRect.anchorMax = new Vector2(0.5f, 0.5f);
            _wheelCenterRect.sizeDelta = new Vector2(240f, 240f);
            _wheelCenterGroup = centerHolder.AddComponent<CanvasGroup>();
            _wheelCenterGroup.blocksRaycasts = false;

            var center = NativeStyleProvider.CreatePanel(centerHolder.transform, "CenterHub", NativeStyleProvider.PanelSubBg, addBorder: false, isSubPanel: true);
            center.GetComponent<RectTransform>().sizeDelta = new Vector2(76f, 28f);
            _centerLabel = NativeStyleProvider.CreateLabel(center.transform, CoopLoc.T("ping.center_label"), 11.5f, NativeStyleProvider.EmberGold, TextAlignmentOptions.Center, FontStyles.Bold, autoSize: true, minFontSize: 9f, addShadow: true);
            NativeStyleProvider.StretchFill(_centerLabel.rectTransform, 6f, 2f);

            for (int i = 0; i < WheelSlices.Length; i++)
            {
                float rad = (i * 72f) * Mathf.Deg2Rad;
                Vector2 pos = new Vector2(Mathf.Sin(rad) * WheelRadius, Mathf.Cos(rad) * WheelRadius);

                var pill = NativeStyleProvider.CreatePanel(centerHolder.transform, $"Slice_{i}", NativeStyleProvider.PanelSubBg, addBorder: false, isSubPanel: true);
                var prt = pill.GetComponent<RectTransform>();
                prt.anchoredPosition = pos;
                prt.sizeDelta = new Vector2(162f, 34f);
                _sliceRects[i] = prt;

                _sliceBgs[i] = pill.GetComponent<Image>();

                var hlg = pill.AddComponent<HorizontalLayoutGroup>();
                hlg.padding = new RectOffset(7, 8, 4, 4);
                hlg.spacing = 6f;
                hlg.childAlignment = TextAnchor.MiddleLeft;
                hlg.childControlWidth = true;
                hlg.childControlHeight = true;
                hlg.childForceExpandWidth = false;

                var icGo = new GameObject("PingIcon");
                icGo.transform.SetParent(pill.transform, false);
                var icLe = icGo.AddComponent<LayoutElement>();
                icLe.minWidth = 20f; icLe.preferredWidth = 20f; icLe.minHeight = 20f; icLe.preferredHeight = 20f;
                var icImg = icGo.AddComponent<Image>();
                icImg.sprite = NativeAssetRegistry.GetPingIconSprite(WheelSlices[i].kind);
                icImg.preserveAspect = true;
                icImg.raycastTarget = false;
                _sliceIcons[i] = icImg;

                var lbl = NativeStyleProvider.CreateLabel(pill.transform, CoopLoc.T(WheelSlices[i].locKey), 11.5f, WheelSlices[i].accent, TextAlignmentOptions.MidlineLeft, FontStyles.Bold, autoSize: true, minFontSize: 8.5f, addShadow: true);
                lbl.gameObject.AddComponent<LayoutElement>().flexibleWidth = 1f;
                _sliceLabels[i] = lbl;
            }
            _wheelOverlayRoot.SetActive(false);
        }

        public void HandlePingPacket(int senderId, BinaryReader r)
        {
            int actorId = r.ReadInt32();
            if (_net.IsHost) actorId = senderId;
            Vector3 pos = r.ReadVector3();
            var kind = (PingKind)r.ReadByte();

            if (actorId != _net.LocalPlayerId && !CoopConfig.MuteTeammateStickers.Value)
                SpawnPingVisual(actorId, pos, kind);

            if (_net.IsHost)
            {
                _net.BroadcastToClients(PacketType.MapPing, w =>
                {
                    w.Write(actorId);
                    w.WriteVector3(pos);
                    w.Write((byte)kind);
                }, DeliveryMode.ReliableOrdered, excludePeerId: senderId);
            }
        }

        public void SpawnPingVisual(int playerId, Vector3 pos, PingKind kind)
        {
            if (!CoopConfig.EnableSpatialFeedback.Value) return;
            Color playerCol = CoopConfig.GetPlayerColor(_net.GetPlayerColorIndex(playerId));
            Color accent = GetPingAccentColor(kind);
            Color col = (kind == PingKind.WatchLane || kind == PingKind.WatchPortal)
                ? accent
                : Color.Lerp(playerCol, accent, 0.35f);

            string callout = $"{_net.GetPlayerName(playerId)}: {GetPingLocalizedLabel(kind)}";
            CreateEffect(pos, col, callout, duration: 3.2f, isPing: true, pingKind: kind, actorPlayerId: playerId);
            PlayCueSound(isPing: true, isUpgrade: kind == PingKind.NeedGold || kind == PingKind.NiceTower);
        }

        public void SpawnActionFeedback(int playerId, Vector3 pos, string actionTag, bool isUpgrade)
        {
            if (!CoopConfig.EnableSpatialFeedback.Value) return;
            Color col = CoopConfig.GetPlayerColor(_net.GetPlayerColorIndex(playerId));
            string label = $"{_net.GetPlayerName(playerId)} · {actionTag}";
            CreateEffect(pos, col, label, duration: 1.55f, isPing: false, pingKind: null, actorPlayerId: playerId);
            PlayCueSound(isPing: false, isUpgrade: isUpgrade);
        }

        private void CreateEffect(Vector3 pos, Color color, string labelText, float duration, bool isPing, PingKind? pingKind, int actorPlayerId)
        {
            try
            {
                NativeAssetRegistry.EnsureScanned();

                var ring = new GameObject("EWCoop_PulseRing");
                ring.layer = 2;
                ring.transform.position = new Vector3(pos.x, pos.y + 0.08f, pos.z);
                ring.transform.rotation = Quaternion.Euler(90f, 0f, 0f);
                var ringSr = ring.AddComponent<SpriteRenderer>();
                ringSr.sprite = NativeAssetRegistry.GetPingRingSprite();
                ringSr.color = new Color(color.r, color.g, color.b, 0.88f);
                ringSr.sortingOrder = 25;
                float ringNorm = NormalizeSpriteUnitScale(ringSr.sprite, 0.55f);
                ring.transform.localScale = Vector3.one * ringNorm;

                GameObject beacon = null;
                SpriteRenderer beaconSr = null;
                if (isPing)
                {
                    beacon = new GameObject("EWCoop_PingBeacon");
                    beacon.layer = 2;
                    beacon.transform.position = new Vector3(pos.x, pos.y + 1.55f, pos.z);
                    beaconSr = beacon.AddComponent<SpriteRenderer>();
                    beaconSr.sprite = NativeAssetRegistry.GetPingBeaconSprite();
                    beaconSr.color = new Color(color.r, color.g, color.b, 0.72f);
                    beaconSr.sortingOrder = 26;
                    float bNorm = NormalizeSpriteUnitScale(beaconSr.sprite, 1.0f);
                    beacon.transform.localScale = new Vector3(bNorm * 0.42f, bNorm * 2.8f, 1f);
                }

                SpawnBurstParticles(pos, color, isPing ? 16 : 10);

                GameObject lblObj = null;
                RectTransform lblRect = null;
                CanvasGroup lblGroup = null;
                TMP_Text tmp = null;
                var canvasRoot = NativeStyleProvider.GetOrCreateOverlayCanvas();
                if (canvasRoot != null)
                {
                    lblObj = NativeStyleProvider.CreatePanel(canvasRoot.transform, "EWCoop_FloatingTag", NativeStyleProvider.TooltipBg, addBorder: true, isSubPanel: true);
                    lblRect = lblObj.GetComponent<RectTransform>();
                    lblRect.anchorMin = new Vector2(0.5f, 0.5f);
                    lblRect.anchorMax = new Vector2(0.5f, 0.5f);
                    lblGroup = lblObj.AddComponent<CanvasGroup>();
                    lblGroup.blocksRaycasts = false;
                    lblGroup.interactable = false;

                    var hlg = lblObj.AddComponent<HorizontalLayoutGroup>();
                    hlg.padding = new RectOffset(11, 12, 7, 7);
                    hlg.spacing = 5f;
                    hlg.childAlignment = TextAnchor.MiddleLeft;
                    hlg.childControlWidth = true;
                    hlg.childControlHeight = true;
                    hlg.childForceExpandWidth = false;
                    var fit = lblObj.AddComponent<ContentSizeFitter>();
                    fit.horizontalFit = ContentSizeFitter.FitMode.PreferredSize;
                    fit.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

                    if (pingKind.HasValue)
                    {
                        var icGo = new GameObject("PingIcon");
                        icGo.transform.SetParent(lblObj.transform, false);
                        var ile = icGo.AddComponent<LayoutElement>();
                        ile.minWidth = 18f; ile.preferredWidth = 18f; ile.minHeight = 18f; ile.preferredHeight = 18f;
                        var iImg = icGo.AddComponent<Image>();
                        iImg.sprite = NativeAssetRegistry.GetPingIconSprite(pingKind.Value);
                        iImg.preserveAspect = true;
                        iImg.raycastTarget = false;
                    }
                    else
                    {
                        Color pCol = CoopConfig.GetPlayerColor(_net.GetPlayerColorIndex(actorPlayerId));
                        NativeStyleProvider.CreateHeroAvatarWidget(lblObj.transform, _net.GetPlayerAvatarHeroId(actorPlayerId), pCol, 18f, clickable: false);
                    }

                    tmp = NativeStyleProvider.CreateLabel(lblObj.transform, labelText, 12f, color, TextAlignmentOptions.MidlineLeft, FontStyles.Bold, addShadow: true);
                    lblRect.localScale = Vector3.one * CoopConfig.GetEffectiveStickerScale();
                }

                _effects.Add(new ActiveEffect
                {
                    RingObj = ring,
                    RingRenderer = ringSr,
                    BeaconObj = beacon,
                    BeaconRenderer = beaconSr,
                    LabelObj = lblObj,
                    LabelRect = lblRect,
                    LabelGroup = lblGroup,
                    LabelText = tmp,
                    BaseColor = color,
                    WorldPos = pos,
                    BaseLabel = labelText,
                    Age = 0f,
                    Duration = duration,
                    IsPing = isPing
                });
            }
            catch (Exception ex)
            {
                CoopLogger.Debug(LogCategory.Presence, $"CreateEffect warning: {ex.Message}");
            }
        }

        private static float NormalizeSpriteUnitScale(Sprite sp, float desiredWorldUnits)
        {
            if (sp == null) return desiredWorldUnits;
            float w = sp.bounds.size.x;
            float h = sp.bounds.size.y;
            float maxDim = Mathf.Max(w, h);
            return maxDim > 0.001f ? desiredWorldUnits / maxDim : desiredWorldUnits;
        }

        private static void SpawnBurstParticles(Vector3 pos, Color color, int count)
        {
            try
            {
                var go = new GameObject("EWCoop_BurstVFX");
                go.transform.position = pos + new Vector3(0f, 0.25f, 0f);
                var ps = go.AddComponent<ParticleSystem>();
                var main = ps.main;
                main.duration = 0.45f;
                main.startLifetime = new ParticleSystem.MinMaxCurve(0.35f, 0.65f);
                main.startSpeed = new ParticleSystem.MinMaxCurve(1.4f, 3.2f);
                main.startSize = new ParticleSystem.MinMaxCurve(0.10f, 0.22f);
                main.startColor = new ParticleSystem.MinMaxGradient(color);
                main.gravityModifier = 0.6f;
                main.loop = false;

                var emission = ps.emission;
                emission.rateOverTime = 0f;
                var shape = ps.shape;
                shape.shapeType = ParticleSystemShapeType.Hemisphere;
                shape.radius = 0.35f;

                var renderer = go.GetComponent<ParticleSystemRenderer>();
                if (renderer != null)
                {
                    if (_sharedParticleMat == null)
                    {
                        var shader = Shader.Find("Sprites/Default") ?? Shader.Find("Universal Render Pipeline/Unlit");
                        if (shader != null) _sharedParticleMat = new Material(shader) { color = Color.white };
                    }
                    if (_sharedParticleMat != null) renderer.sharedMaterial = _sharedParticleMat;
                }
                ps.Emit(count);
                UnityEngine.Object.Destroy(go, 1.2f);
            }
            catch { }
        }

        private static void PlayCueSound(bool isPing, bool isUpgrade)
        {
            try
            {
                if (isPing) NativeStyleProvider.PlayUiClickSound(pitch: isUpgrade ? 1.35f : 1.22f);
                else if (isUpgrade) NativeStyleProvider.PlayUiClickSound(pitch: 1.15f);
                else NativeStyleProvider.PlayUiClickSound(pitch: 0.95f);
            }
            catch { }
        }

        public void Tick(float dt)
        {
            var cam = Camera.main;
            Vector2 canvasSz = NativeStyleProvider.GetCanvasSize();
            float halfW = canvasSz.x * 0.5f;
            float halfH = canvasSz.y * 0.5f;
            bool hasTowerPanel = GameRefs.TryGetActiveTowerInspectionScreenRect(out Rect towerRect);

            for (int i = _effects.Count - 1; i >= 0; i--)
            {
                var e = _effects[i];
                e.Age += dt;
                float t = Mathf.Clamp01(e.Age / e.Duration);

                if (t >= 1f)
                {
                    DestroyEffect(e);
                    _effects.RemoveAt(i);
                    continue;
                }

                float alpha = (1f - t) * (1f - t);
                if (e.RingObj != null && e.RingRenderer != null)
                {
                    float targetUnits = e.IsPing ? Mathf.Lerp(0.55f, 2.55f, t) : Mathf.Lerp(0.50f, 1.65f, t);
                    float s = NormalizeSpriteUnitScale(e.RingRenderer.sprite, targetUnits);
                    e.RingObj.transform.localScale = new Vector3(s, s, 1f);
                    e.RingObj.transform.Rotate(0f, 0f, (e.IsPing ? 55f : 30f) * dt, Space.Self);
                    e.RingRenderer.color = new Color(e.BaseColor.r, e.BaseColor.g, e.BaseColor.b, alpha * 0.92f);
                }
                if (e.BeaconObj != null && e.BeaconRenderer != null)
                {
                    float bNorm = NormalizeSpriteUnitScale(e.BeaconRenderer.sprite, 1.0f);
                    float bw = Mathf.Lerp(0.46f, 0.12f, t);
                    float bh = Mathf.Lerp(2.5f, 3.3f, t);
                    e.BeaconObj.transform.localScale = new Vector3(bNorm * bw, bNorm * bh, 1f);
                    if (cam != null)
                    {
                        Vector3 toCam = cam.transform.position - e.BeaconObj.transform.position;
                        toCam.y = 0f;
                        if (toCam.sqrMagnitude > 0.001f)
                            e.BeaconObj.transform.rotation = Quaternion.LookRotation(-toCam.normalized, Vector3.up);
                    }
                    e.BeaconRenderer.color = new Color(e.BaseColor.r, e.BaseColor.g, e.BaseColor.b, alpha * 0.78f);
                }

                if (e.LabelRect != null && cam != null)
                {
                    float rise = e.IsPing ? 1.45f + t * 0.4f : 1.0f + t * 0.85f;
                    Vector3 sp = cam.WorldToScreenPoint(e.WorldPos + new Vector3(0f, rise, 0f));
                    bool inFront = sp.z > 0f;
                    bool onScreen = inFront && sp.x >= 0f && sp.x <= Screen.width && sp.y >= 0f && sp.y <= Screen.height;
                    float labelAlpha = Mathf.Clamp01(1f - Mathf.Max(0f, (t - 0.62f) / 0.38f));

                    if (onScreen)
                    {
                        if (!e.LabelObj.activeSelf) e.LabelObj.SetActive(true);
                        if (hasTowerPanel && towerRect.Contains(new Vector2(sp.x, sp.y)))
                            labelAlpha *= 0.20f;
                        e.LabelRect.anchoredPosition = NativeStyleProvider.ScreenToCanvasLocal(new Vector2(sp.x, sp.y));
                        if (e.LabelText != null) e.LabelText.text = e.BaseLabel;
                    }
                    else if (e.IsPing)
                    {
                        if (!e.LabelObj.activeSelf) e.LabelObj.SetActive(true);
                        if (!inFront)
                        {
                            sp.x = Screen.width - sp.x;
                            sp.y = Screen.height - sp.y;
                        }
                        Vector2 local = NativeStyleProvider.ScreenToCanvasLocal(new Vector2(sp.x, sp.y));
                        Vector2 dir = local.normalized;
                        // Keep off-screen ping pointers away from vanilla top/bottom bars
                        float cx = Mathf.Clamp(local.x, -halfW + 150f, halfW - 150f);
                        float cy = Mathf.Clamp(local.y, -halfH + 135f, halfH - 135f);
                        e.LabelRect.anchoredPosition = new Vector2(cx, cy);
                        string arrow = Math.Abs(dir.x) > Math.Abs(dir.y)
                            ? (dir.x > 0f ? "→" : "←")
                            : (dir.y > 0f ? "↑" : "↓");
                        if (e.LabelText != null) e.LabelText.text = $"{arrow} {e.BaseLabel}";
                    }
                    else
                    {
                        if (e.LabelObj.activeSelf) e.LabelObj.SetActive(false);
                    }

                    if (e.LabelGroup != null)
                        e.LabelGroup.alpha = labelAlpha;
                }
            }
        }

        private static void DestroyEffect(ActiveEffect e)
        {
            if (e.RingObj != null) UnityEngine.Object.Destroy(e.RingObj);
            if (e.BeaconObj != null) UnityEngine.Object.Destroy(e.BeaconObj);
            if (e.LabelObj != null) UnityEngine.Object.Destroy(e.LabelObj);
        }

        public void ClearAll()
        {
            CancelAndCloseWheel(absorbRelease: false);
            for (int i = 0; i < _effects.Count; i++) DestroyEffect(_effects[i]);
            _effects.Clear();
        }
    }
}
