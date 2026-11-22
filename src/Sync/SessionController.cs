using System;
using System.IO;
using System.Text;
using EmberwardCoop.Core;
using EmberwardCoop.Localization;
using EmberwardCoop.Networking;
using EmberwardCoop.Presence;
using EmberwardCoop.Rules;
using UnityEngine;

namespace EmberwardCoop.Sync
{
    public sealed class SessionController
    {
        public static SessionController Instance { get; private set; }

        public readonly NetworkManager Net = new NetworkManager();
        public readonly SessionSettings Settings = new SessionSettings();
        public readonly LobbySetupState LobbySetup;

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

            Net.OnPlayerJoined += peer =>
            {
                if (Net.IsHost)
                {
                    Settings.OnPlayerCountChanged(Net.Roster.Count);
                    HostBroadcastLobbySetup(LobbySetupChangeMask.SessionRules, targetPeerId: peer.PlayerId, resetReadyOnMajor: false);
                    SendFullSyncToPeer(peer.PlayerId);
                }
                OnPeerReconnected?.Invoke(peer.Name);
                GameRefs.ShowGameToast($"{peer.Name} joined Co-op!", 2.5f);
            };

            Net.OnPlayerLeft += peer =>
            {
                Presence.RemoveRemotePlayer(peer.PlayerId);
                Run.MapNodeVotes.Remove(peer.PlayerId);
                if (Net.IsHost)
                {
                    Settings.OnPlayerCountChanged(Math.Max(2, Net.Roster.Count));
                    HostBroadcastLobbySetup(LobbySetupChangeMask.SessionRules, resetReadyOnMajor: false);
                    Run.BroadcastMapVotes();
                }
                OnMidWavePeerDisconnect?.Invoke(peer.Name);
                GameRefs.ShowGameToast($"{peer.Name} disconnected.", 2.5f);
            };

            Net.OnJoinedHost += () =>
            {
                _hostExplicitlyClosedFlag = false;
                LobbySetup.IsLaunchingRun = false;
                Run.MenuLockDirty = true;
                Net.SendToHost(PacketType.RequestFullSync);
            };

            Net.OnDisconnectedFromSession += reason =>
            {
                bool unintendedDrop = !Net.LastDisconnectWasIntentional && !_hostExplicitlyClosedFlag;
                _hostExplicitlyClosedFlag = false;
                ResetSessionState();
                if (unintendedDrop && GameRefs.IsInTitle)
                {
                    OnHostClosedLobby?.Invoke(CoopLoc.T("lobby.host_closed_body"));
                }
            };
        }

        public void HostBroadcastLobbySetup(LobbySetupChangeMask changeMask, int targetPeerId = -1, bool resetReadyOnMajor = true)
        {
            if (!Net.IsHost) return;

            if (targetPeerId < 0)
            {
                LobbySetup.Revision++;
                if (resetReadyOnMajor && (changeMask & LobbySetupChangeMask.MajorSetup) != 0)
                {
                    if (Net.ResetAllClientsReady())
                        CoopLogger.Info(LogCategory.Sync, CoopLoc.T("lobby.ready_reset_notice"), addToFeed: true);
                }
            }

            Action<BinaryWriter> writer = w => LobbySetup.Write(w, changeMask);
            if (targetPeerId >= 0) Net.SendToPeer(targetPeerId, PacketType.LobbySetupSync, writer);
            else Net.BroadcastToClients(PacketType.LobbySetupSync, writer);

            OnSettingsChanged?.Invoke();
            OnLobbySetupChanged?.Invoke(changeMask);
        }

        public void BroadcastSessionSettings(int targetPeerId = -1)
        {
            HostBroadcastLobbySetup(LobbySetupChangeMask.SessionRules, targetPeerId, resetReadyOnMajor: false);
        }

        private void HandleLobbySetupSync(BinaryReader r)
        {
            LobbySetupChangeMask diff = LobbySetup.ReadFrom(r);
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
            OnSettingsChanged?.Invoke();
            OnLobbySetupChanged?.Invoke(LobbySetupChangeMask.SessionRules);
        }

        public void HostLaunchCoopRun()
        {
            if (!Net.IsHost || !GameRefs.IsInTitle || LobbySetup.IsLaunchingRun) return;

            int seed = LobbySetup.MasterSeed != 0
                ? LobbySetup.MasterSeed
                : Math.Abs(Environment.TickCount ^ UnityEngine.Random.Range(10000, 9999999));
            if (seed == 0) seed = 424242;

            LobbySetup.IsLaunchingRun = true;

            HostBroadcastLobbySetup(LobbySetupChangeMask.LaunchState, targetPeerId: -1, resetReadyOnMajor: false);
            Net.BroadcastToClients(PacketType.EvtStartCoopRun, w =>
            {
                w.Write(seed);
                w.Write((int)LobbySetup.SelectedWorld);
                w.Write((int)LobbySetup.DifficultyTier);
            });

            OnRunLaunchStarted?.Invoke();

            try
            {
                var gdm = GameDataManager.instance;
                if (gdm != null)
                {
                    gdm.StartNewGame(seed, LobbySetup.SelectedWorld, LobbySetup.DifficultyTier);
                    CoopLogger.Info(LogCategory.Sync, $"Launching Co-op Run: {WorldCatalog.GetWorldDisplayName(LobbySetup.SelectedWorld)} ({LobbySetup.DifficultyTier}, Seed {seed})", addToFeed: true);
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

        private void HandleEvtStartCoopRun(BinaryReader r)
        {
            int seed = r.ReadInt32();
            var world = (eWorldType)r.ReadInt32();
            var diff = (eGameDifficultyType)r.ReadInt32();

            LobbySetup.SelectedWorld = world;
            LobbySetup.DifficultyTier = diff;
            LobbySetup.IsLaunchingRun = true;
            Run.PendingStageSeed = seed;

            CoopLogger.Info(LogCategory.Sync, CoopLoc.T("lobby.launching_run"), addToFeed: true);
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
                CoopLogger.Info(LogCategory.Sync, "Sent full state snapshot to all peers.", addToFeed: true);
            }
            else if (Net.IsClient)
            {
                Net.SendToHost(PacketType.RequestFullSync);
                CoopLogger.Info(LogCategory.Sync, "Requested full state sync from Host.", addToFeed: true);
            }
        }

        public void Tick()
        {
            bool sceneChanged = GameRefs.RefreshSceneCache();
            string activeScene = GameRefs.ActiveScene;

            if (sceneChanged)
            {
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
                }
                else if (GameRefs.IsInBattle)
                {
                    Economy.ResetBattleCache();
                    Waves.ResetBattleState();
                    Quests.Reset();
                }
                if (Net.IsHost)
                    Net.UpdateLobbyMetadata(activeScene);
            }

            Net.Poll();

            float now = Time.unscaledTime;
            float dt = Time.unscaledDeltaTime;
            Run.Tick(activeScene, now);

            if (!Net.IsConnected) return;

            Economy.Tick(now);
            Waves.TickHostPauseState();
            Modals.Tick(now);
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
            CoopLogger.PushFeed($"{sender}: {clean}");
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
            CoopLogger.PushFeed($"{sender}: {text}");
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

                    case PacketType.CmdVoteMapNode: if (Net.IsHost) Run.HandleCmdVoteMapNode(pkt.SenderPlayerId, r); break;
                    case PacketType.EvtMapNodeVotes: if (Net.IsClient) Run.HandleEvtMapNodeVotes(r); break;
                    case PacketType.CmdSelectMapNode: if (Net.IsHost) Run.HandleCmdSelectMapNode(pkt.SenderPlayerId, r); break;
                    case PacketType.EvtModalState: if (Net.IsClient) Modals.HandleEvtModalState(r); break;
                    case PacketType.EvtModalClosed: if (Net.IsClient) Modals.HandleEvtModalClosed(); break;
                    case PacketType.EvtVictoryOrDefeat: if (Net.IsClient) Modals.HandleEvtVictoryOrDefeat(r); break;

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
