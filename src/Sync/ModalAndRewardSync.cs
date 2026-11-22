using System;
using System.IO;
using EmberwardCoop.Core;
using EmberwardCoop.Localization;
using EmberwardCoop.Networking;

namespace EmberwardCoop.Sync
{
    public enum CoopModalKind : byte
    {
        None = 0,
        TetrisDraft = 1,
        RewardDraft = 2,
        DiscoverChest = 3,
        Shop = 4,
        MiniShop = 5,
        Workshop = 6,
        MiniWorkshop = 7,
        RuneForge = 8,
        Altar = 9,
        Campsite = 10,
        Circus = 11,
        Academy = 12,
        SecretVault = 13,
        QuestSelect = 14,
        QuestReward = 15,
        RoguelitePerk = 16,
        TowerArrange = 17,
        Victory = 18,
        Defeat = 19,
        StageUpgradeUnlock = 20,
        KingUpgrade = 21,
        ScrapMasterUpgrade = 22,
        AcademyPerkReward = 23,
        AltarEffectReward = 24
    }

    public sealed class ModalAndRewardSync
    {
        private readonly NetworkManager _net;
        private readonly Func<SessionController> _session;

        public CoopModalKind ActiveHostModal { get; private set; } = CoopModalKind.None;
        public string ActiveHostModalBanner { get; private set; } = "";
        public event Action<CoopModalKind, string> OnHostModalChanged;

        private CoopModalKind _lastDetectedHostModal = CoopModalKind.None;
        private float _lastModalPollTime;

        public ModalAndRewardSync(NetworkManager net, Func<SessionController> session)
        {
            _net = net;
            _session = session;
        }

        public void Reset()
        {
            ActiveHostModal = CoopModalKind.None;
            ActiveHostModalBanner = "";
            _lastDetectedHostModal = CoopModalKind.None;
            OnHostModalChanged?.Invoke(CoopModalKind.None, "");
        }

        public void Tick(float now)
        {
            if (!_net.IsHost || _net.RemotePeerCount == 0) return;
            if (now - _lastModalPollTime < 0.25f) return;
            _lastModalPollTime = now;

            CoopModalKind detected = DetectTopActiveModal();
            if (detected != _lastDetectedHostModal)
            {
                bool wasOpen = _lastDetectedHostModal != CoopModalKind.None;
                _lastDetectedHostModal = detected;
                ActiveHostModal = detected;
                ActiveHostModalBanner = FormatBanner(detected);
                _net.SetPeerModal(0, detected == CoopModalKind.None ? "" : GetShortBadge(detected));

                if (detected == CoopModalKind.None && wasOpen)
                {
                    BroadcastModalClosed();
                    _session()?.Run.CheckAndBroadcastHostRunState(force: true);
                    if (GameRefs.IsInBattle)
                        _session()?.Economy.CheckHostEconomyAndHand(force: true);
                }
                else
                {
                    BroadcastModalState(detected, ActiveHostModalBanner);
                }
                OnHostModalChanged?.Invoke(ActiveHostModal, ActiveHostModalBanner);
            }
        }

        public void NotifyHostPopupOpened(CoopModalKind kind)
        {
            if (!_net.IsHost) return;
            _lastDetectedHostModal = kind;
            ActiveHostModal = kind;
            ActiveHostModalBanner = FormatBanner(kind);
            _net.SetPeerModal(0, GetShortBadge(kind));
            BroadcastModalState(kind, ActiveHostModalBanner);
            OnHostModalChanged?.Invoke(kind, ActiveHostModalBanner);
        }

        public void NotifyHostPopupClosed(CoopModalKind kind)
        {
            if (!_net.IsHost) return;
            CoopLogger.Debug(LogCategory.Modal, $"Host closed modal: {kind}");
            _session()?.Votes.CloseSharedDraft(broadcast: true);
            _session()?.Run.MarkDirty();
            if (GameRefs.IsInBattle) _session()?.Economy.MarkDirty();
        }

        public void BroadcastModalState(CoopModalKind kind, string banner)
        {
            if (!_net.IsHost) return;
            _net.BroadcastToClients(PacketType.EvtModalState, w =>
            {
                w.Write((byte)kind);
                w.WriteSafeString(banner);
            });
            CoopLogger.Debug(LogCategory.Modal, $"Broadcast ModalState: {kind} ({banner})");
        }

        public void BroadcastModalClosed()
        {
            if (!_net.IsHost) return;
            _net.BroadcastToClients(PacketType.EvtModalClosed);
            CoopLogger.Debug(LogCategory.Modal, "Broadcast ModalClosed");
        }

        public void HandleEvtModalState(BinaryReader r)
        {
            var kind = (CoopModalKind)r.ReadByte();
            string banner = r.ReadString();
            ActiveHostModal = kind;
            ActiveHostModalBanner = banner;
            OnHostModalChanged?.Invoke(kind, banner);
            CoopLogger.Debug(LogCategory.Modal, $"Host Modal -> {kind}: {banner}");
        }

        public string GetLocalTopModalBadge()
        {
            var kind = DetectTopActiveModal();
            return kind == CoopModalKind.None ? "" : GetShortBadge(kind);
        }

        public CoopModalKind DetectLocalTopModal() => DetectTopActiveModal();

        public void HandleEvtModalClosed()
        {
            ActiveHostModal = CoopModalKind.None;
            ActiveHostModalBanner = "";
            OnHostModalChanged?.Invoke(CoopModalKind.None, "");

            // Only auto-close orphan client popups in Host-Shared Draft mode; Separate mode lets clients finish independently.
            if (!(_session()?.Ruleset.AllowsClientIndependentDraftsAndShops ?? false))
                CloseOrphanClientRewardPopups();
        }

        public void BroadcastVictoryOrDefeat(bool isVictory, bool isFinalBoss)
        {
            if (!_net.IsHost) return;
            if (isVictory)
                _session()?.Votes.OnHostVictorySequenceStarted();

            _net.BroadcastToClients(PacketType.EvtVictoryOrDefeat, w =>
            {
                w.Write(isVictory);
                w.Write(isFinalBoss);
            });
            CoopLogger.Info(LogCategory.Modal, isVictory
                ? (isFinalBoss ? "Final boss defeated; victory synced." : "Stage victory synced.")
                : "Defeat synced.");
        }

        public void HandleEvtVictoryOrDefeat(BinaryReader r)
        {
            bool isVictory = r.ReadBoolean();
            bool isFinalBoss = r.ReadBoolean();
            if (isVictory)
                _session()?.Votes.OnClientVictorySequenceStarted();

            CoopLogger.Info(LogCategory.Modal, isVictory
                ? (isFinalBoss ? "Victory synced; waiting for Host on summary screen." : "Stage cleared; waiting for Host after rewards.")
                : "Defeat synced; Host controls retry/return.");

            GameRefs.ShowGameToast(CoopLoc.T("transition.waiting_for_host"), 3.5f);

            var run = _session()?.Run;
            if (run != null) run.MenuLockDirty = true;
        }

        private static void CloseOrphanClientRewardPopups()
        {
            try
            {
                var uiMgr = UIManager.Instance;
                if (uiMgr == null || !uiMgr.HasAnyPopupWindow()) return;

                var top = uiMgr.GetTopPopupWindow();
                if (top == null) return;
                string name = top.GetIl2CppType()?.Name ?? "";

                // Never close player's own settings/pause/deck viewer or victory/defeat summary.
                if (name.Contains("Setting") || name.Contains("Pause") || name.Contains("DeckList") ||
                    name.Contains("Victory") || name.Contains("Defeat"))
                    return;

                var popup = top.TryCast<APopupWindow>();
                if (popup != null && !popup.IsWindowClosed)
                {
                    using (NetworkScope.Enter())
                    {
                        popup.CloseWindow();
                    }
                    CoopLogger.Debug(LogCategory.Modal, $"Closed orphan client modal: {name}");
                }
            }
            catch { }
        }

        public static CoopModalKind ClassifyPopup(Il2CppSystem.Object popup)
        {
            if (popup == null) return CoopModalKind.None;
            string t = popup.GetIl2CppType()?.Name ?? "";

            if (t.Contains("StageUpgradeUnlock")) return CoopModalKind.StageUpgradeUnlock;
            if (t.Contains("KingUpgrade")) return CoopModalKind.KingUpgrade;
            if (t.Contains("ScrapMasterUpgrade")) return CoopModalKind.ScrapMasterUpgrade;
            if (t.Contains("AcademyPerkReward")) return CoopModalKind.AcademyPerkReward;
            if (t.Contains("AltarEffectReward")) return CoopModalKind.AltarEffectReward;
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

        private static CoopModalKind DetectTopActiveModal()
        {
            try
            {
                var uiMgr = UIManager.Instance;
                if (uiMgr == null || !uiMgr.HasAnyPopupWindow()) return CoopModalKind.None;
                return ClassifyPopup(uiMgr.GetTopPopupWindow());
            }
            catch { }
            return CoopModalKind.None;
        }

        public static string GetShortBadge(CoopModalKind kind) => kind switch
        {
            CoopModalKind.TetrisDraft => "Draft",
            CoopModalKind.RewardDraft => "Reward",
            CoopModalKind.DiscoverChest => "Chest",
            CoopModalKind.Shop or CoopModalKind.MiniShop => "Shop",
            CoopModalKind.Workshop or CoopModalKind.MiniWorkshop => "Workshop",
            CoopModalKind.RuneForge => "Runes",
            CoopModalKind.Altar or CoopModalKind.AltarEffectReward => "Altar",
            CoopModalKind.Campsite => "Camp",
            CoopModalKind.Circus => "Circus",
            CoopModalKind.Academy or CoopModalKind.AcademyPerkReward => "Academy",
            CoopModalKind.SecretVault => "Vault",
            CoopModalKind.QuestSelect or CoopModalKind.QuestReward => "Quest",
            CoopModalKind.RoguelitePerk => "Perk",
            CoopModalKind.StageUpgradeUnlock or CoopModalKind.KingUpgrade or CoopModalKind.ScrapMasterUpgrade => "Upgrades",
            CoopModalKind.TowerArrange => "Loadout",
            CoopModalKind.Victory => "Victory",
            CoopModalKind.Defeat => "Defeat",
            _ => ""
        };

        private string FormatBanner(CoopModalKind kind)
        {
            string host = _net.HostPlayerName ?? "Host";
            return kind switch
            {
                CoopModalKind.TetrisDraft => $"{host} is drafting starting blocks...",
                CoopModalKind.RewardDraft => $"{host} is choosing stage rewards...",
                CoopModalKind.DiscoverChest => $"{host} is opening a treasure chest...",
                CoopModalKind.Shop or CoopModalKind.MiniShop => $"{host} is browsing the Shop...",
                CoopModalKind.Workshop or CoopModalKind.MiniWorkshop => $"{host} is upgrading blocks at the Workshop...",
                CoopModalKind.RuneForge => $"{host} is socketing runes...",
                CoopModalKind.Altar or CoopModalKind.AltarEffectReward => $"{host} is inspecting an Altar Pact...",
                CoopModalKind.Campsite => $"{host} is resting at the Campsite...",
                CoopModalKind.Circus => $"{host} is spinning the Circus wheel...",
                CoopModalKind.Academy or CoopModalKind.AcademyPerkReward => $"{host} is selecting Academy loadout...",
                CoopModalKind.SecretVault => $"{host} is choosing from the Secret Vault...",
                CoopModalKind.QuestSelect => $"{host} is picking a Run Quest...",
                CoopModalKind.QuestReward => $"{host} is claiming Quest Rewards...",
                CoopModalKind.RoguelitePerk => $"{host} is choosing a Perk...",
                CoopModalKind.StageUpgradeUnlock or CoopModalKind.KingUpgrade or CoopModalKind.ScrapMasterUpgrade => $"{host} is picking stage upgrades...",
                CoopModalKind.TowerArrange => $"{host} is arranging Tower Loadout...",
                CoopModalKind.Victory => CoopLoc.T("transition.waiting_for_host"),
                CoopModalKind.Defeat => CoopLoc.T("transition.waiting_for_host"),
                _ => ""
            };
        }
    }
}
