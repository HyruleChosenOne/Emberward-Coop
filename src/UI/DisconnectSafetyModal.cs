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
            _root.SetActive(true);
            RefreshLocalizedTexts();

            PauseCombat(true);
            CoopLogger.Info(LogCategory.Sync, $"Auto-paused combat because {_disconnectedName} disconnected mid-wave.", addToFeed: true);
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
            _root.AddComponent<Image>().color = new Color(0.02f, 0.02f, 0.04f, 0.72f);

            var card = NativeStyleProvider.CreatePanel(_root.transform, "Card", NativeStyleProvider.PanelBg, addBorder: true);
            var rt = card.GetComponent<RectTransform>();
            rt.anchorMin = new Vector2(0.5f, 0.5f);
            rt.anchorMax = new Vector2(0.5f, 0.5f);
            rt.sizeDelta = new Vector2(520f, 230f);

            var vlg = card.AddComponent<VerticalLayoutGroup>();
            vlg.padding = new RectOffset(24, 24, 20, 20);
            vlg.spacing = 12f;
            vlg.childControlWidth = true;
            vlg.childControlHeight = true;
            vlg.childForceExpandWidth = true;
            vlg.childForceExpandHeight = false;

            _titleText = NativeStyleProvider.CreateLabel(card.transform, CoopLoc.T("disconnect.title"), 22f, NativeStyleProvider.EmberOrange, TextAlignmentOptions.Center, FontStyles.Bold);
            _waitingText = NativeStyleProvider.CreateLabel(card.transform, "", 16f, NativeStyleProvider.TextLight, TextAlignmentOptions.Center, FontStyles.Bold);
            _hintText = NativeStyleProvider.CreateLabel(card.transform, CoopLoc.T("disconnect.hint"), 13.5f, NativeStyleProvider.TextMuted, TextAlignmentOptions.Center);
            _hintText.enableWordWrapping = true;

            var btnRow = new GameObject("Buttons");
            btnRow.transform.SetParent(card.transform, false);
            var hlg = btnRow.AddComponent<HorizontalLayoutGroup>();
            hlg.spacing = 12f;
            hlg.childControlWidth = true;
            hlg.childControlHeight = true;
            hlg.childForceExpandWidth = true;
            btnRow.AddComponent<LayoutElement>().minHeight = 42f;

            var waitBtn = NativeStyleProvider.CreateButton(btnRow.transform, CoopLoc.T("disconnect.wait_btn"), NativeStyleProvider.ButtonSecondary, () =>
            {
                NativeStyleProvider.PlayUiClickSound(1.0f);
            }, 14f, 40f);
            _waitBtnText = waitBtn.GetComponentInChildren<TMP_Text>();

            var contBtn = NativeStyleProvider.CreateButton(btnRow.transform, CoopLoc.T("disconnect.continue_btn"), NativeStyleProvider.ButtonPrimary, DismissAndResume, 14f, 40f);
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
