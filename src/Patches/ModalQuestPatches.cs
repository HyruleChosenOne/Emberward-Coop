using System;
using EmberwardCoop.Core;
using EmberwardCoop.Localization;
using EmberwardCoop.Sync;
using HarmonyLib;

namespace EmberwardCoop.Patches
{
    [HarmonyPatch]
    internal static class ModalQuestPatches
    {
        private static SessionController S => SessionController.Instance;

        [HarmonyPatch(typeof(APopupWindow), nameof(APopupWindow.ShowWindow), new Type[0])]
        [HarmonyPrefix]
        public static bool APopupWindow_Show_Prefix(APopupWindow __instance)
        {
            if (__instance == null || S == null || !S.Net.IsConnected || NetworkScope.IsApplying) return true;

            // Deterministically seed all draft, reward, chest, perk, rune, and Academy popups on both Host and Clients.
            // In Shared Resources mode (salt = 0), Host and Clients roll identical choices on their vanilla popups.
            // In Separate Resources mode (salt = LocalPlayerId * 73856093), each player rolls independent random choices.
            var kind = ModalAndRewardSync.ClassifyPopup(__instance);
            if (kind == CoopModalKind.Academy ||
                kind == CoopModalKind.TetrisDraft ||
                kind == CoopModalKind.RewardDraft ||
                kind == CoopModalKind.DiscoverChest ||
                kind == CoopModalKind.RoguelitePerk ||
                kind == CoopModalKind.RuneForge ||
                kind == CoopModalKind.QuestSelect ||
                kind == CoopModalKind.QuestReward ||
                kind == CoopModalKind.Altar ||
                kind == CoopModalKind.Shop ||
                kind == CoopModalKind.MiniShop)
            {
                int baseSeed = S.Run.PendingStageSeed != 0
                    ? S.Run.PendingStageSeed
                    : (GameDataManager.instance?.GameplayData?.randomSeed ?? 424242);
                int detSeed = S.Votes.GetNextDeterministicDraftSeed(baseSeed, S.Net.LocalPlayerId);
                UnityEngine.Random.InitState(detSeed);
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
                    var id = GameDataManager.instance?.IntermediateData;
                    S.Quests.BroadcastMetaRewardGrant(
                        25,
                        isStageVictory: false,
                        isFinalBossVictory: false,
                        worldType: gd != null ? gd.curWorld : eWorldType.WORLD_1_FOREST,
                        difficulty: gd != null ? gd.difficulty : eGameDifficultyType.NORMAL,
                        stageType: id != null ? id.stageType : eStageType.BATTLE,
                        mapStep: gd != null ? gd.curMapStep : 0,
                        characterType: gd != null ? gd.characterType : eCharacterType.BASIC);
                }
                UI.PostRunSummaryOverlay.AttachToPopup(__instance.transform, isVictory: false);
            }

            var charPopup = __instance.TryCast<UI_SelectCharacterPopup>();
            if (charPopup != null)
            {
                bool lockForClient = S.Net.IsClient;
                GameRefs.SetButtonLocked(charPopup.button_OK, lockForClient);
                GameRefs.SetButtonLocked(charPopup.button_Ember, lockForClient);
                GameRefs.SetButtonLocked(charPopup.button_SelectEmber_Arrow_L, lockForClient);
                GameRefs.SetButtonLocked(charPopup.button_SelectEmber_Arrow_R, lockForClient);
                if (lockForClient)
                {
                    if (charPopup.text_Title != null)
                        charPopup.text_Title.text = CoopLoc.T("hero.waiting_for_host");
                    GameRefs.ShowGameToast(CoopLoc.T("hero.waiting_for_host"), 3.0f);
                }
            }

            var igCharPopup = __instance.TryCast<UI_InGameSelectCharacterPopup>();
            if (igCharPopup != null)
            {
                bool lockForClient = S.Net.IsClient;
                GameRefs.SetButtonLocked(igCharPopup.button_Confirm, lockForClient);
                if (lockForClient)
                    GameRefs.ShowGameToast(CoopLoc.T("hero.waiting_for_host"), 3.0f);
            }

            if (S.Net.IsClient)
            {
                S.Run.MenuLockDirty = true;
                return;
            }

            if (S.Net.IsHost)
            {
                var kind = ModalAndRewardSync.ClassifyPopup(__instance);
                if (kind != CoopModalKind.None)
                    S.Modals.NotifyHostPopupOpened(kind);
            }
        }

        [HarmonyPatch(typeof(APopupWindow), nameof(APopupWindow.CloseWindow), new Type[0])]
        [HarmonyPostfix]
        public static void APopupWindow_Close_Postfix(APopupWindow __instance)
        {
            if (S == null || !S.Net.IsHost || __instance == null) return;
            var kind = ModalAndRewardSync.ClassifyPopup(__instance);
            if (kind != CoopModalKind.None)
            {
                S.Modals.NotifyHostPopupClosed(kind);
                if (!S.Ruleset.AllowsClientIndependentDraftsAndShops)
                    S.Votes.BroadcastSharedVanillaDraftSync(SharedVanillaSyncAction.PopupCloseFinished, (int)kind);
            }
        }

        [HarmonyPatch(typeof(UI_SelectCharacterPopup), nameof(UI_SelectCharacterPopup.OnCharacterSelected), new[] { typeof(eCharacterType) })]
        [HarmonyPrefix]
        public static bool SelectCharacterPopup_OnCharacterSelected_Prefix(UI_SelectCharacterPopup __instance, eCharacterType characterType)
        {
            if (S == null || !S.Net.IsConnected || NetworkScope.IsApplying) return true;
            if (S.Net.IsClient)
            {
                GameRefs.ShowGameToast(CoopLoc.T("hero.waiting_for_host"), 1.8f);
                return false;
            }
            return true;
        }

        [HarmonyPatch(typeof(UI_SelectCharacterPopup), nameof(UI_SelectCharacterPopup.OnCharacterSelected), new[] { typeof(eCharacterType) })]
        [HarmonyPostfix]
        public static void SelectCharacterPopup_OnCharacterSelected_Postfix(UI_SelectCharacterPopup __instance)
        {
            if (S != null && S.Net.IsHost && !NetworkScope.IsApplying && __instance != null)
                S.Votes.BroadcastHomeHeroPreview(__instance.currentCharacterType, __instance.currentEmberType);
        }

        [HarmonyPatch(typeof(UI_SelectCharacterPopup), nameof(UI_SelectCharacterPopup.UpdateEmberInfo), new[] { typeof(eEmberType) })]
        [HarmonyPostfix]
        public static void SelectCharacterPopup_UpdateEmberInfo_Postfix(UI_SelectCharacterPopup __instance)
        {
            if (S != null && S.Net.IsHost && !NetworkScope.IsApplying && __instance != null)
                S.Votes.BroadcastHomeHeroPreview(__instance.currentCharacterType, __instance.currentEmberType);
        }

        [HarmonyPatch(typeof(UI_SelectCharacterPopup), nameof(UI_SelectCharacterPopup.OnClickButton_Ember), new Type[0])]
        [HarmonyPrefix]
        public static bool SelectCharacterPopup_ClickEmber_Prefix() => S == null || !S.Net.IsClient || NetworkScope.IsApplying;

        [HarmonyPatch(typeof(UI_SelectCharacterPopup), nameof(UI_SelectCharacterPopup.OnClickSelectEmberArrowL), new Type[0])]
        [HarmonyPrefix]
        public static bool SelectCharacterPopup_EmberL_Prefix() => S == null || !S.Net.IsClient || NetworkScope.IsApplying;

        [HarmonyPatch(typeof(UI_SelectCharacterPopup), nameof(UI_SelectCharacterPopup.OnClickSelectEmberArrowR), new Type[0])]
        [HarmonyPrefix]
        public static bool SelectCharacterPopup_EmberR_Prefix() => S == null || !S.Net.IsClient || NetworkScope.IsApplying;

        [HarmonyPatch(typeof(UI_SelectCharacterPopup), nameof(UI_SelectCharacterPopup.OnClickOK), new Type[0])]
        [HarmonyPrefix]
        public static bool SelectCharacterPopup_OnClickOK_Prefix(UI_SelectCharacterPopup __instance)
        {
            if (S == null || !S.Net.IsConnected || NetworkScope.IsApplying) return true;
            if (S.Net.IsClient)
            {
                GameRefs.ShowGameToast(CoopLoc.T("hero.waiting_for_host"), 1.8f);
                return false;
            }
            if (S.Net.IsHost && __instance != null)
            {
                S.Votes.BroadcastHomeHeroSelected(__instance.currentCharacterType, __instance.currentEmberType);
            }
            return true;
        }

        [HarmonyPatch(typeof(UI_InGameSelectCharacterPopup), nameof(UI_InGameSelectCharacterPopup.OnCharacterSelected), new[] { typeof(eCharacterType), typeof(bool) })]
        [HarmonyPrefix]
        public static bool InGameSelectCharacter_OnCharacterSelected_Prefix(eCharacterType characterType)
        {
            if (S == null || !S.Net.IsConnected || NetworkScope.IsApplying) return true;
            if (S.Net.IsClient)
            {
                GameRefs.ShowGameToast(CoopLoc.T("hero.waiting_for_host"), 1.8f);
                return false;
            }
            return true;
        }

        [HarmonyPatch(typeof(UI_InGameSelectCharacterPopup), nameof(UI_InGameSelectCharacterPopup.OnCharacterSelected), new[] { typeof(eCharacterType), typeof(bool) })]
        [HarmonyPostfix]
        public static void InGameSelectCharacter_OnCharacterSelected_Postfix(UI_InGameSelectCharacterPopup __instance)
        {
            if (S != null && S.Net.IsHost && !NetworkScope.IsApplying && __instance != null)
                S.Votes.BroadcastHomeHeroPreview(__instance.selectedCharacterType, (eEmberType)0);
        }

        [HarmonyPatch(typeof(UI_InGameSelectCharacterPopup), nameof(UI_InGameSelectCharacterPopup.OnClickConfirmButton), new Type[0])]
        [HarmonyPrefix]
        public static bool InGameSelectCharacter_OnClickConfirm_Prefix(UI_InGameSelectCharacterPopup __instance)
        {
            if (S == null || !S.Net.IsConnected || NetworkScope.IsApplying) return true;
            if (S.Net.IsClient)
            {
                GameRefs.ShowGameToast(CoopLoc.T("hero.waiting_for_host"), 1.8f);
                return false;
            }
            if (S.Net.IsHost && __instance != null)
            {
                S.Votes.BroadcastHomeHeroSelected(__instance.selectedCharacterType, (eEmberType)0);
            }
            return true;
        }

        [HarmonyPatch(typeof(UI_EnigmaSanctumIntro_Popup), nameof(UI_EnigmaSanctumIntro_Popup.OnCloseButtonClicked), new Type[0])]
        [HarmonyPostfix]
        public static void EnigmaIntro_Close_Postfix()
        {
            if (S != null && S.Net.IsHost && !NetworkScope.IsApplying)
                S.Votes.BroadcastSharedVanillaDraftSync(SharedVanillaSyncAction.EnigmaIntroClose);
        }

        [HarmonyPatch(typeof(UI_MapScene_Academy_Popup), nameof(UI_MapScene_Academy_Popup.OnCardSetClickedCallback), new[] { typeof(int) })]
        [HarmonyPrefix]
        public static bool Academy_OnCardSetClicked_Prefix(int index)
        {
            if (S == null || !S.Net.IsConnected || NetworkScope.IsApplying) return true;
            if (S.Ruleset.AllowsClientIndependentDraftsAndShops) return true;

            if (S.Net.IsClient)
            {
                S.Votes.VoteSharedDraftOption(index);
                return false;
            }
            if (S.Net.IsHost)
                S.Votes.BroadcastSharedVanillaDraftSync(SharedVanillaSyncAction.AcademySelectCardSet, index);
            return true;
        }

        [HarmonyPatch(typeof(UI_MapScene_Academy_Popup), nameof(UI_MapScene_Academy_Popup.OnRelicCardClickedCallback), new[] { typeof(UI_Obj_ShopCard) })]
        [HarmonyPrefix]
        public static bool Academy_OnRelicCardClicked_Prefix(UI_MapScene_Academy_Popup __instance, UI_Obj_ShopCard card)
        {
            if (S == null || !S.Net.IsConnected || NetworkScope.IsApplying || __instance == null || card == null) return true;
            if (S.Ruleset.AllowsClientIndependentDraftsAndShops) return true;

            int idx = __instance.list_CreatedRelicCards != null ? __instance.list_CreatedRelicCards.IndexOf(card) : -1;
            if (S.Net.IsClient)
            {
                if (idx >= 0) S.Votes.VoteSharedDraftOption(idx);
                return false;
            }
            if (S.Net.IsHost && idx >= 0)
                S.Votes.BroadcastSharedVanillaDraftSync(SharedVanillaSyncAction.AcademySelectRelic, idx);
            return true;
        }

        [HarmonyPatch(typeof(UI_MapScene_Academy_Popup), nameof(UI_MapScene_Academy_Popup.OnRerollButtonClickedCallback), new Type[0])]
        [HarmonyPrefix]
        public static bool Academy_OnRerollAll_Prefix()
        {
            if (S == null || !S.Net.IsConnected || NetworkScope.IsApplying) return true;
            if (S.Ruleset.AllowsClientIndependentDraftsAndShops) return true;

            if (S.Net.IsClient)
            {
                S.Votes.RequestSharedDraftReroll();
                return false;
            }
            if (S.Net.IsHost)
            {
                int seed = S.Votes.GetNextDeterministicDraftSeed(S.Run.PendingStageSeed, 0);
                UnityEngine.Random.InitState(seed);
                S.Votes.BroadcastSharedVanillaDraftSync(SharedVanillaSyncAction.AcademyRerollAll, seed);
            }
            return true;
        }

        [HarmonyPatch(typeof(UI_MapScene_Academy_Popup), nameof(UI_MapScene_Academy_Popup.OnRequestAcademyCardSetReroll), new[] { typeof(int) })]
        [HarmonyPrefix]
        public static bool Academy_OnRerollSet_Prefix(int index)
        {
            if (S == null || !S.Net.IsConnected || NetworkScope.IsApplying) return true;
            if (S.Ruleset.AllowsClientIndependentDraftsAndShops) return true;

            if (S.Net.IsClient)
            {
                S.Votes.RequestSharedDraftReroll();
                return false;
            }
            if (S.Net.IsHost)
            {
                int seed = S.Votes.GetNextDeterministicDraftSeed(S.Run.PendingStageSeed, 0);
                UnityEngine.Random.InitState(seed);
                S.Votes.BroadcastSharedVanillaDraftSync(SharedVanillaSyncAction.AcademyRerollCardSet, index, seed);
            }
            return true;
        }

        [HarmonyPatch(typeof(UI_ChooseRewardCardPopup), nameof(UI_ChooseRewardCardPopup.OnCardClickedCallback), new[] { typeof(UI_Obj_ShopCard) })]
        [HarmonyPrefix]
        public static bool ChooseRewardCard_OnCardClicked_Prefix(UI_ChooseRewardCardPopup __instance, UI_Obj_ShopCard targetCard)
        {
            if (S == null || !S.Net.IsConnected || NetworkScope.IsApplying || __instance == null || targetCard == null) return true;
            if (S.Ruleset.AllowsClientIndependentDraftsAndShops) return true;

            if (S.Net.IsClient)
            {
                int idx = __instance.list_Cards != null ? __instance.list_Cards.IndexOf(targetCard) : -1;
                if (idx >= 0) S.Votes.VoteSharedDraftOption(idx);
                return false;
            }
            return true;
        }

        [HarmonyPatch(typeof(UI_ChooseRewardCardPopup), nameof(UI_ChooseRewardCardPopup.OnClickButton_Reroll), new Type[0])]
        [HarmonyPrefix]
        public static bool ChooseRewardCard_OnReroll_Prefix()
        {
            if (S == null || !S.Net.IsConnected || NetworkScope.IsApplying) return true;
            if (S.Ruleset.AllowsClientIndependentDraftsAndShops) return true;

            if (S.Net.IsClient)
            {
                S.Votes.RequestSharedDraftReroll();
                return false;
            }
            if (S.Net.IsHost)
            {
                int seed = S.Votes.GetNextDeterministicDraftSeed(S.Run.PendingStageSeed, 0);
                UnityEngine.Random.InitState(seed);
                S.Votes.BroadcastSharedVanillaDraftSync(SharedVanillaSyncAction.RewardReroll, seed);
            }
            return true;
        }

        [HarmonyPatch(typeof(UI_DiscoverCard_Popup), nameof(UI_DiscoverCard_Popup.OnCardClickedCallback), new[] { typeof(UI_Obj_DiscoverRewardPack), typeof(DiscoverRewardPack), typeof(Il2CppSystem.Collections.Generic.List<UnityEngine.Transform>) })]
        [HarmonyPrefix]
        public static bool DiscoverCard_OnCardClicked_Prefix(UI_DiscoverCard_Popup __instance, UI_Obj_DiscoverRewardPack ui_Obj_DiscoverRewardPack)
        {
            if (S == null || !S.Net.IsConnected || NetworkScope.IsApplying || __instance == null || ui_Obj_DiscoverRewardPack == null) return true;
            if (S.Ruleset.AllowsClientIndependentDraftsAndShops) return true;

            if (S.Net.IsClient)
            {
                int idx = __instance.list_DiscoverRewardPacks != null ? __instance.list_DiscoverRewardPacks.IndexOf(ui_Obj_DiscoverRewardPack) : -1;
                if (idx >= 0) S.Votes.VoteSharedDraftOption(idx);
                return false;
            }
            return true;
        }

        [HarmonyPatch(typeof(UI_ChooseRoguelitePerk_Popup), nameof(UI_ChooseRoguelitePerk_Popup.OnCardClicked), new[] { typeof(Obj_UI_PerkCard), typeof(PerkSettingData), typeof(PerkSettingData), typeof(eItemType) })]
        [HarmonyPrefix]
        public static bool ChooseRoguelitePerk_OnCardClicked_Prefix(UI_ChooseRoguelitePerk_Popup __instance, Obj_UI_PerkCard card)
        {
            if (S == null || !S.Net.IsConnected || NetworkScope.IsApplying || __instance == null || card == null) return true;
            if (S.Ruleset.AllowsClientIndependentDraftsAndShops) return true;

            if (S.Net.IsClient)
            {
                int idx = __instance.list_PerkCards != null ? __instance.list_PerkCards.IndexOf(card) : -1;
                if (idx >= 0) S.Votes.VoteSharedDraftOption(idx);
                return false;
            }
            return true;
        }

        [HarmonyPatch(typeof(UI_AddRuneToTetrisCard_Popup), nameof(UI_AddRuneToTetrisCard_Popup.OnRuneCardClicked), new[] { typeof(UI_Obj_ShopCard) })]
        [HarmonyPrefix]
        public static bool AddRune_OnRuneCardClicked_Prefix(UI_AddRuneToTetrisCard_Popup __instance, UI_Obj_ShopCard card)
        {
            if (S == null || !S.Net.IsConnected || NetworkScope.IsApplying || __instance == null || card == null) return true;
            if (S.Ruleset.AllowsClientIndependentDraftsAndShops) return true;

            if (S.Net.IsClient)
            {
                int idx = __instance.list_RuneCards != null ? __instance.list_RuneCards.IndexOf(card) : -1;
                if (idx >= 0) S.Votes.VoteSharedDraftOption(idx);
                return false;
            }
            return true;
        }

        [HarmonyPatch(typeof(UI_TetrisDraft_Popup), nameof(UI_TetrisDraft_Popup.OnCardClicked), new[] { typeof(UI_Obj_ShopCard) })]
        [HarmonyPrefix]
        public static bool TetrisDraft_OnCardClicked_Prefix(UI_TetrisDraft_Popup __instance, UI_Obj_ShopCard card)
        {
            if (S == null || !S.Net.IsConnected || NetworkScope.IsApplying || __instance == null || card == null) return true;
            if (S.Ruleset.AllowsClientIndependentDraftsAndShops) return true;

            int idx = __instance.list_TetrisCandidateCards != null ? __instance.list_TetrisCandidateCards.IndexOf(card) : -1;
            if (S.Net.IsClient)
            {
                GameRefs.ShowGameToast(CoopLoc.T("draft.shared_vote_hint"), 1.6f);
                return false;
            }
            if (S.Net.IsHost && idx >= 0)
                S.Votes.BroadcastSharedVanillaDraftSync(SharedVanillaSyncAction.TetrisDraftToggleCard, idx);
            return true;
        }

        [HarmonyPatch(typeof(UI_TetrisDraft_Popup), nameof(UI_TetrisDraft_Popup.OnClickButton_OK), new Type[0])]
        [HarmonyPrefix]
        public static bool TetrisDraft_OnClickOK_Prefix()
        {
            if (S == null || !S.Net.IsConnected || NetworkScope.IsApplying) return true;
            if (S.Ruleset.AllowsClientIndependentDraftsAndShops) return true;

            if (S.Net.IsClient) return false;
            if (S.Net.IsHost)
                S.Votes.BroadcastSharedVanillaDraftSync(SharedVanillaSyncAction.TetrisDraftConfirm);
            return true;
        }

        [HarmonyPatch(typeof(UI_TetrisDraft_Popup), nameof(UI_TetrisDraft_Popup.OnClickButton_Reset), new Type[0])]
        [HarmonyPrefix]
        public static bool TetrisDraft_OnClickReset_Prefix()
        {
            if (S == null || !S.Net.IsConnected || NetworkScope.IsApplying) return true;
            if (S.Ruleset.AllowsClientIndependentDraftsAndShops) return true;

            if (S.Net.IsClient) return false;
            if (S.Net.IsHost)
                S.Votes.BroadcastSharedVanillaDraftSync(SharedVanillaSyncAction.TetrisDraftReset);
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
    }
}
