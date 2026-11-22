using System;
using System.IO;
using EmberwardCoop.Core;
using EmberwardCoop.Networking;
using UnityEngine;

namespace EmberwardCoop.Sync
{
    public sealed class WaveAndSpeedSync
    {
        private readonly NetworkManager _net;
        private readonly Func<SessionController> _session;

        private int _hostWaveSeq;
        private int _lastBroadcastWaveRound = -1;
        private bool _lastHostPaused;

        public WaveAndSpeedSync(NetworkManager net, Func<SessionController> session)
        {
            _net = net;
            _session = session;
        }

        public void ResetBattleState()
        {
            _lastBroadcastWaveRound = -1;
            _lastHostPaused = false;
            ClearAllWaveReadyFlags();
        }

        public void TickHostPauseState()
        {
            if (!_net.IsHost || _net.RemotePeerCount == 0 || !GameRefs.IsInBattle) return;
            var gsc = GameSpeedController.Instance;
            if (gsc == null) return;
            bool isPaused = gsc.SystemGameSpeed <= 0.001f;
            if (isPaused != _lastHostPaused)
            {
                _lastHostPaused = isPaused;
                _net.BroadcastToClients(PacketType.EvtPauseState, w => w.Write(isPaused));
            }
        }

        public void ClearAllWaveReadyFlags()
        {
            bool changed = false;
            foreach (var kv in _net.Roster)
            {
                if (kv.Value.IsWaveReady) { kv.Value.IsWaveReady = false; changed = true; }
            }
            if (changed && _net.IsHost) _net.BroadcastRoster();
        }

        public void ClientRequestStartWave()
        {
            var gsc = GameStateController.Instance;
            if (gsc != null && gsc.IsInBattle) return;

            var policy = CoopConfig.WavePolicy.Value;
            if (policy == WaveStartPolicy.HostOnly)
            {
                _net.SendToHost(PacketType.CmdToggleWaveReady, w => w.Write(true));
                GameRefs.ShowGameToast("Marked Ready for Wave — waiting for Host", 2.0f);
                return;
            }
            _net.SendToHost(PacketType.CmdStartWave);
        }

        public void HandleCmdToggleWaveReady(int senderId, BinaryReader r)
        {
            bool ready = r.ReadBoolean();
            var peer = _net.GetPeer(senderId);
            if (peer == null) return;
            peer.IsWaveReady = ready;
            _net.BroadcastRoster();

            string name = peer.Name;
            CoopLogger.Info(LogCategory.Sync, $"{name} is {(ready ? "READY" : "not ready")} for next wave.", addToFeed: true);
            GameRefs.ShowGameToast($"{name} is ready for next wave!", 2.0f);

            if (ready && CoopConfig.WavePolicy.Value == WaveStartPolicy.ReadyCheckWithHostOverride && AreAllPeersWaveReady())
            {
                HostOnWaveStartTriggered();
                TriggerLocalStartWave();
            }
        }

        public void HandleCmdStartWave(int senderId)
        {
            var gsc = GameStateController.Instance;
            if (gsc != null && gsc.IsInBattle) return;

            var policy = CoopConfig.WavePolicy.Value;
            var peer = _net.GetPeer(senderId);

            if (policy == WaveStartPolicy.HostOnly)
            {
                if (peer != null) { peer.IsWaveReady = true; _net.BroadcastRoster(); }
                GameRefs.ShowGameToast($"{_net.GetPlayerName(senderId)} is ready for wave!", 2.0f);
                return;
            }

            if (policy == WaveStartPolicy.ReadyCheckWithHostOverride && _net.Roster.Count > 1)
            {
                if (peer != null) peer.IsWaveReady = true;
                _net.BroadcastRoster();
                if (!AreAllPeersWaveReady())
                {
                    GameRefs.ShowGameToast($"{_net.GetPlayerName(senderId)} is ready! Press Start Wave to begin.", 2.5f);
                    return;
                }
            }

            CoopLogger.Info(LogCategory.Sync, $"{_net.GetPlayerName(senderId)} started the wave!", addToFeed: true);
            HostOnWaveStartTriggered();
            TriggerLocalStartWave();
        }

        private bool AreAllPeersWaveReady()
        {
            foreach (var kv in _net.Roster)
            {
                if (!kv.Value.IsWaveReady) return false;
            }
            return true;
        }

        public void HostOnWaveStartTriggered()
        {
            if (!_net.IsHost) return;
            int round = MainGame.Instance != null ? MainGame.Instance.Round : 0;
            if (_lastBroadcastWaveRound == round && GameStateController.Instance != null && GameStateController.Instance.IsInBattle)
                return;

            _lastBroadcastWaveRound = round;
            ClearAllWaveReadyFlags();

            int baseSeed = _session()?.Run.PendingStageSeed ?? 1337;
            if (baseSeed == 0) baseSeed = 1337;
            int waveSeed = unchecked(baseSeed ^ (round * 7919) ^ (++_hostWaveSeq * 3571));
            UnityEngine.Random.InitState(waveSeed);

            var speedCtrl = GameRefs.GetSpeedControl();
            int speedType = speedCtrl != null ? (int)speedCtrl.currentGameSpeedType : 0;

            _net.BroadcastToClients(PacketType.EvtStartWave, w =>
            {
                w.Write(round);
                w.Write(waveSeed);
                w.Write(speedType);
            });
            _session()?.Economy.CheckHostEconomyAndHand(force: true);
            _session()?.Gifting.ResetWaveCounter();
        }

        public void HandleEvtStartWave(BinaryReader r)
        {
            int round = r.ReadInt32();
            int waveSeed = r.ReadInt32();
            int speedType = r.ReadInt32();

            ApplyGameSpeedInternal(speedType);
            UnityEngine.Random.InitState(waveSeed);
            _session()?.Gifting.ResetWaveCounter();
            TriggerLocalStartWave();
        }

        private static void TriggerLocalStartWave()
        {
            var gsc = GameStateController.Instance;
            if (gsc != null && gsc.IsInBattle) return;

            using (NetworkScope.Enter())
            {
                var startBtn = GameRefs.GetStartWaveButton();
                if (startBtn != null)
                {
                    startBtn.OnButtonDown();
                }
                else
                {
                    gsc?.OnRequestChangeGameState(eGameState.NORMAL_MODE);
                    GameRefs.SendEvent0(eGameEvents.CancelPlacement);
                    GameRefs.SendEvent0(eGameEvents.RequestStartNextWave);
                }
            }
        }

        public void HostOnWaveEnded()
        {
            if (!_net.IsHost) return;
            int round = MainGame.Instance != null ? MainGame.Instance.Round : 0;
            _net.BroadcastToClients(PacketType.EvtWaveEnd, w => w.Write(round));
            _session()?.Balance.ResetForNewTurn();
            _session()?.Economy.CheckHostEconomyAndHand(force: true);
            _session()?.Quests.BroadcastQuestProgress();
            _session()?.Ownership.BroadcastRunStats();
        }

        public void HandleEvtWaveEnd(BinaryReader r)
        {
            int hostRound = r.ReadInt32();
            var gsc = GameStateController.Instance;
            if (gsc == null || !gsc.IsInBattle) return;

            int localRound = MainGame.Instance != null ? MainGame.Instance.Round : 0;
            if (localRound <= hostRound)
            {
                var list = MonsterManager.Instance?.GetMonstersOnField();
                if (list != null && list.Count > 0)
                {
                    using (NetworkScope.Enter())
                    {
                        for (int i = list.Count - 1; i >= 0; i--)
                        {
                            var m = list[i];
                            if (m != null && m.IsAlive())
                            {
                                try { m.Despawn(); } catch { }
                            }
                        }
                    }
                }
            }
        }

        public void ClientSendSetGameSpeed(int speedType)
            => _net.SendToHost(PacketType.CmdSetGameSpeed, w => w.Write(speedType));

        public void HandleCmdSetGameSpeed(int senderId, BinaryReader r)
        {
            int speedType = r.ReadInt32();
            ApplyGameSpeedInternal(speedType);
            BroadcastEvtSetGameSpeed(speedType);
            CoopLogger.Debug(LogCategory.Sync, $"{_net.GetPlayerName(senderId)} changed game speed -> {(UI_GameSpeedControl.eGameSpeedType)speedType}");
        }

        public void BroadcastEvtSetGameSpeed(int speedType)
        {
            if (!_net.IsHost) return;
            _net.BroadcastToClients(PacketType.EvtSetGameSpeed, w => w.Write(speedType));
        }

        public void HandleEvtSetGameSpeed(BinaryReader r)
        {
            ApplyGameSpeedInternal(r.ReadInt32());
        }

        public void ApplyGameSpeedInternal(int speedType)
        {
            var ctrl = GameRefs.GetSpeedControl();
            if (ctrl == null) return;
            using (NetworkScope.Enter())
            {
                ctrl.OnClickButton_GameSpeed((UI_GameSpeedControl.eGameSpeedType)speedType);
            }
        }

        public void HandleEvtPauseState(BinaryReader r)
        {
            bool paused = r.ReadBoolean();
            var gsc = GameSpeedController.Instance;
            if (gsc == null) return;
            using (NetworkScope.Enter())
            {
                gsc.OnRequestModifySystemGameSpeed(paused ? 0f : 1f);
            }
        }
    }
}
