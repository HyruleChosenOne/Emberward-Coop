using System;
using UnityEngine;

namespace EmberwardCoop.Core
{
    public static class CoopInputGuard
    {
        private static bool _isEmoteWheelOpen;
        private static bool _isPingWheelOpen;
        private static bool _isPingKeyHolding;
        private static bool _isMazeSketching;
        private static bool _isCoopModalOpen;

        private static int _consumeWorldUntilFrame;
        private static float _consumeWorldUntilTime;
        private static bool _awaitingMouseButtonsUp;

        public static bool IsEmoteWheelOpen
        {
            get => _isEmoteWheelOpen;
            set
            {
                if (_isEmoteWheelOpen == value) return;
                bool wasLocked = IsCameraLocked;
                _isEmoteWheelOpen = value;
                if (value && !wasLocked) OnCameraLockEntered();
            }
        }

        public static bool IsPingWheelOpen
        {
            get => _isPingWheelOpen;
            set
            {
                if (_isPingWheelOpen == value) return;
                bool wasLocked = IsCameraLocked;
                _isPingWheelOpen = value;
                if (value && !wasLocked) OnCameraLockEntered();
            }
        }

        public static bool IsPingKeyHolding
        {
            get => _isPingKeyHolding;
            set
            {
                if (_isPingKeyHolding == value) return;
                bool wasLocked = IsCameraLocked;
                _isPingKeyHolding = value;
                if (value && !wasLocked) OnCameraLockEntered();
            }
        }

        public static bool IsMazeSketching
        {
            get => _isMazeSketching;
            set
            {
                if (_isMazeSketching == value) return;
                bool wasLocked = IsCameraLocked;
                _isMazeSketching = value;
                if (value && !wasLocked) OnCameraLockEntered();
            }
        }

        public static bool IsCoopModalOpen
        {
            get => _isCoopModalOpen;
            set
            {
                if (_isCoopModalOpen == value) return;
                bool wasLocked = IsCameraLocked;
                _isCoopModalOpen = value;
                if (value && !wasLocked) OnCameraLockEntered();
            }
        }

        public static bool IsRadialWheelActive => _isEmoteWheelOpen || _isPingWheelOpen || _isPingKeyHolding;

        public static bool IsAbsorbingRelease
        {
            get
            {
                if (_awaitingMouseButtonsUp)
                {
                    if (!Input.GetMouseButton(0) && !Input.GetMouseButton(1) && !Input.GetMouseButton(2))
                        _awaitingMouseButtonsUp = false;
                    else
                        return true;
                }
                return Time.frameCount <= _consumeWorldUntilFrame || Time.unscaledTime <= _consumeWorldUntilTime;
            }
        }

        public static bool IsCameraLocked =>
            _isEmoteWheelOpen || _isPingWheelOpen || _isPingKeyHolding || _isMazeSketching || _isCoopModalOpen || IsAbsorbingRelease;

        public static bool IsCapturingWorldInput => IsCameraLocked;

        public static void NotifyWheelClosed(bool waitForMouseUp = true)
        {
            _consumeWorldUntilFrame = Time.frameCount + 2;
            _consumeWorldUntilTime = Time.unscaledTime + 0.08f;
            if (waitForMouseUp && (Input.GetMouseButton(0) || Input.GetMouseButton(1) || Input.GetMouseButton(2)))
                _awaitingMouseButtonsUp = true;
        }

        public static void OnCameraLockEntered()
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
                    camCtrl.ResetEditModeRotationInput();
                }
            }
            catch { }

            try
            {
                var im = GameRefs.GetInteractionManager();
                if (im != null)
                    im.ClearCurrentTargets();
            }
            catch { }
        }

        public static void Reset()
        {
            _isEmoteWheelOpen = false;
            _isPingWheelOpen = false;
            _isPingKeyHolding = false;
            _isMazeSketching = false;
            _isCoopModalOpen = false;
            _consumeWorldUntilFrame = 0;
            _consumeWorldUntilTime = 0f;
            _awaitingMouseButtonsUp = false;
        }
    }
}
