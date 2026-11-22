using System;
using System.Collections.Generic;
using System.IO;
using EmberwardCoop.Core;
using EmberwardCoop.Networking;
using EmberwardCoop.Serialization;
using EmberwardCoop.UI;
using HighlightPlus;
using Il2CppInterop.Runtime.InteropTypes.Arrays;
using UnityEngine;

namespace EmberwardCoop.Sync
{
    public sealed class PlayerContributionStats
    {
        public int PlayerId;
        public long DamageDealt;
        public int EnemiesDebuffed;
        public int PathLengthAdded;
        public int TowersBuilt;
        public int BlocksPlaced;
        public int GoldGifted;

        public void Reset()
        {
            DamageDealt = 0;
            EnemiesDebuffed = 0;
            PathLengthAdded = 0;
            TowersBuilt = 0;
            BlocksPlaced = 0;
            GoldGifted = 0;
        }

        public void Write(BinaryWriter w)
        {
            w.Write(PlayerId);
            w.Write(DamageDealt);
            w.Write(EnemiesDebuffed);
            w.Write(PathLengthAdded);
            w.Write(TowersBuilt);
            w.Write(BlocksPlaced);
            w.Write(GoldGifted);
        }

        public static PlayerContributionStats Read(BinaryReader r) => new PlayerContributionStats
        {
            PlayerId = r.ReadInt32(),
            DamageDealt = r.ReadInt64(),
            EnemiesDebuffed = r.ReadInt32(),
            PathLengthAdded = r.ReadInt32(),
            TowersBuilt = r.ReadInt32(),
            BlocksPlaced = r.ReadInt32(),
            GoldGifted = r.ReadInt32()
        };
    }

    public sealed class PlayerPeekSnapshot
    {
        public int PlayerId;
        public int Coin;
        public int Gems;
        public int Rerolls;
        public int RelicCount;
        public int[] LoadoutTowers = Array.Empty<int>();
        public int[] HandBlocks = Array.Empty<int>();
        public string ActiveModal = "";

        public void Write(BinaryWriter w)
        {
            w.Write(PlayerId);
            w.Write(Coin);
            w.Write(Gems);
            w.Write(Rerolls);
            w.Write(RelicCount);
            w.Write(LoadoutTowers?.Length ?? 0);
            if (LoadoutTowers != null) for (int i = 0; i < LoadoutTowers.Length; i++) w.Write(LoadoutTowers[i]);
            w.Write(HandBlocks?.Length ?? 0);
            if (HandBlocks != null) for (int i = 0; i < HandBlocks.Length; i++) w.Write(HandBlocks[i]);
            w.WriteSafeString(ActiveModal);
        }

        public static PlayerPeekSnapshot Read(BinaryReader r)
        {
            var s = new PlayerPeekSnapshot
            {
                PlayerId = r.ReadInt32(),
                Coin = r.ReadInt32(),
                Gems = r.ReadInt32(),
                Rerolls = r.ReadInt32(),
                RelicCount = r.ReadInt32()
            };
            int tc = r.ReadBoundedCount(64);
            s.LoadoutTowers = tc > 0 ? new int[tc] : Array.Empty<int>();
            for (int i = 0; i < tc; i++) s.LoadoutTowers[i] = r.ReadInt32();
            int hc = r.ReadBoundedCount(64);
            s.HandBlocks = hc > 0 ? new int[hc] : Array.Empty<int>();
            for (int i = 0; i < hc; i++) s.HandBlocks[i] = r.ReadInt32();
            s.ActiveModal = r.ReadString();
            return s;
        }
    }

    public sealed class OwnershipAndStatsTracker
    {
        private readonly NetworkManager _net;
        private readonly Func<SessionController> _session;

        private readonly Dictionary<int, int> _towerInstanceOwners = new Dictionary<int, int>();
        private readonly Dictionary<int, int> _blockInstanceOwners = new Dictionary<int, int>();
        private readonly Dictionary<int, string> _towerOwnerKeys = new Dictionary<int, string>();
        private readonly Dictionary<int, string> _blockOwnerKeys = new Dictionary<int, string>();
        private readonly Dictionary<int, ABaseTower> _towerInstances = new Dictionary<int, ABaseTower>();

        private int _nextTowerNetId = 1;
        private readonly Dictionary<int, int> _towerInstanceToNetId = new Dictionary<int, int>();
        private readonly Dictionary<int, ABaseTower> _netIdToTower = new Dictionary<int, ABaseTower>();

        // One pooled HighlightEffect per active color index keeps scene overhead flat across any tower count.
        private readonly Dictionary<int, HighlightEffect> _outlineEffectsByColor = new Dictionary<int, HighlightEffect>();
        private readonly Dictionary<int, GameObject> _fallbackGroundRings = new Dictionary<int, GameObject>();
        private bool _highlightEffectFailed;
        private bool _outlinesDirty;
        private float _delayedOutlineRefreshTime;
        private OwnershipDisplayMode _lastDisplayMode = OwnershipDisplayMode.Always;
        private int _lastHoverTargetId = -1;

        public readonly Dictionary<int, PlayerContributionStats> RunStats = new Dictionary<int, PlayerContributionStats>();
        public readonly Dictionary<int, PlayerPeekSnapshot> PeekSnapshots = new Dictionary<int, PlayerPeekSnapshot>();

        public event Action OnPeekUpdated;
        public event Action OnStatsUpdated;

        private float _lastPeekSendTime;
        private float _lastStatsSyncTime;
        private int _lastPathLength = -1;

        public OwnershipAndStatsTracker(NetworkManager net, Func<SessionController> session)
        {
            _net = net;
            _session = session;
        }

        public int AllocateTowerNetId() => _nextTowerNetId++;

        public int GetTowerNetId(ABaseTower tower)
        {
            if (tower == null) return 0;
            int id = tower.GetInstanceID();
            if (_towerInstanceToNetId.TryGetValue(id, out int existing) && existing > 0)
                return existing;
            if (_net.IsHost)
            {
                int allocated = AllocateTowerNetId();
                BindTowerNetId(tower, allocated);
                return allocated;
            }
            return 0;
        }

        public ABaseTower FindTowerByNetId(int netId)
        {
            if (netId <= 0) return null;
            if (_netIdToTower.TryGetValue(netId, out var tower) && tower != null)
                return tower;
            return null;
        }

        private void BindTowerNetId(ABaseTower tower, int netId)
        {
            if (tower == null || netId <= 0) return;
            int id = tower.GetInstanceID();
            if (_towerInstanceToNetId.TryGetValue(id, out int oldNetId) && oldNetId != netId)
                _netIdToTower.Remove(oldNetId);
            _towerInstanceToNetId[id] = netId;
            _netIdToTower[netId] = tower;
            if (netId >= _nextTowerNetId) _nextTowerNetId = netId + 1;
        }

        public void ResetStageEntities()
        {
            _towerInstanceOwners.Clear();
            _blockInstanceOwners.Clear();
            _towerOwnerKeys.Clear();
            _blockOwnerKeys.Clear();
            _towerInstances.Clear();
            _towerInstanceToNetId.Clear();
            _netIdToTower.Clear();
            _nextTowerNetId = 1;
            DestroyAllOutlines();
            _highlightEffectFailed = false;
            _outlinesDirty = false;
            _delayedOutlineRefreshTime = 0f;
            _lastHoverTargetId = -1;
            _lastPathLength = -1;
        }

        private void DestroyAllOutlines()
        {
            foreach (var fx in _outlineEffectsByColor.Values)
            {
                if (fx != null && fx.gameObject != null)
                {
                    try { fx.SetHighlighted(false); } catch { }
                    UnityEngine.Object.Destroy(fx.gameObject);
                }
            }
            _outlineEffectsByColor.Clear();

            foreach (var ring in _fallbackGroundRings.Values)
            {
                if (ring != null) UnityEngine.Object.Destroy(ring);
            }
            _fallbackGroundRings.Clear();
        }

        public void ResetRunStats()
        {
            ResetStageEntities();
            RunStats.Clear();
            PeekSnapshots.Clear();
            OnStatsUpdated?.Invoke();
        }

        public bool IsOwnerConnected(int ownerId)
        {
            if (ownerId <= 0) return true;
            return _net.Roster.ContainsKey(ownerId);
        }

        private string ResolveStableKey(int ownerId)
        {
            var peer = _net.GetPeer(ownerId);
            if (peer != null) return CoopSaveStorage.MakeStablePlayerKey(peer);
            return ownerId == 0 ? "host" : $"peer:{ownerId}";
        }

        public void RebindReconnectedPeer(PeerInfo peer)
        {
            if (peer == null) return;
            string key = CoopSaveStorage.MakeStablePlayerKey(peer);
            if (string.IsNullOrEmpty(key)) return;

            var blockIds = new List<int>(_blockOwnerKeys.Keys);
            for (int i = 0; i < blockIds.Count; i++)
            {
                int id = blockIds[i];
                if (string.Equals(_blockOwnerKeys[id], key, StringComparison.OrdinalIgnoreCase))
                    _blockInstanceOwners[id] = peer.PlayerId;
            }

            bool anyTowerRebound = false;
            var towerIds = new List<int>(_towerOwnerKeys.Keys);
            for (int i = 0; i < towerIds.Count; i++)
            {
                int id = towerIds[i];
                if (string.Equals(_towerOwnerKeys[id], key, StringComparison.OrdinalIgnoreCase))
                {
                    _towerInstanceOwners[id] = peer.PlayerId;
                    anyTowerRebound = true;
                }
            }

            if (anyTowerRebound)
                _outlinesDirty = true;
        }

        public PlayerContributionStats GetOrCreateStats(int playerId)
        {
            if (!RunStats.TryGetValue(playerId, out var st))
            {
                st = new PlayerContributionStats { PlayerId = playerId };
                RunStats[playerId] = st;
            }
            return st;
        }

        public void StampTowerOwner(ABaseTower tower, int ownerId, int towerNetId = 0)
        {
            if (tower == null) return;
            int id = tower.GetInstanceID();
            _towerInstanceOwners[id] = ownerId;
            _towerOwnerKeys[id] = ResolveStableKey(ownerId);
            _towerInstances[id] = tower;
            if (towerNetId > 0) BindTowerNetId(tower, towerNetId);
            else if (_net.IsHost && !_towerInstanceToNetId.ContainsKey(id)) BindTowerNetId(tower, AllocateTowerNetId());
            GetOrCreateStats(ownerId).TowersBuilt++;
            _outlinesDirty = true;
        }

        public void RegisterExistingTowerOwner(ABaseTower tower, int ownerId, int towerNetId = 0)
        {
            if (tower == null) return;
            int id = tower.GetInstanceID();
            _towerInstanceOwners[id] = ownerId;
            _towerOwnerKeys[id] = ResolveStableKey(ownerId);
            _towerInstances[id] = tower;
            if (towerNetId > 0) BindTowerNetId(tower, towerNetId);
            else if (_net.IsHost && !_towerInstanceToNetId.ContainsKey(id)) BindTowerNetId(tower, AllocateTowerNetId());
            _outlinesDirty = true;
        }

        public int GetTowerOwner(ABaseTower tower)
        {
            if (tower == null) return -1;
            return _towerInstanceOwners.TryGetValue(tower.GetInstanceID(), out int owner) ? owner : 0;
        }

        public void OnTowerUpgraded(ABaseTower tower)
        {
            if (tower == null) return;
            int id = tower.GetInstanceID();
            if (!_towerInstances.ContainsKey(id))
                _towerInstances[id] = tower;
            _outlinesDirty = true;
            // CR_UpgradeEffect swaps tower skin meshes after a short transition delay.
            _delayedOutlineRefreshTime = Time.unscaledTime + 0.42f;
        }

        public void UnregisterTower(ABaseTower tower)
        {
            if (tower == null) return;
            int id = tower.GetInstanceID();
            _towerInstanceOwners.Remove(id);
            _towerOwnerKeys.Remove(id);
            _towerInstances.Remove(id);
            if (_towerInstanceToNetId.TryGetValue(id, out int netId))
            {
                _towerInstanceToNetId.Remove(id);
                _netIdToTower.Remove(netId);
            }
            if (_fallbackGroundRings.TryGetValue(id, out var ring))
            {
                if (ring != null) UnityEngine.Object.Destroy(ring);
                _fallbackGroundRings.Remove(id);
            }
            _outlinesDirty = true;
        }

        public void StampBlockOwner(Obj_TetrisBlock block, int ownerId)
        {
            if (block == null) return;
            int id = block.GetInstanceID();
            _blockInstanceOwners[id] = ownerId;
            _blockOwnerKeys[id] = ResolveStableKey(ownerId);
            GetOrCreateStats(ownerId).BlocksPlaced++;
            RecordPathDeltaForPlayer(ownerId);
        }

        public void RegisterExistingBlockOwner(Obj_TetrisBlock block, int ownerId)
        {
            if (block == null) return;
            int id = block.GetInstanceID();
            _blockInstanceOwners[id] = ownerId;
            _blockOwnerKeys[id] = ResolveStableKey(ownerId);
        }

        public int GetBlockOwner(Obj_TetrisBlock block)
        {
            if (block == null) return -1;
            return _blockInstanceOwners.TryGetValue(block.GetInstanceID(), out int owner) ? owner : 0;
        }

        public void UnregisterBlock(Obj_TetrisBlock block)
        {
            if (block == null) return;
            int id = block.GetInstanceID();
            _blockInstanceOwners.Remove(id);
            _blockOwnerKeys.Remove(id);
        }

        public void RefreshOwnershipRingColors()
        {
            _outlinesDirty = true;
        }

        private HighlightEffect GetOrCreateOutlineEffect(int colorIndex)
        {
            if (_outlineEffectsByColor.TryGetValue(colorIndex, out var existing) && existing != null)
                return existing;

            try
            {
                var go = new GameObject($"EWCoop_TowerOutline_C{colorIndex}");
                var fx = go.AddComponent<HighlightEffect>();
                fx.effectGroup = TargetOptions.Scripting;
                fx.outline = 0.90f;
                fx.outlineColor = CoopConfig.GetPlayerColor(colorIndex);
                fx.outlineColorStyle = ColorStyle.SingleColor;
                fx.outlineWidth = 0.34f;
                fx.outlineQuality = HighlightPlus.QualityLevel.High;
                fx.outlineVisibility = Visibility.Normal;
                fx.outlineContourStyle = ContourStyle.AroundObjectShape;
                fx.glow = 0f;
                fx.overlay = 0f;
                fx.innerGlow = 0f;
                fx.targetFX = false;
                fx.iconFX = false;
                fx.labelEnabled = false;
                fx.seeThrough = SeeThroughMode.Never;
                fx.fadeInDuration = 0f;
                fx.fadeOutDuration = 0f;
                fx.sortingPriority = -1;
                fx.highlighted = false;
                _outlineEffectsByColor[colorIndex] = fx;
                return fx;
            }
            catch (Exception ex)
            {
                _highlightEffectFailed = true;
                CoopLogger.Warn(LogCategory.Sync, $"HighlightEffect tower outline unavailable; using ground ring fallback: {ex.Message}");
                return null;
            }
        }

        private static bool IsValidTowerOutlineRenderer(Renderer r, ABaseTower tower)
        {
            if (r == null || !r.enabled || !r.gameObject.activeInHierarchy) return false;
            if (tower.renderer_FogOfWar != null && r == tower.renderer_FogOfWar) return false;
            if (r.TryCast<ParticleSystemRenderer>() != null) return false;
            if (r.TryCast<TrailRenderer>() != null) return false;
            if (r.TryCast<LineRenderer>() != null) return false;
            if (r.TryCast<SpriteRenderer>() != null) return false;

            string n = r.gameObject.name;
            if (!string.IsNullOrEmpty(n))
            {
                if (n.IndexOf("Range", StringComparison.OrdinalIgnoreCase) >= 0 ||
                    n.IndexOf("Preview", StringComparison.OrdinalIgnoreCase) >= 0 ||
                    n.IndexOf("Shadow", StringComparison.OrdinalIgnoreCase) >= 0 ||
                    n.IndexOf("Indicator", StringComparison.OrdinalIgnoreCase) >= 0 ||
                    n.IndexOf("Fog", StringComparison.OrdinalIgnoreCase) >= 0 ||
                    n.IndexOf("VFX", StringComparison.OrdinalIgnoreCase) >= 0 ||
                    n.IndexOf("EWCoop_", StringComparison.OrdinalIgnoreCase) >= 0)
                    return false;
            }

            return r.TryCast<MeshRenderer>() != null || r.TryCast<SkinnedMeshRenderer>() != null;
        }

        private static void CollectTowerOutlineRenderers(ABaseTower tower, List<Renderer> output, HashSet<int> seenIds)
        {
            if (tower == null) return;
            try
            {
                var knownList = tower.List_TowerRenderers;
                if (knownList != null)
                {
                    for (int i = 0; i < knownList.Count; i++)
                    {
                        var r = knownList[i];
                        if (IsValidTowerOutlineRenderer(r, tower) && seenIds.Add(r.GetInstanceID()))
                            output.Add(r);
                    }
                }

                var primary = tower.Renderer_Tower;
                if (IsValidTowerOutlineRenderer(primary, tower) && seenIds.Add(primary.GetInstanceID()))
                    output.Add(primary);

                var children = tower.GetComponentsInChildren<Renderer>(false);
                if (children != null)
                {
                    for (int i = 0; i < children.Length; i++)
                    {
                        var r = children[i];
                        if (IsValidTowerOutlineRenderer(r, tower) && seenIds.Add(r.GetInstanceID()))
                            output.Add(r);
                    }
                }
            }
            catch { }
        }

        private int ResolveHoveredOrInspectedTowerId()
        {
            var curTower = GameRefs.GetTowerControl()?.curTower;
            if (curTower != null)
                return curTower.GetInstanceID();

            if (Camera.main != null)
            {
                Ray ray = Camera.main.ScreenPointToRay(Input.mousePosition);
                Plane ground = new Plane(Vector3.up, Vector3.zero);
                if (ground.Raycast(ray, out float dist))
                {
                    var nearTower = GameRefs.FindTowerAt(ray.GetPoint(dist), 1.15f);
                    if (nearTower != null) return nearTower.GetInstanceID();
                }
            }
            return -1;
        }

        private void UpdateTowerOutlines(float now)
        {
            if (!GameRefs.IsInBattle)
            {
                if (_outlineEffectsByColor.Count > 0 || _fallbackGroundRings.Count > 0)
                    DestroyAllOutlines();
                return;
            }

            if (_delayedOutlineRefreshTime > 0f && now >= _delayedOutlineRefreshTime)
            {
                _delayedOutlineRefreshTime = 0f;
                _outlinesDirty = true;
            }

            var mode = CoopConfig.OwnershipIndicatorMode?.Value ?? OwnershipDisplayMode.Always;
            if (mode != _lastDisplayMode)
            {
                _lastDisplayMode = mode;
                _outlinesDirty = true;
            }

            int hoverTargetId = -1;
            if (mode == OwnershipDisplayMode.HoverOnly)
            {
                hoverTargetId = ResolveHoveredOrInspectedTowerId();
                if (hoverTargetId != _lastHoverTargetId)
                {
                    _lastHoverTargetId = hoverTargetId;
                    _outlinesDirty = true;
                }
            }
            else if (_lastHoverTargetId != -1)
            {
                _lastHoverTargetId = -1;
                _outlinesDirty = true;
            }

            if (!_outlinesDirty) return;
            _outlinesDirty = false;

            if (_highlightEffectFailed)
            {
                RebuildFallbackGroundRings(mode, hoverTargetId);
                return;
            }

            if (mode == OwnershipDisplayMode.Off || _towerInstances.Count == 0)
            {
                foreach (var fx in _outlineEffectsByColor.Values)
                {
                    if (fx != null && fx.highlighted)
                        fx.SetHighlighted(false);
                }
                return;
            }

            var byColor = new Dictionary<int, List<Renderer>>();
            var staleIds = new List<int>();

            foreach (var kv in _towerInstances)
            {
                int id = kv.Key;
                var tower = kv.Value;
                if (tower == null || tower.gameObject == null)
                {
                    staleIds.Add(id);
                    continue;
                }
                if (!tower.gameObject.activeInHierarchy) continue;
                if (mode == OwnershipDisplayMode.HoverOnly && id != hoverTargetId) continue;

                int ownerId = _towerInstanceOwners.TryGetValue(id, out int oid) ? oid : 0;
                int colorIdx = _net.GetPlayerColorIndex(ownerId);
                if (!byColor.TryGetValue(colorIdx, out var list))
                {
                    list = new List<Renderer>();
                    byColor[colorIdx] = list;
                }

                var seen = new HashSet<int>();
                CollectTowerOutlineRenderers(tower, list, seen);
            }

            for (int i = 0; i < staleIds.Count; i++)
            {
                int sid = staleIds[i];
                _towerInstances.Remove(sid);
                _towerInstanceOwners.Remove(sid);
                _towerOwnerKeys.Remove(sid);
                if (_towerInstanceToNetId.TryGetValue(sid, out int nid))
                {
                    _towerInstanceToNetId.Remove(sid);
                    _netIdToTower.Remove(nid);
                }
            }

            foreach (var kv in byColor)
            {
                int colorIdx = kv.Key;
                var list = kv.Value;
                if (list.Count == 0) continue;

                var fx = GetOrCreateOutlineEffect(colorIdx);
                if (fx == null)
                {
                    if (_highlightEffectFailed)
                        RebuildFallbackGroundRings(mode, hoverTargetId);
                    return;
                }

                try
                {
                    var arr = new Il2CppReferenceArray<Renderer>(list.Count);
                    for (int i = 0; i < list.Count; i++) arr[i] = list[i];

                    fx.outlineColor = CoopConfig.GetPlayerColor(colorIdx);
                    fx.outline = 0.90f;
                    fx.outlineWidth = 0.34f;
                    fx.SetTargets(fx.transform, arr);
                    fx.UpdateMaterialProperties(false);
                    fx.SetHighlighted(true);
                    fx.Refresh(false);
                }
                catch (Exception ex)
                {
                    _highlightEffectFailed = true;
                    CoopLogger.Warn(LogCategory.Sync, $"HighlightEffect update failed; switching to ground ring fallback: {ex.Message}");
                    DestroyAllOutlines();
                    RebuildFallbackGroundRings(mode, hoverTargetId);
                    return;
                }
            }

            foreach (var kv in _outlineEffectsByColor)
            {
                if (kv.Value == null) continue;
                bool active = byColor.TryGetValue(kv.Key, out var list) && list.Count > 0;
                if (!active && kv.Value.highlighted)
                    kv.Value.SetHighlighted(false);
            }
        }

        private void RebuildFallbackGroundRings(OwnershipDisplayMode mode, int hoverTargetId)
        {
            if (mode == OwnershipDisplayMode.Off)
            {
                foreach (var r in _fallbackGroundRings.Values)
                    if (r != null && r.activeSelf) r.SetActive(false);
                return;
            }

            Sprite ringSprite = NativeAssetRegistry.GetPingRingSprite();
            foreach (var kv in _towerInstances)
            {
                int id = kv.Key;
                var tower = kv.Value;
                if (tower == null) continue;

                int ownerId = _towerInstanceOwners.TryGetValue(id, out int oid) ? oid : 0;
                Color col = CoopConfig.GetPlayerColor(_net.GetPlayerColorIndex(ownerId));
                bool vis = mode == OwnershipDisplayMode.Always || id == hoverTargetId;

                if (!_fallbackGroundRings.TryGetValue(id, out var ringGo) || ringGo == null)
                {
                    ringGo = new GameObject($"EWCoop_OwnerOutlineFallback_{ownerId}");
                    ringGo.layer = 2;
                    ringGo.transform.SetParent(tower.transform, false);
                    ringGo.transform.localPosition = new Vector3(0f, 0.05f, 0f);
                    ringGo.transform.localRotation = Quaternion.Euler(90f, 0f, 0f);

                    var szType = tower.SettingData != null ? tower.SettingData.TowerSizeType : eTowerSizeType._1x1;
                    float dia = szType switch
                    {
                        eTowerSizeType._3x3 or eTowerSizeType._3x3_Cross or eTowerSizeType._3x3_CornerOnly or eTowerSizeType._3x3_SideOnly => 2.85f,
                        eTowerSizeType._2x2 or eTowerSizeType._2x3_LShape => 1.92f,
                        eTowerSizeType._1x2 or eTowerSizeType._1x3 or eTowerSizeType._1x5 => 1.50f,
                        _ => 1.05f
                    };
                    ringGo.transform.localScale = new Vector3(dia, dia, 1f);
                    var sr = ringGo.AddComponent<SpriteRenderer>();
                    sr.sprite = ringSprite;
                    sr.sortingOrder = 5;
                    _fallbackGroundRings[id] = ringGo;
                }

                var renderer = ringGo.GetComponent<SpriteRenderer>();
                if (renderer != null)
                    renderer.color = new Color(col.r, col.g, col.b, 0.80f);
                if (ringGo.activeSelf != vis)
                    ringGo.SetActive(vis);
            }
        }

        public void RecordDamageByTower(ABaseTower tower, int damage)
        {
            if (tower == null || damage <= 0 || !_net.IsHost) return;
            int owner = GetTowerOwner(tower);
            if (owner < 0) owner = 0;
            GetOrCreateStats(owner).DamageDealt += damage;
        }

        public void RecordDebuffApplied(ABaseTower fromTower)
        {
            if (!_net.IsHost) return;
            int owner = fromTower != null ? GetTowerOwner(fromTower) : 0;
            if (owner < 0) owner = 0;
            GetOrCreateStats(owner).EnemiesDebuffed++;
        }

        public void RecordGoldGifted(int senderId, int amount)
        {
            if (amount <= 0) return;
            GetOrCreateStats(senderId).GoldGifted += amount;
            OnStatsUpdated?.Invoke();
        }

        public void SampleBaselinePathLength()
        {
            try
            {
                var pts = MapManager.Instance?.GetAllPathPoints();
                if (pts != null) _lastPathLength = pts.Count;
            }
            catch { }
        }

        private void RecordPathDeltaForPlayer(int playerId)
        {
            if (!_net.IsHost) return;
            try
            {
                var pts = MapManager.Instance?.GetAllPathPoints();
                if (pts == null) return;
                int curLen = pts.Count;
                if (_lastPathLength > 0 && curLen > _lastPathLength)
                {
                    GetOrCreateStats(playerId).PathLengthAdded += (curLen - _lastPathLength);
                }
                _lastPathLength = curLen;
            }
            catch { }
        }

        public void Tick(float now)
        {
            if (!_net.IsConnected) return;

            UpdateTowerOutlines(now);

            if (now - _lastPeekSendTime >= 1.25f)
            {
                _lastPeekSendTime = now;
                SendLocalPeekSnapshot();
            }

            if (_net.IsHost && _net.RemotePeerCount > 0 && now - _lastStatsSyncTime >= 3.0f)
            {
                _lastStatsSyncTime = now;
                BroadcastRunStats();
            }
        }

        private void SendLocalPeekSnapshot()
        {
            var gd = GameDataManager.instance?.GameplayData;
            var ig = MainGame.Instance?.IngameData;

            var snap = new PlayerPeekSnapshot
            {
                PlayerId = _net.LocalPlayerId,
                Coin = ig != null ? ig.Coin : 0,
                Gems = gd != null ? gd.gem : 0,
                Rerolls = gd != null ? gd.rerollChance : 0,
                RelicCount = gd?.list_CollectedRelic?.Count ?? 0,
                ActiveModal = _session()?.Modals.GetLocalTopModalBadge() ?? ""
            };

            if (gd?.list_LoadoutTowerData != null)
            {
                int n = Math.Min(8, gd.list_LoadoutTowerData.Count);
                snap.LoadoutTowers = new int[n];
                for (int i = 0; i < n; i++) snap.LoadoutTowers[i] = (int)gd.list_LoadoutTowerData[i].ItemType;
            }

            if (ig?.list_HandCardData != null)
            {
                int n = Math.Min(8, ig.list_HandCardData.Count);
                snap.HandBlocks = new int[n];
                for (int i = 0; i < n; i++) snap.HandBlocks[i] = (int)ig.list_HandCardData[i].ItemType;
            }

            PeekSnapshots[snap.PlayerId] = snap;
            _net.SendToAllPeersOrHost(PacketType.PlayerPeekUpdate, w => snap.Write(w), DeliveryMode.Unreliable);
        }

        public void HandlePeekUpdate(int senderId, BinaryReader r)
        {
            var snap = PlayerPeekSnapshot.Read(r);
            if (_net.IsHost) snap.PlayerId = senderId;
            PeekSnapshots[snap.PlayerId] = snap;

            if (_net.IsHost)
            {
                _net.SetPeerModal(snap.PlayerId, snap.ActiveModal);
                _net.BroadcastToClients(PacketType.PlayerPeekUpdate, w => snap.Write(w), DeliveryMode.Unreliable, excludePeerId: senderId);
            }
            OnPeekUpdated?.Invoke();
        }

        public void BroadcastRunStats()
        {
            if (!_net.IsHost) return;
            _net.BroadcastToClients(PacketType.PlayerStatsSync, w =>
            {
                w.Write(RunStats.Count);
                foreach (var st in RunStats.Values) st.Write(w);
            });
            OnStatsUpdated?.Invoke();
        }

        public void HandleRunStatsSync(BinaryReader r)
        {
            int count = r.ReadBoundedCount(32);
            RunStats.Clear();
            for (int i = 0; i < count; i++)
            {
                var st = PlayerContributionStats.Read(r);
                RunStats[st.PlayerId] = st;
            }
            OnStatsUpdated?.Invoke();
        }
    }
}
