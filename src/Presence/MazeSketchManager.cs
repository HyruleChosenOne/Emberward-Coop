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
            public readonly List<SpriteRenderer> TileRenderers = new List<SpriteRenderer>();
            public readonly List<GameObject> ChevronVisuals = new List<GameObject>();
            public readonly List<SpriteRenderer> ChevronRenderers = new List<SpriteRenderer>();
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

        public void CancelLocalDrag() => EndLocalDrag();

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

            NativeAssetRegistry.EnsureScanned();
            Sprite tileSprite = NativeAssetRegistry.GetSketchTileSprite();
            Sprite arrowSprite = NativeAssetRegistry.GetSketchArrowSprite();
            float tileScale = NormalizeSpriteScale(tileSprite, 0.92f);
            float arrowScale = NormalizeSpriteScale(arrowSprite, 0.56f);

            for (int i = 0; i < pts.Count; i++)
            {
                Vector3 p = pts[i];
                var tile = new GameObject($"EWCoop_SketchTile_{playerId}_{i}");
                tile.layer = 2;
                tile.transform.position = new Vector3(p.x, p.y + 0.08f, p.z);
                tile.transform.rotation = Quaternion.Euler(90f, 0f, 0f);
                tile.transform.localScale = new Vector3(tileScale, tileScale, 1f);

                var sr = tile.AddComponent<SpriteRenderer>();
                sr.sprite = tileSprite;
                sr.color = new Color(col.r, col.g, col.b, 0.68f);
                sr.sortingOrder = 20;
                trail.TileVisuals.Add(tile);
                trail.TileRenderers.Add(sr);

                if (i + 1 < pts.Count)
                {
                    Vector3 dir = pts[i + 1] - p;
                    dir.y = 0f;
                    if (dir.sqrMagnitude > 0.01f)
                    {
                        Vector3 mid = (p + pts[i + 1]) * 0.5f;
                        var arrowGo = CreateArrowDecal(mid + new Vector3(0f, 0.12f, 0f), dir.normalized, arrowSprite, arrowScale, out SpriteRenderer aSr);
                        trail.ChevronVisuals.Add(arrowGo);
                        if (aSr != null) trail.ChevronRenderers.Add(aSr);
                    }
                }
            }

            var canvas = NativeStyleProvider.GetOrCreateOverlayCanvas();
            if (canvas != null && pts.Count > 0)
            {
                var lblObj = NativeStyleProvider.CreatePanel(canvas.transform, $"EWCoop_SketchLabel_{playerId}", NativeStyleProvider.TooltipBg, addBorder: true, isSubPanel: true);
                var rt = lblObj.GetComponent<RectTransform>();
                rt.anchorMin = new Vector2(0.5f, 0.5f);
                rt.anchorMax = new Vector2(0.5f, 0.5f);
                var cg = lblObj.AddComponent<CanvasGroup>();
                cg.blocksRaycasts = false;
                cg.interactable = false;

                var hlg = lblObj.AddComponent<HorizontalLayoutGroup>();
                hlg.padding = new RectOffset(10, 12, 7, 7);
                hlg.spacing = 5f;
                hlg.childAlignment = TextAnchor.MiddleLeft;
                hlg.childControlWidth = true;
                hlg.childControlHeight = true;
                hlg.childForceExpandWidth = false;
                var fit = lblObj.AddComponent<ContentSizeFitter>();
                fit.horizontalFit = ContentSizeFitter.FitMode.PreferredSize;
                fit.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

                NativeStyleProvider.CreateHeroAvatarWidget(lblObj.transform, _net.GetPlayerAvatarHeroId(playerId), col, 18f, clickable: false);
                NativeStyleProvider.CreateLabel(lblObj.transform, CoopLoc.F("sketch.hint", _net.GetPlayerName(playerId)), 11.5f, col, TextAlignmentOptions.MidlineLeft, FontStyles.Bold, addShadow: true);

                trail.LabelObj = lblObj;
                trail.LabelRect = rt;
                trail.LabelGroup = cg;
            }

            _trails[playerId] = trail;
        }

        private static GameObject CreateArrowDecal(Vector3 pos, Vector3 forward, Sprite arrowSprite, float scale, out SpriteRenderer sr)
        {
            var root = new GameObject("EWCoop_SketchArrow");
            root.layer = 2;
            root.transform.position = pos;
            // Lay sprite flat on XZ plane and orient +Y (sprite up) along travel forward vector
            float yawDeg = Mathf.Atan2(forward.x, forward.z) * Mathf.Rad2Deg;
            root.transform.rotation = Quaternion.Euler(90f, yawDeg, 0f);
            root.transform.localScale = new Vector3(scale, scale, 1f);

            sr = root.AddComponent<SpriteRenderer>();
            sr.sprite = arrowSprite;
            sr.color = new Color(1f, 0.96f, 0.86f, 0.94f);
            sr.sortingOrder = 22;
            return root;
        }

        private static float NormalizeSpriteScale(Sprite sp, float targetWorldUnits)
        {
            if (sp == null) return targetWorldUnits;
            float maxDim = Mathf.Max(sp.bounds.size.x, sp.bounds.size.y);
            return maxDim > 0.001f ? targetWorldUnits / maxDim : targetWorldUnits;
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

                Color tileCol = new Color(tr.Color.r, tr.Color.g, tr.Color.b, 0.70f * pulse * fade);
                for (int r = 0; r < tr.TileRenderers.Count; r++)
                    if (tr.TileRenderers[r] != null) tr.TileRenderers[r].color = tileCol;

                Color arrowCol = new Color(1f, 0.96f, 0.86f, 0.94f * fade);
                for (int r = 0; r < tr.ChevronRenderers.Count; r++)
                    if (tr.ChevronRenderers[r] != null) tr.ChevronRenderers[r].color = arrowCol;

                if (tr.LabelRect != null && cam != null && tr.Points.Count > 0)
                {
                    Vector3 anchor = tr.Points[tr.Points.Count - 1] + new Vector3(0f, 1.15f, 0f);
                    Vector3 sp = cam.WorldToScreenPoint(anchor);
                    bool vis = sp.z > 0f && sp.x > -100f && sp.x < Screen.width + 100f && sp.y > -100f && sp.y < Screen.height + 100f;
                    if (tr.LabelObj.activeSelf != vis) tr.LabelObj.SetActive(vis);
                    if (vis) tr.LabelRect.anchoredPosition = NativeStyleProvider.ScreenToCanvasLocal(new Vector2(sp.x, sp.y));
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

        private static void DestroyTrail(ActiveTrail tr)
        {
            for (int i = 0; i < tr.TileVisuals.Count; i++)
                if (tr.TileVisuals[i] != null) UnityEngine.Object.Destroy(tr.TileVisuals[i]);
            for (int i = 0; i < tr.ChevronVisuals.Count; i++)
                if (tr.ChevronVisuals[i] != null) UnityEngine.Object.Destroy(tr.ChevronVisuals[i]);
            if (tr.LabelObj != null) UnityEngine.Object.Destroy(tr.LabelObj);
        }

        public void ClearAll()
        {
            EndLocalDrag();
            foreach (var kv in _trails) DestroyTrail(kv.Value);
            _trails.Clear();
        }
    }
}
