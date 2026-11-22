using System;
using EmberwardCoop.Core;
using EmberwardCoop.Sync;
using HarmonyLib;

namespace EmberwardCoop.Patches
{
    [HarmonyPatch]
    internal static class WaveCombatPatches
    {
        private static SessionController S => SessionController.Instance;

        [HarmonyPatch(typeof(StageDataReader), nameof(StageDataReader.LoadStageData), new[] { typeof(StageSettingData) })]
        [HarmonyPostfix]
        public static void StageDataReader_LoadStageData_Postfix(StageDataReader __instance)
        {
            if (S == null || !S.Net.IsConnected) return;
            S.Balance.ApplyWaveDensityScaling(__instance);
            S.Ownership.SampleBaselinePathLength();
        }

        [HarmonyPatch(typeof(AMonsterBase), nameof(AMonsterBase.Spawn), new[] { typeof(MonsterSpawner), typeof(bool), typeof(bool) })]
        [HarmonyPostfix]
        public static void Monster_Spawn_Postfix(AMonsterBase __instance)
        {
            if (S == null || !S.Net.IsConnected || __instance == null) return;
            S.Balance.ApplyMonsterScaling(__instance);
        }

        [HarmonyPatch(typeof(UI_Button_StartWave), nameof(UI_Button_StartWave.OnButtonDown), new Type[0])]
        [HarmonyPrefix]
        public static bool StartWave_OnButtonDown_Prefix()
        {
            if (S == null || !S.Net.IsConnected || NetworkScope.IsApplying) return true;
            if (S.Net.IsClient)
            {
                S.Waves.ClientRequestStartWave();
                return false;
            }
            return true;
        }

        [HarmonyPatch(typeof(SingleEventCapturer), nameof(SingleEventCapturer.OnReceiveEvent), new Type[0])]
        [HarmonyPrefix]
        public static bool SingleEventCapturer_OnReceiveEvent_Prefix(SingleEventCapturer __instance)
        {
            if (S == null || !S.Net.IsConnected || NetworkScope.IsApplying || __instance == null)
                return true;

            if (!GameRefs.IsRequestStartNextWaveEvent(__instance.eventEnum))
                return true;

            if (S.Net.IsClient)
            {
                S.Waves.ClientRequestStartWave();
                return false;
            }

            if (S.Net.IsHost)
                S.Waves.HostOnWaveStartTriggered();
            return true;
        }

        [HarmonyPatch(typeof(UI_Button_StartWave), nameof(UI_Button_StartWave.OnBattleStart), new Type[0])]
        [HarmonyPostfix]
        public static void StartWave_OnBattleStart_Postfix()
        {
            if (S != null && S.Net.IsHost && !NetworkScope.IsApplying)
                S.Waves.HostOnWaveStartTriggered();
        }

        [HarmonyPatch(typeof(StageDataReader), nameof(StageDataReader.OnBattleEnd), new Type[0])]
        [HarmonyPostfix]
        public static void StageDataReader_OnBattleEnd_Postfix()
        {
            if (S != null && S.Net.IsHost)
                S.Waves.HostOnWaveEnded();
        }

        private static bool AllowLocalSpeedInput()
        {
            if (S == null || !S.Net.IsConnected || NetworkScope.IsApplying) return true;
            if (S.Net.IsClient && !S.Ruleset.CanClientChangeGameSpeed())
            {
                GameRefs.ShowGameToast("Host controls game speed", 1.8f);
                return false;
            }
            return true;
        }

        private static void SyncGameSpeed(UI_GameSpeedControl ctrl)
        {
            if (S == null || !S.Net.IsConnected || NetworkScope.IsApplying || ctrl == null) return;
            int speed = (int)ctrl.currentGameSpeedType;
            if (S.Net.IsHost) S.Waves.BroadcastEvtSetGameSpeed(speed);
            else if (S.Net.IsClient && S.Ruleset.CanClientChangeGameSpeed()) S.Waves.ClientSendSetGameSpeed(speed);
        }

        [HarmonyPatch(typeof(UI_GameSpeedControl), nameof(UI_GameSpeedControl.OnClickButton_GameSpeed), new Type[0])]
        [HarmonyPrefix]
        public static bool GameSpeed_ClickCycle_Prefix() => AllowLocalSpeedInput();

        [HarmonyPatch(typeof(UI_GameSpeedControl), nameof(UI_GameSpeedControl.OnClickButton_GameSpeed), new Type[0])]
        [HarmonyPostfix]
        public static void GameSpeed_ClickCycle_Postfix(UI_GameSpeedControl __instance) => SyncGameSpeed(__instance);

        [HarmonyPatch(typeof(UI_GameSpeedControl), nameof(UI_GameSpeedControl.OnClickButton_GameSpeed), new[] { typeof(UI_GameSpeedControl.eGameSpeedType) })]
        [HarmonyPrefix]
        public static bool GameSpeed_ClickDirect_Prefix() => AllowLocalSpeedInput();

        [HarmonyPatch(typeof(UI_GameSpeedControl), nameof(UI_GameSpeedControl.OnClickButton_GameSpeed), new[] { typeof(UI_GameSpeedControl.eGameSpeedType) })]
        [HarmonyPostfix]
        public static void GameSpeed_ClickDirect_Postfix(UI_GameSpeedControl __instance) => SyncGameSpeed(__instance);

        [HarmonyPatch(typeof(UI_GameSpeedControl), nameof(UI_GameSpeedControl.SwitchToNextSpeed), new[] { typeof(bool) })]
        [HarmonyPrefix]
        public static bool GameSpeed_Next_Prefix() => AllowLocalSpeedInput();

        [HarmonyPatch(typeof(UI_GameSpeedControl), nameof(UI_GameSpeedControl.SwitchToNextSpeed), new[] { typeof(bool) })]
        [HarmonyPostfix]
        public static void GameSpeed_Next_Postfix(UI_GameSpeedControl __instance) => SyncGameSpeed(__instance);

        [HarmonyPatch(typeof(UI_GameSpeedControl), nameof(UI_GameSpeedControl.SwitchToFasterSpeed), new Type[0])]
        [HarmonyPrefix]
        public static bool GameSpeed_Faster_Prefix() => AllowLocalSpeedInput();

        [HarmonyPatch(typeof(UI_GameSpeedControl), nameof(UI_GameSpeedControl.SwitchToFasterSpeed), new Type[0])]
        [HarmonyPostfix]
        public static void GameSpeed_Faster_Postfix(UI_GameSpeedControl __instance) => SyncGameSpeed(__instance);

        [HarmonyPatch(typeof(UI_GameSpeedControl), nameof(UI_GameSpeedControl.SwitchToSlowerSpeed), new Type[0])]
        [HarmonyPrefix]
        public static bool GameSpeed_Slower_Prefix() => AllowLocalSpeedInput();

        [HarmonyPatch(typeof(UI_GameSpeedControl), nameof(UI_GameSpeedControl.SwitchToSlowerSpeed), new Type[0])]
        [HarmonyPostfix]
        public static void GameSpeed_Slower_Postfix(UI_GameSpeedControl __instance) => SyncGameSpeed(__instance);

        [HarmonyPatch(typeof(MonsterManager), nameof(MonsterManager.OnRequestSetFocusFireMonster), new[] { typeof(AMonsterBase) })]
        [HarmonyPrefix]
        public static bool MonsterManager_FocusFire_Prefix(AMonsterBase monster)
        {
            if (S == null || !S.Net.IsConnected || NetworkScope.IsApplying || monster == null) return true;
            if (S.Net.IsClient)
            {
                S.Board.ClientSendFocusMonster(monster.transform.position);
                return false;
            }
            if (S.Net.IsHost)
                S.Board.BroadcastEvtFocusMonster(monster.transform.position);
            return true;
        }
    }
}
