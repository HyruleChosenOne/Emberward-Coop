using System;
using System.Collections.Generic;
using System.IO;
using EmberwardCoop.Core;
using EmberwardCoop.Networking;
using EmberwardCoop.Serialization;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace EmberwardCoop.Sync
{
    public sealed class RunStateSync
    {
        private readonly NetworkManager _net;
        private readonly Func<SessionController> _session;

        public string HostTargetScene = "CoinPage";
        public int HostSceneLoadSeq = 1;
        public int HostSelectedMapNodeIndex = -1;
        public string PendingEnvSceneName = "";
        public int PendingStageSeed = 0;

        public bool MenuLockDirty = true;
        private bool _runDirty;
        private ulong _lastRunHash;
        private float _lastRunCheckTime;
        private float _lastMenuLockTime;

        private int _lastClientSceneSeq = -1;
        private string _pendingSceneToLoad = "";
        private bool _wasClientConnected;

        // Map node voting: playerId -> nodeIndex
        public readonly Dictionary<int, int> MapNodeVotes = new Dictionary<int, int>();
        public event Action OnMapVotesChanged;

        public RunStateSync(NetworkManager net, Func<SessionController> session)
        {
            _net = net;
            _session = session;
        }

        public void MarkDirty() => _runDirty = true;

        public void Reset()
        {
            _pendingSceneToLoad = "";
            _lastClientSceneSeq = -1;
            HostSelectedMapNodeIndex = -1;
            MapNodeVotes.Clear();
            MenuLockDirty = true;
            _runDirty = false;
            _lastRunHash = 0;
        }

        public void Tick(string activeScene, float now)
        {
            bool isClient = _net.IsClient;
            if (isClient != _wasClientConnected || MenuLockDirty || (isClient && now - _lastMenuLockTime >= 0.75f))
            {
                _lastMenuLockTime = now;
                MenuLockDirty = false;
                UpdateClientProtectionAndMenuLocks(activeScene, isClient);
            }

            if (!_net.IsConnected) return;

            if (_net.IsHost)
            {
                if (!string.IsNullOrEmpty(activeScene) && activeScene != "LoadingScene")
                    HostTargetScene = activeScene;

                if (_net.RemotePeerCount > 0 && (_runDirty || (activeScene != "MainGame" && now - _lastRunCheckTime >= 0.4f)))
                {
                    _lastRunCheckTime = now;
                    CheckAndBroadcastHostRunState(_runDirty);
                    _runDirty = false;
                }
            }
            else if (_net.IsClient)
            {
                if (!string.IsNullOrEmpty(_pendingSceneToLoad) && activeScene != "LoadingScene")
                {
                    string target = _pendingSceneToLoad;
                    _pendingSceneToLoad = "";
                    ExecuteClientSceneLoad(target);
                }
            }
        }

        private void UpdateClientProtectionAndMenuLocks(string activeScene, bool isClient)
        {
            var gdm = GameDataManager.instance;
            if (isClient && !_wasClientConnected)
            {
                _wasClientConnected = true;
                _lastClientSceneSeq = -1;
                if (gdm != null && !gdm.doUseTempSessionData)
                {
                    gdm.SwitchSession(true);
                    CoopLogger.Info(LogCategory.Sync, "Switched Client to protected co-op session (solo run untouched).");
                }
            }
            else if (!isClient && _wasClientConnected)
            {
                _wasClientConnected = false;
                _pendingSceneToLoad = "";
                if (gdm != null && gdm.doUseTempSessionData)
                    gdm.SwitchSession(false);

                var cp = GameRefs.GetCoinPage();
                if (cp != null)
                {
                    GameRefs.SetButtonLocked(cp.button_StartGame, false);
                    GameRefs.SetButtonLocked(cp.button_Continue, !cp.canContinue);
                    GameRefs.SetButtonLocked(cp.button_CopySaveFile, false);
                    cp.UpdateContinueInfo();
                }
                return;
            }

            if (!isClient) return;

            if (gdm != null && !gdm.doUseTempSessionData)
                gdm.SwitchSession(true);

            if (activeScene == "CoinPage")
            {
                var cp = GameRefs.GetCoinPage();
                if (cp != null)
                {
                    GameRefs.SetButtonLocked(cp.button_StartGame, true);
                    GameRefs.SetButtonLocked(cp.button_Continue, true);
                    GameRefs.SetButtonLocked(cp.button_CopySaveFile, true);
                    if (cp.text_ContinueProgress != null)
                        cp.text_ContinueProgress.text = $"CO-OP: Following {_net.HostPlayerName}";
                }
            }
            else if (activeScene == "MapScene")
            {
                var backBtn = GameRefs.GetBackToTitleButton();
                if (backBtn != null)
                    GameRefs.SetButtonLocked(backBtn.button, true);
            }
            else if (activeScene == "MainGame")
            {
                var pause = GameRefs.GetPauseMenu();
                if (pause != null && pause.gameObject.activeInHierarchy)
                {
                    GameRefs.SetButtonLocked(pause.button_QuickRestart, true);
                    GameRefs.SetButtonLocked(pause.button_EndChallenge, true);
                    GameRefs.SetButtonLocked(pause.button_MainMenu, true);
                }

                var vic = GameRefs.GetVictoryPopup();
                if (vic != null && vic.gameObject.activeInHierarchy)
                {
                    GameRefs.SetButtonLocked(vic.button_Continue, true);
                    GameRefs.SetButtonLocked(vic.button_BackToMenu, true);
                    GameRefs.SetButtonLocked(vic.button_MoreWaves, true);
                    GameRefs.SetButtonLocked(vic.button_MoreLevels, true);
                }

                var def = GameRefs.GetDefeatPopup();
                if (def != null && def.gameObject.activeInHierarchy)
                {
                    GameRefs.SetButtonLocked(def.button_BackToTitle, true);
                    GameRefs.SetButtonLocked(def.button_Retry, true);
                }
            }
        }

        private static ulong HashCombine(ulong h, ulong v) => (h ^ v) * 1099511628211UL;
        private static ulong HashString(ulong h, string s)
        {
            if (string.IsNullOrEmpty(s)) return HashCombine(h, 0);
            for (int i = 0; i < s.Length; i++) h = HashCombine(h, s[i]);
            return h;
        }

        private ulong ComputeRunHash()
        {
            var gdm = GameDataManager.instance;
            var gd = gdm?.GameplayData;
            var id = gdm?.IntermediateData;
            ulong h = 14695981039346656037UL;
            h = HashString(h, HostTargetScene);
            h = HashCombine(h, (ulong)HostSceneLoadSeq);
            h = HashCombine(h, (ulong)HostSelectedMapNodeIndex);
            if (gd != null)
            {
                h = HashCombine(h, (ulong)gd.randomSeed);
                h = HashCombine(h, (ulong)(int)gd.difficulty);
                h = HashCombine(h, (ulong)(int)gd.gameMode);
                h = HashCombine(h, (ulong)(int)gd.characterType);
                h = HashCombine(h, (ulong)(int)gd.emberType);
                h = HashCombine(h, (ulong)(int)gd.curWorld);
                h = HashCombine(h, (ulong)gd.curMapStep);
                h = HashCombine(h, (ulong)gd.curMapNodeIndex);
                h = HashCombine(h, (ulong)gd.gem);
                h = HashCombine(h, (ulong)gd.rerollChance);
                h = HashCombine(h, (ulong)gd.reviveItem);
                h = HashCombine(h, (ulong)gd.curHP);
                h = HashCombine(h, (ulong)gd.maxHP);
                h = HashCombine(h, (ulong)gd.extraShield);
                h = HashCombine(h, (ulong)(gd.quest != null ? (int)gd.quest.questType : 0));
                h = HashCombine(h, (ulong)(gd.list_AltarPactData?.Count ?? 0));
                h = HashCombine(h, (ulong)(gd.list_LoadoutTowerData?.Count ?? 0));
                h = HashCombine(h, (ulong)(gd.list_CollectedTowerData?.Count ?? 0));
                h = HashCombine(h, (ulong)(gd.list_ItemStorage?.Count ?? 0));
                h = HashCombine(h, (ulong)(gd.list_CollectedRelic?.Count ?? 0));
                h = HashCombine(h, (ulong)(gd.list_PlayerDisabledRelic?.Count ?? 0));
                if (gd.list_ItemStorage != null)
                {
                    for (int i = 0; i < gd.list_ItemStorage.Count; i++)
                    {
                        var tc = gd.list_ItemStorage[i]?.TryCast<TetrisCardData>();
                        if (tc != null)
                            h = HashCombine(h, (ulong)((int)tc.ItemType ^ ((tc.list_Runes?.Count ?? 0) << 16)));
                    }
                }
                var md = gd.mapData;
                if (md != null && md.IsGenerated && md.list_MapNodeData != null)
                {
                    h = HashCombine(h, (ulong)md.randomSeed);
                    h = HashCombine(h, (ulong)md.list_MapNodeData.Count);
                    for (int i = 0; i < md.list_MapNodeData.Count; i++)
                    {
                        var n = md.list_MapNodeData[i];
                        if (n != null)
                            h = HashCombine(h, (ulong)(((int)n.State << 1) | (n.isCleared ? 1 : 0)));
                    }
                }
            }
            if (id != null)
            {
                h = HashString(h, id.stageName);
                h = HashCombine(h, (ulong)(int)id.stageType);
                if (id.mapNodeData != null)
                {
                    h = HashCombine(h, (ulong)id.mapNodeData.randomSeed);
                    h = HashString(h, id.mapNodeData.stageEnvSceneName);
                }
            }
            return h;
        }

        public void CheckAndBroadcastHostRunState(bool force)
        {
            if (!_net.IsHost || _net.RemotePeerCount == 0) return;
            ulong hash = ComputeRunHash();
            if (force || hash != _lastRunHash)
            {
                _lastRunHash = hash;
                BroadcastRunSnapshot(-1);
            }
        }

        public void BroadcastRunSnapshot(int targetPeerId = -1, string overrideTargetScene = null)
        {
            if (!_net.IsHost) return;
            var gdm = GameDataManager.instance;
            if (gdm == null) return;
            var gd = gdm.GameplayData;
            var id = gdm.IntermediateData;

            if (!string.IsNullOrEmpty(overrideTargetScene))
            {
                HostTargetScene = overrideTargetScene;
            }
            else
            {
                string active = SceneManager.GetActiveScene().name;
                if (active == "LoadingScene")
                {
                    if (!string.IsNullOrEmpty(RefiUtilities.LoadingData.TargetSceneName))
                        HostTargetScene = RefiUtilities.LoadingData.TargetSceneName;
                }
                else if (!string.IsNullOrEmpty(active))
                {
                    HostTargetScene = active;
                }
            }

            string envScene = "";
            if (MainGame.Instance != null && MainGame.Instance.sceneEntry != null)
                envScene = MainGame.Instance.sceneEntry.name ?? "";
            else if (id?.mapNodeData != null && !string.IsNullOrEmpty(id.mapNodeData.stageEnvSceneName))
                envScene = id.mapNodeData.stageEnvSceneName;

            Action<BinaryWriter> writer = w =>
            {
                w.WriteSafeString(HostTargetScene ?? "CoinPage");
                w.Write(HostSceneLoadSeq);
                w.Write(HostSelectedMapNodeIndex);
                w.WriteSafeString(envScene);

                w.Write(gd != null ? gd.randomSeed : 12345);
                w.Write(gd != null && gd.isInitialized);
                w.Write(gd != null && gd.isGameStarted);
                w.Write(gd != null && gd.isGameEnded);
                w.Write(gd != null && gd.isCustomGame);
                w.Write(gd != null ? (int)gd.difficulty : 0);
                w.Write(gd != null ? (int)gd.gameMode : 0);
                w.Write(gd != null ? (int)gd.characterType : 0);
                w.Write(gd != null ? (int)gd.emberType : 0);
                w.Write(gd != null ? (int)gd.curWorld : 0);
                w.Write(gd != null ? gd.curMapStep : 0);
                w.Write(gd != null ? gd.curMapNodeIndex : 0);
                w.Write(gd != null ? gd.gem : 0);
                w.Write(gd != null ? gd.rerollChance : 0);
                w.Write(gd != null ? gd.reviveItem : 0);
                w.Write(gd != null ? gd.towerCardLimit : 5);
                w.Write(gd != null ? gd.itemCardLimit : 5);
                w.Write(gd != null ? gd.drawCardPerRound : 2);
                w.Write(gd != null ? gd.maxHP : 20);
                w.Write(gd != null ? gd.curHP : 20);
                w.Write(gd != null ? gd.extraShield : 0);
                w.Write(gd != null ? gd.demonFlameLevel : 0);
                w.Write(gd != null ? gd.goldRemainLastLevel : 0);
                w.Write(gd != null && gd.isStartedAfterBossEndlessMode);
                w.Write(gd != null ? gd.levelClearedInAfterBossEndlessMode : 0);
                w.Write(gd != null && gd.turnOffTimer);

                var relics = gd?.list_CollectedRelic;
                int rCount = relics?.Count ?? 0;
                w.Write(rCount);
                for (int i = 0; i < rCount; i++) w.Write((int)relics[i]);

                var disRelics = gd?.list_PlayerDisabledRelic;
                int drCount = disRelics?.Count ?? 0;
                w.Write(drCount);
                for (int i = 0; i < drCount; i++) w.Write((int)disRelics[i]);

                var loadout = gd?.list_LoadoutTowerData;
                int lCount = loadout?.Count ?? 0;
                w.Write(lCount);
                for (int i = 0; i < lCount; i++)
                {
                    var t = loadout[i];
                    w.Write(t != null ? (int)t.ItemType : 0);
                    w.Write(t != null ? t.Level : 1);
                }

                var collected = gd?.list_CollectedTowerData;
                int cCount = collected?.Count ?? 0;
                w.Write(cCount);
                for (int i = 0; i < cCount; i++)
                {
                    var t = collected[i];
                    w.Write(t != null ? (int)t.ItemType : 0);
                    w.Write(t != null ? t.Level : 1);
                }

                var storage = gd?.list_ItemStorage;
                int sCount = storage?.Count ?? 0;
                w.Write(sCount);
                for (int i = 0; i < sCount; i++)
                    GameSerializers.CaptureCard(storage[i]).Write(w);

                GameSerializers.WriteQuestData(w, gd?.quest);
                GameSerializers.WriteAltarPacts(w, gd?.list_AltarPactData);
                GameSerializers.WriteHardModeSetting(w, gd?.hardModeData);
                GameSerializers.WriteCharacterExtras(w, gd);

                var mgs = gd?.mapGenerateSetting;
                if (mgs == null) w.Write(false);
                else
                {
                    w.Write(true);
                    w.Write((int)mgs.WorldType);
                    w.Write(mgs.Step);
                    w.Write(mgs.MinNodeInStep);
                    w.Write(mgs.MaxNodeInStep);
                    w.Write(mgs.Weight_Level);
                    w.Write(mgs.Weight_Level_Corrupted);
                    w.Write(mgs.Weight_Shop);
                    w.Write(mgs.Weight_Workshop);
                    w.Write(mgs.Weight_SpecialEvent);
                    w.Write(mgs.Weight_Altar);
                    w.Write(mgs.Weight_Quest);
                    w.Write(mgs.Weight_Campsite);
                    w.Write(mgs.Seed);
                }

                var md = gd?.mapData;
                if (md == null || !md.IsGenerated || md.list_MapNodeData == null) w.Write(false);
                else
                {
                    w.Write(true);
                    w.Write(md.randomSeed);
                    var stepCounts = md.list_NodeCountInEachStep;
                    int scCount = stepCounts?.Count ?? 0;
                    w.Write(scCount);
                    for (int i = 0; i < scCount; i++) w.Write(stepCounts[i]);
                    var nodes = md.list_MapNodeData;
                    w.Write(nodes.Count);
                    for (int i = 0; i < nodes.Count; i++) GameSerializers.WriteMapNode(w, nodes[i]);
                }

                w.Write(id != null ? (int)id.worldType : 0);
                w.Write(id != null ? (int)id.missionType : 0);
                w.Write(id != null ? (int)id.stageType : 0);
                w.Write(id != null ? (int)id.waveMode : 0);
                w.Write(id != null ? (int)id.endlessModeType : 0);
                w.Write(id != null ? id.difficulty : 0);
                w.Write(id != null && id.isCorrupted);
                w.Write(id != null && id.isAnomaly);
                w.Write(id != null && id.isTerritoryLevel);
                w.WriteSafeString(id?.stageName);
                w.WriteSafeString(id?.leaderboardName);
                w.WriteSafeString(id?.endlessModeRoundRewardDataPath);
                GameSerializers.WriteMapNode(w, id?.mapNodeData);
            };

            if (targetPeerId >= 0) _net.SendToPeer(targetPeerId, PacketType.RunSnapshot, writer);
            else _net.BroadcastToClients(PacketType.RunSnapshot, writer);

            CoopLogger.Debug(LogCategory.Sync, $"Sent RunSnapshot (scene={HostTargetScene}, seq={HostSceneLoadSeq}, seed={gd?.randomSeed ?? 0}).");
        }

        public void HandleRunSnapshot(BinaryReader r)
        {
            string targetScene = r.ReadString();
            int sceneSeq = r.ReadInt32();
            int selectedNodeIdx = r.ReadInt32();
            string envScene = r.ReadString();

            int randomSeed = r.ReadInt32();
            bool isInit = r.ReadBoolean();
            bool isStarted = r.ReadBoolean();
            bool isEnded = r.ReadBoolean();
            bool isCustom = r.ReadBoolean();
            int difficulty = r.ReadInt32();
            int gameMode = r.ReadInt32();
            int charType = r.ReadInt32();
            int emberType = r.ReadInt32();
            int curWorld = r.ReadInt32();
            int curMapStep = r.ReadInt32();
            int curMapNodeIndex = r.ReadInt32();
            int gem = r.ReadInt32();
            int rerollChance = r.ReadInt32();
            int reviveItem = r.ReadInt32();
            int towerLimit = r.ReadInt32();
            int itemLimit = r.ReadInt32();
            int drawPerRound = r.ReadInt32();
            int maxHP = r.ReadInt32();
            int curHP = r.ReadInt32();
            int extraShield = r.ReadInt32();
            int demonFlame = r.ReadInt32();
            int goldRemain = r.ReadInt32();
            bool afterBossEndless = r.ReadBoolean();
            int endlessCleared = r.ReadInt32();
            bool turnOffTimer = r.ReadBoolean();

            int rCount = r.ReadInt32();
            var relics = new int[rCount];
            for (int i = 0; i < rCount; i++) relics[i] = r.ReadInt32();

            int drCount = r.ReadInt32();
            var disRelics = new int[drCount];
            for (int i = 0; i < drCount; i++) disRelics[i] = r.ReadInt32();

            int lCount = r.ReadInt32();
            var loadoutTypes = new int[lCount];
            var loadoutLevels = new int[lCount];
            for (int i = 0; i < lCount; i++) { loadoutTypes[i] = r.ReadInt32(); loadoutLevels[i] = r.ReadInt32(); }

            int cCount = r.ReadInt32();
            var collectedTypes = new int[cCount];
            var collectedLevels = new int[cCount];
            for (int i = 0; i < cCount; i++) { collectedTypes[i] = r.ReadInt32(); collectedLevels[i] = r.ReadInt32(); }

            int sCount = r.ReadInt32();
            var storageCards = new NetCardInfo[sCount];
            for (int i = 0; i < sCount; i++) storageCards[i] = NetCardInfo.Read(r);

            var syncedQuest = GameSerializers.ReadQuestData(r);
            var gdm = GameDataManager.instance;
            var gd = gdm?.GameplayData;

            using (NetworkScope.Enter())
            {
                if (gdm != null && !gdm.doUseTempSessionData)
                {
                    gdm.SwitchSession(true);
                    gd = gdm.GameplayData;
                }

                GameSerializers.ReadAltarPactsInto(r, gd);
                GameSerializers.ReadHardModeSettingInto(r, gd);
                GameSerializers.ReadCharacterExtrasInto(r, gd);

                MapGenerateSetting syncedMgs = null;
                if (r.ReadBoolean())
                {
                    var mgsWorld = (eWorldType)r.ReadInt32();
                    int mgsStep = r.ReadInt32();
                    int mgsMin = r.ReadInt32();
                    int mgsMax = r.ReadInt32();
                    int wLevel = r.ReadInt32();
                    int wCorr = r.ReadInt32();
                    int wShop = r.ReadInt32();
                    int wWork = r.ReadInt32();
                    int wSpec = r.ReadInt32();
                    int wAltar = r.ReadInt32();
                    int wQuest = r.ReadInt32();
                    int wCamp = r.ReadInt32();
                    int mgsSeed = r.ReadInt32();

                    syncedMgs = new MapGenerateSetting(mgsWorld, mgsSeed)
                    {
                        Step = mgsStep,
                        MinNodeInStep = mgsMin,
                        MaxNodeInStep = mgsMax,
                        Weight_Level = wLevel,
                        Weight_Level_Corrupted = wCorr,
                        Weight_Shop = wShop,
                        Weight_Workshop = wWork,
                        Weight_SpecialEvent = wSpec,
                        Weight_Altar = wAltar,
                        Weight_Quest = wQuest,
                        Weight_Campsite = wCamp,
                        Seed = mgsSeed
                    };
                }

                MapData syncedMap = null;
                if (r.ReadBoolean())
                {
                    syncedMap = new MapData { IsGenerated = true, randomSeed = r.ReadInt32() };
                    int scCount = r.ReadInt32();
                    syncedMap.list_NodeCountInEachStep ??= new Il2CppSystem.Collections.Generic.List<int>();
                    syncedMap.list_NodeCountInEachStep.Clear();
                    for (int i = 0; i < scCount; i++) syncedMap.list_NodeCountInEachStep.Add(r.ReadInt32());

                    int nCount = r.ReadInt32();
                    syncedMap.list_MapNodeData ??= new Il2CppSystem.Collections.Generic.List<MapNodeData>();
                    syncedMap.list_MapNodeData.Clear();
                    for (int i = 0; i < nCount; i++)
                    {
                        var nd = GameSerializers.ReadMapNode(r);
                        if (nd != null) syncedMap.list_MapNodeData.Add(nd);
                    }
                }

                int worldType = r.ReadInt32();
                int missionType = r.ReadInt32();
                int stageType = r.ReadInt32();
                int waveMode = r.ReadInt32();
                int endlessType = r.ReadInt32();
                int idDiff = r.ReadInt32();
                bool isCorrupted = r.ReadBoolean();
                bool isAnomaly = r.ReadBoolean();
                bool isTerritory = r.ReadBoolean();
                string stageName = r.ReadString();
                string leaderboardName = r.ReadString();
                string endlessRewardPath = r.ReadString();
                MapNodeData idMapNode = GameSerializers.ReadMapNode(r);

                PendingEnvSceneName = envScene;
                PendingStageSeed = (idMapNode != null && idMapNode.randomSeed != 0) ? idMapNode.randomSeed : randomSeed;

                if (gd != null)
                {
                    bool preserveClientInventory = (_session()?.Ruleset.AllowsClientIndependentDraftsAndShops ?? false)
                        && gd.isGameStarted && gd.randomSeed == randomSeed && randomSeed != 0;

                    gd.randomSeed = randomSeed;
                    gd.isInitialized = isInit;
                    gd.isGameStarted = isStarted;
                    gd.isGameEnded = isEnded;
                    gd.isCustomGame = isCustom;
                    gd.difficulty = (eGameDifficultyType)difficulty;
                    gd.gameMode = (eGameMode)gameMode;
                    gd.characterType = (eCharacterType)charType;
                    gd.emberType = (eEmberType)emberType;
                    gd.curWorld = (eWorldType)curWorld;
                    gd.curMapStep = curMapStep;
                    gd.curMapNodeIndex = curMapNodeIndex;
                    if (!preserveClientInventory)
                    {
                        gd.gem = gem;
                        gd.rerollChance = rerollChance;
                        gd.reviveItem = reviveItem;
                        gd.towerCardLimit = towerLimit;
                        gd.itemCardLimit = itemLimit;
                        gd.drawCardPerRound = drawPerRound;
                    }
                    gd.maxHP = maxHP;
                    gd.curHP = curHP;
                    gd.extraShield = extraShield;
                    gd.demonFlameLevel = demonFlame;
                    gd.goldRemainLastLevel = goldRemain;
                    gd.isStartedAfterBossEndlessMode = afterBossEndless;
                    gd.levelClearedInAfterBossEndlessMode = endlessCleared;
                    gd.turnOffTimer = turnOffTimer;
                    gd.quest = syncedQuest;

                    if (!preserveClientInventory)
                    {
                        if (gd.list_CollectedRelic != null)
                        {
                            gd.list_CollectedRelic.Clear();
                            for (int i = 0; i < rCount; i++) gd.list_CollectedRelic.Add((eItemType)relics[i]);
                        }
                        if (gd.list_PlayerDisabledRelic != null)
                        {
                            gd.list_PlayerDisabledRelic.Clear();
                            for (int i = 0; i < drCount; i++) gd.list_PlayerDisabledRelic.Add((eItemType)disRelics[i]);
                        }
                        if (gd.list_LoadoutTowerData != null)
                        {
                            gd.list_LoadoutTowerData.Clear();
                            for (int i = 0; i < lCount; i++)
                                gd.list_LoadoutTowerData.Add(new TowerIngameData((eItemType)loadoutTypes[i], loadoutLevels[i]));
                        }
                        if (gd.list_CollectedTowerData != null)
                        {
                            gd.list_CollectedTowerData.Clear();
                            for (int i = 0; i < cCount; i++)
                                gd.list_CollectedTowerData.Add(new TowerIngameData((eItemType)collectedTypes[i], collectedLevels[i]));
                        }
                        if (gd.list_ItemStorage != null)
                        {
                            gd.list_ItemStorage.Clear();
                            for (int i = 0; i < sCount; i++)
                            {
                                var cd = GameSerializers.MaterializeCard(storageCards[i]);
                                if (cd != null) gd.list_ItemStorage.Add(cd);
                            }
                        }
                    }
                    if (syncedMgs != null) gd.mapGenerateSetting = syncedMgs;
                    if (syncedMap != null) gd.mapData = syncedMap;
                }

                var id = gdm?.IntermediateData;
                if (id != null)
                {
                    id.worldType = (eWorldType)worldType;
                    id.missionType = (eMissionType)missionType;
                    id.stageType = (eStageType)stageType;
                    id.waveMode = (eWaveMode)waveMode;
                    id.endlessModeType = (eEndlessModeType)endlessType;
                    id.difficulty = idDiff;
                    id.isCorrupted = isCorrupted;
                    id.isAnomaly = isAnomaly;
                    id.isTerritoryLevel = isTerritory;
                    id.stageName = stageName ?? "";
                    id.leaderboardName = leaderboardName ?? "";
                    id.endlessModeRoundRewardDataPath = endlessRewardPath ?? "";

                    if (idMapNode != null)
                    {
                        if (!string.IsNullOrEmpty(envScene)) idMapNode.stageEnvSceneName = envScene;
                        id.mapNodeData = idMapNode;
                    }
                    else if (id.mapNodeData != null && !string.IsNullOrEmpty(envScene))
                    {
                        id.mapNodeData.stageEnvSceneName = envScene;
                    }

                    if (!string.IsNullOrEmpty(stageName))
                    {
                        try { id.LoadDataProcess(); } catch { }
                    }
                }

                string localScene = SceneManager.GetActiveScene().name;
                bool seqChanged = sceneSeq != _lastClientSceneSeq && _lastClientSceneSeq != -1;
                _lastClientSceneSeq = sceneSeq;

                if (!string.IsNullOrEmpty(targetScene) && targetScene != "LoadingScene")
                {
                    if (localScene == "LoadingScene")
                    {
                        if (RefiUtilities.LoadingData.TargetSceneName != targetScene)
                            _pendingSceneToLoad = targetScene;
                    }
                    else if (localScene != targetScene || seqChanged)
                    {
                        ExecuteClientSceneLoad(targetScene);
                        return;
                    }
                }

                if (localScene == "MapScene")
                    RefreshClientMapUI(syncedMap, selectedNodeIdx);

                _session()?.Quests.OnClientQuestRefreshed(syncedQuest);
            }
        }

        public void ExecuteClientSceneLoad(string targetScene)
        {
            if (string.IsNullOrEmpty(targetScene) || targetScene == "LoadingScene") return;
            using (NetworkScope.Enter())
            {
                try
                {
                    CoopLogger.Info(LogCategory.Sync, $"Following Host -> {targetScene} (Seed {PendingStageSeed})");
                    RefiUtilities.SceneLoader.Load(targetScene);
                }
                catch (Exception ex)
                {
                    CoopLogger.Warn(LogCategory.Sync, $"SceneLoader fallback for {targetScene}: {ex.Message}");
                    RefiUtilities.LoadingData.TargetSceneName = targetScene;
                    SceneManager.LoadScene("LoadingScene");
                }
            }
        }

        private static void RefreshClientMapUI(MapData syncedMap, int selectedNodeIdx)
        {
            try
            {
                GameRefs.SendEvent0(eGameEvents.OnRelicChanged);
                GameRefs.SendEvent0(eGameEvents.OnTowerCardChanged);
                GameRefs.SendEvent0(eGameEvents.OnAltarPactChanged);

                var msp = GameRefs.GetMapSceneProcess();
                if (msp?.mapManager != null && syncedMap?.list_MapNodeData != null)
                {
                    var uiNodes = msp.mapManager.list_MapNodes;
                    if (uiNodes != null && uiNodes.Count == syncedMap.list_MapNodeData.Count)
                    {
                        for (int i = 0; i < uiNodes.Count; i++)
                        {
                            var uiNode = uiNodes[i];
                            var dataNode = syncedMap.list_MapNodeData[i];
                            if (uiNode == null || dataNode == null) continue;
                            uiNode.mapNodeData.State = dataNode.State;
                            uiNode.mapNodeData.isCleared = dataNode.isCleared;
                            uiNode.SwitchState(dataNode.State);
                            uiNode.SetCompleted(dataNode.isCleared, true);
                        }
                        msp.StartCoroutine(msp.CR_UpdateMapNodeState());

                        if (selectedNodeIdx >= 0 && selectedNodeIdx < uiNodes.Count && uiNodes[selectedNodeIdx] != null)
                            msp.SetSelectedMapNode(uiNodes[selectedNodeIdx]);
                    }
                }
            }
            catch (Exception ex)
            {
                CoopLogger.Debug(LogCategory.Sync, $"RefreshClientMapUI warning: {ex.Message}");
            }
        }

        public void SendSelectOrVoteMapNode(int nodeIndex, bool isBattleNode)
        {
            if (!_net.IsClient) return;
            // Clients vote on non-battle nodes or request entering battle nodes
            _net.SendToHost(PacketType.CmdVoteMapNode, w => w.Write(nodeIndex));
            if (isBattleNode)
                _net.SendToHost(PacketType.CmdSelectMapNode, w => w.Write(nodeIndex));
        }

        public void HandleCmdVoteMapNode(int senderId, BinaryReader r)
        {
            int nodeIdx = r.ReadInt32();
            MapNodeVotes[senderId] = nodeIdx;
            string pName = _net.GetPlayerName(senderId);
            CoopLogger.Info(LogCategory.Sync, $"{pName} voted for Map Node #{nodeIdx}.");
            GameRefs.ShowGameToast($"{pName} suggests Map Node #{nodeIdx}", 2.5f);
            BroadcastMapVotes();
        }

        public void BroadcastMapVotes()
        {
            if (!_net.IsHost) return;
            _net.BroadcastToClients(PacketType.EvtMapNodeVotes, w =>
            {
                w.Write(MapNodeVotes.Count);
                foreach (var kv in MapNodeVotes)
                {
                    w.Write(kv.Key);
                    w.Write(kv.Value);
                }
            });
            OnMapVotesChanged?.Invoke();
        }

        public void HandleEvtMapNodeVotes(BinaryReader r)
        {
            int count = r.ReadInt32();
            MapNodeVotes.Clear();
            for (int i = 0; i < count; i++)
            {
                int pid = r.ReadInt32();
                int nodeIdx = r.ReadInt32();
                MapNodeVotes[pid] = nodeIdx;
            }
            OnMapVotesChanged?.Invoke();
        }

        public void HandleCmdSelectMapNode(int senderId, BinaryReader r)
        {
            int nodeIndex = r.ReadInt32();
            var msp = GameRefs.GetMapSceneProcess();
            var nodes = msp?.mapManager?.list_MapNodes;
            if (nodes == null || nodeIndex < 0 || nodeIndex >= nodes.Count) return;

            var clicked = nodes[nodeIndex];
            if (clicked == null || clicked.mapNodeData == null) return;

            // Only allow clients to directly trigger battle/boss nodes; non-battle modals are opened by Host
            var st = clicked.mapNodeData.MapNodeType;
            bool isCombatNode = st == eStageType.BATTLE || st == eStageType.BATTLE_CORRUPTED ||
                                st == eStageType.BATTLE_ANOMALY || st == eStageType.BATTLE_DARKNESS ||
                                st == eStageType.BOSS || st == eStageType.MID_BOSS;
            if (!isCombatNode) return;

            string pName = _net.GetPlayerName(senderId);
            CoopLogger.Info(LogCategory.Sync, $"{pName} entered Map Node #{nodeIndex} ({st}).");
            HostSelectedMapNodeIndex = nodeIndex;
            MapNodeVotes.Clear();
            using (NetworkScope.Enter())
            {
                msp.OnClickMapNodeButton(clicked);
            }
            BroadcastRunSnapshot(-1);
        }
    }
}
