using System;
using System.Collections.Generic;
using System.Text;
using EmberwardCoop.Core;
using EmberwardCoop.Localization;
using EmberwardCoop.Networking;
using EmberwardCoop.Rules;
using EmberwardCoop.Sync;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace EmberwardCoop.UI
{
    public sealed class CoopVoteAndDraftOverlay
    {
        private readonly SessionController _session;
        private NetworkManager Net => _session.Net;
        private CoopVoteAndDraftManager Votes => _session.Votes;

        // 1. Map Node Voting Top Bar + Floating MapNode Vote Chips
        private GameObject _mapVoteBar;
        private TMP_Text _mapVoteSummaryText;
        private GameObject _mapResolveBtnObj;
        private readonly Dictionary<int, (GameObject root, RectTransform rt, TMP_Text label)> _nodeVoteChips
            = new Dictionary<int, (GameObject, RectTransform, TMP_Text)>();

        // 2. Shared Draft / Choice Overlay (Full Card Picker on Clients, Compact Tally Strip on Host)
        private GameObject _clientDraftModal;
        private TMP_Text _clientDraftTitle;
        private Transform _clientDraftCardsRow;
        private GameObject _clientDraftRerollBtnObj;
        private TMP_Text _clientDraftRerollLabel;
        private GameObject _clientDraftSkipBtnObj;

        private GameObject _hostDraftTallyBar;
        private TMP_Text _hostDraftTallyText;
        private GameObject _hostDraftConfirmBtnObj;

        // 3. Separate-Mode Drafting Barrier Strip
        private GameObject _barrierStrip;
        private TMP_Text _barrierText;
        private GameObject _barrierForceBtnObj;

        private bool _mapVotesDirty = true;
        private bool _draftDirty = true;
        private bool _barrierDirty = true;
        private float _lastPinUpdate;

        public bool IsBlockingModalOpen => _clientDraftModal != null && _clientDraftModal.activeSelf;

        public CoopVoteAndDraftOverlay(SessionController session)
        {
            _session = session;
            Votes.OnMapVotesChanged += () => _mapVotesDirty = true;
            Votes.OnSharedDraftChanged += () => _draftDirty = true;
            Votes.OnDraftBarrierChanged += () => _barrierDirty = true;
            Net.OnRosterUpdated += () => { _mapVotesDirty = true; _draftDirty = true; _barrierDirty = true; };
            CoopLoc.OnLanguageChanged += () => { _mapVotesDirty = true; _draftDirty = true; _barrierDirty = true; };
        }

        public void Tick(float now)
        {
            bool connected = Net.IsConnected && !GameRefs.IsLoading && !GameRefs.IsInTitle;
            if (!connected)
            {
                HideAll();
                return;
            }

            EnsureBuilt();
            TickMapVotes(now);
            TickSharedDraft();
            TickDraftBarrier();
        }

        private void HideAll()
        {
            if (_mapVoteBar != null && _mapVoteBar.activeSelf) _mapVoteBar.SetActive(false);
            if (_clientDraftModal != null && _clientDraftModal.activeSelf) _clientDraftModal.SetActive(false);
            if (_hostDraftTallyBar != null && _hostDraftTallyBar.activeSelf) _hostDraftTallyBar.SetActive(false);
            if (_barrierStrip != null && _barrierStrip.activeSelf) _barrierStrip.SetActive(false);
            HideAllNodeChips();
        }

        private void HideAllNodeChips()
        {
            foreach (var kv in _nodeVoteChips)
                if (kv.Value.root != null && kv.Value.root.activeSelf) kv.Value.root.SetActive(false);
        }

        private void EnsureBuilt()
        {
            if (_mapVoteBar != null) return;
            NativeStyleProvider.HarvestFromScene();
            var canvas = NativeStyleProvider.GetOrCreateOverlayCanvas();

            // --- Map Vote Top Bar ---
            _mapVoteBar = NativeStyleProvider.CreatePanel(canvas.transform, "EWCoop_MapVoteBar", NativeStyleProvider.HudBg, addBorder: true, roleOverride: PanelSurfaceRole.Hud);
            var mvRt = _mapVoteBar.GetComponent<RectTransform>();
            mvRt.anchorMin = new Vector2(0.5f, 1f);
            mvRt.anchorMax = new Vector2(0.5f, 1f);
            mvRt.pivot = new Vector2(0.5f, 1f);
            mvRt.anchoredPosition = new Vector2(0f, -64f);
            mvRt.sizeDelta = new Vector2(460f, 36f);

            var mvFit = _mapVoteBar.AddComponent<ContentSizeFitter>();
            mvFit.horizontalFit = ContentSizeFitter.FitMode.PreferredSize;
            mvFit.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

            var mvHlg = _mapVoteBar.AddComponent<HorizontalLayoutGroup>();
            mvHlg.padding = new RectOffset(14, 12, 7, 7);
            mvHlg.spacing = 10f;
            mvHlg.childAlignment = TextAnchor.MiddleCenter;
            mvHlg.childControlWidth = true;
            mvHlg.childControlHeight = true;
            mvHlg.childForceExpandWidth = false;

            _mapVoteSummaryText = NativeStyleProvider.CreateLabel(_mapVoteBar.transform, "", 12.5f, NativeStyleProvider.TextLight, TextAlignmentOptions.MidlineLeft, FontStyles.Bold, addShadow: true);
            _mapVoteSummaryText.richText = true;
            _mapVoteSummaryText.gameObject.AddComponent<LayoutElement>().minWidth = 180f;

            var resolveBtn = NativeStyleProvider.CreateButton(_mapVoteBar.transform, CoopLoc.T("vote.go_now"), NativeStyleProvider.ButtonSuccess, () => Votes.HostForceResolveMapVote(), 11.5f, 26f);
            var resLe = resolveBtn.GetComponent<LayoutElement>();
            resLe.minWidth = 96f; resLe.preferredWidth = 108f;
            _mapResolveBtnObj = resolveBtn.gameObject;
            _mapVoteBar.SetActive(false);

            // --- Client Shared Draft Modal ---
            _clientDraftModal = new GameObject("EWCoop_ClientSharedDraft");
            _clientDraftModal.transform.SetParent(canvas.transform, false);
            NativeStyleProvider.StretchFill(_clientDraftModal.AddComponent<RectTransform>());
            _clientDraftModal.AddComponent<Image>().color = EmberwardTheme.ModalScrim;

            var card = NativeStyleProvider.CreatePanel(_clientDraftModal.transform, "DraftWindow", NativeStyleProvider.PanelBg, addBorder: true, roleOverride: PanelSurfaceRole.Window);
            var crt = card.GetComponent<RectTransform>();
            crt.anchorMin = new Vector2(0.5f, 0.5f);
            crt.anchorMax = new Vector2(0.5f, 0.5f);
            crt.sizeDelta = new Vector2(680f, 320f);
            var cfit = card.AddComponent<ContentSizeFitter>();
            cfit.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

            var cvlg = card.AddComponent<VerticalLayoutGroup>();
            cvlg.padding = new RectOffset(20, 20, 16, 18);
            cvlg.spacing = 10f;
            cvlg.childControlWidth = true;
            cvlg.childControlHeight = true;
            cvlg.childForceExpandWidth = true;
            cvlg.childForceExpandHeight = false;

            _clientDraftTitle = NativeStyleProvider.CreateLabel(card.transform, "", 17f, NativeStyleProvider.EmberGold, TextAlignmentOptions.Center, FontStyles.Bold, autoSize: true, minFontSize: 13f, addShadow: true);
            NativeStyleProvider.CreateDivider(card.transform, "DraftDiv");

            var cardsRow = new GameObject("OptionCards");
            cardsRow.transform.SetParent(card.transform, false);
            var crHlg = cardsRow.AddComponent<HorizontalLayoutGroup>();
            crHlg.spacing = 10f;
            crHlg.childAlignment = TextAnchor.MiddleCenter;
            crHlg.childControlWidth = true;
            crHlg.childControlHeight = true;
            crHlg.childForceExpandWidth = true;
            crHlg.childForceExpandHeight = false;
            var crLe = cardsRow.AddComponent<LayoutElement>();
            crLe.minHeight = 160f;
            _clientDraftCardsRow = cardsRow.transform;

            var foot = new GameObject("DraftFooter");
            foot.transform.SetParent(card.transform, false);
            var fHlg = foot.AddComponent<HorizontalLayoutGroup>();
            fHlg.spacing = 10f;
            fHlg.childAlignment = TextAnchor.MiddleCenter;
            fHlg.childControlWidth = true;
            fHlg.childControlHeight = true;
            fHlg.childForceExpandWidth = false;
            foot.AddComponent<LayoutElement>().preferredHeight = 34f;

            var rerollBtn = NativeStyleProvider.CreateButton(foot.transform, CoopLoc.T("draft.reroll"), NativeStyleProvider.ButtonPrimary, () => Votes.RequestSharedDraftReroll(), 12.5f, 32f);
            rerollBtn.GetComponent<LayoutElement>().preferredWidth = 150f;
            _clientDraftRerollBtnObj = rerollBtn.gameObject;
            _clientDraftRerollLabel = rerollBtn.GetComponentInChildren<TMP_Text>();

            var skipBtn = NativeStyleProvider.CreateButton(foot.transform, CoopLoc.T("draft.skip"), NativeStyleProvider.ButtonSecondary, () => Votes.VoteSharedDraftOption(-1), 12.5f, 32f);
            skipBtn.GetComponent<LayoutElement>().preferredWidth = 130f;
            _clientDraftSkipBtnObj = skipBtn.gameObject;
            _clientDraftModal.SetActive(false);

            // --- Host Shared Draft Tally Strip ---
            _hostDraftTallyBar = NativeStyleProvider.CreatePanel(canvas.transform, "EWCoop_HostDraftTally", NativeStyleProvider.TooltipBg, addBorder: true, roleOverride: PanelSurfaceRole.Tooltip);
            var htRt = _hostDraftTallyBar.GetComponent<RectTransform>();
            htRt.anchorMin = new Vector2(0.5f, 1f);
            htRt.anchorMax = new Vector2(0.5f, 1f);
            htRt.pivot = new Vector2(0.5f, 1f);
            htRt.anchoredPosition = new Vector2(0f, -118f);
            htRt.sizeDelta = new Vector2(460f, 34f);

            var htFit = _hostDraftTallyBar.AddComponent<ContentSizeFitter>();
            htFit.horizontalFit = ContentSizeFitter.FitMode.PreferredSize;
            htFit.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

            var htHlg = _hostDraftTallyBar.AddComponent<HorizontalLayoutGroup>();
            htHlg.padding = new RectOffset(14, 12, 7, 7);
            htHlg.spacing = 10f;
            htHlg.childAlignment = TextAnchor.MiddleCenter;
            htHlg.childControlWidth = true;
            htHlg.childControlHeight = true;
            htHlg.childForceExpandWidth = false;

            _hostDraftTallyText = NativeStyleProvider.CreateLabel(_hostDraftTallyBar.transform, "", 12f, NativeStyleProvider.TextLight, TextAlignmentOptions.MidlineLeft, FontStyles.Bold, addShadow: true);
            _hostDraftTallyText.richText = true;
            _hostDraftTallyText.gameObject.AddComponent<LayoutElement>().minWidth = 200f;

            var pickBtn = NativeStyleProvider.CreateButton(_hostDraftTallyBar.transform, CoopLoc.T("draft.pick_voted"), NativeStyleProvider.ButtonSuccess, () => Votes.HostApplyTopVotedDraftOption(), 11.5f, 24f);
            pickBtn.GetComponent<LayoutElement>().preferredWidth = 128f;
            _hostDraftConfirmBtnObj = pickBtn.gameObject;
            _hostDraftTallyBar.SetActive(false);

            // --- Separate-Mode Drafting Barrier Strip ---
            _barrierStrip = NativeStyleProvider.CreatePanel(canvas.transform, "EWCoop_DraftBarrier", NativeStyleProvider.TooltipBg, addBorder: true, roleOverride: PanelSurfaceRole.Tooltip);
            var bsRt = _barrierStrip.GetComponent<RectTransform>();
            bsRt.anchorMin = new Vector2(0.5f, 1f);
            bsRt.anchorMax = new Vector2(0.5f, 1f);
            bsRt.pivot = new Vector2(0.5f, 1f);
            bsRt.anchoredPosition = new Vector2(0f, -156f);
            bsRt.sizeDelta = new Vector2(420f, 34f);

            var bsFit = _barrierStrip.AddComponent<ContentSizeFitter>();
            bsFit.horizontalFit = ContentSizeFitter.FitMode.PreferredSize;
            bsFit.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

            var bsHlg = _barrierStrip.AddComponent<HorizontalLayoutGroup>();
            bsHlg.padding = new RectOffset(14, 12, 7, 7);
            bsHlg.spacing = 10f;
            bsHlg.childAlignment = TextAnchor.MiddleCenter;
            bsHlg.childControlWidth = true;
            bsHlg.childControlHeight = true;
            bsHlg.childForceExpandWidth = false;

            _barrierText = NativeStyleProvider.CreateLabel(_barrierStrip.transform, "", 12f, NativeStyleProvider.EmberGold, TextAlignmentOptions.MidlineLeft, FontStyles.Bold, addShadow: true);
            _barrierText.richText = true;
            _barrierText.gameObject.AddComponent<LayoutElement>().minWidth = 180f;

            var forceBtn = NativeStyleProvider.CreateButton(_barrierStrip.transform, CoopLoc.T("draft.force_continue"), NativeStyleProvider.ButtonPrimary, () => Votes.HostForceContinuePastBarrier(), 11f, 24f);
            forceBtn.GetComponent<LayoutElement>().preferredWidth = 134f;
            _barrierForceBtnObj = forceBtn.gameObject;
            _barrierStrip.SetActive(false);
        }

        private void TickMapVotes(float now)
        {
            bool showMapVotes = GameRefs.IsInMap && Net.RemotePeerCount > 0 && Votes.MapNodeVotes.Count > 0;
            if (!showMapVotes)
            {
                if (_mapVoteBar.activeSelf) _mapVoteBar.SetActive(false);
                HideAllNodeChips();
                return;
            }

            if (!_mapVoteBar.activeSelf) _mapVoteBar.SetActive(true);
            if (_mapVotesDirty)
            {
                _mapVotesDirty = false;
                var sb = new StringBuilder();
                sb.Append(CoopLoc.F("vote.map_progress", Votes.MapNodeVotes.Count, Net.Roster.Count));
                sb.Append("  ·  ");
                int shown = 0;
                foreach (var kv in Votes.MapNodeVotes)
                {
                    if (shown++ > 0) sb.Append("  ");
                    string hex = ColorUtility.ToHtmlStringRGB(CoopConfig.GetPlayerColor(Net.GetPlayerColorIndex(kv.Key)));
                    sb.Append($"<color=#{hex}>{Net.GetPlayerName(kv.Key)}</color>→#{kv.Value}");
                }
                _mapVoteSummaryText.text = sb.ToString();
                _mapResolveBtnObj.SetActive(Net.IsHost);
            }

            if (now - _lastPinUpdate >= 0.05f)
            {
                _lastPinUpdate = now;
                UpdateMapNodeVoteChips();
            }
        }

        private void UpdateMapNodeVoteChips()
        {
            var msp = GameRefs.GetMapSceneProcess();
            var nodes = msp?.mapManager?.list_MapNodes;
            if (nodes == null) { HideAllNodeChips(); return; }

            var byNode = new Dictionary<int, List<int>>();
            foreach (var kv in Votes.MapNodeVotes)
            {
                if (!byNode.TryGetValue(kv.Value, out var list))
                {
                    list = new List<int>();
                    byNode[kv.Value] = list;
                }
                list.Add(kv.Key);
            }

            var canvas = NativeStyleProvider.GetOrCreateOverlayCanvas();
            var used = new HashSet<int>();

            foreach (var kv in byNode)
            {
                int nodeIdx = kv.Key;
                if (nodeIdx < 0 || nodeIdx >= nodes.Count || nodes[nodeIdx] == null) continue;
                var nodeRt = nodes[nodeIdx].GetComponent<RectTransform>();
                if (nodeRt == null || !nodes[nodeIdx].gameObject.activeInHierarchy) continue;

                Canvas nodeCanvas = nodeRt.GetComponentInParent<Canvas>();
                Camera cam = (nodeCanvas != null && nodeCanvas.renderMode != RenderMode.ScreenSpaceOverlay) ? nodeCanvas.worldCamera : null;
                Vector2 screenPos = RectTransformUtility.WorldToScreenPoint(cam, nodeRt.position);
                if (screenPos.x < -40f || screenPos.x > Screen.width + 40f || screenPos.y < -40f || screenPos.y > Screen.height + 40f)
                    continue;

                used.Add(nodeIdx);
                if (!_nodeVoteChips.TryGetValue(nodeIdx, out var chip) || chip.root == null)
                {
                    var go = NativeStyleProvider.CreateBadgePill(canvas.transform, $"VoteChip_{nodeIdx}", NativeStyleProvider.TooltipBg, addBorder: true);
                    var rt = go.GetComponent<RectTransform>();
                    rt.anchorMin = new Vector2(0.5f, 0.5f);
                    rt.anchorMax = new Vector2(0.5f, 0.5f);
                    var cg = go.AddComponent<CanvasGroup>();
                    cg.blocksRaycasts = false;
                    cg.interactable = false;
                    var hlg = go.AddComponent<HorizontalLayoutGroup>();
                    hlg.padding = new RectOffset(7, 7, 3, 3);
                    hlg.childControlWidth = true;
                    hlg.childControlHeight = true;
                    var fit = go.AddComponent<ContentSizeFitter>();
                    fit.horizontalFit = ContentSizeFitter.FitMode.PreferredSize;
                    fit.verticalFit = ContentSizeFitter.FitMode.PreferredSize;
                    var lbl = NativeStyleProvider.CreateLabel(go.transform, "", 11f, NativeStyleProvider.TextLight, TextAlignmentOptions.Center, FontStyles.Bold, addShadow: true);
                    lbl.richText = true;
                    chip = (go, rt, lbl);
                    _nodeVoteChips[nodeIdx] = chip;
                }

                var sb = new StringBuilder();
                sb.Append($"[{kv.Value.Count}] ");
                for (int i = 0; i < kv.Value.Count; i++)
                {
                    if (i > 0) sb.Append(", ");
                    int pid = kv.Value[i];
                    string hex = ColorUtility.ToHtmlStringRGB(CoopConfig.GetPlayerColor(Net.GetPlayerColorIndex(pid)));
                    sb.Append($"<color=#{hex}>{Net.GetPlayerName(pid)}</color>");
                }
                chip.label.text = sb.ToString();
                Vector2 localPos = NativeStyleProvider.ScreenToCanvasLocal(screenPos);
                chip.rt.anchoredPosition = localPos + new Vector2(0f, 44f);
                if (!chip.root.activeSelf) chip.root.SetActive(true);
            }

            foreach (var kv in _nodeVoteChips)
                if (!used.Contains(kv.Key) && kv.Value.root != null && kv.Value.root.activeSelf)
                    kv.Value.root.SetActive(false);
        }

        private void TickSharedDraft()
        {
            var draft = Votes.ActiveSharedDraft;
            if (draft == null || draft.Options.Count == 0)
            {
                if (_clientDraftModal.activeSelf) _clientDraftModal.SetActive(false);
                if (_hostDraftTallyBar.activeSelf) _hostDraftTallyBar.SetActive(false);
                return;
            }

            if (Net.IsClient)
            {
                if (_hostDraftTallyBar.activeSelf) _hostDraftTallyBar.SetActive(false);
                if (!_clientDraftModal.activeSelf) _clientDraftModal.SetActive(true);
                if (_draftDirty)
                {
                    _draftDirty = false;
                    RebuildClientDraftModal(draft);
                }
            }
            else if (Net.IsHost)
            {
                if (_clientDraftModal.activeSelf) _clientDraftModal.SetActive(false);
                if (!_hostDraftTallyBar.activeSelf) _hostDraftTallyBar.SetActive(true);
                if (_draftDirty)
                {
                    _draftDirty = false;
                    var sb = new StringBuilder();
                    sb.Append(CoopLoc.T("draft.party_votes_prefix")).Append(' ');
                    if (draft.Votes.Count == 0)
                    {
                        sb.Append(CoopLoc.T("draft.no_votes_yet"));
                    }
                    else
                    {
                        int n = 0;
                        foreach (var kv in draft.Votes)
                        {
                            if (n++ > 0) sb.Append("  ·  ");
                            string hex = ColorUtility.ToHtmlStringRGB(CoopConfig.GetPlayerColor(Net.GetPlayerColorIndex(kv.Key)));
                            string pick = kv.Value >= 0 && kv.Value < draft.Options.Count
                                ? draft.Options[kv.Value].DisplayName
                                : CoopLoc.T("draft.skip");
                            sb.Append($"<color=#{hex}>{Net.GetPlayerName(kv.Key)}</color>: {pick}");
                        }
                    }
                    _hostDraftTallyText.text = sb.ToString();
                    _hostDraftConfirmBtnObj.SetActive(draft.Votes.Count > 0);
                }
            }
        }

        private void RebuildClientDraftModal(SharedDraftSession draft)
        {
            _clientDraftTitle.text = $"{draft.Title}  ({CoopLoc.T("draft.shared_vote_hint")})";

            for (int i = _clientDraftCardsRow.childCount - 1; i >= 0; i--)
                UnityEngine.Object.Destroy(_clientDraftCardsRow.GetChild(i).gameObject);

            draft.Votes.TryGetValue(Net.LocalPlayerId, out int myVote);
            bool iVoted = draft.Votes.ContainsKey(Net.LocalPlayerId);

            for (int i = 0; i < draft.Options.Count; i++)
            {
                var opt = draft.Options[i];
                bool selected = iVoted && myVote == opt.Index;

                var cardGo = NativeStyleProvider.CreatePanel(
                    _clientDraftCardsRow,
                    $"DraftOpt_{opt.Index}",
                    selected ? EmberwardTheme.SelectedTileBg : NativeStyleProvider.CardBg,
                    addBorder: true,
                    isSubPanel: true,
                    roleOverride: selected ? PanelSurfaceRole.CustomAccent : PanelSurfaceRole.Card);
                var cle = cardGo.AddComponent<LayoutElement>();
                cle.minWidth = 148f;
                cle.preferredWidth = 178f;
                cle.minHeight = 152f;

                var vlg = cardGo.AddComponent<VerticalLayoutGroup>();
                vlg.padding = new RectOffset(10, 10, 10, 10);
                vlg.spacing = 6f;
                vlg.childAlignment = TextAnchor.UpperCenter;
                vlg.childControlWidth = true;
                vlg.childControlHeight = true;
                vlg.childForceExpandWidth = true;
                vlg.childForceExpandHeight = false;

                Sprite icon = null;
                try { if (opt.ItemType != eItemType.NONE) icon = ResourceManager.Instance?.GetItemDataByType(opt.ItemType)?.GetCardIcon(); } catch { }
                if (icon != null)
                {
                    var icWrap = new GameObject("Icon");
                    icWrap.transform.SetParent(cardGo.transform, false);
                    var ile = icWrap.AddComponent<LayoutElement>();
                    ile.minHeight = 52f; ile.preferredHeight = 52f;
                    var icImg = icWrap.AddComponent<Image>();
                    icImg.sprite = icon;
                    icImg.color = Color.white;
                    icImg.preserveAspect = true;
                    icImg.raycastTarget = false;
                }

                NativeStyleProvider.CreateLabel(cardGo.transform, opt.DisplayName, 13.5f, NativeStyleProvider.EmberGold, TextAlignmentOptions.Center, FontStyles.Bold, wordWrap: true, addShadow: true);
                if (!string.IsNullOrWhiteSpace(opt.Subtitle))
                    NativeStyleProvider.CreateLabel(cardGo.transform, opt.Subtitle, 11f, NativeStyleProvider.TextMuted, TextAlignmentOptions.Center, wordWrap: true);

                var votersSb = new StringBuilder();
                foreach (var kv in draft.Votes)
                {
                    if (kv.Value != opt.Index) continue;
                    if (votersSb.Length > 0) votersSb.Append(", ");
                    string hex = ColorUtility.ToHtmlStringRGB(CoopConfig.GetPlayerColor(Net.GetPlayerColorIndex(kv.Key)));
                    votersSb.Append($"<color=#{hex}>{Net.GetPlayerName(kv.Key)}</color>");
                }
                if (votersSb.Length > 0)
                {
                    var vLbl = NativeStyleProvider.CreateLabel(cardGo.transform, votersSb.ToString(), 11f, NativeStyleProvider.TextLight, TextAlignmentOptions.Center, FontStyles.Bold, wordWrap: true);
                    vLbl.richText = true;
                }

                int optIdx = opt.Index;
                var btn = cardGo.AddComponent<Button>();
                btn.targetGraphic = cardGo.GetComponent<Image>();
                NativeStyleProvider.AttachHoverSound(cardGo, 1.14f);
                btn.onClick.AddListener((UnityEngine.Events.UnityAction)(() =>
                {
                    NativeStyleProvider.PlayUiSelectSound();
                    Votes.VoteSharedDraftOption(optIdx);
                }));
            }

            _clientDraftRerollBtnObj.SetActive(draft.RerollsLeft > 0);
            if (_clientDraftRerollLabel != null)
                _clientDraftRerollLabel.text = $"{CoopLoc.T("draft.reroll")} ({draft.RerollsLeft})";
            _clientDraftSkipBtnObj.SetActive(draft.CanSkip);
        }

        private void TickDraftBarrier()
        {
            string busyNames = "";
            bool anyBusy = _session.Ruleset.AllowsClientIndependentDraftsAndShops
                && Net.RemotePeerCount > 0
                && Votes.AnyRemotePeerDrafting(out busyNames);

            if (!anyBusy)
            {
                if (_barrierStrip.activeSelf) _barrierStrip.SetActive(false);
                return;
            }

            if (!_barrierStrip.activeSelf) _barrierStrip.SetActive(true);
            if (_barrierDirty)
            {
                _barrierDirty = false;
                _barrierText.text = CoopLoc.F("draft.waiting_peers", busyNames);
                _barrierForceBtnObj.SetActive(Net.IsHost);
            }
        }
    }
}
