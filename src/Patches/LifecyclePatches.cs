using System;
using EmberwardCoop.Core;
using EmberwardCoop.Rules;
using EmberwardCoop.Serialization;
using EmberwardCoop.Sync;
using HarmonyLib;
using UnityEngine;

namespace EmberwardCoop.Patches
{
    [HarmonyPatch]
    internal static class LifecyclePatches
    {
        private static SessionController S => SessionController.Instance;

        [HarmonyPatch(typeof(RefiUtilities.SceneLoader), nameof(RefiUtilities.SceneLoader.Load), new[] { typeof(string) })]
        [HarmonyPrefix]
        public static bool SceneLoader_Load_Prefix(string targetSceneName)
        {
            if (S == null || !S.Net.IsConnected) return true;

            if (S.Net.IsClient && !NetworkScope.IsApplying)
            {
                CoopLogger.Info(LogCategory.Sync, $"Blocked unauthorized client scene transition to '{targetSceneName}' — following Host.");
                return false;
            }

            if (S.Net.IsHost)
            {
                if (targetSceneName == "CoinPage" && !GameRefs.IsInTitle)
                    S.SaveActiveCoopRun("return_to_title");

                S.Run.HostTargetScene = targetSceneName;
                S.Run.HostSceneLoadSeq++;
                CoopLogger.Info(LogCategory.Sync, $"Host loading '{targetSceneName}' — syncing peers.");
                S.Run.BroadcastRunSnapshot(-1, targetSceneName);
            }
            return true;
        }

        [HarmonyPatch(typeof(GameDataManager), nameof(GameDataManager.StartNewGame), new[] { typeof(int), typeof(eWorldType), typeof(eGameDifficultyType) })]
        [HarmonyPostfix]
        public static void GameDataManager_StartNewGame_Postfix(int seed, eWorldType worldType, eGameDifficultyType difficulty)
        {
            if (S == null || !S.Net.IsHost || NetworkScope.IsApplying) return;
            S.Run.HostSelectedMapNodeIndex = -1;
            S.Run.PendingStageSeed = seed;
            S.Votes.ClearMapVotes();
            CoopLogger.Info(LogCategory.Sync, $"New Co-op Run started (Seed {seed}, {worldType}, {difficulty}).");
            S.Run.BroadcastRunSnapshot(-1);
        }

        [HarmonyPatch(typeof(GameDataManager), nameof(GameDataManager.Save), new Type[0])]
        [HarmonyPrefix]
        public static bool GameDataManager_Save_Prefix()
        {
            if (!SoloSaveGuard.IsInIsolatedCoopSession || NetworkScope.IsClientMetaSaveAllowed) return true;
            if (S != null && S.Net.IsHost && !GameRefs.IsInTitle)
                S.SaveActiveCoopRun("vanilla_save");
            return false;
        }

        [HarmonyPatch(typeof(GameDataManager), nameof(GameDataManager.SaveData), new Type[0])]
        [HarmonyPrefix]
        public static bool GameDataManager_SaveData0_Prefix()
        {
            if (!SoloSaveGuard.IsInIsolatedCoopSession || NetworkScope.IsClientMetaSaveAllowed) return true;
            if (S != null && S.Net.IsHost && !GameRefs.IsInTitle)
                S.SaveActiveCoopRun("vanilla_savedata");
            return false;
        }

        [HarmonyPatch(typeof(GameDataManager), nameof(GameDataManager.SaveData), new[] { typeof(bool) })]
        [HarmonyPrefix]
        public static bool GameDataManager_SaveData1_Prefix()
        {
            if (!SoloSaveGuard.IsInIsolatedCoopSession || NetworkScope.IsClientMetaSaveAllowed) return true;
            if (S != null && S.Net.IsHost && !GameRefs.IsInTitle)
                S.SaveActiveCoopRun("vanilla_savedata_flag");
            return false;
        }

        [HarmonyPatch(typeof(GameDataManager), nameof(GameDataManager.SerializeCurrentDataToCache), new[] { typeof(string), typeof(bool) })]
        [HarmonyPrefix]
        public static void GameDataManager_SerializeCache_Prefix(GameDataManager __instance, out bool __state)
            => SoloSaveGuard.BeginSoloSafeSerialize(__instance, out __state);

        [HarmonyPatch(typeof(GameDataManager), nameof(GameDataManager.SerializeCurrentDataToCache), new[] { typeof(string), typeof(bool) })]
        [HarmonyFinalizer]
        public static void GameDataManager_SerializeCache_Finalizer(GameDataManager __instance, bool __state)
            => SoloSaveGuard.EndSoloSafeSerialize(__instance, __state);

        // Co-op-only memory unlock overrides: strictly active in memory while inside the Co-op Menu
        // or an active Co-op session with "Unlock All Maps/Modes (Co-op Only)" enabled.
        [HarmonyPatch(typeof(PlayerLifetimeData), nameof(PlayerLifetimeData.IsWorldUnlocked), new[] { typeof(eGameDifficultyType), typeof(eWorldType) })]
        [HarmonyPostfix]
        public static void PlayerLifetimeData_IsWorldUnlocked_Postfix(ref bool __result)
        {
            if (!__result && PartyUnlockResolver.IsCoopUnlockAllEffective())
                __result = true;
        }

        [HarmonyPatch(typeof(PlayerLifetimeData), nameof(PlayerLifetimeData.IsWorldUnlockedInAnyDifficulty), new[] { typeof(eWorldType) })]
        [HarmonyPostfix]
        public static void PlayerLifetimeData_IsWorldUnlockedAny_Postfix(ref bool __result)
        {
            if (!__result && PartyUnlockResolver.IsCoopUnlockAllEffective())
                __result = true;
        }

        [HarmonyPatch(typeof(PlayerLifetimeData), nameof(PlayerLifetimeData.IsCharacterUnlocked), new[] { typeof(eCharacterType) })]
        [HarmonyPostfix]
        public static void PlayerLifetimeData_IsCharacterUnlocked_Postfix(ref bool __result)
        {
            if (!__result && PartyUnlockResolver.IsCoopUnlockAllEffective())
                __result = true;
        }

        [HarmonyPatch(typeof(PlayerLifetimeData), nameof(PlayerLifetimeData.IsAnyEndlessModeUnlocked), new Type[0])]
        [HarmonyPostfix]
        public static void PlayerLifetimeData_IsAnyEndlessModeUnlocked_Postfix(ref bool __result)
        {
            if (!__result && PartyUnlockResolver.IsCoopUnlockAllEffective())
                __result = true;
        }

        [HarmonyPatch(typeof(PlayerLifetimeData), nameof(PlayerLifetimeData.IsEnigmaSanctumUnlocked), new Type[0])]
        [HarmonyPostfix]
        public static void PlayerLifetimeData_IsEnigmaSanctumUnlocked_Postfix(ref bool __result)
        {
            if (!__result && PartyUnlockResolver.IsCoopUnlockAllEffective())
                __result = true;
        }

        [HarmonyPatch(typeof(CoinPage), nameof(CoinPage.OnClick_StartGame), new Type[0])]
        [HarmonyPrefix]
        public static bool CoinPage_StartGame_Prefix()
        {
            if (S != null && S.Net.IsClient)
            {
                GameRefs.ShowGameToast("Waiting for Co-op Host to start run", 2.0f);
                return false;
            }
            return true;
        }

        [HarmonyPatch(typeof(CoinPage), nameof(CoinPage.OnClick_Continue), new Type[0])]
        [HarmonyPrefix]
        public static bool CoinPage_Continue_Prefix()
        {
            if (S != null && S.Net.IsClient)
            {
                GameRefs.ShowGameToast("Waiting for Co-op Host to continue run", 2.0f);
                return false;
            }
            return true;
        }

        [HarmonyPatch(typeof(CoinPage), nameof(CoinPage.OnRequestActivateEndlessMode), new[] { typeof(EndlessMapActivateData) })]
        [HarmonyPrefix]
        public static bool CoinPage_Endless_Prefix() => S == null || !S.Net.IsClient || NetworkScope.IsApplying;

        [HarmonyPatch(typeof(UI_SocialMediaButtons), nameof(UI_SocialMediaButtons.OnClickButton_SaveSlot), new Type[0])]
        [HarmonyPrefix]
        public static bool SaveSlot_Prefix() => S == null || !S.Net.IsClient;

        [HarmonyPatch(typeof(Map), nameof(Map.VisualizeMap), new[] { typeof(MapGenerateSetting), typeof(MapData) })]
        [HarmonyPostfix]
        public static void Map_VisualizeMap_Postfix()
        {
            if (S != null && S.Net.IsHost)
                S.Run.BroadcastRunSnapshot(-1, "MapScene");
        }

        [HarmonyPatch(typeof(MapSceneProcess), nameof(MapSceneProcess.OnClickMapNodeButton), new[] { typeof(MapNode) })]
        [HarmonyPrefix]
        public static bool MapSceneProcess_OnClickMapNodeButton_Prefix(MapNode clickedNode)
        {
            if (S == null || !S.Net.IsConnected || NetworkScope.IsApplying) return true;
            if (clickedNode?.mapNodeData == null) return true;

            if (S.Net.IsClient)
            {
                S.Votes.CastMapNodeVote(clickedNode.mapNodeData.Index);
                return false;
            }

            if (S.Net.IsHost)
            {
                if (S.Net.RemotePeerCount > 0)
                {
                    S.Votes.CastMapNodeVote(clickedNode.mapNodeData.Index);
                    return false;
                }

                S.Run.HostSelectedMapNodeIndex = clickedNode.mapNodeData.Index;
                S.Run.BroadcastRunSnapshot(-1, "MapScene");
            }
            return true;
        }

        [HarmonyPatch(typeof(MapSceneProcess), nameof(MapSceneProcess.SetSelectedMapNode), new[] { typeof(MapNode) })]
        [HarmonyPostfix]
        public static void MapSceneProcess_SetSelectedMapNode_Postfix(MapNode mapNode)
        {
            if (S != null && S.Net.IsHost && mapNode?.mapNodeData != null)
                S.Run.HostSelectedMapNodeIndex = mapNode.mapNodeData.Index;
        }

        [HarmonyPatch(typeof(UI_Button_BackToTitle), nameof(UI_Button_BackToTitle.OnButtonClick), new Type[0])]
        [HarmonyPrefix]
        public static bool BackToTitle_OnButtonClick_Prefix()
        {
            if (S != null && S.Net.IsClient) return false;
            if (S != null && S.Net.IsHost) S.SaveActiveCoopRun("map_back_to_title");
            return true;
        }

        [HarmonyPatch(typeof(UI_PauseMenu), nameof(UI_PauseMenu.OnClickButton_QuickRestart), new Type[0])]
        [HarmonyPrefix]
        public static bool Pause_QuickRestart_Prefix() => S == null || !S.Net.IsClient;

        [HarmonyPatch(typeof(UI_PauseMenu), nameof(UI_PauseMenu.OnClickButton_EndChallenge), new Type[0])]
        [HarmonyPrefix]
        public static bool Pause_EndChallenge_Prefix() => S == null || !S.Net.IsClient;

        [HarmonyPatch(typeof(UI_PauseMenu), nameof(UI_PauseMenu.OnClickButton_MainMenu), new Type[0])]
        [HarmonyPrefix]
        public static bool Pause_MainMenu_Prefix()
        {
            if (S != null && S.Net.IsClient) return false;
            if (S != null && S.Net.IsHost) S.SaveActiveCoopRun("pause_main_menu");
            return true;
        }

        [HarmonyPatch(typeof(UI_Victory_Popup), nameof(UI_Victory_Popup.OnClickContinue), new Type[0])]
        [HarmonyPrefix]
        public static bool Victory_Continue_Prefix()
        {
            if (S == null || !S.Net.IsConnected) return true;
            if (S.Net.IsClient) return false;
            if (S.Net.IsHost && !S.Votes.CanHostAdvancePastBarrier(out string reason))
            {
                GameRefs.ShowGameToast(reason, 2.5f);
                return false;
            }
            return true;
        }

        [HarmonyPatch(typeof(UI_Victory_Popup), nameof(UI_Victory_Popup.OnClickBackToMenu), new Type[0])]
        [HarmonyPrefix]
        public static bool Victory_BackToMenu_Prefix() => S == null || !S.Net.IsClient;

        [HarmonyPatch(typeof(UI_Victory_Popup), nameof(UI_Victory_Popup.OnClickMoreLevelsButton), new Type[0])]
        [HarmonyPrefix]
        public static bool Victory_MoreLevels_Prefix() => S == null || !S.Net.IsClient;

        [HarmonyPatch(typeof(UI_Victory_Popup), nameof(UI_Victory_Popup.OnClickMoreWavesButton), new Type[0])]
        [HarmonyPrefix]
        public static bool Victory_MoreWaves_Prefix() => S == null || !S.Net.IsClient;

        [HarmonyPatch(typeof(UI_Defeat_Popup), nameof(UI_Defeat_Popup.OnClick_BackToTitle), new Type[0])]
        [HarmonyPrefix]
        public static bool Defeat_BackToTitle_Prefix() => S == null || !S.Net.IsClient;

        [HarmonyPatch(typeof(UI_Defeat_Popup), nameof(UI_Defeat_Popup.OnClick_Retry), new Type[0])]
        [HarmonyPrefix]
        public static bool Defeat_Retry_Prefix() => S == null || !S.Net.IsClient;

        [HarmonyPatch(typeof(UI_Victory_Popup), nameof(UI_Victory_Popup.Setup), new[] { typeof(bool), typeof(UI_Victory_Popup.eResultDetailType), typeof(UI_Victory_Popup.eShieldImageType) })]
        [HarmonyPostfix]
        public static void Victory_Setup_Postfix(UI_Victory_Popup __instance, bool isFinalBoss)
        {
            if (S == null || !S.Net.IsConnected) return;
            if (S.Net.IsHost)
            {
                S.Ownership.BroadcastRunStats();
                S.Modals.BroadcastVictoryOrDefeat(isVictory: true, isFinalBoss: isFinalBoss);
                var gd = GameDataManager.instance?.GameplayData;
                var id = GameDataManager.instance?.IntermediateData;
                int exp = gd?.GetCurrentStageRewardData()?.Exp ?? 0;
                if (isFinalBoss && exp <= 0) exp = 150;
                S.Quests.BroadcastMetaRewardGrant(
                    exp,
                    isStageVictory: true,
                    isFinalBossVictory: isFinalBoss,
                    worldType: gd != null ? gd.curWorld : eWorldType.WORLD_1_FOREST,
                    difficulty: gd != null ? gd.difficulty : eGameDifficultyType.NORMAL,
                    stageType: id != null ? id.stageType : (isFinalBoss ? eStageType.BOSS : eStageType.BATTLE),
                    mapStep: gd != null ? gd.curMapStep : 0,
                    characterType: gd != null ? gd.characterType : eCharacterType.BASIC);

                if (!isFinalBoss)
                    S.SaveActiveCoopRun("stage_clear");
            }
            UI.PostRunSummaryOverlay.AttachToPopup(__instance?.transform, isVictory: true);
        }

        [HarmonyPatch(typeof(MainGame), nameof(MainGame.Awake), new Type[0])]
        [HarmonyPrefix]
        public static void MainGame_Awake_Prefix()
        {
            if (S == null || !S.Net.IsConnected) return;

            var gdm = GameDataManager.instance;
            var gd = gdm?.GameplayData;
            var id = gdm?.IntermediateData;

            if (S.Net.IsClient && id != null && !string.IsNullOrEmpty(S.Run.PendingEnvSceneName))
            {
                if (id.mapNodeData != null)
                    id.mapNodeData.stageEnvSceneName = S.Run.PendingEnvSceneName;
                if (string.IsNullOrEmpty(id.stageName))
                    id.stageName = S.Run.PendingEnvSceneName;
            }

            int stageSeed = 12345;
            if (id?.mapNodeData != null && id.mapNodeData.randomSeed != 0)
                stageSeed = id.mapNodeData.randomSeed;
            else if (S.Net.IsClient && S.Run.PendingStageSeed != 0)
                stageSeed = S.Run.PendingStageSeed;
            else if (gd != null && gd.randomSeed != 0)
                stageSeed = gd.randomSeed + gd.curMapStep * 1000 + gd.curMapNodeIndex;

            S.Run.PendingStageSeed = stageSeed;
            UnityEngine.Random.InitState(stageSeed);
        }

        [HarmonyPatch(typeof(MainGame), nameof(MainGame.Start), new Type[0])]
        [HarmonyPostfix]
        public static void MainGame_Start_Postfix()
        {
            if (S == null || !S.Net.IsConnected) return;

            var ig = MainGame.Instance?.IngameData;
            if (S.Run.PendingRestoreCoin >= 0 && ig != null)
            {
                using (NetworkScope.Enter())
                    ig.SetCoin(S.Run.PendingRestoreCoin);
                S.Run.PendingRestoreCoin = -1;
            }

            if (S.Net.IsHost)
            {
                if (S.Run.PendingRestoreBoardBytes != null)
                {
                    var boardBytes = S.Run.PendingRestoreBoardBytes;
                    S.Run.PendingRestoreBoardBytes = null;
                    S.Board.ApplyBoardSnapshotBytes(boardBytes);
                }

                CoopLogger.Info(LogCategory.Sync, "Battle Stage loaded — synchronizing all peers.");
                S.ForceFullSyncAll();
            }
            else if (S.Net.IsClient)
            {
                CoopLogger.Info(LogCategory.Sync, "Battle Stage loaded — requesting authoritative Host state.");
                S.Net.SendToHost(Networking.PacketType.RequestFullSync);
            }
        }
    }
}
