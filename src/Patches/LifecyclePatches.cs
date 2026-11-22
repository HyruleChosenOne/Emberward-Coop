using System;
using EmberwardCoop.Core;
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
            if (S == null || !S.Net.IsHost) return;
            S.Run.HostSelectedMapNodeIndex = -1;
            S.Run.PendingStageSeed = seed;
            S.Run.MapNodeVotes.Clear();
            CoopLogger.Info(LogCategory.Sync, $"New Co-op Run started (Seed {seed}, {worldType}, {difficulty}).");
            S.Run.BroadcastRunSnapshot(-1);
        }

        // Block solo-save writes on clients except during explicit PlayerLifetimeData meta-save commits.
        [HarmonyPatch(typeof(GameDataManager), nameof(GameDataManager.Save), new Type[0])]
        [HarmonyPrefix]
        public static bool GameDataManager_Save_Prefix()
            => S == null || !S.Net.IsClient || NetworkScope.IsClientMetaSaveAllowed;

        [HarmonyPatch(typeof(GameDataManager), nameof(GameDataManager.SaveData), new Type[0])]
        [HarmonyPrefix]
        public static bool GameDataManager_SaveData0_Prefix()
            => S == null || !S.Net.IsClient || NetworkScope.IsClientMetaSaveAllowed;

        [HarmonyPatch(typeof(GameDataManager), nameof(GameDataManager.SaveData), new[] { typeof(bool) })]
        [HarmonyPrefix]
        public static bool GameDataManager_SaveData1_Prefix()
            => S == null || !S.Net.IsClient || NetworkScope.IsClientMetaSaveAllowed;

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
        public static bool CoinPage_Endless_Prefix() => S == null || !S.Net.IsClient;

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

        [HarmonyPatch(typeof(MapSceneProcess), nameof(MapSceneProcess.CR_StartNodeProc), new Type[0])]
        [HarmonyPostfix]
        public static void MapSceneProcess_CR_StartNodeProc_Postfix(Il2CppSystem.Collections.IEnumerator __result)
        {
            // Abort client starting-draft coroutine in Host-Shared mode; Independent mode drafts locally.
            if (S != null && S.Net.IsClient && !S.Ruleset.AllowsClientIndependentDraftsAndShops && __result != null)
            {
                var st = __result.TryCast<MapSceneProcess._CR_StartNodeProc_d__31>();
                if (st != null) st.__1__state = -1;
            }
        }

        [HarmonyPatch(typeof(MapSceneProcess), nameof(MapSceneProcess.OnClickMapNodeButton), new[] { typeof(MapNode) })]
        [HarmonyPrefix]
        public static bool MapSceneProcess_OnClickMapNodeButton_Prefix(MapNode clickedNode)
        {
            if (S == null || !S.Net.IsConnected || NetworkScope.IsApplying) return true;

            if (S.Net.IsClient)
            {
                if (clickedNode?.mapNodeData != null)
                {
                    var st = clickedNode.mapNodeData.MapNodeType;
                    bool isCombat = st == eStageType.BATTLE || st == eStageType.BATTLE_CORRUPTED ||
                                    st == eStageType.BATTLE_ANOMALY || st == eStageType.BATTLE_DARKNESS ||
                                    st == eStageType.BOSS || st == eStageType.MID_BOSS;
                    S.Run.SendSelectOrVoteMapNode(clickedNode.mapNodeData.Index, isCombat);
                }
                return false;
            }

            if (S.Net.IsHost && clickedNode?.mapNodeData != null)
            {
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
        public static bool BackToTitle_OnButtonClick_Prefix() => S == null || !S.Net.IsClient;

        [HarmonyPatch(typeof(UI_PauseMenu), nameof(UI_PauseMenu.OnClickButton_QuickRestart), new Type[0])]
        [HarmonyPrefix]
        public static bool Pause_QuickRestart_Prefix() => S == null || !S.Net.IsClient;

        [HarmonyPatch(typeof(UI_PauseMenu), nameof(UI_PauseMenu.OnClickButton_EndChallenge), new Type[0])]
        [HarmonyPrefix]
        public static bool Pause_EndChallenge_Prefix() => S == null || !S.Net.IsClient;

        [HarmonyPatch(typeof(UI_PauseMenu), nameof(UI_PauseMenu.OnClickButton_MainMenu), new Type[0])]
        [HarmonyPrefix]
        public static bool Pause_MainMenu_Prefix() => S == null || !S.Net.IsClient;

        [HarmonyPatch(typeof(UI_Victory_Popup), nameof(UI_Victory_Popup.OnClickContinue), new Type[0])]
        [HarmonyPrefix]
        public static bool Victory_Continue_Prefix() => S == null || !S.Net.IsClient;

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
                int exp = gd?.GetCurrentStageRewardData()?.Exp ?? 0;
                if (isFinalBoss && exp <= 0) exp = 150;
                S.Quests.BroadcastMetaRewardGrant(exp, true, isFinalBoss, gd != null ? gd.curWorld : eWorldType.WORLD_1_FOREST, gd != null ? gd.difficulty : eGameDifficultyType.NORMAL);
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

            if (S.Net.IsClient && id?.mapNodeData != null && !string.IsNullOrEmpty(S.Run.PendingEnvSceneName))
                id.mapNodeData.stageEnvSceneName = S.Run.PendingEnvSceneName;

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
            if (S.Net.IsHost)
            {
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
