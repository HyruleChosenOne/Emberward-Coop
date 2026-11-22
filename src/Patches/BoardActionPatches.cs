using System;
using EmberwardCoop.Core;
using EmberwardCoop.Localization;
using EmberwardCoop.Serialization;
using EmberwardCoop.Sync;
using HarmonyLib;
using UnityEngine;

namespace EmberwardCoop.Patches
{
    [HarmonyPatch]
    internal static class BoardActionPatches
    {
        private static SessionController S => SessionController.Instance;

        [HarmonyPatch(typeof(ObjectPlacementHandler), nameof(ObjectPlacementHandler.OnConfirmPlacement), new[] { typeof(bool) })]
        [HarmonyPrefix]
        public static bool ObjectPlacementHandler_OnConfirmPlacement_Prefix(ObjectPlacementHandler __instance, bool doContinuousBuild)
        {
            if (CoopInputGuard.IsCapturingWorldInput) return false;
            if (S == null || !S.Net.IsConnected || NetworkScope.IsApplying) return true;
            if (__instance.curPlacementPrefab == null || __instance.placementDemoObject == null || __instance.placementDemoObjectScpt == null)
                return true;

            __instance.CheckPlacementBlocking();
            if (!__instance.isCurrentPlacementAvaliable) return true;

            Vector3 demoPos = __instance.placementDemoObject.transform.position;
            demoPos.y = 0f;
            Vector3 finalPos = demoPos + __instance.placementDemoObjectScpt.GetPlacementOffset();
            Vector3 finalEuler = __instance.targetPlacementRotation;

            var tetrisCheck = __instance.curPlacementPrefab.GetComponent<Obj_TetrisBlock>();
            if (tetrisCheck != null && !S.Balance.CanPlaceBlockNow(out string denyReason))
            {
                GameRefs.ShowGameToast(denyReason, 2.5f);
                __instance.PlayAnim_PlacementError();
                return false;
            }

            if (S.Net.IsClient)
            {
                var ig = MainGame.Instance?.IngameData;
                var tower = __instance.curPlacementPrefab.GetComponent<ABaseTower>();
                if (tower?.SettingData != null)
                {
                    bool fromHand = __instance.currentPlacementCardData != null;
                    string rId = fromHand ? (__instance.currentPlacementCardData.RuntimeCardId ?? "") : "";
                    int cost = fromHand ? 0 : (TowerManager.Instance != null ? TowerManager.Instance.GetBuildCost(tower.SettingData.itemType) : 0);

                    if (S.Ruleset.ClientSpendsOwnResourcesOnBuild && ig != null)
                    {
                        if (fromHand)
                        {
                            using (NetworkScope.Enter()) ig.OnRequestRemoveCardFromHand(__instance.currentPlacementCardData);
                        }
                        else
                        {
                            if (!ig.IsCoinEnough(cost))
                            {
                                __instance.PlayAnim_PlacementError();
                                return false;
                            }
                            using (NetworkScope.Enter()) ig.AddCoin(-cost);
                        }
                    }

                    S.Undo.RecordLocalTowerPlacement(finalPos, cost, fromHand, __instance.currentPlacementCardData);
                    S.Board.ClientSendPlaceTower(tower.SettingData.itemType, finalPos, finalEuler, fromHand, rId);

                    if (!(!fromHand && doContinuousBuild && __instance.canBuildContinuously))
                    {
                        GameStateController.Instance?.OnRequestChangeGameState(eGameState.NORMAL_MODE);
                        __instance.OnCancelPlacement();
                    }
                    return false;
                }

                if (tetrisCheck != null)
                {
                    var cInfo = GameSerializers.CaptureCard(__instance.currentPlacementCardData);
                    cInfo.ItemType = (int)tetrisCheck.itemType;
                    cInfo.IsTetris = true;
                    S.Undo.RecordLocalBlockPlacement(finalPos, cInfo);

                    if (S.Ruleset.ClientSpendsOwnResourcesOnBuild && ig != null && __instance.currentPlacementCardData != null)
                    {
                        using (NetworkScope.Enter()) ig.OnRequestRemoveCardFromHand(__instance.currentPlacementCardData);
                    }
                    S.Board.ClientSendPlaceTetris(__instance.currentPlacementCardData, tetrisCheck.itemType, finalPos, finalEuler);
                    GameStateController.Instance?.OnRequestChangeGameState(eGameState.NORMAL_MODE);
                    __instance.OnCancelPlacement();
                    return false;
                }
                return true;
            }

            if (S.Net.IsHost)
            {
                var tower = __instance.curPlacementPrefab.GetComponent<ABaseTower>();
                if (tower?.SettingData != null)
                {
                    bool fromHand = __instance.currentPlacementCardData != null;
                    int cost = fromHand ? 0 : (TowerManager.Instance != null ? TowerManager.Instance.GetBuildCost(tower.SettingData.itemType) : 0);
                    S.Undo.RecordLocalTowerPlacement(finalPos, cost, fromHand, __instance.currentPlacementCardData);
                    S.Board.BroadcastEvtPlaceTower(0, tower.SettingData.itemType, finalPos, finalEuler);
                    S.Economy.MarkDirty();
                }
                else if (tetrisCheck != null)
                {
                    var cInfo = GameSerializers.CaptureCard(__instance.currentPlacementCardData);
                    cInfo.ItemType = (int)tetrisCheck.itemType;
                    cInfo.IsTetris = true;
                    S.Undo.RecordLocalBlockPlacement(finalPos, cInfo);
                    S.Balance.IncrementTurnBlockCount();
                    S.Board.BroadcastEvtPlaceTetris(0, tetrisCheck.itemType, finalPos, finalEuler, cInfo);
                    S.Economy.MarkDirty();
                }
            }
            return true;
        }

        [HarmonyPatch(typeof(TowerManager), nameof(TowerManager.RegisterTower), new[] { typeof(ABaseTower) })]
        [HarmonyPostfix]
        public static void TowerManager_RegisterTower_Postfix(ABaseTower tower)
        {
            if (S == null || !S.Net.IsConnected || NetworkScope.IsApplying || tower == null) return;
            if (S.Net.IsHost)
                S.Ownership.StampTowerOwner(tower, 0);
        }

        [HarmonyPatch(typeof(TetrisManager), nameof(TetrisManager.RegisterTetris), new[] { typeof(Obj_TetrisBlock) })]
        [HarmonyPostfix]
        public static void TetrisManager_RegisterTetris_Postfix(Obj_TetrisBlock tetris)
        {
            if (S == null || !S.Net.IsConnected || NetworkScope.IsApplying || tetris == null) return;
            if (S.Net.IsHost)
                S.Ownership.StampBlockOwner(tetris, 0);
        }

        // Hide modification controls when inspecting another player's tower in Separate mode.
        [HarmonyPatch(typeof(UI_TowerControl), nameof(UI_TowerControl.UpdateUIContent), new[] { typeof(ABaseTower) })]
        [HarmonyPostfix]
        public static void UI_TowerControl_UpdateUIContent_Postfix(UI_TowerControl __instance, ABaseTower tower)
        {
            if (S == null || !S.Net.IsConnected || __instance == null || tower == null) return;
            if (!S.Ruleset.EnforcesEntityOwnership) return;

            int ownerId = S.Ownership.GetTowerOwner(tower);
            bool canModify = S.Ruleset.CanPlayerModifyTower(S.Net.LocalPlayerId, tower);

            if (!canModify)
            {
                if (__instance.node_UpgradeButton != null) __instance.node_UpgradeButton.gameObject.SetActive(false);
                if (__instance.node_Sell != null) __instance.node_Sell.gameObject.SetActive(false);
                if (__instance.node_TargetPriority != null) __instance.node_TargetPriority.gameObject.SetActive(false);
                if (__instance.node_RerollDiceTower != null) __instance.node_RerollDiceTower.SetActive(false);
                if (__instance.node_DrillTowerExtraButton != null) __instance.node_DrillTowerExtraButton.SetActive(false);
                if (__instance.node_ForceActivateTower != null) __instance.node_ForceActivateTower.gameObject.SetActive(false);
                if (__instance.node_ScorchTowerControl != null) __instance.node_ScorchTowerControl.SetActive(false);

                if (__instance.text_TowerName != null)
                {
                    string ownerName = S.Net.GetPlayerName(ownerId);
                    Color ownerCol = CoopConfig.GetPlayerColor(S.Net.GetPlayerColorIndex(ownerId));
                    string hex = ColorUtility.ToHtmlStringRGB(ownerCol);
                    string ownedTag = CoopLoc.F("hud.owned_by", ownerName);
                    string cur = __instance.text_TowerName.text ?? "";
                    if (!cur.Contains(ownedTag))
                        __instance.text_TowerName.text = $"{cur}\n<size=68%><color=#{hex}>{ownedTag}</color></size>";
                }
            }
        }

        [HarmonyPatch(typeof(Obj_TetrisBlock), nameof(Obj_TetrisBlock.Recall), new[] { typeof(bool) })]
        [HarmonyPrefix]
        public static bool Obj_TetrisBlock_Recall_Prefix(Obj_TetrisBlock __instance, bool isForceRecall)
        {
            if (S == null || !S.Net.IsConnected || NetworkScope.IsApplying) return true;

            if (!S.Ruleset.CanPlayerRecallBlock(S.Net.LocalPlayerId, __instance))
            {
                string ownerName = S.Net.GetPlayerName(S.Ownership.GetBlockOwner(__instance));
                GameRefs.ShowGameToast(CoopLoc.F("hud.owned_by", ownerName), 1.8f);
                return false;
            }

            Vector3 pos = __instance.transform.position;
            if (S.Net.IsClient)
            {
                S.Board.ClientSendRecallTetris(pos, isForceRecall);
                return false;
            }
            if (S.Net.IsHost)
            {
                S.Ownership.UnregisterBlock(__instance);
                S.Balance.DecrementTurnBlockCount();
                S.Board.BroadcastEvtRecallTetris(0, pos, isForceRecall);
                S.Economy.MarkDirty();
            }
            return true;
        }

        [HarmonyPatch(typeof(UI_TowerControl), nameof(UI_TowerControl.RequestUpgradeTower), new[] { typeof(ABaseTower.eUpgradeType) })]
        [HarmonyPrefix]
        public static bool UI_TowerControl_Upgrade_Prefix(UI_TowerControl __instance, ABaseTower.eUpgradeType upgradeType)
        {
            if (S == null || !S.Net.IsConnected || NetworkScope.IsApplying) return true;
            var tower = __instance.curTower;
            if (tower == null) return true;

            if (!S.Ruleset.CanPlayerModifyTower(S.Net.LocalPlayerId, tower))
            {
                GameRefs.ShowGameToast(CoopLoc.F("hud.owned_by", S.Net.GetPlayerName(S.Ownership.GetTowerOwner(tower))), 1.8f);
                return false;
            }

            if (S.Net.IsClient)
            {
                if (S.Ruleset.ClientSpendsOwnResourcesOnBuild)
                {
                    var ig = MainGame.Instance?.IngameData;
                    int cost = tower.GetUpgradeCost(upgradeType);
                    if (ig == null || !ig.IsCoinEnough(cost)) return false;
                    using (NetworkScope.Enter()) ig.AddCoin(-cost);
                }
                S.Board.ClientSendUpgradeTower(tower.transform.position, upgradeType);
                return false;
            }
            if (S.Net.IsHost)
            {
                var ig = MainGame.Instance?.IngameData;
                if (ig != null && ig.IsCoinEnough(tower.GetUpgradeCost(upgradeType)))
                {
                    S.Board.BroadcastEvtUpgradeTower(0, tower.transform.position, upgradeType);
                    S.Economy.MarkDirty();
                }
            }
            return true;
        }

        [HarmonyPatch(typeof(UI_TowerControl), nameof(UI_TowerControl.OnClickButtonSell), new Type[0])]
        [HarmonyPrefix]
        public static bool UI_TowerControl_Sell_Prefix(UI_TowerControl __instance)
        {
            if (S == null || !S.Net.IsConnected || NetworkScope.IsApplying) return true;
            var tower = __instance.curTower;
            if (tower == null || !tower.CanSellTower()) return true;

            if (!S.Ruleset.CanPlayerModifyTower(S.Net.LocalPlayerId, tower))
            {
                GameRefs.ShowGameToast(CoopLoc.F("hud.owned_by", S.Net.GetPlayerName(S.Ownership.GetTowerOwner(tower))), 1.8f);
                return false;
            }

            if (S.Net.IsClient)
            {
                if (S.Ruleset.ClientSpendsOwnResourcesOnBuild)
                {
                    var ig = MainGame.Instance?.IngameData;
                    int refund = tower.GetSellValue();
                    if (ig != null && refund > 0)
                    {
                        using (NetworkScope.Enter()) ig.AddCoin(refund);
                    }
                }
                S.Board.ClientSendSellTower(tower.transform.position);
                __instance.Close();
                return false;
            }
            if (S.Net.IsHost)
            {
                S.Ownership.UnregisterTower(tower);
                S.Board.BroadcastEvtSellTower(0, tower.transform.position);
                S.Economy.MarkDirty();
            }
            return true;
        }

        // NetworkScope prevents Separate-mode income scaling from reducing sell refunds.
        [HarmonyPatch(typeof(IngameData), nameof(IngameData.OnRequestSellTower), new[] { typeof(ABaseTower) })]
        [HarmonyPrefix]
        public static void IngameData_SellTower_Prefix(out NetworkScope.ScopeToken __state)
            => __state = NetworkScope.Enter();

        [HarmonyPatch(typeof(IngameData), nameof(IngameData.OnRequestSellTower), new[] { typeof(ABaseTower) })]
        [HarmonyFinalizer]
        public static void IngameData_SellTower_Finalizer(NetworkScope.ScopeToken __state)
            => __state.Dispose();

        [HarmonyPatch(typeof(UI_TowerControl), nameof(UI_TowerControl.OnClickRerollDiceTower), new Type[0])]
        [HarmonyPrefix]
        public static bool TowerControl_DiceReroll_Prefix(UI_TowerControl __instance)
            => HandleSpecialTowerButton(__instance, TowerSpecialActionType.DiceReroll);

        [HarmonyPatch(typeof(UI_TowerControl), nameof(UI_TowerControl.OnClickDrillTowerExtraButton), new Type[0])]
        [HarmonyPrefix]
        public static bool TowerControl_DrillExtra_Prefix(UI_TowerControl __instance)
            => HandleSpecialTowerButton(__instance, TowerSpecialActionType.DrillExtra);

        [HarmonyPatch(typeof(UI_TowerControl), nameof(UI_TowerControl.OnClickForceActivateTower), new Type[0])]
        [HarmonyPrefix]
        public static bool TowerControl_ForceActivate_Prefix(UI_TowerControl __instance)
            => HandleSpecialTowerButton(__instance, TowerSpecialActionType.ForceActivate);

        [HarmonyPatch(typeof(UI_TowerControl), nameof(UI_TowerControl.OnClickScorchTowerInverseRotation), new Type[0])]
        [HarmonyPrefix]
        public static bool TowerControl_ScorchReverse_Prefix(UI_TowerControl __instance)
            => HandleSpecialTowerButton(__instance, TowerSpecialActionType.ScorchInverseRotation);

        private static bool HandleSpecialTowerButton(UI_TowerControl ctrl, TowerSpecialActionType action)
        {
            if (S == null || !S.Net.IsConnected || NetworkScope.IsApplying || ctrl?.curTower == null) return true;
            if (!S.Ruleset.CanPlayerModifyTower(S.Net.LocalPlayerId, ctrl.curTower)) return false;

            Vector3 pos = ctrl.curTower.transform.position;
            if (S.Net.IsClient)
            {
                S.Board.ClientSendTowerSpecialAction(pos, action);
                return false;
            }
            if (S.Net.IsHost)
            {
                S.Board.BroadcastEvtTowerSpecialAction(pos, action);
                S.Economy.MarkDirty();
            }
            return true;
        }

        [HarmonyPatch(typeof(ABaseTower), nameof(ABaseTower.SwitchToNextTargetPriority), new Type[0])]
        [HarmonyPrefix]
        public static bool Tower_NextPriority_Prefix(ABaseTower __instance)
        {
            if (S == null || !S.Net.IsConnected || NetworkScope.IsApplying) return true;
            return S.Ruleset.CanPlayerModifyTower(S.Net.LocalPlayerId, __instance);
        }

        [HarmonyPatch(typeof(ABaseTower), nameof(ABaseTower.SwitchToNextTargetPriority), new Type[0])]
        [HarmonyPostfix]
        public static void Tower_NextPriority_Postfix(ABaseTower __instance)
        {
            if (S == null || !S.Net.IsConnected || NetworkScope.IsApplying) return;
            if (S.Net.IsClient) S.Board.ClientSendTowerPriority(__instance.transform.position, __instance.TargetPriority);
            else if (S.Net.IsHost) S.Board.BroadcastEvtTowerPriority(__instance.transform.position, __instance.TargetPriority);
        }

        [HarmonyPatch(typeof(ABaseTower), nameof(ABaseTower.SwitchToPreviousTargetPriority), new Type[0])]
        [HarmonyPrefix]
        public static bool Tower_PrevPriority_Prefix(ABaseTower __instance)
        {
            if (S == null || !S.Net.IsConnected || NetworkScope.IsApplying) return true;
            return S.Ruleset.CanPlayerModifyTower(S.Net.LocalPlayerId, __instance);
        }

        [HarmonyPatch(typeof(ABaseTower), nameof(ABaseTower.SwitchToPreviousTargetPriority), new Type[0])]
        [HarmonyPostfix]
        public static void Tower_PrevPriority_Postfix(ABaseTower __instance)
        {
            if (S == null || !S.Net.IsConnected || NetworkScope.IsApplying) return;
            if (S.Net.IsClient) S.Board.ClientSendTowerPriority(__instance.transform.position, __instance.TargetPriority);
            else if (S.Net.IsHost) S.Board.BroadcastEvtTowerPriority(__instance.transform.position, __instance.TargetPriority);
        }

        [HarmonyPatch(typeof(ABaseTower), nameof(ABaseTower.RecordDamage), new[] { typeof(int) })]
        [HarmonyPostfix]
        public static void Tower_RecordDamage_Postfix(ABaseTower __instance, int damage)
        {
            if (S != null && S.Net.IsHost && damage > 0)
                S.Ownership.RecordDamageByTower(__instance, damage);
        }

        [HarmonyPatch(typeof(AMonsterBase), nameof(AMonsterBase.ApplyFragileEffect), new[] { typeof(float), typeof(ABaseTower) })]
        [HarmonyPostfix]
        public static void Monster_ApplyFragile_Postfix(ABaseTower fromTower)
        {
            if (S != null && S.Net.IsHost) S.Ownership.RecordDebuffApplied(fromTower);
        }

        [HarmonyPatch(typeof(AMonsterBase), nameof(AMonsterBase.ApplyBurnEffect), new[] { typeof(float), typeof(ABaseTower) })]
        [HarmonyPostfix]
        public static void Monster_ApplyBurn_Postfix(ABaseTower fromTower)
        {
            if (S != null && S.Net.IsHost) S.Ownership.RecordDebuffApplied(fromTower);
        }

        [HarmonyPatch(typeof(AMonsterBase), nameof(AMonsterBase.ApplyPoison), new[] { typeof(int), typeof(ABaseTower) })]
        [HarmonyPostfix]
        public static void Monster_ApplyPoison_Postfix(ABaseTower fromTower)
        {
            if (S != null && S.Net.IsHost) S.Ownership.RecordDebuffApplied(fromTower);
        }

        [HarmonyPatch(typeof(AMonsterBase), nameof(AMonsterBase.ApplyChillEffect), new[] { typeof(float), typeof(bool), typeof(int), typeof(int) })]
        [HarmonyPostfix]
        public static void Monster_ApplyChill_Postfix(bool isFromPlayer)
        {
            if (S != null && S.Net.IsHost && isFromPlayer) S.Ownership.RecordDebuffApplied(null);
        }

        [HarmonyPatch(typeof(BuffEffectHandler), nameof(BuffEffectHandler.OnConfirmBuffSelection), new Type[0])]
        [HarmonyPrefix]
        public static bool Buff_OnConfirm_Prefix(BuffEffectHandler __instance)
        {
            if (S == null || !S.Net.IsConnected || NetworkScope.IsApplying) return true;
            var data = __instance.curData;
            if (data == null) return true;

            int targetMode = (int)data.TargetType;
            Vector3 targetPos;
            if (targetMode == 0 && __instance.currentPointingTower != null) targetPos = __instance.currentPointingTower.transform.position;
            else if (targetMode == 1 && __instance.currentPointingTetrisBlock != null) targetPos = __instance.currentPointingTetrisBlock.transform.position;
            else if (targetMode == 2 && __instance.currentPointingMonster != null) targetPos = __instance.currentPointingMonster.transform.position;
            else if (targetMode == 3) targetPos = __instance.currentPointingPosition;
            else return true;

            if (S.Net.IsClient)
            {
                var cardData = GameRefs.GetCardController()?.currentPlacementCard?.CardData;
                string rId = cardData?.RuntimeCardId ?? "";
                if (S.Ruleset.ClientSpendsOwnResourcesOnBuild && cardData != null)
                {
                    var ig = MainGame.Instance?.IngameData;
                    using (NetworkScope.Enter())
                    {
                        cardData.ConsumeItemUseCount();
                        if (!cardData.HasRemainingItemUseCount() && ig != null)
                            ig.OnRequestRemoveCardFromHand(cardData);
                    }
                }
                S.Board.ClientSendUseBuff(rId, data.itemType, targetMode, targetPos);
                GameStateController.Instance?.OnRequestChangeGameState(eGameState.NORMAL_MODE);
                __instance.OnCancelBuffSelection();
                return false;
            }
            if (S.Net.IsHost)
            {
                S.Board.BroadcastEvtUseBuff(0, data.itemType, targetMode, targetPos);
                S.Economy.MarkDirty();
            }
            return true;
        }

        [HarmonyPatch(typeof(BuffEffectHandler), nameof(BuffEffectHandler.OnRequestImmediateBuffCast), new[] { typeof(ABaseBuffSettingData), typeof(bool), typeof(bool) })]
        [HarmonyPrefix]
        public static bool Buff_Immediate_Prefix(ABaseBuffSettingData data, bool isFromPlayer)
        {
            if (S == null || !S.Net.IsConnected || NetworkScope.IsApplying || data == null || !isFromPlayer)
                return true;

            if (S.Net.IsClient)
            {
                var cardData = GameRefs.GetCardController()?.currentPlacementCard?.CardData;
                string rId = cardData?.RuntimeCardId ?? "";
                if (S.Ruleset.ClientSpendsOwnResourcesOnBuild && cardData != null)
                {
                    var ig = MainGame.Instance?.IngameData;
                    using (NetworkScope.Enter())
                    {
                        cardData.ConsumeItemUseCount();
                        if (!cardData.HasRemainingItemUseCount() && ig != null)
                            ig.OnRequestRemoveCardFromHand(cardData);
                    }
                }
                S.Board.ClientSendUseBuff(rId, data.itemType, 4, Vector3.zero);
                return false;
            }
            if (S.Net.IsHost)
            {
                S.Board.BroadcastEvtUseBuff(0, data.itemType, 4, Vector3.zero);
                S.Economy.MarkDirty();
            }
            return true;
        }

        [HarmonyPatch(typeof(Obj_RuneStone), nameof(Obj_RuneStone.PayToTrigger), new Type[0])]
        [HarmonyPrefix]
        public static bool RuneStone_PayToTrigger_Prefix(Obj_RuneStone __instance)
        {
            if (S == null || !S.Net.IsConnected || NetworkScope.IsApplying || __instance == null) return true;
            Vector3 pos = __instance.transform.position;
            if (S.Net.IsClient)
            {
                if (S.Ruleset.ClientSpendsOwnResourcesOnBuild)
                {
                    var ig = MainGame.Instance?.IngameData;
                    int cost = __instance.GetCurrentCost();
                    if (ig == null || !ig.IsCoinEnough(cost)) return false;
                    using (NetworkScope.Enter()) ig.AddCoin(-cost);
                }
                S.Board.ClientSendActivateRuneStone(pos);
                return false;
            }
            if (S.Net.IsHost)
            {
                var ig = MainGame.Instance?.IngameData;
                if (ig != null && ig.IsCoinEnough(__instance.GetCurrentCost()))
                {
                    S.Board.BroadcastEvtActivateRuneStone(0, pos);
                    S.Economy.MarkDirty();
                }
            }
            return true;
        }

        [HarmonyPatch(typeof(Obj_Chest), nameof(Obj_Chest.OpenChest), new Type[0])]
        [HarmonyPrefix]
        public static bool Obj_Chest_OpenChest_Prefix(Obj_Chest __instance)
        {
            if (S == null || !S.Net.IsConnected || NetworkScope.IsApplying || __instance == null) return true;
            Vector3 pos = __instance.transform.position;
            if (S.Net.IsClient)
            {
                S.Board.ClientSendOpenChest(pos);
                return false;
            }
            if (S.Net.IsHost)
            {
                S.Board.BroadcastEvtOpenChest(pos);
            }
            return true;
        }
    }
}
