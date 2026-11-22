using System;
using System.Collections.Generic;
using System.IO;
using EmberwardCoop.Core;
using EmberwardCoop.Networking;
using EmberwardCoop.Serialization;
using UnityEngine;

namespace EmberwardCoop.Sync
{
    public enum TowerSpecialActionType : byte
    {
        DiceReroll = 1,
        DrillExtra = 2,
        ForceActivate = 3,
        ScorchInverseRotation = 4
    }

    public sealed class BoardAndCombatSync
    {
        private readonly NetworkManager _net;
        private readonly Func<SessionController> _session;

        public BoardAndCombatSync(NetworkManager net, Func<SessionController> session)
        {
            _net = net;
            _session = session;
        }

        public byte[] CaptureBoardSnapshotBytes()
        {
            if (TetrisManager.Instance == null || TowerManager.Instance == null)
                return Array.Empty<byte>();
            using var ms = new MemoryStream(1024);
            using var bw = new BinaryWriter(ms, System.Text.Encoding.UTF8, leaveOpen: true);
            WriteBoardSnapshot(bw);
            bw.Flush();
            return ms.ToArray();
        }

        public void ApplyBoardSnapshotBytes(byte[] data)
        {
            if (data == null || data.Length == 0) return;
            using var ms = new MemoryStream(data);
            using var br = new BinaryReader(ms, System.Text.Encoding.UTF8);
            HandleBoardSnapshot(br);
        }

        public void BroadcastBoardSnapshot(int targetPeerId = -1)
        {
            if (!_net.IsHost) return;
            if (TetrisManager.Instance == null || TowerManager.Instance == null) return;

            Action<BinaryWriter> writer = WriteBoardSnapshot;
            if (targetPeerId >= 0) _net.SendToPeer(targetPeerId, PacketType.BoardSnapshot, writer);
            else _net.BroadcastToClients(PacketType.BoardSnapshot, writer);
        }

        private void WriteBoardSnapshot(BinaryWriter w)
        {
            var tm = TetrisManager.Instance;
            var twm = TowerManager.Instance;
            var ownership = _session()?.Ownership;
            var blocks = tm?.GetAllTetrisOnField();
            var towers = twm?.GetAllTowersOnField();
            var speedCtrl = GameRefs.GetSpeedControl();
            int speedType = speedCtrl != null ? (int)speedCtrl.currentGameSpeedType : 0;

            w.Write(speedType);

            int bCount = blocks?.Count ?? 0;
            w.Write(bCount);
            for (int i = 0; i < bCount; i++)
            {
                var b = blocks[i];
                if (b == null) { w.Write(false); continue; }
                w.Write(true);
                w.Write(ownership != null ? ownership.GetBlockOwner(b) : 0);
                w.Write((int)b.itemType);
                w.WriteVector3(b.transform.position);
                w.WriteVector3(b.transform.eulerAngles);
                GameSerializers.CaptureCard(b.tetrisCardData).Write(w);
            }

            int tCount = towers?.Count ?? 0;
            w.Write(tCount);
            for (int i = 0; i < tCount; i++)
            {
                var t = towers[i];
                if (t == null || t.SettingData == null) { w.Write(false); continue; }
                w.Write(true);
                w.Write(ownership != null ? ownership.GetTowerOwner(t) : 0);
                w.Write((int)t.SettingData.itemType);
                w.WriteVector3(t.transform.position);
                w.WriteVector3(t.transform.eulerAngles);
                w.Write(t.Level);
                w.Write((int)t.UpgradeType);
                w.Write((int)t.TargetPriority);
            }
        }

        public void HandleBoardSnapshot(BinaryReader r)
        {
            int speedType = r.ReadInt32();
            var ownership = _session()?.Ownership;

            int bCount = r.ReadBoundedCount(512);
            var hostBlockPositions = new List<Vector3>(bCount);
            for (int i = 0; i < bCount; i++)
            {
                if (!r.ReadBoolean()) continue;
                int ownerId = r.ReadInt32();
                var itemType = (eItemType)r.ReadInt32();
                Vector3 pos = r.ReadVector3();
                Vector3 rot = r.ReadVector3();
                NetCardInfo cInfo = NetCardInfo.Read(r);
                hostBlockPositions.Add(pos);

                var existingBlock = GameRefs.FindTetrisAt(pos, 0.35f);
                if (existingBlock == null)
                    SpawnTetrisInternal(itemType, pos, rot, cInfo, ownerId, countAsNewStat: false);
                else
                    ownership?.RegisterExistingBlockOwner(existingBlock, ownerId);
            }

            int tCount = r.ReadBoundedCount(512);
            var hostTowerPositions = new List<Vector3>(tCount);
            for (int i = 0; i < tCount; i++)
            {
                if (!r.ReadBoolean()) continue;
                int ownerId = r.ReadInt32();
                var itemType = (eItemType)r.ReadInt32();
                Vector3 pos = r.ReadVector3();
                Vector3 rot = r.ReadVector3();
                int level = r.ReadInt32();
                int upgType = r.ReadInt32();
                int prio = r.ReadInt32();
                hostTowerPositions.Add(pos);

                var existing = GameRefs.FindTowerAt(pos, 0.35f);
                if (existing == null)
                    existing = SpawnTowerInternal(itemType, pos, rot, ownerId, countAsNewStat: false);
                else
                    ownership?.RegisterExistingTowerOwner(existing, ownerId);

                if (existing != null)
                {
                    using (NetworkScope.Enter())
                    {
                        while (existing.Level < level && !existing.IsMaxLevel())
                            existing.Upgrade((ABaseTower.eUpgradeType)upgType, false, true, true, false);
                        existing.OverrideTargetPriority((eTowerTargetPriority)prio);
                    }
                }
            }

            // Reconcile stray towers
            var localTowers = TowerManager.Instance?.GetAllTowersOnField();
            if (localTowers != null)
            {
                using (NetworkScope.Enter())
                {
                    for (int i = localTowers.Count - 1; i >= 0; i--)
                    {
                        var lt = localTowers[i];
                        if (lt == null) continue;
                        if (!ContainsNear(hostTowerPositions, lt.transform.position, 0.45f))
                        {
                            try { ownership?.UnregisterTower(lt); lt.Despawn(); } catch { }
                        }
                    }
                }
            }

            _session()?.Waves.ApplyGameSpeedInternal(speedType);
        }

        private static bool ContainsNear(List<Vector3> positions, Vector3 target, float radius)
        {
            float rSq = radius * radius;
            for (int i = 0; i < positions.Count; i++)
            {
                Vector3 d = target - positions[i];
                d.y = 0f;
                if (d.sqrMagnitude <= rSq) return true;
            }
            return false;
        }

        public void ClientSendPlaceTetris(CardData cardData, eItemType itemType, Vector3 pos, Vector3 eulerRot)
        {
            var cInfo = GameSerializers.CaptureCard(cardData);
            cInfo.ItemType = (int)itemType;
            cInfo.IsTetris = true;
            _net.SendToHost(PacketType.CmdPlaceTetris, w =>
            {
                w.WriteVector3(pos);
                w.WriteVector3(eulerRot);
                cInfo.Write(w);
            });
        }

        public void HandleCmdPlaceTetris(int senderId, BinaryReader r)
        {
            Vector3 pos = r.ReadVector3();
            Vector3 rot = r.ReadVector3();
            NetCardInfo cInfo = NetCardInfo.Read(r);

            var s = _session();
            var ig = MainGame.Instance?.IngameData;
            if (s == null || ig == null) return;

            if (!s.Balance.CanPlaceBlockNow(out _))
            {
                BroadcastBoardSnapshot(senderId);
                return;
            }

            if (GameRefs.FindTetrisAt(pos, 0.25f) != null)
            {
                BroadcastBoardSnapshot(senderId);
                return;
            }

            NetCardInfo authCard = cInfo;
            CardData matched = null;
            if (!s.Ruleset.ClientSpendsOwnResourcesOnBuild)
            {
                matched = GameRefs.FindHandCard(ig, cInfo.RuntimeId, (eItemType)cInfo.ItemType);
                if (matched == null)
                {
                    s.Economy.BroadcastSnapshot(senderId);
                    return;
                }
                authCard = GameSerializers.CaptureCard(matched);
                authCard.IsTetris = true;
            }

            var block = SpawnTetrisInternal((eItemType)authCard.ItemType, pos, rot, authCard, senderId, countAsNewStat: true);
            if (block != null)
            {
                if (matched != null)
                {
                    using (NetworkScope.Enter())
                        ig.OnRequestRemoveCardFromHand(matched);
                }

                s.Balance.IncrementTurnBlockCount();
                BroadcastEvtPlaceTetris(senderId, (eItemType)authCard.ItemType, pos, rot, authCard);
                s.Feedback.SpawnActionFeedback(senderId, pos, "BLOCK", false);
                if (s.Ruleset.SyncsGlobalEconomyAndHand)
                    s.Economy.CheckHostEconomyAndHand(force: true);
            }
        }

        public void BroadcastEvtPlaceTetris(int actorId, eItemType itemType, Vector3 pos, Vector3 rot, NetCardInfo cInfo)
        {
            if (!_net.IsHost) return;
            _net.BroadcastToClients(PacketType.EvtPlaceTetris, w =>
            {
                w.Write(actorId);
                w.Write((int)itemType);
                w.WriteVector3(pos);
                w.WriteVector3(rot);
                cInfo.Write(w);
            });
        }

        public void HandleEvtPlaceTetris(BinaryReader r)
        {
            int actorId = r.ReadInt32();
            var itemType = (eItemType)r.ReadInt32();
            Vector3 pos = r.ReadVector3();
            Vector3 rot = r.ReadVector3();
            NetCardInfo cInfo = NetCardInfo.Read(r);

            if (GameRefs.FindTetrisAt(pos, 0.25f) == null)
                SpawnTetrisInternal(itemType, pos, rot, cInfo, actorId, countAsNewStat: true);

            if (actorId != _net.LocalPlayerId)
                _session()?.Feedback.SpawnActionFeedback(actorId, pos, "BLOCK", false);
        }

        public Obj_TetrisBlock SpawnTetrisInternal(eItemType itemType, Vector3 pos, Vector3 eulerRot, NetCardInfo cInfo, int ownerId, bool countAsNewStat)
        {
            using (NetworkScope.Enter())
            {
                try
                {
                    var rm = ResourceManager.Instance;
                    var pm = PrefabManager.Instance;
                    if (rm == null || pm == null) return null;

                    var panelData = rm.GetPanelDataByType(itemType);
                    if (panelData == null) return null;

                    var cardObj = GameSerializers.MaterializeCard(cInfo);
                    var tetrisCard = cardObj?.TryCast<TetrisCardData>();
                    if (tetrisCard == null)
                    {
                        tetrisCard = TetrisCardData.CreateCardData(panelData, false);
                        tetrisCard.seed = cInfo.Seed;
                    }

                    GameObject go = pm.InstantiatePrefab(panelData.GetPrefab(), pos, Quaternion.Euler(eulerRot), null);
                    if (go == null) return null;

                    go.transform.position = pos;
                    go.transform.rotation = Quaternion.Euler(eulerRot);
                    Physics.SyncTransforms();

                    var block = go.GetComponent<Obj_TetrisBlock>();
                    if (block != null)
                    {
                        block.PreSpawn(tetrisCard);
                        GameRefs.SendEventRef<GameObject>(eGameEvents.OnGridObjectChanged, go);
                        block.Spawn(tetrisCard, Obj_TetrisBlock.eTetrisSpawnStyle.NORMAL);
                        if (countAsNewStat) _session()?.Ownership.StampBlockOwner(block, ownerId);
                        else _session()?.Ownership.RegisterExistingBlockOwner(block, ownerId);
                    }
                    return block;
                }
                catch (Exception ex)
                {
                    CoopLogger.Error(LogCategory.Sync, "SpawnTetrisInternal failed", ex);
                    return null;
                }
            }
        }

        public void ClientSendRecallTetris(Vector3 pos, bool isForceRecall)
        {
            _net.SendToHost(PacketType.CmdRecallTetris, w =>
            {
                w.WriteVector3(pos);
                w.Write(isForceRecall);
            });
        }

        public void HandleCmdRecallTetris(int senderId, BinaryReader r)
        {
            Vector3 pos = r.ReadVector3();
            bool forceRecall = r.ReadBoolean();
            var s = _session();
            var block = GameRefs.FindTetrisAt(pos, 0.75f);
            if (s == null || block == null) return;
            if (!s.Ruleset.CanPlayerRecallBlock(senderId, block)) return;
            if (!(forceRecall || block.IsRecallAble())) return;

            using (NetworkScope.Enter())
            {
                if (s.Ruleset.ClientSpendsOwnResourcesOnBuild && senderId != 0)
                {
                    // In Separate mode, Host removes block without adding Client's recalled block card to Host's hand
                    s.Ownership.UnregisterBlock(block);
                    block.Remove();
                }
                else
                {
                    s.Ownership.UnregisterBlock(block);
                    block.Recall(forceRecall);
                }
            }

            s.Balance.DecrementTurnBlockCount();
            BroadcastEvtRecallTetris(senderId, pos, forceRecall);
            if (s.Ruleset.SyncsGlobalEconomyAndHand)
                s.Economy.CheckHostEconomyAndHand(force: true);
        }

        public void BroadcastEvtRecallTetris(int actorId, Vector3 pos, bool forceRecall)
        {
            if (!_net.IsHost) return;
            _net.BroadcastToClients(PacketType.EvtRecallTetris, w =>
            {
                w.Write(actorId);
                w.WriteVector3(pos);
                w.Write(forceRecall);
            });
        }

        public void HandleEvtRecallTetris(BinaryReader r)
        {
            int actorId = r.ReadInt32();
            Vector3 pos = r.ReadVector3();
            bool forceRecall = r.ReadBoolean();
            var s = _session();
            var block = GameRefs.FindTetrisAt(pos, 0.75f);
            if (s == null || block == null) return;

            using (NetworkScope.Enter())
            {
                s.Ownership.UnregisterBlock(block);
                if (s.Ruleset.ClientSpendsOwnResourcesOnBuild && actorId != _net.LocalPlayerId)
                    block.Remove(); // Don't give someone else's recalled card to this client!
                else
                    block.Recall(forceRecall);
            }
        }

        public void ClientSendPlaceTower(eItemType towerType, Vector3 pos, Vector3 eulerRot, bool isFromHandCard, string runtimeCardId)
        {
            _net.SendToHost(PacketType.CmdPlaceTower, w =>
            {
                w.Write((int)towerType);
                w.WriteVector3(pos);
                w.WriteVector3(eulerRot);
                w.Write(isFromHandCard);
                w.WriteSafeString(runtimeCardId);
            });
        }

        public void HandleCmdPlaceTower(int senderId, BinaryReader r)
        {
            var towerType = (eItemType)r.ReadInt32();
            Vector3 pos = r.ReadVector3();
            Vector3 rot = r.ReadVector3();
            bool isFromHandCard = r.ReadBoolean();
            string runtimeCardId = r.ReadString();

            var s = _session();
            var ig = MainGame.Instance?.IngameData;
            var twm = TowerManager.Instance;
            if (s == null || ig == null || twm == null) return;

            if (GameRefs.FindTowerAt(pos, 0.25f) != null)
            {
                BroadcastBoardSnapshot(senderId);
                return;
            }

            int buildCost = 0;
            CardData handCard = null;
            if (!s.Ruleset.ClientSpendsOwnResourcesOnBuild)
            {
                if (isFromHandCard)
                {
                    handCard = GameRefs.FindHandCard(ig, runtimeCardId, towerType);
                    if (handCard == null) { s.Economy.BroadcastSnapshot(senderId); return; }
                }
                else
                {
                    buildCost = twm.GetBuildCost(towerType);
                    if (!ig.IsCoinEnough(buildCost)) { s.Economy.BroadcastSnapshot(senderId); return; }
                }
            }

            using (NetworkScope.Enter())
            {
                if (!s.Ruleset.ClientSpendsOwnResourcesOnBuild)
                {
                    if (isFromHandCard && handCard != null) ig.OnRequestRemoveCardFromHand(handCard);
                    else if (buildCost > 0) ig.AddCoin(-buildCost);
                }

                var tower = SpawnTowerInternal(towerType, pos, rot, senderId, countAsNewStat: true);
                if (tower != null)
                {
                    BroadcastEvtPlaceTower(senderId, towerType, pos, rot);
                    s.Feedback.SpawnActionFeedback(senderId, pos, "BUILD", false);
                    if (s.Ruleset.SyncsGlobalEconomyAndHand)
                        s.Economy.CheckHostEconomyAndHand(force: true);
                }
            }
        }

        public void BroadcastEvtPlaceTower(int actorId, eItemType towerType, Vector3 pos, Vector3 rot)
        {
            if (!_net.IsHost) return;
            _net.BroadcastToClients(PacketType.EvtPlaceTower, w =>
            {
                w.Write(actorId);
                w.Write((int)towerType);
                w.WriteVector3(pos);
                w.WriteVector3(rot);
            });
        }

        public void HandleEvtPlaceTower(BinaryReader r)
        {
            int actorId = r.ReadInt32();
            var towerType = (eItemType)r.ReadInt32();
            Vector3 pos = r.ReadVector3();
            Vector3 rot = r.ReadVector3();

            if (GameRefs.FindTowerAt(pos, 0.25f) == null)
                SpawnTowerInternal(towerType, pos, rot, actorId, countAsNewStat: true);

            if (actorId != _net.LocalPlayerId)
                _session()?.Feedback.SpawnActionFeedback(actorId, pos, "BUILD", false);
        }

        public ABaseTower SpawnTowerInternal(eItemType towerType, Vector3 pos, Vector3 eulerRot, int ownerId, bool countAsNewStat)
        {
            using (NetworkScope.Enter())
            {
                try
                {
                    var twm = TowerManager.Instance;
                    if (twm == null) return null;
                    var tower = twm.CreateTowerAtPosition(towerType, pos, true, Quaternion.Euler(eulerRot), true, false);
                    if (tower != null)
                    {
                        Physics.SyncTransforms();
                        GameRefs.SendEventRef<GameObject>(eGameEvents.OnGridObjectChanged, tower.gameObject);
                        GameRefs.SendEventRef<ABaseTower>(eGameEvents.OnTowerPlaced, tower);
                        if (countAsNewStat) _session()?.Ownership.StampTowerOwner(tower, ownerId);
                        else _session()?.Ownership.RegisterExistingTowerOwner(tower, ownerId);
                    }
                    return tower;
                }
                catch (Exception ex)
                {
                    CoopLogger.Error(LogCategory.Sync, "SpawnTowerInternal failed", ex);
                    return null;
                }
            }
        }

        public void ClientSendUpgradeTower(Vector3 pos, ABaseTower.eUpgradeType upgradeType)
        {
            _net.SendToHost(PacketType.CmdUpgradeTower, w => { w.WriteVector3(pos); w.Write((int)upgradeType); });
        }

        public void HandleCmdUpgradeTower(int senderId, BinaryReader r)
        {
            Vector3 pos = r.ReadVector3();
            var upgType = (ABaseTower.eUpgradeType)r.ReadInt32();
            var s = _session();
            var tower = GameRefs.FindTowerAt(pos, 0.75f);
            var ig = MainGame.Instance?.IngameData;
            if (s == null || tower == null || ig == null || tower.IsMaxLevel()) return;
            if (!s.Ruleset.CanPlayerModifyTower(senderId, tower)) return;

            bool freeOnHost = s.Ruleset.ClientSpendsOwnResourcesOnBuild;
            if (!freeOnHost)
            {
                int cost = tower.GetUpgradeCost(upgType);
                if (!ig.IsCoinEnough(cost)) { s.Economy.BroadcastSnapshot(senderId); return; }
            }

            using (NetworkScope.Enter()) tower.Upgrade(upgType, !freeOnHost, freeOnHost, false, false);
            BroadcastEvtUpgradeTower(senderId, pos, upgType);
            s.Feedback.SpawnActionFeedback(senderId, pos, $"LV.{tower.Level}", true);
            if (s.Ruleset.SyncsGlobalEconomyAndHand)
                s.Economy.CheckHostEconomyAndHand(force: true);
        }

        public void BroadcastEvtUpgradeTower(int actorId, Vector3 pos, ABaseTower.eUpgradeType upgType)
        {
            if (!_net.IsHost) return;
            _net.BroadcastToClients(PacketType.EvtUpgradeTower, w => { w.Write(actorId); w.WriteVector3(pos); w.Write((int)upgType); });
        }

        public void HandleEvtUpgradeTower(BinaryReader r)
        {
            int actorId = r.ReadInt32();
            Vector3 pos = r.ReadVector3();
            var upgType = (ABaseTower.eUpgradeType)r.ReadInt32();
            var tower = GameRefs.FindTowerAt(pos, 0.75f);
            if (tower != null && !tower.IsMaxLevel())
            {
                using (NetworkScope.Enter()) tower.Upgrade(upgType, false, true, false, false);
                if (actorId != _net.LocalPlayerId)
                    _session()?.Feedback.SpawnActionFeedback(actorId, pos, $"LV.{tower.Level}", true);
            }
        }

        public void ClientSendSellTower(Vector3 pos) => _net.SendToHost(PacketType.CmdSellTower, w => w.WriteVector3(pos));

        public void HandleCmdSellTower(int senderId, BinaryReader r)
        {
            Vector3 pos = r.ReadVector3();
            var s = _session();
            var tower = GameRefs.FindTowerAt(pos, 0.75f);
            var ig = MainGame.Instance?.IngameData;
            if (s == null || tower == null || ig == null || !tower.CanSellTower()) return;
            if (!s.Ruleset.CanPlayerModifyTower(senderId, tower)) return;

            using (NetworkScope.Enter())
            {
                s.Ownership.UnregisterTower(tower);
                if (s.Ruleset.ClientSpendsOwnResourcesOnBuild)
                    tower.Despawn(); // Client already received its own sell refund locally
                else
                    ig.OnRequestSellTower(tower);
            }
            BroadcastEvtSellTower(senderId, pos);
            if (s.Ruleset.SyncsGlobalEconomyAndHand)
                s.Economy.CheckHostEconomyAndHand(force: true);
        }

        public void BroadcastEvtSellTower(int actorId, Vector3 pos)
        {
            if (!_net.IsHost) return;
            _net.BroadcastToClients(PacketType.EvtSellTower, w => { w.Write(actorId); w.WriteVector3(pos); });
        }

        public void HandleEvtSellTower(BinaryReader r)
        {
            int actorId = r.ReadInt32();
            Vector3 pos = r.ReadVector3();
            var tower = GameRefs.FindTowerAt(pos, 0.75f);
            if (tower != null)
            {
                using (NetworkScope.Enter())
                {
                    _session()?.Ownership.UnregisterTower(tower);
                    tower.Despawn();
                }
                if (actorId != _net.LocalPlayerId)
                    _session()?.Feedback.SpawnActionFeedback(actorId, pos, "SOLD", false);
            }
        }

        public void ClientSendTowerPriority(Vector3 pos, eTowerTargetPriority prio)
            => _net.SendToHost(PacketType.CmdTowerPriority, w => { w.WriteVector3(pos); w.Write((int)prio); });

        public void HandleCmdTowerPriority(int senderId, BinaryReader r)
        {
            Vector3 pos = r.ReadVector3();
            var prio = (eTowerTargetPriority)r.ReadInt32();
            var s = _session();
            var tower = GameRefs.FindTowerAt(pos, 0.75f);
            if (s == null || tower == null) return;
            if (!s.Ruleset.CanPlayerModifyTower(senderId, tower)) return;
            using (NetworkScope.Enter()) tower.OverrideTargetPriority(prio);
            BroadcastEvtTowerPriority(pos, prio);
        }

        public void BroadcastEvtTowerPriority(Vector3 pos, eTowerTargetPriority prio)
        {
            if (!_net.IsHost) return;
            _net.BroadcastToClients(PacketType.EvtTowerPriority, w => { w.WriteVector3(pos); w.Write((int)prio); });
        }

        public void HandleEvtTowerPriority(BinaryReader r)
        {
            Vector3 pos = r.ReadVector3();
            var prio = (eTowerTargetPriority)r.ReadInt32();
            var tower = GameRefs.FindTowerAt(pos, 0.75f);
            if (tower != null)
            {
                using (NetworkScope.Enter()) tower.OverrideTargetPriority(prio);
            }
        }

        public void ClientSendTowerSpecialAction(Vector3 pos, TowerSpecialActionType action)
            => _net.SendToHost(PacketType.CmdTowerSpecialAction, w => { w.WriteVector3(pos); w.Write((byte)action); });

        public void HandleCmdTowerSpecialAction(int senderId, BinaryReader r)
        {
            Vector3 pos = r.ReadVector3();
            var action = (TowerSpecialActionType)r.ReadByte();
            var s = _session();
            var tower = GameRefs.FindTowerAt(pos, 0.85f);
            if (s == null || tower == null) return;
            if (!s.Ruleset.CanPlayerModifyTower(senderId, tower)) return;
            if (ExecuteTowerSpecialOnControl(tower, action))
            {
                BroadcastEvtTowerSpecialAction(pos, action);
                if (s.Ruleset.SyncsGlobalEconomyAndHand)
                    s.Economy.CheckHostEconomyAndHand(force: true);
            }
        }

        public void BroadcastEvtTowerSpecialAction(Vector3 pos, TowerSpecialActionType action)
        {
            if (!_net.IsHost) return;
            _net.BroadcastToClients(PacketType.EvtTowerSpecialAction, w => { w.WriteVector3(pos); w.Write((byte)action); });
        }

        public void HandleEvtTowerSpecialAction(BinaryReader r)
        {
            Vector3 pos = r.ReadVector3();
            var action = (TowerSpecialActionType)r.ReadByte();
            var tower = GameRefs.FindTowerAt(pos, 0.85f);
            if (tower != null) ExecuteTowerSpecialOnControl(tower, action);
        }

        private static bool ExecuteTowerSpecialOnControl(ABaseTower tower, TowerSpecialActionType action)
        {
            var tc = GameRefs.GetTowerControl();
            if (tc == null) return false;
            var prevTower = tc.curTower;
            using (NetworkScope.Enter())
            {
                try
                {
                    tc.curTower = tower;
                    switch (action)
                    {
                        case TowerSpecialActionType.DiceReroll: tc.OnClickRerollDiceTower(); break;
                        case TowerSpecialActionType.DrillExtra: tc.OnClickDrillTowerExtraButton(); break;
                        case TowerSpecialActionType.ForceActivate: tc.OnClickForceActivateTower(); break;
                        case TowerSpecialActionType.ScorchInverseRotation: tc.OnClickScorchTowerInverseRotation(); break;
                    }
                    return true;
                }
                catch { return false; }
                finally { tc.curTower = prevTower; }
            }
        }

        public void ClientSendDrawCard() => _net.SendToHost(PacketType.CmdDrawCard);
        public void ClientSendMulligan() => _net.SendToHost(PacketType.CmdMulligan);

        public void HandleCmdDrawCard(int senderId)
        {
            var deckUI = GameRefs.GetPlayerDeck();
            if (deckUI == null) return;
            using (NetworkScope.Enter()) deckUI.OnClickButton_DrawCard();
            _session()?.Economy.CheckHostEconomyAndHand(force: true);
        }

        public void HandleCmdMulligan(int senderId)
        {
            var deckUI = GameRefs.GetPlayerDeck();
            if (deckUI == null) return;
            using (NetworkScope.Enter()) deckUI.OnClickButton_Mulligan();
            CoopLogger.Info(LogCategory.Sync, $"{_net.GetPlayerName(senderId)} triggered Mulligan.");
            _session()?.Economy.CheckHostEconomyAndHand(force: true);
        }

        public void ClientSendUseBuff(string runtimeCardId, eItemType buffType, int targetMode, Vector3 targetPos)
        {
            _net.SendToHost(PacketType.CmdUseBuff, w =>
            {
                w.WriteSafeString(runtimeCardId);
                w.Write((int)buffType);
                w.Write(targetMode);
                w.WriteVector3(targetPos);
            });
        }

        public void HandleCmdUseBuff(int senderId, BinaryReader r)
        {
            string runtimeCardId = r.ReadString();
            var buffType = (eItemType)r.ReadInt32();
            int targetMode = r.ReadInt32();
            Vector3 targetPos = r.ReadVector3();

            var s = _session();
            var ig = MainGame.Instance?.IngameData;
            if (s == null || ig == null) return;

            CardData matched = null;
            if (!s.Ruleset.ClientSpendsOwnResourcesOnBuild)
            {
                matched = GameRefs.FindHandCard(ig, runtimeCardId, buffType);
                if (matched == null) { s.Economy.BroadcastSnapshot(senderId); return; }
            }

            if (ApplyBuffInternal(buffType, targetMode, targetPos))
            {
                if (matched != null)
                {
                    using (NetworkScope.Enter())
                    {
                        matched.ConsumeItemUseCount();
                        if (!matched.HasRemainingItemUseCount())
                            ig.OnRequestRemoveCardFromHand(matched);
                    }
                }
                BroadcastEvtUseBuff(senderId, buffType, targetMode, targetPos);
                s.Feedback.SpawnActionFeedback(senderId, targetPos, "BUFF", true);
                if (s.Ruleset.SyncsGlobalEconomyAndHand)
                    s.Economy.CheckHostEconomyAndHand(force: true);
            }
        }

        public void BroadcastEvtUseBuff(int actorId, eItemType buffType, int targetMode, Vector3 targetPos)
        {
            if (!_net.IsHost) return;
            _net.BroadcastToClients(PacketType.EvtUseBuff, w =>
            {
                w.Write(actorId);
                w.Write((int)buffType);
                w.Write(targetMode);
                w.WriteVector3(targetPos);
            });
        }

        public void HandleEvtUseBuff(BinaryReader r)
        {
            int actorId = r.ReadInt32();
            var buffType = (eItemType)r.ReadInt32();
            int targetMode = r.ReadInt32();
            Vector3 targetPos = r.ReadVector3();
            ApplyBuffInternal(buffType, targetMode, targetPos);
            if (actorId != _net.LocalPlayerId)
                _session()?.Feedback.SpawnActionFeedback(actorId, targetPos, "BUFF", true);
        }

        private static bool ApplyBuffInternal(eItemType buffType, int targetMode, Vector3 targetPos)
        {
            var beh = BuffEffectHandler.Instance;
            var rm = ResourceManager.Instance;
            if (beh == null || rm == null) return false;
            var buffData = rm.GetItemDataByType(buffType)?.TryCast<ABaseBuffSettingData>();
            if (buffData == null) return false;

            using (NetworkScope.Enter())
            {
                try
                {
                    switch (targetMode)
                    {
                        case 0:
                            var tower = GameRefs.FindTowerAt(targetPos, 1.0f);
                            if (tower == null) return false;
                            beh.AddBuffToTower(buffData, tower, true, true, -1);
                            return true;
                        case 1:
                            var block = GameRefs.FindTetrisAt(targetPos, 1.5f);
                            if (block == null) return false;
                            beh.AddBuffToTetris(buffData, block, true, true);
                            return true;
                        case 2:
                            var monster = GameRefs.FindMonsterNear(targetPos, 2.5f);
                            if (monster == null) return false;
                            beh.AddBuffToMonster(buffData, monster, true, true);
                            return true;
                        case 3:
                            beh.AddBuffToMapArea(buffData, targetPos, true, true);
                            return true;
                        case 4:
                            beh.OnRequestImmediateBuffCast(buffData, true, true);
                            return true;
                    }
                }
                catch (Exception ex)
                {
                    CoopLogger.Debug(LogCategory.Sync, $"ApplyBuffInternal warning: {ex.Message}");
                }
            }
            return false;
        }

        public void ClientSendActivateRuneStone(Vector3 pos) => _net.SendToHost(PacketType.CmdActivateRuneStone, w => w.WriteVector3(pos));

        public void HandleCmdActivateRuneStone(int senderId, BinaryReader r)
        {
            Vector3 pos = r.ReadVector3();
            var s = _session();
            var stone = GameRefs.FindRuneStoneAt(pos);
            if (s == null || stone == null || stone.isTriggered) return;

            if (!s.Ruleset.ClientSpendsOwnResourcesOnBuild)
            {
                var ig = MainGame.Instance?.IngameData;
                if (ig == null || !ig.IsCoinEnough(stone.GetCurrentCost())) return;
                using (NetworkScope.Enter()) stone.PayToTrigger();
            }
            else
            {
                using (NetworkScope.Enter()) stone.TriggerEffect();
            }

            BroadcastEvtActivateRuneStone(senderId, pos);
            if (s.Ruleset.SyncsGlobalEconomyAndHand)
                s.Economy.CheckHostEconomyAndHand(force: true);
        }

        public void BroadcastEvtActivateRuneStone(int actorId, Vector3 pos)
        {
            if (!_net.IsHost) return;
            _net.BroadcastToClients(PacketType.EvtActivateRuneStone, w => { w.Write(actorId); w.WriteVector3(pos); });
        }

        public void HandleEvtActivateRuneStone(BinaryReader r)
        {
            int actorId = r.ReadInt32();
            Vector3 pos = r.ReadVector3();
            var stone = GameRefs.FindRuneStoneAt(pos);
            if (stone != null && !stone.isTriggered)
            {
                using (NetworkScope.Enter()) stone.TriggerEffect();
                if (actorId != _net.LocalPlayerId)
                    _session()?.Feedback.SpawnActionFeedback(actorId, pos, "RUNE", true);
            }
        }

        public void ClientSendOpenChest(Vector3 pos) => _net.SendToHost(PacketType.CmdOpenChest, w => w.WriteVector3(pos));

        public void HandleCmdOpenChest(int senderId, BinaryReader r)
        {
            Vector3 pos = r.ReadVector3();
            var chest = GameRefs.FindChestAt(pos);
            if (chest == null) return;
            using (NetworkScope.Enter()) chest.OpenChest();
            BroadcastEvtOpenChest(pos);
        }

        public void BroadcastEvtOpenChest(Vector3 pos)
        {
            if (!_net.IsHost) return;
            _net.BroadcastToClients(PacketType.EvtOpenChest, w => w.WriteVector3(pos));
        }

        public void HandleEvtOpenChest(BinaryReader r)
        {
            Vector3 pos = r.ReadVector3();
            var chest = GameRefs.FindChestAt(pos);
            if (chest != null)
            {
                using (NetworkScope.Enter())
                {
                    // Separate mode lets clients open chests for their own independent discover pick.
                    if (_session()?.Ruleset.AllowsClientIndependentDraftsAndShops ?? false)
                    {
                        try { chest.OpenChest(); } catch { }
                    }
                    else
                    {
                        try { chest.RemoveChest(); } catch { }
                    }
                }
            }
        }

        public void ClientSendFocusMonster(Vector3 pos) => _net.SendToHost(PacketType.CmdFocusMonster, w => w.WriteVector3(pos));

        public void HandleCmdFocusMonster(BinaryReader r)
        {
            Vector3 pos = r.ReadVector3();
            var m = GameRefs.FindMonsterNear(pos, 2.5f);
            if (m != null && MonsterManager.Instance != null)
            {
                using (NetworkScope.Enter()) MonsterManager.Instance.OnRequestSetFocusFireMonster(m);
                BroadcastEvtFocusMonster(m.transform.position);
            }
        }

        public void BroadcastEvtFocusMonster(Vector3 pos)
        {
            if (!_net.IsHost) return;
            _net.BroadcastToClients(PacketType.EvtFocusMonster, w => w.WriteVector3(pos));
        }

        public void HandleEvtFocusMonster(BinaryReader r)
        {
            Vector3 pos = r.ReadVector3();
            var m = GameRefs.FindMonsterNear(pos, 2.5f);
            if (m != null && MonsterManager.Instance != null)
            {
                using (NetworkScope.Enter()) MonsterManager.Instance.OnRequestSetFocusFireMonster(m);
            }
        }
    }
}
