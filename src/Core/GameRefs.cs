using System;
using Il2CppInterop.Runtime;
using Il2CppInterop.Runtime.InteropTypes;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace EmberwardCoop.Core
{
    public static class GameRefs
    {
        private static string _activeScene = "";
        private static CoinPage _coinPage;
        private static MapSceneProcess _mapSceneProcess;
        private static UI_Button_BackToTitle _backToTitleBtn;
        private static UI_PauseMenu _pauseMenu;
        private static UI_Victory_Popup _victoryPopup;
        private static UI_Defeat_Popup _defeatPopup;
        private static CardController _cardController;
        private static UI_PlayerDeck _playerDeck;
        private static UI_PlayerStatus _playerStatus;
        private static UI_Button_StartWave _startWaveBtn;
        private static UI_GameSpeedControl _speedCtrl;
        private static UI_QuestStatus _questStatus;
        private static UI_TowerControl _towerControl;
        private static UI_Notification _notification;

        private static Il2CppSystem.Enum _boxedRequestStartNextWave;

        public static string ActiveScene => _activeScene;
        public static bool IsInTitle => _activeScene == "CoinPage";
        public static bool IsInMap => _activeScene == "MapScene";
        public static bool IsInBattle => _activeScene == "MainGame";
        public static bool IsLoading => _activeScene == "LoadingScene";

        public static bool RefreshSceneCache()
        {
            string cur = SceneManager.GetActiveScene().name ?? "";
            if (cur == _activeScene) return false;

            _activeScene = cur;
            _coinPage = null;
            _mapSceneProcess = null;
            _backToTitleBtn = null;
            _pauseMenu = null;
            _victoryPopup = null;
            _defeatPopup = null;
            _cardController = null;
            _playerDeck = null;
            _playerStatus = null;
            _startWaveBtn = null;
            _speedCtrl = null;
            _questStatus = null;
            _towerControl = null;
            _notification = null;
            return true;
        }

        public static CoinPage GetCoinPage()
        {
            if (_coinPage == null && IsInTitle)
                _coinPage = UnityEngine.Object.FindObjectOfType<CoinPage>();
            return _coinPage;
        }

        public static MapSceneProcess GetMapSceneProcess()
        {
            if (_mapSceneProcess == null && IsInMap)
                _mapSceneProcess = UnityEngine.Object.FindObjectOfType<MapSceneProcess>();
            return _mapSceneProcess;
        }

        public static UI_Button_BackToTitle GetBackToTitleButton()
        {
            if (_backToTitleBtn == null && IsInMap)
                _backToTitleBtn = UnityEngine.Object.FindObjectOfType<UI_Button_BackToTitle>();
            return _backToTitleBtn;
        }

        public static UI_PauseMenu GetPauseMenu()
        {
            if (_pauseMenu == null && IsInBattle)
                _pauseMenu = UnityEngine.Object.FindObjectOfType<UI_PauseMenu>(true);
            return _pauseMenu;
        }

        public static UI_Victory_Popup GetVictoryPopup()
        {
            if (_victoryPopup == null && IsInBattle)
                _victoryPopup = UnityEngine.Object.FindObjectOfType<UI_Victory_Popup>(true);
            return _victoryPopup;
        }

        public static UI_Defeat_Popup GetDefeatPopup()
        {
            if (_defeatPopup == null && IsInBattle)
                _defeatPopup = UnityEngine.Object.FindObjectOfType<UI_Defeat_Popup>(true);
            return _defeatPopup;
        }

        public static CardController GetCardController()
        {
            if (_cardController == null && IsInBattle)
                _cardController = UnityEngine.Object.FindObjectOfType<CardController>();
            return _cardController;
        }

        public static UI_PlayerDeck GetPlayerDeck()
        {
            if (_playerDeck == null && IsInBattle)
            {
                var cc = GetCardController();
                _playerDeck = (cc != null && cc.ui_PlayerDeck != null)
                    ? cc.ui_PlayerDeck
                    : UnityEngine.Object.FindObjectOfType<UI_PlayerDeck>();
            }
            return _playerDeck;
        }

        public static UI_PlayerStatus GetPlayerStatus()
        {
            if (_playerStatus == null && IsInBattle)
                _playerStatus = UnityEngine.Object.FindObjectOfType<UI_PlayerStatus>();
            return _playerStatus;
        }

        public static UI_Button_StartWave GetStartWaveButton()
        {
            if (_startWaveBtn == null && IsInBattle)
                _startWaveBtn = UnityEngine.Object.FindObjectOfType<UI_Button_StartWave>();
            return _startWaveBtn;
        }

        public static UI_GameSpeedControl GetSpeedControl()
        {
            if (_speedCtrl == null && IsInBattle)
                _speedCtrl = UnityEngine.Object.FindObjectOfType<UI_GameSpeedControl>();
            return _speedCtrl;
        }

        public static UI_QuestStatus GetQuestStatus()
        {
            if (_questStatus == null && IsInBattle)
                _questStatus = UnityEngine.Object.FindObjectOfType<UI_QuestStatus>(true);
            return _questStatus;
        }

        public static UI_TowerControl GetTowerControl()
        {
            if (_towerControl == null && IsInBattle)
                _towerControl = UnityEngine.Object.FindObjectOfType<UI_TowerControl>(true);
            return _towerControl;
        }

        public static UI_Notification GetNotification()
        {
            if (_notification == null)
                _notification = UnityEngine.Object.FindObjectOfType<UI_Notification>(true);
            return _notification;
        }

        public static void ShowGameToast(string message, float duration = 3.0f)
        {
            try
            {
                var notif = GetNotification();
                if (notif != null)
                    notif.TriggerNotification_OverrideTime(message, duration);
            }
            catch { }
        }

        // Manual IL2CPP boxing avoids Il2CppInterop enum boxing errors on EventMgr.SendEvent.
        public static unsafe Il2CppSystem.Enum BoxEvent(eGameEvents evt)
        {
            int val = (int)evt;
            IntPtr boxed = IL2CPP.il2cpp_value_box(Il2CppClassPointerStore<eGameEvents>.NativeClassPtr, (IntPtr)(&val));
            return new Il2CppSystem.Object(boxed).Cast<Il2CppSystem.Enum>();
        }

        public static bool IsRequestStartNextWaveEvent(Il2CppSystem.Enum ev)
        {
            if (ev == null) return false;
            if (_boxedRequestStartNextWave == null)
                _boxedRequestStartNextWave = BoxEvent(eGameEvents.RequestStartNextWave);
            return ev.Equals(_boxedRequestStartNextWave);
        }

        public static void SendEvent0(eGameEvents evt)
        {
            try { EventMgr.SendEvent(BoxEvent(evt)); }
            catch (Exception ex) { CoopLogger.Debug(LogCategory.Sync, $"SendEvent0({evt}) failed: {ex.Message}"); }
        }

        public static void SendEventRef<T>(eGameEvents evt, T arg) where T : Il2CppObjectBase
        {
            try { EventMgr.SendEvent<T>(BoxEvent(evt), arg); }
            catch (Exception ex) { CoopLogger.Debug(LogCategory.Sync, $"SendEventRef<{typeof(T).Name}>({evt}) failed: {ex.Message}"); }
        }

        public static void SendEventString(eGameEvents evt, string arg)
        {
            try { EventMgr.SendEvent<string>(BoxEvent(evt), arg); }
            catch (Exception ex) { CoopLogger.Debug(LogCategory.Sync, $"SendEventString({evt}) failed: {ex.Message}"); }
        }

        public static void SetButtonLocked(Button btn, bool locked)
        {
            if (btn == null) return;
            btn.interactable = !locked;
            var cg = btn.gameObject.GetComponent<CanvasGroup>();
            if (locked)
            {
                if (cg == null) cg = btn.gameObject.AddComponent<CanvasGroup>();
                if (cg != null)
                {
                    cg.alpha = 0.38f;
                    cg.interactable = false;
                    cg.blocksRaycasts = false;
                }
            }
            else if (cg != null)
            {
                cg.alpha = 1f;
                cg.interactable = true;
                cg.blocksRaycasts = true;
            }
        }

        public static void CancelLocalPlacementIfActive()
        {
            try
            {
                var mg = MainGame.Instance;
                if (mg != null && mg.ObjectPlacementHandler != null && mg.ObjectPlacementHandler.IsCurrentInPlacement())
                {
                    GameStateController.Instance?.OnRequestChangeGameState(eGameState.NORMAL_MODE);
                    mg.ObjectPlacementHandler.OnCancelPlacement();
                }
            }
            catch { }
        }

        public static Obj_TetrisBlock FindTetrisAt(Vector3 pos, float maxDist = 0.70f)
        {
            var list = TetrisManager.Instance?.GetAllTetrisOnField();
            if (list == null) return null;

            Obj_TetrisBlock best = null;
            float bestSq = maxDist * maxDist;
            for (int i = 0; i < list.Count; i++)
            {
                var b = list[i];
                if (b == null) continue;
                Vector3 d = b.transform.position - pos;
                d.y = 0f;
                float sq = d.sqrMagnitude;
                if (sq <= bestSq) { bestSq = sq; best = b; }
            }
            return best;
        }

        public static ABaseTower FindTowerAt(Vector3 pos, float maxDist = 0.70f)
        {
            var list = TowerManager.Instance?.GetAllTowersOnField();
            if (list == null) return null;

            ABaseTower best = null;
            float bestSq = maxDist * maxDist;
            for (int i = 0; i < list.Count; i++)
            {
                var t = list[i];
                if (t == null) continue;
                Vector3 d = t.transform.position - pos;
                d.y = 0f;
                float sq = d.sqrMagnitude;
                if (sq <= bestSq) { bestSq = sq; best = t; }
            }
            return best;
        }

        public static AMonsterBase FindMonsterNear(Vector3 pos, float maxDist = 2.5f)
        {
            var list = MonsterManager.Instance?.GetMonstersOnField();
            if (list == null) return null;

            AMonsterBase best = null;
            float bestSq = maxDist * maxDist;
            for (int i = 0; i < list.Count; i++)
            {
                var m = list[i];
                if (m == null || !m.IsAlive()) continue;
                Vector3 d = m.transform.position - pos;
                d.y = 0f;
                float sq = d.sqrMagnitude;
                if (sq <= bestSq) { bestSq = sq; best = m; }
            }
            return best;
        }

        public static Obj_Chest FindChestAt(Vector3 pos, float maxDist = 1.25f)
        {
            var chests = UnityEngine.Object.FindObjectsOfType<Obj_Chest>();
            if (chests == null) return null;

            Obj_Chest best = null;
            float bestSq = maxDist * maxDist;
            for (int i = 0; i < chests.Length; i++)
            {
                var c = chests[i];
                if (c == null || !c.gameObject.activeInHierarchy) continue;
                Vector3 d = c.transform.position - pos;
                d.y = 0f;
                float sq = d.sqrMagnitude;
                if (sq <= bestSq) { bestSq = sq; best = c; }
            }
            return best;
        }

        public static Obj_RuneStone FindRuneStoneAt(Vector3 pos, float maxDist = 1.25f)
        {
            var stones = UnityEngine.Object.FindObjectsOfType<Obj_RuneStone>();
            if (stones == null) return null;

            Obj_RuneStone best = null;
            float bestSq = maxDist * maxDist;
            for (int i = 0; i < stones.Length; i++)
            {
                var s = stones[i];
                if (s == null || !s.gameObject.activeInHierarchy) continue;
                Vector3 d = s.transform.position - pos;
                d.y = 0f;
                float sq = d.sqrMagnitude;
                if (sq <= bestSq) { bestSq = sq; best = s; }
            }
            return best;
        }

        public static CardData FindHandCard(IngameData ig, string runtimeCardId, eItemType fallbackType)
        {
            var hand = ig?.list_HandCardData;
            if (hand == null) return null;

            if (!string.IsNullOrEmpty(runtimeCardId))
            {
                for (int i = 0; i < hand.Count; i++)
                {
                    var c = hand[i];
                    if (c != null && c.RuntimeCardId == runtimeCardId)
                        return c;
                }
            }
            for (int i = 0; i < hand.Count; i++)
            {
                var c = hand[i];
                if (c != null && c.ItemType == fallbackType)
                    return c;
            }
            return null;
        }
    }
}
