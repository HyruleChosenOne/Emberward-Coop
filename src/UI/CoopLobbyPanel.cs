using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using EmberwardCoop.Core;
using EmberwardCoop.Localization;
using EmberwardCoop.Networking;
using EmberwardCoop.Rules;
using EmberwardCoop.Serialization;
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
        private RectTransform _windowRt;
        private CanvasGroup _windowCg;
        private float _openAnimProgress = 1f;
        private bool _isOpen;
        private bool _hideLobbyCode = false;
        private SteamLobbyPrivacy _steamPrivacy = SteamLobbyPrivacy.FriendsOnly;

        private GameObject _hubSection;
        private GameObject _steamSubPanel;
        private GameObject _lanSubPanel;
        private GameObject _connectedSection;

        private GameObject _settingsModalRoot;
        private RectTransform _settingsCardRt;
        private CanvasGroup _settingsCardCg;
        private float _settingsAnimProgress = 1f;

        private GameObject _overrideDialogRoot;
        private TMP_Text _overrideBodyText;
        private GameObject _hostClosedDialogRoot;
        private TMP_Text _hostClosedBodyText;

        private GameObject _avatarPickerModalRoot;
        private RectTransform _avatarPickerCardRt;
        private CanvasGroup _avatarPickerCardCg;
        private float _avatarPickerAnimProgress = 1f;
        private Transform _avatarGridContent;
        private LayoutElement _avatarScrollLe;
        private TMP_Text _avatarHoverLabel;
        private readonly List<(string heroId, string displayName, RectTransform rt)> _avatarTileRects = new List<(string, string, RectTransform)>();

        private GameObject _runModeSwitchRow;
        private Image _runTabNewBg;
        private Image _runTabLoadBg;
        private GameObject _newRunSetupGroup;
        private GameObject _loadRunCard;
        private Transform _saveListRoot;
        private LayoutElement _saveScrollLe;
        private TMP_Text _clientLoadSaveSummaryText;
        private GameObject _hostSaveListContainer;
        private List<CoopSaveFile> _cachedCoopSaves = new List<CoopSaveFile>();

        private Image _gameModeCardBg;
        private GameObject _gameModePrevBtnObj;
        private GameObject _gameModeNextBtnObj;
        private TMP_Text _gameModeNameText;
        private TMP_Text _gameModeUnlockNoteText;
        private TMP_Text _gameModeDescText;

        private TMP_Text _worldUnlockNoteText;
        private TMP_Text _diffUnlockNoteText;

        private LayoutElement _steamScrollLe;
        private LayoutElement _lanScrollLe;
        private LayoutElement _rosterScrollLe;
        private LayoutElement _feedScrollLe;

        private Image _tabSteamBg;
        private Image _tabLanBg;
        private Image _modeSharedBg;
        private Image _modeSeparateBg;

        private readonly List<(string locKey, TMP_Text label)> _locBindings = new List<(string, TMP_Text)>();

        private TMP_Text _headerStatusText;

        private TMP_Text _steamAvailabilityText;
        private TMP_Text _privacyBtnLabel;
        private Button _steamHostBtn;
        private Button _steamJoinCodeBtn;
        private Button _steamRefreshBtn;
        private TMP_Text _steamRefreshBtnLabel;
        private TMP_Text _steamScanningText;
        private TMP_InputField _steamCodeInput;
        private Transform _steamLobbyListRoot;

        private TMP_InputField _lanNameInput;
        private TMP_InputField _lanIpInput;
        private TMP_InputField _lanPortInput;
        private Button _lanRefreshBtn;
        private TMP_Text _lanRefreshBtnLabel;
        private TMP_Text _lanScanningText;
        private Transform _lanLobbyListRoot;

        private GameObject _clientReadOnlyBadgeObj;
        private Image _modeCardBg;
        private GameObject _hostModeButtonsRow;
        private GameObject _clientModeReadOnlyRow;
        private TMP_Text _clientModeReadOnlyText;
        private TMP_Text _modeRulesDescText;

        private Image _worldCardBg;
        private GameObject _worldPrevBtnObj;
        private GameObject _worldNextBtnObj;
        private GameObject _endlessStageRowObj;
        private GameObject _endlessStagePrevBtnObj;
        private GameObject _endlessStageNextBtnObj;
        private TMP_Text _endlessStageNameText;
        private Image _worldBannerImage;
        private TMP_Text _worldNameText;
        private TMP_Text _worldMetaBadgeText;
        private TMP_Text _worldDescText;
        private TMP_Text _worldHazardsText;

        private Image _rulesCardBg;
        private GameObject _diffControlsGroupObj;
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

        private ScrollRect _feedScrollRect;
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
        private TMP_Text _hostColorValueText;
        private TMP_Text _clientColorValueText;

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
        private float _gameModePulseTimer;
        private float _worldPulseTimer;
        private float _rulesPulseTimer;

        private float _lastRefreshTime;
        private bool _dirty = true;
        private bool _feedNeedsScrollBottom;

        public static bool IsAnyOpen { get; private set; }
        public bool IsOpen => _isOpen;

        public CoopLobbyPanel(SessionController session)
        {
            _session = session;
            Net.OnRosterUpdated += () => _dirty = true;
            Net.OnLobbyListUpdated += () => _dirty = true;
            session.OnSettingsChanged += () => _dirty = true;
            session.OnLobbySetupChanged += HandleLobbySetupChangedFeedback;
            CoopLogger.OnFeedEntryAdded += _ => { _dirty = true; _feedNeedsScrollBottom = true; };
            CoopLoc.OnLanguageChanged += OnLanguageChanged;
        }

        private void OnLanguageChanged()
        {
            NativeAssetRegistry.RefreshHeroDisplayNames();
            RefreshStaticLocalizedBindings();
            _dirty = true;
            if (_avatarPickerModalRoot != null && _avatarPickerModalRoot.activeSelf)
                RebuildAvatarPickerGrid();
        }

        private void HandleLobbySetupChangedFeedback(LobbySetupChangeMask mask)
        {
            _dirty = true;
            if (!Net.IsClient || !_isOpen || mask == LobbySetupChangeMask.None) return;

            bool pulsed = false;
            if ((mask & LobbySetupChangeMask.CoopMode) != 0) { _modePulseTimer = 0.85f; pulsed = true; }
            if ((mask & LobbySetupChangeMask.GameMode) != 0) { _gameModePulseTimer = 0.85f; pulsed = true; }
            if ((mask & (LobbySetupChangeMask.World | LobbySetupChangeMask.GameMode)) != 0) { _worldPulseTimer = 0.85f; pulsed = true; }
            if ((mask & (LobbySetupChangeMask.Difficulty | LobbySetupChangeMask.SessionRules)) != 0) { _rulesPulseTimer = 0.85f; pulsed = true; }

            if (pulsed)
                NativeStyleProvider.PlayUiClickSound(1.24f);
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
            IsAnyOpen = true;
            _openAnimProgress = 0f;
            _hideLobbyCode = CoopConfig.HideLobbyCodeByDefault?.Value ?? false;
            _root.SetActive(true);
            _dirty = true;
            TitleMenuIntegration.SetMainMenuButtonsVisible(false);
            RefreshSavedPresetsList();
            RefreshCoopSavesList();
            _session.Unlocks.RefreshLocalSummary();
            if (Net.IsHost)
                _session.RefreshPartyUnlocksAndValidateSelection(broadcast: false);
            if (!Net.IsConnected)
            {
                bool steamReady = SteamP2PTransport.IsSteamReady();
                if (!steamReady) SelectTransportTab(false);
                Net.Lan.RefreshLobbyList();
                if (steamReady) Net.Steam.RefreshLobbyList();
            }
            RefreshStaticLocalizedBindings();
            RefreshView();
            NativeStyleProvider.PlayUiOpenSound(true);
        }

        public void Close()
        {
            bool wasOpen = _isOpen;
            _isOpen = false;
            IsAnyOpen = false;
            HideAllSubModals();
            if (_root != null) _root.SetActive(false);
            TitleMenuIntegration.SetMainMenuButtonsVisible(true);
            if (wasOpen) NativeStyleProvider.PlayUiOpenSound(false);
        }

        public void ForceCloseForRun()
        {
            _isOpen = false;
            IsAnyOpen = false;
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
            if (_avatarPickerModalRoot != null) _avatarPickerModalRoot.SetActive(false);
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

            float dt = Time.unscaledDeltaTime;
            NativeStyleProvider.TickOpenTransition(_windowRt, _windowCg, ref _openAnimProgress, dt, 0.14f);
            if (_settingsModalRoot != null && _settingsModalRoot.activeSelf)
                NativeStyleProvider.TickOpenTransition(_settingsCardRt, _settingsCardCg, ref _settingsAnimProgress, dt, 0.12f);
            if (_avatarPickerModalRoot != null && _avatarPickerModalRoot.activeSelf)
            {
                NativeStyleProvider.TickOpenTransition(_avatarPickerCardRt, _avatarPickerCardCg, ref _avatarPickerAnimProgress, dt, 0.11f);
                UpdateAvatarPickerHoverCaption();
            }

            if (Input.GetKeyDown(KeyCode.Escape) && !(CoopSettingsModal.Instance?.IsOpen ?? false))
            {
                if (_hostClosedDialogRoot != null && _hostClosedDialogRoot.activeSelf)
                    _hostClosedDialogRoot.SetActive(false);
                else if (_overrideDialogRoot != null && _overrideDialogRoot.activeSelf)
                    _overrideDialogRoot.SetActive(false);
                else if (_avatarPickerModalRoot != null && _avatarPickerModalRoot.activeSelf)
                {
                    _avatarPickerModalRoot.SetActive(false);
                    NativeStyleProvider.PlayUiOpenSound(false);
                }
                else if (_settingsModalRoot != null && _settingsModalRoot.activeSelf)
                    _settingsModalRoot.SetActive(false);
                else
                    Close();
                return;
            }

            TickPulseHighlights(dt);
            UpdateScanningIndicators(now);

            if (_dirty || now - _lastRefreshTime >= 0.65f)
            {
                _lastRefreshTime = now;
                _dirty = false;
                RefreshView();
            }

            if (_feedNeedsScrollBottom && _feedScrollRect != null)
            {
                _feedNeedsScrollBottom = false;
                _feedScrollRect.verticalNormalizedPosition = 0f;
            }
        }

        private void UpdateScanningIndicators(float now)
        {
            if (Net.IsConnected) return;
            int dots = ((int)(now * 3f)) % 4;
            string dotStr = new string('.', dots);

            bool steamBusy = Net.Steam.IsRefreshingLobbies;
            if (_steamScanningText != null)
            {
                _steamScanningText.gameObject.SetActive(steamBusy);
                if (steamBusy) _steamScanningText.text = $"{CoopLoc.T("lobby.refreshing")}{dotStr}";
            }
            if (_steamRefreshBtnLabel != null)
                _steamRefreshBtnLabel.text = steamBusy ? $"{CoopLoc.T("lobby.refreshing")}{dotStr}" : CoopLoc.T("lobby.refresh");

            bool lanBusy = Net.Lan.IsRefreshingLobbies;
            if (_lanScanningText != null)
            {
                _lanScanningText.gameObject.SetActive(lanBusy);
                if (lanBusy) _lanScanningText.text = $"{CoopLoc.T("lobby.refreshing")}{dotStr}";
            }
            if (_lanRefreshBtnLabel != null)
                _lanRefreshBtnLabel.text = lanBusy ? $"{CoopLoc.T("lobby.refreshing")}{dotStr}" : CoopLoc.T("lobby.refresh");
        }

        private void TickPulseHighlights(float dt)
        {
            UpdateCardPulse(ref _modePulseTimer, _modeCardBg, dt);
            UpdateCardPulse(ref _gameModePulseTimer, _gameModeCardBg, dt);
            UpdateCardPulse(ref _worldPulseTimer, _worldCardBg, dt);
            UpdateCardPulse(ref _rulesPulseTimer, _rulesCardBg, dt);
        }

        private static void UpdateCardPulse(ref float timer, Image cardBg, float dt)
        {
            if (cardBg == null) return;
            if (timer > 0f)
            {
                timer = Mathf.Max(0f, timer - dt);
                float pulse = Mathf.Sin((timer / 0.85f) * Mathf.PI);
                cardBg.color = Color.Lerp(EmberwardTheme.SubPanelSurface, EmberwardTheme.SelectedTileBg, pulse);
                if (timer <= 0f)
                    cardBg.color = EmberwardTheme.SubPanelSurface;
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

        private Button CreateLocButton(Transform parent, string locKey, Color tint, Action onClick, float fontSize = 13.5f, float height = 34f, bool isTab = false)
        {
            var btn = NativeStyleProvider.CreateButton(parent, CoopLoc.T(locKey), tint, onClick, fontSize, height, isTab);
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
            _root.AddComponent<Image>().color = EmberwardTheme.ModalScrim;

            var window = NativeStyleProvider.CreatePanel(_root.transform, "PreRunWindow", NativeStyleProvider.PanelBg, addBorder: true, roleOverride: PanelSurfaceRole.Window);
            _windowRt = window.GetComponent<RectTransform>();
            _windowRt.anchorMin = new Vector2(0.5f, 0.5f);
            _windowRt.anchorMax = new Vector2(0.5f, 0.5f);
            _windowRt.sizeDelta = new Vector2(780f, 460f);
            _windowCg = window.AddComponent<CanvasGroup>();

            var winFit = window.AddComponent<ContentSizeFitter>();
            winFit.horizontalFit = ContentSizeFitter.FitMode.Unconstrained;
            winFit.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

            var vlg = window.AddComponent<VerticalLayoutGroup>();
            vlg.padding = new RectOffset(20, 20, 17, 18);
            vlg.spacing = 8f;
            vlg.childControlWidth = true;
            vlg.childControlHeight = true;
            vlg.childForceExpandWidth = true;
            vlg.childForceExpandHeight = false;

            var headerRow = CreateRow(window.transform, "Header", 36f, 10f);
            var backBtn = CreateLocButton(headerRow.transform, "lobby.back_to_menu", NativeStyleProvider.ButtonSecondary, Close, 13f, 32f);
            var backLe = backBtn.GetComponent<LayoutElement>();
            backLe.minWidth = 132f; backLe.preferredWidth = 152f;

            var title = BindLoc(NativeStyleProvider.CreateLabel(headerRow.transform, "", 21f, NativeStyleProvider.EmberGold, TextAlignmentOptions.Center, FontStyles.Bold, autoSize: true, minFontSize: 15f, addShadow: true), "lobby.title");
            title.gameObject.AddComponent<LayoutElement>().flexibleWidth = 1f;

            _headerStatusText = NativeStyleProvider.CreateLabel(headerRow.transform, "Offline", 12f, NativeStyleProvider.TextMuted, TextAlignmentOptions.MidlineRight);
            var stLe = _headerStatusText.gameObject.AddComponent<LayoutElement>();
            stLe.minWidth = 110f; stLe.preferredWidth = 165f;

            var modCfgBtn = CreateLocButton(headerRow.transform, "lobby.settings_btn", NativeStyleProvider.CardBg, () => CoopSettingsModal.Instance?.Open(), 12.5f, 32f);
            var cfgLe = modCfgBtn.GetComponent<LayoutElement>();
            cfgLe.minWidth = 122f; cfgLe.preferredWidth = 138f;

            NativeStyleProvider.CreateDivider(window.transform, "TopDivider");

            BuildHubSection(window.transform);
            BuildConnectedLobbySection(window.transform);
            BuildSessionSettingsModal(_root.transform);
            BuildAvatarPickerModal(_root.transform);
            BuildStartOverrideDialog(_root.transform);
            BuildHostClosedDialog(_root.transform);

            SelectTransportTab(SteamP2PTransport.IsSteamReady());
            _root.SetActive(false);
        }

        private void BuildHubSection(Transform parent)
        {
            _hubSection = new GameObject("CoopHubSection");
            _hubSection.transform.SetParent(parent, false);
            var vlg = _hubSection.AddComponent<VerticalLayoutGroup>();
            vlg.spacing = 7f;
            vlg.childControlWidth = true;
            vlg.childControlHeight = true;
            vlg.childForceExpandWidth = true;
            vlg.childForceExpandHeight = false;

            var tabRow = CreateRow(_hubSection.transform, "TransportTabs", 34f, 8f);
            var steamTabBtn = CreateLocButton(tabRow.transform, "lobby.tab_steam", EmberwardTheme.TabActive, () => SelectTransportTab(true), 14f, 34f, isTab: true);
            steamTabBtn.GetComponent<LayoutElement>().flexibleWidth = 1f;
            _tabSteamBg = steamTabBtn.GetComponent<Image>();

            var lanTabBtn = CreateLocButton(tabRow.transform, "lobby.tab_lan", EmberwardTheme.TabInactive, () => SelectTransportTab(false), 14f, 34f, isTab: true);
            lanTabBtn.GetComponent<LayoutElement>().flexibleWidth = 1f;
            _tabLanBg = lanTabBtn.GetComponent<Image>();

            BuildSteamHubPanel(_hubSection.transform);
            BuildLanHubPanel(_hubSection.transform);
        }

        private void BuildSteamHubPanel(Transform parent)
        {
            _steamSubPanel = NativeStyleProvider.CreatePanel(parent, "SteamHubPanel", NativeStyleProvider.PanelSubBg, addBorder: true, isSubPanel: true);
            var vlg = _steamSubPanel.AddComponent<VerticalLayoutGroup>();
            vlg.padding = new RectOffset(12, 12, 10, 10);
            vlg.spacing = 7f;
            vlg.childControlWidth = true;
            vlg.childControlHeight = true;
            vlg.childForceExpandWidth = true;
            vlg.childForceExpandHeight = false;

            _steamAvailabilityText = NativeStyleProvider.CreateLabel(_steamSubPanel.transform, "Steam P2P Ready", 13f, NativeStyleProvider.EmberGold, TextAlignmentOptions.MidlineLeft, FontStyles.Bold, addShadow: true);

            var hostRow = CreateRow(_steamSubPanel.transform, "SteamHostRow", 36f, 8f);
            var privBtn = NativeStyleProvider.CreateButton(hostRow.transform, "Privacy: FriendsOnly", NativeStyleProvider.ButtonSecondary, CycleSteamPrivacy, 12.5f, 34f);
            var pLe = privBtn.GetComponent<LayoutElement>();
            pLe.minWidth = 178f; pLe.preferredWidth = 198f;
            _privacyBtnLabel = privBtn.GetComponentInChildren<TMP_Text>();

            _steamHostBtn = CreateLocButton(hostRow.transform, "lobby.host_steam", NativeStyleProvider.ButtonPrimary, () =>
            {
                Net.HostSteam(_steamPrivacy, _session.GetCurrentCoopModeTag());
                _dirty = true;
            }, 14f, 34f);
            _steamHostBtn.GetComponent<LayoutElement>().flexibleWidth = 1f;

            var joinRow = CreateRow(_steamSubPanel.transform, "SteamJoinRow", 34f, 8f);
            _steamCodeInput = NativeStyleProvider.CreateInputField(joinRow.transform, CoopLoc.T("lobby.paste_code"), "", 32f, 12.5f);
            _steamCodeInput.GetComponent<LayoutElement>().flexibleWidth = 1f;
            _steamJoinCodeBtn = CreateLocButton(joinRow.transform, "lobby.join_code", NativeStyleProvider.ButtonSuccess, () =>
            {
                string raw = (_steamCodeInput.text ?? "").Trim();
                if (ulong.TryParse(raw, out ulong lid) && lid > 0) Net.JoinSteam(lid);
                else GameRefs.ShowGameToast("Enter a valid numeric Steam Lobby Code.", 2.2f);
            }, 13f, 32f);
            var jcLe = _steamJoinCodeBtn.GetComponent<LayoutElement>();
            jcLe.minWidth = 132f; jcLe.preferredWidth = 152f;

            NativeStyleProvider.CreateDivider(_steamSubPanel.transform, "SteamBrowserDivider");

            var listHeader = CreateRow(_steamSubPanel.transform, "SteamListHeader", 28f, 8f);
            var lbl = BindLoc(NativeStyleProvider.CreateLabel(listHeader.transform, "", 13.5f, NativeStyleProvider.EmberGold, TextAlignmentOptions.MidlineLeft, FontStyles.Bold, autoSize: true, minFontSize: 11f, addShadow: true), "lobby.steam_lobbies");
            lbl.gameObject.AddComponent<LayoutElement>().flexibleWidth = 1f;

            _steamScanningText = NativeStyleProvider.CreateLabel(listHeader.transform, "", 11.5f, NativeStyleProvider.EmberGoldSoft, TextAlignmentOptions.MidlineRight, FontStyles.Italic);
            _steamScanningText.gameObject.AddComponent<LayoutElement>().preferredWidth = 110f;
            _steamScanningText.gameObject.SetActive(false);

            _steamRefreshBtn = NativeStyleProvider.CreateButton(listHeader.transform, CoopLoc.T("lobby.refresh"), NativeStyleProvider.ButtonSecondary, () =>
            {
                Net.Steam.RefreshLobbyList();
                _dirty = true;
            }, 12f, 26f);
            var srLe = _steamRefreshBtn.GetComponent<LayoutElement>();
            srLe.minWidth = 98f; srLe.preferredWidth = 112f;
            _steamRefreshBtnLabel = _steamRefreshBtn.GetComponentInChildren<TMP_Text>();

            BuildLobbyTableHeader(_steamSubPanel.transform);

            var scroll = NativeStyleProvider.CreateScrollView(_steamSubPanel.transform, "SteamLobbyScroll", NativeStyleProvider.PanelWellBg, out RectTransform contentRt, padding: 6f, spacing: 5f);
            _steamScrollLe = scroll.gameObject.AddComponent<LayoutElement>();
            _steamScrollLe.minHeight = 48f;
            _steamScrollLe.preferredHeight = 56f;
            _steamLobbyListRoot = contentRt;
        }

        private void BuildLanHubPanel(Transform parent)
        {
            _lanSubPanel = NativeStyleProvider.CreatePanel(parent, "LanHubPanel", NativeStyleProvider.PanelSubBg, addBorder: true, isSubPanel: true);
            var vlg = _lanSubPanel.AddComponent<VerticalLayoutGroup>();
            vlg.padding = new RectOffset(12, 12, 10, 10);
            vlg.spacing = 7f;
            vlg.childControlWidth = true;
            vlg.childControlHeight = true;
            vlg.childForceExpandWidth = true;
            vlg.childForceExpandHeight = false;

            var nameRow = CreateRow(_lanSubPanel.transform, "NameRow", 32f, 8f);
            var nameLbl = BindLoc(NativeStyleProvider.CreateLabel(nameRow.transform, "", 13f, NativeStyleProvider.TextLight, TextAlignmentOptions.MidlineLeft, autoSize: true, minFontSize: 11f), "lobby.player_name");
            nameLbl.gameObject.AddComponent<LayoutElement>().preferredWidth = 132f;
            _lanNameInput = NativeStyleProvider.CreateInputField(nameRow.transform, "Name", Net.LocalPlayerName, 32f, 12.5f);
            _lanNameInput.GetComponent<LayoutElement>().flexibleWidth = 1f;

            var addrRow = CreateRow(_lanSubPanel.transform, "AddrRow", 32f, 8f);
            var ipLbl = BindLoc(NativeStyleProvider.CreateLabel(addrRow.transform, "", 13f, NativeStyleProvider.TextLight, TextAlignmentOptions.MidlineLeft, autoSize: true, minFontSize: 11f), "lobby.host_ip_port");
            ipLbl.gameObject.AddComponent<LayoutElement>().preferredWidth = 132f;
            _lanIpInput = NativeStyleProvider.CreateInputField(addrRow.transform, "127.0.0.1", "127.0.0.1", 32f, 12.5f);
            _lanIpInput.GetComponent<LayoutElement>().flexibleWidth = 1f;
            _lanPortInput = NativeStyleProvider.CreateInputField(addrRow.transform, "27777", CoopConfig.DefaultLanPort.Value.ToString(), 32f, 12.5f);
            _lanPortInput.GetComponent<LayoutElement>().preferredWidth = 88f;

            var btnRow = CreateRow(_lanSubPanel.transform, "LanActionRow", 36f, 8f);
            var hostLanBtn = CreateLocButton(btnRow.transform, "lobby.host_lan", NativeStyleProvider.ButtonPrimary, () =>
            {
                ApplyLanName();
                Net.HostLan(ParsePort(), _session.GetCurrentCoopModeTag());
                _dirty = true;
            }, 13.5f, 34f);
            hostLanBtn.GetComponent<LayoutElement>().flexibleWidth = 1f;

            var joinLanBtn = CreateLocButton(btnRow.transform, "lobby.join_lan", NativeStyleProvider.ButtonSuccess, () =>
            {
                ApplyLanName();
                Net.JoinLan((_lanIpInput.text ?? "127.0.0.1").Trim(), ParsePort());
                _dirty = true;
            }, 13.5f, 34f);
            joinLanBtn.GetComponent<LayoutElement>().flexibleWidth = 1f;

            NativeStyleProvider.CreateDivider(_lanSubPanel.transform, "LanBrowserDivider");

            var lanHdr = CreateRow(_lanSubPanel.transform, "LanListHeader", 28f, 8f);
            var lanTitle = BindLoc(NativeStyleProvider.CreateLabel(lanHdr.transform, "", 13.5f, NativeStyleProvider.EmberGold, TextAlignmentOptions.MidlineLeft, FontStyles.Bold, autoSize: true, minFontSize: 11f, addShadow: true), "lobby.discovered_lan");
            lanTitle.gameObject.AddComponent<LayoutElement>().flexibleWidth = 1f;

            _lanScanningText = NativeStyleProvider.CreateLabel(lanHdr.transform, "", 11.5f, NativeStyleProvider.EmberGoldSoft, TextAlignmentOptions.MidlineRight, FontStyles.Italic);
            _lanScanningText.gameObject.AddComponent<LayoutElement>().preferredWidth = 110f;
            _lanScanningText.gameObject.SetActive(false);

            _lanRefreshBtn = NativeStyleProvider.CreateButton(lanHdr.transform, CoopLoc.T("lobby.refresh"), NativeStyleProvider.ButtonSecondary, () =>
            {
                Net.Lan.RefreshLobbyList();
                _dirty = true;
            }, 12f, 26f);
            var lrLe = _lanRefreshBtn.GetComponent<LayoutElement>();
            lrLe.minWidth = 98f; lrLe.preferredWidth = 112f;
            _lanRefreshBtnLabel = _lanRefreshBtn.GetComponentInChildren<TMP_Text>();

            BuildLobbyTableHeader(_lanSubPanel.transform);

            var scroll = NativeStyleProvider.CreateScrollView(_lanSubPanel.transform, "LanLobbyScroll", NativeStyleProvider.PanelWellBg, out RectTransform contentRt, padding: 6f, spacing: 5f);
            _lanScrollLe = scroll.gameObject.AddComponent<LayoutElement>();
            _lanScrollLe.minHeight = 48f;
            _lanScrollLe.preferredHeight = 56f;
            _lanLobbyListRoot = contentRt;
        }

        private void BuildLobbyTableHeader(Transform parent)
        {
            var hdr = CreateRow(parent, "TableColumns", 20f, 8f);
            var hlg = hdr.GetComponent<HorizontalLayoutGroup>();
            hlg.padding = new RectOffset(14, 18, 0, 0);

            var cHost = BindLoc(NativeStyleProvider.CreateLabel(hdr.transform, "", 11f, NativeStyleProvider.TextMuted, TextAlignmentOptions.MidlineLeft, FontStyles.Bold), "lobby.col_host");
            cHost.gameObject.AddComponent<LayoutElement>().flexibleWidth = 1f;

            var cMode = BindLoc(NativeStyleProvider.CreateLabel(hdr.transform, "", 11f, NativeStyleProvider.TextMuted, TextAlignmentOptions.Center, FontStyles.Bold), "lobby.col_mode");
            cMode.gameObject.AddComponent<LayoutElement>().preferredWidth = 102f;

            var cPlayers = BindLoc(NativeStyleProvider.CreateLabel(hdr.transform, "", 11f, NativeStyleProvider.TextMuted, TextAlignmentOptions.Center, FontStyles.Bold), "lobby.col_players");
            cPlayers.gameObject.AddComponent<LayoutElement>().preferredWidth = 68f;

            var cPing = BindLoc(NativeStyleProvider.CreateLabel(hdr.transform, "", 11f, NativeStyleProvider.TextMuted, TextAlignmentOptions.Center, FontStyles.Bold), "lobby.col_ping");
            cPing.gameObject.AddComponent<LayoutElement>().preferredWidth = 68f;

            var cAction = new GameObject("ColActionSpacer");
            cAction.transform.SetParent(hdr.transform, false);
            cAction.AddComponent<LayoutElement>().preferredWidth = 112f;
        }

        private void BuildConnectedLobbySection(Transform parent)
        {
            _connectedSection = new GameObject("ConnectedPreRunLobby");
            _connectedSection.transform.SetParent(parent, false);

            var split = _connectedSection.AddComponent<HorizontalLayoutGroup>();
            split.spacing = 10f;
            split.childAlignment = TextAnchor.UpperLeft;
            split.childControlWidth = true;
            split.childControlHeight = true;
            split.childForceExpandWidth = true;
            split.childForceExpandHeight = false;

            BuildLeftSetupColumn(_connectedSection.transform);
            BuildRightPartyColumn(_connectedSection.transform);
        }

        private void BuildLeftSetupColumn(Transform parent)
        {
            var leftCol = new GameObject("LeftMatchSetupCol");
            leftCol.transform.SetParent(parent, false);
            var le = leftCol.AddComponent<LayoutElement>();
            le.flexibleWidth = 1.42f;
            le.minWidth = 500f;

            var vlg = leftCol.AddComponent<VerticalLayoutGroup>();
            vlg.spacing = 6f;
            vlg.childControlWidth = true;
            vlg.childControlHeight = true;
            vlg.childForceExpandWidth = true;
            vlg.childForceExpandHeight = false;

            var hdr = CreateRow(leftCol.transform, "SetupHdr", 24f, 8f);
            var hdrLbl = BindLoc(NativeStyleProvider.CreateLabel(hdr.transform, "", 14f, NativeStyleProvider.EmberGold, TextAlignmentOptions.MidlineLeft, FontStyles.Bold, autoSize: true, minFontSize: 11f, addShadow: true), "lobby.match_setup_title");
            hdrLbl.gameObject.AddComponent<LayoutElement>().flexibleWidth = 1f;

            var roBadge = NativeStyleProvider.CreateBadgePill(hdr.transform, "ReadOnlyBadge", EmberwardTheme.HostBadgeBg, addBorder: true);
            var roLe = roBadge.AddComponent<LayoutElement>();
            roLe.minWidth = 148f; roLe.preferredWidth = 182f; roLe.minHeight = 22f; roLe.preferredHeight = 22f;
            var roTxt = BindLoc(NativeStyleProvider.CreateLabel(roBadge.transform, "", 10.5f, NativeStyleProvider.EmberGold, TextAlignmentOptions.Center, FontStyles.Bold, autoSize: true, minFontSize: 9f), "lobby.host_controls_badge");
            NativeStyleProvider.StretchFill(roTxt.rectTransform, 6f, 2f);
            _clientReadOnlyBadgeObj = roBadge;

            _runModeSwitchRow = CreateRow(leftCol.transform, "RunSourceTabs", 28f, 6f);
            var newRunBtn = CreateLocButton(_runModeSwitchRow.transform, "save.tab_new_run", EmberwardTheme.TabActive, () => HostSetLoadRunMode(false), 12f, 28f, isTab: true);
            newRunBtn.GetComponent<LayoutElement>().flexibleWidth = 1f;
            _runTabNewBg = newRunBtn.GetComponent<Image>();

            var loadRunBtn = CreateLocButton(_runModeSwitchRow.transform, "save.tab_load_run", EmberwardTheme.TabInactive, () => HostSetLoadRunMode(true), 12f, 28f, isTab: true);
            loadRunBtn.GetComponent<LayoutElement>().flexibleWidth = 1f;
            _runTabLoadBg = loadRunBtn.GetComponent<Image>();

            _loadRunCard = NativeStyleProvider.CreatePanel(leftCol.transform, "LoadCoopSaveCard", NativeStyleProvider.PanelSubBg, addBorder: false, isSubPanel: true);
            var lrv = _loadRunCard.AddComponent<VerticalLayoutGroup>();
            lrv.padding = new RectOffset(10, 10, 8, 8);
            lrv.spacing = 6f;
            lrv.childControlWidth = true;
            lrv.childControlHeight = true;
            lrv.childForceExpandWidth = true;
            lrv.childForceExpandHeight = false;

            var saveHdr = CreateRow(_loadRunCard.transform, "SaveListHdr", 24f, 6f);
            var saveTitle = BindLoc(NativeStyleProvider.CreateLabel(saveHdr.transform, "", 12f, NativeStyleProvider.EmberGold, TextAlignmentOptions.MidlineLeft, FontStyles.Bold, addShadow: true), "save.list_title");
            saveTitle.gameObject.AddComponent<LayoutElement>().flexibleWidth = 1f;
            var saveRefBtn = CreateLocButton(saveHdr.transform, "lobby.refresh", NativeStyleProvider.ButtonSecondary, () => { RefreshCoopSavesList(); _dirty = true; }, 11f, 24f);
            saveRefBtn.GetComponent<LayoutElement>().preferredWidth = 86f;

            _clientLoadSaveSummaryText = NativeStyleProvider.CreateLabel(_loadRunCard.transform, "", 12.5f, NativeStyleProvider.TextLight, TextAlignmentOptions.TopLeft, FontStyles.Bold, wordWrap: true);

            var saveScroll = NativeStyleProvider.CreateScrollView(_loadRunCard.transform, "CoopSavesScroll", NativeStyleProvider.PanelWellBg, out RectTransform saveContent, padding: 6f, spacing: 5f);
            _saveScrollLe = saveScroll.gameObject.AddComponent<LayoutElement>();
            _saveScrollLe.minHeight = 90f;
            _saveScrollLe.preferredHeight = 230f;
            _saveListRoot = saveContent;
            _hostSaveListContainer = saveScroll.gameObject;
            _loadRunCard.SetActive(false);

            _newRunSetupGroup = new GameObject("NewRunSetupGroup");
            _newRunSetupGroup.transform.SetParent(leftCol.transform, false);
            var nrv = _newRunSetupGroup.AddComponent<VerticalLayoutGroup>();
            nrv.spacing = 6f;
            nrv.childControlWidth = true;
            nrv.childControlHeight = true;
            nrv.childForceExpandWidth = true;
            nrv.childForceExpandHeight = false;

            var modeCard = NativeStyleProvider.CreatePanel(_newRunSetupGroup.transform, "CoopModeCard", NativeStyleProvider.PanelSubBg, addBorder: false, isSubPanel: true);
            _modeCardBg = modeCard.GetComponent<Image>();
            var mv = modeCard.AddComponent<VerticalLayoutGroup>();
            mv.padding = new RectOffset(10, 10, 6, 7);
            mv.spacing = 4f;
            mv.childControlWidth = true;
            mv.childControlHeight = true;
            mv.childForceExpandWidth = true;
            mv.childForceExpandHeight = false;

            BindLoc(NativeStyleProvider.CreateLabel(modeCard.transform, "", 11.5f, NativeStyleProvider.EmberGold, TextAlignmentOptions.MidlineLeft, FontStyles.Bold, addShadow: true), "lobby.mode_label");

            _hostModeButtonsRow = CreateRow(modeCard.transform, "HostModeRow", 30f, 8f);
            var sharedBtn = CreateLocButton(_hostModeButtonsRow.transform, "lobby.mode_shared", NativeStyleProvider.ButtonPrimary, () => HostSetEconomyMode(CoopEconomyMode.SharedResources), 12f, 30f);
            sharedBtn.GetComponent<LayoutElement>().flexibleWidth = 1f;
            _modeSharedBg = sharedBtn.GetComponent<Image>();

            var separateBtn = CreateLocButton(_hostModeButtonsRow.transform, "lobby.mode_separate", NativeStyleProvider.ButtonSecondary, () => HostSetEconomyMode(CoopEconomyMode.SeparateResources), 12f, 30f);
            separateBtn.GetComponent<LayoutElement>().flexibleWidth = 1f;
            _modeSeparateBg = separateBtn.GetComponent<Image>();

            _clientModeReadOnlyRow = NativeStyleProvider.CreatePanel(modeCard.transform, "ClientModePill", NativeStyleProvider.CardBg, addBorder: false, isSubPanel: true, roleOverride: PanelSurfaceRole.Card);
            _clientModeReadOnlyRow.AddComponent<LayoutElement>().preferredHeight = 26f;
            _clientModeReadOnlyText = NativeStyleProvider.CreateLabel(_clientModeReadOnlyRow.transform, "", 13f, NativeStyleProvider.EmberGold, TextAlignmentOptions.Center, FontStyles.Bold, autoSize: true, minFontSize: 10.5f, addShadow: true);
            NativeStyleProvider.StretchFill(_clientModeReadOnlyText.rectTransform, 8f, 2f);

            _modeRulesDescText = NativeStyleProvider.CreateLabel(modeCard.transform, "", 11f, NativeStyleProvider.TextMuted, TextAlignmentOptions.TopLeft, wordWrap: true);

            var gameModeCard = NativeStyleProvider.CreatePanel(_newRunSetupGroup.transform, "GameModeCard", NativeStyleProvider.PanelSubBg, addBorder: false, isSubPanel: true);
            _gameModeCardBg = gameModeCard.GetComponent<Image>();
            var gmv = gameModeCard.AddComponent<VerticalLayoutGroup>();
            gmv.padding = new RectOffset(10, 10, 6, 7);
            gmv.spacing = 3f;
            gmv.childControlWidth = true;
            gmv.childControlHeight = true;
            gmv.childForceExpandWidth = true;
            gmv.childForceExpandHeight = false;

            var gmTopRow = CreateRow(gameModeCard.transform, "GameModeHeaderRow", 26f, 6f);
            var gmLbl = BindLoc(NativeStyleProvider.CreateLabel(gmTopRow.transform, "", 11.5f, NativeStyleProvider.EmberGold, TextAlignmentOptions.MidlineLeft, FontStyles.Bold, addShadow: true), "lobby.game_mode");
            gmLbl.gameObject.AddComponent<LayoutElement>().preferredWidth = 108f;

            var gmPrev = NativeStyleProvider.CreateButton(gmTopRow.transform, "<", NativeStyleProvider.ButtonSecondary, () => HostCycleRunMode(-1), 13.5f, 24f);
            gmPrev.GetComponent<LayoutElement>().preferredWidth = 28f;
            _gameModePrevBtnObj = gmPrev.gameObject;

            var gmPill = NativeStyleProvider.CreateBadgePill(gmTopRow.transform, "GameModePill", NativeStyleProvider.CardBg, addBorder: true);
            var gmpLe = gmPill.AddComponent<LayoutElement>();
            gmpLe.minWidth = 148f; gmpLe.preferredWidth = 176f; gmpLe.minHeight = 24f; gmpLe.preferredHeight = 24f;
            _gameModeNameText = NativeStyleProvider.CreateLabel(gmPill.transform, "", 12.5f, NativeStyleProvider.EmberGold, TextAlignmentOptions.Center, FontStyles.Bold, autoSize: true, minFontSize: 10f, addShadow: true);
            NativeStyleProvider.StretchFill(_gameModeNameText.rectTransform, 6f, 2f);

            var gmNext = NativeStyleProvider.CreateButton(gmTopRow.transform, ">", NativeStyleProvider.ButtonSecondary, () => HostCycleRunMode(1), 13.5f, 24f);
            gmNext.GetComponent<LayoutElement>().preferredWidth = 28f;
            _gameModeNextBtnObj = gmNext.gameObject;

            _gameModeUnlockNoteText = NativeStyleProvider.CreateLabel(gmTopRow.transform, "", 10.5f, NativeStyleProvider.StatusReady, TextAlignmentOptions.MidlineRight, FontStyles.Italic);
            _gameModeUnlockNoteText.gameObject.AddComponent<LayoutElement>().flexibleWidth = 1f;

            _gameModeDescText = NativeStyleProvider.CreateLabel(gameModeCard.transform, "", 10.5f, NativeStyleProvider.TextMuted, TextAlignmentOptions.TopLeft, wordWrap: true);

            var worldCard = NativeStyleProvider.CreatePanel(_newRunSetupGroup.transform, "LevelPreviewCard", NativeStyleProvider.PanelSubBg, addBorder: false, isSubPanel: true);
            _worldCardBg = worldCard.GetComponent<Image>();
            var wv = worldCard.AddComponent<VerticalLayoutGroup>();
            wv.padding = new RectOffset(10, 10, 7, 8);
            wv.spacing = 5f;
            wv.childControlWidth = true;
            wv.childControlHeight = true;
            wv.childForceExpandWidth = true;
            wv.childForceExpandHeight = false;

            var worldTopRow = CreateRow(worldCard.transform, "WorldSelectorRow", 26f, 6f);
            var wTitleLbl = BindLoc(NativeStyleProvider.CreateLabel(worldTopRow.transform, "", 11.5f, NativeStyleProvider.EmberGold, TextAlignmentOptions.MidlineLeft, FontStyles.Bold, addShadow: true), "lobby.select_world");
            wTitleLbl.gameObject.AddComponent<LayoutElement>().flexibleWidth = 1f;

            var prevWorldBtn = NativeStyleProvider.CreateButton(worldTopRow.transform, "<", NativeStyleProvider.ButtonSecondary, () => HostCycleWorld(-1), 13.5f, 24f);
            prevWorldBtn.GetComponent<LayoutElement>().preferredWidth = 30f;
            _worldPrevBtnObj = prevWorldBtn.gameObject;

            var nextWorldBtn = NativeStyleProvider.CreateButton(worldTopRow.transform, ">", NativeStyleProvider.ButtonSecondary, () => HostCycleWorld(1), 13.5f, 24f);
            nextWorldBtn.GetComponent<LayoutElement>().preferredWidth = 30f;
            _worldNextBtnObj = nextWorldBtn.gameObject;

            _endlessStageRowObj = CreateRow(worldCard.transform, "EndlessStageRow", 24f, 6f);
            var esLbl = BindLoc(NativeStyleProvider.CreateLabel(_endlessStageRowObj.transform, "", 11f, NativeStyleProvider.TextMuted, TextAlignmentOptions.MidlineLeft, FontStyles.Bold), "lobby.select_stage");
            esLbl.gameObject.AddComponent<LayoutElement>().preferredWidth = 104f;

            var esPrev = NativeStyleProvider.CreateButton(_endlessStageRowObj.transform, "<", NativeStyleProvider.ButtonSecondary, () => HostCycleEndlessStage(-1), 12.5f, 22f);
            esPrev.GetComponent<LayoutElement>().preferredWidth = 26f;
            _endlessStagePrevBtnObj = esPrev.gameObject;

            _endlessStageNameText = NativeStyleProvider.CreateLabel(_endlessStageRowObj.transform, "", 12f, NativeStyleProvider.TextLight, TextAlignmentOptions.Center, FontStyles.Bold);
            _endlessStageNameText.gameObject.AddComponent<LayoutElement>().flexibleWidth = 1f;

            var esNext = NativeStyleProvider.CreateButton(_endlessStageRowObj.transform, ">", NativeStyleProvider.ButtonSecondary, () => HostCycleEndlessStage(1), 12.5f, 22f);
            esNext.GetComponent<LayoutElement>().preferredWidth = 26f;
            _endlessStageNextBtnObj = esNext.gameObject;
            _endlessStageRowObj.SetActive(false);

            var previewBody = new GameObject("WorldPreviewBody");
            previewBody.transform.SetParent(worldCard.transform, false);
            var pbHlg = previewBody.AddComponent<HorizontalLayoutGroup>();
            pbHlg.spacing = 10f;
            pbHlg.childAlignment = TextAnchor.MiddleLeft;
            pbHlg.childControlWidth = true;
            pbHlg.childControlHeight = true;
            pbHlg.childForceExpandWidth = false;
            pbHlg.childForceExpandHeight = false;

            var thumbFrame = NativeStyleProvider.CreatePanel(previewBody.transform, "BiomeThumb", NativeStyleProvider.PanelWellBg, addBorder: false, isSubPanel: true, roleOverride: PanelSurfaceRole.Well);
            var tle = thumbFrame.AddComponent<LayoutElement>();
            tle.minWidth = 154f; tle.preferredWidth = 154f; tle.minHeight = 82f; tle.preferredHeight = 82f;

            var thumbImgGo = new GameObject("BannerSprite");
            thumbImgGo.transform.SetParent(thumbFrame.transform, false);
            NativeStyleProvider.StretchFill(thumbImgGo.AddComponent<RectTransform>(), 5f, 5f);
            _worldBannerImage = thumbImgGo.AddComponent<Image>();
            _worldBannerImage.sprite = NativeAssetRegistry.GetWorldBannerSprite(Setup.SelectedWorld);
            _worldBannerImage.preserveAspect = true;
            _worldBannerImage.raycastTarget = false;

            var metaCol = new GameObject("WorldMeta");
            metaCol.transform.SetParent(previewBody.transform, false);
            metaCol.AddComponent<LayoutElement>().flexibleWidth = 1f;
            var metaV = metaCol.AddComponent<VerticalLayoutGroup>();
            metaV.spacing = 2f;
            metaV.childControlWidth = true;
            metaV.childControlHeight = true;
            metaV.childForceExpandWidth = true;
            metaV.childForceExpandHeight = false;

            _worldNameText = NativeStyleProvider.CreateLabel(metaCol.transform, "", 15f, NativeStyleProvider.EmberGold, TextAlignmentOptions.MidlineLeft, FontStyles.Bold, autoSize: true, minFontSize: 12f, addShadow: true);
            _worldUnlockNoteText = NativeStyleProvider.CreateLabel(metaCol.transform, "", 10.5f, NativeStyleProvider.StatusReady, TextAlignmentOptions.MidlineLeft, FontStyles.Italic);
            _worldMetaBadgeText = NativeStyleProvider.CreateLabel(metaCol.transform, "", 10.5f, new Color(0.68f, 0.90f, 0.96f, 1f), TextAlignmentOptions.MidlineLeft, FontStyles.Bold);
            _worldDescText = NativeStyleProvider.CreateLabel(metaCol.transform, "", 11f, NativeStyleProvider.TextLight, TextAlignmentOptions.TopLeft, wordWrap: true);
            _worldHazardsText = NativeStyleProvider.CreateLabel(metaCol.transform, "", 10.5f, NativeStyleProvider.EmberOrange, TextAlignmentOptions.TopLeft, FontStyles.Bold, wordWrap: true);

            var rulesCard = NativeStyleProvider.CreatePanel(_newRunSetupGroup.transform, "DifficultyRulesCard", NativeStyleProvider.PanelSubBg, addBorder: false, isSubPanel: true);
            _rulesCardBg = rulesCard.GetComponent<Image>();
            var rv = rulesCard.AddComponent<VerticalLayoutGroup>();
            rv.padding = new RectOffset(10, 10, 7, 8);
            rv.spacing = 5f;
            rv.childControlWidth = true;
            rv.childControlHeight = true;
            rv.childForceExpandWidth = true;
            rv.childForceExpandHeight = false;

            var diffRow = CreateRow(rulesCard.transform, "DifficultyRow", 30f, 6f);
            _diffControlsGroupObj = CreateRow(diffRow.transform, "DiffControlsGroup", 30f, 6f);
            var diffLbl = BindLoc(NativeStyleProvider.CreateLabel(_diffControlsGroupObj.transform, "", 12f, NativeStyleProvider.EmberGold, TextAlignmentOptions.MidlineLeft, FontStyles.Bold, autoSize: true, minFontSize: 10f, addShadow: true), "lobby.select_difficulty");
            var dlLe = diffLbl.gameObject.AddComponent<LayoutElement>();
            dlLe.minWidth = 88f; dlLe.preferredWidth = 104f;

            var dPrev = NativeStyleProvider.CreateButton(_diffControlsGroupObj.transform, "<", NativeStyleProvider.ButtonSecondary, () => HostCycleDifficulty(-1), 13.5f, 28f);
            dPrev.GetComponent<LayoutElement>().preferredWidth = 30f;
            _diffPrevBtnObj = dPrev.gameObject;

            var diffPill = NativeStyleProvider.CreateBadgePill(_diffControlsGroupObj.transform, "DiffPill", NativeStyleProvider.CardBg, addBorder: true);
            var dpLe = diffPill.AddComponent<LayoutElement>();
            dpLe.minWidth = 108f; dpLe.preferredWidth = 124f; dpLe.minHeight = 28f; dpLe.preferredHeight = 28f;
            _difficultyBadgeBg = diffPill.GetComponent<Image>();
            _difficultyBadgeText = NativeStyleProvider.CreateLabel(diffPill.transform, "", 13f, Color.white, TextAlignmentOptions.Center, FontStyles.Bold, autoSize: true, minFontSize: 10.5f, addShadow: true);
            NativeStyleProvider.StretchFill(_difficultyBadgeText.rectTransform, 6f, 2f);

            var dNext = NativeStyleProvider.CreateButton(_diffControlsGroupObj.transform, ">", NativeStyleProvider.ButtonSecondary, () => HostCycleDifficulty(1), 13.5f, 28f);
            dNext.GetComponent<LayoutElement>().preferredWidth = 30f;
            _diffNextBtnObj = dNext.gameObject;

            var spacer = new GameObject("Spacer");
            spacer.transform.SetParent(diffRow.transform, false);
            spacer.AddComponent<LayoutElement>().flexibleWidth = 1f;

            var rulesModalBtn = NativeStyleProvider.CreateButton(diffRow.transform, CoopLoc.T("lobby.session_settings_btn"), NativeStyleProvider.ButtonPrimary, OpenSessionSettingsModal, 12f, 28f);
            var rmLe = rulesModalBtn.GetComponent<LayoutElement>();
            rmLe.minWidth = 148f; rmLe.preferredWidth = 172f;
            _openRulesBtnLabel = rulesModalBtn.GetComponentInChildren<TMP_Text>();

            _diffUnlockNoteText = NativeStyleProvider.CreateLabel(rulesCard.transform, "", 10.5f, NativeStyleProvider.StatusReady, TextAlignmentOptions.MidlineLeft, FontStyles.Italic);

            BindLoc(NativeStyleProvider.CreateLabel(rulesCard.transform, "", 11f, NativeStyleProvider.TextMuted, TextAlignmentOptions.MidlineLeft, FontStyles.Bold), "lobby.active_rules_title");

            var rulesScroll = NativeStyleProvider.CreateScrollView(rulesCard.transform, "ActiveRulesScroll", NativeStyleProvider.PanelWellBg, out RectTransform rulesContent, padding: 6f, spacing: 3f, addBorder: false);
            var rsLe = rulesScroll.gameObject.AddComponent<LayoutElement>();
            rsLe.minHeight = 36f;
            rsLe.preferredHeight = 44f;
            _activeRulesSummaryText = NativeStyleProvider.CreateLabel(rulesContent, "", 11.5f, NativeStyleProvider.TextLight, TextAlignmentOptions.TopLeft, wordWrap: true);
        }

        private void BuildRightPartyColumn(Transform parent)
        {
            var rightCol = NativeStyleProvider.CreatePanel(parent, "RightPartyCol", NativeStyleProvider.PanelSubBg, addBorder: true, isSubPanel: true);
            var le = rightCol.AddComponent<LayoutElement>();
            le.flexibleWidth = 1.0f;
            le.minWidth = 340f;

            var vlg = rightCol.AddComponent<VerticalLayoutGroup>();
            vlg.padding = new RectOffset(10, 10, 9, 9);
            vlg.spacing = 6f;
            vlg.childControlWidth = true;
            vlg.childControlHeight = true;
            vlg.childForceExpandWidth = true;
            vlg.childForceExpandHeight = false;

            var connHeader = CreateRow(rightCol.transform, "ConnHeader", 22f, 6f);
            _transportBadgeText = NativeStyleProvider.CreateLabel(connHeader.transform, "ONLINE P2P", 12f, NativeStyleProvider.EmberGold, TextAlignmentOptions.MidlineLeft, FontStyles.Bold, autoSize: true, minFontSize: 10f, addShadow: true);
            _transportBadgeText.gameObject.AddComponent<LayoutElement>().flexibleWidth = 1f;

            _readySummaryText = NativeStyleProvider.CreateLabel(connHeader.transform, "", 11f, NativeStyleProvider.TextMuted, TextAlignmentOptions.MidlineRight, FontStyles.Bold);

            var codeRow = CreateRow(rightCol.transform, "CodeBar", 26f, 5f);
            _lobbyCodeText = NativeStyleProvider.CreateLabel(codeRow.transform, "Code: -", 11.5f, NativeStyleProvider.TextLight, TextAlignmentOptions.MidlineLeft);
            _lobbyCodeText.gameObject.AddComponent<LayoutElement>().flexibleWidth = 1f;

            var hideBtn = NativeStyleProvider.CreateButton(codeRow.transform, CoopLoc.T("lobby.hide_code"), NativeStyleProvider.ButtonSecondary, () =>
            {
                _hideLobbyCode = !_hideLobbyCode;
                _dirty = true;
            }, 10.5f, 24f);
            var hbLe = hideBtn.GetComponent<LayoutElement>();
            hbLe.minWidth = 54f; hbLe.preferredWidth = 62f;
            _hideCodeBtnLabel = hideBtn.GetComponentInChildren<TMP_Text>();

            var copyBtn = CreateLocButton(codeRow.transform, "lobby.copy_code", NativeStyleProvider.ButtonSecondary, () =>
            {
                if (!string.IsNullOrEmpty(Net.LobbyCode))
                {
                    CopyToClipboard(Net.LobbyCode);
                    GameRefs.ShowGameToast($"Copied '{Net.LobbyCode}'", 1.8f);
                }
            }, 10.5f, 24f);
            var cbLe = copyBtn.GetComponent<LayoutElement>();
            cbLe.minWidth = 80f; cbLe.preferredWidth = 92f;

            var inviteBtn = CreateLocButton(rightCol.transform, "lobby.invite_friends", NativeStyleProvider.ButtonSecondary, () => Net.Steam.OpenSteamInviteOverlay(), 12f, 26f);
            _steamInviteBtnObj = inviteBtn.gameObject;

            var rosterScroll = NativeStyleProvider.CreateScrollView(rightCol.transform, "RosterScroll", NativeStyleProvider.PanelWellBg, out RectTransform rosterContent, padding: 5f, spacing: 4f, addBorder: false);
            _rosterScrollLe = rosterScroll.gameObject.AddComponent<LayoutElement>();
            _rosterScrollLe.minHeight = 52f;
            _rosterScrollLe.preferredHeight = 98f;
            _rosterListRoot = rosterContent;

            _feedScrollRect = NativeStyleProvider.CreateScrollView(rightCol.transform, "PreRunFeedScroll", EmberwardTheme.ChatSurface, out RectTransform feedContent, padding: 7f, spacing: 2f, addBorder: false);
            _feedScrollLe = _feedScrollRect.gameObject.AddComponent<LayoutElement>();
            _feedScrollLe.minHeight = 36f;
            _feedScrollLe.preferredHeight = 44f;

            _feedText = NativeStyleProvider.CreateLabel(feedContent, "", 11.5f, NativeStyleProvider.TextLight, TextAlignmentOptions.TopLeft, wordWrap: true, addShadow: true);
            _feedText.richText = true;
            _feedScrollRect.gameObject.SetActive(false);

            var chatRow = CreateRow(rightCol.transform, "ChatRow", 30f, 6f);
            _chatInput = NativeStyleProvider.CreateInputField(chatRow.transform, CoopLoc.T("lobby.chat_placeholder"), "", 28f, 11.5f);
            _chatInput.GetComponent<LayoutElement>().flexibleWidth = 1f;
            _chatInput.onSubmit.AddListener((UnityAction<string>)(msg => SubmitChat(msg)));

            var sendBtn = CreateLocButton(chatRow.transform, "lobby.send", NativeStyleProvider.ButtonSecondary, () => SubmitChat(_chatInput.text), 11.5f, 28f);
            sendBtn.GetComponent<LayoutElement>().preferredWidth = 64f;

            _hostFooterObj = new GameObject("HostFooter");
            _hostFooterObj.transform.SetParent(rightCol.transform, false);
            var hfv = _hostFooterObj.AddComponent<VerticalLayoutGroup>();
            hfv.spacing = 5f;
            hfv.childControlWidth = true;
            hfv.childControlHeight = true;
            hfv.childForceExpandWidth = true;
            hfv.childForceExpandHeight = false;

            _startRunBtn = NativeStyleProvider.CreateButton(_hostFooterObj.transform, CoopLoc.T("lobby.start_run"), NativeStyleProvider.ButtonSuccess, OnHostClickStartRun, 15f, 38f);
            _startRunBtnBg = _startRunBtn.GetComponent<Image>();
            _startRunBtnLabel = _startRunBtn.GetComponentInChildren<TMP_Text>();

            var hostSubRow = CreateRow(_hostFooterObj.transform, "HostSubRow", 30f, 5f);
            _hostColorValueText = BuildInlineColorStepper(hostSubRow.transform);

            _disbandBtn = CreateLocButton(hostSubRow.transform, "lobby.disband_lobby", NativeStyleProvider.ButtonDanger, () =>
            {
                _session.HostDisbandLobby();
                _dirty = true;
            }, 12f, 30f);
            _disbandBtn.GetComponent<LayoutElement>().flexibleWidth = 1f;

            _clientFooterObj = new GameObject("ClientFooter");
            _clientFooterObj.transform.SetParent(rightCol.transform, false);
            var cfv = _clientFooterObj.AddComponent<VerticalLayoutGroup>();
            cfv.spacing = 5f;
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
            }, 15f, 38f);
            _readyBtnBg = _readyBtn.GetComponent<Image>();
            _readyBtnLabel = _readyBtn.GetComponentInChildren<TMP_Text>();

            var clientSubRow = CreateRow(_clientFooterObj.transform, "ClientSubRow", 30f, 5f);
            _clientColorValueText = BuildInlineColorStepper(clientSubRow.transform);

            _leaveBtn = CreateLocButton(clientSubRow.transform, "lobby.leave", NativeStyleProvider.ButtonDanger, () =>
            {
                Net.Disconnect(intentional: true);
                Close();
            }, 12f, 30f);
            _leaveBtn.GetComponent<LayoutElement>().flexibleWidth = 1f;
        }

        private TMP_Text BuildInlineColorStepper(Transform parent)
        {
            var prev = NativeStyleProvider.CreateButton(parent, "<", NativeStyleProvider.ButtonSecondary, () =>
            {
                Net.CycleLocalColor(-1);
                _dirty = true;
            }, 12.5f, 30f);
            prev.GetComponent<LayoutElement>().preferredWidth = 26f;

            var pill = NativeStyleProvider.CreateBadgePill(parent, "ColorPill", NativeStyleProvider.CardBg, addBorder: true);
            var pLe = pill.AddComponent<LayoutElement>();
            pLe.minWidth = 76f; pLe.preferredWidth = 84f; pLe.minHeight = 28f; pLe.preferredHeight = 28f;
            var valTxt = NativeStyleProvider.CreateLabel(pill.transform, "", 11.5f, NativeStyleProvider.EmberGold, TextAlignmentOptions.Center, FontStyles.Bold, autoSize: true, minFontSize: 9.5f, addShadow: true);
            NativeStyleProvider.StretchFill(valTxt.rectTransform, 4f, 2f);

            var next = NativeStyleProvider.CreateButton(parent, ">", NativeStyleProvider.ButtonSecondary, () =>
            {
                Net.CycleLocalColor(1);
                _dirty = true;
            }, 12.5f, 30f);
            next.GetComponent<LayoutElement>().preferredWidth = 26f;
            return valTxt;
        }

        private void BuildSessionSettingsModal(Transform parent)
        {
            _settingsModalRoot = new GameObject("SessionSettingsModal");
            _settingsModalRoot.transform.SetParent(parent, false);
            NativeStyleProvider.StretchFill(_settingsModalRoot.AddComponent<RectTransform>());
            _settingsModalRoot.AddComponent<Image>().color = EmberwardTheme.ModalScrim;

            var card = NativeStyleProvider.CreatePanel(_settingsModalRoot.transform, "SettingsCard", NativeStyleProvider.PanelBg, addBorder: true, roleOverride: PanelSurfaceRole.Window);
            _settingsCardRt = card.GetComponent<RectTransform>();
            _settingsCardRt.anchorMin = new Vector2(0.5f, 0.5f);
            _settingsCardRt.anchorMax = new Vector2(0.5f, 0.5f);
            _settingsCardRt.sizeDelta = new Vector2(780f, 385f);
            _settingsCardCg = card.AddComponent<CanvasGroup>();

            var cardFit = card.AddComponent<ContentSizeFitter>();
            cardFit.horizontalFit = ContentSizeFitter.FitMode.Unconstrained;
            cardFit.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

            var vlg = card.AddComponent<VerticalLayoutGroup>();
            vlg.padding = new RectOffset(20, 20, 17, 18);
            vlg.spacing = 7f;
            vlg.childControlWidth = true;
            vlg.childControlHeight = true;
            vlg.childForceExpandWidth = true;
            vlg.childForceExpandHeight = false;

            var hdr = CreateRow(card.transform, "ModalHdr", 32f, 8f);
            var t = BindLoc(NativeStyleProvider.CreateLabel(hdr.transform, "", 17.5f, NativeStyleProvider.EmberGold, TextAlignmentOptions.MidlineLeft, FontStyles.Bold, autoSize: true, minFontSize: 13f, addShadow: true), "lobby.subtab_settings");
            t.gameObject.AddComponent<LayoutElement>().flexibleWidth = 1f;
            NativeStyleProvider.CreateCloseButton(hdr.transform, () => _settingsModalRoot.SetActive(false), 28f);

            _settingsModalSubtitle = NativeStyleProvider.CreateLabel(card.transform, "", 12f, NativeStyleProvider.TextMuted);

            var presetRow = CreateRow(card.transform, "PresetsRow", 30f, 6f);
            AddPresetButton(presetRow.transform, "preset.balanced", SessionPreset.BalancedCoop);
            AddPresetButton(presetRow.transform, "preset.casual", SessionPreset.Casual);
            AddPresetButton(presetRow.transform, "preset.hardcore", SessionPreset.TacticalHardcore);
            AddPresetButton(presetRow.transform, "preset.chaos", SessionPreset.Chaos);

            var customPresetRow = CreateRow(card.transform, "CustomPresetBar", 30f, 6f);
            _presetNameInput = NativeStyleProvider.CreateInputField(customPresetRow.transform, CoopLoc.T("preset.save_placeholder"), "", 28f, 12f);
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
            }, 12f, 28f);
            savePresetBtn.GetComponent<LayoutElement>().preferredWidth = 84f;

            var cycleSavedBtn = NativeStyleProvider.CreateButton(customPresetRow.transform, "-", NativeStyleProvider.CardBg, () =>
            {
                RefreshSavedPresetsList();
                if (_savedPresets.Count > 0)
                {
                    _selectedSavedPresetIdx = (_selectedSavedPresetIdx + 1) % _savedPresets.Count;
                    _dirty = true;
                }
            }, 12f, 28f);
            cycleSavedBtn.GetComponent<LayoutElement>().preferredWidth = 165f;
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
            }, 12f, 28f);
            loadPresetBtn.GetComponent<LayoutElement>().preferredWidth = 84f;
            _hostOnlySettingButtons.Add(loadPresetBtn);

            var scroll = NativeStyleProvider.CreateScrollView(card.transform, "SettingsScroll", NativeStyleProvider.PanelWellBg, out RectTransform scrollContent, padding: 8f, spacing: 6f);
            var sLe = scroll.gameObject.AddComponent<LayoutElement>();
            sLe.minHeight = 206f;
            sLe.preferredHeight = 216f;

            var gridRow = new GameObject("SettingsCols");
            gridRow.transform.SetParent(scrollContent, false);
            var gHlg = gridRow.AddComponent<HorizontalLayoutGroup>();
            gHlg.spacing = 14f;
            gHlg.childControlWidth = true;
            gHlg.childControlHeight = true;
            gHlg.childForceExpandWidth = true;
            gHlg.childForceExpandHeight = false;

            var leftCol = new GameObject("LeftCol");
            leftCol.transform.SetParent(gridRow.transform, false);
            leftCol.AddComponent<LayoutElement>().flexibleWidth = 1f;
            var lv = leftCol.AddComponent<VerticalLayoutGroup>();
            lv.spacing = 6f;
            lv.childControlWidth = true;
            lv.childControlHeight = true;
            lv.childForceExpandWidth = true;
            lv.childForceExpandHeight = false;

            var rightCol = new GameObject("RightCol");
            rightCol.transform.SetParent(gridRow.transform, false);
            rightCol.AddComponent<LayoutElement>().flexibleWidth = 1f;
            var rv = rightCol.AddComponent<VerticalLayoutGroup>();
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
                else if (Settings.SharedTurnBlockCap > 1) Settings.SharedTurnBlockCap--;
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

        private void BuildAvatarPickerModal(Transform parent)
        {
            _avatarPickerModalRoot = new GameObject("HeroAvatarPickerModal");
            _avatarPickerModalRoot.transform.SetParent(parent, false);
            NativeStyleProvider.StretchFill(_avatarPickerModalRoot.AddComponent<RectTransform>());
            var dimImg = _avatarPickerModalRoot.AddComponent<Image>();
            dimImg.color = EmberwardTheme.ModalScrim;
            var dimBtn = _avatarPickerModalRoot.AddComponent<Button>();
            dimBtn.transition = Selectable.Transition.None;
            dimBtn.onClick.AddListener((UnityAction)(() =>
            {
                _avatarPickerModalRoot.SetActive(false);
                NativeStyleProvider.PlayUiOpenSound(false);
            }));

            var card = NativeStyleProvider.CreatePanel(_avatarPickerModalRoot.transform, "AvatarPickerCard", NativeStyleProvider.PanelBg, addBorder: true, roleOverride: PanelSurfaceRole.Window);
            _avatarPickerCardRt = card.GetComponent<RectTransform>();
            _avatarPickerCardRt.anchorMin = new Vector2(0.5f, 0.5f);
            _avatarPickerCardRt.anchorMax = new Vector2(0.5f, 0.5f);
            _avatarPickerCardRt.sizeDelta = new Vector2(502f, 360f);
            _avatarPickerCardCg = card.AddComponent<CanvasGroup>();

            var cardFit = card.AddComponent<ContentSizeFitter>();
            cardFit.horizontalFit = ContentSizeFitter.FitMode.Unconstrained;
            cardFit.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

            var vlg = card.AddComponent<VerticalLayoutGroup>();
            vlg.padding = new RectOffset(20, 20, 17, 18);
            vlg.spacing = 7f;
            vlg.childControlWidth = true;
            vlg.childControlHeight = true;
            vlg.childForceExpandWidth = true;
            vlg.childForceExpandHeight = false;

            var hdr = CreateRow(card.transform, "AvatarHdr", 28f, 8f);
            var title = BindLoc(NativeStyleProvider.CreateLabel(hdr.transform, "", 16.5f, NativeStyleProvider.EmberGold, TextAlignmentOptions.MidlineLeft, FontStyles.Bold, autoSize: true, minFontSize: 13f, addShadow: true), "lobby.select_avatar_title");
            title.gameObject.AddComponent<LayoutElement>().flexibleWidth = 1f;
            NativeStyleProvider.CreateCloseButton(hdr.transform, () =>
            {
                _avatarPickerModalRoot.SetActive(false);
                NativeStyleProvider.PlayUiOpenSound(false);
            }, 26f);

            NativeStyleProvider.CreateDivider(card.transform, "AvatarDiv");

            var scroll = NativeStyleProvider.CreateScrollView(card.transform, "AvatarGridScroll", NativeStyleProvider.PanelWellBg, out RectTransform scrollContent, padding: 8f, spacing: 8f);
            _avatarScrollLe = scroll.gameObject.AddComponent<LayoutElement>();
            _avatarScrollLe.minHeight = 116f;
            _avatarScrollLe.preferredHeight = 224f;

            var oldVlg = scrollContent.GetComponent<VerticalLayoutGroup>();
            if (oldVlg != null) UnityEngine.Object.DestroyImmediate(oldVlg);
            var grid = scrollContent.gameObject.AddComponent<GridLayoutGroup>();
            grid.padding = new RectOffset(8, 8, 8, 8);
            grid.cellSize = new Vector2(106f, 98f);
            grid.spacing = new Vector2(8f, 8f);
            grid.constraint = GridLayoutGroup.Constraint.FixedColumnCount;
            grid.constraintCount = 4;
            grid.childAlignment = TextAnchor.UpperCenter;
            _avatarGridContent = scrollContent;

            var foot = CreateRow(card.transform, "AvatarFooter", 28f, 8f);
            _avatarHoverLabel = NativeStyleProvider.CreateLabel(foot.transform, "", 12.5f, NativeStyleProvider.EmberGold, TextAlignmentOptions.MidlineLeft, FontStyles.Bold, autoSize: true, minFontSize: 10.5f, addShadow: true);
            _avatarHoverLabel.gameObject.AddComponent<LayoutElement>().flexibleWidth = 1f;

            var closeBtn = CreateLocButton(foot.transform, "modcfg.close", NativeStyleProvider.ButtonSecondary, () =>
            {
                _avatarPickerModalRoot.SetActive(false);
                NativeStyleProvider.PlayUiOpenSound(false);
            }, 12f, 26f);
            closeBtn.GetComponent<LayoutElement>().preferredWidth = 90f;

            _avatarPickerModalRoot.SetActive(false);
        }

        private void OpenAvatarPickerModal()
        {
            if (_avatarPickerModalRoot == null) return;
            NativeAssetRegistry.EnsureScanned(force: true);
            _avatarPickerAnimProgress = 0f;
            RebuildAvatarPickerGrid();
            _avatarPickerModalRoot.SetActive(true);
            NativeStyleProvider.PlayUiOpenSound(true);
        }

        private void RebuildAvatarPickerGrid()
        {
            if (_avatarGridContent == null) return;
            ClearChildren(_avatarGridContent);
            _avatarTileRects.Clear();

            var heroes = NativeAssetRegistry.GetPlayableHeroes();
            string currentHeroId = Net.LocalAvatarHeroId;

            for (int i = 0; i < heroes.Count; i++)
            {
                var hero = heroes[i];
                bool isSelected = string.Equals(hero.HeroId, currentHeroId, StringComparison.OrdinalIgnoreCase);

                var tile = NativeStyleProvider.CreatePanel(
                    _avatarGridContent,
                    $"HeroTile_{hero.HeroId}",
                    isSelected ? EmberwardTheme.SelectedTileBg : NativeStyleProvider.CardBg,
                    addBorder: false,
                    isSubPanel: true,
                    roleOverride: isSelected ? PanelSurfaceRole.CustomAccent : PanelSurfaceRole.Card);

                var tv = tile.AddComponent<VerticalLayoutGroup>();
                tv.padding = new RectOffset(5, 5, 5, 4);
                tv.spacing = 4f;
                tv.childAlignment = TextAnchor.MiddleCenter;
                tv.childControlWidth = false;
                tv.childControlHeight = false;
                tv.childForceExpandWidth = false;
                tv.childForceExpandHeight = false;

                NativeStyleProvider.CreateHeroAvatarWidget(
                    tile.transform,
                    hero.HeroId,
                    Color.white,
                    62f,
                    clickable: false,
                    showEditBadge: false,
                    selected: isSelected);

                var nameLbl = NativeStyleProvider.CreateLabel(
                    tile.transform,
                    hero.DisplayName,
                    11f,
                    isSelected ? NativeStyleProvider.EmberGold : NativeStyleProvider.TextLight,
                    TextAlignmentOptions.Center,
                    isSelected ? FontStyles.Bold : FontStyles.Normal,
                    wordWrap: false,
                    autoSize: true,
                    minFontSize: 8.5f,
                    addShadow: true);
                var nRt = nameLbl.rectTransform;
                nRt.sizeDelta = new Vector2(96f, 17f);

                string chosenId = hero.HeroId;
                var btn = tile.AddComponent<Button>();
                btn.targetGraphic = tile.GetComponent<Image>();
                var cols = btn.colors;
                cols.normalColor = Color.white;
                cols.highlightedColor = new Color(1.20f, 1.15f, 1.05f, 1f);
                cols.pressedColor = new Color(0.86f, 0.80f, 0.74f, 1f);
                cols.fadeDuration = 0.07f;
                btn.colors = cols;
                NativeStyleProvider.AttachHoverSound(tile, 1.14f);

                btn.onClick.AddListener((UnityAction)(() =>
                {
                    NativeStyleProvider.PlayUiSelectSound(1.18f);
                    Net.SetLocalAvatar(chosenId);
                    _dirty = true;
                    _avatarPickerModalRoot.SetActive(false);
                }));

                _avatarTileRects.Add((hero.HeroId, hero.DisplayName, tile.GetComponent<RectTransform>()));
            }

            if (_avatarScrollLe != null)
            {
                int rows = Math.Max(1, Mathf.CeilToInt(heroes.Count / 4f));
                _avatarScrollLe.preferredHeight = Mathf.Clamp(rows * 106f + 14f, 118f, 332f);
            }
            if (_avatarPickerCardRt != null)
                LayoutRebuilder.ForceRebuildLayoutImmediate(_avatarPickerCardRt);

            UpdateAvatarPickerHoverCaption();
        }

        private void UpdateAvatarPickerHoverCaption()
        {
            if (_avatarHoverLabel == null) return;
            Vector2 mouse = Input.mousePosition;
            for (int i = 0; i < _avatarTileRects.Count; i++)
            {
                var (hid, dname, rt) = _avatarTileRects[i];
                if (rt != null && RectTransformUtility.RectangleContainsScreenPoint(rt, mouse, null))
                {
                    bool active = string.Equals(hid, Net.LocalAvatarHeroId, StringComparison.OrdinalIgnoreCase);
                    _avatarHoverLabel.text = active
                        ? CoopLoc.F("lobby.avatar_selected", dname)
                        : dname;
                    return;
                }
            }

            var current = NativeAssetRegistry.GetHeroAvatar(Net.LocalAvatarHeroId);
            _avatarHoverLabel.text = CoopLoc.F("lobby.avatar_selected", current.DisplayName);
        }

        private void BuildStartOverrideDialog(Transform parent)
        {
            _overrideDialogRoot = new GameObject("StartOverrideDialog");
            _overrideDialogRoot.transform.SetParent(parent, false);
            NativeStyleProvider.StretchFill(_overrideDialogRoot.AddComponent<RectTransform>());
            _overrideDialogRoot.AddComponent<Image>().color = EmberwardTheme.ModalScrim;

            var card = NativeStyleProvider.CreatePanel(_overrideDialogRoot.transform, "OverrideCard", NativeStyleProvider.PanelBg, addBorder: true);
            var rt = card.GetComponent<RectTransform>();
            rt.anchorMin = new Vector2(0.5f, 0.5f);
            rt.anchorMax = new Vector2(0.5f, 0.5f);
            rt.sizeDelta = new Vector2(440f, 175f);
            var fit = card.AddComponent<ContentSizeFitter>();
            fit.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

            var vlg = card.AddComponent<VerticalLayoutGroup>();
            vlg.padding = new RectOffset(20, 20, 16, 16);
            vlg.spacing = 12f;
            vlg.childControlWidth = true;
            vlg.childControlHeight = true;
            vlg.childForceExpandWidth = true;
            vlg.childForceExpandHeight = false;

            BindLoc(NativeStyleProvider.CreateLabel(card.transform, "", 17f, NativeStyleProvider.EmberGold, TextAlignmentOptions.Center, FontStyles.Bold, autoSize: true, minFontSize: 13f, addShadow: true), "lobby.start_run_override_title");
            _overrideBodyText = NativeStyleProvider.CreateLabel(card.transform, "", 13.5f, NativeStyleProvider.TextLight, TextAlignmentOptions.Center, wordWrap: true);

            var row = CreateRow(card.transform, "Btns", 36f, 10f);
            var cancelBtn = CreateLocButton(row.transform, "lobby.cancel", NativeStyleProvider.ButtonSecondary, () => _overrideDialogRoot.SetActive(false), 13.5f, 34f);
            cancelBtn.GetComponent<LayoutElement>().flexibleWidth = 1f;

            var forceBtn = CreateLocButton(row.transform, "lobby.start_anyway", NativeStyleProvider.ButtonPrimary, () =>
            {
                _overrideDialogRoot.SetActive(false);
                _session.HostLaunchCoopRun();
            }, 13.5f, 34f);
            forceBtn.GetComponent<LayoutElement>().flexibleWidth = 1f;

            _overrideDialogRoot.SetActive(false);
        }

        private void BuildHostClosedDialog(Transform parent)
        {
            _hostClosedDialogRoot = new GameObject("HostClosedDialog");
            _hostClosedDialogRoot.transform.SetParent(parent, false);
            NativeStyleProvider.StretchFill(_hostClosedDialogRoot.AddComponent<RectTransform>());
            _hostClosedDialogRoot.AddComponent<Image>().color = EmberwardTheme.ModalScrim;

            var card = NativeStyleProvider.CreatePanel(_hostClosedDialogRoot.transform, "ClosedCard", NativeStyleProvider.PanelBg, addBorder: true);
            var rt = card.GetComponent<RectTransform>();
            rt.anchorMin = new Vector2(0.5f, 0.5f);
            rt.anchorMax = new Vector2(0.5f, 0.5f);
            rt.sizeDelta = new Vector2(430f, 165f);
            var fit = card.AddComponent<ContentSizeFitter>();
            fit.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

            var vlg = card.AddComponent<VerticalLayoutGroup>();
            vlg.padding = new RectOffset(20, 20, 16, 16);
            vlg.spacing = 12f;
            vlg.childControlWidth = true;
            vlg.childControlHeight = true;
            vlg.childForceExpandWidth = true;
            vlg.childForceExpandHeight = false;

            BindLoc(NativeStyleProvider.CreateLabel(card.transform, "", 17f, NativeStyleProvider.EmberOrange, TextAlignmentOptions.Center, FontStyles.Bold, autoSize: true, minFontSize: 13f, addShadow: true), "lobby.host_closed_title");
            _hostClosedBodyText = NativeStyleProvider.CreateLabel(card.transform, CoopLoc.T("lobby.host_closed_body"), 13.5f, NativeStyleProvider.TextLight, TextAlignmentOptions.Center, wordWrap: true);

            var okBtn = CreateLocButton(card.transform, "lobby.ok", NativeStyleProvider.ButtonPrimary, () => _hostClosedDialogRoot.SetActive(false), 14f, 34f);
            okBtn.GetComponent<LayoutElement>().minHeight = 34f;

            _hostClosedDialogRoot.SetActive(false);
        }

        private void HostSetLoadRunMode(bool loadMode)
        {
            if (!Net.IsHost || Setup.IsLaunchingRun) return;
            if (Setup.IsLoadRunMode == loadMode) return;
            Setup.IsLoadRunMode = loadMode;
            if (loadMode)
            {
                RefreshCoopSavesList();
                if (_cachedCoopSaves.Count > 0 && string.IsNullOrEmpty(Setup.SelectedSaveId))
                {
                    Setup.SelectedSaveId = _cachedCoopSaves[0].SaveId;
                    Setup.SelectedSaveSummary = _cachedCoopSaves[0].BuildCompactSummary();
                }
            }
            _session.HostBroadcastLobbySetup(LobbySetupChangeMask.SaveMode);
        }

        private void RefreshCoopSavesList()
        {
            _cachedCoopSaves = CoopSaveStorage.ListSaves();
            if (!string.IsNullOrEmpty(Setup.SelectedSaveId))
            {
                bool found = false;
                for (int i = 0; i < _cachedCoopSaves.Count; i++)
                {
                    if (_cachedCoopSaves[i].SaveId == Setup.SelectedSaveId)
                    {
                        Setup.SelectedSaveSummary = _cachedCoopSaves[i].BuildCompactSummary();
                        found = true;
                        break;
                    }
                }
                if (!found)
                {
                    Setup.SelectedSaveId = _cachedCoopSaves.Count > 0 ? _cachedCoopSaves[0].SaveId : "";
                    Setup.SelectedSaveSummary = _cachedCoopSaves.Count > 0 ? _cachedCoopSaves[0].BuildCompactSummary() : "";
                }
            }
        }

        private void RebuildCoopSavesList()
        {
            if (_saveListRoot == null) return;
            ClearChildren(_saveListRoot);

            if (_cachedCoopSaves.Count == 0)
            {
                var empty = NativeStyleProvider.CreateLabel(_saveListRoot, CoopLoc.T("save.no_saves"), 12f, NativeStyleProvider.TextMuted, TextAlignmentOptions.Center, FontStyles.Italic, wordWrap: true);
                empty.gameObject.AddComponent<LayoutElement>().minHeight = 46f;
                return;
            }

            for (int i = 0; i < _cachedCoopSaves.Count; i++)
            {
                var sv = _cachedCoopSaves[i];
                bool selected = sv.SaveId == Setup.SelectedSaveId;

                var row = NativeStyleProvider.CreatePanel(
                    _saveListRoot,
                    $"Save_{sv.SaveId}",
                    selected ? EmberwardTheme.SelectedTileBg : NativeStyleProvider.CardBg,
                    addBorder: false,
                    isSubPanel: true,
                    roleOverride: selected ? PanelSurfaceRole.CustomAccent : PanelSurfaceRole.Card);
                var rle = row.AddComponent<LayoutElement>();
                rle.minHeight = 44f; rle.preferredHeight = 44f;

                var hlg = row.AddComponent<HorizontalLayoutGroup>();
                hlg.padding = new RectOffset(8, 6, 4, 4);
                hlg.spacing = 6f;
                hlg.childAlignment = TextAnchor.MiddleLeft;
                hlg.childControlWidth = true;
                hlg.childControlHeight = true;
                hlg.childForceExpandWidth = false;

                var txtCol = new GameObject("Txt");
                txtCol.transform.SetParent(row.transform, false);
                txtCol.AddComponent<LayoutElement>().flexibleWidth = 1f;
                var tv = txtCol.AddComponent<VerticalLayoutGroup>();
                tv.spacing = 1f;
                tv.childControlWidth = true;
                tv.childControlHeight = true;
                tv.childForceExpandWidth = true;

                var heroEntry = NativeAssetRegistry.GetHeroAvatar(((eCharacterType)sv.HostHero).ToString());
                NativeStyleProvider.CreateLabel(txtCol.transform, $"{sv.DisplayName}  ·  {heroEntry.DisplayName}", 12f, selected ? NativeStyleProvider.EmberGold : NativeStyleProvider.TextLight, TextAlignmentOptions.MidlineLeft, FontStyles.Bold);
                NativeStyleProvider.CreateLabel(txtCol.transform, sv.BuildCompactSummary(), 10.5f, NativeStyleProvider.TextMuted, TextAlignmentOptions.MidlineLeft);

                string sid = sv.SaveId;
                string summary = sv.BuildCompactSummary();
                var rowBtn = row.AddComponent<Button>();
                rowBtn.targetGraphic = row.GetComponent<Image>();
                rowBtn.onClick.AddListener((UnityAction)(() =>
                {
                    if (!Net.IsHost || Setup.IsLaunchingRun) return;
                    Setup.SelectedSaveId = sid;
                    Setup.SelectedSaveSummary = summary;
                    _session.HostBroadcastLobbySetup(LobbySetupChangeMask.SaveMode, resetReadyOnMajor: false);
                }));

                var delBtn = NativeStyleProvider.CreateButton(row.transform, "×", NativeStyleProvider.ButtonDanger, () =>
                {
                    if (!Net.IsHost || Setup.IsLaunchingRun) return;
                    CoopSaveStorage.DeleteSave(sid);
                    RefreshCoopSavesList();
                    _session.HostBroadcastLobbySetup(LobbySetupChangeMask.SaveMode, resetReadyOnMajor: false);
                }, 13f, 26f);
                delBtn.GetComponent<LayoutElement>().preferredWidth = 28f;
            }
        }

        private void HostSetEconomyMode(CoopEconomyMode mode)
        {
            if (!Net.IsHost || Setup.IsLaunchingRun) return;
            if (Settings.EconomyMode == mode) return;
            Settings.EconomyMode = mode;
            _session.HostBroadcastLobbySetup(LobbySetupChangeMask.CoopMode);
        }

        private void HostCycleRunMode(int delta)
        {
            if (!Net.IsHost || Setup.IsLaunchingRun) return;
            var modes = WorldCatalog.GetPartySelectableRunModes(_session.Unlocks);
            if (modes.Count <= 1) return;

            int curIdx = modes.IndexOf(Setup.SelectedRunMode);
            if (curIdx < 0) curIdx = 0;

            int nextIdx = ((curIdx + delta) % modes.Count + modes.Count) % modes.Count;
            Setup.SelectedRunMode = modes[nextIdx];

            var validWorlds = WorldCatalog.GetHostSelectableWorlds(Setup.DifficultyTier, _session.Unlocks, Setup.SelectedRunMode);
            bool worldValid = false;
            for (int i = 0; i < validWorlds.Count; i++)
                if (validWorlds[i].WorldType == Setup.SelectedWorld) { worldValid = true; break; }
            if (!worldValid && validWorlds.Count > 0)
                Setup.SelectedWorld = validWorlds[0].WorldType;

            _session.SyncEndlessStageSelectionWithMode();
            _session.HostBroadcastLobbySetup(LobbySetupChangeMask.GameMode | (worldValid ? LobbySetupChangeMask.None : LobbySetupChangeMask.World));
        }

        private void HostCycleWorld(int delta)
        {
            if (!Net.IsHost || Setup.IsLaunchingRun) return;
            if (Setup.SelectedRunMode == CoopRunMode.EnigmaSanctum) return;

            var worlds = WorldCatalog.GetHostSelectableWorlds(Setup.DifficultyTier, _session.Unlocks, Setup.SelectedRunMode);
            if (worlds.Count == 0) return;

            int curIdx = 0;
            for (int i = 0; i < worlds.Count; i++)
                if (worlds[i].WorldType == Setup.SelectedWorld) { curIdx = i; break; }

            int nextIdx = ((curIdx + delta) % worlds.Count + worlds.Count) % worlds.Count;
            Setup.SelectedWorld = worlds[nextIdx].WorldType;
            _session.SyncEndlessStageSelectionWithMode();
            _session.HostBroadcastLobbySetup(LobbySetupChangeMask.World);
        }

        private void HostCycleEndlessStage(int delta)
        {
            if (!Net.IsHost || Setup.IsLaunchingRun || Setup.SelectedRunMode != CoopRunMode.Endless) return;
            var stages = WorldCatalog.GetEndlessStagesForWorld(Setup.SelectedWorld);
            if (stages.Count == 0) return;

            int nextIdx = ((Setup.SelectedSubStageIndex + delta) % stages.Count + stages.Count) % stages.Count;
            Setup.SelectedSubStageIndex = nextIdx;
            Setup.SelectedEndlessSceneName = stages[nextIdx].SceneName;
            Setup.SelectedEndlessType = stages[nextIdx].EndlessType;
            _session.HostBroadcastLobbySetup(LobbySetupChangeMask.GameMode);
        }

        private void HostCycleDifficulty(int delta)
        {
            if (!Net.IsHost || Setup.IsLaunchingRun) return;
            var diffs = WorldCatalog.GetPartySelectableDifficulties(_session.Unlocks);
            if (diffs.Count == 0) return;
            int curIdx = diffs.IndexOf(Setup.DifficultyTier);
            if (curIdx < 0) curIdx = 0;

            int nextIdx = ((curIdx + delta) % diffs.Count + diffs.Count) % diffs.Count;
            Setup.DifficultyTier = diffs[nextIdx];

            var unlocked = WorldCatalog.GetHostSelectableWorlds(Setup.DifficultyTier, _session.Unlocks, Setup.SelectedRunMode);
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

            if (!_session.ValidateRunStart(out string validationErr))
            {
                GameRefs.ShowGameToast(validationErr, 2.8f);
                return;
            }

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
            _settingsAnimProgress = 0f;
            RefreshSavedPresetsList();
            RefreshSettingsLabels();
            _settingsModalRoot.SetActive(true);
            NativeStyleProvider.PlayUiOpenSound(true);
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
            }, 12f, 28f);
            btn.GetComponent<LayoutElement>().flexibleWidth = 1f;
            _hostOnlySettingButtons.Add(btn);
        }

        private TMP_Text AddStepperRow(Transform parent, string locKey, Action onMinus, Action onPlus)
        {
            var row = CreateRow(parent, locKey, 32f, 4f);
            var lbl = BindLoc(NativeStyleProvider.CreateLabel(row.transform, "", 12.5f, NativeStyleProvider.TextMuted, TextAlignmentOptions.MidlineLeft, autoSize: true, minFontSize: 10f), locKey);
            lbl.gameObject.AddComponent<LayoutElement>().flexibleWidth = 1f;

            var minus = NativeStyleProvider.CreateButton(row.transform, "-", NativeStyleProvider.ButtonSecondary, () => { if (Net.IsHost && !Setup.IsLaunchingRun) onMinus(); }, 14f, 28f);
            minus.GetComponent<LayoutElement>().preferredWidth = 28f;
            _hostOnlySettingButtons.Add(minus);

            var val = NativeStyleProvider.CreateLabel(row.transform, "", 12.5f, NativeStyleProvider.TextLight, TextAlignmentOptions.Center, FontStyles.Bold, autoSize: true, minFontSize: 10f);
            val.gameObject.AddComponent<LayoutElement>().preferredWidth = 96f;

            var plus = NativeStyleProvider.CreateButton(row.transform, "+", NativeStyleProvider.ButtonSecondary, () => { if (Net.IsHost && !Setup.IsLaunchingRun) onPlus(); }, 14f, 28f);
            plus.GetComponent<LayoutElement>().preferredWidth = 28f;
            _hostOnlySettingButtons.Add(plus);
            return val;
        }

        private TMP_Text AddToggleRow(Transform parent, string locKey, Action onToggle)
        {
            var row = CreateRow(parent, locKey, 32f, 6f);
            var lbl = BindLoc(NativeStyleProvider.CreateLabel(row.transform, "", 12.5f, NativeStyleProvider.TextMuted, TextAlignmentOptions.MidlineLeft, autoSize: true, minFontSize: 10f), locKey);
            lbl.gameObject.AddComponent<LayoutElement>().flexibleWidth = 1f;

            var btn = NativeStyleProvider.CreateButton(row.transform, "", NativeStyleProvider.ButtonSecondary, () => { if (Net.IsHost && !Setup.IsLaunchingRun) onToggle(); }, 11.5f, 28f);
            btn.GetComponent<LayoutElement>().preferredWidth = 158f;
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
            _feedNeedsScrollBottom = true;
        }

        private void SelectTransportTab(bool steam)
        {
            if (_steamSubPanel != null) _steamSubPanel.SetActive(steam);
            if (_lanSubPanel != null) _lanSubPanel.SetActive(!steam);
            if (_tabSteamBg != null) _tabSteamBg.color = steam ? EmberwardTheme.TabActive : EmberwardTheme.TabInactive;
            if (_tabLanBg != null) _tabLanBg.color = !steam ? EmberwardTheme.TabActive : EmberwardTheme.TabInactive;
            if (_windowRt != null) LayoutRebuilder.ForceRebuildLayoutImmediate(_windowRt);
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
            if (_windowRt != null)
                _windowRt.sizeDelta = new Vector2(connected ? 940f : 780f, _windowRt.sizeDelta.y);
            _headerStatusText.text = launching
                ? CoopLoc.T("lobby.launching_run")
                : (connected ? Net.StatusText : $"v{CoopPlugin.PluginVersion}  ·  {Net.StatusText}");

            for (int i = 0; i < _hostOnlySettingButtons.Count; i++)
                if (_hostOnlySettingButtons[i] != null) _hostOnlySettingButtons[i].interactable = hostEditable;

            if (!connected)
            {
                bool steamReady = SteamP2PTransport.IsSteamReady();
                _steamAvailabilityText.text = steamReady
                    ? $"Steam: {SteamP2PTransport.GetLocalSteamName()} (P2P Relay)"
                    : "Steam offline — Direct LAN ready";
                _steamAvailabilityText.color = steamReady ? NativeStyleProvider.EmberGold : NativeStyleProvider.StatusBad;
                if (_steamHostBtn != null) _steamHostBtn.interactable = steamReady;
                if (_steamJoinCodeBtn != null) _steamJoinCodeBtn.interactable = steamReady;
                if (_steamRefreshBtn != null) _steamRefreshBtn.interactable = steamReady;

                RebuildLobbyList(_steamLobbyListRoot, Net.Steam.GetDiscoveredLobbies(), isSteam: true, _steamScrollLe);
                RebuildLobbyList(_lanLobbyListRoot, Net.Lan.GetDiscoveredLobbies(), isSteam: false, _lanScrollLe);
                if (_windowRt != null) LayoutRebuilder.ForceRebuildLayoutImmediate(_windowRt);
                return;
            }

            _clientReadOnlyBadgeObj.SetActive(isClient);
            if (_runModeSwitchRow != null) _runModeSwitchRow.SetActive(isHost);
            if (_runTabNewBg != null) _runTabNewBg.color = !Setup.IsLoadRunMode ? EmberwardTheme.TabActive : EmberwardTheme.TabInactive;
            if (_runTabLoadBg != null) _runTabLoadBg.color = Setup.IsLoadRunMode ? EmberwardTheme.TabActive : EmberwardTheme.TabInactive;
            if (_newRunSetupGroup != null) _newRunSetupGroup.SetActive(!Setup.IsLoadRunMode);
            if (_loadRunCard != null) _loadRunCard.SetActive(Setup.IsLoadRunMode);

            if (Setup.IsLoadRunMode)
            {
                if (_hostSaveListContainer != null) _hostSaveListContainer.SetActive(isHost);
                if (_clientLoadSaveSummaryText != null)
                {
                    _clientLoadSaveSummaryText.gameObject.SetActive(isClient);
                    if (isClient)
                    {
                        _clientLoadSaveSummaryText.text = string.IsNullOrWhiteSpace(Setup.SelectedSaveSummary)
                            ? CoopLoc.T("save.waiting_host_select")
                            : CoopLoc.F("save.client_resuming_summary", Setup.SelectedSaveSummary);
                    }
                }
                if (isHost) RebuildCoopSavesList();
            }

            _hostModeButtonsRow.SetActive(isHost);
            _clientModeReadOnlyRow.SetActive(isClient);

            var selectableRunModes = WorldCatalog.GetPartySelectableRunModes(_session.Unlocks);
            bool canCycleRunMode = hostEditable && selectableRunModes.Count > 1;
            if (_gameModePrevBtnObj != null) _gameModePrevBtnObj.SetActive(canCycleRunMode);
            if (_gameModeNextBtnObj != null) _gameModeNextBtnObj.SetActive(canCycleRunMode);
            if (_gameModeNameText != null) _gameModeNameText.text = WorldCatalog.GetRunModeDisplayName(Setup.SelectedRunMode);
            if (_gameModeDescText != null) _gameModeDescText.text = WorldCatalog.GetRunModeDescription(Setup.SelectedRunMode);
            if (_gameModeUnlockNoteText != null)
            {
                string mNote = _session.Unlocks.GetRunModeUnlockNote(Setup.SelectedRunMode);
                _gameModeUnlockNoteText.gameObject.SetActive(!string.IsNullOrEmpty(mNote));
                _gameModeUnlockNoteText.text = mNote;
            }

            bool isCampaign = Setup.SelectedRunMode == CoopRunMode.Campaign;
            bool isEndless = Setup.SelectedRunMode == CoopRunMode.Endless;
            bool isSanctum = Setup.SelectedRunMode == CoopRunMode.EnigmaSanctum;

            _worldPrevBtnObj.SetActive(hostEditable && !isSanctum);
            _worldNextBtnObj.SetActive(hostEditable && !isSanctum);
            if (_endlessStageRowObj != null) _endlessStageRowObj.SetActive(isEndless);
            if (_endlessStagePrevBtnObj != null) _endlessStagePrevBtnObj.SetActive(hostEditable && isEndless);
            if (_endlessStageNextBtnObj != null) _endlessStageNextBtnObj.SetActive(hostEditable && isEndless);

            if (_diffControlsGroupObj != null) _diffControlsGroupObj.SetActive(isCampaign);
            _diffPrevBtnObj.SetActive(hostEditable && isCampaign);
            _diffNextBtnObj.SetActive(hostEditable && isCampaign);

            bool isSeparate = Settings.EconomyMode == CoopEconomyMode.SeparateResources;
            if (_modeSharedBg != null) _modeSharedBg.color = !isSeparate ? NativeStyleProvider.ButtonPrimary : NativeStyleProvider.ButtonSecondary;
            if (_modeSeparateBg != null) _modeSeparateBg.color = isSeparate ? NativeStyleProvider.ButtonPrimary : NativeStyleProvider.ButtonSecondary;
            if (_clientModeReadOnlyText != null)
                _clientModeReadOnlyText.text = isSeparate ? CoopLoc.T("lobby.mode_separate") : CoopLoc.T("lobby.mode_shared");
            if (_modeRulesDescText != null)
                _modeRulesDescText.text = isSeparate ? CoopLoc.T("lobby.mode_separate_desc") : CoopLoc.T("lobby.mode_shared_desc");

            if (_worldBannerImage != null)
            {
                _worldBannerImage.sprite = NativeAssetRegistry.GetGameModeBannerSprite(Setup.SelectedRunMode, Setup.SelectedWorld, Setup.SelectedEndlessType);
                _worldBannerImage.preserveAspect = true;
            }

            if (isSanctum)
            {
                var sanctumEntry = WorldCatalog.GetEnigmaSanctumEntry();
                _worldNameText.text = sanctumEntry.DisplayName;
                _worldNameText.color = new Color(0.76f, 0.56f, 0.98f, 1f);
                if (_worldUnlockNoteText != null)
                {
                    string mNote = _session.Unlocks.GetRunModeUnlockNote(CoopRunMode.EnigmaSanctum);
                    _worldUnlockNoteText.gameObject.SetActive(!string.IsNullOrEmpty(mNote));
                    _worldUnlockNoteText.text = mNote;
                }
                _worldMetaBadgeText.text = sanctumEntry.Subtitle;
                _worldDescText.text = sanctumEntry.Description;
                _worldHazardsText.text = CoopLoc.T("mode.enigma_sanctum_hazards");
            }
            else if (isEndless)
            {
                var stages = WorldCatalog.GetEndlessStagesForWorld(Setup.SelectedWorld);
                int sIdx = Math.Clamp(Setup.SelectedSubStageIndex, 0, Math.Max(0, stages.Count - 1));
                var stageEntry = stages.Count > 0 ? stages[sIdx] : WorldCatalog.GetEnigmaSanctumEntry();
                Color worldAccent = WorldCatalog.GetWorldAccentColor(Setup.SelectedWorld);

                if (_endlessStageNameText != null)
                    _endlessStageNameText.text = stageEntry.DisplayName;

                _worldNameText.text = stageEntry.DisplayName;
                _worldNameText.color = worldAccent;
                if (_worldUnlockNoteText != null)
                {
                    string wNote = _session.Unlocks.GetWorldUnlockNote(Setup.SelectedWorld);
                    _worldUnlockNoteText.gameObject.SetActive(!string.IsNullOrEmpty(wNote));
                    _worldUnlockNoteText.text = wNote;
                }
                _worldMetaBadgeText.text = stageEntry.Subtitle;
                _worldDescText.text = stageEntry.Description;
                _worldHazardsText.text = CoopLoc.T(WorldCatalog.GetWorldInfo(Setup.SelectedWorld).HazardsKey);
            }
            else
            {
                var worldInfo = WorldCatalog.GetWorldInfo(Setup.SelectedWorld);
                Color worldAccent = WorldCatalog.GetWorldAccentColor(Setup.SelectedWorld);

                bool cleared = false;
                try
                {
                    using (PartyUnlockResolver.EnterRealUnlockRead())
                    {
                        var pld = GameDataManager.instance?.Playerdata;
                        if (pld != null) cleared = pld.IsWorldCleared(Setup.DifficultyTier, Setup.SelectedWorld);
                    }
                }
                catch { }

                _worldNameText.text = cleared
                    ? $"{CoopLoc.T(worldInfo.NameKey)}  ✓"
                    : CoopLoc.T(worldInfo.NameKey);
                _worldNameText.color = worldAccent;
                if (_worldUnlockNoteText != null)
                {
                    string wNote = _session.Unlocks.GetWorldUnlockNote(Setup.SelectedWorld);
                    _worldUnlockNoteText.gameObject.SetActive(!string.IsNullOrEmpty(wNote));
                    _worldUnlockNoteText.text = wNote;
                }
                _worldMetaBadgeText.text = CoopLoc.F("world.meta_format", worldInfo.MapSteps, worldInfo.WavesLabel);
                _worldDescText.text = CoopLoc.T(worldInfo.DescKey);
                _worldHazardsText.text = CoopLoc.T(worldInfo.HazardsKey);
            }

            Color diffCol = WorldCatalog.GetDifficultyColor(Setup.DifficultyTier);
            _difficultyBadgeText.text = WorldCatalog.GetDifficultyDisplayName(Setup.DifficultyTier);
            _difficultyBadgeText.color = Color.white;
            if (_difficultyBadgeBg != null)
                _difficultyBadgeBg.color = new Color(diffCol.r * 0.58f + 0.12f, diffCol.g * 0.58f + 0.10f, diffCol.b * 0.58f + 0.08f, 0.96f);

            if (_diffUnlockNoteText != null)
            {
                string dNote = isCampaign ? _session.Unlocks.GetDifficultyUnlockNote(Setup.DifficultyTier) : _session.Unlocks.GetRunModeUnlockNote(Setup.SelectedRunMode);
                _diffUnlockNoteText.gameObject.SetActive(!string.IsNullOrEmpty(dNote));
                _diffUnlockNoteText.text = dNote;
            }

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
                _readySummaryText.color = allReady ? NativeStyleProvider.StatusReady : NativeStyleProvider.EmberGold;
            }

            _hostFooterObj.SetActive(isHost);
            _clientFooterObj.SetActive(isClient);

            int localColIdx = Net.LocalColorIndex;
            string colName = CoopLoc.T(CoopConfig.GetPlayerColorNameKey(localColIdx));
            Color localCol = CoopConfig.GetPlayerColor(localColIdx);
            if (_hostColorValueText != null)
            {
                _hostColorValueText.text = colName;
                _hostColorValueText.color = localCol;
            }
            if (_clientColorValueText != null)
            {
                _clientColorValueText.text = colName;
                _clientColorValueText.color = localCol;
            }

            if (isHost)
            {
                if (_startRunBtn != null) _startRunBtn.interactable = !launching;
                if (_disbandBtn != null) _disbandBtn.interactable = !launching;
                if (_startRunBtnBg != null)
                    _startRunBtnBg.color = allReady ? NativeStyleProvider.ButtonSuccess : NativeStyleProvider.ButtonPrimary;
                if (_startRunBtnLabel != null)
                    _startRunBtnLabel.text = launching
                        ? CoopLoc.T("lobby.launching_run")
                        : (Setup.IsLoadRunMode ? CoopLoc.T("save.resume_run_btn") : CoopLoc.T("lobby.start_run"));
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

            var chatEntries = CoopLogger.GetChatSnapshot();
            bool hasChat = chatEntries.Count > 0;
            if (_feedScrollRect != null)
            {
                if (_feedScrollRect.gameObject.activeSelf != hasChat)
                    _feedScrollRect.gameObject.SetActive(hasChat);

                if (hasChat)
                {
                    int start = Math.Max(0, chatEntries.Count - 20);
                    int shown = chatEntries.Count - start;
                    var sb = new System.Text.StringBuilder();
                    for (int i = start; i < chatEntries.Count; i++)
                    {
                        if (i > start) sb.Append('\n');
                        sb.Append(chatEntries[i].RichText);
                    }
                    _feedText.text = sb.ToString();
                    if (_feedScrollLe != null)
                        _feedScrollLe.preferredHeight = Mathf.Clamp(shown * 19f + 16f, 38f, 118f);
                }
            }

            if (_windowRt != null)
                LayoutRebuilder.ForceRebuildLayoutImmediate(_windowRt);
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
                ? CoopLoc.F("settings.val_per_player_cap", Settings.SharedTurnBlockCap)
                : CoopLoc.T("settings.val_independent");
            _lblGifting.text = !Settings.AllowResourceGifting
                ? CoopLoc.T("settings.val_disabled")
                : (Settings.GiftPerWaveCap > 0 ? $"{Settings.GiftPerWaveCap}g/Wave" : CoopLoc.T("settings.val_unlimited"));
            _lblDraftSync.text = Settings.DraftMode == DraftSyncMode.SimultaneousIndependent ? CoopLoc.T("settings.val_simultaneous") : CoopLoc.T("settings.val_host_shared");
            _lblSpeedCtrl.text = Settings.SpeedControl == SpeedControlRule.AnyPlayer ? CoopLoc.T("settings.val_any_player") : CoopLoc.T("settings.val_host_only");
        }

        private void RebuildLobbyList(Transform container, IReadOnlyList<LobbySummary> lobbies, bool isSteam, LayoutElement scrollLe)
        {
            ClearChildren(container);

            int rendered = 0;
            if (lobbies != null)
            {
                for (int i = 0; i < lobbies.Count; i++)
                {
                    var lobby = lobbies[i];
                    if (!IsValidEmberwardLobbyForUi(lobby)) continue;

                    BuildLobbyRow(container, lobby, isSteam);
                    rendered++;
                }
            }

            if (rendered == 0)
            {
                var emptyLbl = NativeStyleProvider.CreateLabel(
                    container,
                    isSteam ? CoopLoc.T("lobby.no_steam_lobbies") : CoopLoc.T("lobby.listening_lan"),
                    12f,
                    NativeStyleProvider.TextMuted,
                    TextAlignmentOptions.Center,
                    FontStyles.Italic,
                    wordWrap: true);
                var ele = emptyLbl.gameObject.AddComponent<LayoutElement>();
                ele.minHeight = 34f;
                ele.preferredHeight = 36f;
            }

            if (scrollLe != null)
            {
                scrollLe.preferredHeight = rendered == 0
                    ? 50f
                    : Mathf.Clamp(rendered * 44f + 14f, 58f, 224f);
            }
        }

        private static bool IsValidEmberwardLobbyForUi(LobbySummary lobby)
        {
            if (lobby == null) return false;
            if (!string.Equals(lobby.GameTag, LobbyMetadataKeys.GameTagValue, StringComparison.Ordinal)) return false;
            if (!string.Equals(lobby.ModSignature, LobbyMetadataKeys.ModSignatureValue, StringComparison.Ordinal)) return false;
            if (string.IsNullOrWhiteSpace(lobby.HostName)) return false;
            if (!LobbyMetadataKeys.IsValidCoopMode(lobby.CoopMode)) return false;
            if (lobby.MaxPlayers < 2 || lobby.MaxPlayers > 6) return false;
            if (lobby.PlayerCount < 1 || lobby.PlayerCount >= lobby.MaxPlayers) return false;
            if (lobby.InProgress && !lobby.JoinableInProgress) return false;
            return true;
        }

        private void BuildLobbyRow(Transform container, LobbySummary lobby, bool isSteam)
        {
            var row = NativeStyleProvider.CreatePanel(container, $"Lobby_{lobby.LobbyKey}", NativeStyleProvider.CardBg, addBorder: true, isSubPanel: true);
            var rle = row.AddComponent<LayoutElement>();
            rle.minHeight = 40f;
            rle.preferredHeight = 40f;

            var hlg = row.AddComponent<HorizontalLayoutGroup>();
            hlg.padding = new RectOffset(10, 8, 5, 5);
            hlg.spacing = 8f;
            hlg.childAlignment = TextAnchor.MiddleLeft;
            hlg.childControlWidth = true;
            hlg.childControlHeight = true;
            hlg.childForceExpandWidth = false;

            var hostTmp = NativeStyleProvider.CreateLabel(row.transform, lobby.HostName, 13.5f, NativeStyleProvider.TextLight, TextAlignmentOptions.MidlineLeft, FontStyles.Bold);
            hostTmp.overflowMode = TextOverflowModes.Ellipsis;
            hostTmp.gameObject.AddComponent<LayoutElement>().flexibleWidth = 1f;

            bool isSep = string.Equals(lobby.CoopMode, LobbyMetadataKeys.ModeSeparate, StringComparison.Ordinal);
            Color modeBg = isSep ? NativeStyleProvider.ModeSeparateTint : NativeStyleProvider.ModeSharedTint;
            var modePill = NativeStyleProvider.CreateBadgePill(row.transform, "ModePill", modeBg);
            var mLe = modePill.AddComponent<LayoutElement>();
            mLe.minWidth = 96f; mLe.preferredWidth = 102f; mLe.minHeight = 24f; mLe.preferredHeight = 24f;
            var modeTxt = NativeStyleProvider.CreateLabel(
                modePill.transform,
                isSep ? CoopLoc.T("lobby.mode_separate_short") : CoopLoc.T("lobby.mode_shared_short"),
                11f,
                Color.white,
                TextAlignmentOptions.Center,
                FontStyles.Bold,
                autoSize: true,
                minFontSize: 9f);
            NativeStyleProvider.StretchFill(modeTxt.rectTransform, 6f, 2f);

            var playersTmp = NativeStyleProvider.CreateLabel(row.transform, $"{lobby.PlayerCount}/{lobby.MaxPlayers}", 13f, NativeStyleProvider.EmberGold, TextAlignmentOptions.Center, FontStyles.Bold);
            var pLe = playersTmp.gameObject.AddComponent<LayoutElement>();
            pLe.minWidth = 58f; pLe.preferredWidth = 68f;

            string pingLabel = lobby.PingMs >= 0 ? $"{lobby.PingMs} ms" : (isSteam ? "P2P" : "LAN");
            Color pingCol = lobby.PingMs < 0
                ? NativeStyleProvider.TextMuted
                : EmberwardTheme.GetPingColor(lobby.PingMs);
            var pingTmp = NativeStyleProvider.CreateLabel(row.transform, pingLabel, 12f, pingCol, TextAlignmentOptions.Center, FontStyles.Bold);
            var pgLe = pingTmp.gameObject.AddComponent<LayoutElement>();
            pgLe.minWidth = 58f; pgLe.preferredWidth = 68f;

            if (lobby.IsVersionMismatch)
            {
                var verPill = NativeStyleProvider.CreateBadgePill(row.transform, "VersionMismatch", new Color(0.56f, 0.24f, 0.20f, 0.95f), addBorder: true);
                var vLe = verPill.AddComponent<LayoutElement>();
                vLe.minWidth = 106f; vLe.preferredWidth = 112f; vLe.minHeight = 28f; vLe.preferredHeight = 28f;
                var vTxt = NativeStyleProvider.CreateLabel(
                    verPill.transform,
                    CoopLoc.F("lobby.version_mismatch", lobby.ModVersion),
                    10.5f,
                    NativeStyleProvider.StatusWarn,
                    TextAlignmentOptions.Center,
                    FontStyles.Bold,
                    autoSize: true,
                    minFontSize: 8.5f);
                NativeStyleProvider.StretchFill(vTxt.rectTransform, 4f, 2f);
            }
            else
            {
                var joinBtn = NativeStyleProvider.CreateButton(row.transform, CoopLoc.T("lobby.join_btn"), NativeStyleProvider.ButtonSuccess, () =>
                {
                    if (isSteam) Net.JoinSteam(lobby.SteamLobbyId);
                    else { ApplyLanName(); Net.JoinLan(lobby.AddressOrCode.Split(':')[0], lobby.Port); }
                    _dirty = true;
                }, 13f, 28f);
                var jLe = joinBtn.GetComponent<LayoutElement>();
                jLe.minWidth = 96f; jLe.preferredWidth = 112f;
            }
        }

        private void RebuildRosterList()
        {
            ClearChildren(_rosterListRoot);
            foreach (var kv in Net.Roster)
            {
                var p = kv.Value;
                bool isLocal = p.PlayerId == Net.LocalPlayerId;
                Color pCol = CoopConfig.GetPlayerColor(p.ColorIndex);
                HeroAvatarEntry hero = NativeAssetRegistry.GetHeroAvatar(p.AvatarHeroId);

                var rowBg = NativeStyleProvider.CreatePanel(_rosterListRoot, $"Peer_{p.PlayerId}", NativeStyleProvider.CardBg, addBorder: false, isSubPanel: true, roleOverride: PanelSurfaceRole.Card);
                var rle = rowBg.AddComponent<LayoutElement>();
                rle.minHeight = 42f; rle.preferredHeight = 42f;

                var hlg = rowBg.AddComponent<HorizontalLayoutGroup>();
                hlg.padding = new RectOffset(6, 8, 4, 4);
                hlg.spacing = 7f;
                hlg.childAlignment = TextAnchor.MiddleLeft;
                hlg.childControlWidth = true;
                hlg.childControlHeight = true;
                hlg.childForceExpandWidth = false;

                NativeStyleProvider.CreateHeroAvatarWidget(
                    rowBg.transform,
                    p.AvatarHeroId,
                    Color.white,
                    34f,
                    clickable: isLocal,
                    onClick: isLocal ? OpenAvatarPickerModal : (Action)null,
                    showEditBadge: isLocal);

                if (p.IsHost && NativeStyleProvider.NativeCrownSprite != null)
                {
                    var crownGo = new GameObject("Crown");
                    crownGo.transform.SetParent(rowBg.transform, false);
                    var cle = crownGo.AddComponent<LayoutElement>();
                    cle.minWidth = 14f; cle.preferredWidth = 14f; cle.minHeight = 14f; cle.preferredHeight = 14f;
                    var ci = crownGo.AddComponent<Image>();
                    ci.sprite = NativeStyleProvider.NativeCrownSprite;
                    ci.color = NativeStyleProvider.EmberGold;
                    ci.preserveAspect = true;
                    ci.raycastTarget = false;
                }

                var nameWrap = new GameObject("NameCol");
                nameWrap.transform.SetParent(rowBg.transform, false);
                nameWrap.AddComponent<LayoutElement>().flexibleWidth = 1f;
                var nv = nameWrap.AddComponent<VerticalLayoutGroup>();
                nv.spacing = 0f;
                nv.childAlignment = TextAnchor.MiddleLeft;
                nv.childControlWidth = true;
                nv.childControlHeight = true;
                nv.childForceExpandWidth = true;
                nv.childForceExpandHeight = false;

                var namePingRow = new GameObject("NamePingRow");
                namePingRow.transform.SetParent(nameWrap.transform, false);
                var npHlg = namePingRow.AddComponent<HorizontalLayoutGroup>();
                npHlg.spacing = 5f;
                npHlg.childAlignment = TextAnchor.MiddleLeft;
                npHlg.childControlWidth = true;
                npHlg.childControlHeight = true;
                npHlg.childForceExpandWidth = false;

                var nLbl = NativeStyleProvider.CreateLabel(namePingRow.transform, p.Name, 13f, pCol, TextAlignmentOptions.MidlineLeft, FontStyles.Bold, addShadow: true);
                nLbl.overflowMode = TextOverflowModes.Ellipsis;
                var nLe = nLbl.gameObject.AddComponent<LayoutElement>();
                nLe.flexibleWidth = 1f;
                nLe.minWidth = 42f;

                NativeStyleProvider.CreateInlinePingLabel(namePingRow.transform, p.PingMs, p.IsHost, 10.5f);

                string subLine = isLocal ? $"{hero.DisplayName}  ·  {CoopLoc.T("lobby.change_avatar_hint")}" : hero.DisplayName;
                var hLbl = NativeStyleProvider.CreateLabel(nameWrap.transform, subLine, 10f, isLocal ? NativeStyleProvider.EmberGoldSoft : NativeStyleProvider.TextMuted, TextAlignmentOptions.MidlineLeft);
                hLbl.overflowMode = TextOverflowModes.Ellipsis;

                string statusStr = p.IsHost ? "HOST" : (p.IsReady ? CoopLoc.T("lobby.ready") : CoopLoc.T("lobby.not_ready"));
                Color statusCol = p.IsHost
                    ? NativeStyleProvider.EmberGold
                    : (p.IsReady ? NativeStyleProvider.StatusReady : NativeStyleProvider.StatusWarn);
                var sLbl = NativeStyleProvider.CreateLabel(rowBg.transform, statusStr, 11.5f, statusCol, TextAlignmentOptions.MidlineRight, FontStyles.Bold, autoSize: true, minFontSize: 9.5f, addShadow: true);
                sLbl.gameObject.AddComponent<LayoutElement>().preferredWidth = 74f;
            }

            if (_rosterScrollLe != null)
            {
                int count = Math.Max(1, Net.Roster.Count);
                _rosterScrollLe.preferredHeight = Mathf.Clamp(count * 46f + 10f, 54f, 194f);
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
