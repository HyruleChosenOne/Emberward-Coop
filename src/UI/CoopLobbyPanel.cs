using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using EmberwardCoop.Core;
using EmberwardCoop.Localization;
using EmberwardCoop.Networking;
using EmberwardCoop.Rules;
using EmberwardCoop.Sync;
using TMPro;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.UI;

namespace EmberwardCoop.UI
{
    public sealed class CoopLobbyPanel
    {
        private readonly SessionController _session;
        private NetworkManager Net => _session.Net;
        private SessionSettings Settings => _session.Settings;
        private LobbySetupState Setup => _session.LobbySetup;

        private GameObject _root;
        private bool _isOpen;
        private bool _hideLobbyCode = false;
        private SteamLobbyPrivacy _steamPrivacy = SteamLobbyPrivacy.FriendsOnly;

        private GameObject _hubSection;
        private GameObject _steamSubPanel;
        private GameObject _lanSubPanel;
        private GameObject _connectedSection;

        private GameObject _settingsModalRoot;
        private GameObject _overrideDialogRoot;
        private TMP_Text _overrideBodyText;
        private GameObject _hostClosedDialogRoot;
        private TMP_Text _hostClosedBodyText;

        private Image _tabSteamBg;
        private Image _tabLanBg;
        private Image _modeSharedBg;
        private Image _modeSeparateBg;

        private readonly List<(string locKey, TMP_Text label)> _locBindings = new List<(string, TMP_Text)>();

        private TMP_Text _headerStatusText;

        private TMP_Text _steamAvailabilityText;
        private TMP_Text _privacyBtnLabel;
        private TMP_InputField _steamCodeInput;
        private Transform _steamLobbyListRoot;
        private TMP_InputField _lanNameInput;
        private TMP_InputField _lanIpInput;
        private TMP_InputField _lanPortInput;
        private Transform _lanLobbyListRoot;

        private GameObject _clientReadOnlyBadgeObj;
        private Outline _modeCardOutline;
        private GameObject _hostModeButtonsRow;
        private GameObject _clientModeReadOnlyRow;
        private TMP_Text _clientModeReadOnlyText;
        private TMP_Text _modeRulesDescText;

        private Outline _worldCardOutline;
        private GameObject _worldPrevBtnObj;
        private GameObject _worldNextBtnObj;
        private Image _worldBannerImage;
        private Outline _worldBannerOutline;
        private TMP_Text _worldNameText;
        private TMP_Text _worldMetaBadgeText;
        private TMP_Text _worldDescText;
        private TMP_Text _worldHazardsText;

        private Outline _rulesCardOutline;
        private GameObject _diffPrevBtnObj;
        private GameObject _diffNextBtnObj;
        private TMP_Text _difficultyBadgeText;
        private Image _difficultyBadgeBg;
        private TMP_Text _openRulesBtnLabel;
        private TMP_Text _activeRulesSummaryText;

        private TMP_Text _transportBadgeText;
        private TMP_Text _lobbyCodeText;
        private TMP_Text _hideCodeBtnLabel;
        private GameObject _steamInviteBtnObj;
        private Transform _rosterListRoot;
        private TMP_Text _readySummaryText;

        private TMP_Text _feedText;
        private TMP_InputField _chatInput;

        private GameObject _hostFooterObj;
        private GameObject _clientFooterObj;
        private Button _startRunBtn;
        private Image _startRunBtnBg;
        private TMP_Text _startRunBtnLabel;
        private Button _disbandBtn;
        private Button _readyBtn;
        private Image _readyBtnBg;
        private TMP_Text _readyBtnLabel;
        private Button _leaveBtn;

        private TMP_Text _settingsModalSubtitle;
        private TMP_InputField _presetNameInput;
        private TMP_Text _loadPresetBtnLabel;
        private List<string> _savedPresets = new List<string>();
        private int _selectedSavedPresetIdx = 0;

        private TMP_Text _lblEnemyHp;
        private TMP_Text _lblWaveDensity;
        private TMP_Text _lblEnemySpeed;
        private TMP_Text _lblBossMult;
        private TMP_Text _lblStartGold;
        private TMP_Text _lblWaveIncome;
        private TMP_Text _lblBlockPacing;
        private TMP_Text _lblCoreHp;
        private TMP_Text _lblGifting;
        private TMP_Text _lblDraftSync;
        private TMP_Text _lblSpeedCtrl;
        private TMP_Text _lblAutoScale;
        private readonly List<Button> _hostOnlySettingButtons = new List<Button>();

        private float _modePulseTimer;
        private float _worldPulseTimer;
        private float _rulesPulseTimer;

        private float _lastRefreshTime;
        private bool _dirty = true;

        public bool IsOpen => _isOpen;

        public CoopLobbyPanel(SessionController session)
        {
            _session = session;
            Net.OnRosterUpdated += () => _dirty = true;
            session.OnSettingsChanged += () => _dirty = true;
            session.OnLobbySetupChanged += HandleLobbySetupChangedFeedback;
            CoopLogger.OnFeedEntryAdded += _ => _dirty = true;
            CoopLoc.OnLanguageChanged += OnLanguageChanged;
        }

        private void OnLanguageChanged()
        {
            RefreshStaticLocalizedBindings();
            _dirty = true;
        }

        private void HandleLobbySetupChangedFeedback(LobbySetupChangeMask mask)
        {
            _dirty = true;
            if (!Net.IsClient || !_isOpen || mask == LobbySetupChangeMask.None) return;

            bool pulsed = false;
            if ((mask & LobbySetupChangeMask.CoopMode) != 0) { _modePulseTimer = 0.90f; pulsed = true; }
            if ((mask & LobbySetupChangeMask.World) != 0) { _worldPulseTimer = 0.90f; pulsed = true; }
            if ((mask & (LobbySetupChangeMask.Difficulty | LobbySetupChangeMask.SessionRules)) != 0) { _rulesPulseTimer = 0.90f; pulsed = true; }

            if (pulsed)
                NativeStyleProvider.PlayUiClickSound(1.26f);
        }

        public void Toggle()
        {
            if (!GameRefs.IsInTitle)
            {
                if (_isOpen) ForceCloseForRun();
                return;
            }
            if (_isOpen) Close();
            else Open();
        }

        public void Open()
        {
            if (!GameRefs.IsInTitle) return;

            EnsureBuilt();
            _isOpen = true;
            _root.SetActive(true);
            _dirty = true;
            TitleMenuIntegration.SetMainMenuButtonsVisible(false);
            RefreshSavedPresetsList();
            if (!Net.IsConnected)
            {
                Net.Lan.RefreshLobbyList();
                if (Net.Steam.IsAvailable) Net.Steam.RefreshLobbyList();
            }
            RefreshStaticLocalizedBindings();
            RefreshView();
        }

        public void Close()
        {
            _isOpen = false;
            HideAllSubModals();
            if (_root != null) _root.SetActive(false);
            TitleMenuIntegration.SetMainMenuButtonsVisible(true);
        }

        public void ForceCloseForRun()
        {
            _isOpen = false;
            HideAllSubModals();
            if (_root != null && _root.activeSelf)
                _root.SetActive(false);
            TitleMenuIntegration.SetMainMenuButtonsVisible(true);
        }

        public void ShowHostClosedDialog(string message)
        {
            if (!GameRefs.IsInTitle) return;
            Open();
            HideAllSubModals();
            if (_hostClosedDialogRoot != null)
            {
                if (_hostClosedBodyText != null)
                    _hostClosedBodyText.text = string.IsNullOrWhiteSpace(message) ? CoopLoc.T("lobby.host_closed_body") : message;
                _hostClosedDialogRoot.SetActive(true);
                NativeStyleProvider.PlayUiClickSound(0.84f);
            }
        }

        private void HideAllSubModals()
        {
            if (_settingsModalRoot != null) _settingsModalRoot.SetActive(false);
            if (_overrideDialogRoot != null) _overrideDialogRoot.SetActive(false);
            if (_hostClosedDialogRoot != null) _hostClosedDialogRoot.SetActive(false);
        }

        public void Tick(float now)
        {
            if (!_isOpen || _root == null) return;

            if (!GameRefs.IsInTitle)
            {
                ForceCloseForRun();
                return;
            }

            if (Input.GetKeyDown(KeyCode.Escape) && !(CoopSettingsModal.Instance?.IsOpen ?? false))
            {
                if (_hostClosedDialogRoot != null && _hostClosedDialogRoot.activeSelf)
                    _hostClosedDialogRoot.SetActive(false);
                else if (_overrideDialogRoot != null && _overrideDialogRoot.activeSelf)
                    _overrideDialogRoot.SetActive(false);
                else if (_settingsModalRoot != null && _settingsModalRoot.activeSelf)
                    _settingsModalRoot.SetActive(false);
                else
                    Close();
                return;
            }

            TickPulseHighlights(Time.unscaledDeltaTime);

            if (_dirty || now - _lastRefreshTime >= 0.70f)
            {
                _lastRefreshTime = now;
                _dirty = false;
                RefreshView();
            }
        }

        private void TickPulseHighlights(float dt)
        {
            UpdateCardPulse(ref _modePulseTimer, _modeCardOutline, dt);
            UpdateCardPulse(ref _worldPulseTimer, _worldCardOutline, dt);
            UpdateCardPulse(ref _rulesPulseTimer, _rulesCardOutline, dt);
        }

        private static void UpdateCardPulse(ref float timer, Outline outline, float dt)
        {
            if (outline == null) return;
            if (timer > 0f)
            {
                timer = Mathf.Max(0f, timer - dt);
                float pulse = Mathf.Sin((timer / 0.90f) * Mathf.PI);
                outline.effectColor = Color.Lerp(NativeStyleProvider.EmberBorder, new Color(1f, 0.92f, 0.38f, 1f), pulse);
                float dist = Mathf.Lerp(1.6f, 3.4f, pulse);
                outline.effectDistance = new Vector2(dist, -dist);
            }
            else if (outline.effectDistance.x > 1.7f)
            {
                outline.effectColor = NativeStyleProvider.EmberBorder;
                outline.effectDistance = new Vector2(1.5f, -1.5f);
            }
        }

        private TMP_Text BindLoc(TMP_Text tmp, string key)
        {
            if (tmp != null && !string.IsNullOrEmpty(key))
            {
                tmp.text = CoopLoc.T(key);
                _locBindings.Add((key, tmp));
            }
            return tmp;
        }

        private Button CreateLocButton(Transform parent, string locKey, Color tint, Action onClick, float fontSize = 15f, float height = 36f)
        {
            var btn = NativeStyleProvider.CreateButton(parent, CoopLoc.T(locKey), tint, onClick, fontSize, height);
            BindLoc(btn.GetComponentInChildren<TMP_Text>(), locKey);
            return btn;
        }

        private void RefreshStaticLocalizedBindings()
        {
            for (int i = 0; i < _locBindings.Count; i++)
            {
                var (key, txt) = _locBindings[i];
                if (txt != null) txt.text = CoopLoc.T(key);
            }
            if (_steamCodeInput?.placeholder is TMP_Text sp) sp.text = CoopLoc.T("lobby.paste_code");
            if (_chatInput?.placeholder is TMP_Text cp) cp.text = CoopLoc.T("lobby.chat_placeholder");
            if (_presetNameInput?.placeholder is TMP_Text pp) pp.text = CoopLoc.T("preset.save_placeholder");
        }

        private void EnsureBuilt()
        {
            if (_root != null) return;

            NativeStyleProvider.HarvestFromScene();
            var canvas = NativeStyleProvider.GetOrCreateOverlayCanvas();

            _root = new GameObject("EWCoop_PreRunScreen");
            _root.transform.SetParent(canvas.transform, false);
            NativeStyleProvider.StretchFill(_root.AddComponent<RectTransform>());
            _root.AddComponent<Image>().color = new Color(0.03f, 0.02f, 0.05f, 0.78f);

            var window = NativeStyleProvider.CreatePanel(_root.transform, "PreRunWindow", NativeStyleProvider.PanelBg, addBorder: true);
            var winRt = window.GetComponent<RectTransform>();
            winRt.anchorMin = new Vector2(0.5f, 0.5f);
            winRt.anchorMax = new Vector2(0.5f, 0.5f);
            winRt.sizeDelta = new Vector2(1180f, 780f);

            var vlg = window.AddComponent<VerticalLayoutGroup>();
            vlg.padding = new RectOffset(22, 22, 16, 16);
            vlg.spacing = 12f;
            vlg.childControlWidth = true;
            vlg.childControlHeight = true;
            vlg.childForceExpandWidth = true;
            vlg.childForceExpandHeight = false;

            var headerRow = CreateRow(window.transform, "Header", 42f, 10f);
            var backBtn = CreateLocButton(headerRow.transform, "lobby.back_to_menu", NativeStyleProvider.ButtonSecondary, Close, 14f, 36f);
            backBtn.GetComponent<LayoutElement>().preferredWidth = 175f;

            var title = BindLoc(NativeStyleProvider.CreateLabel(headerRow.transform, "", 25f, NativeStyleProvider.EmberGold, TextAlignmentOptions.Center, FontStyles.Bold), "lobby.title");
            title.gameObject.AddComponent<LayoutElement>().flexibleWidth = 1f;

            _headerStatusText = NativeStyleProvider.CreateLabel(headerRow.transform, "Offline", 13.5f, NativeStyleProvider.TextMuted, TextAlignmentOptions.MidlineRight);
            _headerStatusText.gameObject.AddComponent<LayoutElement>().preferredWidth = 185f;

            var modCfgBtn = CreateLocButton(headerRow.transform, "lobby.settings_btn", NativeStyleProvider.CardBg, () => CoopSettingsModal.Instance?.Open(), 13.5f, 36f);
            modCfgBtn.GetComponent<LayoutElement>().preferredWidth = 138f;

            BuildHubSection(window.transform);
            BuildConnectedLobbySection(window.transform);
            BuildSessionSettingsModal(_root.transform);
            BuildStartOverrideDialog(_root.transform);
            BuildHostClosedDialog(_root.transform);

            SelectTransportTab(SteamP2PTransport.IsSteamReady());
            _root.SetActive(false);
        }

        private void BuildHubSection(Transform parent)
        {
            _hubSection = new GameObject("CoopHubSection");
            _hubSection.transform.SetParent(parent, false);
            _hubSection.AddComponent<LayoutElement>().flexibleHeight = 1f;
            var vlg = _hubSection.AddComponent<VerticalLayoutGroup>();
            vlg.spacing = 10f;
            vlg.childControlWidth = true;
            vlg.childControlHeight = true;
            vlg.childForceExpandWidth = true;
            vlg.childForceExpandHeight = false;

            var tabRow = CreateRow(_hubSection.transform, "TransportTabs", 42f, 12f);
            var steamTabBtn = CreateLocButton(tabRow.transform, "lobby.tab_steam", NativeStyleProvider.ButtonPrimary, () => SelectTransportTab(true), 16f, 40f);
            steamTabBtn.GetComponent<LayoutElement>().flexibleWidth = 1f;
            _tabSteamBg = steamTabBtn.GetComponent<Image>();

            var lanTabBtn = CreateLocButton(tabRow.transform, "lobby.tab_lan", NativeStyleProvider.ButtonSecondary, () => SelectTransportTab(false), 16f, 40f);
            lanTabBtn.GetComponent<LayoutElement>().flexibleWidth = 1f;
            _tabLanBg = lanTabBtn.GetComponent<Image>();

            BuildSteamHubPanel(_hubSection.transform);
            BuildLanHubPanel(_hubSection.transform);
        }

        private void BuildSteamHubPanel(Transform parent)
        {
            _steamSubPanel = NativeStyleProvider.CreatePanel(parent, "SteamHubPanel", NativeStyleProvider.PanelSubBg, addBorder: true);
            _steamSubPanel.AddComponent<LayoutElement>().flexibleHeight = 1f;
            var vlg = _steamSubPanel.AddComponent<VerticalLayoutGroup>();
            vlg.padding = new RectOffset(18, 18, 14, 14);
            vlg.spacing = 10f;
            vlg.childControlWidth = true;
            vlg.childControlHeight = true;
            vlg.childForceExpandWidth = true;
            vlg.childForceExpandHeight = false;

            _steamAvailabilityText = NativeStyleProvider.CreateLabel(_steamSubPanel.transform, "Steam P2P Ready", 15f, NativeStyleProvider.EmberGold);

            var hostRow = CreateRow(_steamSubPanel.transform, "SteamHostRow", 46f, 12f);
            var privBtn = NativeStyleProvider.CreateButton(hostRow.transform, "Privacy: FriendsOnly", NativeStyleProvider.ButtonSecondary, CycleSteamPrivacy, 14f, 42f);
            privBtn.GetComponent<LayoutElement>().preferredWidth = 230f;
            _privacyBtnLabel = privBtn.GetComponentInChildren<TMP_Text>();

            var hostBtn = CreateLocButton(hostRow.transform, "lobby.host_steam", NativeStyleProvider.ButtonPrimary, () => { Net.HostSteam(_steamPrivacy); _dirty = true; }, 16.5f, 42f);
            hostBtn.GetComponent<LayoutElement>().flexibleWidth = 1f;

            var joinRow = CreateRow(_steamSubPanel.transform, "SteamJoinRow", 42f, 12f);
            _steamCodeInput = NativeStyleProvider.CreateInputField(joinRow.transform, CoopLoc.T("lobby.paste_code"), "", 40f);
            _steamCodeInput.GetComponent<LayoutElement>().flexibleWidth = 1f;
            var joinCodeBtn = CreateLocButton(joinRow.transform, "lobby.join_code", NativeStyleProvider.ButtonSuccess, () =>
            {
                string raw = (_steamCodeInput.text ?? "").Trim();
                if (ulong.TryParse(raw, out ulong lid) && lid > 0) Net.JoinSteam(lid);
                else CoopLogger.Warn(LogCategory.UI, "Enter a valid numeric Steam Lobby Code.", addToFeed: true);
            }, 15f, 40f);
            joinCodeBtn.GetComponent<LayoutElement>().preferredWidth = 175f;

            var listHeader = CreateRow(_steamSubPanel.transform, "SteamListHeader", 32f, 8f);
            var lbl = BindLoc(NativeStyleProvider.CreateLabel(listHeader.transform, "", 15.5f, NativeStyleProvider.EmberGold, TextAlignmentOptions.MidlineLeft, FontStyles.Bold), "lobby.steam_lobbies");
            lbl.gameObject.AddComponent<LayoutElement>().flexibleWidth = 1f;
            var refBtn = CreateLocButton(listHeader.transform, "lobby.refresh", NativeStyleProvider.ButtonSecondary, () => { Net.Steam.RefreshLobbyList(); _dirty = true; }, 13.5f, 30f);
            refBtn.GetComponent<LayoutElement>().preferredWidth = 115f;

            var listBox = NativeStyleProvider.CreatePanel(_steamSubPanel.transform, "SteamLobbyList", new Color(0.07f, 0.06f, 0.10f, 0.90f), addBorder: false);
            listBox.AddComponent<LayoutElement>().flexibleHeight = 1f;
            var lv = listBox.AddComponent<VerticalLayoutGroup>();
            lv.padding = new RectOffset(10, 10, 10, 10);
            lv.spacing = 8f;
            lv.childControlWidth = true;
            lv.childControlHeight = true;
            lv.childForceExpandWidth = true;
            lv.childForceExpandHeight = false;
            _steamLobbyListRoot = listBox.transform;
        }

        private void BuildLanHubPanel(Transform parent)
        {
            _lanSubPanel = NativeStyleProvider.CreatePanel(parent, "LanHubPanel", NativeStyleProvider.PanelSubBg, addBorder: true);
            _lanSubPanel.AddComponent<LayoutElement>().flexibleHeight = 1f;
            var vlg = _lanSubPanel.AddComponent<VerticalLayoutGroup>();
            vlg.padding = new RectOffset(18, 18, 14, 14);
            vlg.spacing = 10f;
            vlg.childControlWidth = true;
            vlg.childControlHeight = true;
            vlg.childForceExpandWidth = true;
            vlg.childForceExpandHeight = false;

            var nameRow = CreateRow(_lanSubPanel.transform, "NameRow", 40f, 10f);
            var nameLbl = BindLoc(NativeStyleProvider.CreateLabel(nameRow.transform, "", 15f, NativeStyleProvider.TextLight, TextAlignmentOptions.MidlineLeft), "lobby.player_name");
            nameLbl.gameObject.AddComponent<LayoutElement>().preferredWidth = 150f;
            _lanNameInput = NativeStyleProvider.CreateInputField(nameRow.transform, "Name", Net.LocalPlayerName, 38f);
            _lanNameInput.GetComponent<LayoutElement>().flexibleWidth = 1f;

            var addrRow = CreateRow(_lanSubPanel.transform, "AddrRow", 40f, 10f);
            var ipLbl = BindLoc(NativeStyleProvider.CreateLabel(addrRow.transform, "", 15f, NativeStyleProvider.TextLight, TextAlignmentOptions.MidlineLeft), "lobby.host_ip_port");
            ipLbl.gameObject.AddComponent<LayoutElement>().preferredWidth = 150f;
            _lanIpInput = NativeStyleProvider.CreateInputField(addrRow.transform, "127.0.0.1", "127.0.0.1", 38f);
            _lanIpInput.GetComponent<LayoutElement>().flexibleWidth = 1f;
            _lanPortInput = NativeStyleProvider.CreateInputField(addrRow.transform, "27777", CoopConfig.DefaultLanPort.Value.ToString(), 38f);
            _lanPortInput.GetComponent<LayoutElement>().preferredWidth = 110f;

            var btnRow = CreateRow(_lanSubPanel.transform, "LanActionRow", 46f, 12f);
            var hostLanBtn = CreateLocButton(btnRow.transform, "lobby.host_lan", NativeStyleProvider.ButtonPrimary, () =>
            {
                ApplyLanName();
                Net.HostLan(ParsePort());
                _dirty = true;
            }, 16f, 42f);
            hostLanBtn.GetComponent<LayoutElement>().flexibleWidth = 1f;

            var joinLanBtn = CreateLocButton(btnRow.transform, "lobby.join_lan", NativeStyleProvider.ButtonSuccess, () =>
            {
                ApplyLanName();
                Net.JoinLan((_lanIpInput.text ?? "127.0.0.1").Trim(), ParsePort());
                _dirty = true;
            }, 16f, 42f);
            joinLanBtn.GetComponent<LayoutElement>().flexibleWidth = 1f;

            BindLoc(NativeStyleProvider.CreateLabel(_lanSubPanel.transform, "", 15.5f, NativeStyleProvider.EmberGold, TextAlignmentOptions.MidlineLeft, FontStyles.Bold), "lobby.discovered_lan");

            var listBox = NativeStyleProvider.CreatePanel(_lanSubPanel.transform, "LanLobbyList", new Color(0.07f, 0.06f, 0.10f, 0.90f), addBorder: false);
            listBox.AddComponent<LayoutElement>().flexibleHeight = 1f;
            var lv = listBox.AddComponent<VerticalLayoutGroup>();
            lv.padding = new RectOffset(10, 10, 10, 10);
            lv.spacing = 8f;
            lv.childControlWidth = true;
            lv.childControlHeight = true;
            lv.childForceExpandWidth = true;
            lv.childForceExpandHeight = false;
            _lanLobbyListRoot = listBox.transform;
        }

        private void BuildConnectedLobbySection(Transform parent)
        {
            _connectedSection = new GameObject("ConnectedPreRunLobby");
            _connectedSection.transform.SetParent(parent, false);
            _connectedSection.AddComponent<LayoutElement>().flexibleHeight = 1f;

            var split = _connectedSection.AddComponent<HorizontalLayoutGroup>();
            split.spacing = 14f;
            split.childControlWidth = true;
            split.childControlHeight = true;
            split.childForceExpandWidth = true;
            split.childForceExpandHeight = true;

            BuildLeftSetupColumn(_connectedSection.transform);
            BuildRightPartyColumn(_connectedSection.transform);
        }

        private void BuildLeftSetupColumn(Transform parent)
        {
            var leftCol = new GameObject("LeftMatchSetupCol");
            leftCol.transform.SetParent(parent, false);
            var le = leftCol.AddComponent<LayoutElement>();
            le.flexibleWidth = 1.55f;
            le.minWidth = 650f;

            var vlg = leftCol.AddComponent<VerticalLayoutGroup>();
            vlg.spacing = 10f;
            vlg.childControlWidth = true;
            vlg.childControlHeight = true;
            vlg.childForceExpandWidth = true;
            vlg.childForceExpandHeight = false;

            var hdr = CreateRow(leftCol.transform, "SetupHdr", 28f, 8f);
            var hdrLbl = BindLoc(NativeStyleProvider.CreateLabel(hdr.transform, "", 16f, NativeStyleProvider.EmberGold, TextAlignmentOptions.MidlineLeft, FontStyles.Bold), "lobby.match_setup_title");
            hdrLbl.gameObject.AddComponent<LayoutElement>().flexibleWidth = 1f;

            var roBadge = NativeStyleProvider.CreatePanel(hdr.transform, "ReadOnlyBadge", new Color(0.22f, 0.16f, 0.08f, 0.92f), addBorder: true);
            roBadge.AddComponent<LayoutElement>().preferredWidth = 215f;
            var roTxt = BindLoc(NativeStyleProvider.CreateLabel(roBadge.transform, "", 12f, NativeStyleProvider.EmberGold, TextAlignmentOptions.Center, FontStyles.Bold), "lobby.host_controls_badge");
            NativeStyleProvider.StretchFill(roTxt.rectTransform, 6f, 2f);
            _clientReadOnlyBadgeObj = roBadge;

            var modeCard = NativeStyleProvider.CreatePanel(leftCol.transform, "CoopModeCard", NativeStyleProvider.PanelSubBg, addBorder: true);
            _modeCardOutline = modeCard.GetComponent<Outline>();
            var mv = modeCard.AddComponent<VerticalLayoutGroup>();
            mv.padding = new RectOffset(14, 14, 10, 10);
            mv.spacing = 7f;
            mv.childControlWidth = true;
            mv.childControlHeight = true;
            mv.childForceExpandWidth = true;
            mv.childForceExpandHeight = false;

            BindLoc(NativeStyleProvider.CreateLabel(modeCard.transform, "", 13f, NativeStyleProvider.EmberGold, TextAlignmentOptions.MidlineLeft, FontStyles.Bold), "lobby.mode_label");

            _hostModeButtonsRow = CreateRow(modeCard.transform, "HostModeRow", 38f, 10f);
            var sharedBtn = CreateLocButton(_hostModeButtonsRow.transform, "lobby.mode_shared", NativeStyleProvider.ButtonPrimary, () => HostSetEconomyMode(CoopEconomyMode.SharedResources), 13.5f, 36f);
            sharedBtn.GetComponent<LayoutElement>().flexibleWidth = 1f;
            _modeSharedBg = sharedBtn.GetComponent<Image>();

            var separateBtn = CreateLocButton(_hostModeButtonsRow.transform, "lobby.mode_separate", NativeStyleProvider.ButtonSecondary, () => HostSetEconomyMode(CoopEconomyMode.SeparateResources), 13.5f, 36f);
            separateBtn.GetComponent<LayoutElement>().flexibleWidth = 1f;
            _modeSeparateBg = separateBtn.GetComponent<Image>();

            _clientModeReadOnlyRow = NativeStyleProvider.CreatePanel(modeCard.transform, "ClientModePill", new Color(0.18f, 0.14f, 0.24f, 0.95f), addBorder: false);
            _clientModeReadOnlyRow.AddComponent<LayoutElement>().preferredHeight = 34f;
            _clientModeReadOnlyText = NativeStyleProvider.CreateLabel(_clientModeReadOnlyRow.transform, "", 15f, NativeStyleProvider.EmberGold, TextAlignmentOptions.Center, FontStyles.Bold);
            NativeStyleProvider.StretchFill(_clientModeReadOnlyText.rectTransform, 10f, 2f);

            _modeRulesDescText = NativeStyleProvider.CreateLabel(modeCard.transform, "", 13f, NativeStyleProvider.TextMuted, TextAlignmentOptions.MidlineLeft);
            _modeRulesDescText.enableWordWrapping = true;

            var worldCard = NativeStyleProvider.CreatePanel(leftCol.transform, "LevelPreviewCard", NativeStyleProvider.PanelSubBg, addBorder: true);
            _worldCardOutline = worldCard.GetComponent<Outline>();
            var wv = worldCard.AddComponent<VerticalLayoutGroup>();
            wv.padding = new RectOffset(14, 14, 10, 12);
            wv.spacing = 9f;
            wv.childControlWidth = true;
            wv.childControlHeight = true;
            wv.childForceExpandWidth = true;
            wv.childForceExpandHeight = false;

            var worldTopRow = CreateRow(worldCard.transform, "WorldSelectorRow", 34f, 8f);
            var wTitleLbl = BindLoc(NativeStyleProvider.CreateLabel(worldTopRow.transform, "", 13f, NativeStyleProvider.EmberGold, TextAlignmentOptions.MidlineLeft, FontStyles.Bold), "lobby.select_world");
            wTitleLbl.gameObject.AddComponent<LayoutElement>().flexibleWidth = 1f;

            var prevWorldBtn = NativeStyleProvider.CreateButton(worldTopRow.transform, "<", NativeStyleProvider.ButtonSecondary, () => HostCycleWorld(-1), 16f, 32f);
            prevWorldBtn.GetComponent<LayoutElement>().preferredWidth = 42f;
            _worldPrevBtnObj = prevWorldBtn.gameObject;

            var nextWorldBtn = NativeStyleProvider.CreateButton(worldTopRow.transform, ">", NativeStyleProvider.ButtonSecondary, () => HostCycleWorld(1), 16f, 32f);
            nextWorldBtn.GetComponent<LayoutElement>().preferredWidth = 42f;
            _worldNextBtnObj = nextWorldBtn.gameObject;

            var previewBody = CreateRow(worldCard.transform, "WorldPreviewBody", 136f, 14f);

            var thumbFrame = NativeStyleProvider.CreatePanel(previewBody.transform, "BiomeThumb", new Color(0.06f, 0.05f, 0.09f, 1f), addBorder: true);
            var tle = thumbFrame.AddComponent<LayoutElement>();
            tle.minWidth = 248f; tle.preferredWidth = 248f; tle.minHeight = 128f; tle.preferredHeight = 128f;
            _worldBannerOutline = thumbFrame.GetComponent<Outline>();

            var thumbImgGo = new GameObject("BannerSprite");
            thumbImgGo.transform.SetParent(thumbFrame.transform, false);
            NativeStyleProvider.StretchFill(thumbImgGo.AddComponent<RectTransform>(), 3f, 3f);
            _worldBannerImage = thumbImgGo.AddComponent<Image>();
            _worldBannerImage.preserveAspect = false;

            var metaCol = new GameObject("WorldMeta");
            metaCol.transform.SetParent(previewBody.transform, false);
            metaCol.AddComponent<LayoutElement>().flexibleWidth = 1f;
            var metaV = metaCol.AddComponent<VerticalLayoutGroup>();
            metaV.spacing = 5f;
            metaV.childControlWidth = true;
            metaV.childControlHeight = true;
            metaV.childForceExpandWidth = true;
            metaV.childForceExpandHeight = false;

            _worldNameText = NativeStyleProvider.CreateLabel(metaCol.transform, "", 20f, NativeStyleProvider.EmberGold, TextAlignmentOptions.MidlineLeft, FontStyles.Bold);
            _worldMetaBadgeText = NativeStyleProvider.CreateLabel(metaCol.transform, "", 13f, new Color(0.65f, 0.90f, 1f, 1f), TextAlignmentOptions.MidlineLeft, FontStyles.Bold);
            _worldDescText = NativeStyleProvider.CreateLabel(metaCol.transform, "", 13.5f, NativeStyleProvider.TextLight, TextAlignmentOptions.TopLeft);
            _worldDescText.enableWordWrapping = true;
            _worldHazardsText = NativeStyleProvider.CreateLabel(metaCol.transform, "", 12.5f, NativeStyleProvider.EmberOrange, TextAlignmentOptions.TopLeft, FontStyles.Bold);
            _worldHazardsText.enableWordWrapping = true;

            var rulesCard = NativeStyleProvider.CreatePanel(leftCol.transform, "DifficultyRulesCard", NativeStyleProvider.PanelSubBg, addBorder: true);
            rulesCard.AddComponent<LayoutElement>().flexibleHeight = 1f;
            _rulesCardOutline = rulesCard.GetComponent<Outline>();
            var rv = rulesCard.AddComponent<VerticalLayoutGroup>();
            rv.padding = new RectOffset(14, 14, 10, 12);
            rv.spacing = 9f;
            rv.childControlWidth = true;
            rv.childControlHeight = true;
            rv.childForceExpandWidth = true;
            rv.childForceExpandHeight = false;

            var diffRow = CreateRow(rulesCard.transform, "DifficultyRow", 38f, 8f);
            var diffLbl = BindLoc(NativeStyleProvider.CreateLabel(diffRow.transform, "", 14f, NativeStyleProvider.EmberGold, TextAlignmentOptions.MidlineLeft, FontStyles.Bold), "lobby.select_difficulty");
            diffLbl.gameObject.AddComponent<LayoutElement>().preferredWidth = 115f;

            var dPrev = NativeStyleProvider.CreateButton(diffRow.transform, "<", NativeStyleProvider.ButtonSecondary, () => HostCycleDifficulty(-1), 16f, 34f);
            dPrev.GetComponent<LayoutElement>().preferredWidth = 38f;
            _diffPrevBtnObj = dPrev.gameObject;

            var diffPill = NativeStyleProvider.CreatePanel(diffRow.transform, "DiffPill", NativeStyleProvider.CardBg, addBorder: true);
            diffPill.AddComponent<LayoutElement>().preferredWidth = 160f;
            _difficultyBadgeBg = diffPill.GetComponent<Image>();
            _difficultyBadgeText = NativeStyleProvider.CreateLabel(diffPill.transform, "", 15f, Color.white, TextAlignmentOptions.Center, FontStyles.Bold);
            NativeStyleProvider.StretchFill(_difficultyBadgeText.rectTransform, 6f, 2f);

            var dNext = NativeStyleProvider.CreateButton(diffRow.transform, ">", NativeStyleProvider.ButtonSecondary, () => HostCycleDifficulty(1), 16f, 34f);
            dNext.GetComponent<LayoutElement>().preferredWidth = 38f;
            _diffNextBtnObj = dNext.gameObject;

            var spacer = new GameObject("Spacer");
            spacer.transform.SetParent(diffRow.transform, false);
            spacer.AddComponent<LayoutElement>().flexibleWidth = 1f;

            var rulesModalBtn = NativeStyleProvider.CreateButton(diffRow.transform, CoopLoc.T("lobby.session_settings_btn"), NativeStyleProvider.ButtonPrimary, OpenSessionSettingsModal, 13.5f, 34f);
            rulesModalBtn.GetComponent<LayoutElement>().preferredWidth = 195f;
            _openRulesBtnLabel = rulesModalBtn.GetComponentInChildren<TMP_Text>();

            BindLoc(NativeStyleProvider.CreateLabel(rulesCard.transform, "", 12.5f, NativeStyleProvider.TextMuted, TextAlignmentOptions.MidlineLeft, FontStyles.Bold), "lobby.active_rules_title");

            var summaryBox = NativeStyleProvider.CreatePanel(rulesCard.transform, "ActiveRulesBox", new Color(0.07f, 0.06f, 0.10f, 0.90f), addBorder: false);
            summaryBox.AddComponent<LayoutElement>().flexibleHeight = 1f;
            _activeRulesSummaryText = NativeStyleProvider.CreateLabel(summaryBox.transform, "", 13.5f, NativeStyleProvider.TextLight, TextAlignmentOptions.TopLeft);
            NativeStyleProvider.StretchFill(_activeRulesSummaryText.rectTransform, 12f, 10f);
            _activeRulesSummaryText.enableWordWrapping = true;
        }

        private void BuildRightPartyColumn(Transform parent)
        {
            var rightCol = NativeStyleProvider.CreatePanel(parent, "RightPartyCol", NativeStyleProvider.PanelSubBg, addBorder: true);
            var le = rightCol.AddComponent<LayoutElement>();
            le.flexibleWidth = 1.0f;
            le.minWidth = 400f;

            var vlg = rightCol.AddComponent<VerticalLayoutGroup>();
            vlg.padding = new RectOffset(14, 14, 12, 12);
            vlg.spacing = 9f;
            vlg.childControlWidth = true;
            vlg.childControlHeight = true;
            vlg.childForceExpandWidth = true;
            vlg.childForceExpandHeight = false;

            var connHeader = CreateRow(rightCol.transform, "ConnHeader", 26f, 6f);
            _transportBadgeText = NativeStyleProvider.CreateLabel(connHeader.transform, "ONLINE P2P", 13.5f, NativeStyleProvider.EmberGold, TextAlignmentOptions.MidlineLeft, FontStyles.Bold);
            _transportBadgeText.gameObject.AddComponent<LayoutElement>().flexibleWidth = 1f;

            _readySummaryText = NativeStyleProvider.CreateLabel(connHeader.transform, "", 12.5f, NativeStyleProvider.TextMuted, TextAlignmentOptions.MidlineRight, FontStyles.Bold);

            var codeRow = CreateRow(rightCol.transform, "CodeBar", 32f, 6f);
            _lobbyCodeText = NativeStyleProvider.CreateLabel(codeRow.transform, "Code: -", 13f, NativeStyleProvider.TextLight, TextAlignmentOptions.MidlineLeft);
            _lobbyCodeText.gameObject.AddComponent<LayoutElement>().flexibleWidth = 1f;

            var hideBtn = NativeStyleProvider.CreateButton(codeRow.transform, CoopLoc.T("lobby.hide_code"), NativeStyleProvider.ButtonSecondary, () =>
            {
                _hideLobbyCode = !_hideLobbyCode;
                _dirty = true;
            }, 11.5f, 28f);
            hideBtn.GetComponent<LayoutElement>().preferredWidth = 64f;
            _hideCodeBtnLabel = hideBtn.GetComponentInChildren<TMP_Text>();

            var copyBtn = CreateLocButton(codeRow.transform, "lobby.copy_code", NativeStyleProvider.ButtonSecondary, () =>
            {
                if (!string.IsNullOrEmpty(Net.LobbyCode))
                {
                    CopyToClipboard(Net.LobbyCode);
                    CoopLogger.Info(LogCategory.UI, $"Copied '{Net.LobbyCode}' to clipboard!", addToFeed: true);
                }
            }, 11.5f, 28f);
            copyBtn.GetComponent<LayoutElement>().preferredWidth = 88f;

            var inviteBtn = CreateLocButton(rightCol.transform, "lobby.invite_friends", NativeStyleProvider.CardBg, () => Net.Steam.OpenSteamInviteOverlay(), 13f, 30f);
            _steamInviteBtnObj = inviteBtn.gameObject;

            var rosterBox = NativeStyleProvider.CreatePanel(rightCol.transform, "RosterBox", new Color(0.07f, 0.06f, 0.10f, 0.90f), addBorder: false);
            var rle = rosterBox.AddComponent<LayoutElement>();
            rle.minHeight = 195f;
            rle.preferredHeight = 205f;
            var rv = rosterBox.AddComponent<VerticalLayoutGroup>();
            rv.padding = new RectOffset(10, 10, 8, 8);
            rv.spacing = 6f;
            rv.childControlWidth = true;
            rv.childControlHeight = true;
            rv.childForceExpandWidth = true;
            rv.childForceExpandHeight = false;
            _rosterListRoot = rosterBox.transform;

            var feedBox = NativeStyleProvider.CreatePanel(rightCol.transform, "PreRunFeedBox", new Color(0.06f, 0.05f, 0.09f, 0.92f), addBorder: false);
            var fle = feedBox.AddComponent<LayoutElement>();
            fle.flexibleHeight = 1f;
            fle.minHeight = 120f;

            _feedText = NativeStyleProvider.CreateLabel(feedBox.transform, "", 12.5f, NativeStyleProvider.TextMuted, TextAlignmentOptions.BottomLeft);
            NativeStyleProvider.StretchFill(_feedText.rectTransform, 9f, 6f);
            _feedText.enableWordWrapping = true;

            var chatRow = CreateRow(rightCol.transform, "ChatRow", 34f, 6f);
            _chatInput = NativeStyleProvider.CreateInputField(chatRow.transform, CoopLoc.T("lobby.chat_placeholder"), "", 32f);
            _chatInput.GetComponent<LayoutElement>().flexibleWidth = 1f;
            _chatInput.onSubmit.AddListener((UnityAction<string>)(msg => SubmitChat(msg)));

            var sendBtn = CreateLocButton(chatRow.transform, "lobby.send", NativeStyleProvider.ButtonSecondary, () => SubmitChat(_chatInput.text), 13f, 32f);
            sendBtn.GetComponent<LayoutElement>().preferredWidth = 72f;

            _hostFooterObj = new GameObject("HostFooter");
            _hostFooterObj.transform.SetParent(rightCol.transform, false);
            var hfv = _hostFooterObj.AddComponent<VerticalLayoutGroup>();
            hfv.spacing = 7f;
            hfv.childControlWidth = true;
            hfv.childControlHeight = true;
            hfv.childForceExpandWidth = true;
            hfv.childForceExpandHeight = false;

            _startRunBtn = NativeStyleProvider.CreateButton(_hostFooterObj.transform, CoopLoc.T("lobby.start_run"), NativeStyleProvider.ButtonSuccess, OnHostClickStartRun, 17f, 44f);
            _startRunBtnBg = _startRunBtn.GetComponent<Image>();
            _startRunBtnLabel = _startRunBtn.GetComponentInChildren<TMP_Text>();

            var hostSubRow = CreateRow(_hostFooterObj.transform, "HostSubRow", 34f, 8f);
            var hColorBtn = CreateLocButton(hostSubRow.transform, "lobby.color", NativeStyleProvider.ButtonSecondary, () =>
            {
                Net.SetLocalColor((Net.LocalColorIndex + 1) % CoopConfig.PlayerPalette.Length);
                _dirty = true;
            }, 13f, 34f);
            hColorBtn.GetComponent<LayoutElement>().preferredWidth = 100f;

            _disbandBtn = CreateLocButton(hostSubRow.transform, "lobby.disband_lobby", NativeStyleProvider.ButtonDanger, () =>
            {
                _session.HostDisbandLobby();
                _dirty = true;
            }, 13.5f, 34f);
            _disbandBtn.GetComponent<LayoutElement>().flexibleWidth = 1f;

            _clientFooterObj = new GameObject("ClientFooter");
            _clientFooterObj.transform.SetParent(rightCol.transform, false);
            var cfv = _clientFooterObj.AddComponent<VerticalLayoutGroup>();
            cfv.spacing = 7f;
            cfv.childControlWidth = true;
            cfv.childControlHeight = true;
            cfv.childForceExpandWidth = true;
            cfv.childForceExpandHeight = false;

            _readyBtn = NativeStyleProvider.CreateButton(_clientFooterObj.transform, CoopLoc.T("lobby.set_ready"), NativeStyleProvider.ButtonSuccess, () =>
            {
                if (Setup.IsLaunchingRun) return;
                var me = Net.GetPeer(Net.LocalPlayerId);
                Net.SetLocalReady(!(me?.IsReady ?? false));
                _dirty = true;
            }, 17f, 44f);
            _readyBtnBg = _readyBtn.GetComponent<Image>();
            _readyBtnLabel = _readyBtn.GetComponentInChildren<TMP_Text>();

            var clientSubRow = CreateRow(_clientFooterObj.transform, "ClientSubRow", 34f, 8f);
            var cColorBtn = CreateLocButton(clientSubRow.transform, "lobby.color", NativeStyleProvider.ButtonSecondary, () =>
            {
                Net.SetLocalColor((Net.LocalColorIndex + 1) % CoopConfig.PlayerPalette.Length);
                _dirty = true;
            }, 13f, 34f);
            cColorBtn.GetComponent<LayoutElement>().preferredWidth = 100f;

            _leaveBtn = CreateLocButton(clientSubRow.transform, "lobby.leave", NativeStyleProvider.ButtonDanger, () =>
            {
                Net.Disconnect(intentional: true);
                Close();
            }, 13.5f, 34f);
            _leaveBtn.GetComponent<LayoutElement>().flexibleWidth = 1f;
        }

        private void BuildSessionSettingsModal(Transform parent)
        {
            _settingsModalRoot = new GameObject("SessionSettingsModal");
            _settingsModalRoot.transform.SetParent(parent, false);
            NativeStyleProvider.StretchFill(_settingsModalRoot.AddComponent<RectTransform>());
            _settingsModalRoot.AddComponent<Image>().color = new Color(0.01f, 0.01f, 0.03f, 0.72f);

            var card = NativeStyleProvider.CreatePanel(_settingsModalRoot.transform, "SettingsCard", NativeStyleProvider.PanelBg, addBorder: true);
            var rt = card.GetComponent<RectTransform>();
            rt.anchorMin = new Vector2(0.5f, 0.5f);
            rt.anchorMax = new Vector2(0.5f, 0.5f);
            rt.sizeDelta = new Vector2(880f, 520f);

            var vlg = card.AddComponent<VerticalLayoutGroup>();
            vlg.padding = new RectOffset(20, 20, 14, 14);
            vlg.spacing = 8f;
            vlg.childControlWidth = true;
            vlg.childControlHeight = true;
            vlg.childForceExpandWidth = true;
            vlg.childForceExpandHeight = false;

            var hdr = CreateRow(card.transform, "ModalHdr", 34f, 8f);
            var t = BindLoc(NativeStyleProvider.CreateLabel(hdr.transform, "", 19f, NativeStyleProvider.EmberGold, TextAlignmentOptions.MidlineLeft, FontStyles.Bold), "lobby.subtab_settings");
            t.gameObject.AddComponent<LayoutElement>().flexibleWidth = 1f;
            var xBtn = CreateLocButton(hdr.transform, "modcfg.close", NativeStyleProvider.ButtonSecondary, () => _settingsModalRoot.SetActive(false), 13.5f, 32f);
            xBtn.GetComponent<LayoutElement>().preferredWidth = 96f;

            _settingsModalSubtitle = NativeStyleProvider.CreateLabel(card.transform, "", 13f, NativeStyleProvider.TextMuted);

            var presetRow = CreateRow(card.transform, "PresetsRow", 32f, 6f);
            AddPresetButton(presetRow.transform, "preset.balanced", SessionPreset.BalancedCoop);
            AddPresetButton(presetRow.transform, "preset.casual", SessionPreset.Casual);
            AddPresetButton(presetRow.transform, "preset.hardcore", SessionPreset.TacticalHardcore);
            AddPresetButton(presetRow.transform, "preset.chaos", SessionPreset.Chaos);

            var customPresetRow = CreateRow(card.transform, "CustomPresetBar", 32f, 6f);
            _presetNameInput = NativeStyleProvider.CreateInputField(customPresetRow.transform, CoopLoc.T("preset.save_placeholder"), "", 30f);
            _presetNameInput.GetComponent<LayoutElement>().flexibleWidth = 1f;

            var savePresetBtn = CreateLocButton(customPresetRow.transform, "preset.save", NativeStyleProvider.ButtonSuccess, () =>
            {
                string pname = (_presetNameInput?.text ?? "").Trim();
                if (string.IsNullOrWhiteSpace(pname)) pname = $"Custom_{DateTime.Now:HHmm}";
                if (PresetStorage.SavePreset(pname, Settings))
                {
                    RefreshSavedPresetsList();
                    GameRefs.ShowGameToast(CoopLoc.F("preset.saved_toast", pname), 2.0f);
                    _dirty = true;
                }
            }, 13f, 30f);
            savePresetBtn.GetComponent<LayoutElement>().preferredWidth = 86f;

            var cycleSavedBtn = NativeStyleProvider.CreateButton(customPresetRow.transform, "-", NativeStyleProvider.CardBg, () =>
            {
                RefreshSavedPresetsList();
                if (_savedPresets.Count > 0)
                {
                    _selectedSavedPresetIdx = (_selectedSavedPresetIdx + 1) % _savedPresets.Count;
                    _dirty = true;
                }
            }, 12.5f, 30f);
            cycleSavedBtn.GetComponent<LayoutElement>().preferredWidth = 175f;
            _loadPresetBtnLabel = cycleSavedBtn.GetComponentInChildren<TMP_Text>();

            var loadPresetBtn = CreateLocButton(customPresetRow.transform, "preset.load", NativeStyleProvider.ButtonPrimary, () =>
            {
                if (Net.IsClient || _savedPresets.Count == 0) return;
                string name = _savedPresets[Math.Clamp(_selectedSavedPresetIdx, 0, _savedPresets.Count - 1)];
                if (PresetStorage.LoadPreset(name, Settings))
                {
                    BroadcastRulesChange(LobbySetupChangeMask.SessionRules | LobbySetupChangeMask.CoopMode);
                    GameRefs.ShowGameToast(CoopLoc.F("preset.loaded_toast", name), 2.0f);
                }
            }, 13f, 30f);
            loadPresetBtn.GetComponent<LayoutElement>().preferredWidth = 86f;
            _hostOnlySettingButtons.Add(loadPresetBtn);

            var gridRow = CreateRow(card.transform, "SettingsCols", 275f, 12f);

            var leftCol = NativeStyleProvider.CreatePanel(gridRow.transform, "LeftCol", new Color(0.08f, 0.07f, 0.11f, 0.88f), addBorder: false);
            leftCol.AddComponent<LayoutElement>().flexibleWidth = 1f;
            var lv = leftCol.AddComponent<VerticalLayoutGroup>();
            lv.padding = new RectOffset(10, 10, 8, 8);
            lv.spacing = 6f;
            lv.childControlWidth = true;
            lv.childControlHeight = true;
            lv.childForceExpandWidth = true;
            lv.childForceExpandHeight = false;

            var rightCol = NativeStyleProvider.CreatePanel(gridRow.transform, "RightCol", new Color(0.08f, 0.07f, 0.11f, 0.88f), addBorder: false);
            rightCol.AddComponent<LayoutElement>().flexibleWidth = 1f;
            var rv = rightCol.AddComponent<VerticalLayoutGroup>();
            rv.padding = new RectOffset(10, 10, 8, 8);
            rv.spacing = 6f;
            rv.childControlWidth = true;
            rv.childControlHeight = true;
            rv.childForceExpandWidth = true;
            rv.childForceExpandHeight = false;

            _lblEnemyHp = AddStepperRow(leftCol.transform, "settings.enemy_hp", () => AdjustFloat(ref Settings.EnemyHpMultiplier, -0.10f, 1.0f, 2.5f), () => AdjustFloat(ref Settings.EnemyHpMultiplier, 0.10f, 1.0f, 2.5f));
            _lblWaveDensity = AddStepperRow(leftCol.transform, "settings.wave_density", () => AdjustFloat(ref Settings.WaveDensityMultiplier, -0.10f, 1.0f, 2.0f), () => AdjustFloat(ref Settings.WaveDensityMultiplier, 0.10f, 1.0f, 2.0f));
            _lblEnemySpeed = AddStepperRow(leftCol.transform, "settings.enemy_speed", () => AdjustFloat(ref Settings.EnemySpeedMultiplier, -0.05f, 0.8f, 1.5f), () => AdjustFloat(ref Settings.EnemySpeedMultiplier, 0.05f, 0.8f, 1.5f));
            _lblBossMult = AddStepperRow(leftCol.transform, "settings.boss_mult", () => AdjustFloat(ref Settings.BossHpArmorMultiplier, -0.15f, 1.0f, 3.0f), () => AdjustFloat(ref Settings.BossHpArmorMultiplier, 0.15f, 1.0f, 3.0f));
            _lblCoreHp = AddToggleRow(leftCol.transform, "settings.core_hp", () =>
            {
                Settings.CoreHp = Settings.CoreHp == CoreHpRule.SharedSingleCore ? CoreHpRule.ScaledBonusCore : CoreHpRule.SharedSingleCore;
                MarkCustomAndBroadcast();
            });
            _lblAutoScale = AddToggleRow(leftCol.transform, "settings.auto_scale", () =>
            {
                Settings.AutoScaleByPlayerCount = !Settings.AutoScaleByPlayerCount;
                if (Settings.AutoScaleByPlayerCount) Settings.OnPlayerCountChanged(Math.Max(2, Net.Roster.Count));
                BroadcastRulesChange(LobbySetupChangeMask.SessionRules);
            });

            _lblStartGold = AddToggleRow(rightCol.transform, "settings.start_gold", () =>
            {
                Settings.StartingGold = Settings.StartingGold == StartingGoldRule.BalancedSplit ? StartingGoldRule.FullPerPlayer : StartingGoldRule.BalancedSplit;
                MarkCustomAndBroadcast();
            });
            _lblWaveIncome = AddStepperRow(rightCol.transform, "settings.wave_income", () => AdjustFloat(ref Settings.WaveIncomeMultiplier, -0.05f, 0.50f, 1.50f), () => AdjustFloat(ref Settings.WaveIncomeMultiplier, 0.05f, 0.50f, 1.50f));
            _lblBlockPacing = AddStepperRow(rightCol.transform, "settings.block_pacing", () =>
            {
                if (Settings.BlockPacing == BlockPacingRule.IndependentHandCap) Settings.BlockPacing = BlockPacingRule.SharedTurnBlockCap;
                else if (Settings.SharedTurnBlockCap > 2) Settings.SharedTurnBlockCap--;
                else Settings.BlockPacing = BlockPacingRule.IndependentHandCap;
                MarkCustomAndBroadcast();
            }, () =>
            {
                if (Settings.BlockPacing == BlockPacingRule.IndependentHandCap) { Settings.BlockPacing = BlockPacingRule.SharedTurnBlockCap; Settings.SharedTurnBlockCap = 3; }
                else if (Settings.SharedTurnBlockCap < 8) Settings.SharedTurnBlockCap++;
                else Settings.BlockPacing = BlockPacingRule.IndependentHandCap;
                MarkCustomAndBroadcast();
            });
            _lblGifting = AddToggleRow(rightCol.transform, "settings.gifting", () =>
            {
                if (!Settings.AllowResourceGifting) { Settings.AllowResourceGifting = true; Settings.GiftPerWaveCap = 0; }
                else if (Settings.GiftPerWaveCap == 0) Settings.GiftPerWaveCap = 100;
                else if (Settings.GiftPerWaveCap == 100) Settings.GiftPerWaveCap = 200;
                else Settings.AllowResourceGifting = false;
                MarkCustomAndBroadcast();
            });
            _lblDraftSync = AddToggleRow(rightCol.transform, "settings.draft_sync", () =>
            {
                Settings.DraftMode = Settings.DraftMode == DraftSyncMode.SimultaneousIndependent ? DraftSyncMode.HostSharedDraft : DraftSyncMode.SimultaneousIndependent;
                MarkCustomAndBroadcast();
            });
            _lblSpeedCtrl = AddToggleRow(rightCol.transform, "settings.speed_ctrl", () =>
            {
                Settings.SpeedControl = Settings.SpeedControl == SpeedControlRule.AnyPlayer ? SpeedControlRule.HostOnly : SpeedControlRule.AnyPlayer;
                MarkCustomAndBroadcast();
            });

            _settingsModalRoot.SetActive(false);
        }

        private void BuildStartOverrideDialog(Transform parent)
        {
            _overrideDialogRoot = new GameObject("StartOverrideDialog");
            _overrideDialogRoot.transform.SetParent(parent, false);
            NativeStyleProvider.StretchFill(_overrideDialogRoot.AddComponent<RectTransform>());
            _overrideDialogRoot.AddComponent<Image>().color = new Color(0.01f, 0.01f, 0.03f, 0.72f);

            var card = NativeStyleProvider.CreatePanel(_overrideDialogRoot.transform, "OverrideCard", NativeStyleProvider.PanelBg, addBorder: true);
            var rt = card.GetComponent<RectTransform>();
            rt.anchorMin = new Vector2(0.5f, 0.5f);
            rt.anchorMax = new Vector2(0.5f, 0.5f);
            rt.sizeDelta = new Vector2(470f, 195f);

            var vlg = card.AddComponent<VerticalLayoutGroup>();
            vlg.padding = new RectOffset(22, 22, 18, 18);
            vlg.spacing = 12f;
            vlg.childControlWidth = true;
            vlg.childControlHeight = true;
            vlg.childForceExpandWidth = true;
            vlg.childForceExpandHeight = false;

            BindLoc(NativeStyleProvider.CreateLabel(card.transform, "", 19f, NativeStyleProvider.EmberGold, TextAlignmentOptions.Center, FontStyles.Bold), "lobby.start_run_override_title");
            _overrideBodyText = NativeStyleProvider.CreateLabel(card.transform, "", 14f, NativeStyleProvider.TextLight, TextAlignmentOptions.Center);
            _overrideBodyText.enableWordWrapping = true;

            var row = CreateRow(card.transform, "Btns", 40f, 12f);
            var cancelBtn = CreateLocButton(row.transform, "lobby.cancel", NativeStyleProvider.ButtonSecondary, () => _overrideDialogRoot.SetActive(false), 14f, 38f);
            cancelBtn.GetComponent<LayoutElement>().flexibleWidth = 1f;

            var forceBtn = CreateLocButton(row.transform, "lobby.start_anyway", NativeStyleProvider.ButtonPrimary, () =>
            {
                _overrideDialogRoot.SetActive(false);
                _session.HostLaunchCoopRun();
            }, 14f, 38f);
            forceBtn.GetComponent<LayoutElement>().flexibleWidth = 1f;

            _overrideDialogRoot.SetActive(false);
        }

        private void BuildHostClosedDialog(Transform parent)
        {
            _hostClosedDialogRoot = new GameObject("HostClosedDialog");
            _hostClosedDialogRoot.transform.SetParent(parent, false);
            NativeStyleProvider.StretchFill(_hostClosedDialogRoot.AddComponent<RectTransform>());
            _hostClosedDialogRoot.AddComponent<Image>().color = new Color(0.01f, 0.01f, 0.03f, 0.76f);

            var card = NativeStyleProvider.CreatePanel(_hostClosedDialogRoot.transform, "ClosedCard", NativeStyleProvider.PanelBg, addBorder: true);
            var rt = card.GetComponent<RectTransform>();
            rt.anchorMin = new Vector2(0.5f, 0.5f);
            rt.anchorMax = new Vector2(0.5f, 0.5f);
            rt.sizeDelta = new Vector2(450f, 185f);

            var vlg = card.AddComponent<VerticalLayoutGroup>();
            vlg.padding = new RectOffset(22, 22, 18, 18);
            vlg.spacing = 12f;
            vlg.childControlWidth = true;
            vlg.childControlHeight = true;
            vlg.childForceExpandWidth = true;
            vlg.childForceExpandHeight = false;

            BindLoc(NativeStyleProvider.CreateLabel(card.transform, "", 19f, NativeStyleProvider.EmberOrange, TextAlignmentOptions.Center, FontStyles.Bold), "lobby.host_closed_title");
            _hostClosedBodyText = NativeStyleProvider.CreateLabel(card.transform, CoopLoc.T("lobby.host_closed_body"), 14.5f, NativeStyleProvider.TextLight, TextAlignmentOptions.Center);
            _hostClosedBodyText.enableWordWrapping = true;

            var okBtn = CreateLocButton(card.transform, "lobby.ok", NativeStyleProvider.ButtonPrimary, () => _hostClosedDialogRoot.SetActive(false), 15f, 38f);
            okBtn.GetComponent<LayoutElement>().minHeight = 38f;

            _hostClosedDialogRoot.SetActive(false);
        }

        private void HostSetEconomyMode(CoopEconomyMode mode)
        {
            if (!Net.IsHost || Setup.IsLaunchingRun) return;
            if (Settings.EconomyMode == mode) return;
            Settings.EconomyMode = mode;
            _session.HostBroadcastLobbySetup(LobbySetupChangeMask.CoopMode);
        }

        private void HostCycleWorld(int delta)
        {
            if (!Net.IsHost || Setup.IsLaunchingRun) return;
            var worlds = WorldCatalog.GetHostSelectableWorlds(Setup.DifficultyTier);
            if (worlds.Count == 0) return;

            int curIdx = 0;
            for (int i = 0; i < worlds.Count; i++)
                if (worlds[i].WorldType == Setup.SelectedWorld) { curIdx = i; break; }

            int nextIdx = ((curIdx + delta) % worlds.Count + worlds.Count) % worlds.Count;
            Setup.SelectedWorld = worlds[nextIdx].WorldType;
            _session.HostBroadcastLobbySetup(LobbySetupChangeMask.World);
        }

        private void HostCycleDifficulty(int delta)
        {
            if (!Net.IsHost || Setup.IsLaunchingRun) return;
            var diffs = WorldCatalog.AllDifficulties;
            int curIdx = Array.IndexOf(diffs, Setup.DifficultyTier);
            if (curIdx < 0) curIdx = 1;

            int nextIdx = ((curIdx + delta) % diffs.Length + diffs.Length) % diffs.Length;
            Setup.DifficultyTier = diffs[nextIdx];

            // Ensure SelectedWorld remains unlocked for the new difficulty
            var unlocked = WorldCatalog.GetHostSelectableWorlds(Setup.DifficultyTier);
            bool stillValid = false;
            for (int i = 0; i < unlocked.Count; i++)
                if (unlocked[i].WorldType == Setup.SelectedWorld) { stillValid = true; break; }
            if (!stillValid && unlocked.Count > 0)
                Setup.SelectedWorld = unlocked[0].WorldType;

            _session.HostBroadcastLobbySetup(LobbySetupChangeMask.Difficulty | (stillValid ? LobbySetupChangeMask.None : LobbySetupChangeMask.World));
        }

        private void OnHostClickStartRun()
        {
            if (!Net.IsHost || Setup.IsLaunchingRun) return;

            bool allReady = Net.AreAllClientsReady(out int readyCount, out int totalClients);
            if (allReady)
            {
                _session.HostLaunchCoopRun();
            }
            else if (_overrideDialogRoot != null)
            {
                if (_overrideBodyText != null)
                    _overrideBodyText.text = CoopLoc.F("lobby.start_run_override_body", readyCount, totalClients);
                _overrideDialogRoot.SetActive(true);
            }
        }

        private void OpenSessionSettingsModal()
        {
            if (_settingsModalRoot == null) return;
            RefreshSavedPresetsList();
            RefreshSettingsLabels();
            _settingsModalRoot.SetActive(true);
        }

        private void RefreshSavedPresetsList()
        {
            _savedPresets = PresetStorage.ListSavedPresets();
            if (_selectedSavedPresetIdx >= _savedPresets.Count) _selectedSavedPresetIdx = 0;
        }

        private void AddPresetButton(Transform parent, string locKey, SessionPreset preset)
        {
            var btn = CreateLocButton(parent, locKey, NativeStyleProvider.CardBg, () =>
            {
                if (Net.IsClient || Setup.IsLaunchingRun) return;
                Settings.ApplyPreset(preset, Math.Max(2, Net.Roster.Count));
                BroadcastRulesChange(LobbySetupChangeMask.SessionRules);
            }, 13f, 30f);
            btn.GetComponent<LayoutElement>().flexibleWidth = 1f;
            _hostOnlySettingButtons.Add(btn);
        }

        private TMP_Text AddStepperRow(Transform parent, string locKey, Action onMinus, Action onPlus)
        {
            var row = CreateRow(parent, locKey, 36f, 4f);
            var lbl = BindLoc(NativeStyleProvider.CreateLabel(row.transform, "", 13f, NativeStyleProvider.TextMuted, TextAlignmentOptions.MidlineLeft), locKey);
            lbl.gameObject.AddComponent<LayoutElement>().flexibleWidth = 1f;

            var minus = NativeStyleProvider.CreateButton(row.transform, "-", NativeStyleProvider.ButtonSecondary, () => { if (Net.IsHost && !Setup.IsLaunchingRun) onMinus(); }, 15f, 30f);
            minus.GetComponent<LayoutElement>().preferredWidth = 30f;
            _hostOnlySettingButtons.Add(minus);

            var val = NativeStyleProvider.CreateLabel(row.transform, "", 13f, NativeStyleProvider.TextLight, TextAlignmentOptions.Center, FontStyles.Bold);
            val.gameObject.AddComponent<LayoutElement>().preferredWidth = 108f;

            var plus = NativeStyleProvider.CreateButton(row.transform, "+", NativeStyleProvider.ButtonSecondary, () => { if (Net.IsHost && !Setup.IsLaunchingRun) onPlus(); }, 15f, 30f);
            plus.GetComponent<LayoutElement>().preferredWidth = 30f;
            _hostOnlySettingButtons.Add(plus);
            return val;
        }

        private TMP_Text AddToggleRow(Transform parent, string locKey, Action onToggle)
        {
            var row = CreateRow(parent, locKey, 36f, 6f);
            var lbl = BindLoc(NativeStyleProvider.CreateLabel(row.transform, "", 13f, NativeStyleProvider.TextMuted, TextAlignmentOptions.MidlineLeft), locKey);
            lbl.gameObject.AddComponent<LayoutElement>().flexibleWidth = 1f;

            var btn = NativeStyleProvider.CreateButton(row.transform, "", NativeStyleProvider.ButtonSecondary, () => { if (Net.IsHost && !Setup.IsLaunchingRun) onToggle(); }, 12.5f, 30f);
            btn.GetComponent<LayoutElement>().preferredWidth = 172f;
            _hostOnlySettingButtons.Add(btn);
            return btn.GetComponentInChildren<TMP_Text>();
        }

        private void AdjustFloat(ref float field, float delta, float min, float max)
        {
            field = Mathf.Clamp((float)Math.Round(field + delta, 2), min, max);
            MarkCustomAndBroadcast();
        }

        private void MarkCustomAndBroadcast()
        {
            Settings.Preset = SessionPreset.Custom;
            BroadcastRulesChange(LobbySetupChangeMask.SessionRules);
        }

        private void BroadcastRulesChange(LobbySetupChangeMask mask)
        {
            if (Net.IsHost) _session.HostBroadcastLobbySetup(mask);
            _dirty = true;
        }

        private void SubmitChat(string text)
        {
            if (string.IsNullOrWhiteSpace(text)) return;
            _session.SendChat(text);
            if (_chatInput != null) _chatInput.text = "";
            _dirty = true;
        }

        private void SelectTransportTab(bool steam)
        {
            if (_steamSubPanel != null) _steamSubPanel.SetActive(steam);
            if (_lanSubPanel != null) _lanSubPanel.SetActive(!steam);
            if (_tabSteamBg != null) _tabSteamBg.color = steam ? NativeStyleProvider.ButtonPrimary : NativeStyleProvider.ButtonSecondary;
            if (_tabLanBg != null) _tabLanBg.color = !steam ? NativeStyleProvider.ButtonPrimary : NativeStyleProvider.ButtonSecondary;
        }

        private void CycleSteamPrivacy()
        {
            _steamPrivacy = (SteamLobbyPrivacy)(((int)_steamPrivacy + 1) % 3);
            if (_privacyBtnLabel != null) _privacyBtnLabel.text = $"Privacy: {_steamPrivacy}";
        }

        private void ApplyLanName()
        {
            string name = (_lanNameInput?.text ?? "").Trim();
            if (!string.IsNullOrWhiteSpace(name))
            {
                Net.LocalPlayerName = name;
                CoopConfig.PlayerName.Value = name;
            }
        }

        private int ParsePort()
            => int.TryParse(_lanPortInput?.text, out int p) && p >= 1 && p <= 65535 ? p : CoopConfig.DefaultLanPort.Value;

        private void RefreshView()
        {
            bool connected = Net.IsConnected;
            bool isHost = Net.IsHost;
            bool isClient = Net.IsClient;
            bool launching = Setup.IsLaunchingRun;
            bool hostEditable = isHost && !launching;

            _hubSection.SetActive(!connected);
            _connectedSection.SetActive(connected);
            _headerStatusText.text = launching ? CoopLoc.T("lobby.launching_run") : Net.StatusText;

            for (int i = 0; i < _hostOnlySettingButtons.Count; i++)
                if (_hostOnlySettingButtons[i] != null) _hostOnlySettingButtons[i].interactable = hostEditable;

            if (!connected)
            {
                bool steamReady = SteamP2PTransport.IsSteamReady();
                _steamAvailabilityText.text = steamReady
                    ? $"Steam: {SteamP2PTransport.GetLocalSteamName()} (P2P Relay)"
                    : "Steam offline — Direct LAN ready";
                _steamAvailabilityText.color = steamReady ? NativeStyleProvider.EmberGold : new Color(0.95f, 0.45f, 0.40f, 1f);

                RebuildLobbyList(_steamLobbyListRoot, Net.Steam.GetDiscoveredLobbies(), isSteam: true);
                RebuildLobbyList(_lanLobbyListRoot, Net.Lan.GetDiscoveredLobbies(), isSteam: false);
                return;
            }

            _clientReadOnlyBadgeObj.SetActive(isClient);
            _hostModeButtonsRow.SetActive(isHost);
            _clientModeReadOnlyRow.SetActive(isClient);
            _worldPrevBtnObj.SetActive(hostEditable);
            _worldNextBtnObj.SetActive(hostEditable);
            _diffPrevBtnObj.SetActive(hostEditable);
            _diffNextBtnObj.SetActive(hostEditable);

            bool isSeparate = Settings.EconomyMode == CoopEconomyMode.SeparateResources;
            if (_modeSharedBg != null) _modeSharedBg.color = !isSeparate ? NativeStyleProvider.ButtonPrimary : NativeStyleProvider.ButtonSecondary;
            if (_modeSeparateBg != null) _modeSeparateBg.color = isSeparate ? NativeStyleProvider.ButtonPrimary : NativeStyleProvider.ButtonSecondary;
            if (_clientModeReadOnlyText != null)
                _clientModeReadOnlyText.text = isSeparate ? CoopLoc.T("lobby.mode_separate") : CoopLoc.T("lobby.mode_shared");
            if (_modeRulesDescText != null)
                _modeRulesDescText.text = isSeparate ? CoopLoc.T("lobby.mode_separate_desc") : CoopLoc.T("lobby.mode_shared_desc");

            var worldInfo = WorldCatalog.GetWorldInfo(Setup.SelectedWorld);
            Color worldAccent = WorldCatalog.GetWorldAccentColor(Setup.SelectedWorld);
            if (_worldBannerImage != null) _worldBannerImage.sprite = WorldArtworkGenerator.GetBanner(Setup.SelectedWorld);
            if (_worldBannerOutline != null) _worldBannerOutline.effectColor = worldAccent;

            bool cleared = false;
            try
            {
                var pld = GameDataManager.instance?.Playerdata;
                if (pld != null) cleared = pld.IsWorldCleared(Setup.DifficultyTier, Setup.SelectedWorld);
            }
            catch { }

            _worldNameText.text = cleared
                ? $"{CoopLoc.T(worldInfo.NameKey)}  ✓"
                : CoopLoc.T(worldInfo.NameKey);
            _worldNameText.color = worldAccent;
            _worldMetaBadgeText.text = CoopLoc.F("world.meta_format", worldInfo.MapSteps, worldInfo.WavesLabel);
            _worldDescText.text = CoopLoc.T(worldInfo.DescKey);
            _worldHazardsText.text = CoopLoc.T(worldInfo.HazardsKey);

            Color diffCol = WorldCatalog.GetDifficultyColor(Setup.DifficultyTier);
            _difficultyBadgeText.text = WorldCatalog.GetDifficultyDisplayName(Setup.DifficultyTier);
            _difficultyBadgeText.color = Color.white;
            if (_difficultyBadgeBg != null)
                _difficultyBadgeBg.color = new Color(diffCol.r * 0.48f, diffCol.g * 0.48f, diffCol.b * 0.48f, 0.96f);

            if (_openRulesBtnLabel != null)
                _openRulesBtnLabel.text = isHost ? CoopLoc.T("lobby.session_settings_btn") : CoopLoc.T("lobby.view_rules_btn");

            _activeRulesSummaryText.text = WorldCatalog.BuildActiveRulesSummary(Setup);

            string transLabel = Net.ActiveTransportKind == TransportKind.SteamP2P ? CoopLoc.T("lobby.tab_steam") : CoopLoc.T("lobby.tab_lan");
            _transportBadgeText.text = transLabel;

            string rawCode = string.IsNullOrEmpty(Net.LobbyCode) ? "Local" : Net.LobbyCode;
            _lobbyCodeText.text = _hideLobbyCode ? "••••••••••" : rawCode;
            if (_hideCodeBtnLabel != null)
                _hideCodeBtnLabel.text = _hideLobbyCode ? CoopLoc.T("lobby.show_code") : CoopLoc.T("lobby.hide_code");
            _steamInviteBtnObj.SetActive(isHost && Net.ActiveTransportKind == TransportKind.SteamP2P);

            bool allReady = Net.AreAllClientsReady(out int readyClients, out int totalClients);
            if (_readySummaryText != null)
            {
                _readySummaryText.text = totalClients == 0
                    ? CoopLoc.T("lobby.all_ready")
                    : (allReady ? CoopLoc.T("lobby.all_ready") : CoopLoc.F("lobby.waiting_clients_ready", readyClients, totalClients));
                _readySummaryText.color = allReady ? new Color(0.40f, 0.96f, 0.56f, 1f) : NativeStyleProvider.EmberGold;
            }

            _hostFooterObj.SetActive(isHost);
            _clientFooterObj.SetActive(isClient);

            if (isHost)
            {
                if (_startRunBtn != null) _startRunBtn.interactable = !launching;
                if (_disbandBtn != null) _disbandBtn.interactable = !launching;
                if (_startRunBtnBg != null)
                    _startRunBtnBg.color = allReady ? NativeStyleProvider.ButtonSuccess : NativeStyleProvider.ButtonPrimary;
                if (_startRunBtnLabel != null)
                    _startRunBtnLabel.text = launching ? CoopLoc.T("lobby.launching_run") : CoopLoc.T("lobby.start_run");
            }
            else if (isClient)
            {
                var me = Net.GetPeer(Net.LocalPlayerId);
                bool amReady = me?.IsReady ?? false;
                if (_readyBtn != null) _readyBtn.interactable = !launching;
                if (_leaveBtn != null) _leaveBtn.interactable = !launching;
                if (_readyBtnBg != null)
                    _readyBtnBg.color = amReady ? NativeStyleProvider.ButtonSuccess : NativeStyleProvider.ButtonPrimary;
                if (_readyBtnLabel != null)
                    _readyBtnLabel.text = launching
                        ? CoopLoc.T("lobby.launching_run")
                        : (amReady ? CoopLoc.T("lobby.ready") : CoopLoc.T("lobby.set_ready"));
            }

            RebuildRosterList();
            if (_settingsModalRoot != null && _settingsModalRoot.activeSelf)
                RefreshSettingsLabels();

            var entries = CoopLogger.GetFeedSnapshot();
            int start = Math.Max(0, entries.Count - 7);
            _feedText.text = string.Join("\n", entries.GetRange(start, entries.Count - start));
        }

        private void RefreshSettingsLabels()
        {
            string modeNote = Net.IsClient ? $" ({CoopLoc.T("lobby.host_controls_badge")})" : "";
            _settingsModalSubtitle.text = $"{WorldCatalog.GetPresetDisplayName(Settings.Preset)}  ·  {Settings.GetCompactSummary()}{modeNote}";

            if (_loadPresetBtnLabel != null)
            {
                _loadPresetBtnLabel.text = _savedPresets.Count > 0
                    ? _savedPresets[Math.Clamp(_selectedSavedPresetIdx, 0, _savedPresets.Count - 1)]
                    : "-";
            }

            _lblEnemyHp.text = $"{Mathf.RoundToInt(Settings.EnemyHpMultiplier * 100)}%";
            _lblWaveDensity.text = $"{Settings.WaveDensityMultiplier:0.00}x";
            _lblEnemySpeed.text = $"{Settings.EnemySpeedMultiplier:0.00}x";
            _lblBossMult.text = $"{Settings.BossHpArmorMultiplier:0.00}x";
            _lblCoreHp.text = Settings.CoreHp == CoreHpRule.SharedSingleCore ? CoopLoc.T("settings.val_shared_core") : CoopLoc.T("settings.val_scaled_core");
            _lblAutoScale.text = Settings.AutoScaleByPlayerCount ? CoopLoc.T("settings.val_auto") : CoopLoc.T("settings.val_manual");

            _lblStartGold.text = Settings.StartingGold == StartingGoldRule.BalancedSplit ? CoopLoc.T("settings.val_balanced_split") : CoopLoc.T("settings.val_full_each");
            _lblWaveIncome.text = $"{Mathf.RoundToInt(Settings.WaveIncomeMultiplier * 100)}%";
            _lblBlockPacing.text = Settings.BlockPacing == BlockPacingRule.SharedTurnBlockCap
                ? $"Cap: {Settings.SharedTurnBlockCap}"
                : CoopLoc.T("settings.val_independent");
            _lblGifting.text = !Settings.AllowResourceGifting
                ? CoopLoc.T("settings.val_disabled")
                : (Settings.GiftPerWaveCap > 0 ? $"{Settings.GiftPerWaveCap}g/Wave" : CoopLoc.T("settings.val_unlimited"));
            _lblDraftSync.text = Settings.DraftMode == DraftSyncMode.SimultaneousIndependent ? CoopLoc.T("settings.val_simultaneous") : CoopLoc.T("settings.val_host_shared");
            _lblSpeedCtrl.text = Settings.SpeedControl == SpeedControlRule.AnyPlayer ? CoopLoc.T("settings.val_any_player") : CoopLoc.T("settings.val_host_only");
        }

        private void RebuildLobbyList(Transform container, IReadOnlyList<LobbySummary> lobbies, bool isSteam)
        {
            ClearChildren(container);
            if (lobbies == null || lobbies.Count == 0)
            {
                NativeStyleProvider.CreateLabel(container,
                    isSteam ? CoopLoc.T("lobby.no_steam_lobbies") : CoopLoc.T("lobby.listening_lan"),
                    13.5f, NativeStyleProvider.TextMuted, TextAlignmentOptions.MidlineLeft);
                return;
            }

            int shown = Math.Min(6, lobbies.Count);
            for (int i = 0; i < shown; i++)
            {
                var lobby = lobbies[i];
                string label = $"JOIN  {lobby.HostName}  ·  {lobby.SceneName}  ({lobby.PlayerCount}/{lobby.MaxPlayers})";
                var btn = NativeStyleProvider.CreateButton(container, label, NativeStyleProvider.CardBg, () =>
                {
                    if (isSteam) Net.JoinSteam(lobby.SteamLobbyId);
                    else { ApplyLanName(); Net.JoinLan(lobby.AddressOrCode.Split(':')[0], lobby.Port); }
                    _dirty = true;
                }, 14.5f, 38f);
                btn.GetComponent<LayoutElement>().flexibleWidth = 1f;
            }
        }

        private void RebuildRosterList()
        {
            ClearChildren(_rosterListRoot);
            foreach (var kv in Net.Roster)
            {
                var p = kv.Value;
                var rowBg = NativeStyleProvider.CreatePanel(_rosterListRoot, $"Peer_{p.PlayerId}", new Color(0.12f, 0.11f, 0.17f, 0.90f), addBorder: false);
                var rle = rowBg.AddComponent<LayoutElement>();
                rle.minHeight = 34f; rle.preferredHeight = 34f;

                var hlg = rowBg.AddComponent<HorizontalLayoutGroup>();
                hlg.padding = new RectOffset(8, 8, 4, 4);
                hlg.spacing = 8f;
                hlg.childAlignment = TextAnchor.MiddleLeft;
                hlg.childControlWidth = true;
                hlg.childControlHeight = true;
                hlg.childForceExpandWidth = false;

                var swatch = new GameObject("Swatch");
                swatch.transform.SetParent(rowBg.transform, false);
                var sle = swatch.AddComponent<LayoutElement>();
                sle.minWidth = 12f; sle.preferredWidth = 12f; sle.minHeight = 20f; sle.preferredHeight = 20f;
                swatch.AddComponent<Image>().color = CoopConfig.GetPlayerColor(p.ColorIndex);

                string crown = p.IsHost ? "★ " : "";
                Color nameCol = p.IsHost ? NativeStyleProvider.EmberGold : NativeStyleProvider.TextLight;
                var nLbl = NativeStyleProvider.CreateLabel(rowBg.transform, $"{crown}{p.Name}", 14f, nameCol, TextAlignmentOptions.MidlineLeft, FontStyles.Bold);
                nLbl.gameObject.AddComponent<LayoutElement>().flexibleWidth = 1f;

                string statusStr = p.IsHost ? "HOST" : (p.IsReady ? CoopLoc.T("lobby.ready") : CoopLoc.T("lobby.not_ready"));
                Color statusCol = p.IsHost
                    ? NativeStyleProvider.EmberGold
                    : (p.IsReady ? new Color(0.38f, 0.95f, 0.54f, 1f) : new Color(0.95f, 0.62f, 0.28f, 1f));
                var sLbl = NativeStyleProvider.CreateLabel(rowBg.transform, statusStr, 12f, statusCol, TextAlignmentOptions.MidlineRight, FontStyles.Bold);
                sLbl.gameObject.AddComponent<LayoutElement>().preferredWidth = 88f;

                string ping = p.IsHost ? "" : $"{p.PingMs}ms";
                var pLbl = NativeStyleProvider.CreateLabel(rowBg.transform, ping, 11.5f, NativeStyleProvider.TextMuted, TextAlignmentOptions.MidlineRight);
                pLbl.gameObject.AddComponent<LayoutElement>().preferredWidth = 44f;
            }
        }

        private static GameObject CreateRow(Transform parent, string name, float height, float spacing)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            var hlg = go.AddComponent<HorizontalLayoutGroup>();
            hlg.spacing = spacing;
            hlg.childAlignment = TextAnchor.MiddleLeft;
            hlg.childControlWidth = true;
            hlg.childControlHeight = true;
            hlg.childForceExpandWidth = false;
            hlg.childForceExpandHeight = false;

            var le = go.AddComponent<LayoutElement>();
            le.minHeight = height;
            le.preferredHeight = height;
            return go;
        }

        private static void ClearChildren(Transform parent)
        {
            if (parent == null) return;
            for (int i = parent.childCount - 1; i >= 0; i--)
                UnityEngine.Object.Destroy(parent.GetChild(i).gameObject);
        }

        [DllImport("user32.dll")]
        private static extern bool OpenClipboard(IntPtr hWndNewOwner);
        [DllImport("user32.dll")]
        private static extern bool CloseClipboard();
        [DllImport("user32.dll")]
        private static extern bool EmptyClipboard();
        [DllImport("user32.dll")]
        private static extern IntPtr SetClipboardData(uint uFormat, IntPtr hMem);

        private static void CopyToClipboard(string text)
        {
            if (string.IsNullOrEmpty(text)) return;
            try
            {
                if (!OpenClipboard(IntPtr.Zero)) return;
                try
                {
                    EmptyClipboard();
                    IntPtr ptr = Marshal.StringToHGlobalUni(text);
                    if (SetClipboardData(13, ptr) == IntPtr.Zero)
                        Marshal.FreeHGlobal(ptr);
                }
                finally { CloseClipboard(); }
            }
            catch { }
        }
    }
}
