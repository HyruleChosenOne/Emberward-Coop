using System;
using EmberwardCoop.Core;
using EmberwardCoop.Sync;
using HarmonyLib;
using UnityEngine;

namespace EmberwardCoop.Patches
{
    [HarmonyPatch]
    internal static class EconomyHandPatches
    {
        private static SessionController S => SessionController.Instance;

        private static bool AllowLocalHandMutation()
            => S == null || !(S.Net.IsClient && S.Ruleset.SyncsGlobalDeckAndHand && !NetworkScope.IsApplying);

        private static bool AllowLocalGoldMutation()
            => S == null || !(S.Net.IsClient && S.Ruleset.SyncsGlobalGold && !NetworkScope.IsApplying);

        private static bool AllowLocalCoreVitalMutation()
            => S == null || !(S.Net.IsClient && !NetworkScope.IsApplying);

        [HarmonyPatch(typeof(IngameData), nameof(IngameData.Initialize), new[] { typeof(int), typeof(int), typeof(int) })]
        [HarmonyPrefix]
        public static void IngameData_Initialize_Prefix(ref int coin, ref int hp, ref int armor, out NetworkScope.ScopeToken __state)
        {
            if (S != null && S.Net.IsConnected)
            {
                int players = Math.Max(1, S.Net.Roster.Count);
                coin = S.Ruleset.ComputeStartingGold(coin, players);
                hp = S.Balance.ApplyCoreHpBonus(hp);
            }
            __state = NetworkScope.Enter();
        }

        [HarmonyPatch(typeof(IngameData), nameof(IngameData.Initialize), new[] { typeof(int), typeof(int), typeof(int) })]
        [HarmonyFinalizer]
        public static void IngameData_Initialize_Finalizer(NetworkScope.ScopeToken __state)
        {
            __state.Dispose();
        }

        [HarmonyPatch(typeof(MainGame), nameof(MainGame.DrawCardAtRoundStart), new Type[0])]
        [HarmonyPrefix]
        public static bool DrawCardAtRoundStart_Prefix(ref Coroutine __result)
        {
            if (S != null && S.Net.IsClient && S.Ruleset.SyncsGlobalDeckAndHand && !NetworkScope.IsApplying)
            {
                __result = null;
                return false;
            }
            return true;
        }

        [HarmonyPatch(typeof(IngameData), nameof(IngameData.DrawCardFromDeck), new[] { typeof(int) })]
        [HarmonyPrefix]
        public static bool DrawCardFromDeck_Prefix() => AllowLocalHandMutation();

        [HarmonyPatch(typeof(IngameData), nameof(IngameData.DrawSpecificCardFromDeck), new[] { typeof(CardData) })]
        [HarmonyPrefix]
        public static bool DrawSpecificCardFromDeck_Prefix() => AllowLocalHandMutation();

        [HarmonyPatch(typeof(IngameData), nameof(IngameData.OnRequestDrawCard), new[] { typeof(int), typeof(bool) })]
        [HarmonyPrefix]
        public static bool OnRequestDrawCard_Prefix() => AllowLocalHandMutation();

        [HarmonyPatch(typeof(IngameData), nameof(IngameData.OnRequestDrawSpecificCard), new[] { typeof(CardData), typeof(bool) })]
        [HarmonyPrefix]
        public static bool OnRequestDrawSpecificCard_Prefix() => AllowLocalHandMutation();

        [HarmonyPatch(typeof(IngameData), nameof(IngameData.AddCardToPlayerHand), new[] { typeof(eItemType) })]
        [HarmonyPrefix]
        public static bool AddCardToPlayerHand_Item_Prefix() => AllowLocalHandMutation();

        [HarmonyPatch(typeof(IngameData), nameof(IngameData.AddCardToPlayerHand), new[] { typeof(CardData) })]
        [HarmonyPrefix]
        public static bool AddCardToPlayerHand_Data_Prefix() => AllowLocalHandMutation();

        [HarmonyPatch(typeof(IngameData), nameof(IngameData.AddCardToPlayerHand), new[] { typeof(CardData) })]
        [HarmonyPostfix]
        public static void AddCardToPlayerHand_Data_Postfix()
        {
            if (S != null && S.Net.IsHost) S.Economy.MarkDirty();
        }

        [HarmonyPatch(typeof(IngameData), nameof(IngameData.AddCardToPlayerHandFromPosition), new[] { typeof(eItemType), typeof(Vector3) })]
        [HarmonyPrefix]
        public static bool AddCardToPlayerHandFromPos_Prefix() => AllowLocalHandMutation();

        [HarmonyPatch(typeof(IngameData), nameof(IngameData.OnRequestAddCardToHand), new[] { typeof(eItemType), typeof(bool) })]
        [HarmonyPrefix]
        public static bool OnRequestAddCardToHand_Prefix() => AllowLocalHandMutation();

        [HarmonyPatch(typeof(IngameData), nameof(IngameData.OnRequestAddCardToHandFromPosition), new[] { typeof(eItemType), typeof(Vector3), typeof(bool) })]
        [HarmonyPrefix]
        public static bool OnRequestAddCardToHandFromPos_Prefix() => AllowLocalHandMutation();

        [HarmonyPatch(typeof(IngameData), nameof(IngameData.RequestAddCardToHandByCardData), new[] { typeof(CardData), typeof(bool) })]
        [HarmonyPrefix]
        public static bool RequestAddCardToHandByData_Prefix() => AllowLocalHandMutation();

        [HarmonyPatch(typeof(IngameData), nameof(IngameData.OnRequestRemoveCardFromHand), new[] { typeof(CardData) })]
        [HarmonyPrefix]
        public static bool OnRequestRemoveCardFromHand_Prefix() => AllowLocalHandMutation();

        [HarmonyPatch(typeof(IngameData), nameof(IngameData.OnRequestRemoveCardFromHand), new[] { typeof(CardData) })]
        [HarmonyPostfix]
        public static void OnRequestRemoveCardFromHand_Postfix()
        {
            if (S != null && S.Net.IsHost) S.Economy.MarkDirty();
        }

        [HarmonyPatch(typeof(IngameData), nameof(IngameData.OnRequestDiscardCardFromHand), new[] { typeof(CardData) })]
        [HarmonyPrefix]
        public static bool OnRequestDiscardCard_Prefix() => AllowLocalHandMutation();

        [HarmonyPatch(typeof(IngameData), nameof(IngameData.OnRequestDiscardAllCardsFromHand), new Type[0])]
        [HarmonyPrefix]
        public static bool OnRequestDiscardAll_Prefix() => AllowLocalHandMutation();

        [HarmonyPatch(typeof(IngameData), nameof(IngameData.OnRequestRecallBlockCard), new[] { typeof(TetrisCardData) })]
        [HarmonyPrefix]
        public static bool OnRequestRecallBlockCard_Prefix() => AllowLocalHandMutation();

        [HarmonyPatch(typeof(IngameData), nameof(IngameData.OnRequestRedrawCards), new Type[0])]
        [HarmonyPrefix]
        public static bool OnRequestRedrawCards_Prefix() => AllowLocalHandMutation();

        [HarmonyPatch(typeof(IngameData), nameof(IngameData.OnRequestMulligan), new Type[0])]
        [HarmonyPrefix]
        public static bool OnRequestMulligan_Prefix() => AllowLocalHandMutation();

        [HarmonyPatch(typeof(IngameData), nameof(IngameData.AddCoin), new[] { typeof(int) })]
        [HarmonyPrefix]
        public static bool AddCoin_Prefix(ref int value)
        {
            if (!AllowLocalGoldMutation()) return false;
            // NetworkScope bypasses income scaling for gifts and sell refunds.
            if (S != null && S.Net.IsConnected && !NetworkScope.IsApplying && value > 0)
            {
                value = S.Ruleset.ComputeScaledIncome(value, Math.Max(1, S.Net.Roster.Count));
            }
            return true;
        }

        [HarmonyPatch(typeof(IngameData), nameof(IngameData.SetCoin), new[] { typeof(int) })]
        [HarmonyPrefix]
        public static bool SetCoin_Prefix() => AllowLocalGoldMutation();

        [HarmonyPatch(typeof(IngameData), nameof(IngameData.SetCoin), new[] { typeof(int) })]
        [HarmonyPostfix]
        public static void SetCoin_Postfix()
        {
            if (S != null && S.Net.IsHost) S.Economy.MarkDirty();
        }

        // Core HP, Armor, and Score stay Host-authoritative in both economy modes.
        [HarmonyPatch(typeof(IngameData), nameof(IngameData.AddHP), new[] { typeof(int) })]
        [HarmonyPrefix]
        public static bool AddHP_Prefix() => AllowLocalCoreVitalMutation();

        [HarmonyPatch(typeof(IngameData), nameof(IngameData.SetHP), new[] { typeof(int) })]
        [HarmonyPrefix]
        public static bool SetHP_Prefix() => AllowLocalCoreVitalMutation();

        [HarmonyPatch(typeof(IngameData), nameof(IngameData.SetHP), new[] { typeof(int) })]
        [HarmonyPostfix]
        public static void SetHP_Postfix()
        {
            if (S != null && S.Net.IsHost) S.Economy.MarkDirty();
        }

        [HarmonyPatch(typeof(IngameData), nameof(IngameData.AddArmor), new[] { typeof(int) })]
        [HarmonyPrefix]
        public static bool AddArmor_Prefix() => AllowLocalCoreVitalMutation();

        [HarmonyPatch(typeof(IngameData), nameof(IngameData.SetArmor), new[] { typeof(int) })]
        [HarmonyPrefix]
        public static bool SetArmor_Prefix() => AllowLocalCoreVitalMutation();

        [HarmonyPatch(typeof(IngameData), nameof(IngameData.AddScore), new[] { typeof(int) })]
        [HarmonyPrefix]
        public static bool AddScore_Prefix() => AllowLocalCoreVitalMutation();

        [HarmonyPatch(typeof(IngameData), nameof(IngameData.SetScore), new[] { typeof(int) })]
        [HarmonyPrefix]
        public static bool SetScore_Prefix() => AllowLocalCoreVitalMutation();

        [HarmonyPatch(typeof(IngameData), nameof(IngameData.AddCosmoShard), new[] { typeof(int) })]
        [HarmonyPrefix]
        public static bool AddCosmoShard_Prefix() => AllowLocalGoldMutation();

        [HarmonyPatch(typeof(IngameData), nameof(IngameData.SetCosmoShard), new[] { typeof(int) })]
        [HarmonyPrefix]
        public static bool SetCosmoShard_Prefix() => AllowLocalGoldMutation();

        [HarmonyPatch(typeof(IngameData), nameof(IngameData.IncreateDrawCost), new Type[0])]
        [HarmonyPrefix]
        public static bool IncreateDrawCost_Prefix() => AllowLocalHandMutation();

        [HarmonyPatch(typeof(IngameData), nameof(IngameData.OnRequestResetDrawCardCost), new Type[0])]
        [HarmonyPrefix]
        public static bool ResetDrawCardCost_Prefix() => AllowLocalHandMutation();

        [HarmonyPatch(typeof(IngameData), nameof(IngameData.OnMonsterDealDamageToPlayer), new[] { typeof(AMonsterBase), typeof(int), typeof(int), typeof(int) })]
        [HarmonyPrefix]
        public static bool MonsterDealDamage_Prefix() => AllowLocalCoreVitalMutation();

        [HarmonyPatch(typeof(UI_PlayerDeck), nameof(UI_PlayerDeck.OnClickButton_DrawCard), new Type[0])]
        [HarmonyPrefix]
        public static bool UI_PlayerDeck_DrawCard_Prefix()
        {
            if (S == null || !S.Net.IsConnected || NetworkScope.IsApplying) return true;
            if (S.Net.IsClient && S.Ruleset.SyncsGlobalDeckAndHand)
            {
                S.Board.ClientSendDrawCard();
                return false;
            }
            return true;
        }

        [HarmonyPatch(typeof(UI_PlayerDeck), nameof(UI_PlayerDeck.OnClickButton_Mulligan), new Type[0])]
        [HarmonyPrefix]
        public static bool UI_PlayerDeck_Mulligan_Prefix(UI_PlayerDeck __instance)
        {
            if (S == null || !S.Net.IsConnected || NetworkScope.IsApplying) return true;
            if (S.Net.IsClient && S.Ruleset.SyncsGlobalDeckAndHand)
            {
                __instance.DisableMulligan();
                S.Board.ClientSendMulligan();
                return false;
            }
            return true;
        }
    }
}
