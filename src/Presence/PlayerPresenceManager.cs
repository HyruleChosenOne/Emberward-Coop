using System;
using System.Collections.Generic;
using System.IO;
using EmberwardCoop.Core;
using EmberwardCoop.Networking;
using EmberwardCoop.UI;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace EmberwardCoop.Presence
{
    public sealed class PlayerPresenceManager
    {
        private readonly NetworkManager _net;
        private readonly GhostPreviewManager _ghosts;
        private readonly PingAndFeedbackManager _feedback;

        private sealed class RemoteAvatar
        {
            public int PlayerId;
            public string PlayerName;
            public Vector3 TargetPos;
            public Vector3 CurrentPos;
            public bool IsPlacing;
            public bool IsTower;
            public int PlacingItemType;
            public Vector3 PlacingEuler;
            public bool IsPlacementValid;
            public float LastHeardTime;

            public GameObject CursorRoot;
            public GameObject DiamondObj;
            public GameObject RingObj;
            public Material CursorMat;
            public int LastColorIndex = -1;
            public GameObject TagObj;
            public RectTransform TagRect;
            public TMP_Text TagText;
        }

        private readonly Dictionary<int, RemoteAvatar> _avatars = new Dictionary<int, RemoteAvatar>();

        private float _lastSendTime;
        private float _lastHeartbeatTime;
        private Vector3 _lastSentPos;
        private bool _lastSentPlacing;
        private bool _lastSentIsTower;
        private int _lastSentItem;
        private Vector3 _lastSentEuler;
        private bool _lastSentValid;

        public PlayerPresenceManager(NetworkManager net, GhostPreviewManager ghosts, PingAndFeedbackManager feedback)
        {
            _net = net;
            _ghosts = ghosts;
            _feedback = feedback;
        }

        public void Tick(float now)
        {
            _feedback.TryHandleLocalPingInput();

            if ((!_net.IsHost || _net.RemotePeerCount > 0) && now - _lastSendTime >= 0.05f)
            {
                _lastSendTime = now;
                SendLocalPresence(now);
            }

            float dt = Time.unscaledDeltaTime;
            UpdateAvatarVisuals(now, dt);
            _ghosts.TickVisuals(dt, now);
        }

        private void SendLocalPresence(float now)
        {
            var cam = Camera.main;
            if (cam == null) return;

            Ray ray = cam.ScreenPointToRay(Input.mousePosition);
            Plane ground = new Plane(Vector3.up, Vector3.zero);
            if (!ground.Raycast(ray, out float enter)) return;
            Vector3 pos = ray.GetPoint(enter);

            bool isPlacing = false;
            bool isTower = false;
            int placingItem = 0;
            Vector3 placingEuler = Vector3.zero;
            bool isValid = true;

            var oph = MainGame.Instance?.ObjectPlacementHandler;
            if (oph != null && oph.curPlacementPrefab != null && oph.placementDemoObject != null)
            {
                isPlacing = true;
                pos = oph.placementDemoObject.transform.position;
                placingEuler = oph.targetPlacementRotation;
                isValid = oph.isCurrentPlacementAvaliable;
                if (oph.isCurrentPlacementTower && oph.currentPlacementTower?.SettingData != null)
                {
                    isTower = true;
                    placingItem = (int)oph.currentPlacementTower.SettingData.itemType;
                }
                else if (oph.isCurrentPlacementTetris && oph.currentPlacementTetris != null)
                {
                    isTower = false;
                    placingItem = (int)oph.currentPlacementTetris.itemType;
                }
            }

            bool unchanged = (pos - _lastSentPos).sqrMagnitude < 0.0025f
                && isPlacing == _lastSentPlacing
                && isTower == _lastSentIsTower
                && placingItem == _lastSentItem
                && (placingEuler - _lastSentEuler).sqrMagnitude < 0.1f
                && isValid == _lastSentValid
                && now - _lastHeartbeatTime < 0.5f;

            if (unchanged) return;

            _lastSentPos = pos;
            _lastSentPlacing = isPlacing;
            _lastSentIsTower = isTower;
            _lastSentItem = placingItem;
            _lastSentEuler = placingEuler;
            _lastSentValid = isValid;
            _lastHeartbeatTime = now;

            Action<BinaryWriter> writer = w =>
            {
                w.Write(_net.LocalPlayerId);
                w.WriteSafeString(_net.LocalPlayerName);
                w.WriteVector3(pos);
                w.Write(isPlacing);
                w.Write(isTower);
                w.Write(placingItem);
                w.WriteVector3(placingEuler);
                w.Write(isValid);
            };

            _net.SendToAllPeersOrHost(PacketType.CursorAndGhostState, writer, DeliveryMode.Unreliable);
        }

        public void HandleRemoteCursorPacket(int senderId, BinaryReader r)
        {
            int pid = r.ReadInt32();
            if (_net.IsHost) pid = senderId;
            string name = r.ReadString();
            Vector3 pos = r.ReadVector3();
            bool isPlacing = r.ReadBoolean();
            bool isTower = r.ReadBoolean();
            int itemType = r.ReadInt32();
            Vector3 euler = r.ReadVector3();
            bool isValid = r.ReadBoolean();

            if (pid == _net.LocalPlayerId) return;

            if (!_avatars.TryGetValue(pid, out var av))
            {
                av = new RemoteAvatar { PlayerId = pid, CurrentPos = pos, TargetPos = pos };
                _avatars[pid] = av;
            }

            av.PlayerName = string.IsNullOrWhiteSpace(name) ? _net.GetPlayerName(pid) : name;
            av.TargetPos = pos;
            av.IsPlacing = isPlacing;
            av.IsTower = isTower;
            av.PlacingItemType = itemType;
            av.PlacingEuler = euler;
            av.IsPlacementValid = isValid;
            av.LastHeardTime = Time.unscaledTime;

            Color col = CoopConfig.GetPlayerColor(_net.GetPlayerColorIndex(pid));
            if (CoopConfig.EnableGhostPreviews.Value)
                _ghosts.UpdateGhost(pid, isPlacing, isTower, itemType, pos, euler, isValid, col);
            else
                _ghosts.HideGhost(pid);

            if (_net.IsHost)
            {
                _net.BroadcastToClients(PacketType.CursorAndGhostState, w =>
                {
                    w.Write(pid);
                    w.WriteSafeString(av.PlayerName);
                    w.WriteVector3(pos);
                    w.Write(isPlacing);
                    w.Write(isTower);
                    w.Write(itemType);
                    w.WriteVector3(euler);
                    w.Write(isValid);
                }, DeliveryMode.Unreliable, excludePeerId: senderId);
            }
        }

        private void UpdateAvatarVisuals(float now, float dt)
        {
            var cam = Camera.main;
            float lerpT = Mathf.Clamp01(dt * 18f);

            foreach (var kv in _avatars)
            {
                var av = kv.Value;
                bool active = (now - av.LastHeardTime) <= 5.0f;

                if (!active)
                {
                    if (av.CursorRoot != null && av.CursorRoot.activeSelf) av.CursorRoot.SetActive(false);
                    if (av.TagObj != null && av.TagObj.activeSelf) av.TagObj.SetActive(false);
                    _ghosts.HideGhost(av.PlayerId);
                    continue;
                }

                if (!CoopConfig.EnableGhostPreviews.Value)
                    _ghosts.HideGhost(av.PlayerId);

                if (!CoopConfig.ShowTeammateCursors.Value)
                {
                    if (av.CursorRoot != null && av.CursorRoot.activeSelf) av.CursorRoot.SetActive(false);
                    if (av.TagObj != null && av.TagObj.activeSelf) av.TagObj.SetActive(false);
                    continue;
                }

                EnsureAvatarVisuals(av);
                av.CurrentPos = Vector3.Lerp(av.CurrentPos, av.TargetPos, lerpT);

                int curColIdx = _net.GetPlayerColorIndex(av.PlayerId);
                if (av.CursorMat != null && curColIdx != av.LastColorIndex)
                {
                    av.LastColorIndex = curColIdx;
                    Color curCol = CoopConfig.GetPlayerColor(curColIdx);
                    if (av.CursorMat.HasProperty("_BaseColor")) av.CursorMat.SetColor("_BaseColor", curCol);
                    if (av.CursorMat.HasProperty("_Color")) av.CursorMat.SetColor("_Color", curCol);
                }

                if (av.CursorRoot != null)
                {
                    if (!av.CursorRoot.activeSelf) av.CursorRoot.SetActive(true);
                    av.CursorRoot.transform.position = av.CurrentPos;
                }
                if (av.DiamondObj != null)
                {
                    float bob = 0.65f + 0.12f * Mathf.Sin(now * 4.5f + av.PlayerId);
                    av.DiamondObj.transform.localPosition = new Vector3(0f, bob, 0f);
                    av.DiamondObj.transform.Rotate(0f, 110f * dt, 0f, Space.World);
                }

                if (av.TagObj != null && av.TagRect != null && cam != null)
                {
                    Vector3 sp = cam.WorldToScreenPoint(av.CurrentPos + new Vector3(0f, 1.25f, 0f));
                    bool onScreen = sp.z > 0f && sp.x >= 0f && sp.x <= Screen.width && sp.y >= 0f && sp.y <= Screen.height;
                    if (av.TagObj.activeSelf != onScreen) av.TagObj.SetActive(onScreen);
                    if (onScreen)
                    {
                        av.TagRect.anchoredPosition = NativeStyleProvider.ScreenToCanvasLocal(new Vector2(sp.x, sp.y));
                        if (av.TagText != null)
                        {
                            Color curCol = CoopConfig.GetPlayerColor(_net.GetPlayerColorIndex(av.PlayerId));
                            av.TagText.color = curCol;
                            av.TagText.text = av.IsPlacing && av.PlacingItemType > 0
                                ? $"{av.PlayerName} ({FormatShortItemName((eItemType)av.PlacingItemType)})"
                                : av.PlayerName;
                        }
                    }
                }
            }
        }

        private void EnsureAvatarVisuals(RemoteAvatar av)
        {
            if (av.CursorRoot != null) return;

            Color col = CoopConfig.GetPlayerColor(_net.GetPlayerColorIndex(av.PlayerId));
            var shader = Shader.Find("Universal Render Pipeline/Unlit") ?? Shader.Find("Sprites/Default") ?? Shader.Find("Unlit/Color");

            av.CursorRoot = new GameObject($"EWCoop_Cursor_{av.PlayerId}");
            av.CursorRoot.layer = 2;

            av.CursorMat = shader != null ? new Material(shader) : null;
            if (av.CursorMat != null)
            {
                if (av.CursorMat.HasProperty("_BaseColor")) av.CursorMat.SetColor("_BaseColor", col);
                if (av.CursorMat.HasProperty("_Color")) av.CursorMat.SetColor("_Color", col);
            }

            av.DiamondObj = GameObject.CreatePrimitive(PrimitiveType.Cube);
            av.DiamondObj.name = "PointerDiamond";
            av.DiamondObj.layer = 2;
            av.DiamondObj.transform.SetParent(av.CursorRoot.transform, false);
            av.DiamondObj.transform.localScale = new Vector3(0.22f, 0.22f, 0.22f);
            av.DiamondObj.transform.localRotation = Quaternion.Euler(45f, 0f, 45f);
            var dCol = av.DiamondObj.GetComponent<Collider>();
            if (dCol != null) UnityEngine.Object.Destroy(dCol);
            var dRdr = av.DiamondObj.GetComponent<Renderer>();
            if (dRdr != null && av.CursorMat != null)
            {
                dRdr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                dRdr.sharedMaterial = av.CursorMat;
            }

            av.RingObj = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            av.RingObj.name = "GroundRing";
            av.RingObj.layer = 2;
            av.RingObj.transform.SetParent(av.CursorRoot.transform, false);
            av.RingObj.transform.localPosition = new Vector3(0f, 0.05f, 0f);
            av.RingObj.transform.localScale = new Vector3(0.55f, 0.015f, 0.55f);
            var rCol = av.RingObj.GetComponent<Collider>();
            if (rCol != null) UnityEngine.Object.Destroy(rCol);
            var rRdr = av.RingObj.GetComponent<Renderer>();
            if (rRdr != null && av.CursorMat != null)
            {
                rRdr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                rRdr.sharedMaterial = av.CursorMat;
            }

            var canvasRoot = NativeStyleProvider.GetOrCreateOverlayCanvas();
            if (canvasRoot != null)
            {
                av.TagObj = NativeStyleProvider.CreatePanel(canvasRoot.transform, $"EWCoop_Nameplate_{av.PlayerId}", NativeStyleProvider.TooltipBg, addBorder: true, isSubPanel: true);
                av.TagRect = av.TagObj.GetComponent<RectTransform>();
                av.TagRect.anchorMin = new Vector2(0.5f, 0.5f);
                av.TagRect.anchorMax = new Vector2(0.5f, 0.5f);

                var cg = av.TagObj.AddComponent<CanvasGroup>();
                cg.blocksRaycasts = false;
                cg.interactable = false;

                var hlg = av.TagObj.AddComponent<HorizontalLayoutGroup>();
                hlg.padding = new RectOffset(10, 12, 6, 6);
                hlg.spacing = 5f;
                hlg.childAlignment = TextAnchor.MiddleLeft;
                hlg.childControlWidth = true;
                hlg.childControlHeight = true;
                hlg.childForceExpandWidth = false;
                var fit = av.TagObj.AddComponent<ContentSizeFitter>();
                fit.horizontalFit = ContentSizeFitter.FitMode.PreferredSize;
                fit.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

                NativeStyleProvider.CreateHeroAvatarWidget(av.TagObj.transform, _net.GetPlayerAvatarHeroId(av.PlayerId), col, 18f, clickable: false);
                av.TagText = NativeStyleProvider.CreateLabel(av.TagObj.transform, av.PlayerName ?? "", 11.5f, col, TextAlignmentOptions.MidlineLeft, FontStyles.Bold, addShadow: true);
            }
        }

        private static string FormatShortItemName(eItemType type)
        {
            try
            {
                var item = ResourceManager.Instance?.GetItemDataByType(type);
                string loc = item?.GetLocNameString(false);
                if (!string.IsNullOrWhiteSpace(loc)) return loc;
            }
            catch { }
            string raw = type.ToString();
            int idx = raw.IndexOf('_', 1);
            return idx > 0 && idx + 1 < raw.Length ? raw.Substring(idx + 1).Replace('_', ' ') : raw;
        }

        public void RemoveRemotePlayer(int playerId)
        {
            _ghosts.DestroyGhost(playerId);
            if (_avatars.TryGetValue(playerId, out var av))
            {
                DestroyAvatar(av);
                _avatars.Remove(playerId);
            }
        }

        public void ClearAll()
        {
            foreach (var av in _avatars.Values) DestroyAvatar(av);
            _avatars.Clear();
        }

        private static void DestroyAvatar(RemoteAvatar av)
        {
            if (av == null) return;
            if (av.CursorRoot != null) UnityEngine.Object.Destroy(av.CursorRoot);
            if (av.TagObj != null) UnityEngine.Object.Destroy(av.TagObj);
            if (av.CursorMat != null) UnityEngine.Object.Destroy(av.CursorMat);
        }
    }
}
