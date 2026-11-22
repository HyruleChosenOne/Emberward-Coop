using System;
using System.IO;
using EmberwardCoop.Core;
using EmberwardCoop.Localization;
using EmberwardCoop.Networking;
using EmberwardCoop.Serialization;
using EmberwardCoop.UI;
using UnityEngine;

namespace EmberwardCoop.Sync
{
    public sealed class BuildUndoManager
    {
        private readonly NetworkManager _net;
        private readonly Func<SessionController> _session;

        private const float GraceWindowSeconds = 12.0f;

        private sealed class UndoRecord
        {
            public bool IsTower;
            public Vector3 Position;
            public int GoldSpent;
            public bool FromHandCard;
            public NetCardInfo CardInfo;
            public float PlacedTime;
        }

        private UndoRecord _lastLocal;
        private bool _wasInBattleWave;

        public BuildUndoManager(NetworkManager net, Func<SessionController> session)
        {
            _net = net;
            _session = session;
        }

        public bool HasValidLocalUndo
        {
            get
            {
                if (_lastLocal == null || !GameRefs.IsInBattle) return false;
                var gsc = GameStateController.Instance;
                if (gsc != null && gsc.IsInBattle) return false;
                return Time.unscaledTime - _lastLocal.PlacedTime <= GraceWindowSeconds;
            }
        }

        public void RecordLocalTowerPlacement(Vector3 pos, int goldSpent, bool fromHand, CardData cardData)
        {
            var gsc = GameStateController.Instance;
            if (gsc != null && gsc.IsInBattle) return;

            _lastLocal = new UndoRecord
            {
                IsTower = true,
                Position = pos,
                GoldSpent = Math.Max(0, goldSpent),
                FromHandCard = fromHand,
                CardInfo = fromHand ? GameSerializers.CaptureCard(cardData) : default,
                PlacedTime = Time.unscaledTime
            };
        }

        public void RecordLocalBlockPlacement(Vector3 pos, NetCardInfo cardInfo)
        {
            var gsc = GameStateController.Instance;
            if (gsc != null && gsc.IsInBattle) return;

            _lastLocal = new UndoRecord
            {
                IsTower = false,
                Position = pos,
                GoldSpent = 0,
                FromHandCard = true,
                CardInfo = cardInfo,
                PlacedTime = Time.unscaledTime
            };
        }

        public void ClearHistory()
        {
            _lastLocal = null;
        }

        public void Tick()
        {
            if (!_net.IsConnected || !GameRefs.IsInBattle)
            {
                _lastLocal = null;
                _wasInBattleWave = false;
                return;
            }

            var gsc = GameStateController.Instance;
            bool inWave = gsc != null && gsc.IsInBattle;
            if (inWave && !_wasInBattleWave)
                _lastLocal = null; // Grace window ends immediately when wave combat starts
            _wasInBattleWave = inWave;

            if (CoopInputGuard.IsCapturingWorldInput) return;

            bool ctrlHeld = Input.GetKey(KeyCode.LeftControl) || Input.GetKey(KeyCode.RightControl);
            if (ctrlHeld && Input.GetKeyDown(CoopConfig.UndoBuildKey.Value))
            {
                TryTriggerLocalUndo();
            }
        }

        public void TryTriggerLocalUndo()
        {
            if (!_net.IsConnected || !GameRefs.IsInBattle) return;

            if (!HasValidLocalUndo)
            {
                GameRefs.ShowGameToast(CoopLoc.T("undo.unavailable"), 1.8f);
                return;
            }

            var rec = _lastLocal;
            _lastLocal = null;

            if (_net.IsHost)
            {
                if (ExecuteUndoAuthoritative(0, rec.IsTower, rec.Position, rec.GoldSpent, rec.FromHandCard, rec.CardInfo))
                {
                    NativeStyleProvider.PlayUiClickSound(0.92f);
                    GameRefs.ShowGameToast(CoopLoc.T("undo.success"), 2.0f);
                }
                else
                {
                    GameRefs.ShowGameToast(CoopLoc.T("undo.unavailable"), 1.8f);
                }
            }
            else if (_net.IsClient)
            {
                var s = _session();
                if (s != null && s.Ruleset.ClientSpendsOwnResourcesOnBuild)
                    RefundLocalResources(rec.IsTower, rec.GoldSpent, rec.FromHandCard, rec.CardInfo);

                _net.SendToHost(PacketType.CmdUndoBuild, w =>
                {
                    w.Write(rec.IsTower);
                    w.WriteVector3(rec.Position);
                    w.Write(rec.GoldSpent);
                    w.Write(rec.FromHandCard);
                    rec.CardInfo.Write(w);
                });

                NativeStyleProvider.PlayUiClickSound(0.92f);
                GameRefs.ShowGameToast(CoopLoc.T("undo.success"), 2.0f);
            }
        }

        public void HandleCmdUndoBuild(int senderId, BinaryReader r)
        {
            if (!_net.IsHost) return;
            bool isTower = r.ReadBoolean();
            Vector3 pos = r.ReadVector3();
            int goldSpent = r.ReadInt32();
            bool fromHand = r.ReadBoolean();
            NetCardInfo cardInfo = NetCardInfo.Read(r);

            var gsc = GameStateController.Instance;
            if (gsc != null && gsc.IsInBattle) return;

            ExecuteUndoAuthoritative(senderId, isTower, pos, goldSpent, fromHand, cardInfo);
        }

        private bool ExecuteUndoAuthoritative(int actorId, bool isTower, Vector3 pos, int goldSpent, bool fromHand, NetCardInfo cardInfo)
        {
            var s = _session();
            if (s == null) return false;

            if (isTower)
            {
                var tower = GameRefs.FindTowerAt(pos, 0.75f);
                if (tower == null) return false;
                if (!s.Ruleset.CanPlayerModifyTower(actorId, tower)) return false;

                using (NetworkScope.Enter())
                {
                    s.Ownership.UnregisterTower(tower);
                    tower.Despawn();
                    if (!s.Ruleset.ClientSpendsOwnResourcesOnBuild || actorId == 0)
                        RefundLocalResources(true, goldSpent, fromHand, cardInfo);
                }

                BroadcastEvtUndoBuild(actorId, true, pos);
                s.Feedback.SpawnActionFeedback(actorId, pos, CoopLoc.T("hud.undo_btn"), false);
                if (s.Ruleset.SyncsGlobalEconomyAndHand)
                    s.Economy.CheckHostEconomyAndHand(force: true);
                return true;
            }
            else
            {
                var block = GameRefs.FindTetrisAt(pos, 0.75f);
                if (block == null) return false;
                if (!s.Ruleset.CanPlayerRecallBlock(actorId, block)) return false;

                using (NetworkScope.Enter())
                {
                    s.Ownership.UnregisterBlock(block);
                    if (s.Ruleset.ClientSpendsOwnResourcesOnBuild && actorId != 0)
                    {
                        block.Remove();
                    }
                    else
                    {
                        if (block.IsRecallAble()) block.Recall(true);
                        else
                        {
                            block.Remove();
                            RefundLocalResources(false, 0, true, cardInfo);
                        }
                    }
                }

                s.Balance.DecrementTurnBlockCount();
                BroadcastEvtUndoBuild(actorId, false, pos);
                s.Feedback.SpawnActionFeedback(actorId, pos, CoopLoc.T("hud.undo_btn"), false);
                if (s.Ruleset.SyncsGlobalEconomyAndHand)
                    s.Economy.CheckHostEconomyAndHand(force: true);
                return true;
            }
        }

        private void BroadcastEvtUndoBuild(int actorId, bool isTower, Vector3 pos)
        {
            if (!_net.IsHost) return;
            _net.BroadcastToClients(PacketType.EvtUndoBuild, w =>
            {
                w.Write(actorId);
                w.Write(isTower);
                w.WriteVector3(pos);
            });
        }

        public void HandleEvtUndoBuild(BinaryReader r)
        {
            int actorId = r.ReadInt32();
            bool isTower = r.ReadBoolean();
            Vector3 pos = r.ReadVector3();

            var s = _session();
            if (s == null) return;

            using (NetworkScope.Enter())
            {
                if (isTower)
                {
                    var tower = GameRefs.FindTowerAt(pos, 0.75f);
                    if (tower != null)
                    {
                        s.Ownership.UnregisterTower(tower);
                        tower.Despawn();
                    }
                }
                else
                {
                    var block = GameRefs.FindTetrisAt(pos, 0.75f);
                    if (block != null)
                    {
                        s.Ownership.UnregisterBlock(block);
                        block.Remove();
                    }
                }
            }

            if (actorId != _net.LocalPlayerId)
                s.Feedback.SpawnActionFeedback(actorId, pos, CoopLoc.T("hud.undo_btn"), false);
        }

        private static void RefundLocalResources(bool isTower, int goldSpent, bool fromHand, NetCardInfo cardInfo)
        {
            var ig = MainGame.Instance?.IngameData;
            if (ig == null) return;

            using (NetworkScope.Enter())
            {
                if (isTower && !fromHand && goldSpent > 0)
                {
                    ig.AddCoin(goldSpent);
                }
                else if (fromHand && cardInfo.ItemType != 0)
                {
                    var card = GameSerializers.MaterializeCard(cardInfo);
                    if (card != null)
                        ig.RequestAddCardToHandByCardData(card, true);
                }
            }
        }
    }
}
