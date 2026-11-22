using System;
using System.Collections.Generic;
using System.IO;
using EmberwardCoop.Core;
using EmberwardCoop.Localization;
using EmberwardCoop.Networking;
using EmberwardCoop.Rules;
using EmberwardCoop.Serialization;
using UnityEngine;

namespace EmberwardCoop.Sync
{
    public sealed class CoopBalanceManager
    {
        private readonly NetworkManager _net;
        private readonly Func<SessionController> _session;

        private readonly Dictionary<int, int> _blocksPlacedByPlayer = new Dictionary<int, int>();
        private readonly Dictionary<string, int> _blocksPlacedByStableKey = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);

        // Consumed once when MainGame finishes loading a saved co-op run.
        public Dictionary<int, int> PendingRestoreBlockCounts;

        public event Action OnTurnBlockCountChanged;

        private bool _waveDensityAppliedForStage;

        public CoopBalanceManager(NetworkManager net, Func<SessionController> session)
        {
            _net = net;
            _session = session;
        }

        public void ResetForNewStage()
        {
            _blocksPlacedByPlayer.Clear();
            _blocksPlacedByStableKey.Clear();
            _waveDensityAppliedForStage = false;
            OnTurnBlockCountChanged?.Invoke();
        }

        public void ResetForNewTurn()
        {
            _blocksPlacedByPlayer.Clear();
            _blocksPlacedByStableKey.Clear();
            if (_net.IsHost) BroadcastTurnBlockCount();
            OnTurnBlockCountChanged?.Invoke();
        }

        public void ClearPendingRestoreCounts()
        {
            PendingRestoreBlockCounts = null;
        }

        public bool IsBlockCapActive()
        {
            var s = _session();
            if (s == null || !_net.IsConnected) return false;
            return s.Settings.BlockPacing == BlockPacingRule.SharedTurnBlockCap;
        }

        public int GetEffectiveTurnBlockCap()
        {
            var s = _session();
            return s != null ? Math.Max(1, s.Settings.SharedTurnBlockCap) : 3;
        }

        public int GetBlocksPlacedByPlayer(int playerId)
            => _blocksPlacedByPlayer.TryGetValue(playerId, out int count) ? Math.Max(0, count) : 0;

        public int GetRemainingBlocksForPlayer(int playerId)
            => Math.Max(0, GetEffectiveTurnBlockCap() - GetBlocksPlacedByPlayer(playerId));

        private string ResolveStableKey(int playerId)
        {
            var peer = _net.GetPeer(playerId);
            if (peer != null) return CoopSaveStorage.MakeStablePlayerKey(peer);
            return playerId == 0 ? "host" : "";
        }

        public void SetPlayerTurnBlockCount(int playerId, int count)
        {
            int clamped = Math.Max(0, count);
            _blocksPlacedByPlayer[playerId] = clamped;
            string key = ResolveStableKey(playerId);
            if (!string.IsNullOrEmpty(key))
                _blocksPlacedByStableKey[key] = clamped;
        }

        public void RebindReconnectedPeer(PeerInfo peer)
        {
            if (peer == null) return;
            string key = CoopSaveStorage.MakeStablePlayerKey(peer);
            if (string.IsNullOrEmpty(key)) return;
            if (_blocksPlacedByStableKey.TryGetValue(key, out int prevCount))
            {
                _blocksPlacedByPlayer[peer.PlayerId] = Math.Max(0, prevCount);
                if (_net.IsHost) BroadcastTurnBlockCount();
                OnTurnBlockCountChanged?.Invoke();
            }
        }

        public void ApplyPendingRestoredBlockCounts()
        {
            if (PendingRestoreBlockCounts == null) return;
            var pending = PendingRestoreBlockCounts;
            PendingRestoreBlockCounts = null;

            _blocksPlacedByPlayer.Clear();
            _blocksPlacedByStableKey.Clear();
            foreach (var kv in pending)
                SetPlayerTurnBlockCount(kv.Key, kv.Value);

            if (_net.IsHost) BroadcastTurnBlockCount();
            OnTurnBlockCountChanged?.Invoke();
        }

        public bool CanPlaceBlockNow(int playerId, out string denyReason)
        {
            denyReason = "";
            if (!IsBlockCapActive()) return true;
            int cap = GetEffectiveTurnBlockCap();
            int placed = GetBlocksPlacedByPlayer(playerId);
            if (placed >= cap)
            {
                denyReason = CoopLoc.F("hud.block_limit_reached", placed, cap);
                return false;
            }
            return true;
        }

        public void IncrementTurnBlockCount(int playerId)
        {
            if (!IsBlockCapActive()) return;
            SetPlayerTurnBlockCount(playerId, GetBlocksPlacedByPlayer(playerId) + 1);
            if (_net.IsHost) BroadcastTurnBlockCount();
            OnTurnBlockCountChanged?.Invoke();
        }

        public void DecrementTurnBlockCount(int playerId)
        {
            if (!IsBlockCapActive()) return;
            int cur = GetBlocksPlacedByPlayer(playerId);
            if (cur > 0)
                SetPlayerTurnBlockCount(playerId, cur - 1);
            if (_net.IsHost) BroadcastTurnBlockCount();
            OnTurnBlockCountChanged?.Invoke();
        }

        public void BroadcastTurnBlockCount()
        {
            if (!_net.IsHost) return;
            _net.BroadcastToClients(PacketType.EvtTurnBlockCount, w =>
            {
                w.Write(_blocksPlacedByPlayer.Count);
                foreach (var kv in _blocksPlacedByPlayer)
                {
                    w.Write(kv.Key);
                    w.Write(kv.Value);
                }
            });
        }

        public void HandleEvtTurnBlockCount(BinaryReader r)
        {
            int count = r.ReadBoundedCount(32);
            _blocksPlacedByPlayer.Clear();
            for (int i = 0; i < count; i++)
            {
                int playerId = r.ReadInt32();
                int placed = Math.Max(0, r.ReadInt32());
                SetPlayerTurnBlockCount(playerId, placed);
            }
            OnTurnBlockCountChanged?.Invoke();
        }

        public void ApplyWaveDensityScaling(StageDataReader reader)
        {
            var s = _session();
            if (s == null || !_net.IsConnected || reader == null || _waveDensityAppliedForStage) return;

            float mult = s.Settings.WaveDensityMultiplier;
            if (Mathf.Approximately(mult, 1.0f) || mult <= 1.01f) return;

            var stageData = reader.stageData;
            if (stageData?.list_WaveData == null) return;

            _waveDensityAppliedForStage = true;
            try
            {
                for (int w = 0; w < stageData.list_WaveData.Count; w++)
                {
                    var wd = stageData.list_WaveData[w];
                    if (wd?.list_SpawnData == null) continue;
                    for (int i = 0; i < wd.list_SpawnData.Count; i++)
                    {
                        var sd = wd.list_SpawnData[i];
                        if (sd == null) continue;
                        // Keep single-count boss spawns intact; scale regular wave groups.
                        if (sd.waveCount > 1)
                        {
                            int newWaveCount = Math.Max(sd.waveCount, Mathf.RoundToInt(sd.waveCount * mult));
                            sd.OverrideWaveCount(newWaveCount);
                        }
                        else if (sd.countForEachSpawn > 1)
                        {
                            int newEach = Math.Max(sd.countForEachSpawn, Mathf.RoundToInt(sd.countForEachSpawn * mult));
                            sd.OverrideCountForEachSpawn(newEach);
                        }
                    }
                }
                CoopLogger.Info(LogCategory.Sync, $"Applied Co-op Wave Density scaling ({mult:0.00}x) deterministically.");
            }
            catch (Exception ex)
            {
                CoopLogger.Debug(LogCategory.Sync, $"WaveDensity scaling warning: {ex.Message}");
            }
        }

        public void ApplyMonsterScaling(AMonsterBase monster)
        {
            var s = _session();
            if (s == null || !_net.IsConnected || monster == null) return;

            var settings = s.Settings;
            var size = monster.MonsterData != null ? monster.MonsterData.GetMonsterSize() : eMonsterSize.MEDIUM;
            bool isBoss = size == eMonsterSize.BOSS;
            bool isLarge = size == eMonsterSize.LARGE;

            float hpMult = settings.EnemyHpMultiplier;
            if (isBoss) hpMult *= settings.BossHpArmorMultiplier / Math.Max(1.0f, settings.EnemyHpMultiplier * 0.85f);
            else if (isLarge) hpMult *= Mathf.Lerp(1.0f, settings.BossHpArmorMultiplier, 0.35f);

            if (hpMult > 1.01f)
            {
                int baseMax = Math.Max(1, monster.GetMaxHP(true));
                int targetMax = Math.Max(baseMax, Mathf.RoundToInt(baseMax * hpMult));
                monster.OverrideMaxHP(targetMax);
                monster.HealToMaxHP();

                if (isBoss && settings.BossHpArmorMultiplier >= 1.35f)
                {
                    int armorShield = Mathf.RoundToInt(targetMax * 0.12f * (settings.BossHpArmorMultiplier - 1.0f));
                    if (armorShield > 0)
                    {
                        try { monster.ApplyCrystalizeEffect(armorShield); } catch { }
                    }
                }
            }

            float spdMult = settings.EnemySpeedMultiplier;
            if (!Mathf.Approximately(spdMult, 1.0f))
            {
                try
                {
                    monster.ApplySpeedModifier(spdMult, 9999f, false, 77420);
                }
                catch { }
            }
        }

        public int ApplyCoreHpBonus(int baseHp)
        {
            var s = _session();
            if (s == null || !_net.IsConnected) return baseHp;
            if (s.Settings.CoreHp != CoreHpRule.ScaledBonusCore) return baseHp;
            int extraPlayers = Math.Max(0, _net.Roster.Count - 1);
            return Mathf.RoundToInt(baseHp * (1f + extraPlayers * 0.25f));
        }
    }
}
