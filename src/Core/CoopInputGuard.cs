using UnityEngine;

namespace EmberwardCoop.Core
{
    // Central input state guard. Tracks active radial wheels, key holds, and modals so
    // Harmony prefixes on CameraController and InteractionManager can suppress game input cleanly.
    public static class CoopInputGuard
    {
        private static bool _isEmoteWheelOpen;
        private static bool _isPingWheelOpen;
        private static bool _isPingKeyHolding;
        private static bool _isMazeSketching;
        private static bool _isCoopModalOpen;

        private static int _suppressFramesRemaining;
        private static float _suppressUntilUnscaledTime;
        private static bool _waitForMouseButtonsRelease;

        public static bool IsEmoteWheelOpen
        {
            get => _isEmoteWheelOpen;
            set
            {
                if (_isEmoteWheelOpen && !value)
                    CancelVanillaCameraDragState();
                _isEmoteWheelOpen = value;
                if (value) CancelVanillaCameraDragState();
            }
        }

        public static bool IsPingWheelOpen
        {
            get => _isPingWheelOpen;
            set
            {
                if (_isPingWheelOpen && !value)
                    CancelVanillaCameraDragState();
                _isPingWheelOpen = value;
                if (value) CancelVanillaCameraDragState();
            }
        }

        public static bool IsPingKeyHolding
        {
            get => _isPingKeyHolding;
            set
            {
                if (_isPingKeyHolding && !value)
                    CancelVanillaCameraDragState();
                _isPingKeyHolding = value;
                if (value) CancelVanillaCameraDragState();
            }
        }

        public static bool IsMazeSketching
        {
            get => _isMazeSketching;
            set => _isMazeSketching = value;
        }

        public static bool IsCoopModalOpen
        {
            get => _isCoopModalOpen;
            set
            {
                if (_isCoopModalOpen != value)
                {
                    _isCoopModalOpen = value;
                    if (value) CancelVanillaCameraDragState();
                }
            }
        }

        public static bool IsCameraLocked
        {
            get
            {
                if (_isCoopModalOpen) return true;
                bool lockRadial = CoopConfig.LockCameraDuringRadialMenus == null || CoopConfig.LockCameraDuringRadialMenus.Value;
                return lockRadial && (_isEmoteWheelOpen || _isPingWheelOpen || _isPingKeyHolding);
            }
        }

        public static bool IsRadialWheelActive =>
            _isEmoteWheelOpen || _isPingWheelOpen || _isPingKeyHolding;

        public static bool IsAbsorbingRelease =>
            IsPostCloseSuppressed();

        public static bool IsCapturingWorldInput =>
            IsCameraLocked ||
            _isEmoteWheelOpen ||
            _isPingWheelOpen ||
            _isPingKeyHolding ||
            _isMazeSketching ||
            IsPostCloseSuppressed();

        public static void NotifyWheelClosed(bool waitForMouseUp = true)
        {
            _suppressFramesRemaining = 2;
            _suppressUntilUnscaledTime = Time.unscaledTime + 0.08f;
            _waitForMouseButtonsRelease = waitForMouseUp &&
                (Input.GetMouseButton(0) || Input.GetMouseButton(1) || Input.GetMouseButton(2));
            CancelVanillaCameraDragState();
        }

        private static bool IsPostCloseSuppressed()
        {
            if (_waitForMouseButtonsRelease)
            {
                if (Input.GetMouseButton(0) || Input.GetMouseButton(1) || Input.GetMouseButton(2))
                    return true;
                _waitForMouseButtonsRelease = false;
            }

            if (Time.unscaledTime < _suppressUntilUnscaledTime)
                return true;

            if (_suppressFramesRemaining > 0)
            {
                _suppressFramesRemaining--;
                return true;
            }
            return false;
        }

        public static void CancelVanillaCameraDragState()
        {
            try
            {
                var camCtrl = GameRefs.GetCameraController();
                if (camCtrl != null)
                {
                    camCtrl.isMovingCamera = false;
                    camCtrl.movingCameraMouseButtonIndex = -1;
                    camCtrl.rightClickDiff = Vector3.zero;
                    camCtrl.targetCameraLerpPosition = camCtrl.transform.position;
                }
            }
            catch { }
        }

        public static void ClearTransientLocks()
        {
            _isEmoteWheelOpen = false;
            _isPingWheelOpen = false;
            _isPingKeyHolding = false;
            _isMazeSketching = false;
            _suppressFramesRemaining = 0;
            _suppressUntilUnscaledTime = 0f;
            _waitForMouseButtonsRelease = false;
            CancelVanillaCameraDragState();
        }

        public static void Reset()
        {
            _isEmoteWheelOpen = false;
            _isPingWheelOpen = false;
            _isPingKeyHolding = false;
            _isMazeSketching = false;
            _isCoopModalOpen = false;
            _suppressFramesRemaining = 0;
            _suppressUntilUnscaledTime = 0f;
            _waitForMouseButtonsRelease = false;
        }
    }
}
