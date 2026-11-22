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

        [HarmonyPatch(typeof(APopupWindow), nameof(APopupWindow.ShowWindowProc), new Type[0])]
        [HarmonyPostfix]
        public static void APopupWindow_Show_Postfix(APopupWindow __instance)
        {
            if (S == null || !S.Net.IsConnected || __instance == null) return;
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

        [HarmonyPatch(typeof(APopupWindow), nameof(APopupWindow.CloseWindowProc), new Type[0])]
        [HarmonyPostfix]
        public static void APopupWindow_Close_Postfix(APopupWindow __instance)
        {
            if (S == null || !S.Net.IsHost || __instance == null) return;
            var kind = ClassifyPopup(__instance);
            if (kind != CoopModalKind.None)
                S.Modals.NotifyHostPopupClosed(kind);
        }

        // Close duplicate client reward popups in Host-Shared mode; salt RNG per player in Independent mode.
        [HarmonyPatch(typeof(UI_ChooseRewardCardPopup), nameof(UI_ChooseRewardCardPopup.ShowWindowProc), new Type[0])]
        [HarmonyPrefix]
        public static bool ChooseRewardCard_Show_Prefix(UI_ChooseRewardCardPopup __instance)
        {
            if (S != null && S.Net.IsClient && !NetworkScope.IsApplying)
            {
                if (!S.Ruleset.AllowsClientIndependentDraftsAndShops)
                {
                    __instance.OverrideIsWindowFinished(true);
                    __instance.CloseWindow();
                    return false;
                }
                int salt = S.Ruleset.ComputeDraftSeedSalt(S.Net.LocalPlayerId);
                if (salt != 0) UnityEngine.Random.InitState(unchecked(S.Run.PendingStageSeed ^ salt ^ Environment.TickCount));
            }
            return true;
        }

        [HarmonyPatch(typeof(UI_AddRuneToTetrisCard_Popup), nameof(UI_AddRuneToTetrisCard_Popup.ShowWindowProc), new Type[0])]
        [HarmonyPrefix]
        public static bool AddRunePopup_Show_Prefix(UI_AddRuneToTetrisCard_Popup __instance)
        {
            if (S != null && S.Net.IsClient && !NetworkScope.IsApplying && !S.Ruleset.AllowsClientIndependentDraftsAndShops)
            {
                __instance.OverrideIsWindowFinished(true);
                __instance.CloseWindow();
                return false;
            }
            return true;
        }

        [HarmonyPatch(typeof(UI_QuestReward_Popup), nameof(UI_QuestReward_Popup.ShowWindowProc), new Type[0])]
        [HarmonyPrefix]
        public static bool QuestRewardPopup_Show_Prefix(UI_QuestReward_Popup __instance)
        {
            if (S != null && S.Net.IsClient && !NetworkScope.IsApplying && !S.Ruleset.AllowsClientIndependentDraftsAndShops)
            {
                __instance.OverrideIsWindowFinished(true);
                __instance.CloseWindow();
                return false;
            }
            return true;
        }

        [HarmonyPatch(typeof(UI_DiscoverCard_Popup), nameof(UI_DiscoverCard_Popup.ShowWindowProc), new Type[0])]
        [HarmonyPrefix]
        public static bool DiscoverCardPopup_Show_Prefix(UI_DiscoverCard_Popup __instance)
        {
            if (__instance != null && S != null && S.Net != null && S.Net.IsClient && !NetworkScope.IsApplying)
            {
                if (S.Ruleset != null && !S.Ruleset.AllowsClientIndependentDraftsAndShops)
                {
                    __instance.OverrideIsWindowFinished(true);
                    __instance.CloseWindow();
                    return false;
                }
                if (S.Ruleset != null && S.Run != null)
                {
                    int salt = S.Ruleset.ComputeDraftSeedSalt(S.Net.LocalPlayerId);
                    if (salt != 0) UnityEngine.Random.InitState(unchecked(S.Run.PendingStageSeed ^ salt ^ Environment.TickCount));
                }
            }
            return true;
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
