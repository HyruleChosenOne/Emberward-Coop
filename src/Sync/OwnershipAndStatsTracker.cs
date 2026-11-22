using System;
using System.Collections.Generic;
using System.IO;
using EmberwardCoop.Core;
using EmberwardCoop.Networking;
using EmberwardCoop.Serialization;
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
        private readonly Dictionary<int, GameObject> _towerRings = new Dictionary<int, GameObject>();
        private readonly Dictionary<int, Material> _ringMaterialsByColor = new Dictionary<int, Material>();

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

        public void ResetStageEntities()
        {
            _towerInstanceOwners.Clear();
            _blockInstanceOwners.Clear();
            _towerOwnerKeys.Clear();
            _blockOwnerKeys.Clear();
            _towerInstances.Clear();
            foreach (var ring in _towerRings.Values)
                if (ring != null) UnityEngine.Object.Destroy(ring);
            _towerRings.Clear();
            foreach (var mat in _ringMaterialsByColor.Values)
                if (mat != null) UnityEngine.Object.Destroy(mat);
            _ringMaterialsByColor.Clear();
            _lastPathLength = -1;
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

            var towerIds = new List<int>(_towerOwnerKeys.Keys);
            for (int i = 0; i < towerIds.Count; i++)
            {
                int id = towerIds[i];
                if (string.Equals(_towerOwnerKeys[id], key, StringComparison.OrdinalIgnoreCase))
                {
                    _towerInstanceOwners[id] = peer.PlayerId;
                    if (_towerRings.TryGetValue(id, out var oldRing) && oldRing != null)
                        UnityEngine.Object.Destroy(oldRing);
                    _towerRings.Remove(id);
                    if (_towerInstances.TryGetValue(id, out var tower) && tower != null)
                        EnsureOwnershipRing(tower, peer.PlayerId);
                }
            }
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

        public void StampTowerOwner(ABaseTower tower, int ownerId)
        {
            if (tower == null) return;
            int id = tower.GetInstanceID();
            _towerInstanceOwners[id] = ownerId;
            _towerOwnerKeys[id] = ResolveStableKey(ownerId);
            _towerInstances[id] = tower;
            GetOrCreateStats(ownerId).TowersBuilt++;
            EnsureOwnershipRing(tower, ownerId);
        }

        public void RegisterExistingTowerOwner(ABaseTower tower, int ownerId)
        {
            if (tower == null) return;
            int id = tower.GetInstanceID();
            _towerInstanceOwners[id] = ownerId;
            _towerOwnerKeys[id] = ResolveStableKey(ownerId);
            _towerInstances[id] = tower;
            EnsureOwnershipRing(tower, ownerId);
        }

        public int GetTowerOwner(ABaseTower tower)
        {
            if (tower == null) return -1;
            return _towerInstanceOwners.TryGetValue(tower.GetInstanceID(), out int owner) ? owner : 0;
        }

        public void UnregisterTower(ABaseTower tower)
        {
            if (tower == null) return;
            int id = tower.GetInstanceID();
            _towerInstanceOwners.Remove(id);
            _towerOwnerKeys.Remove(id);
            _towerInstances.Remove(id);
            if (_towerRings.TryGetValue(id, out var ring))
            {
                if (ring != null) UnityEngine.Object.Destroy(ring);
                _towerRings.Remove(id);
            }
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

        private Material GetOrCreateRingMaterial(int colorIndex)
        {
            if (_ringMaterialsByColor.TryGetValue(colorIndex, out var cached) && cached != null)
                return cached;

            var shader = Shader.Find("Universal Render Pipeline/Unlit") ?? Shader.Find("Sprites/Default") ?? Shader.Find("Unlit/Color");
            if (shader == null) return null;

            Color col = CoopConfig.GetPlayerColor(colorIndex);
            var mat = new Material(shader);
            Color ringCol = new Color(col.r, col.g, col.b, 0.58f);
            if (mat.HasProperty("_BaseColor")) mat.SetColor("_BaseColor", ringCol);
            if (mat.HasProperty("_Color")) mat.SetColor("_Color", ringCol);
            if (mat.HasProperty("_Surface")) mat.SetFloat("_Surface", 1f);
            if (mat.HasProperty("_SrcBlend")) mat.SetInt("_SrcBlend", (int)UnityEngine.Rendering.BlendMode.SrcAlpha);
            if (mat.HasProperty("_DstBlend")) mat.SetInt("_DstBlend", (int)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
            if (mat.HasProperty("_ZWrite")) mat.SetInt("_ZWrite", 0);
            mat.renderQueue = 3050;
            _ringMaterialsByColor[colorIndex] = mat;
            return mat;
        }

        private void EnsureOwnershipRing(ABaseTower tower, int ownerId)
        {
            var session = _session();
            if (session == null || !session.Ruleset.EnforcesEntityOwnership) return;

            int id = tower.GetInstanceID();
            if (_towerRings.TryGetValue(id, out var existing) && existing != null) return;

            try
            {
                var ring = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
                ring.name = $"EWCoop_OwnerRing_{ownerId}";
                ring.layer = 2;
                var c = ring.GetComponent<Collider>();
                if (c != null) UnityEngine.Object.Destroy(c);

                ring.transform.SetParent(tower.transform, false);
                var szType = tower.SettingData != null ? tower.SettingData.TowerSizeType : eTowerSizeType._1x1;
                float dia = szType switch
                {
                    eTowerSizeType._3x3 or eTowerSizeType._3x3_Cross or eTowerSizeType._3x3_CornerOnly or eTowerSizeType._3x3_SideOnly => 2.75f,
                    eTowerSizeType._2x2 or eTowerSizeType._2x3_LShape => 1.85f,
                    eTowerSizeType._1x2 or eTowerSizeType._1x3 or eTowerSizeType._1x5 => 1.45f,
                    _ => 0.96f
                };
                ring.transform.localPosition = new Vector3(0f, 0.04f, 0f);
                ring.transform.localScale = new Vector3(dia, 0.015f, dia);

                var mat = GetOrCreateRingMaterial(_net.GetPlayerColorIndex(ownerId));
                var rdr = ring.GetComponent<Renderer>();
                if (rdr != null && mat != null)
                {
                    rdr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                    rdr.receiveShadows = false;
                    rdr.sharedMaterial = mat;
                }
                _towerRings[id] = ring;
            }
            catch { }
        }

        public void RefreshOwnershipRingColors()
        {
            foreach (var kv in _towerRings)
            {
                if (kv.Value == null || !_towerInstanceOwners.TryGetValue(kv.Key, out int ownerId)) continue;
                var mat = GetOrCreateRingMaterial(_net.GetPlayerColorIndex(ownerId));
                var rdr = kv.Value.GetComponent<Renderer>();
                if (rdr != null && mat != null) rdr.sharedMaterial = mat;
            }
        }

        private void UpdateOwnershipRingsVisibility()
        {
            if (_towerRings.Count == 0) return;
            var mode = CoopConfig.OwnershipIndicatorMode?.Value ?? OwnershipDisplayMode.Always;
            if (mode == OwnershipDisplayMode.Always)
            {
                foreach (var kv in _towerRings)
                    if (kv.Value != null && !kv.Value.activeSelf) kv.Value.SetActive(true);
                return;
            }
            if (mode == OwnershipDisplayMode.Off)
            {
                foreach (var kv in _towerRings)
                    if (kv.Value != null && kv.Value.activeSelf) kv.Value.SetActive(false);
                return;
            }

            int hoveredId = -1;
            var curTower = GameRefs.GetTowerControl()?.curTower;
            if (curTower != null)
                hoveredId = curTower.GetInstanceID();

            if (hoveredId < 0 && Camera.main != null)
            {
                Ray ray = Camera.main.ScreenPointToRay(Input.mousePosition);
                Plane ground = new Plane(Vector3.up, Vector3.zero);
                if (ground.Raycast(ray, out float dist))
                {
                    var nearTower = GameRefs.FindTowerAt(ray.GetPoint(dist), 1.15f);
                    if (nearTower != null) hoveredId = nearTower.GetInstanceID();
                }
            }

            foreach (var kv in _towerRings)
            {
                if (kv.Value == null) continue;
                bool vis = kv.Key == hoveredId;
                if (kv.Value.activeSelf != vis) kv.Value.SetActive(vis);
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

            UpdateOwnershipRingsVisibility();

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
