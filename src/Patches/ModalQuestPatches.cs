using System;
using EmberwardCoop.Core;
using EmberwardCoop.Sync;
using HarmonyLib;

namespace EmberwardCoop.Patches
{
    [HarmonyPatch]
    internal static class ModalQuestPatches
    {
        private static SessionController S => SessionController.Instance;

        // Hook concrete APopupWindow.ShowWindow/CloseWindow instead of abstract ShowWindowProc/CloseWindowProc
        // or per-subclass overrides (which can be ICF-folded empty stubs in IL2CPP).
        [HarmonyPatch(typeof(APopupWindow), nameof(APopupWindow.ShowWindow), new Type[0])]
        [HarmonyPrefix]
        public static bool APopupWindow_Show_Prefix(APopupWindow __instance)
        {
            if (__instance != null && S != null && S.Net != null && S.Net.IsClient && !NetworkScope.IsApplying && S.Ruleset != null)
            {
                bool isDraftPopup =
                    __instance.TryCast<UI_ChooseRewardCardPopup>() != null ||
                    __instance.TryCast<UI_DiscoverCard_Popup>() != null;

                if (isDraftPopup)
                {
                    if (!S.Ruleset.AllowsClientIndependentDraftsAndShops)
                    {
                        __instance.OverrideIsWindowFinished(true);
                        __instance.CloseWindow();
                        return false;
                    }
                    if (S.Run != null)
                    {
                        int salt = S.Ruleset.ComputeDraftSeedSalt(S.Net.LocalPlayerId);
                        if (salt != 0) UnityEngine.Random.InitState(unchecked(S.Run.PendingStageSeed ^ salt ^ Environment.TickCount));
                    }
                }
                else if (__instance.TryCast<UI_AddRuneToTetrisCard_Popup>() != null ||
                         __instance.TryCast<UI_QuestReward_Popup>() != null)
                {
                    if (!S.Ruleset.AllowsClientIndependentDraftsAndShops)
                    {
                        __instance.OverrideIsWindowFinished(true);
                        __instance.CloseWindow();
                        return false;
                    }
                }
            }
            return true;
        }

        [HarmonyPatch(typeof(APopupWindow), nameof(APopupWindow.ShowWindow), new Type[0])]
        [HarmonyPostfix]
        public static void APopupWindow_Show_Postfix(APopupWindow __instance)
        {
            if (S == null || !S.Net.IsConnected || __instance == null) return;

            if (__instance.TryCast<UI_Defeat_Popup>() != null)
            {
                if (S.Net.IsHost)
                {
                    S.Ownership.BroadcastRunStats();
                    S.Modals.BroadcastVictoryOrDefeat(isVictory: false, isFinalBoss: false);
                    var gd = GameDataManager.instance?.GameplayData;
                    S.Quests.BroadcastMetaRewardGrant(25, false, false, gd != null ? gd.curWorld : eWorldType.WORLD_1_FOREST, gd != null ? gd.difficulty : eGameDifficultyType.NORMAL);
                }
                UI.PostRunSummaryOverlay.AttachToPopup(__instance.transform, isVictory: false);
            }

            if (S.Net.IsClient)
            {
                S.Run.MenuLockDirty = true;
                return;
            }

            if (S.Net.IsHost)
            {
                var kind = ClassifyPopup(__instance);
                if (kind != CoopModalKind.None)
                    S.Modals.NotifyHostPopupOpened(kind);
            }
        }

        [HarmonyPatch(typeof(APopupWindow), nameof(APopupWindow.CloseWindow), new Type[0])]
        [HarmonyPostfix]
        public static void APopupWindow_Close_Postfix(APopupWindow __instance)
        {
            if (S == null || !S.Net.IsHost || __instance == null) return;
            var kind = ClassifyPopup(__instance);
            if (kind != CoopModalKind.None)
                S.Modals.NotifyHostPopupClosed(kind);
        }

        [HarmonyPatch(typeof(QuestManager), nameof(QuestManager.OnRequestLoadQuestInGame), new Type[0])]
        [HarmonyPostfix]
        public static void QuestManager_LoadInGame_Postfix()
        {
            if (S != null && S.Net.IsHost && !NetworkScope.IsApplying)
                S.Quests.BroadcastQuestSnapshot(-1);
        }

        [HarmonyPatch(typeof(AQuestBase), nameof(AQuestBase.SwitchStatus), new[] { typeof(eQuestStatus) })]
        [HarmonyPostfix]
        public static void AQuestBase_SwitchStatus_Postfix(AQuestBase __instance, eQuestStatus status)
        {
            if (S == null || !S.Net.IsConnected || NetworkScope.IsApplying || __instance == null) return;
            if (S.Net.IsHost)
                S.Quests.BroadcastQuestProgress();
            else if (S.Net.IsClient)
                S.Quests.ClientNotifyQuestTrigger(__instance.challengeType, status);
        }

        [HarmonyPatch(typeof(UI_QuestStatus), nameof(UI_QuestStatus.OnUpdateQuestUIProgressText), new[] { typeof(string) })]
        [HarmonyPostfix]
        public static void UI_QuestStatus_Progress_Postfix()
        {
            if (S != null && S.Net.IsHost && !NetworkScope.IsApplying)
                S.Quests.BroadcastQuestProgress();
        }

        [HarmonyPatch(typeof(AchievementManager), nameof(AchievementManager.UnlockAchievement), new[] { typeof(eAchievementType) })]
        [HarmonyPostfix]
        public static void AchievementManager_Unlock_Postfix(eAchievementType type)
        {
            if (S != null && S.Net.IsHost && !NetworkScope.IsApplying)
                S.Quests.BroadcastAchievementUnlock(type);
        }

        private static CoopModalKind ClassifyPopup(APopupWindow popup)
        {
            string t = popup.GetIl2CppType()?.Name ?? "";
            if (t.Contains("TetrisDraft")) return CoopModalKind.TetrisDraft;
            if (t.Contains("ChooseRewardCard")) return CoopModalKind.RewardDraft;
            if (t.Contains("DiscoverCard")) return CoopModalKind.DiscoverChest;
            if (t.Contains("MiniShop")) return CoopModalKind.MiniShop;
            if (t.Contains("Shop")) return CoopModalKind.Shop;
            if (t.Contains("MiniWorkshop")) return CoopModalKind.MiniWorkshop;
            if (t.Contains("Workshop") || t.Contains("ModifyBlock") || t.Contains("RemoveCard")) return CoopModalKind.Workshop;
            if (t.Contains("AddRuneToTetris")) return CoopModalKind.RuneForge;
            if (t.Contains("Altar")) return CoopModalKind.Altar;
            if (t.Contains("Campsite") || t.Contains("EmberRecover")) return CoopModalKind.Campsite;
            if (t.Contains("Circus")) return CoopModalKind.Circus;
            if (t.Contains("SecretVault")) return CoopModalKind.SecretVault;
            if (t.Contains("Academy")) return CoopModalKind.Academy;
            if (t.Contains("QuestReward")) return CoopModalKind.QuestReward;
            if (t.Contains("Quest")) return CoopModalKind.QuestSelect;
            if (t.Contains("RoguelitePerk") || t.Contains("ScrapMasterPerk") || t.Contains("SkeletonKingPerk") || t.Contains("KingStrategy")) return CoopModalKind.RoguelitePerk;
            if (t.Contains("TowerArrange")) return CoopModalKind.TowerArrange;
            if (t.Contains("Victory")) return CoopModalKind.Victory;
            if (t.Contains("Defeat")) return CoopModalKind.Defeat;
            return CoopModalKind.None;
        }
    }
}
