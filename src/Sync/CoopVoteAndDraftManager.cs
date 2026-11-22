using System;
using System.Collections.Generic;
using System.IO;
using EmberwardCoop.Core;
using EmberwardCoop.Localization;
using EmberwardCoop.Networking;
using EmberwardCoop.Rules;
using UnityEngine;

namespace EmberwardCoop.Sync
{
    public enum SharedVanillaSyncAction : byte
    {
        AcademySelectCardSet = 1,
        AcademySelectRelic = 2,
        AcademyRerollAll = 3,
        AcademyRerollCardSet = 4,
        RewardReroll = 5,
        TetrisDraftToggleCard = 6,
        TetrisDraftConfirm = 7,
        TetrisDraftReset = 8,
        EnigmaIntroClose = 9,
        PopupCloseFinished = 10
    }

    public sealed class SharedDraftOption
    {
        public int Index;
        public eItemType ItemType;
        public eCardType CardType;
        public string DisplayName = "";
        public string Subtitle = "";

        public void Write(BinaryWriter w)
        {
            w.Write(Index);
            w.Write((int)ItemType);
            w.Write((int)CardType);
            w.WriteSafeString(DisplayName);
            w.WriteSafeString(Subtitle);
        }

        public static SharedDraftOption Read(BinaryReader r) => new SharedDraftOption
        {
            Index = r.ReadInt32(),
            ItemType = (eItemType)r.ReadInt32(),
            CardType = (eCardType)r.ReadInt32(),
            DisplayName = r.ReadString(),
            Subtitle = r.ReadString()
        };
    }

    public sealed class SharedDraftSession
    {
        public int DraftId;
        public CoopModalKind Kind;
        public string Title = "";
        public int RerollsLeft;
        public bool CanSkip;
        public readonly List<SharedDraftOption> Options = new List<SharedDraftOption>();
        public readonly Dictionary<int, int> Votes = new Dictionary<int, int>(); // playerId -> optionIdx (-1 = skip)
    }

    public sealed class CoopVoteAndDraftManager
    {
        private readonly NetworkManager _net;
        private readonly Func<SessionController> _session;

        // Map node voting: playerId -> MapNodeData.Index
        public readonly Dictionary<int, int> MapNodeVotes = new Dictionary<int, int>();
        public event Action OnMapVotesChanged;

        // Shared draft voting session (active in Shared Resources mode on vanilla popups)
        public SharedDraftSession ActiveSharedDraft { get; private set; }
        public event Action OnSharedDraftChanged;

        // Separate/Individual mode drafting barrier: playerId -> (isBusy, modalBadge)
        public readonly Dictionary<int, (bool busy, string modal)> PeerDraftBarrier = new Dictionary<int, (bool, string)>();
        public event Action OnDraftBarrierChanged;
        public bool HostForceContinueRequested { get; private set; }
        public string BarrierWaitNotice { get; private set; } = "";

        private int _nextDraftId = 1;
        private int _localDraftCounter;
        private APopupWindow _hostActiveDraftPopup;
        private int _lastCapturedOptionsHash;
        private float _lastDraftPollTime;
        private float _lastBarrierPollTime;
        private bool _lastLocalBusy;
        private string _lastLocalBadge = "";
        private float _resolveCooldownUntil;
        private bool _hostInVictorySequence;
        private bool _clientInVictoryUpgradeSequence;
        private float _clientVictoryStartTime;

        public CoopVoteAndDraftManager(NetworkManager net, Func<SessionController> session)
        {
            _net = net;
            _session = session;
        }

        public void Reset()
        {
            MapNodeVotes.Clear();
            ActiveSharedDraft = null;
            _hostActiveDraftPopup = null;
            _lastCapturedOptionsHash = 0;
            _localDraftCounter = 0;
            PeerDraftBarrier.Clear();
            HostForceContinueRequested = false;
            BarrierWaitNotice = "";
            _lastLocalBusy = false;
            _lastLocalBadge = "";
            _hostInVictorySequence = false;
            _clientInVictoryUpgradeSequence = false;
            _clientVictoryStartTime = 0f;
            OnMapVotesChanged?.Invoke();
            OnSharedDraftChanged?.Invoke();
            OnDraftBarrierChanged?.Invoke();
        }

        public void ClearVictoryUpgradeState()
        {
            bool changed = _hostInVictorySequence || _clientInVictoryUpgradeSequence || HostForceContinueRequested;
            _hostInVictorySequence = false;
            _clientInVictoryUpgradeSequence = false;
            _clientVictoryStartTime = 0f;
            HostForceContinueRequested = false;
            BarrierWaitNotice = "";
            if (changed)
            {
                if (_net.IsHost)
                {
                    PeerDraftBarrier.Clear();
                    foreach (var kv in _net.Roster)
                        kv.Value.IsDraftingBusy = false;
                    BroadcastDraftBarrierState();
                }
                else
                {
                    _lastLocalBusy = false;
                    _lastLocalBadge = "";
                }
                OnDraftBarrierChanged?.Invoke();
            }
        }

        public void OnHostVictorySequenceStarted()
        {
            if (!_net.IsHost || _hostInVictorySequence) return;
            _hostInVictorySequence = true;
            HostForceContinueRequested = false;

            // Pre-mark remote peers busy on victory start so Host cannot leave before Clients open reward popups.
            bool anyRemote = false;
            foreach (var kv in _net.Roster)
            {
                if (kv.Key == 0) continue;
                anyRemote = true;
                PeerDraftBarrier[kv.Key] = (true, "Upgrades");
                kv.Value.IsDraftingBusy = true;
                kv.Value.ActiveModal = "Upgrades";
            }

            if (anyRemote)
            {
                _net.BroadcastRoster();
                BroadcastDraftBarrierState();
            }
        }

        public void OnClientVictorySequenceStarted()
        {
            if (!_net.IsClient || _clientInVictoryUpgradeSequence) return;
            _clientInVictoryUpgradeSequence = true;
            _clientVictoryStartTime = Time.unscaledTime;
            _lastLocalBusy = true;
            _lastLocalBadge = "Upgrades";
            _net.SendToHost(PacketType.CmdDraftBarrierState, w =>
            {
                w.Write(true);
                w.WriteSafeString("Upgrades");
            });
        }

        public void ClearMapVotes()
        {
            if (MapNodeVotes.Count == 0) return;
            MapNodeVotes.Clear();
            if (_net.IsHost) BroadcastMapVotes();
            else OnMapVotesChanged?.Invoke();
        }

        public int GetNextDeterministicDraftSeed(int stageSeed, int localPlayerId)
        {
            _localDraftCounter++;
            int baseSeed = stageSeed != 0 ? stageSeed : 424242;
            int salt = _session()?.Ruleset.ComputeDraftSeedSalt(localPlayerId) ?? 0;
            return unchecked(baseSeed ^ salt ^ (_localDraftCounter * 19349663));
        }

        public void Tick(float now)
        {
            if (!_net.IsConnected) return;

            if (now - _lastBarrierPollTime >= 0.25f)
            {
                _lastBarrierPollTime = now;
                PollLocalDraftBarrierState();
            }

            if (_net.IsHost && now - _lastDraftPollTime >= 0.25f)
            {
                _lastDraftPollTime = now;
                PollHostSharedDraftPopup();
            }
        }

        public void BroadcastHomeHeroPreview(eCharacterType charType, eEmberType emberType)
        {
            if (!_net.IsHost || _net.RemotePeerCount == 0) return;
            _net.BroadcastToClients(PacketType.EvtHomeHeroPreview, w =>
            {
                w.Write((int)charType);
                w.Write((int)emberType);
            });
        }

        public void BroadcastHomeHeroSelected(eCharacterType charType, eEmberType emberType)
        {
            if (!_net.IsHost) return;
            _net.BroadcastToClients(PacketType.EvtHomeHeroSelected, w =>
            {
                w.Write((int)charType);
                w.Write((int)emberType);
            });
            _session()?.Run.MarkDirty();
        }

        public void HandleEvtHomeHeroPreview(BinaryReader r)
        {
            var charType = (eCharacterType)r.ReadInt32();
            var emberType = (eEmberType)r.ReadInt32();
            if (!_net.IsClient) return;

            using (NetworkScope.Enter())
            {
                try
                {
                    var popups = UnityEngine.Object.FindObjectsByType<UI_SelectCharacterPopup>(FindObjectsInactive.Exclude, FindObjectsSortMode.None);
                    if (popups != null)
                    {
                        for (int i = 0; i < popups.Length; i++)
                        {
                            var p = popups[i];
                            if (p != null && p.gameObject.activeInHierarchy && !p.IsWindowClosed)
                            {
                                p.OnCharacterSelected(charType);
                                p.UpdateEmberInfo(emberType);
                            }
                        }
                    }

                    var igPopups = UnityEngine.Object.FindObjectsByType<UI_InGameSelectCharacterPopup>(FindObjectsInactive.Exclude, FindObjectsSortMode.None);
                    if (igPopups != null)
                    {
                        for (int i = 0; i < igPopups.Length; i++)
                        {
                            var p = igPopups[i];
                            if (p != null && p.gameObject.activeInHierarchy && !p.IsWindowClosed)
                                p.OnCharacterSelected(charType, false);
                        }
                    }
                }
                catch { }
            }
        }

        public void HandleEvtHomeHeroSelected(BinaryReader r)
        {
            var charType = (eCharacterType)r.ReadInt32();
            var emberType = (eEmberType)r.ReadInt32();
            if (!_net.IsClient) return;

            using (NetworkScope.Enter())
            {
                var gd = GameDataManager.instance?.GameplayData;
                if (gd != null)
                {
                    gd.characterType = charType;
                    gd.emberType = emberType;
                }

                try
                {
                    var popups = UnityEngine.Object.FindObjectsByType<UI_SelectCharacterPopup>(FindObjectsInactive.Exclude, FindObjectsSortMode.None);
                    if (popups != null)
                    {
                        for (int i = 0; i < popups.Length; i++)
                        {
                            var p = popups[i];
                            if (p != null && p.gameObject.activeInHierarchy && !p.IsWindowClosed)
                            {
                                p.OnCharacterSelected(charType);
                                p.UpdateEmberInfo(emberType);
                                p.OnClickOK();
                            }
                        }
                    }

                    var igPopups = UnityEngine.Object.FindObjectsByType<UI_InGameSelectCharacterPopup>(FindObjectsInactive.Exclude, FindObjectsSortMode.None);
                    if (igPopups != null)
                    {
                        for (int i = 0; i < igPopups.Length; i++)
                        {
                            var p = igPopups[i];
                            if (p != null && p.gameObject.activeInHierarchy && !p.IsWindowClosed)
                            {
                                p.OnCharacterSelected(charType, false);
                                p.OnClickConfirmButton();
                            }
                        }
                    }
                }
                catch (Exception ex)
                {
                    CoopLogger.Warn(LogCategory.Modal, $"HandleEvtHomeHeroSelected warning: {ex.Message}");
                }
            }
        }

        public void BroadcastSharedVanillaDraftSync(SharedVanillaSyncAction action, int arg1 = 0, int arg2 = 0)
        {
            if (!_net.IsHost || _net.RemotePeerCount == 0) return;
            var s = _session();
            if (s != null && s.Ruleset.AllowsClientIndependentDraftsAndShops && action != SharedVanillaSyncAction.EnigmaIntroClose)
                return;

            _net.BroadcastToClients(PacketType.EvtSharedVanillaDraftSync, w =>
            {
                w.Write((byte)action);
                w.Write(arg1);
                w.Write(arg2);
            });
        }

        public void HandleEvtSharedVanillaDraftSync(BinaryReader r)
        {
            var action = (SharedVanillaSyncAction)r.ReadByte();
            int arg1 = r.ReadInt32();
            int arg2 = r.ReadInt32();
            if (!_net.IsClient) return;

            var s = _session();
            if (s != null && s.Ruleset.AllowsClientIndependentDraftsAndShops && action != SharedVanillaSyncAction.EnigmaIntroClose)
                return;

            using (NetworkScope.Enter())
            {
                try
                {
                    switch (action)
                    {
                        case SharedVanillaSyncAction.AcademySelectCardSet:
                        {
                            var acad = FindOpenPopup<UI_MapScene_Academy_Popup>();
                            if (acad != null && !acad.isCardSetSelected)
                                acad.OnCardSetClickedCallback(arg1);
                            break;
                        }
                        case SharedVanillaSyncAction.AcademySelectRelic:
                        {
                            var acad = FindOpenPopup<UI_MapScene_Academy_Popup>();
                            if (acad != null && !acad.isRelicSelected && acad.list_CreatedRelicCards != null && arg1 >= 0 && arg1 < acad.list_CreatedRelicCards.Count)
                                acad.OnRelicCardClickedCallback(acad.list_CreatedRelicCards[arg1]);
                            break;
                        }
                        case SharedVanillaSyncAction.AcademyRerollAll:
                        {
                            var acad = FindOpenPopup<UI_MapScene_Academy_Popup>();
                            if (acad != null)
                            {
                                UnityEngine.Random.InitState(arg1);
                                acad.OnRerollButtonClickedCallback();
                            }
                            break;
                        }
                        case SharedVanillaSyncAction.AcademyRerollCardSet:
                        {
                            var acad = FindOpenPopup<UI_MapScene_Academy_Popup>();
                            if (acad != null)
                            {
                                UnityEngine.Random.InitState(arg2);
                                acad.OnRequestAcademyCardSetReroll(arg1);
                            }
                            break;
                        }
                        case SharedVanillaSyncAction.RewardReroll:
                        {
                            var rew = FindOpenPopup<UI_ChooseRewardCardPopup>();
                            if (rew != null)
                            {
                                UnityEngine.Random.InitState(arg1);
                                rew.OnClickButton_Reroll();
                            }
                            break;
                        }
                        case SharedVanillaSyncAction.TetrisDraftToggleCard:
                        {
                            var td = FindOpenPopup<UI_TetrisDraft_Popup>();
                            if (td != null && td.list_TetrisCandidateCards != null && arg1 >= 0 && arg1 < td.list_TetrisCandidateCards.Count)
                                td.OnCardClicked(td.list_TetrisCandidateCards[arg1]);
                            break;
                        }
                        case SharedVanillaSyncAction.TetrisDraftConfirm:
                        {
                            var td = FindOpenPopup<UI_TetrisDraft_Popup>();
                            if (td != null) td.OnClickButton_OK();
                            break;
                        }
                        case SharedVanillaSyncAction.TetrisDraftReset:
                        {
                            var td = FindOpenPopup<UI_TetrisDraft_Popup>();
                            if (td != null) td.OnClickButton_Reset();
                            break;
                        }
                        case SharedVanillaSyncAction.EnigmaIntroClose:
                        {
                            var intro = FindOpenPopup<UI_EnigmaSanctumIntro_Popup>();
                            if (intro != null) intro.OnCloseButtonClicked();
                            break;
                        }
                        case SharedVanillaSyncAction.PopupCloseFinished:
                        {
                            CloseTopClientSharedPopup();
                            break;
                        }
                    }
                }
                catch (Exception ex)
                {
                    CoopLogger.Debug(LogCategory.Modal, $"HandleEvtSharedVanillaDraftSync({action}) warning: {ex.Message}");
                }
            }
        }

        private static T FindOpenPopup<T>() where T : APopupWindow
        {
            try
            {
                var all = UnityEngine.Object.FindObjectsByType<T>(FindObjectsInactive.Exclude, FindObjectsSortMode.None);
                if (all != null)
                {
                    for (int i = 0; i < all.Length; i++)
                        if (all[i] != null && all[i].gameObject.activeInHierarchy && !all[i].IsWindowClosed)
                            return all[i];
                }
            }
            catch { }
            return null;
        }

        private static void CloseTopClientSharedPopup()
        {
            try
            {
                var uiMgr = UIManager.Instance;
                if (uiMgr == null || !uiMgr.HasAnyPopupWindow()) return;
                var top = uiMgr.GetTopPopupWindow();
                if (top == null) return;
                string name = top.GetIl2CppType()?.Name ?? "";
                if (name.Contains("Setting") || name.Contains("Pause") || name.Contains("DeckList") ||
                    name.Contains("Victory") || name.Contains("Defeat") || name.Contains("SelectCharacter"))
                    return;

                var popup = top.TryCast<APopupWindow>();
                if (popup != null && !popup.IsWindowClosed)
                {
                    popup.OverrideIsWindowFinished(true);
                    popup.CloseWindow();
                }
            }
            catch { }
        }

        public void CastMapNodeVote(int nodeIndex)
        {
            if (!_net.IsConnected || nodeIndex < 0) return;

            if (_net.IsClient)
            {
                _net.SendToHost(PacketType.CmdVoteMapNode, w => w.Write(nodeIndex));
                return;
            }

            if (_net.RemotePeerCount == 0)
            {
                ExecuteHostMapNodeSelection(nodeIndex);
                return;
            }

            if (AnyRemotePeerDrafting(out string busyNames) && !HostForceContinueRequested)
            {
                BarrierWaitNotice = CoopLoc.F("draft.waiting_peers", busyNames);
                GameRefs.ShowGameToast(BarrierWaitNotice, 2.5f);
                OnDraftBarrierChanged?.Invoke();
                return;
            }

            MapNodeVotes[0] = nodeIndex;
            BroadcastMapVotes();
            TryAutoResolveMapVote();
        }

        public void HandleCmdVoteMapNode(int senderId, BinaryReader r)
        {
            int nodeIdx = r.ReadInt32();
            if (!_net.IsHost || nodeIdx < 0) return;
            if (!IsMapNodeAvailable(nodeIdx)) return;

            MapNodeVotes[senderId] = nodeIdx;
            string pName = _net.GetPlayerName(senderId);
            CoopLogger.Info(LogCategory.Sync, $"{pName} voted for Map Node #{nodeIdx}.");
            BroadcastMapVotes();
            TryAutoResolveMapVote();
        }

        public void HostForceResolveMapVote()
        {
            if (!_net.IsHost) return;
            if (MapNodeVotes.Count == 0) return;

            if (AnyRemotePeerDrafting(out string busyNames) && !HostForceContinueRequested)
            {
                BarrierWaitNotice = CoopLoc.F("draft.waiting_peers", busyNames);
                GameRefs.ShowGameToast(BarrierWaitNotice, 2.5f);
                OnDraftBarrierChanged?.Invoke();
                return;
            }

            int winner = ComputeWinningMapNode();
            if (winner >= 0)
                ResolveWinningMapNode(winner);
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
            int count = r.ReadBoundedCount(32);
            MapNodeVotes.Clear();
            for (int i = 0; i < count; i++)
            {
                int pid = r.ReadInt32();
                int nodeIdx = r.ReadInt32();
                MapNodeVotes[pid] = nodeIdx;
            }
            OnMapVotesChanged?.Invoke();
        }

        public void HandleEvtMapNodeResolved(BinaryReader r)
        {
            int nodeIdx = r.ReadInt32();
            var stageType = (eStageType)r.ReadInt32();
            MapNodeVotes.Clear();
            OnMapVotesChanged?.Invoke();
            CoopLogger.Info(LogCategory.Sync, $"Map vote resolved -> Node #{nodeIdx} ({stageType}).");
        }

        public void OnPeerDisconnected(int playerId)
        {
            MapNodeVotes.Remove(playerId);
            PeerDraftBarrier.Remove(playerId);
            ActiveSharedDraft?.Votes.Remove(playerId);
            if (_net.IsHost)
            {
                BroadcastMapVotes();
                BroadcastDraftBarrierState();
                if (ActiveSharedDraft != null) BroadcastSharedDraftState();
                TryAutoResolveMapVote();
            }
            OnDraftBarrierChanged?.Invoke();
        }

        private void TryAutoResolveMapVote()
        {
            if (!_net.IsHost || Time.unscaledTime < _resolveCooldownUntil) return;
            if (_net.Roster.Count == 0) return;

            int votedPeers = 0;
            foreach (var kv in _net.Roster)
            {
                if (MapNodeVotes.ContainsKey(kv.Key)) votedPeers++;
            }

            if (votedPeers >= _net.Roster.Count)
            {
                if (AnyRemotePeerDrafting(out string busyNames) && !HostForceContinueRequested)
                {
                    BarrierWaitNotice = CoopLoc.F("draft.waiting_peers", busyNames);
                    OnDraftBarrierChanged?.Invoke();
                    return;
                }

                int winner = ComputeWinningMapNode();
                if (winner >= 0)
                    ResolveWinningMapNode(winner);
            }
        }

        private int ComputeWinningMapNode()
        {
            var tallies = new Dictionary<int, int>();
            int maxVotes = 0;
            foreach (var kv in MapNodeVotes)
            {
                if (!IsMapNodeAvailable(kv.Value)) continue;
                int c = tallies.TryGetValue(kv.Value, out int prev) ? prev + 1 : 1;
                tallies[kv.Value] = c;
                if (c > maxVotes) maxVotes = c;
            }
            if (maxVotes == 0) return -1;

            var tied = new List<int>();
            foreach (var kv in tallies)
                if (kv.Value == maxVotes) tied.Add(kv.Key);
            tied.Sort();

            if (tied.Count == 1) return tied[0];

            if (MapNodeVotes.TryGetValue(0, out int hostVote) && tied.Contains(hostVote))
                return hostVote;

            var gd = GameDataManager.instance?.GameplayData;
            int seed = (gd != null ? gd.randomSeed ^ (gd.curMapStep * 7919) : 1337) & 0x7FFFFFFF;
            return tied[seed % tied.Count];
        }

        private void ResolveWinningMapNode(int nodeIndex)
        {
            _resolveCooldownUntil = Time.unscaledTime + 1.0f;
            HostForceContinueRequested = false;
            BarrierWaitNotice = "";

            var msp = GameRefs.GetMapSceneProcess();
            var nodes = msp?.mapManager?.list_MapNodes;
            MapNode targetNode = (nodes != null && nodeIndex >= 0 && nodeIndex < nodes.Count) ? nodes[nodeIndex] : null;
            eStageType stageType = targetNode?.mapNodeData != null ? targetNode.mapNodeData.MapNodeType : eStageType.NONE;

            MapNodeVotes.Clear();
            BroadcastMapVotes();
            _net.BroadcastToClients(PacketType.EvtMapNodeResolved, w =>
            {
                w.Write(nodeIndex);
                w.Write((int)stageType);
            });

            ExecuteHostMapNodeSelection(nodeIndex);
        }

        private void ExecuteHostMapNodeSelection(int nodeIndex)
        {
            var s = _session();
            var msp = GameRefs.GetMapSceneProcess();
            var nodes = msp?.mapManager?.list_MapNodes;
            if (s == null || msp == null || nodes == null || nodeIndex < 0 || nodeIndex >= nodes.Count) return;

            var targetNode = nodes[nodeIndex];
            if (targetNode?.mapNodeData == null) return;

            s.Run.HostSelectedMapNodeIndex = nodeIndex;
            s.Run.BroadcastRunSnapshot(-1, "MapScene");

            eStageType st = targetNode.mapNodeData.MapNodeType;
            bool isCombat = IsCombatStage(st);

            int nodeSeed = targetNode.mapNodeData.randomSeed != 0
                ? targetNode.mapNodeData.randomSeed
                : unchecked((GameDataManager.instance?.GameplayData?.randomSeed ?? 1337) ^ (nodeIndex * 7919));

            // Open vanilla non-combat MapNode modals on Clients in BOTH Shared and Separate modes.
            if (!isCombat && _net.RemotePeerCount > 0)
            {
                _net.BroadcastToClients(PacketType.EvtOpenPeerNodeModal, w =>
                {
                    w.Write(nodeIndex);
                    w.Write((int)st);
                    w.Write(nodeSeed);
                });
            }

            if (!isCombat)
            {
                int hostSeed = GetNextDeterministicDraftSeed(nodeSeed, 0);
                UnityEngine.Random.InitState(hostSeed);
            }

            using (NetworkScope.Enter())
            {
                msp.OnClickMapNodeButton(targetNode);
            }
        }

        public void HandleEvtOpenPeerNodeModal(BinaryReader r)
        {
            int nodeIndex = r.ReadInt32();
            var stageType = (eStageType)r.ReadInt32();
            int nodeSeed = r.ReadInt32();

            var s = _session();
            if (!_net.IsClient || s == null) return;
            if (!GameRefs.IsInMap) return;

            try
            {
                var msp = GameRefs.GetMapSceneProcess();
                var nodes = msp?.mapManager?.list_MapNodes;
                if (msp == null || nodes == null || nodeIndex < 0 || nodeIndex >= nodes.Count) return;

                var node = nodes[nodeIndex];
                if (node == null) return;

                // In Shared mode, salt=0 produces the exact same seed as Host; in Separate mode, salt=LocalPlayerId*73856093 rolls unique options.
                int peerSeed = GetNextDeterministicDraftSeed(nodeSeed, _net.LocalPlayerId);
                UnityEngine.Random.InitState(peerSeed);

                using (NetworkScope.Enter())
                {
                    msp.currentMapNode = node;
                    msp.SetSelectedMapNode(node);

                    switch (stageType)
                    {
                        case eStageType.SHOP:
                            msp.StartCoroutine(msp.CR_ShopNodeProc());
                            break;
                        case eStageType.WORKSHOP:
                            msp.StartCoroutine(msp.CR_WorkshopNodeProc());
                            break;
                        case eStageType.CAMPSITE:
                        case eStageType.RECOVER:
                            msp.StartCoroutine(msp.CR_CampsiteNodeProc());
                            break;
                        case eStageType.ALTAR:
                            msp.StartCoroutine(msp.CR_AltarNodeProc());
                            break;
                        case eStageType.SECRET_VAULT:
                            msp.StartCoroutine(msp.CR_SecretVaultNodeProc());
                            break;
                        case eStageType.TREASURE:
                            msp.StartCoroutine(msp.CR_TreasureNodeProc());
                            break;
                        case eStageType.QUEST:
                            msp.StartCoroutine(msp.CR_QuestNodeProc());
                            break;
                        case eStageType.SPECIAL_EVENT:
                            msp.StartCoroutine(msp.CR_EventNodeProc());
                            break;
                        case eStageType.ACADEMY:
                            msp.StartCoroutine(msp.CR_AcademyNodeProc());
                            break;
                        case eStageType.ROYAL_LIBRARY:
                            msp.StartCoroutine(msp.CR_RoyalLibraryNodeProc());
                            break;
                    }
                }

                CoopLogger.Info(LogCategory.Modal, $"Opened client vanilla node modal: {stageType} (Node #{nodeIndex}, Seed {peerSeed}).");
            }
            catch (Exception ex)
            {
                CoopLogger.Warn(LogCategory.Modal, $"HandleEvtOpenPeerNodeModal({stageType}) warning: {ex.Message}");
            }
        }

        public static bool IsCombatStage(eStageType st) =>
            st == eStageType.BATTLE ||
            st == eStageType.BATTLE_CORRUPTED ||
            st == eStageType.BATTLE_ANOMALY ||
            st == eStageType.BATTLE_DARKNESS ||
            st == eStageType.BOSS ||
            st == eStageType.MID_BOSS;

        private static bool IsMapNodeAvailable(int nodeIndex)
        {
            var msp = GameRefs.GetMapSceneProcess();
            var nodes = msp?.mapManager?.list_MapNodes;
            if (nodes == null || nodeIndex < 0 || nodeIndex >= nodes.Count) return false;
            var nd = nodes[nodeIndex]?.mapNodeData;
            return nd != null && nd.State == eMapNodeState.AVALIABLE;
        }

        private void PollHostSharedDraftPopup()
        {
            var s = _session();
            if (s == null || s.Ruleset.AllowsClientIndependentDraftsAndShops || _net.RemotePeerCount == 0)
            {
                if (ActiveSharedDraft != null) CloseSharedDraft(broadcast: true);
                return;
            }

            APopupWindow topPopup = null;
            try
            {
                var uiMgr = UIManager.Instance;
                if (uiMgr != null && uiMgr.HasAnyPopupWindow())
                    topPopup = uiMgr.GetTopPopupWindow()?.TryCast<APopupWindow>();
            }
            catch { }

            if (topPopup == null || topPopup.IsWindowClosed)
            {
                if (ActiveSharedDraft != null)
                    CloseSharedDraft(broadcast: true);
                return;
            }

            if (TryExtractSharedDraftOptions(topPopup, out CoopModalKind kind, out string title, out int rerolls, out bool canSkip, out var options) && options.Count > 0)
            {
                int hash = ComputeOptionsHash(kind, options, rerolls);
                if (_hostActiveDraftPopup != topPopup || ActiveSharedDraft == null || hash != _lastCapturedOptionsHash)
                {
                    _hostActiveDraftPopup = topPopup;
                    _lastCapturedOptionsHash = hash;
                    ActiveSharedDraft = new SharedDraftSession
                    {
                        DraftId = _nextDraftId++,
                        Kind = kind,
                        Title = title,
                        RerollsLeft = rerolls,
                        CanSkip = canSkip
                    };
                    ActiveSharedDraft.Options.AddRange(options);
                    BroadcastSharedDraftState();
                }
            }
            else if (ActiveSharedDraft != null && _hostActiveDraftPopup != topPopup)
            {
                CloseSharedDraft(broadcast: true);
            }
        }

        public void CloseSharedDraft(bool broadcast)
        {
            if (ActiveSharedDraft == null && _hostActiveDraftPopup == null) return;
            ActiveSharedDraft = null;
            _hostActiveDraftPopup = null;
            _lastCapturedOptionsHash = 0;
            if (broadcast && _net.IsHost)
                _net.BroadcastToClients(PacketType.EvtSharedDraftClosed);
            OnSharedDraftChanged?.Invoke();
        }

        public void BroadcastSharedDraftState()
        {
            if (!_net.IsHost || ActiveSharedDraft == null) return;
            var d = ActiveSharedDraft;
            _net.BroadcastToClients(PacketType.EvtSharedDraftState, w =>
            {
                w.Write(d.DraftId);
                w.Write((byte)d.Kind);
                w.WriteSafeString(d.Title);
                w.Write(d.RerollsLeft);
                w.Write(d.CanSkip);
                w.Write(d.Options.Count);
                for (int i = 0; i < d.Options.Count; i++) d.Options[i].Write(w);
                w.Write(d.Votes.Count);
                foreach (var kv in d.Votes)
                {
                    w.Write(kv.Key);
                    w.Write(kv.Value);
                }
            });
            OnSharedDraftChanged?.Invoke();
        }

        public void HandleEvtSharedDraftState(BinaryReader r)
        {
            var d = new SharedDraftSession
            {
                DraftId = r.ReadInt32(),
                Kind = (CoopModalKind)r.ReadByte(),
                Title = r.ReadString(),
                RerollsLeft = r.ReadInt32(),
                CanSkip = r.ReadBoolean()
            };
            int optCount = r.ReadBoundedCount(24);
            for (int i = 0; i < optCount; i++) d.Options.Add(SharedDraftOption.Read(r));
            int vCount = r.ReadBoundedCount(16);
            for (int i = 0; i < vCount; i++)
            {
                int pid = r.ReadInt32();
                int opt = r.ReadInt32();
                d.Votes[pid] = opt;
            }
            ActiveSharedDraft = d;
            OnSharedDraftChanged?.Invoke();
        }

        public void HandleEvtSharedDraftClosed()
        {
            ActiveSharedDraft = null;
            OnSharedDraftChanged?.Invoke();
        }

        public void VoteSharedDraftOption(int optionIndex)
        {
            if (!_net.IsConnected) return;
            if (ActiveSharedDraft == null)
            {
                if (_net.IsClient)
                {
                    _net.SendToHost(PacketType.CmdDraftVote, w =>
                    {
                        w.Write(-1);
                        w.Write(optionIndex);
                    });
                }
                return;
            }

            int draftId = ActiveSharedDraft.DraftId;

            if (_net.IsClient)
            {
                ActiveSharedDraft.Votes[_net.LocalPlayerId] = optionIndex;
                OnSharedDraftChanged?.Invoke();
                _net.SendToHost(PacketType.CmdDraftVote, w =>
                {
                    w.Write(draftId);
                    w.Write(optionIndex);
                });
                return;
            }

            ActiveSharedDraft.Votes[0] = optionIndex;
            BroadcastSharedDraftState();
            TryResolveHostSharedDraft(forcePick: true);
        }

        public void RequestSharedDraftReroll()
        {
            if (!_net.IsConnected) return;
            if (_net.IsClient)
            {
                int draftId = ActiveSharedDraft?.DraftId ?? -1;
                _net.SendToHost(PacketType.CmdDraftReroll, w => w.Write(draftId));
                return;
            }
            ExecuteHostSharedDraftReroll();
        }

        public void HandleCmdDraftVote(int senderId, BinaryReader r)
        {
            int draftId = r.ReadInt32();
            int optionIndex = r.ReadInt32();
            if (!_net.IsHost) return;
            if (ActiveSharedDraft == null)
            {
                PollHostSharedDraftPopup();
                if (ActiveSharedDraft == null) return;
            }
            if (draftId >= 0 && ActiveSharedDraft.DraftId != draftId) return;

            ActiveSharedDraft.Votes[senderId] = optionIndex;
            string pName = _net.GetPlayerName(senderId);
            string optName = optionIndex >= 0 && optionIndex < ActiveSharedDraft.Options.Count
                ? ActiveSharedDraft.Options[optionIndex].DisplayName
                : $"#{optionIndex + 1}";
            GameRefs.ShowGameToast($"{pName} -> {optName}", 2.0f);
            BroadcastSharedDraftState();
            TryResolveHostSharedDraft(forcePick: false);
        }

        public void HandleCmdDraftReroll(int senderId, BinaryReader r)
        {
            int draftId = r.ReadInt32();
            if (!_net.IsHost) return;
            if (ActiveSharedDraft != null && draftId >= 0 && ActiveSharedDraft.DraftId != draftId) return;

            string pName = _net.GetPlayerName(senderId);
            CoopLogger.Info(LogCategory.Modal, $"{pName} triggered shared draft reroll.");
            GameRefs.ShowGameToast(CoopLoc.F("draft.rerolled_by", pName), 2.0f);
            ExecuteHostSharedDraftReroll();
        }

        public void HostApplyTopVotedDraftOption()
        {
            if (!_net.IsHost || ActiveSharedDraft == null) return;
            TryResolveHostSharedDraft(forcePick: true);
        }

        private void TryResolveHostSharedDraft(bool forcePick)
        {
            if (!_net.IsHost || ActiveSharedDraft == null || _hostActiveDraftPopup == null) return;
            var d = ActiveSharedDraft;

            if (!forcePick)
            {
                if (d.Votes.Count < _net.Roster.Count) return;
                int firstOpt = int.MinValue;
                foreach (var kv in d.Votes)
                {
                    if (firstOpt == int.MinValue) firstOpt = kv.Value;
                    else if (kv.Value != firstOpt) return;
                }
                if (firstOpt == int.MinValue) return;
                ApplyOptionToHostPopup(_hostActiveDraftPopup, firstOpt);
                return;
            }

            var counts = new Dictionary<int, int>();
            int bestVotes = 0;
            foreach (var kv in d.Votes)
            {
                int c = counts.TryGetValue(kv.Value, out int prev) ? prev + 1 : 1;
                counts[kv.Value] = c;
                if (c > bestVotes) bestVotes = c;
            }
            if (bestVotes == 0) return;

            int winningOpt = -1;
            if (d.Votes.TryGetValue(0, out int hostChoice) && counts.TryGetValue(hostChoice, out int hc) && hc == bestVotes)
                winningOpt = hostChoice;
            else
            {
                foreach (var kv in counts)
                    if (kv.Value == bestVotes) { winningOpt = kv.Key; break; }
            }

            ApplyOptionToHostPopup(_hostActiveDraftPopup, winningOpt);
        }

        private void ExecuteHostSharedDraftReroll()
        {
            if (_hostActiveDraftPopup == null) return;
            try
            {
                var rew = _hostActiveDraftPopup.TryCast<UI_ChooseRewardCardPopup>();
                if (rew != null && rew.button_Reroll != null && rew.button_Reroll.interactable)
                {
                    rew.OnClickButton_Reroll();
                    _lastCapturedOptionsHash = 0;
                    return;
                }

                var acad = _hostActiveDraftPopup.TryCast<UI_MapScene_Academy_Popup>();
                if (acad != null)
                {
                    acad.OnRerollButtonClickedCallback();
                    _lastCapturedOptionsHash = 0;
                    return;
                }
            }
            catch (Exception ex)
            {
                CoopLogger.Warn(LogCategory.Modal, $"Shared draft reroll error: {ex.Message}");
            }
        }

        private void ApplyOptionToHostPopup(APopupWindow popup, int optionIndex)
        {
            if (popup == null || popup.IsWindowClosed) return;
            try
            {
                var acad = popup.TryCast<UI_MapScene_Academy_Popup>();
                if (acad != null)
                {
                    if (!acad.isCardSetSelected && optionIndex >= 0)
                    {
                        acad.OnCardSetClickedCallback(optionIndex);
                        _lastCapturedOptionsHash = 0;
                    }
                    else if (acad.isCardSetSelected && !acad.isRelicSelected && acad.list_CreatedRelicCards != null && optionIndex >= 0 && optionIndex < acad.list_CreatedRelicCards.Count)
                    {
                        acad.OnRelicCardClickedCallback(acad.list_CreatedRelicCards[optionIndex]);
                        _lastCapturedOptionsHash = 0;
                    }
                    return;
                }

                var rew = popup.TryCast<UI_ChooseRewardCardPopup>();
                if (rew != null)
                {
                    if (optionIndex >= 0 && rew.list_Cards != null && optionIndex < rew.list_Cards.Count)
                    {
                        var card = rew.list_Cards[optionIndex];
                        if (card != null) rew.OnCardClickedCallback(card);
                    }
                    else if (optionIndex < 0)
                    {
                        rew.CloseWindow();
                    }
                    return;
                }

                var disc = popup.TryCast<UI_DiscoverCard_Popup>();
                if (disc != null)
                {
                    if (optionIndex >= 0 && disc.list_DiscoverRewardPacks != null && optionIndex < disc.list_DiscoverRewardPacks.Count)
                    {
                        var packUi = disc.list_DiscoverRewardPacks[optionIndex];
                        if (packUi != null) packUi.OnClickButton();
                    }
                    else if (optionIndex < 0)
                    {
                        disc.CloseWindow();
                    }
                    return;
                }

                var perk = popup.TryCast<UI_ChooseRoguelitePerk_Popup>();
                if (perk != null && optionIndex >= 0 && perk.list_PerkCards != null && optionIndex < perk.list_PerkCards.Count)
                {
                    perk.list_PerkCards[optionIndex]?.OnClickButton();
                    return;
                }

                var rune = popup.TryCast<UI_AddRuneToTetrisCard_Popup>();
                if (rune != null && optionIndex >= 0 && rune.list_RuneCards != null && optionIndex < rune.list_RuneCards.Count)
                {
                    rune.OnRuneCardClicked(rune.list_RuneCards[optionIndex]);
                    return;
                }

                var quest = popup.TryCast<UI_MapScene_Quest_Popup>();
                if (quest != null && optionIndex >= 0 && quest.list_QuestData != null && optionIndex < quest.list_QuestData.Count)
                {
                    quest.OnQuestBoardClickedCallback(optionIndex);
                    return;
                }

                var altar = popup.TryCast<UI_MapScene_Altar_V2_Popup>();
                if (altar != null)
                {
                    if (optionIndex < 0)
                    {
                        altar.button_Exit?.onClick?.Invoke();
                    }
                    else if (optionIndex >= 0 && optionIndex < 5)
                    {
                        altar.OnCardClickedCallback((eAltarEffectTypeV2)optionIndex);
                    }
                    return;
                }

                var camp = popup.TryCast<UI_MapScene_Campsite_Popup>();
                if (camp != null)
                {
                    if (optionIndex == 0) camp.OnClick_Recover();
                    else if (optionIndex == 1) camp.OnClick_Exchange();
                    else if (optionIndex == 2) camp.OnClick_Circus();
                    else camp.OnClick_Leave();
                    return;
                }
            }
            catch (Exception ex)
            {
                CoopLogger.Warn(LogCategory.Modal, $"ApplyOptionToHostPopup warning: {ex.Message}");
            }
            finally
            {
                _session()?.Run.MarkDirty();
                if (GameRefs.IsInBattle) _session()?.Economy.MarkDirty();
            }
        }

        private static bool TryExtractSharedDraftOptions(
            APopupWindow popup,
            out CoopModalKind kind,
            out string title,
            out int rerolls,
            out bool canSkip,
            out List<SharedDraftOption> options)
        {
            kind = CoopModalKind.None;
            title = "";
            rerolls = 0;
            canSkip = false;
            options = new List<SharedDraftOption>();

            try
            {
                var acad = popup.TryCast<UI_MapScene_Academy_Popup>();
                if (acad != null)
                {
                    kind = CoopModalKind.Academy;
                    title = "Academy";
                    if (!acad.isCardSetSelected && acad.list_CardSetData != null && acad.list_CardSetData.Count > 0)
                    {
                        for (int i = 0; i < acad.list_CardSetData.Count; i++)
                        {
                            var set = acad.list_CardSetData[i];
                            eItemType firstTower = (set?.list_TowerCardData != null && set.list_TowerCardData.Count > 0 && set.list_TowerCardData[0] != null)
                                ? set.list_TowerCardData[0].ItemType
                                : eItemType.NONE;
                            string label = firstTower != eItemType.NONE
                                ? $"Set #{i + 1} ({WorldCatalog.GetItemShortName(firstTower)})"
                                : $"Set #{i + 1}";
                            options.Add(new SharedDraftOption { Index = i, ItemType = firstTower, CardType = eCardType.TOWER_CARD, DisplayName = label, Subtitle = "Academy" });
                        }
                        return options.Count > 0;
                    }
                    if (acad.isCardSetSelected && !acad.isRelicSelected && acad.list_CreatedRelicCards != null && acad.list_CreatedRelicCards.Count > 0)
                    {
                        for (int i = 0; i < acad.list_CreatedRelicCards.Count; i++)
                        {
                            var rc = acad.list_CreatedRelicCards[i];
                            if (rc == null) continue;
                            options.Add(new SharedDraftOption { Index = i, ItemType = rc.ItemType, CardType = eCardType.RELIC_CARD, DisplayName = SafeShopCardName(rc, rc.ItemType), Subtitle = "Relic" });
                        }
                        return options.Count > 0;
                    }
                }

                var rew = popup.TryCast<UI_ChooseRewardCardPopup>();
                if (rew != null && !rew.isSelected && rew.list_Cards != null && rew.list_Cards.Count > 0)
                {
                    kind = CoopModalKind.RewardDraft;
                    title = CoopLoc.T("draft.title_reward");
                    rerolls = (rew.button_Reroll != null && rew.button_Reroll.gameObject.activeInHierarchy && rew.button_Reroll.interactable)
                        ? Math.Max(0, GameDataManager.instance?.GameplayData?.rerollChance ?? 0)
                        : 0;
                    canSkip = true;
                    for (int i = 0; i < rew.list_Cards.Count; i++)
                    {
                        var c = rew.list_Cards[i];
                        if (c == null || !c.gameObject.activeInHierarchy) continue;
                        var itemType = c.ItemType;
                        string name = SafeShopCardName(c, itemType);
                        string sub = c.CardType.ToString().Replace("_CARD", "");
                        options.Add(new SharedDraftOption { Index = i, ItemType = itemType, CardType = c.CardType, DisplayName = name, Subtitle = sub });
                    }
                    return options.Count > 0;
                }

                var disc = popup.TryCast<UI_DiscoverCard_Popup>();
                if (disc != null && !disc.isSelected && disc.list_DiscoverRewardPacks != null && disc.list_DiscoverRewardPacks.Count > 0)
                {
                    kind = CoopModalKind.DiscoverChest;
                    title = CoopLoc.T("draft.title_chest");
                    canSkip = true;
                    for (int i = 0; i < disc.list_DiscoverRewardPacks.Count; i++)
                    {
                        var packUi = disc.list_DiscoverRewardPacks[i];
                        if (packUi == null || !packUi.gameObject.activeInHierarchy) continue;
                        var first = packUi.rewardPackData?.list_RewardData != null && packUi.rewardPackData.list_RewardData.Count > 0
                            ? packUi.rewardPackData.list_RewardData[0]
                            : null;
                        var it = first?.rewardCardData != null ? first.rewardCardData.ItemType : eItemType.NONE;
                        string name = it != eItemType.NONE ? WorldCatalog.GetItemShortName(it) : $"Pack #{i + 1}";
                        int cnt = packUi.rewardPackData?.list_RewardData?.Count ?? 1;
                        options.Add(new SharedDraftOption { Index = i, ItemType = it, CardType = first?.rewardCardData != null ? first.rewardCardData.CardType : eCardType.NONE, DisplayName = name, Subtitle = cnt > 1 ? $"+{cnt - 1} more" : "" });
                    }
                    return options.Count > 0;
                }

                var perk = popup.TryCast<UI_ChooseRoguelitePerk_Popup>();
                if (perk != null && !perk.isCardSelected && perk.list_PerkCards != null && perk.list_PerkCards.Count > 0)
                {
                    kind = CoopModalKind.RoguelitePerk;
                    title = CoopLoc.T("draft.title_perk");
                    for (int i = 0; i < perk.list_PerkCards.Count; i++)
                    {
                        var pc = perk.list_PerkCards[i];
                        if (pc == null || !pc.gameObject.activeInHierarchy) continue;
                        string buff = pc.data_Buff != null ? pc.data_Buff.GetLocNameString(false) : $"Perk #{i + 1}";
                        string debuff = pc.data_Debuff != null ? pc.data_Debuff.GetLocNameString(false) : "";
                        options.Add(new SharedDraftOption { Index = i, ItemType = pc.extraRewardItemType, CardType = eCardType.NONE, DisplayName = buff ?? $"Perk #{i + 1}", Subtitle = debuff ?? "" });
                    }
                    return options.Count > 0;
                }

                var rune = popup.TryCast<UI_AddRuneToTetrisCard_Popup>();
                if (rune != null && rune.isShowRuneCompleted && rune.list_RuneCards != null && rune.list_RuneCards.Count > 0)
                {
                    kind = CoopModalKind.RuneForge;
                    title = CoopLoc.T("draft.title_rune");
                    for (int i = 0; i < rune.list_RuneCards.Count; i++)
                    {
                        var rc = rune.list_RuneCards[i];
                        if (rc == null || !rc.gameObject.activeInHierarchy) continue;
                        options.Add(new SharedDraftOption { Index = i, ItemType = rc.ItemType, CardType = eCardType.RUNE_CARD, DisplayName = SafeShopCardName(rc, rc.ItemType), Subtitle = "Rune" });
                    }
                    return options.Count > 0;
                }

                var quest = popup.TryCast<UI_MapScene_Quest_Popup>();
                if (quest != null && !quest.isSelected && quest.list_QuestData != null && quest.list_QuestData.Count > 0)
                {
                    kind = CoopModalKind.QuestSelect;
                    title = CoopLoc.T("draft.title_quest");
                    for (int i = 0; i < quest.list_QuestData.Count; i++)
                    {
                        var qd = quest.list_QuestData[i];
                        if (qd == null) continue;
                        string qName = qd.questType.ToString().Replace('_', ' ');
                        string sub = $"{qd.difficulty} · {qd.RewardType}";
                        options.Add(new SharedDraftOption { Index = i, ItemType = qd.ItemType, CardType = eCardType.NONE, DisplayName = qName, Subtitle = sub });
                    }
                    return options.Count > 0;
                }

                var camp = popup.TryCast<UI_MapScene_Campsite_Popup>();
                if (camp != null && !camp.isCardSelected)
                {
                    kind = CoopModalKind.Campsite;
                    title = CoopLoc.T("draft.title_campsite");
                    canSkip = true;
                    if (camp.button_Recover != null && camp.button_Recover.gameObject.activeInHierarchy && camp.button_Recover.interactable)
                        options.Add(new SharedDraftOption { Index = 0, DisplayName = CoopLoc.T("draft.camp_recover"), Subtitle = "HP" });
                    if (camp.button_Exchange != null && camp.button_Exchange.gameObject.activeInHierarchy && camp.button_Exchange.interactable)
                        options.Add(new SharedDraftOption { Index = 1, DisplayName = CoopLoc.T("draft.camp_exchange"), Subtitle = "Cards" });
                    if (camp.button_Circus != null && camp.button_Circus.gameObject.activeInHierarchy && camp.button_Circus.interactable)
                        options.Add(new SharedDraftOption { Index = 2, DisplayName = CoopLoc.T("draft.camp_circus"), Subtitle = "Event" });
                    return options.Count > 0;
                }
            }
            catch { }

            return false;
        }

        private static string SafeShopCardName(UI_Obj_ShopCard card, eItemType fallbackType)
        {
            try
            {
                string n = card.GetLocNameString();
                if (!string.IsNullOrWhiteSpace(n)) return n.Trim();
            }
            catch { }
            return WorldCatalog.GetItemShortName(fallbackType);
        }

        private static int ComputeOptionsHash(CoopModalKind kind, List<SharedDraftOption> opts, int rerolls)
        {
            unchecked
            {
                int h = (int)kind * 397 ^ rerolls;
                for (int i = 0; i < opts.Count; i++)
                {
                    h = (h * 31) ^ (int)opts[i].ItemType;
                    h = (h * 31) ^ (opts[i].DisplayName?.GetHashCode() ?? 0);
                }
                return h;
            }
        }

        private void PollLocalDraftBarrierState()
        {
            var s = _session();
            if (s == null) return;

            bool busy = false;
            string badge = "";

            if (_net.IsClient && _clientInVictoryUpgradeSequence)
            {
                if (!GameRefs.IsInBattle)
                {
                    _clientInVictoryUpgradeSequence = false;
                }
                else
                {
                    var topKind = s.Modals.DetectLocalTopModal();
                    bool blockingModalOpen = IsBlockingDraftOrShopModal(topKind);
                    bool victoryButtonsReady = false;
                    try
                    {
                        var vic = GameRefs.GetVictoryPopup();
                        if (vic != null && vic.gameObject.activeInHierarchy && vic.canvasGroup_Buttons != null)
                            victoryButtonsReady = vic.canvasGroup_Buttons.alpha >= 0.85f && vic.canvasGroup_Buttons.interactable;
                    }
                    catch { }

                    bool pastGrace = (Time.unscaledTime - _clientVictoryStartTime) >= 1.5f;
                    if (!blockingModalOpen && victoryButtonsReady && pastGrace)
                    {
                        _clientInVictoryUpgradeSequence = false;
                    }
                    else
                    {
                        busy = true;
                        badge = blockingModalOpen ? ModalAndRewardSync.GetShortBadge(topKind) : "Upgrades";
                    }
                }
            }
            else if (s.Ruleset.AllowsClientIndependentDraftsAndShops && (GameRefs.IsInMap || GameRefs.IsInBattle))
            {
                var topKind = s.Modals.DetectLocalTopModal();
                if (IsBlockingDraftOrShopModal(topKind))
                {
                    busy = true;
                    badge = ModalAndRewardSync.GetShortBadge(topKind);
                }
            }

            if (busy != _lastLocalBusy || badge != _lastLocalBadge)
            {
                _lastLocalBusy = busy;
                _lastLocalBadge = badge;

                if (_net.IsClient)
                {
                    _net.SendToHost(PacketType.CmdDraftBarrierState, w =>
                    {
                        w.Write(busy);
                        w.WriteSafeString(badge);
                    });
                }
                else if (_net.IsHost)
                {
                    PeerDraftBarrier[0] = (busy, badge);
                    if (_net.Roster.TryGetValue(0, out var hostPeer))
                    {
                        hostPeer.IsDraftingBusy = busy;
                        hostPeer.ActiveModal = badge;
                    }
                    BroadcastDraftBarrierState();
                }
            }
        }

        public static bool IsBlockingDraftOrShopModal(CoopModalKind kind) =>
            kind == CoopModalKind.TetrisDraft ||
            kind == CoopModalKind.RewardDraft ||
            kind == CoopModalKind.DiscoverChest ||
            kind == CoopModalKind.Shop ||
            kind == CoopModalKind.MiniShop ||
            kind == CoopModalKind.Workshop ||
            kind == CoopModalKind.MiniWorkshop ||
            kind == CoopModalKind.RuneForge ||
            kind == CoopModalKind.Altar ||
            kind == CoopModalKind.AltarEffectReward ||
            kind == CoopModalKind.Campsite ||
            kind == CoopModalKind.Circus ||
            kind == CoopModalKind.Academy ||
            kind == CoopModalKind.AcademyPerkReward ||
            kind == CoopModalKind.SecretVault ||
            kind == CoopModalKind.QuestSelect ||
            kind == CoopModalKind.QuestReward ||
            kind == CoopModalKind.RoguelitePerk ||
            kind == CoopModalKind.StageUpgradeUnlock ||
            kind == CoopModalKind.KingUpgrade ||
            kind == CoopModalKind.ScrapMasterUpgrade;

        public void HandleCmdDraftBarrierState(int senderId, BinaryReader r)
        {
            bool busy = r.ReadBoolean();
            string badge = r.ReadString();
            if (!_net.IsHost) return;

            PeerDraftBarrier[senderId] = (busy, badge ?? "");
            if (_net.Roster.TryGetValue(senderId, out var peer))
            {
                peer.IsDraftingBusy = busy;
                peer.ActiveModal = badge ?? "";
            }
            if (!AnyRemotePeerDrafting(out _))
            {
                BarrierWaitNotice = "";
                HostForceContinueRequested = false;
            }
            _net.BroadcastRoster();
            BroadcastDraftBarrierState();
        }

        public void BroadcastDraftBarrierState()
        {
            if (!_net.IsHost) return;
            _net.BroadcastToClients(PacketType.EvtDraftBarrierState, w =>
            {
                w.Write(PeerDraftBarrier.Count);
                foreach (var kv in PeerDraftBarrier)
                {
                    w.Write(kv.Key);
                    w.Write(kv.Value.busy);
                    w.WriteSafeString(kv.Value.modal);
                }
            });
            OnDraftBarrierChanged?.Invoke();
        }

        public void HandleEvtDraftBarrierState(BinaryReader r)
        {
            int count = r.ReadBoundedCount(32);
            PeerDraftBarrier.Clear();
            for (int i = 0; i < count; i++)
            {
                int pid = r.ReadInt32();
                bool busy = r.ReadBoolean();
                string modal = r.ReadString();
                PeerDraftBarrier[pid] = (busy, modal);
                if (_net.Roster.TryGetValue(pid, out var peer))
                    peer.IsDraftingBusy = busy;
            }
            OnDraftBarrierChanged?.Invoke();
        }

        public bool AnyRemotePeerDrafting(out string busyNames)
        {
            var names = new List<string>();
            foreach (var kv in PeerDraftBarrier)
            {
                if (kv.Key == _net.LocalPlayerId) continue;
                if (kv.Value.busy && _net.Roster.ContainsKey(kv.Key))
                    names.Add(_net.GetPlayerName(kv.Key));
            }
            busyNames = string.Join(", ", names);
            return names.Count > 0;
        }

        public bool IsHostVictoryTransitionGated(out string busyNames)
        {
            busyNames = "";
            if (!_net.IsHost || !GameRefs.IsInBattle || !_hostInVictorySequence || HostForceContinueRequested)
                return false;
            return AnyRemotePeerDrafting(out busyNames);
        }

        public bool CanHostAdvancePastBarrier(out string blockReason)
        {
            blockReason = "";
            if (!_net.IsHost || HostForceContinueRequested) return true;

            if (IsHostVictoryTransitionGated(out string vicBusy))
            {
                blockReason = CoopLoc.F("victory.waiting_client_upgrades", vicBusy);
                BarrierWaitNotice = blockReason;
                OnDraftBarrierChanged?.Invoke();
                return false;
            }

            var s = _session();
            if (s != null && s.Ruleset.AllowsClientIndependentDraftsAndShops && AnyRemotePeerDrafting(out string busyNames))
            {
                blockReason = CoopLoc.F("draft.waiting_peers", busyNames);
                BarrierWaitNotice = blockReason;
                OnDraftBarrierChanged?.Invoke();
                return false;
            }
            return true;
        }

        public void HostForceContinuePastBarrier()
        {
            if (!_net.IsHost) return;
            HostForceContinueRequested = true;
            BarrierWaitNotice = "";
            OnDraftBarrierChanged?.Invoke();
            GameRefs.ShowGameToast(CoopLoc.T("draft.force_continued"), 2.0f);
        }
    }
}
