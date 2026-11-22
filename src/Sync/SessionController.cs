using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using EmberwardCoop.Core;
using EmberwardCoop.Localization;
using EmberwardCoop.Networking;
using EmberwardCoop.Presence;
using EmberwardCoop.Rules;
using EmberwardCoop.Serialization;
using UnityEngine;

namespace EmberwardCoop.Sync
{
    public sealed class SessionController
    {
        public static SessionController Instance { get; private set; }

        public readonly NetworkManager Net = new NetworkManager();
        public readonly SessionSettings Settings = new SessionSettings();
        public readonly LobbySetupState LobbySetup;
        public readonly PartyUnlockResolver Unlocks = new PartyUnlockResolver();

        private readonly SharedResourcesRuleset _sharedRuleset;
        private readonly SeparateResourcesRuleset _separateRuleset;
        public ICoopRuleset Ruleset => Settings.EconomyMode == CoopEconomyMode.SeparateResources
            ? (ICoopRuleset)_separateRuleset
            : _sharedRuleset;

        public readonly RunStateSync Run;
        public readonly EconomyAndHandSync Economy;
        public readonly BoardAndCombatSync Board;
        public readonly WaveAndSpeedSync Waves;
        public readonly ModalAndRewardSync Modals;
        public readonly CoopVoteAndDraftManager Votes;
        public readonly QuestAndMetaSync Quests;
        public readonly OwnershipAndStatsTracker Ownership;
        public readonly CoopBalanceManager Balance;
        public readonly ResourceGiftingManager Gifting;
        public readonly BuildUndoManager Undo;

        public readonly PlayerPresenceManager Presence;
        public readonly GhostPreviewManager Ghosts;
        public readonly PingAndFeedbackManager Feedback;
        public readonly EmoteAndCalloutManager Emotes;
        public readonly MazeSketchManager Sketch;

        public event Action OnSettingsChanged;
        public event Action<LobbySetupChangeMask> OnLobbySetupChanged;
        public event Action<string> OnHostClosedLobby;
        public event Action OnRunLaunchStarted;
        public event Action<string> OnMidWavePeerDisconnect;
        public event Action<string> OnPeerReconnected;

        private bool _hostExplicitlyClosedFlag;

        public SessionController()
        {
            Instance = this;
            LobbySetup = new LobbySetupState(Settings);

            Ownership = new OwnershipAndStatsTracker(Net, () => this);
            Balance = new CoopBalanceManager(Net, () => this);
            Gifting = new ResourceGiftingManager(Net, () => this);
            Undo = new BuildUndoManager(Net, () => this);

            _sharedRuleset = new SharedResourcesRuleset(Settings);
            _separateRuleset = new SeparateResourcesRuleset(Settings, Ownership);
            Settings.ApplyPreset(SessionPreset.BalancedCoop, 2);

            Run = new RunStateSync(Net, () => this);
            Economy = new EconomyAndHandSync(Net, () => this);
            Board = new BoardAndCombatSync(Net, () => this);
            Waves = new WaveAndSpeedSync(Net, () => this);
            Modals = new ModalAndRewardSync(Net, () => this);
            Votes = new CoopVoteAndDraftManager(Net, () => this);
            Votes.OnMapVotesChanged += () => Run.NotifyMapVotesChanged();
            Quests = new QuestAndMetaSync(Net);

            Ghosts = new GhostPreviewManager(Net);
            Feedback = new PingAndFeedbackManager(Net);
            Emotes = new EmoteAndCalloutManager(Net);
            Sketch = new MazeSketchManager(Net);
            Presence = new PlayerPresenceManager(Net, Ghosts, Feedback);
        }

        public void Initialize()
        {
            Net.Initialize(DispatchPacket);

            Net.OnPeerUnlocksUpdated += () =>
            {
                if (Net.IsHost)
                {
                    RefreshPartyUnlocksAndValidateSelection(broadcast: true);
                }
            };

            Net.OnPlayerJoined += peer =>
            {
                if (Net.IsHost)
                {
                    Settings.OnPlayerCountChanged(Net.Roster.Count);
                    RefreshPartyUnlocksAndValidateSelection(broadcast: false);
                    HostBroadcastLobbySetup(LobbySetupChangeMask.SessionRules | LobbySetupChangeMask.Unlocks, targetPeerId: peer.PlayerId, resetReadyOnMajor: false);
                    SendFullSyncToPeer(peer.PlayerId);
                    SyncHostLobbyMetadata();
                }
                OnPeerReconnected?.Invoke(peer.Name);
                GameRefs.ShowGameToast($"{peer.Name} joined Co-op!", 2.5f);
            };

            Net.OnPlayerLeft += peer =>
            {
                Presence.RemoveRemotePlayer(peer.PlayerId);
                Votes.OnPeerDisconnected(peer.PlayerId);
                if (Net.IsHost)
                {
                    Settings.OnPlayerCountChanged(Math.Max(2, Net.Roster.Count));
                    RefreshPartyUnlocksAndValidateSelection(broadcast: false);
                    HostBroadcastLobbySetup(LobbySetupChangeMask.SessionRules | LobbySetupChangeMask.Unlocks, resetReadyOnMajor: false);
                    SyncHostLobbyMetadata();
                }
                OnMidWavePeerDisconnect?.Invoke(peer.Name);
                GameRefs.ShowGameToast($"{peer.Name} disconnected.", 2.5f);
            };

            Net.OnJoinedHost += () =>
            {
                _hostExplicitlyClosedFlag = false;
                LobbySetup.IsLaunchingRun = false;
                Run.MenuLockDirty = true;
                Unlocks.RefreshLocalSummary();
                Net.SendLocalUnlockSummary();
                Net.SendToHost(PacketType.RequestFullSync);
            };

            Net.OnDisconnectedFromSession += reason =>
            {
                bool unintendedDrop = !Net.LastDisconnectWasIntentional && !_hostExplicitlyClosedFlag;
                _hostExplicitlyClosedFlag = false;
                PartyUnlockResolver.CoopUnlockAllActive = false;
                ResetSessionState();
                if (unintendedDrop && GameRefs.IsInTitle)
                {
                    OnHostClosedLobby?.Invoke(CoopLoc.T("lobby.host_closed_body"));
                }
            };
        }

        public void RefreshPartyUnlocksAndValidateSelection(bool broadcast)
        {
            PartyUnlockResolver.CoopUnlockAllActive = Net.IsConnected && Settings.UnlockAllMapsModes;
            Unlocks.RecomputeFromRoster(Net);

            LobbySetup.SyncedPartyUnlocks = Unlocks.PartyBaseline.Clone();
            LobbySetup.SyncedMostProgressedPlayerId = Unlocks.MostProgressedPlayerId;
            LobbySetup.SyncedMostProgressedPlayerName = Unlocks.MostProgressedPlayerName;

            if (!Net.IsHost) return;

            LobbySetupChangeMask changed = LobbySetupChangeMask.Unlocks;

            var validDiffs = WorldCatalog.GetPartySelectableDifficulties(Unlocks);
            if (!validDiffs.Contains(LobbySetup.DifficultyTier))
            {
                LobbySetup.DifficultyTier = validDiffs.Contains(eGameDifficultyType.NORMAL)
                    ? eGameDifficultyType.NORMAL
                    : validDiffs[0];
                changed |= LobbySetupChangeMask.Difficulty;
            }

            var validWorlds = WorldCatalog.GetHostSelectableWorlds(LobbySetup.DifficultyTier, Unlocks);
            bool worldValid = false;
            for (int i = 0; i < validWorlds.Count; i++)
                if (validWorlds[i].WorldType == LobbySetup.SelectedWorld) { worldValid = true; break; }
            if (!worldValid && validWorlds.Count > 0)
            {
                LobbySetup.SelectedWorld = validWorlds[0].WorldType;
                changed |= LobbySetupChangeMask.World;
            }

            var validHeroes = LoadoutCatalog.GetSelectableHeroes(Unlocks);
            if (!validHeroes.Contains(LobbySetup.HostHero))
            {
                LobbySetup.HostHero = validHeroes[0];
                changed |= LobbySetupChangeMask.Hero;
            }

            if (broadcast)
                HostBroadcastLobbySetup(changed, resetReadyOnMajor: (changed & LobbySetupChangeMask.MajorSetup) != 0);
        }

        public void HostBroadcastLobbySetup(LobbySetupChangeMask changeMask, int targetPeerId = -1, bool resetReadyOnMajor = true)
        {
            if (!Net.IsHost) return;

            PartyUnlockResolver.CoopUnlockAllActive = Net.IsConnected && Settings.UnlockAllMapsModes;
            Unlocks.RecomputeFromRoster(Net);
            LobbySetup.SyncedPartyUnlocks = Unlocks.PartyBaseline.Clone();
            LobbySetup.SyncedMostProgressedPlayerId = Unlocks.MostProgressedPlayerId;
            LobbySetup.SyncedMostProgressedPlayerName = Unlocks.MostProgressedPlayerName;

            if (targetPeerId < 0)
            {
                LobbySetup.Revision++;
                if (resetReadyOnMajor && (changeMask & LobbySetupChangeMask.MajorSetup) != 0)
                {
                    if (Net.ResetAllClientsReady())
                        CoopLogger.Info(LogCategory.Sync, CoopLoc.T("lobby.ready_reset_notice"));
                }
            }

            Action<BinaryWriter> writer = w => LobbySetup.Write(w, changeMask);
            if (targetPeerId >= 0) Net.SendToPeer(targetPeerId, PacketType.LobbySetupSync, writer);
            else Net.BroadcastToClients(PacketType.LobbySetupSync, writer);

            OnSettingsChanged?.Invoke();
            OnLobbySetupChanged?.Invoke(changeMask);
            SyncHostLobbyMetadata();
        }

        public string GetCurrentCoopModeTag() =>
            Settings.EconomyMode == CoopEconomyMode.SeparateResources
                ? LobbyMetadataKeys.ModeSeparate
                : LobbyMetadataKeys.ModeShared;

        public void SyncHostLobbyMetadata()
        {
            if (!Net.IsHost) return;
            bool inProgress = !GameRefs.IsInTitle || LobbySetup.IsLaunchingRun;
            Net.UpdateLobbyMetadata(GameRefs.ActiveScene, GetCurrentCoopModeTag(), inProgress, joinableInProgress: false);
        }

        public void BroadcastSessionSettings(int targetPeerId = -1)
        {
            HostBroadcastLobbySetup(LobbySetupChangeMask.SessionRules, targetPeerId, resetReadyOnMajor: false);
        }

        private void HandleLobbySetupSync(BinaryReader r)
        {
            LobbySetupChangeMask diff = LobbySetup.ReadFrom(r);
            PartyUnlockResolver.CoopUnlockAllActive = Net.IsConnected && Settings.UnlockAllMapsModes;
            Unlocks.ApplySyncedBaseline(LobbySetup.SyncedPartyUnlocks, LobbySetup.SyncedMostProgressedPlayerId, LobbySetup.SyncedMostProgressedPlayerName);
            OnSettingsChanged?.Invoke();
            OnLobbySetupChanged?.Invoke(diff);

            if ((diff & LobbySetupChangeMask.LaunchState) != 0 && LobbySetup.IsLaunchingRun)
            {
                OnRunLaunchStarted?.Invoke();
            }
        }

        private void HandleSessionSettingsSync(BinaryReader r)
        {
            Settings.ReadFrom(r);
            PartyUnlockResolver.CoopUnlockAllActive = Net.IsConnected && Settings.UnlockAllMapsModes;
            OnSettingsChanged?.Invoke();
            OnLobbySetupChanged?.Invoke(LobbySetupChangeMask.SessionRules);
        }

        public bool ValidateLoadoutsBeforeStart(out string errorMessage)
        {
            errorMessage = "";
            if (LobbySetup.IsLoadRunMode)
            {
                if (string.IsNullOrWhiteSpace(LobbySetup.SelectedSaveId) || CoopSaveStorage.LoadSave(LobbySetup.SelectedSaveId) == null)
                {
                    errorMessage = CoopLoc.T("save.none_selected");
                    return false;
                }
                return true;
            }

            if (LobbySetup.HostHero == eCharacterType.NONE)
            {
                errorMessage = CoopLoc.T("loadout.select_hero_first");
                return false;
            }

            if (Ruleset.UsesIndividualLoadouts)
            {
                foreach (var kv in Net.Roster)
                {
                    var p = kv.Value;
                    if (p.Loadout == null || !p.Loadout.IsValid)
                    {
                        errorMessage = CoopLoc.F("loadout.player_invalid", p.Name);
                        return false;
                    }
                }
            }
            return true;
        }

        public void HostLaunchCoopRun()
        {
            if (!Net.IsHost || !GameRefs.IsInTitle || LobbySetup.IsLaunchingRun) return;

            if (LobbySetup.IsLoadRunMode && !string.IsNullOrWhiteSpace(LobbySetup.SelectedSaveId))
            {
                HostLaunchLoadedCoopRun(LobbySetup.SelectedSaveId);
                return;
            }

            int seed = LobbySetup.MasterSeed != 0
                ? LobbySetup.MasterSeed
                : Math.Abs(Environment.TickCount ^ UnityEngine.Random.Range(10000, 9999999));
            if (seed == 0) seed = 424242;

            LobbySetup.IsLaunchingRun = true;
            Run.ActiveCoopSaveId = CoopSaveStorage.CreateNewSaveId(LobbySetup.SelectedWorld);
            Run.HasHydratedLocalStartLoadout = false;
            Run.PendingRestoreBoardBytes = null;
            Run.PendingRestoreCoin = -1;
            PartyUnlockResolver.CoopUnlockAllActive = Settings.UnlockAllMapsModes;

            HostBroadcastLobbySetup(LobbySetupChangeMask.LaunchState, targetPeerId: -1, resetReadyOnMajor: false);
            Net.BroadcastToClients(PacketType.EvtStartCoopRun, w =>
            {
                w.Write(seed);
                w.Write((int)LobbySetup.SelectedWorld);
                w.Write((int)LobbySetup.DifficultyTier);
                w.Write((int)LobbySetup.HostHero);
                w.Write(false); // isLoadedSave
            });

            OnRunLaunchStarted?.Invoke();

            try
            {
                var gdm = GameDataManager.instance;
                if (gdm != null)
                {
                    // Launch onto isolated tempSession so Host's single-player active save is never touched.
                    SoloSaveGuard.InitializeFreshTempRun(seed, LobbySetup.SelectedWorld, LobbySetup.DifficultyTier);

                    var gd = gdm.GameplayData;
                    var hostChoice = Ruleset.UsesIndividualLoadouts ? Net.LocalLoadout : LobbySetup.HostSharedLoadout;
                    LoadoutCatalog.HydrateGameplayData(gd, LobbySetup.HostHero, hostChoice, seed, forceOverwriteInventory: true);
                    Run.HasHydratedLocalStartLoadout = true;
                    Run.PendingStageSeed = seed;

                    // Pre-save initial co-op slot cleanly
                    CoopSaveStorage.SaveActiveRun(this, Run.ActiveCoopSaveId, "new_run");

                    CoopLogger.Info(LogCategory.Sync, $"Launching standalone Co-op Run: {WorldCatalog.GetWorldDisplayName(LobbySetup.SelectedWorld)} ({LobbySetup.DifficultyTier}, Hero {LobbySetup.HostHero}, Seed {seed})");
                    RefiUtilities.SceneLoader.Load("MapScene");
                }
                else
                {
                    LobbySetup.IsLaunchingRun = false;
                    CoopLogger.Error(LogCategory.Sync, "Cannot launch run: GameDataManager.instance is null.");
                }
            }
            catch (Exception ex)
            {
                LobbySetup.IsLaunchingRun = false;
                CoopLogger.Error(LogCategory.Sync, "HostLaunchCoopRun failed", ex);
            }
        }

        public void HostLaunchLoadedCoopRun(string saveId)
        {
            if (!Net.IsHost || !GameRefs.IsInTitle || LobbySetup.IsLaunchingRun) return;
            var save = CoopSaveStorage.LoadSave(saveId);
            if (save == null)
            {
                GameRefs.ShowGameToast(CoopLoc.T("save.load_failed"), 2.5f);
                return;
            }

            LobbySetup.IsLaunchingRun = true;
            CoopSaveStorage.ApplySettingsFromSave(save, Settings);
            LobbySetup.SelectedWorld = (eWorldType)save.WorldType;
            LobbySetup.DifficultyTier = (eGameDifficultyType)save.Difficulty;
            LobbySetup.HostHero = save.HostHero != 0 ? (eCharacterType)save.HostHero : eCharacterType.BASIC;
            PartyUnlockResolver.CoopUnlockAllActive = Settings.UnlockAllMapsModes;

            Run.ActiveCoopSaveId = save.SaveId;
            Run.HasHydratedLocalStartLoadout = true;
            Run.PendingStageSeed = save.MasterSeed;
            Run.PendingRestoreBoardBytes = !string.IsNullOrEmpty(save.BoardSnapshotBase64)
                ? Convert.FromBase64String(save.BoardSnapshotBase64)
                : null;

            HostBroadcastLobbySetup(LobbySetupChangeMask.LaunchState | LobbySetupChangeMask.MajorSetup | LobbySetupChangeMask.SessionRules, targetPeerId: -1, resetReadyOnMajor: false);
            Net.BroadcastToClients(PacketType.EvtStartCoopRun, w =>
            {
                w.Write(save.MasterSeed);
                w.Write(save.WorldType);
                w.Write(save.Difficulty);
                w.Write((int)LobbySetup.HostHero);
                w.Write(true); // isLoadedSave
            });
            OnRunLaunchStarted?.Invoke();

            try
            {
                SoloSaveGuard.EnterCoopTempSession();
                byte[] runBytes = Convert.FromBase64String(save.RunSnapshotBase64);
                Run.ApplyRunSnapshotBytes(runBytes, loadScene: false);

                // Match each connected peer to a saved player slot and restore their personal inventory.
                var usedSlots = new HashSet<int>();
                var gd = GameDataManager.instance?.GameplayData;

                if (Net.Roster.TryGetValue(0, out var hostPeer))
                {
                    var hostSaved = CoopSaveStorage.MatchSavedPlayer(save, hostPeer, usedSlots);
                    if (hostSaved != null)
                    {
                        CoopSaveStorage.ApplySavedInventoryToGameplay(gd, hostSaved);
                        Run.PendingRestoreCoin = hostSaved.Coin;
                    }
                }

                foreach (var kv in Net.Roster)
                {
                    if (kv.Key == 0) continue;
                    var peerSaved = CoopSaveStorage.MatchSavedPlayer(save, kv.Value, usedSlots);
                    if (peerSaved != null)
                        Run.SendPeerInventoryRestore(kv.Key, peerSaved);
                }

                string targetScene = save.TargetScene == "MainGame" ? "MainGame" : "MapScene";
                CoopLogger.Info(LogCategory.Sync, $"Resuming Co-op Save '{save.SaveId}' -> {targetScene} (Step {save.CurMapStep + 1}).");
                RefiUtilities.SceneLoader.Load(targetScene);
            }
            catch (Exception ex)
            {
                LobbySetup.IsLaunchingRun = false;
                CoopLogger.Error(LogCategory.Sync, "HostLaunchLoadedCoopRun failed", ex);
            }
        }

        public bool SaveActiveCoopRun(string reason = "")
        {
            if (!Net.IsHost || GameRefs.IsInTitle) return false;
            if (string.IsNullOrWhiteSpace(Run.ActiveCoopSaveId))
                Run.ActiveCoopSaveId = CoopSaveStorage.CreateNewSaveId(LobbySetup.SelectedWorld);
            return CoopSaveStorage.SaveActiveRun(this, Run.ActiveCoopSaveId, reason);
        }

        private void HandleEvtStartCoopRun(BinaryReader r)
        {
            int seed = r.ReadInt32();
            var world = (eWorldType)r.ReadInt32();
            var diff = (eGameDifficultyType)r.ReadInt32();
            var hero = r.BaseStream.Position < r.BaseStream.Length ? (eCharacterType)r.ReadInt32() : eCharacterType.BASIC;
            bool isLoadedSave = r.BaseStream.Position < r.BaseStream.Length && r.ReadBoolean();

            LobbySetup.SelectedWorld = world;
            LobbySetup.DifficultyTier = diff;
            LobbySetup.HostHero = hero == eCharacterType.NONE ? eCharacterType.BASIC : hero;
            LobbySetup.IsLaunchingRun = true;
            Run.PendingStageSeed = seed;
            Run.HasHydratedLocalStartLoadout = isLoadedSave;
            PartyUnlockResolver.CoopUnlockAllActive = Settings.UnlockAllMapsModes;

            // Ensure client starts cleanly on isolated tempSession with pre-hydrated loadout.
            SoloSaveGuard.EnterCoopTempSession();
            if (!isLoadedSave)
            {
                SoloSaveGuard.InitializeFreshTempRun(seed, world, diff);
                var gd = GameDataManager.instance?.GameplayData;
                var choice = Ruleset.UsesIndividualLoadouts ? Net.LocalLoadout : LobbySetup.HostSharedLoadout;
                int peerSeed = Ruleset.UsesIndividualLoadouts ? unchecked(seed ^ (Net.LocalPlayerId * 73856093)) : seed;
                LoadoutCatalog.HydrateGameplayData(gd, LobbySetup.HostHero, choice, peerSeed, forceOverwriteInventory: true);
                Run.HasHydratedLocalStartLoadout = true;
            }

            CoopLogger.Info(LogCategory.Sync, CoopLoc.T("lobby.launching_run"));
            OnRunLaunchStarted?.Invoke();
        }

        public void HostDisbandLobby()
        {
            if (Net.IsHost)
            {
                try
                {
                    Net.BroadcastToClients(PacketType.EvtHostClosedLobby, w => w.WriteSafeString(Net.LocalPlayerName));
                }
                catch { }
            }
            Net.Disconnect(intentional: true);
        }

        private void HandleEvtHostClosedLobby(BinaryReader r)
        {
            string hostName = r.ReadString();
            _hostExplicitlyClosedFlag = true;
            Net.Disconnect(intentional: false);
            string msg = string.IsNullOrWhiteSpace(hostName)
                ? CoopLoc.T("lobby.host_closed_body")
                : CoopLoc.F("lobby.host_closed_named", hostName);
            OnHostClosedLobby?.Invoke(msg);
        }

        public void ResetSessionState()
        {
            CoopInputGuard.Reset();
            LobbySetup.IsLaunchingRun = false;
            Presence.ClearAll();
            Ghosts.ClearAll();
            Feedback.ClearAll();
            Emotes.ClearAll();
            Sketch.ClearAll();
            Undo.ClearHistory();
            Run.Reset();
            Economy.ResetBattleCache();
            Waves.ResetBattleState();
            Modals.Reset();
            Votes.Reset();
            Quests.Reset();
            Ownership.ResetRunStats();
            Balance.ResetForNewStage();
            Gifting.ResetWaveCounter();
        }

        public void SendFullSyncToPeer(int peerId)
        {
            if (!Net.IsHost) return;
            HostBroadcastLobbySetup(LobbySetupChangeMask.None, targetPeerId: peerId, resetReadyOnMajor: false);
            Run.BroadcastRunSnapshot(peerId);
            if (GameRefs.IsInBattle)
            {
                Economy.BroadcastSnapshot(peerId);
                Board.BroadcastBoardSnapshot(peerId);
                Quests.BroadcastQuestSnapshot(peerId);
                Balance.BroadcastTurnBlockCount();
            }
            if (Modals.ActiveHostModal != CoopModalKind.None)
                Modals.BroadcastModalState(Modals.ActiveHostModal, Modals.ActiveHostModalBanner);
            Ownership.BroadcastRunStats();
        }

        public void ForceFullSyncAll()
        {
            if (Net.IsHost)
            {
                HostBroadcastLobbySetup(LobbySetupChangeMask.None, targetPeerId: -1, resetReadyOnMajor: false);
                Run.BroadcastRunSnapshot(-1);
                if (GameRefs.IsInBattle)
                {
                    Economy.BroadcastSnapshot(-1);
                    Board.BroadcastBoardSnapshot(-1);
                    Quests.BroadcastQuestSnapshot(-1);
                    Balance.BroadcastTurnBlockCount();
                }
                Ownership.BroadcastRunStats();
                CoopLogger.Info(LogCategory.Sync, "Sent full state snapshot to all peers.");
            }
            else if (Net.IsClient)
            {
                Net.SendToHost(PacketType.RequestFullSync);
                CoopLogger.Info(LogCategory.Sync, "Requested full state sync from Host.");
            }
        }

        public void Tick()
        {
            bool sceneChanged = GameRefs.RefreshSceneCache();
            string activeScene = GameRefs.ActiveScene;

            if (sceneChanged)
            {
                UI.NativeAssetRegistry.OnSceneChanged();
                UI.NativeStyleProvider.RefreshFontForActiveLanguage();
                Run.MenuLockDirty = true;
                Presence.ClearAll();
                Ghosts.ClearAll();
                Feedback.ClearAll();
                Emotes.ClearAll();
                Sketch.ClearAll();
                Undo.ClearHistory();
                Ownership.ResetStageEntities();
                Balance.ResetForNewStage();
                Gifting.ResetWaveCounter();

                if (GameRefs.IsInTitle)
                {
                    LobbySetup.IsLaunchingRun = false;
                    Votes.ClearMapVotes();
                    if (!Net.IsConnected)
                        SoloSaveGuard.ExitCoopTempSession();
                }
                else if (GameRefs.IsInMap)
                {
                    Votes.ClearMapVotes();
                    if (Net.IsHost) SaveActiveCoopRun("entered_map");
                }
                else if (GameRefs.IsInBattle)
                {
                    Economy.ResetBattleCache();
                    Waves.ResetBattleState();
                    Quests.Reset();
                }
                if (Net.IsHost)
                    SyncHostLobbyMetadata();
            }

            Net.Poll();

            float now = Time.unscaledTime;
            float dt = Time.unscaledDeltaTime;
            Run.Tick(activeScene, now);

            if (!Net.IsConnected) return;

            Economy.Tick(now);
            Waves.TickHostPauseState();
            Modals.Tick(now);
            Votes.Tick(now);
            Quests.Tick(now);
            Ownership.Tick(now);
            Emotes.Tick(now, dt);

            if (GameRefs.IsInBattle)
            {
                Presence.Tick(now);
                Feedback.Tick(dt);
                Sketch.Tick(now, dt);
                Undo.Tick();
            }
        }

        public void SendChat(string text)
        {
            if (string.IsNullOrWhiteSpace(text) || !Net.IsConnected) return;
            string clean = text.Trim();
            if (clean.Length > 200) clean = clean.Substring(0, 200);
            string sender = Net.LocalPlayerName ?? "Player";
            Color senderCol = CoopConfig.GetPlayerColor(Net.LocalColorIndex);
            CoopLogger.PushChatMessage(ChatEntryKind.PlayerChat, $"{sender}: {clean}", senderCol);
            Net.SendToAllPeersOrHost(PacketType.ChatMessage, w =>
            {
                w.WriteSafeString(sender);
                w.WriteSafeString(clean);
            });
        }

        private void HandleChatMessage(int senderId, BinaryReader r)
        {
            string sender = r.ReadString();
            string text = r.ReadString();
            Color senderCol = CoopConfig.GetPlayerColor(Net.GetPlayerColorIndex(senderId));
            CoopLogger.PushChatMessage(ChatEntryKind.PlayerChat, $"{sender}: {text}", senderCol);
            if (Net.IsHost)
            {
                Net.BroadcastToClients(PacketType.ChatMessage, w =>
                {
                    w.WriteSafeString(sender);
                    w.WriteSafeString(text);
                }, DeliveryMode.ReliableOrdered, excludePeerId: senderId);
            }
        }

        private void DispatchPacket(IncomingPacket pkt)
        {
            try
            {
                using var ms = new MemoryStream(pkt.Payload);
                using var r = new BinaryReader(ms, Encoding.UTF8);

                switch (pkt.Type)
                {
                    case PacketType.ChatMessage: HandleChatMessage(pkt.SenderPlayerId, r); break;
                    case PacketType.SessionSettingsSync: if (Net.IsClient) HandleSessionSettingsSync(r); break;
                    case PacketType.LobbySetupSync: if (Net.IsClient) HandleLobbySetupSync(r); break;
                    case PacketType.EvtHostClosedLobby: if (Net.IsClient) HandleEvtHostClosedLobby(r); break;
                    case PacketType.EvtStartCoopRun: if (Net.IsClient) HandleEvtStartCoopRun(r); break;

                    case PacketType.RequestFullSync: if (Net.IsHost) SendFullSyncToPeer(pkt.SenderPlayerId); break;
                    case PacketType.RunSnapshot: if (Net.IsClient) Run.HandleRunSnapshot(r); break;
                    case PacketType.EconomyAndHandSnapshot: if (Net.IsClient) Economy.HandleSnapshot(r); break;
                    case PacketType.BoardSnapshot: if (Net.IsClient) Board.HandleBoardSnapshot(r); break;
                    case PacketType.EvtStateHash: if (Net.IsClient) Economy.HandleEvtStateHash(r); break;
                    case PacketType.CmdDesyncReport: if (Net.IsHost) Economy.HandleCmdDesyncReport(pkt.SenderPlayerId, r); break;
                    case PacketType.EvtPeerInventoryRestore: if (Net.IsClient) Run.HandleEvtPeerInventoryRestore(r); break;

                    case PacketType.CmdPlaceTetris: if (Net.IsHost) Board.HandleCmdPlaceTetris(pkt.SenderPlayerId, r); break;
                    case PacketType.EvtPlaceTetris: if (Net.IsClient) Board.HandleEvtPlaceTetris(r); break;
                    case PacketType.CmdRecallTetris: if (Net.IsHost) Board.HandleCmdRecallTetris(pkt.SenderPlayerId, r); break;
                    case PacketType.EvtRecallTetris: if (Net.IsClient) Board.HandleEvtRecallTetris(r); break;
                    case PacketType.CmdActivateRuneStone: if (Net.IsHost) Board.HandleCmdActivateRuneStone(pkt.SenderPlayerId, r); break;
                    case PacketType.EvtActivateRuneStone: if (Net.IsClient) Board.HandleEvtActivateRuneStone(r); break;

                    case PacketType.CmdPlaceTower: if (Net.IsHost) Board.HandleCmdPlaceTower(pkt.SenderPlayerId, r); break;
                    case PacketType.EvtPlaceTower: if (Net.IsClient) Board.HandleEvtPlaceTower(r); break;
                    case PacketType.CmdUpgradeTower: if (Net.IsHost) Board.HandleCmdUpgradeTower(pkt.SenderPlayerId, r); break;
                    case PacketType.EvtUpgradeTower: if (Net.IsClient) Board.HandleEvtUpgradeTower(r); break;
                    case PacketType.CmdSellTower: if (Net.IsHost) Board.HandleCmdSellTower(pkt.SenderPlayerId, r); break;
                    case PacketType.EvtSellTower: if (Net.IsClient) Board.HandleEvtSellTower(r); break;
                    case PacketType.CmdTowerPriority: if (Net.IsHost) Board.HandleCmdTowerPriority(pkt.SenderPlayerId, r); break;
                    case PacketType.EvtTowerPriority: if (Net.IsClient) Board.HandleEvtTowerPriority(r); break;
                    case PacketType.CmdTowerSpecialAction: if (Net.IsHost) Board.HandleCmdTowerSpecialAction(pkt.SenderPlayerId, r); break;
                    case PacketType.EvtTowerSpecialAction: if (Net.IsClient) Board.HandleEvtTowerSpecialAction(r); break;

                    case PacketType.CmdDrawCard: if (Net.IsHost) Board.HandleCmdDrawCard(pkt.SenderPlayerId); break;
                    case PacketType.CmdMulligan: if (Net.IsHost) Board.HandleCmdMulligan(pkt.SenderPlayerId); break;
                    case PacketType.CmdUseBuff: if (Net.IsHost) Board.HandleCmdUseBuff(pkt.SenderPlayerId, r); break;
                    case PacketType.EvtUseBuff: if (Net.IsClient) Board.HandleEvtUseBuff(r); break;
                    case PacketType.CmdOpenChest: if (Net.IsHost) Board.HandleCmdOpenChest(pkt.SenderPlayerId, r); break;
                    case PacketType.EvtOpenChest: if (Net.IsClient) Board.HandleEvtOpenChest(r); break;
                    case PacketType.CmdFocusMonster: if (Net.IsHost) Board.HandleCmdFocusMonster(r); break;
                    case PacketType.EvtFocusMonster: if (Net.IsClient) Board.HandleEvtFocusMonster(r); break;

                    case PacketType.CmdToggleWaveReady: if (Net.IsHost) Waves.HandleCmdToggleWaveReady(pkt.SenderPlayerId, r); break;
                    case PacketType.CmdStartWave: if (Net.IsHost) Waves.HandleCmdStartWave(pkt.SenderPlayerId); break;
                    case PacketType.EvtStartWave: if (Net.IsClient) Waves.HandleEvtStartWave(r); break;
                    case PacketType.EvtWaveEnd: if (Net.IsClient) Waves.HandleEvtWaveEnd(r); break;
                    case PacketType.CmdSetGameSpeed: if (Net.IsHost) Waves.HandleCmdSetGameSpeed(pkt.SenderPlayerId, r); break;
                    case PacketType.EvtSetGameSpeed: if (Net.IsClient) Waves.HandleEvtSetGameSpeed(r); break;
                    case PacketType.EvtPauseState: if (Net.IsClient) Waves.HandleEvtPauseState(r); break;

                    case PacketType.CmdVoteMapNode: if (Net.IsHost) Votes.HandleCmdVoteMapNode(pkt.SenderPlayerId, r); break;
                    case PacketType.EvtMapNodeVotes: if (Net.IsClient) Votes.HandleEvtMapNodeVotes(r); break;
                    case PacketType.CmdSelectMapNode: if (Net.IsHost) Votes.HandleCmdVoteMapNode(pkt.SenderPlayerId, r); break;
                    case PacketType.EvtMapNodeResolved: if (Net.IsClient) Votes.HandleEvtMapNodeResolved(r); break;
                    case PacketType.EvtModalState: if (Net.IsClient) Modals.HandleEvtModalState(r); break;
                    case PacketType.EvtModalClosed: if (Net.IsClient) Modals.HandleEvtModalClosed(); break;
                    case PacketType.EvtVictoryOrDefeat: if (Net.IsClient) Modals.HandleEvtVictoryOrDefeat(r); break;
                    case PacketType.EvtOpenPeerNodeModal: if (Net.IsClient) Votes.HandleEvtOpenPeerNodeModal(r); break;
                    case PacketType.EvtSharedDraftState: if (Net.IsClient) Votes.HandleEvtSharedDraftState(r); break;
                    case PacketType.CmdDraftVote: if (Net.IsHost) Votes.HandleCmdDraftVote(pkt.SenderPlayerId, r); break;
                    case PacketType.CmdDraftReroll: if (Net.IsHost) Votes.HandleCmdDraftReroll(pkt.SenderPlayerId, r); break;
                    case PacketType.EvtSharedDraftClosed: if (Net.IsClient) Votes.HandleEvtSharedDraftClosed(); break;
                    case PacketType.CmdDraftBarrierState: if (Net.IsHost) Votes.HandleCmdDraftBarrierState(pkt.SenderPlayerId, r); break;
                    case PacketType.EvtDraftBarrierState: if (Net.IsClient) Votes.HandleEvtDraftBarrierState(r); break;

                    case PacketType.EvtQuestSnapshot: if (Net.IsClient) Quests.HandleEvtQuestSnapshot(r); break;
                    case PacketType.EvtQuestProgress: if (Net.IsClient) Quests.HandleEvtQuestProgress(r); break;
                    case PacketType.CmdClientQuestTrigger: if (Net.IsHost) Quests.HandleCmdClientQuestTrigger(pkt.SenderPlayerId, r); break;
                    case PacketType.EvtMetaRewardGrant: if (Net.IsClient) Quests.HandleEvtMetaRewardGrant(r); break;
                    case PacketType.EvtAchievementTrigger: if (Net.IsClient) Quests.HandleEvtAchievementTrigger(r); break;

                    case PacketType.CursorAndGhostState: Presence.HandleRemoteCursorPacket(pkt.SenderPlayerId, r); break;
                    case PacketType.MapPing: Feedback.HandlePingPacket(pkt.SenderPlayerId, r); break;
                    case PacketType.StickerEmote: Emotes.HandleStickerPacket(pkt.SenderPlayerId, r); break;
                    case PacketType.QuickCallout: Emotes.HandleCalloutPacket(pkt.SenderPlayerId, r); break;
                    case PacketType.MazeSketchTrail: Sketch.HandleSketchPacket(pkt.SenderPlayerId, r); break;

                    case PacketType.CmdGiftGold: if (Net.IsHost) Gifting.HandleCmdGiftGold(pkt.SenderPlayerId, r); break;
                    case PacketType.EvtGiftGold: if (Net.IsClient) Gifting.HandleEvtGiftGold(r); break;
                    case PacketType.PlayerPeekUpdate: Ownership.HandlePeekUpdate(pkt.SenderPlayerId, r); break;
                    case PacketType.PlayerStatsSync: if (Net.IsClient) Ownership.HandleRunStatsSync(r); break;
                    case PacketType.EvtTurnBlockCount: if (Net.IsClient) Balance.HandleEvtTurnBlockCount(r); break;
                    case PacketType.CmdUndoBuild: if (Net.IsHost) Undo.HandleCmdUndoBuild(pkt.SenderPlayerId, r); break;
                    case PacketType.EvtUndoBuild: if (Net.IsClient) Undo.HandleEvtUndoBuild(r); break;
                }
            }
            catch (Exception ex)
            {
                CoopLogger.Error(LogCategory.Net, $"Error handling packet {pkt.Type} from #{pkt.SenderPlayerId}", ex);
            }
        }
    }
}
