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
    public sealed class CoopHudOverlay
    {
        private readonly SessionController _session;
        private readonly CoopLobbyPanel _lobbyPanel;
        private NetworkManager Net => _session.Net;

        private GameObject _hudRoot;
        private RectTransform _hudRect;
        private Transform _rowsContainer;
        private TMP_Text _headerText;
        private TMP_Text _subStatusText;
        private GameObject _undoBtnObj;
        private TMP_Text _emoteBtnText;
        private TMP_Text _undoBtnText;

        private GameObject _peekCardObj;
        private TMP_Text _peekTitleText;
        private TMP_Text _peekBodyText;

        private GameObject _modalBannerObj;
        private TMP_Text _modalBannerText;

        private readonly List<(int playerId, RectTransform rect)> _rowRects = new List<(int, RectTransform)>();
        private float _lastRefreshTime;
        private bool _dirty = true;
        private bool _lastCompactState;

        public CoopHudOverlay(SessionController session, CoopLobbyPanel lobbyPanel)
        {
            _session = session;
            _lobbyPanel = lobbyPanel;
            Net.OnRosterUpdated += () => _dirty = true;
            session.OnSettingsChanged += () => _dirty = true;
            session.Modals.OnHostModalChanged += (_, _) => _dirty = true;
            session.Run.OnMapVotesChanged += () => _dirty = true;
            session.Balance.OnTurnBlockCountChanged += () => _dirty = true;
            session.Ownership.OnPeekUpdated += () => _dirty = true;
            CoopLoc.OnLanguageChanged += () => _dirty = true;

            session.Emotes.ResolvePlayerHudAnchor = GetPlayerRowScreenAnchor;
        }

        public Vector2? GetPlayerRowScreenAnchor(int playerId)
        {
            if (_hudRoot == null || !_hudRoot.activeInHierarchy) return null;
            for (int i = 0; i < _rowRects.Count; i++)
            {
                var (pid, rt) = _rowRects[i];
                if (pid == playerId && rt != null)
                {
                    Vector3 pos = rt.position;
                    float leftOffset = (CoopConfig.CompactHudMode.Value ? 255f : 325f) * 0.5f + 135f;
                    return new Vector2(pos.x - leftOffset, pos.y);
                }
            }
            return null;
        }

        public void Tick(float now)
        {
            bool shouldShow = CoopConfig.ShowCoopHud.Value && Net.IsConnected && !GameRefs.IsLoading && !GameRefs.IsInTitle && !_lobbyPanel.IsOpen;
            if (!shouldShow)
            {
                if (_hudRoot != null && _hudRoot.activeSelf) _hudRoot.SetActive(false);
                if (_modalBannerObj != null && _modalBannerObj.activeSelf) _modalBannerObj.SetActive(false);
                if (_peekCardObj != null && _peekCardObj.activeSelf) _peekCardObj.SetActive(false);
                return;
            }

            EnsureBuilt();
            if (!_hudRoot.activeSelf) _hudRoot.SetActive(true);

            bool compact = CoopConfig.CompactHudMode.Value;
            if (compact != _lastCompactState)
            {
                _lastCompactState = compact;
                _dirty = true;
            }

            if (_undoBtnObj != null)
            {
                bool canUndo = GameRefs.IsInBattle && _session.Undo.HasValidLocalUndo;
                if (_undoBtnObj.activeSelf != canUndo) _undoBtnObj.SetActive(canUndo);
            }

            if (_dirty || now - _lastRefreshTime >= 0.75f)
            {
                _lastRefreshTime = now;
                _dirty = false;
                RefreshHud();
            }

            UpdateHoverPeekCard();
        }

        private void EnsureBuilt()
        {
            if (_hudRoot != null) return;

            NativeStyleProvider.HarvestFromScene();
            var canvas = NativeStyleProvider.GetOrCreateOverlayCanvas();

            _hudRoot = NativeStyleProvider.CreatePanel(canvas.transform, "EWCoop_HudWidget", new Color(0.09f, 0.08f, 0.12f, 0.88f), addBorder: true);
            _hudRect = _hudRoot.GetComponent<RectTransform>();
            _hudRect.anchorMin = new Vector2(1f, 1f);
            _hudRect.anchorMax = new Vector2(1f, 1f);
            _hudRect.pivot = new Vector2(1f, 1f);
            _hudRect.anchoredPosition = new Vector2(-18f, -18f);
            _hudRect.sizeDelta = new Vector2(325f, 130f);

            var fitter = _hudRoot.AddComponent<ContentSizeFitter>();
            fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

            var vlg = _hudRoot.AddComponent<VerticalLayoutGroup>();
            vlg.padding = new RectOffset(10, 10, 8, 9);
            vlg.spacing = 5f;
            vlg.childControlWidth = true;
            vlg.childControlHeight = true;
            vlg.childForceExpandWidth = true;
            vlg.childForceExpandHeight = false;

            var hdr = new GameObject("HudHeader");
            hdr.transform.SetParent(_hudRoot.transform, false);
            var hlg = hdr.AddComponent<HorizontalLayoutGroup>();
            hlg.spacing = 4f;
            hlg.childControlWidth = true;
            hlg.childControlHeight = true;
            hlg.childForceExpandWidth = false;

            _headerText = NativeStyleProvider.CreateLabel(hdr.transform, "CO-OP", 13.5f, NativeStyleProvider.EmberGold, TextAlignmentOptions.MidlineLeft, FontStyles.Bold);
            _headerText.gameObject.AddComponent<LayoutElement>().flexibleWidth = 1f;

            var undoBtn = NativeStyleProvider.CreateButton(hdr.transform, CoopLoc.T("hud.undo_btn"), NativeStyleProvider.ButtonDanger, () => _session.Undo.TryTriggerLocalUndo(), 11f, 22f);
            undoBtn.GetComponent<LayoutElement>().preferredWidth = 48f;
            _undoBtnObj = undoBtn.gameObject;
            _undoBtnText = undoBtn.GetComponentInChildren<TMP_Text>();
            _undoBtnObj.SetActive(false);

            var emoteBtn = NativeStyleProvider.CreateButton(hdr.transform, CoopLoc.T("hud.emotes_btn"), NativeStyleProvider.ButtonPrimary, () => _session.Emotes.ToggleWheel(), 11f, 22f);
            emoteBtn.GetComponent<LayoutElement>().preferredWidth = 58f;
            _emoteBtnText = emoteBtn.GetComponentInChildren<TMP_Text>();

            _subStatusText = NativeStyleProvider.CreateLabel(_hudRoot.transform, "", 11.5f, NativeStyleProvider.TextMuted, TextAlignmentOptions.MidlineLeft);

            var rowsGo = new GameObject("Rows");
            rowsGo.transform.SetParent(_hudRoot.transform, false);
            var rv = rowsGo.AddComponent<VerticalLayoutGroup>();
            rv.spacing = 4f;
            rv.childControlWidth = true;
            rv.childControlHeight = true;
            rv.childForceExpandWidth = true;
            rv.childForceExpandHeight = false;
            _rowsContainer = rowsGo.transform;

            _peekCardObj = NativeStyleProvider.CreatePanel(canvas.transform, "EWCoop_TeammatePeek", new Color(0.08f, 0.07f, 0.12f, 0.96f), addBorder: true);
            var prt = _peekCardObj.GetComponent<RectTransform>();
            prt.anchorMin = new Vector2(1f, 1f);
            prt.anchorMax = new Vector2(1f, 1f);
            prt.pivot = new Vector2(1f, 1f);
            prt.anchoredPosition = new Vector2(-354f, -18f);
            prt.sizeDelta = new Vector2(280f, 175f);
            var pcg = _peekCardObj.AddComponent<CanvasGroup>();
            pcg.blocksRaycasts = false;
            pcg.interactable = false;

            var pv = _peekCardObj.AddComponent<VerticalLayoutGroup>();
            pv.padding = new RectOffset(12, 12, 10, 10);
            pv.spacing = 5f;
            pv.childControlWidth = true;
            pv.childControlHeight = true;
            pv.childForceExpandWidth = true;
            pv.childForceExpandHeight = false;

            _peekTitleText = NativeStyleProvider.CreateLabel(_peekCardObj.transform, "", 15f, NativeStyleProvider.EmberGold, TextAlignmentOptions.MidlineLeft, FontStyles.Bold);
            _peekBodyText = NativeStyleProvider.CreateLabel(_peekCardObj.transform, "", 13f, NativeStyleProvider.TextLight, TextAlignmentOptions.TopLeft);
            _peekBodyText.enableWordWrapping = true;
            _peekCardObj.SetActive(false);

            _modalBannerObj = NativeStyleProvider.CreatePanel(canvas.transform, "EWCoop_ModalBanner", new Color(0.12f, 0.09f, 0.06f, 0.92f), addBorder: true);
            var brt = _modalBannerObj.GetComponent<RectTransform>();
            brt.anchorMin = new Vector2(0.5f, 1f);
            brt.anchorMax = new Vector2(0.5f, 1f);
            brt.pivot = new Vector2(0.5f, 1f);
            brt.anchoredPosition = new Vector2(0f, -18f);
            brt.sizeDelta = new Vector2(560f, 40f);

            _modalBannerText = NativeStyleProvider.CreateLabel(_modalBannerObj.transform, "", 15f, NativeStyleProvider.EmberGold, TextAlignmentOptions.Center, FontStyles.Bold);
            NativeStyleProvider.StretchFill(_modalBannerText.rectTransform, 14f, 4f);
            _modalBannerObj.SetActive(false);
        }

        private void RefreshHud()
        {
            bool compact = CoopConfig.CompactHudMode.Value;
            if (_hudRect != null)
                _hudRect.sizeDelta = new Vector2(compact ? 258f : 325f, _hudRect.sizeDelta.y);

            if (_emoteBtnText != null) _emoteBtnText.text = CoopLoc.T("hud.emotes_btn");
            if (_undoBtnText != null) _undoBtnText.text = CoopLoc.T("hud.undo_btn");

            string trans = Net.ActiveTransportKind == TransportKind.SteamP2P ? "STEAM" : "LAN";
            string eco = _session.Settings.EconomyMode == CoopEconomyMode.SeparateResources ? "SEP" : "SHR";
            string syncTag = _session.Economy.IsDesyncWarningActive ? " · SYNC" : "";
            _headerText.text = compact
                ? $"{CoopLoc.T("title.coop_button")} · {eco}"
                : $"{CoopLoc.T("title.coop_button")} · {eco} ({trans}){syncTag}";

            if (compact)
            {
                _subStatusText.gameObject.SetActive(false);
            }
            else if (GameRefs.IsInBattle && _session.Balance.IsSharedBlockCapActive())
            {
                int placed = _session.Balance.BlocksPlacedThisTurn;
                int cap = _session.Balance.GetEffectiveTurnBlockCap();
                _subStatusText.gameObject.SetActive(true);
                _subStatusText.text = CoopLoc.F("hud.turn_blocks", placed, cap);
                _subStatusText.color = placed >= cap ? NativeStyleProvider.EmberOrange : NativeStyleProvider.TextMuted;
            }
            else
            {
                _subStatusText.gameObject.SetActive(true);
                _subStatusText.text = $"{_session.Settings.Preset} · HP {Mathf.RoundToInt(_session.Settings.EnemyHpMultiplier * 100)}%";
                _subStatusText.color = NativeStyleProvider.TextMuted;
            }

            _rowRects.Clear();
            for (int i = _rowsContainer.childCount - 1; i >= 0; i--)
                UnityEngine.Object.Destroy(_rowsContainer.GetChild(i).gameObject);

            bool showGiftBtns = GameRefs.IsInBattle && _session.Ruleset.SupportsResourceGifting;
            bool isSeparate = _session.Settings.EconomyMode == CoopEconomyMode.SeparateResources;

            foreach (var kv in Net.Roster)
            {
                var p = kv.Value;
                var row = new GameObject($"HudPeer_{p.PlayerId}");
                row.transform.SetParent(_rowsContainer, false);
                var rowRt = row.AddComponent<RectTransform>();
                var hlg = row.AddComponent<HorizontalLayoutGroup>();
                hlg.spacing = 5f;
                hlg.childAlignment = TextAnchor.MiddleLeft;
                hlg.childControlWidth = true;
                hlg.childControlHeight = true;
                hlg.childForceExpandWidth = false;

                var dot = new GameObject("Dot");
                dot.transform.SetParent(row.transform, false);
                var dle = dot.AddComponent<LayoutElement>();
                dle.minWidth = 10f; dle.preferredWidth = 10f; dle.minHeight = 10f; dle.preferredHeight = 10f;
                dot.AddComponent<Image>().color = CoopConfig.GetPlayerColor(p.ColorIndex);

                string badge = "";
                if (!string.IsNullOrEmpty(p.ActiveModal)) badge = $" [{p.ActiveModal}]";
                else if (p.IsWaveReady) badge = " [✓]";

                string coinStr = "";
                if (isSeparate && GameRefs.IsInBattle && _session.Ownership.PeekSnapshots.TryGetValue(p.PlayerId, out var peek))
                    coinStr = $" {peek.Coin}g";

                var nameTmp = NativeStyleProvider.CreateLabel(row.transform, $"{p.Name}{coinStr}{badge}", compact ? 12f : 13f, NativeStyleProvider.TextLight, TextAlignmentOptions.MidlineLeft);
                nameTmp.gameObject.AddComponent<LayoutElement>().flexibleWidth = 1f;

                if (showGiftBtns && p.PlayerId != Net.LocalPlayerId)
                {
                    int targetId = p.PlayerId;
                    var giftBtn = NativeStyleProvider.CreateButton(row.transform, "+G", NativeStyleProvider.ButtonSuccess, () =>
                    {
                        var ig = MainGame.Instance?.IngameData;
                        if (ig == null) return;
                        int amt = Math.Max(25, Mathf.RoundToInt(ig.Coin * 0.25f));
                        if (ig.Coin < amt) amt = ig.Coin;
                        if (amt > 0) _session.Gifting.SendGoldToPlayer(targetId, amt);
                    }, 11f, 20f);
                    var gle = giftBtn.GetComponent<LayoutElement>();
                    gle.minWidth = 32f; gle.preferredWidth = 32f;
                }

                if (!compact)
                {
                    string right = p.IsHost ? "HOST" : $"{p.PingMs}ms";
                    var rTmp = NativeStyleProvider.CreateLabel(row.transform, right, 11f, NativeStyleProvider.TextMuted, TextAlignmentOptions.MidlineRight);
                    rTmp.gameObject.AddComponent<LayoutElement>().minWidth = 40f;
                }

                _rowRects.Add((p.PlayerId, rowRt));
            }

            bool independentModals = _session.Ruleset.AllowsClientIndependentDraftsAndShops;
            bool showBanner = Net.IsClient && !independentModals && _session.Modals.ActiveHostModal != CoopModalKind.None && !string.IsNullOrEmpty(_session.Modals.ActiveHostModalBanner);
            if (!showBanner && Net.IsClient && GameRefs.IsInTitle)
            {
                showBanner = true;
                _modalBannerText.text = $"{Net.HostPlayerName} ({eco}) ...";
            }
            else if (showBanner)
            {
                _modalBannerText.text = _session.Modals.ActiveHostModalBanner;
            }

            if (_modalBannerObj.activeSelf != showBanner)
                _modalBannerObj.SetActive(showBanner);
        }

        private void UpdateHoverPeekCard()
        {
            if (_peekCardObj == null || _lobbyPanel.IsOpen || CoopInputGuard.IsCapturingWorldInput)
            {
                if (_peekCardObj != null && _peekCardObj.activeSelf) _peekCardObj.SetActive(false);
                return;
            }

            Vector2 mouse = Input.mousePosition;
            int hoveredPeer = -1;
            for (int i = 0; i < _rowRects.Count; i++)
            {
                var (pid, rt) = _rowRects[i];
                if (rt != null && RectTransformUtility.RectangleContainsScreenPoint(rt, mouse, null))
                {
                    hoveredPeer = pid;
                    break;
                }
            }

            if (hoveredPeer < 0 || !_session.Ownership.PeekSnapshots.TryGetValue(hoveredPeer, out var snap))
            {
                if (_peekCardObj.activeSelf) _peekCardObj.SetActive(false);
                return;
            }

            if (!_peekCardObj.activeSelf) _peekCardObj.SetActive(true);
            Color col = CoopConfig.GetPlayerColor(Net.GetPlayerColorIndex(hoveredPeer));
            _peekTitleText.text = CoopLoc.F("hud.peek_title", Net.GetPlayerName(hoveredPeer));
            _peekTitleText.color = col;

            var sb = new StringBuilder();
            sb.AppendLine($"Gold: {snap.Coin}g   Gems: {snap.Gems}   Rerolls: {snap.Rerolls}");
            sb.AppendLine($"Relics: {snap.RelicCount}" + (string.IsNullOrEmpty(snap.ActiveModal) ? "" : $"   [{snap.ActiveModal}]"));

            if (snap.LoadoutTowers != null && snap.LoadoutTowers.Length > 0)
            {
                sb.Append("Towers: ");
                for (int i = 0; i < Math.Min(5, snap.LoadoutTowers.Length); i++)
                {
                    if (i > 0) sb.Append(", ");
                    sb.Append(ShortItem((eItemType)snap.LoadoutTowers[i]));
                }
                sb.AppendLine();
            }
            if (snap.HandBlocks != null && snap.HandBlocks.Length > 0)
            {
                sb.Append("Hand: ");
                for (int i = 0; i < Math.Min(5, snap.HandBlocks.Length); i++)
                {
                    if (i > 0) sb.Append(", ");
                    sb.Append(ShortItem((eItemType)snap.HandBlocks[i]));
                }
            }

            _peekBodyText.text = sb.ToString();
        }

        private static string ShortItem(eItemType t)
        {
            try
            {
                string loc = ResourceManager.Instance?.GetItemDataByType(t)?.GetLocNameString(false);
                if (!string.IsNullOrWhiteSpace(loc)) return loc;
            }
            catch { }
            string s = t.ToString();
            int idx = s.IndexOf('_', 1);
            return idx > 0 && idx + 1 < s.Length ? s.Substring(idx + 1).Replace("_TOWER", "").Replace("PANEL_", "").Replace('_', ' ') : s;
        }
    }
}
