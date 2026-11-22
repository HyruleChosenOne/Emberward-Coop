using System;
using EmberwardCoop.Core;
using EmberwardCoop.Localization;
using EmberwardCoop.Sync;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace EmberwardCoop.UI
{
    public sealed class DisconnectSafetyModal
    {
        private readonly SessionController _session;

        private GameObject _root;
        private RectTransform _cardRt;
        private CanvasGroup _cardCg;
        private float _openAnimProgress = 1f;

        private TMP_Text _titleText;
        private TMP_Text _waitingText;
        private TMP_Text _hintText;
        private TMP_Text _waitBtnText;
        private TMP_Text _continueBtnText;

        private bool _isOpen;
        private string _disconnectedName = "";
        private float _disconnectedAtTime;
        private bool _pausedCombat;

        public bool IsOpen => _isOpen;

        public DisconnectSafetyModal(SessionController session)
        {
            _session = session;
            CoopLoc.OnLanguageChanged += RefreshLocalizedTexts;
        }

        public void TriggerForDisconnect(string playerName)
        {
            if (!CoopConfig.AutoPauseOnDisconnect.Value) return;
            if (!GameRefs.IsInBattle) return;

            var gsc = GameStateController.Instance;
            bool inWave = gsc != null && gsc.IsInBattle;
            if (!inWave) return;

            EnsureBuilt();
            _disconnectedName = string.IsNullOrWhiteSpace(playerName) ? "Teammate" : playerName;
            _disconnectedAtTime = Time.unscaledTime;
            _isOpen = true;
            _openAnimProgress = 0f;
            _root.SetActive(true);
            RefreshLocalizedTexts();
            NativeStyleProvider.PlayUiOpenSound(true);

            PauseCombat(true);
            CoopLogger.Info(LogCategory.Sync, $"Auto-paused combat because {_disconnectedName} disconnected mid-wave.");
        }

        public void OnPeerReconnected(string playerName)
        {
            if (!_isOpen) return;
            DismissAndResume();
            GameRefs.ShowGameToast($"{playerName} reconnected!", 2.5f);
        }

        public void DismissAndResume()
        {
            _isOpen = false;
            if (_root != null && _root.activeSelf)
                _root.SetActive(false);

            if (_pausedCombat)
                PauseCombat(false);
        }

        public void Tick(float now)
        {
            if (!_isOpen) return;

            NativeStyleProvider.TickOpenTransition(_cardRt, _cardCg, ref _openAnimProgress, Time.unscaledDeltaTime, 0.13f);

            if (!GameRefs.IsInBattle)
            {
                DismissAndResume();
                return;
            }

            int elapsed = Math.Max(0, Mathf.FloorToInt(now - _disconnectedAtTime));
            if (_waitingText != null)
                _waitingText.text = CoopLoc.F("disconnect.waiting", _disconnectedName, elapsed);
        }

        private void PauseCombat(bool paused)
        {
            _pausedCombat = paused;
            try
            {
                var gsc = GameSpeedController.Instance;
                if (gsc != null)
                    gsc.OnRequestModifySystemGameSpeed(paused ? 0f : 1f);

                if (_session.Net.IsHost)
                {
                    _session.Net.BroadcastToClients(Networking.PacketType.EvtPauseState, w => w.Write(paused));
                }
            }
            catch { }
        }

        private void EnsureBuilt()
        {
            if (_root != null) return;
            NativeStyleProvider.HarvestFromScene();
            var canvas = NativeStyleProvider.GetOrCreateOverlayCanvas();

            _root = new GameObject("EWCoop_DisconnectModal");
            _root.transform.SetParent(canvas.transform, false);
            NativeStyleProvider.StretchFill(_root.AddComponent<RectTransform>());
            _root.AddComponent<Image>().color = EmberwardTheme.ModalScrim;

            var card = NativeStyleProvider.CreatePanel(_root.transform, "Card", NativeStyleProvider.PanelBg, addBorder: true);
            _cardRt = card.GetComponent<RectTransform>();
            _cardRt.anchorMin = new Vector2(0.5f, 0.5f);
            _cardRt.anchorMax = new Vector2(0.5f, 0.5f);
            _cardRt.sizeDelta = new Vector2(460f, 195f);
            _cardCg = card.AddComponent<CanvasGroup>();

            var fit = card.AddComponent<ContentSizeFitter>();
            fit.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

            var vlg = card.AddComponent<VerticalLayoutGroup>();
            vlg.padding = new RectOffset(20, 20, 18, 18);
            vlg.spacing = 10f;
            vlg.childControlWidth = true;
            vlg.childControlHeight = true;
            vlg.childForceExpandWidth = true;
            vlg.childForceExpandHeight = false;

            _titleText = NativeStyleProvider.CreateLabel(card.transform, CoopLoc.T("disconnect.title"), 19f, NativeStyleProvider.EmberOrange, TextAlignmentOptions.Center, FontStyles.Bold, autoSize: true, minFontSize: 14f, addShadow: true);
            _waitingText = NativeStyleProvider.CreateLabel(card.transform, "", 14.5f, NativeStyleProvider.TextLight, TextAlignmentOptions.Center, FontStyles.Bold, wordWrap: true);
            _hintText = NativeStyleProvider.CreateLabel(card.transform, CoopLoc.T("disconnect.hint"), 12.5f, NativeStyleProvider.TextMuted, TextAlignmentOptions.Center, wordWrap: true);

            var btnRow = new GameObject("Buttons");
            btnRow.transform.SetParent(card.transform, false);
            var hlg = btnRow.AddComponent<HorizontalLayoutGroup>();
            hlg.spacing = 10f;
            hlg.childControlWidth = true;
            hlg.childControlHeight = true;
            hlg.childForceExpandWidth = true;
            btnRow.AddComponent<LayoutElement>().minHeight = 36f;

            var waitBtn = NativeStyleProvider.CreateButton(btnRow.transform, CoopLoc.T("disconnect.wait_btn"), NativeStyleProvider.ButtonSecondary, () =>
            {
                NativeStyleProvider.PlayUiClickSound(1.0f);
            }, 13f, 36f);
            _waitBtnText = waitBtn.GetComponentInChildren<TMP_Text>();

            var contBtn = NativeStyleProvider.CreateButton(btnRow.transform, CoopLoc.T("disconnect.continue_btn"), NativeStyleProvider.ButtonPrimary, DismissAndResume, 13f, 36f);
            _continueBtnText = contBtn.GetComponentInChildren<TMP_Text>();

            _root.SetActive(false);
        }

        private void RefreshLocalizedTexts()
        {
            if (_root == null) return;
            if (_titleText != null) _titleText.text = CoopLoc.T("disconnect.title");
            if (_hintText != null) _hintText.text = CoopLoc.T("disconnect.hint");
            if (_waitBtnText != null) _waitBtnText.text = CoopLoc.T("disconnect.wait_btn");
            if (_continueBtnText != null) _continueBtnText.text = CoopLoc.T("disconnect.continue_btn");
        }
    }
}
