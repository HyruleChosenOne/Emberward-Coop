using System;
using System.IO;
using EmberwardCoop.Core;
using EmberwardCoop.Networking;
using EmberwardCoop.UI;
using UnityEngine;

namespace EmberwardCoop.Sync
{
    public sealed class ResourceGiftingManager
    {
        private readonly NetworkManager _net;
        private readonly Func<SessionController> _session;

        private int _giftedThisWave;

        public ResourceGiftingManager(NetworkManager net, Func<SessionController> session)
        {
            _net = net;
            _session = session;
        }

        public void ResetWaveCounter() => _giftedThisWave = 0;

        public bool CanGiftNow(int amount, out string reason)
        {
            reason = "";
            var s = _session();
            if (s == null || !_net.IsConnected || !GameRefs.IsInBattle) { reason = "Not in battle"; return false; }
            if (!s.Ruleset.SupportsResourceGifting) { reason = "Gifting disabled"; return false; }
            if (amount <= 0) { reason = "Invalid amount"; return false; }

            var ig = MainGame.Instance?.IngameData;
            if (ig == null || ig.Coin < amount) { reason = "Not enough gold"; return false; }

            int cap = s.Settings.GiftPerWaveCap;
            if (cap > 0 && _giftedThisWave + amount > cap)
            {
                reason = $"Wave gift cap ({cap}g) reached";
                return false;
            }
            return true;
        }

        public void SendGoldToPlayer(int targetPlayerId, int amount)
        {
            if (targetPlayerId == _net.LocalPlayerId) return;
            if (!CanGiftNow(amount, out string reason))
            {
                GameRefs.ShowGameToast(reason, 2.0f);
                return;
            }

            var ig = MainGame.Instance?.IngameData;
            if (ig == null) return;

            using (NetworkScope.Enter())
            {
                ig.AddCoin(-amount);
            }
            _giftedThisWave += amount;
            _session()?.Ownership.RecordGoldGifted(_net.LocalPlayerId, amount);

            string targetName = _net.GetPlayerName(targetPlayerId);
            CoopLogger.Info(LogCategory.Sync, $"Sent {amount}g to {targetName}!", addToFeed: true);
            GameRefs.ShowGameToast($"Sent {amount}g -> {targetName}", 2.0f);
            NativeStyleProvider.PlayUiClickSound(1.2f);

            if (_net.IsHost)
            {
                _net.SendToPeer(targetPlayerId, PacketType.EvtGiftGold, w =>
                {
                    w.Write(0);
                    w.Write(targetPlayerId);
                    w.Write(amount);
                });
            }
            else
            {
                _net.SendToHost(PacketType.CmdGiftGold, w =>
                {
                    w.Write(targetPlayerId);
                    w.Write(amount);
                });
            }
        }

        public void HandleCmdGiftGold(int senderId, BinaryReader r)
        {
            int targetId = r.ReadInt32();
            int amount = r.ReadInt32();
            var s = _session();
            if (s == null || !s.Ruleset.SupportsResourceGifting || amount <= 0 || amount > 50000) return;

            s.Ownership.RecordGoldGifted(senderId, amount);

            if (targetId == 0)
            {
                ReceiveGiftLocally(senderId, amount);
            }
            else
            {
                _net.SendToPeer(targetId, PacketType.EvtGiftGold, w =>
                {
                    w.Write(senderId);
                    w.Write(targetId);
                    w.Write(amount);
                });
            }
        }

        public void HandleEvtGiftGold(BinaryReader r)
        {
            int senderId = r.ReadInt32();
            int targetId = r.ReadInt32();
            int amount = r.ReadInt32();
            if (targetId != _net.LocalPlayerId) return;
            ReceiveGiftLocally(senderId, amount);
        }

        private void ReceiveGiftLocally(int senderId, int amount)
        {
            var ig = MainGame.Instance?.IngameData;
            if (ig == null) return;

            // NetworkScope bypasses WaveIncomeMultiplier so the recipient receives the full amount.
            using (NetworkScope.Enter())
            {
                ig.AddCoin(amount);
            }

            string senderName = _net.GetPlayerName(senderId);
            CoopLogger.Info(LogCategory.Sync, $"Received +{amount}g from {senderName}.", addToFeed: true);
            GameRefs.ShowGameToast($"+{amount}g from {senderName}", 2.8f);

            Vector3 vfxPos = MapManager.Instance?.PlayerMainOrigin != null
                ? MapManager.Instance.PlayerMainOrigin.transform.position
                : Vector3.zero;
            _session()?.Feedback.SpawnActionFeedback(senderId, vfxPos, $"+{amount}g", true);
        }
    }
}
