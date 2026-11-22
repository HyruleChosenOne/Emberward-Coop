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
    public sealed class MazeSketchManager
    {
        private readonly NetworkManager _net;

        private const int MaxTrailCells = 18;
        private const float TrailDuration = 4.8f;

        private sealed class ActiveTrail
        {
            public int OwnerPlayerId;
            public uint SequenceId;
            public Color Color;
            public readonly List<Vector3> Points = new List<Vector3>();
            public readonly List<GameObject> TileVisuals = new List<GameObject>();
            public readonly List<GameObject> ChevronVisuals = new List<GameObject>();
            public Material TileMat;
            public Material ChevronMat;
            public GameObject LabelObj;
            public RectTransform LabelRect;
            public CanvasGroup LabelGroup;
            public float Age;
            public float Duration = TrailDuration;
        }

        private readonly Dictionary<int, ActiveTrail> _trails = new Dictionary<int, ActiveTrail>();
        private readonly List<Vector3> _localDragPoints = new List<Vector3>();
        private Vector3Int _lastSnappedCell = new Vector3Int(int.MinValue, 0, int.MinValue);
        private bool _isDragging;
        private uint _localSeq;
        private float _lastStreamTime;
        private float _lastHintToastTime;

        public MazeSketchManager(NetworkManager net)
        {
            _net = net;
        }

        public void Tick(float now, float dt)
        {
            if (!_net.IsConnected || !GameRefs.IsInBattle)
            {
                if (_isDragging) EndLocalDrag();
                TickTrails(dt);
                return;
            }

            HandleLocalSketchInput(now);
            TickTrails(dt);
        }

        private void HandleLocalSketchInput(float now)
        {
            if (CoopInputGuard.IsCoopModalOpen || CoopInputGuard.IsEmoteWheelOpen || CoopInputGuard.IsPingWheelOpen)
            {
                if (_isDragging) EndLocalDrag();
                return;
            }

            KeyCode modKey = CoopConfig.SketchModifierKey.Value;
            bool modHeld = Input.GetKey(modKey) || (modKey == KeyCode.LeftAlt && Input.GetKey(KeyCode.RightAlt));

            if (!_isDragging)
            {
                if (modHeld && Input.GetMouseButtonDown(0))
                {
                    if (TryGetSnappedGroundPoint(out Vector3Int cell, out Vector3 worldPt))
                    {
                        _isDragging = true;
                        CoopInputGuard.IsMazeSketching = true;
                        _localSeq++;
                        _localDragPoints.Clear();
                        _localDragPoints.Add(worldPt);
                        _lastSnappedCell = cell;
                        _lastStreamTime = now;
                        ApplyTrail(_net.LocalPlayerId, _localSeq, _localDragPoints, isFinal: false);
                        SendTrailPacket(_localSeq, _localDragPoints, isFinal: false);
                    }
                }
                return;
            }

            if (Input.GetMouseButton(0) && modHeld)
            {
                if (TryGetSnappedGroundPoint(out Vector3Int cell, out Vector3 worldPt))
                {
                    if (cell != _lastSnappedCell && _localDragPoints.Count < MaxTrailCells)
                    {
                        bool duplicate = false;
                        for (int i = 0; i < _localDragPoints.Count; i++)
                        {
                            if ((_localDragPoints[i] - worldPt).sqrMagnitude < 0.2f) { duplicate = true; break; }
                        }

                        if (!duplicate)
                        {
                            _lastSnappedCell = cell;
                            _localDragPoints.Add(worldPt);
                            ApplyTrail(_net.LocalPlayerId, _localSeq, _localDragPoints, isFinal: false);
                            if (now - _lastStreamTime >= 0.06f)
                            {
                                _lastStreamTime = now;
                                SendTrailPacket(_localSeq, _localDragPoints, isFinal: false);
                            }
                        }
                    }
                }
            }
            else
            {
                if (_localDragPoints.Count > 0)
                {
                    ApplyTrail(_net.LocalPlayerId, _localSeq, _localDragPoints, isFinal: true);
                    SendTrailPacket(_localSeq, _localDragPoints, isFinal: true);
                    if (CoopConfig.EnableSpatialFeedback.Value)
                        NativeStyleProvider.PlayUiClickSound(1.24f);
                }
                EndLocalDrag();
            }
        }

        private void EndLocalDrag()
        {
            _isDragging = false;
            CoopInputGuard.IsMazeSketching = false;
            _localDragPoints.Clear();
            _lastSnappedCell = new Vector3Int(int.MinValue, 0, int.MinValue);
        }

        private void SendTrailPacket(uint seq, List<Vector3> pts, bool isFinal)
        {
            if (!_net.IsConnected || pts.Count == 0) return;
            DeliveryMode mode = isFinal ? DeliveryMode.ReliableOrdered : DeliveryMode.Unreliable;
            _net.SendToAllPeersOrHost(PacketType.MazeSketchTrail, w =>
            {
                w.Write(_net.LocalPlayerId);
                w.Write(seq);
                w.Write(isFinal);
                w.Write((byte)Math.Min(pts.Count, MaxTrailCells));
                for (int i = 0; i < Math.Min(pts.Count, MaxTrailCells); i++)
                    w.WriteVector3(pts[i]);
            }, mode);
        }

        public void HandleSketchPacket(int senderId, BinaryReader r)
        {
            int actorId = r.ReadInt32();
            if (_net.IsHost) actorId = senderId;
            uint seq = r.ReadUInt32();
            bool isFinal = r.ReadBoolean();
            int count = Math.Clamp((int)r.ReadByte(), 0, MaxTrailCells);
            var pts = new List<Vector3>(count);
            for (int i = 0; i < count; i++)
                pts.Add(r.ReadVector3());

            if (actorId != _net.LocalPlayerId && count > 0)
            {
                // Reject stale out-of-order unreliable packets
                if (_trails.TryGetValue(actorId, out var existing) && seq < existing.SequenceId)
                    return;

                ApplyTrail(actorId, seq, pts, isFinal);
                if (isFinal && Time.unscaledTime - _lastHintToastTime > 3.0f)
                {
                    _lastHintToastTime = Time.unscaledTime;
                    if (CoopConfig.EnableSpatialFeedback.Value)
                        NativeStyleProvider.PlayUiClickSound(1.18f);
                }
            }

            if (_net.IsHost)
            {
                DeliveryMode mode = isFinal ? DeliveryMode.ReliableOrdered : DeliveryMode.Unreliable;
                _net.BroadcastToClients(PacketType.MazeSketchTrail, w =>
                {
                    w.Write(actorId);
                    w.Write(seq);
                    w.Write(isFinal);
                    w.Write((byte)count);
                    for (int i = 0; i < count; i++) w.WriteVector3(pts[i]);
                }, mode, excludePeerId: senderId);
            }
        }

        private void ApplyTrail(int playerId, uint seq, List<Vector3> pts, bool isFinal)
        {
            if (_trails.TryGetValue(playerId, out var old))
                DestroyTrail(old);

            Color col = CoopConfig.GetPlayerColor(_net.GetPlayerColorIndex(playerId));
            var trail = new ActiveTrail
            {
                OwnerPlayerId = playerId,
                SequenceId = seq,
                Color = col,
                Age = 0f,
                Duration = isFinal ? TrailDuration : TrailDuration + 1.5f
            };
            trail.Points.AddRange(pts);

            var shader = Shader.Find("Universal Render Pipeline/Unlit") ?? Shader.Find("Sprites/Default") ?? Shader.Find("Unlit/Color");
            trail.TileMat = CreateTranslucentMat(shader, new Color(col.r, col.g, col.b, 0.36f), 3120);
            trail.ChevronMat = CreateTranslucentMat(shader, new Color(1f, 0.96f, 0.85f, 0.92f), 3130);

            for (int i = 0; i < pts.Count; i++)
            {
                Vector3 p = pts[i];
                var tile = GameObject.CreatePrimitive(PrimitiveType.Cube);
                tile.name = $"EWCoop_SketchTile_{playerId}_{i}";
                tile.layer = 2;
                var c = tile.GetComponent<Collider>();
                if (c != null) UnityEngine.Object.Destroy(c);

                tile.transform.position = new Vector3(p.x, p.y + 0.06f, p.z);
                tile.transform.localScale = new Vector3(0.88f, 0.04f, 0.88f);
                var rdr = tile.GetComponent<Renderer>();
                if (rdr != null && trail.TileMat != null)
                {
                    rdr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                    rdr.sharedMaterial = trail.TileMat;
                }
                trail.TileVisuals.Add(tile);

                if (i + 1 < pts.Count)
                {
                    Vector3 dir = pts[i + 1] - p;
                    dir.y = 0f;
                    if (dir.sqrMagnitude > 0.01f)
                    {
                        Vector3 mid = (p + pts[i + 1]) * 0.5f;
                        var chev = CreateChevronVisual(mid + new Vector3(0f, 0.14f, 0f), dir.normalized, trail.ChevronMat);
                        trail.ChevronVisuals.Add(chev);
                    }
                }
            }

            var canvas = NativeStyleProvider.GetOrCreateOverlayCanvas();
            if (canvas != null && pts.Count > 0)
            {
                var lblObj = new GameObject($"EWCoop_SketchLabel_{playerId}");
                lblObj.transform.SetParent(canvas.transform, false);
                var rt = lblObj.AddComponent<RectTransform>();
                rt.sizeDelta = new Vector2(220f, 26f);
                var cg = lblObj.AddComponent<CanvasGroup>();
                cg.blocksRaycasts = false;
                cg.interactable = false;

                var bg = lblObj.AddComponent<Image>();
                bg.color = new Color(0.08f, 0.07f, 0.12f, 0.86f);
                bg.raycastTarget = false;
                var outl = lblObj.AddComponent<Outline>();
                outl.effectColor = col;
                outl.effectDistance = new Vector2(1.4f, -1.4f);

                var txt = NativeStyleProvider.CreateLabel(lblObj.transform, CoopLoc.F("sketch.hint", _net.GetPlayerName(playerId)), 11.5f, col, TextAlignmentOptions.Center, FontStyles.Bold);
                NativeStyleProvider.StretchFill(txt.rectTransform, 6f, 2f);

                trail.LabelObj = lblObj;
                trail.LabelRect = rt;
                trail.LabelGroup = cg;
            }

            _trails[playerId] = trail;
        }

        private static GameObject CreateChevronVisual(Vector3 pos, Vector3 forward, Material mat)
        {
            var root = new GameObject("EWCoop_Chevron");
            root.layer = 2;
            root.transform.position = pos;
            root.transform.rotation = Quaternion.LookRotation(forward, Vector3.up);

            for (int side = -1; side <= 1; side += 2)
            {
                var arm = GameObject.CreatePrimitive(PrimitiveType.Cube);
                arm.layer = 2;
                var c = arm.GetComponent<Collider>();
                if (c != null) UnityEngine.Object.Destroy(c);
                arm.transform.SetParent(root.transform, false);
                arm.transform.localPosition = new Vector3(side * 0.13f, 0f, -0.05f);
                arm.transform.localRotation = Quaternion.Euler(0f, side * -38f, 0f);
                arm.transform.localScale = new Vector3(0.08f, 0.04f, 0.36f);
                var rdr = arm.GetComponent<Renderer>();
                if (rdr != null && mat != null)
                {
                    rdr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                    rdr.sharedMaterial = mat;
                }
            }
            return root;
        }

        private void TickTrails(float dt)
        {
            if (_trails.Count == 0) return;
            var cam = Camera.main;
            var toRemove = new List<int>();
            float time = Time.unscaledTime;

            foreach (var kv in _trails)
            {
                var tr = kv.Value;
                tr.Age += dt;
                float t = Mathf.Clamp01(tr.Age / tr.Duration);
                if (t >= 1f)
                {
                    toRemove.Add(kv.Key);
                    continue;
                }

                float fade = t > 0.70f ? Mathf.Clamp01(1f - (t - 0.70f) / 0.30f) : 1f;
                float pulse = 0.80f + 0.20f * Mathf.Sin(time * 6.5f);

                if (tr.TileMat != null)
                    SetMatColor(tr.TileMat, new Color(tr.Color.r, tr.Color.g, tr.Color.b, 0.38f * pulse * fade));
                if (tr.ChevronMat != null)
                    SetMatColor(tr.ChevronMat, new Color(1f, 0.96f, 0.86f, 0.92f * fade));

                if (tr.LabelRect != null && cam != null && tr.Points.Count > 0)
                {
                    Vector3 anchor = tr.Points[tr.Points.Count - 1] + new Vector3(0f, 1.15f, 0f);
                    Vector3 sp = cam.WorldToScreenPoint(anchor);
                    bool vis = sp.z > 0f && sp.x > -100f && sp.x < Screen.width + 100f && sp.y > -100f && sp.y < Screen.height + 100f;
                    if (tr.LabelObj.activeSelf != vis) tr.LabelObj.SetActive(vis);
                    if (vis) tr.LabelRect.position = new Vector3(sp.x, sp.y, 0f);
                    if (tr.LabelGroup != null) tr.LabelGroup.alpha = fade;
                }
            }

            for (int i = 0; i < toRemove.Count; i++)
            {
                int id = toRemove[i];
                DestroyTrail(_trails[id]);
                _trails.Remove(id);
            }
        }

        private static bool TryGetSnappedGroundPoint(out Vector3Int cell, out Vector3 centerWorld)
        {
            cell = Vector3Int.zero;
            centerWorld = Vector3.zero;
            var cam = Camera.main;
            if (cam == null) return false;

            Ray ray = cam.ScreenPointToRay(Input.mousePosition);
            Plane ground = new Plane(Vector3.up, Vector3.zero);
            if (!ground.Raycast(ray, out float dist)) return false;
            Vector3 hit = ray.GetPoint(dist);

            try
            {
                var gs = GridSystem.Instance;
                if (gs != null)
                {
                    cell = gs.GetGridCell(hit);
                    centerWorld = gs.GetCellCenterWorld(cell);
                    centerWorld.y = 0f;
                    return true;
                }
            }
            catch { }

            cell = new Vector3Int(Mathf.RoundToInt(hit.x), 0, Mathf.RoundToInt(hit.z));
            centerWorld = new Vector3(cell.x, 0f, cell.z);
            return true;
        }

        private static Material CreateTranslucentMat(Shader shader, Color col, int renderQueue)
        {
            if (shader == null) return null;
            var mat = new Material(shader);
            if (mat.HasProperty("_Surface")) mat.SetFloat("_Surface", 1f);
            if (mat.HasProperty("_SrcBlend")) mat.SetInt("_SrcBlend", (int)UnityEngine.Rendering.BlendMode.SrcAlpha);
            if (mat.HasProperty("_DstBlend")) mat.SetInt("_DstBlend", (int)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
            if (mat.HasProperty("_ZWrite")) mat.SetInt("_ZWrite", 0);
            mat.renderQueue = renderQueue;
            SetMatColor(mat, col);
            return mat;
        }

        private static void SetMatColor(Material mat, Color c)
        {
            if (mat.HasProperty("_BaseColor")) mat.SetColor("_BaseColor", c);
            if (mat.HasProperty("_Color")) mat.SetColor("_Color", c);
        }

        private static void DestroyTrail(ActiveTrail tr)
        {
            for (int i = 0; i < tr.TileVisuals.Count; i++)
                if (tr.TileVisuals[i] != null) UnityEngine.Object.Destroy(tr.TileVisuals[i]);
            for (int i = 0; i < tr.ChevronVisuals.Count; i++)
                if (tr.ChevronVisuals[i] != null) UnityEngine.Object.Destroy(tr.ChevronVisuals[i]);
            if (tr.LabelObj != null) UnityEngine.Object.Destroy(tr.LabelObj);
            if (tr.TileMat != null) UnityEngine.Object.Destroy(tr.TileMat);
            if (tr.ChevronMat != null) UnityEngine.Object.Destroy(tr.ChevronMat);
        }

        public void ClearAll()
        {
            EndLocalDrag();
            foreach (var kv in _trails) DestroyTrail(kv.Value);
            _trails.Clear();
        }
    }
}
