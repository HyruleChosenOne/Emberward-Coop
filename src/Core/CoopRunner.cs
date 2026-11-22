using System;
using EmberwardCoop.Sync;
using EmberwardCoop.UI;
using UnityEngine;

namespace EmberwardCoop.Core
{
    public sealed class CoopRunner : MonoBehaviour
    {
        public CoopRunner(IntPtr ptr) : base(ptr) { }

        private SessionController _session;
        private CoopLobbyPanel _lobbyPanel;
        private CoopHudOverlay _hudOverlay;
        private DisconnectSafetyModal _disconnectModal;
        private CoopSettingsModal _settingsModal;

        private bool _wasInActiveRun;

        private void Awake()
        {
            CoopPlugin.ForceRunInBackground();
            _session = SessionController.Instance;
            if (_session != null)
            {
                _settingsModal = new CoopSettingsModal();
                _disconnectModal = new DisconnectSafetyModal(_session);
                _lobbyPanel = new CoopLobbyPanel(_session);
                _hudOverlay = new CoopHudOverlay(_session, _lobbyPanel);

                _session.OnMidWavePeerDisconnect += name => _disconnectModal?.TriggerForDisconnect(name);
                _session.OnPeerReconnected += name => _disconnectModal?.OnPeerReconnected(name);
                _session.OnRunLaunchStarted += () => _lobbyPanel?.ForceCloseForRun();
                _session.OnHostClosedLobby += reason => _lobbyPanel?.ShowHostClosedDialog(reason);
            }
        }

        private void Update()
        {
            if (_session == null) return;

            _session.Tick();

            bool inTitle = GameRefs.IsInTitle;
            bool inActiveRun = GameRefs.IsInMap || GameRefs.IsInBattle;

            // Close the pre-run lobby on any transition out of CoinPage.
            if (!inTitle && (_lobbyPanel?.IsOpen ?? false))
            {
                _lobbyPanel.ForceCloseForRun();
            }

            if (inActiveRun)
            {
                _wasInActiveRun = true;
            }
            else if (inTitle && _wasInActiveRun)
            {
                _wasInActiveRun = false;
                if (_session.Net.IsConnected)
                {
                    _lobbyPanel?.Open();
                }
            }

            bool settingsOpen = _settingsModal?.IsOpen ?? false;
            if (inTitle && !settingsOpen && Input.GetKeyDown(CoopConfig.ToggleLobbyKey.Value))
            {
                _lobbyPanel?.Toggle();
            }

            CoopInputGuard.IsCoopModalOpen =
                (_lobbyPanel?.IsOpen ?? false) ||
                (_settingsModal?.IsOpen ?? false) ||
                (_disconnectModal?.IsOpen ?? false);

            if (inTitle)
            {
                TitleMenuIntegration.TryInjectTitleButton(() => _lobbyPanel?.Open());
            }

            float now = Time.unscaledTime;
            _lobbyPanel?.Tick(now);
            _hudOverlay?.Tick(now);
            _disconnectModal?.Tick(now);
            _settingsModal?.Tick();
        }

        private void OnApplicationFocus(bool hasFocus)
        {
            if (!hasFocus && _session != null)
            {
                // Guarantee zero stuck-input or camera-lock states when window loses focus
                _session.Emotes.CloseWheel(absorbRelease: false);
                _session.Feedback.CancelAndCloseWheel(absorbRelease: false);
                _session.Sketch.CancelLocalDrag();
                CoopInputGuard.IsEmoteWheelOpen = false;
                CoopInputGuard.IsPingWheelOpen = false;
                CoopInputGuard.IsPingKeyHolding = false;
                CoopInputGuard.IsMazeSketching = false;
            }
        }

        private void OnApplicationQuit()
        {
            try { _session?.Net.Disconnect(intentional: true); } catch { }
        }
    }
}
