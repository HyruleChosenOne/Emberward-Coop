using System;
using System.IO;
using EmberwardCoop.Core;
using EmberwardCoop.Networking;
using EmberwardCoop.Rules;
using UnityEngine;

namespace EmberwardCoop.Sync
{
    public sealed class CoopBalanceManager
    {
        private readonly NetworkManager _net;
        private readonly Func<SessionController> _session;

        public int BlocksPlacedThisTurn { get; private set; }
        public event Action OnTurnBlockCountChanged;

        private bool _waveDensityAppliedForStage;

        public CoopBalanceManager(NetworkManager net, Func<SessionController> session)
        {
            _net = net;
            _session = session;
        }

        public void ResetForNewStage()
        {
            BlocksPlacedThisTurn = 0;
            _waveDensityAppliedForStage = false;
            OnTurnBlockCountChanged?.Invoke();
        }

        public void ResetForNewTurn()
        {
            BlocksPlacedThisTurn = 0;
            if (_net.IsHost) BroadcastTurnBlockCount();
            OnTurnBlockCountChanged?.Invoke();
        }

        public bool IsSharedBlockCapActive()
        {
            var s = _session();
            if (s == null || !_net.IsConnected) return false;
            return s.Settings.BlockPacing == BlockPacingRule.SharedTurnBlockCap
                && s.Settings.EconomyMode == CoopEconomyMode.SeparateResources;
        }

        public int GetEffectiveTurnBlockCap()
        {
            var s = _session();
            return s != null ? Math.Max(1, s.Settings.SharedTurnBlockCap) : 3;
        }

        public bool CanPlaceBlockNow(out string denyReason)
        {
            denyReason = "";
            if (!IsSharedBlockCapActive()) return true;
            int cap = GetEffectiveTurnBlockCap();
            if (BlocksPlacedThisTurn >= cap)
            {
                denyReason = $"Turn block cap reached ({BlocksPlacedThisTurn}/{cap}) — coordinate with teammates!";
                return false;
            }
            return true;
        }

        public void IncrementTurnBlockCount()
        {
            if (!IsSharedBlockCapActive()) return;
            BlocksPlacedThisTurn++;
            if (_net.IsHost) BroadcastTurnBlockCount();
            OnTurnBlockCountChanged?.Invoke();
        }

        public void DecrementTurnBlockCount()
        {
            if (!IsSharedBlockCapActive()) return;
            if (BlocksPlacedThisTurn > 0) BlocksPlacedThisTurn--;
            if (_net.IsHost) BroadcastTurnBlockCount();
            OnTurnBlockCountChanged?.Invoke();
        }

        public void BroadcastTurnBlockCount()
        {
            if (!_net.IsHost) return;
            _net.BroadcastToClients(PacketType.EvtTurnBlockCount, w => w.Write(BlocksPlacedThisTurn));
        }

        public void HandleEvtTurnBlockCount(BinaryReader r)
        {
            BlocksPlacedThisTurn = r.ReadInt32();
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
                        // Do not duplicate single-count bosses; scale regular wave counts
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
