using System;
using EmberwardCoop.Sync;
using UnityEngine;

namespace EmberwardCoop.Rules
{
    public interface ICoopRuleset
    {
        CoopEconomyMode Mode { get; }
        bool SyncsGlobalGold { get; }
        bool SyncsGlobalDeckAndHand { get; }
        bool SyncsGlobalEconomyAndHand { get; }
        bool UsesIndividualLoadouts { get; }
        bool EnforcesEntityOwnership { get; }
        bool AllowsClientIndependentDraftsAndShops { get; }
        bool BlocksClientLocalEconomyAndHandMutations { get; }
        bool ClientSpendsOwnResourcesOnBuild { get; }
        bool SupportsResourceGifting { get; }

        bool CanPlayerModifyTower(int playerId, ABaseTower tower);
        bool CanPlayerRecallBlock(int playerId, Obj_TetrisBlock block);
        int ComputeStartingGold(int baseCoin, int playerCount);
        int ComputeScaledIncome(int positiveDelta, int playerCount);
        int ComputeDraftSeedSalt(int localPlayerId);
        bool CanClientChangeGameSpeed();
    }

    public sealed class SharedResourcesRuleset : ICoopRuleset
    {
        private readonly SessionSettings _settings;

        public SharedResourcesRuleset(SessionSettings settings) => _settings = settings;

        public CoopEconomyMode Mode => CoopEconomyMode.SharedResources;
        public bool SyncsGlobalGold => true;
        public bool SyncsGlobalDeckAndHand => _settings.SharedLoadoutsInSharedMode;
        public bool SyncsGlobalEconomyAndHand => _settings.SharedLoadoutsInSharedMode;
        public bool UsesIndividualLoadouts => !_settings.SharedLoadoutsInSharedMode;
        public bool EnforcesEntityOwnership => false;
        public bool AllowsClientIndependentDraftsAndShops => !_settings.SharedLoadoutsInSharedMode;
        public bool BlocksClientLocalEconomyAndHandMutations => _settings.SharedLoadoutsInSharedMode;
        public bool ClientSpendsOwnResourcesOnBuild => false;
        public bool SupportsResourceGifting => false;

        public bool CanPlayerModifyTower(int playerId, ABaseTower tower) => true;
        public bool CanPlayerRecallBlock(int playerId, Obj_TetrisBlock block) => true;
        public int ComputeStartingGold(int baseCoin, int playerCount) => baseCoin;
        public int ComputeScaledIncome(int positiveDelta, int playerCount) => positiveDelta;
        public int ComputeDraftSeedSalt(int localPlayerId) => _settings.SharedLoadoutsInSharedMode ? 0 : unchecked(localPlayerId * 73856093);
        public bool CanClientChangeGameSpeed() => _settings.SpeedControl == SpeedControlRule.AnyPlayer;
    }

    public sealed class SeparateResourcesRuleset : ICoopRuleset
    {
        private readonly SessionSettings _settings;
        private readonly OwnershipAndStatsTracker _ownership;

        public SeparateResourcesRuleset(SessionSettings settings, OwnershipAndStatsTracker ownership)
        {
            _settings = settings;
            _ownership = ownership;
        }

        public CoopEconomyMode Mode => CoopEconomyMode.SeparateResources;
        public bool SyncsGlobalGold => false;
        public bool SyncsGlobalDeckAndHand => false;
        public bool SyncsGlobalEconomyAndHand => false;
        public bool UsesIndividualLoadouts => true;
        public bool EnforcesEntityOwnership => true;
        public bool AllowsClientIndependentDraftsAndShops => _settings.DraftMode == DraftSyncMode.SimultaneousIndependent;
        public bool BlocksClientLocalEconomyAndHandMutations => false;
        public bool ClientSpendsOwnResourcesOnBuild => true;
        public bool SupportsResourceGifting => _settings.AllowResourceGifting;

        public bool CanPlayerModifyTower(int playerId, ABaseTower tower)
        {
            if (tower == null) return false;
            int owner = _ownership.GetTowerOwner(tower);
            return owner < 0 || owner == playerId;
        }

        public bool CanPlayerRecallBlock(int playerId, Obj_TetrisBlock block)
        {
            if (block == null) return false;
            int owner = _ownership.GetBlockOwner(block);
            return owner < 0 || owner == playerId;
        }

        public int ComputeStartingGold(int baseCoin, int playerCount)
        {
            if (playerCount <= 1 || _settings.StartingGold == StartingGoldRule.FullPerPlayer)
                return baseCoin;

            // Balanced Co-op Split: target ~1.30x combined starting gold across team (e.g., 65% each in 2P)
            float mult = Mathf.Clamp(1.30f / playerCount, 0.45f, 0.80f);
            return Math.Max(10, Mathf.RoundToInt(baseCoin * mult));
        }

        public int ComputeScaledIncome(int positiveDelta, int playerCount)
        {
            if (positiveDelta <= 0 || playerCount <= 1) return positiveDelta;
            int scaled = Mathf.RoundToInt(positiveDelta * _settings.WaveIncomeMultiplier);
            return Math.Max(1, scaled);
        }

        public int ComputeDraftSeedSalt(int localPlayerId)
        {
            if (_settings.DraftMode == DraftSyncMode.HostSharedDraft) return 0;
            return unchecked(localPlayerId * 73856093);
        }

        public bool CanClientChangeGameSpeed() => _settings.SpeedControl == SpeedControlRule.AnyPlayer;
    }
}
