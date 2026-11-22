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

        private sealed class ActiveEffect
        {
            public GameObject RingObj;
            public GameObject BeaconObj;
            public GameObject LabelObj;
            public RectTransform LabelRect;
            public CanvasGroup LabelGroup;
            public TMP_Text LabelText;
            public Material RingMat;
            public Vector3 WorldPos;
            public string BaseLabel;
            public float Age;
            public float Duration;
            public bool IsPing;
        }

        private readonly List<ActiveEffect> _effects = new List<ActiveEffect>();
        private float _lastLocalPingTime;

        private bool _holdingPingKey;
        private float _pingHoldStartTime;
        private Vector3 _pingAnchorScreenPos;
        private Vector3 _pingAnchorWorldPos;
        private bool _wheelVisible;
        private int _highlightedSlice = 0;

        private GameObject _wheelRoot;
        private RectTransform _wheelRect;
        private readonly Image[] _sliceBgs = new Image[5];
        private readonly TMP_Text[] _sliceLabels = new TMP_Text[5];

        private static readonly (PingKind kind, string locKey, Color accent)[] WheelSlices =
        {
            (PingKind.BuildMazeHere,  "ping.build_maze",    new Color(0.96f, 0.78f, 0.25f, 1f)),
            (PingKind.PlaceDpsHere,   "ping.place_dps",     new Color(0.95f, 0.44f, 0.22f, 1f)),
            (PingKind.NeedGold,       "ping.need_gold",     new Color(1.00f, 0.86f, 0.30f, 1f)),
            (PingKind.UpgradeOrFocus, "ping.upgrade_focus", new Color(0.32f, 0.85f, 1.00f, 1f)),
            (PingKind.WatchLane,      "ping.watch_lane",    new Color(1.00f, 0.32f, 0.32f, 1f))
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
            PingKind.BuildMazeHere  => new Color(0.96f, 0.78f, 0.25f, 1f),
            PingKind.PlaceDpsHere   => new Color(0.95f, 0.44f, 0.22f, 1f),
            PingKind.NeedGold       => new Color(1.00f, 0.86f, 0.30f, 1f),
            PingKind.UpgradeOrFocus => new Color(0.32f, 0.85f, 1.00f, 1f),
            PingKind.WatchLane      => new Color(1.00f, 0.30f, 0.25f, 1f),
            PingKind.NiceTower      => new Color(0.40f, 0.96f, 0.56f, 1f),
            PingKind.WatchPortal    => new Color(0.92f, 0.28f, 0.96f, 1f),
            _                       => Color.white
        };

        public void TryHandleLocalPingInput()
        {
            if (!_net.IsConnected || !GameRefs.IsInBattle || CoopInputGuard.IsEmoteWheelOpen || CoopInputGuard.IsCoopModalOpen || CoopInputGuard.IsMazeSketching)
            {
                HideWheel();
                _holdingPingKey = false;
                return;
            }

            if (Input.GetKeyDown(KeyCode.F1)) { TryQuickHotkeyPing(PingKind.BuildMazeHere); return; }
            if (Input.GetKeyDown(KeyCode.F2)) { TryQuickHotkeyPing(PingKind.PlaceDpsHere); return; }
            if (Input.GetKeyDown(KeyCode.F3)) { TryQuickHotkeyPing(PingKind.NeedGold); return; }
            if (Input.GetKeyDown(KeyCode.F4)) { TryQuickHotkeyPing(PingKind.UpgradeOrFocus); return; }
            if (Input.GetKeyDown(KeyCode.F5)) { TryQuickHotkeyPing(PingKind.WatchLane); return; }

            bool pressed = Input.GetKeyDown(CoopConfig.PingHotkey.Value);
            bool held = Input.GetKey(CoopConfig.PingHotkey.Value);

            if (pressed && !_holdingPingKey)
            {
                if (TryRaycastGround(out Vector3 hitPos))
                {
                    _holdingPingKey = true;
                    _pingHoldStartTime = Time.unscaledTime;
                    _pingAnchorScreenPos = Input.mousePosition;
                    _pingAnchorWorldPos = hitPos;
                    _highlightedSlice = Math.Clamp((int)InferContextualPing(hitPos), 0, WheelSlices.Length - 1);
                }
            }

            if (_holdingPingKey)
            {
                float holdDur = Time.unscaledTime - _pingHoldStartTime;
                if (held)
                {
                    if (holdDur >= 0.18f)
                        ShowOrUpdateWheel();
                }
                else
                {
                    PingKind chosen = _wheelVisible
                        ? WheelSlices[Math.Clamp(_highlightedSlice, 0, WheelSlices.Length - 1)].kind
                        : InferContextualPing(_pingAnchorWorldPos);

                    _holdingPingKey = false;
                    HideWheel();
                    FireLocalPing(_pingAnchorWorldPos, chosen);
                }
            }
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
                CoopInputGuard.IsPingWheelOpen = true;
                _wheelRoot.SetActive(true);
                _wheelRect.position = _pingAnchorScreenPos;
                RefreshLocalizedWheelLabels();
            }

            Vector2 delta = (Vector2)(Input.mousePosition - _pingAnchorScreenPos);
            if (delta.sqrMagnitude > 225f)
            {
                float angle = Mathf.Atan2(delta.x, delta.y) * Mathf.Rad2Deg;
                if (angle < 0f) angle += 360f;
                int idx = Mathf.FloorToInt(((angle + 36f) % 360f) / 72f);
                _highlightedSlice = Math.Clamp(idx, 0, WheelSlices.Length - 1);
            }

            for (int i = 0; i < WheelSlices.Length; i++)
            {
                bool active = i == _highlightedSlice;
                if (_sliceBgs[i] != null)
                {
                    Color accent = WheelSlices[i].accent;
                    _sliceBgs[i].color = active
                        ? new Color(accent.r * 0.55f, accent.g * 0.55f, accent.b * 0.55f, 0.96f)
                        : new Color(0.10f, 0.09f, 0.14f, 0.88f);
                }
                if (_sliceLabels[i] != null)
                    _sliceLabels[i].color = active ? Color.white : WheelSlices[i].accent;
            }
        }

        private void HideWheel()
        {
            _wheelVisible = false;
            CoopInputGuard.IsPingWheelOpen = false;
            if (_wheelRoot != null && _wheelRoot.activeSelf)
                _wheelRoot.SetActive(false);
        }

        private void RefreshLocalizedWheelLabels()
        {
            if (_wheelRoot == null) return;
            for (int i = 0; i < WheelSlices.Length; i++)
                if (_sliceLabels[i] != null)
                    _sliceLabels[i].text = CoopLoc.T(WheelSlices[i].locKey);
        }

        private void EnsureWheelBuilt()
        {
            if (_wheelRoot != null) return;
            var canvas = NativeStyleProvider.GetOrCreateOverlayCanvas();

            _wheelRoot = new GameObject("EWCoop_PingWheel");
            _wheelRoot.transform.SetParent(canvas.transform, false);
            _wheelRect = _wheelRoot.AddComponent<RectTransform>();
            _wheelRect.sizeDelta = new Vector2(280f, 280f);

            var cg = _wheelRoot.AddComponent<CanvasGroup>();
            cg.blocksRaycasts = false;
            cg.interactable = false;

            var center = NativeStyleProvider.CreatePanel(_wheelRoot.transform, "Center", new Color(0.08f, 0.07f, 0.11f, 0.92f), addBorder: true);
            var crt = center.GetComponent<RectTransform>();
            crt.sizeDelta = new Vector2(88f, 30f);
            var cLbl = NativeStyleProvider.CreateLabel(center.transform, "PING", 13f, NativeStyleProvider.EmberGold, TextAlignmentOptions.Center, FontStyles.Bold);
            NativeStyleProvider.StretchFill(cLbl.rectTransform);

            const float radius = 118f;
            for (int i = 0; i < WheelSlices.Length; i++)
            {
                float rad = (i * 72f) * Mathf.Deg2Rad;
                Vector2 pos = new Vector2(Mathf.Sin(rad) * radius, Mathf.Cos(rad) * radius);

                var pill = NativeStyleProvider.CreatePanel(_wheelRoot.transform, $"Slice_{i}", NativeStyleProvider.PanelBg, addBorder: true);
                var prt = pill.GetComponent<RectTransform>();
                prt.anchoredPosition = pos;
                prt.sizeDelta = new Vector2(154f, 34f);

                _sliceBgs[i] = pill.GetComponent<Image>();
                var outl = pill.GetComponent<Outline>();
                if (outl != null) outl.effectColor = WheelSlices[i].accent;

                var lbl = NativeStyleProvider.CreateLabel(pill.transform, CoopLoc.T(WheelSlices[i].locKey), 12f, WheelSlices[i].accent, TextAlignmentOptions.Center, FontStyles.Bold);
                NativeStyleProvider.StretchFill(lbl.rectTransform, 4f, 2f);
                _sliceLabels[i] = lbl;
            }
            _wheelRoot.SetActive(false);
        }

        public void HandlePingPacket(int senderId, BinaryReader r)
        {
            int actorId = r.ReadInt32();
            if (_net.IsHost) actorId = senderId;
            Vector3 pos = r.ReadVector3();
            var kind = (PingKind)r.ReadByte();

            if (actorId != _net.LocalPlayerId)
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
            CreateEffect(pos, col, callout, duration: 3.2f, isPing: true);
            PlayCueSound(isPing: true, isUpgrade: kind == PingKind.NeedGold || kind == PingKind.NiceTower);
        }

        public void SpawnActionFeedback(int playerId, Vector3 pos, string actionTag, bool isUpgrade)
        {
            if (!CoopConfig.EnableSpatialFeedback.Value) return;
            Color col = CoopConfig.GetPlayerColor(_net.GetPlayerColorIndex(playerId));
            string label = $"{_net.GetPlayerName(playerId)} · {actionTag}";
            CreateEffect(pos, col, label, duration: 1.55f, isPing: false);
            PlayCueSound(isPing: false, isUpgrade: isUpgrade);
        }

        private void CreateEffect(Vector3 pos, Color color, string labelText, float duration, bool isPing)
        {
            try
            {
                var shader = Shader.Find("Universal Render Pipeline/Unlit") ?? Shader.Find("Sprites/Default") ?? Shader.Find("Unlit/Color");
                var ring = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
                ring.name = "EWCoop_PulseRing";
                ring.layer = 2;
                var col = ring.GetComponent<Collider>();
                if (col != null) UnityEngine.Object.Destroy(col);
                ring.transform.position = new Vector3(pos.x, pos.y + 0.08f, pos.z);
                ring.transform.localScale = new Vector3(0.4f, 0.02f, 0.4f);

                Material mat = shader != null ? new Material(shader) : null;
                if (mat != null)
                {
                    if (mat.HasProperty("_Surface")) mat.SetFloat("_Surface", 1f);
                    if (mat.HasProperty("_SrcBlend")) mat.SetInt("_SrcBlend", (int)UnityEngine.Rendering.BlendMode.SrcAlpha);
                    if (mat.HasProperty("_DstBlend")) mat.SetInt("_DstBlend", (int)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
                    if (mat.HasProperty("_ZWrite")) mat.SetInt("_ZWrite", 0);
                    mat.renderQueue = 3150;
                    SetMatColor(mat, new Color(color.r, color.g, color.b, 0.80f));
                    var rdr = ring.GetComponent<Renderer>();
                    if (rdr != null)
                    {
                        rdr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                        rdr.sharedMaterial = mat;
                    }
                }

                GameObject beacon = null;
                if (isPing)
                {
                    beacon = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
                    beacon.name = "EWCoop_PingBeacon";
                    beacon.layer = 2;
                    var bcol = beacon.GetComponent<Collider>();
                    if (bcol != null) UnityEngine.Object.Destroy(bcol);
                    beacon.transform.position = new Vector3(pos.x, pos.y + 2.2f, pos.z);
                    beacon.transform.localScale = new Vector3(0.11f, 2.2f, 0.11f);
                    var br = beacon.GetComponent<Renderer>();
                    if (br != null && mat != null)
                    {
                        br.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                        br.sharedMaterial = mat;
                    }
                }

                SpawnBurstParticles(pos, color, isPing ? 16 : 10);

                GameObject lblObj = null;
                RectTransform lblRect = null;
                CanvasGroup lblGroup = null;
                TMP_Text tmp = null;
                var canvasRoot = NativeStyleProvider.GetOrCreateOverlayCanvas();
                if (canvasRoot != null)
                {
                    lblObj = new GameObject("EWCoop_FloatingTag");
                    lblObj.transform.SetParent(canvasRoot.transform, false);
                    lblRect = lblObj.AddComponent<RectTransform>();
                    lblRect.sizeDelta = new Vector2(235f, 30f);
                    lblGroup = lblObj.AddComponent<CanvasGroup>();
                    lblGroup.blocksRaycasts = false;
                    lblGroup.interactable = false;

                    var bg = lblObj.AddComponent<Image>();
                    bg.color = new Color(0.08f, 0.07f, 0.11f, 0.86f);
                    bg.raycastTarget = false;

                    var outline = lblObj.AddComponent<Outline>();
                    outline.effectColor = new Color(color.r, color.g, color.b, 0.95f);
                    outline.effectDistance = new Vector2(1.5f, -1.5f);

                    var txtObj = new GameObject("Text");
                    txtObj.transform.SetParent(lblObj.transform, false);
                    var txtRect = txtObj.AddComponent<RectTransform>();
                    NativeStyleProvider.StretchFill(txtRect, 6f, 2f);

                    tmp = txtObj.AddComponent<TextMeshProUGUI>();
                    if (NativeStyleProvider.PrimaryFont != null) tmp.font = NativeStyleProvider.PrimaryFont;
                    tmp.text = labelText;
                    tmp.fontSize = 13f;
                    tmp.fontStyle = FontStyles.Bold;
                    tmp.alignment = TextAlignmentOptions.Center;
                    tmp.color = color;
                    tmp.raycastTarget = false;
                }

                _effects.Add(new ActiveEffect
                {
                    RingObj = ring,
                    BeaconObj = beacon,
                    LabelObj = lblObj,
                    LabelRect = lblRect,
                    LabelGroup = lblGroup,
                    LabelText = tmp,
                    RingMat = mat,
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
                    var shader = Shader.Find("Sprites/Default") ?? Shader.Find("Universal Render Pipeline/Unlit");
                    if (shader != null) renderer.material = new Material(shader) { color = color };
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
            float padX = 125f;
            float padY = 40f;

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
                if (e.RingObj != null)
                {
                    float scale = e.IsPing ? Mathf.Lerp(0.45f, 2.45f, t) : Mathf.Lerp(0.5f, 1.65f, t);
                    e.RingObj.transform.localScale = new Vector3(scale, 0.02f, scale);
                }
                if (e.BeaconObj != null)
                {
                    float bScale = Mathf.Lerp(0.14f, 0.02f, t);
                    e.BeaconObj.transform.localScale = new Vector3(bScale, Mathf.Lerp(2.4f, 3.2f, t), bScale);
                }
                if (e.RingMat != null)
                {
                    Color c = e.RingMat.HasProperty("_BaseColor") ? e.RingMat.GetColor("_BaseColor") : e.RingMat.color;
                    c.a = alpha * 0.85f;
                    SetMatColor(e.RingMat, c);
                }

                if (e.LabelRect != null && cam != null)
                {
                    float rise = e.IsPing ? 1.45f + t * 0.4f : 1.0f + t * 0.85f;
                    Vector3 sp = cam.WorldToScreenPoint(e.WorldPos + new Vector3(0f, rise, 0f));
                    bool inFront = sp.z > 0f;
                    bool onScreen = inFront && sp.x >= 0f && sp.x <= Screen.width && sp.y >= 0f && sp.y <= Screen.height;

                    if (onScreen)
                    {
                        if (!e.LabelObj.activeSelf) e.LabelObj.SetActive(true);
                        e.LabelRect.position = new Vector3(sp.x, sp.y, 0f);
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
                        Vector2 dir = new Vector2(sp.x - Screen.width * 0.5f, sp.y - Screen.height * 0.5f).normalized;
                        float cx = Mathf.Clamp(sp.x, padX, Screen.width - padX);
                        float cy = Mathf.Clamp(sp.y, padY, Screen.height - padY);
                        e.LabelRect.position = new Vector3(cx, cy, 0f);
                        string arrow = Math.Abs(dir.x) > Math.Abs(dir.y)
                            ? (dir.x > 0f ? "->" : "<-")
                            : (dir.y > 0f ? "^" : "v");
                        if (e.LabelText != null) e.LabelText.text = $"{arrow} {e.BaseLabel}";
                    }
                    else
                    {
                        if (e.LabelObj.activeSelf) e.LabelObj.SetActive(false);
                    }

                    if (e.LabelGroup != null)
                        e.LabelGroup.alpha = Mathf.Clamp01(1f - Mathf.Max(0f, (t - 0.62f) / 0.38f));
                }
            }
        }

        private static void SetMatColor(Material mat, Color c)
        {
            if (mat.HasProperty("_BaseColor")) mat.SetColor("_BaseColor", c);
            if (mat.HasProperty("_Color")) mat.SetColor("_Color", c);
        }

        private static void DestroyEffect(ActiveEffect e)
        {
            if (e.RingObj != null) UnityEngine.Object.Destroy(e.RingObj);
            if (e.BeaconObj != null) UnityEngine.Object.Destroy(e.BeaconObj);
            if (e.LabelObj != null) UnityEngine.Object.Destroy(e.LabelObj);
            if (e.RingMat != null) UnityEngine.Object.Destroy(e.RingMat);
        }

        public void ClearAll()
        {
            _holdingPingKey = false;
            HideWheel();
            for (int i = 0; i < _effects.Count; i++) DestroyEffect(_effects[i]);
            _effects.Clear();
        }
    }
}
