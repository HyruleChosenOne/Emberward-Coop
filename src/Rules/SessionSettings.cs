using System;
using System.IO;
using UnityEngine;

namespace EmberwardCoop.Rules
{
    public enum CoopEconomyMode : byte
    {
        SharedResources = 0,
        SeparateResources = 1
    }

    public enum SessionPreset : byte
    {
        BalancedCoop = 0,
        Casual = 1,
        TacticalHardcore = 2,
        Chaos = 3,
        Custom = 4
    }

    public enum StartingGoldRule : byte
    {
        BalancedSplit = 0,
        FullPerPlayer = 1
    }

    public enum BlockPacingRule : byte
    {
        SharedTurnBlockCap = 0,
        IndependentHandCap = 1
    }

    public enum CoreHpRule : byte
    {
        SharedSingleCore = 0,
        ScaledBonusCore = 1
    }

    public enum DraftSyncMode : byte
    {
        SimultaneousIndependent = 0,
        HostSharedDraft = 1
    }

    public enum SpeedControlRule : byte
    {
        AnyPlayer = 0,
        HostOnly = 1
    }

    public sealed class SessionSettings
    {
        public CoopEconomyMode EconomyMode = CoopEconomyMode.SharedResources;
        public SessionPreset Preset = SessionPreset.BalancedCoop;
        public bool AutoScaleByPlayerCount = true;

        public float EnemyHpMultiplier = 1.35f;
        public float WaveDensityMultiplier = 1.15f;
        public float EnemySpeedMultiplier = 1.00f;
        public float BossHpArmorMultiplier = 1.50f;

        public StartingGoldRule StartingGold = StartingGoldRule.BalancedSplit;
        public float WaveIncomeMultiplier = 0.65f;
        public BlockPacingRule BlockPacing = BlockPacingRule.SharedTurnBlockCap;
        public int SharedTurnBlockCap = 3;
        public CoreHpRule CoreHp = CoreHpRule.SharedSingleCore;

        public bool AllowResourceGifting = true;
        public int GiftPerWaveCap = 0; // 0 = unlimited
        public DraftSyncMode DraftMode = DraftSyncMode.SimultaneousIndependent;
        public SpeedControlRule SpeedControl = SpeedControlRule.AnyPlayer;

        public void ApplyPreset(SessionPreset preset, int playerCount)
        {
            int p = Math.Clamp(playerCount, 1, 6);
            int extra = Math.Max(0, p - 1);
            Preset = preset;

            switch (preset)
            {
                case SessionPreset.Casual:
                    EnemyHpMultiplier = Mathf.Clamp(1.00f + extra * 0.15f, 1.0f, 2.5f);
                    WaveDensityMultiplier = 1.00f;
                    EnemySpeedMultiplier = 0.90f;
                    BossHpArmorMultiplier = Mathf.Clamp(1.10f + extra * 0.20f, 1.0f, 3.0f);
                    StartingGold = StartingGoldRule.FullPerPlayer;
                    WaveIncomeMultiplier = Mathf.Clamp(1.00f - extra * 0.10f, 0.70f, 1.50f);
                    BlockPacing = BlockPacingRule.IndependentHandCap;
                    SharedTurnBlockCap = 4 + extra;
                    CoreHp = CoreHpRule.ScaledBonusCore;
                    AllowResourceGifting = true;
                    GiftPerWaveCap = 0;
                    DraftMode = DraftSyncMode.SimultaneousIndependent;
                    SpeedControl = SpeedControlRule.AnyPlayer;
                    break;

                case SessionPreset.TacticalHardcore:
                    EnemyHpMultiplier = Mathf.Clamp(1.30f + extra * 0.45f, 1.0f, 2.5f);
                    WaveDensityMultiplier = Mathf.Clamp(1.15f + extra * 0.15f, 1.0f, 2.0f);
                    EnemySpeedMultiplier = 1.15f;
                    BossHpArmorMultiplier = Mathf.Clamp(1.50f + extra * 0.55f, 1.0f, 3.0f);
                    StartingGold = StartingGoldRule.BalancedSplit;
                    WaveIncomeMultiplier = Mathf.Clamp(1.15f / p, 0.50f, 1.00f);
                    BlockPacing = BlockPacingRule.SharedTurnBlockCap;
                    SharedTurnBlockCap = Math.Max(2, 2 + extra);
                    CoreHp = CoreHpRule.SharedSingleCore;
                    AllowResourceGifting = true;
                    GiftPerWaveCap = 100;
                    DraftMode = DraftSyncMode.SimultaneousIndependent;
                    SpeedControl = SpeedControlRule.HostOnly;
                    break;

                case SessionPreset.Chaos:
                    EnemyHpMultiplier = Mathf.Clamp(1.40f + extra * 0.35f, 1.0f, 2.5f);
                    WaveDensityMultiplier = Mathf.Clamp(1.40f + extra * 0.20f, 1.0f, 2.0f);
                    EnemySpeedMultiplier = 1.25f;
                    BossHpArmorMultiplier = Mathf.Clamp(1.75f + extra * 0.45f, 1.0f, 3.0f);
                    StartingGold = StartingGoldRule.FullPerPlayer;
                    WaveIncomeMultiplier = 1.10f;
                    BlockPacing = BlockPacingRule.IndependentHandCap;
                    SharedTurnBlockCap = 6;
                    CoreHp = CoreHpRule.ScaledBonusCore;
                    AllowResourceGifting = true;
                    GiftPerWaveCap = 0;
                    DraftMode = DraftSyncMode.SimultaneousIndependent;
                    SpeedControl = SpeedControlRule.AnyPlayer;
                    break;

                case SessionPreset.BalancedCoop:
                default:
                    // Balanced Co-op: ~1.25x combined team gold in 2P, scaled HP/boss pressure, negotiated maze turns
                    EnemyHpMultiplier = Mathf.Clamp(1.00f + extra * 0.35f, 1.0f, 2.5f);
                    WaveDensityMultiplier = Mathf.Clamp(1.00f + extra * 0.15f, 1.0f, 2.0f);
                    EnemySpeedMultiplier = 1.00f;
                    BossHpArmorMultiplier = Mathf.Clamp(1.15f + extra * 0.40f, 1.0f, 3.0f);
                    StartingGold = StartingGoldRule.BalancedSplit;
                    // Target combined team economy ~1.25x solo: 2P -> 0.63x each, 3P -> 0.50x each
                    WaveIncomeMultiplier = Mathf.Clamp((float)Math.Round(1.26f / Math.Max(1, p), 2), 0.50f, 1.00f);
                    BlockPacing = BlockPacingRule.SharedTurnBlockCap;
                    SharedTurnBlockCap = Math.Clamp(2 + extra, 2, 6);
                    CoreHp = CoreHpRule.SharedSingleCore;
                    AllowResourceGifting = true;
                    GiftPerWaveCap = 0;
                    DraftMode = DraftSyncMode.SimultaneousIndependent;
                    SpeedControl = SpeedControlRule.AnyPlayer;
                    break;
            }
        }

        public void OnPlayerCountChanged(int playerCount)
        {
            if (AutoScaleByPlayerCount && Preset != SessionPreset.Custom)
                ApplyPreset(Preset, Math.Max(2, playerCount));
        }

        public void Write(BinaryWriter w)
        {
            w.Write((byte)EconomyMode);
            w.Write((byte)Preset);
            w.Write(AutoScaleByPlayerCount);
            w.Write(EnemyHpMultiplier);
            w.Write(WaveDensityMultiplier);
            w.Write(EnemySpeedMultiplier);
            w.Write(BossHpArmorMultiplier);
            w.Write((byte)StartingGold);
            w.Write(WaveIncomeMultiplier);
            w.Write((byte)BlockPacing);
            w.Write(SharedTurnBlockCap);
            w.Write((byte)CoreHp);
            w.Write(AllowResourceGifting);
            w.Write(GiftPerWaveCap);
            w.Write((byte)DraftMode);
            w.Write((byte)SpeedControl);
        }

        public void ReadFrom(BinaryReader r)
        {
            EconomyMode = (CoopEconomyMode)r.ReadByte();
            Preset = (SessionPreset)r.ReadByte();
            AutoScaleByPlayerCount = r.ReadBoolean();
            EnemyHpMultiplier = Mathf.Clamp(r.ReadSingle(), 1.0f, 2.5f);
            WaveDensityMultiplier = Mathf.Clamp(r.ReadSingle(), 1.0f, 2.0f);
            EnemySpeedMultiplier = Mathf.Clamp(r.ReadSingle(), 0.8f, 1.5f);
            BossHpArmorMultiplier = Mathf.Clamp(r.ReadSingle(), 1.0f, 3.0f);
            StartingGold = (StartingGoldRule)r.ReadByte();
            WaveIncomeMultiplier = Mathf.Clamp(r.ReadSingle(), 0.5f, 1.5f);
            BlockPacing = (BlockPacingRule)r.ReadByte();
            SharedTurnBlockCap = Math.Clamp(r.ReadInt32(), 1, 12);
            CoreHp = (CoreHpRule)r.ReadByte();
            AllowResourceGifting = r.ReadBoolean();
            GiftPerWaveCap = Math.Max(0, r.ReadInt32());
            DraftMode = (DraftSyncMode)r.ReadByte();
            SpeedControl = (SpeedControlRule)r.ReadByte();
        }

        public string GetCompactSummary()
        {
            string mode = EconomyMode == CoopEconomyMode.SeparateResources ? "Separate" : "Shared";
            return $"{mode} · {Preset} · HP {Mathf.RoundToInt(EnemyHpMultiplier * 100)}% · Boss {BossHpArmorMultiplier:0.0}x";
        }
    }
}
