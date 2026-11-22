using System;
using System.Collections.Generic;
using System.Text;
using EmberwardCoop.Core;
using EmberwardCoop.Localization;
using EmberwardCoop.Networking;
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

        private GameObject _mapVoteBar;
        private TMP_Text _mapVoteSummaryText;
        private GameObject _mapResolveBtnObj;
        private readonly Dictionary<int, (GameObject root, RectTransform rt, TMP_Text label)> _nodeVoteChips
            = new Dictionary<int, (GameObject, RectTransform, TMP_Text)>();

        private GameObject _sharedDraftTallyBar;
        private TMP_Text _sharedDraftTallyText;
        private GameObject _hostDraftConfirmBtnObj;

        private GameObject _barrierStrip;
        private TMP_Text _barrierText;
        private GameObject _barrierForceBtnObj;

        private bool _mapVotesDirty = true;
        private bool _draftDirty = true;
        private bool _barrierDirty = true;
        private float _lastPinUpdate;

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
            if (_sharedDraftTallyBar != null && _sharedDraftTallyBar.activeSelf) _sharedDraftTallyBar.SetActive(false);
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

            _sharedDraftTallyBar = NativeStyleProvider.CreatePanel(canvas.transform, "EWCoop_SharedDraftTally", NativeStyleProvider.TooltipBg, addBorder: true, roleOverride: PanelSurfaceRole.Tooltip);
            var htRt = _sharedDraftTallyBar.GetComponent<RectTransform>();
            htRt.anchorMin = new Vector2(0.5f, 1f);
            htRt.anchorMax = new Vector2(0.5f, 1f);
            htRt.pivot = new Vector2(0.5f, 1f);
            htRt.anchoredPosition = new Vector2(0f, -118f);
            htRt.sizeDelta = new Vector2(460f, 34f);

            var htFit = _sharedDraftTallyBar.AddComponent<ContentSizeFitter>();
            htFit.horizontalFit = ContentSizeFitter.FitMode.PreferredSize;
            htFit.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

            var htHlg = _sharedDraftTallyBar.AddComponent<HorizontalLayoutGroup>();
            htHlg.padding = new RectOffset(14, 12, 7, 7);
            htHlg.spacing = 10f;
            htHlg.childAlignment = TextAnchor.MiddleCenter;
            htHlg.childControlWidth = true;
            htHlg.childControlHeight = true;
            htHlg.childForceExpandWidth = false;

            _sharedDraftTallyText = NativeStyleProvider.CreateLabel(_sharedDraftTallyBar.transform, "", 12f, NativeStyleProvider.TextLight, TextAlignmentOptions.MidlineLeft, FontStyles.Bold, addShadow: true);
            _sharedDraftTallyText.richText = true;
            _sharedDraftTallyText.gameObject.AddComponent<LayoutElement>().minWidth = 200f;

            var pickBtn = NativeStyleProvider.CreateButton(_sharedDraftTallyBar.transform, CoopLoc.T("draft.pick_voted"), NativeStyleProvider.ButtonSuccess, () => Votes.HostApplyTopVotedDraftOption(), 11.5f, 24f);
            pickBtn.GetComponent<LayoutElement>().preferredWidth = 128f;
            _hostDraftConfirmBtnObj = pickBtn.gameObject;
            _sharedDraftTallyBar.SetActive(false);

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
                if (_sharedDraftTallyBar.activeSelf) _sharedDraftTallyBar.SetActive(false);
                return;
            }

            if (!_sharedDraftTallyBar.activeSelf) _sharedDraftTallyBar.SetActive(true);
            if (_draftDirty)
            {
                _draftDirty = false;
                var sb = new StringBuilder();
                sb.Append(CoopLoc.T("draft.party_votes_prefix")).Append(' ');
                if (draft.Votes.Count == 0)
                {
                    sb.Append(Net.IsClient ? CoopLoc.T("draft.shared_vote_hint") : CoopLoc.T("draft.no_votes_yet"));
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
                _sharedDraftTallyText.text = sb.ToString();
                _hostDraftConfirmBtnObj.SetActive(Net.IsHost && draft.Votes.Count > 0);
            }
        }

        private void TickDraftBarrier()
        {
            string busyNames = "";
            bool victoryGated = Votes.IsHostVictoryTransitionGated(out string vicBusy);
            bool anyBusy = victoryGated
                || (_session.Ruleset.AllowsClientIndependentDraftsAndShops
                    && Net.RemotePeerCount > 0
                    && Votes.AnyRemotePeerDrafting(out busyNames));

            if (!anyBusy)
            {
                if (_barrierStrip.activeSelf) _barrierStrip.SetActive(false);
                return;
            }

            if (!_barrierStrip.activeSelf) _barrierStrip.SetActive(true);
            string expectedText = victoryGated
                ? CoopLoc.F("victory.waiting_client_upgrades", vicBusy)
                : CoopLoc.F("draft.waiting_peers", busyNames);
            if (_barrierDirty || _barrierText.text != expectedText)
            {
                _barrierDirty = false;
                _barrierText.text = expectedText;
                _barrierForceBtnObj.SetActive(Net.IsHost);
            }
        }
    }
}
