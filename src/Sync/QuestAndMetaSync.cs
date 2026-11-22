using System;
using System.IO;
using EmberwardCoop.Core;
using EmberwardCoop.Networking;
using EmberwardCoop.Serialization;

namespace EmberwardCoop.Sync
{
    public sealed class QuestAndMetaSync
    {
        private readonly NetworkManager _net;

        private eQuestStatus _lastBroadcastStatus = eQuestStatus.NONE;
        private string _lastBroadcastProgress = "";
        private eQuestType _lastBroadcastType = eQuestType.NONE;
        private float _lastPollTime;

        public QuestAndMetaSync(NetworkManager net)
        {
            _net = net;
        }

        public void Reset()
        {
            _lastBroadcastStatus = eQuestStatus.NONE;
            _lastBroadcastProgress = "";
            _lastBroadcastType = eQuestType.NONE;
        }

        public void Tick(float now)
        {
            if (!_net.IsHost || _net.RemotePeerCount == 0 || !GameRefs.IsInBattle) return;
            if (now - _lastPollTime < 0.4f) return;
            _lastPollTime = now;

            var qm = QuestManager.Instance;
            var cur = qm?.currentQuest;
            if (cur == null) return;

            var qsUi = GameRefs.GetQuestStatus();
            string progText = qsUi != null ? (qsUi.progressText ?? "") : "";
            eQuestStatus status = cur.status;
            eQuestType qType = cur.challengeType;

            if (status != _lastBroadcastStatus || progText != _lastBroadcastProgress || qType != _lastBroadcastType)
            {
                _lastBroadcastStatus = status;
                _lastBroadcastProgress = progText;
                _lastBroadcastType = qType;
                BroadcastQuestProgress();
            }
        }

        public void BroadcastQuestSnapshot(int targetPeerId = -1)
        {
            if (!_net.IsHost) return;
            var gd = GameDataManager.instance?.GameplayData;
            var q = gd?.quest;
            var cur = QuestManager.Instance?.currentQuest;
            var qsUi = GameRefs.GetQuestStatus();
            int status = cur != null ? (int)cur.status : (int)eQuestStatus.NONE;
            string prog = qsUi != null ? (qsUi.progressText ?? "") : "";

            Action<BinaryWriter> writer = w =>
            {
                GameSerializers.WriteQuestData(w, q);
                w.Write(status);
                w.WriteSafeString(prog);
            };

            if (targetPeerId >= 0) _net.SendToPeer(targetPeerId, PacketType.EvtQuestSnapshot, writer);
            else _net.BroadcastToClients(PacketType.EvtQuestSnapshot, writer);
        }

        public void BroadcastQuestProgress()
        {
            if (!_net.IsHost || _net.RemotePeerCount == 0) return;
            var cur = QuestManager.Instance?.currentQuest;
            if (cur == null) return;
            var qsUi = GameRefs.GetQuestStatus();
            string prog = qsUi != null ? (qsUi.progressText ?? "") : "";

            _net.BroadcastToClients(PacketType.EvtQuestProgress, w =>
            {
                w.Write((int)cur.challengeType);
                w.Write((int)cur.status);
                w.WriteSafeString(prog);
            });
            CoopLogger.Debug(LogCategory.Quest, $"Quest progress synced: {cur.challengeType} [{cur.status}] {prog}");
        }

        public void HandleEvtQuestSnapshot(BinaryReader r)
        {
            var qData = GameSerializers.ReadQuestData(r);
            var status = (eQuestStatus)r.ReadInt32();
            string prog = r.ReadString();

            ApplyQuestOnClient(qData, status, prog);
        }

        public void HandleEvtQuestProgress(BinaryReader r)
        {
            var qType = (eQuestType)r.ReadInt32();
            var status = (eQuestStatus)r.ReadInt32();
            string prog = r.ReadString();

            var qm = QuestManager.Instance;
            var cur = qm?.currentQuest;
            using (NetworkScope.Enter())
            {
                if (cur != null && (qType == eQuestType.NONE || cur.challengeType == qType))
                {
                    if (cur.status != status && status != eQuestStatus.NONE)
                        cur.SwitchStatus(status);
                }
                var qsUi = GameRefs.GetQuestStatus();
                if (qsUi != null)
                {
                    if (!string.IsNullOrEmpty(prog))
                        qsUi.OnUpdateQuestUIProgressText(prog);
                    if (status == eQuestStatus.SUCCESS) qsUi.OnQuestSuccess();
                    else if (status == eQuestStatus.FAILED) qsUi.OnQuestFailed();
                    else if (status == eQuestStatus.IN_PROGRESS) qsUi.OnQuestBackToInProgress();
                }
            }
        }

        public void OnClientQuestRefreshed(QuestData syncedQuest)
        {
            if (!_net.IsClient) return;
            if (syncedQuest == null || syncedQuest.questType == eQuestType.NONE) return;
            ApplyQuestOnClient(syncedQuest, eQuestStatus.IN_PROGRESS, "");
        }

        private static void ApplyQuestOnClient(QuestData qData, eQuestStatus status, string prog)
        {
            var gd = GameDataManager.instance?.GameplayData;
            using (NetworkScope.Enter())
            {
                if (gd != null) gd.quest = qData;

                if (GameRefs.IsInBattle && qData != null && qData.questType != eQuestType.NONE)
                {
                    var qm = QuestManager.Instance;
                    if (qm != null && (qm.currentQuest == null || qm.currentQuest.challengeType != qData.questType))
                    {
                        try { qm.OnRequestLoadQuestInGame(); } catch { }
                    }
                    if (qm?.currentQuest != null && status != eQuestStatus.NONE && qm.currentQuest.status != status)
                    {
                        try { qm.currentQuest.SwitchStatus(status); } catch { }
                    }

                    var qsUi = GameRefs.GetQuestStatus();
                    if (qsUi != null)
                    {
                        try
                        {
                            qsUi.UpdateContent(qData);
                            qsUi.OnToggleQuestStatusUI(true);
                            if (!string.IsNullOrEmpty(prog)) qsUi.OnUpdateQuestUIProgressText(prog);
                        }
                        catch { }
                    }
                }
            }
        }

        public void ClientNotifyQuestTrigger(eQuestType qType, eQuestStatus status)
        {
            if (!_net.IsClient) return;
            _net.SendToHost(PacketType.CmdClientQuestTrigger, w =>
            {
                w.Write((int)qType);
                w.Write((int)status);
            });
        }

        public void HandleCmdClientQuestTrigger(int senderId, BinaryReader r)
        {
            var qType = (eQuestType)r.ReadInt32();
            var clientStatus = (eQuestStatus)r.ReadInt32();
            CoopLogger.Debug(LogCategory.Quest, $"Client #{senderId} reported quest {qType} -> {clientStatus}; replying with authoritative state.");
            BroadcastQuestSnapshot(senderId);
        }

        public void BroadcastMetaRewardGrant(int expEarned, bool isStageVictory, bool isFinalBossVictory, eWorldType worldType, eGameDifficultyType difficulty)
        {
            if (!_net.IsHost || _net.RemotePeerCount == 0) return;

            var gd = GameDataManager.instance?.GameplayData;
            var towers = gd?.list_CollectedTowerData;
            var relics = gd?.list_CollectedRelic;

            _net.BroadcastToClients(PacketType.EvtMetaRewardGrant, w =>
            {
                w.Write(expEarned);
                w.Write(isStageVictory);
                w.Write(isFinalBossVictory);
                w.Write((int)worldType);
                w.Write((int)difficulty);

                int tCount = towers?.Count ?? 0;
                w.Write(tCount);
                for (int i = 0; i < tCount; i++) w.Write(towers[i] != null ? (int)towers[i].ItemType : 0);

                int rCount = relics?.Count ?? 0;
                w.Write(rCount);
                for (int i = 0; i < rCount; i++) w.Write((int)relics[i]);
            });

            CoopLogger.Info(LogCategory.Quest, $"Granted co-op meta-progression to clients (+{expEarned} EXP, victory={isStageVictory}, boss={isFinalBossVictory}).");
        }

        public void HandleEvtMetaRewardGrant(BinaryReader r)
        {
            int expEarned = r.ReadInt32();
            _ = r.ReadBoolean();
            bool isFinalBossVictory = r.ReadBoolean();
            _ = (eWorldType)r.ReadInt32();
            var difficulty = (eGameDifficultyType)r.ReadInt32();

            int tCount = r.ReadInt32();
            var towerTypes = new eItemType[tCount];
            for (int i = 0; i < tCount; i++) towerTypes[i] = (eItemType)r.ReadInt32();

            int rCount = r.ReadInt32();
            var relicTypes = new eItemType[rCount];
            for (int i = 0; i < rCount; i++) relicTypes[i] = (eItemType)r.ReadInt32();

            if (!CoopConfig.AllowClientMetaProgression.Value) return;

            var gdm = GameDataManager.instance;
            var pld = gdm?.Playerdata;
            if (gdm == null || pld == null) return;

            using (NetworkScope.Enter())
            {
                try
                {
                    if (expEarned > 0)
                        pld.OnRequestAddExp(expEarned);

                    for (int i = 0; i < towerTypes.Length; i++)
                    {
                        if (towerTypes[i] != eItemType.NONE)
                        {
                            pld.RecordSeenTowerCard(towerTypes[i]);
                            pld.OnRequestRecordTowerBuilt(towerTypes[i]);
                        }
                    }
                    for (int i = 0; i < relicTypes.Length; i++)
                    {
                        if (relicTypes[i] != eItemType.NONE)
                        {
                            pld.RecordSeenRelic(relicTypes[i]);
                            pld.OnRequestAddRelic(relicTypes[i]);
                        }
                    }

                    if (isFinalBossVictory)
                    {
                        if (difficulty == eGameDifficultyType.CASUAL) pld.isDefeatedCasualModeBoss = true;
                        else if (difficulty == eGameDifficultyType.NORMAL) pld.isDefeatedNormalModeBoss = true;
                        else if (difficulty == eGameDifficultyType.HEROIC || difficulty == eGameDifficultyType.NIGHTMARE) pld.isDefeatedHeroicModeBoss = true;
                    }

                    // Clients run on tempSessionData, so SaveData(false) commits PlayerLifetimeData without modifying their solo save.
                    if (gdm.doUseTempSessionData)
                    {
                        using (NetworkScope.EnterClientMetaSave())
                        {
                            gdm.SaveData(false);
                        }
                    }

                    CoopLogger.Info(LogCategory.Quest, $"Co-op rewards saved to profile: +{expEarned} EXP!");
                    if (expEarned > 0)
                        GameRefs.ShowGameToast($"Co-op Profile Saved (+{expEarned} EXP)", 3.0f);
                }
                catch (Exception ex)
                {
                    CoopLogger.Warn(LogCategory.Quest, $"Client meta-progression commit warning: {ex.Message}");
                }
            }
        }

        public void BroadcastAchievementUnlock(eAchievementType achType)
        {
            if (!_net.IsHost || _net.RemotePeerCount == 0) return;
            _net.BroadcastToClients(PacketType.EvtAchievementTrigger, w => w.Write((int)achType));
        }

        public void HandleEvtAchievementTrigger(BinaryReader r)
        {
            var achType = (eAchievementType)r.ReadInt32();
            if (!CoopConfig.AllowClientMetaProgression.Value) return;
            try
            {
                var am = AchievementManager.Instance;
                if (am != null && !am.IsAchievementUnlocked(achType))
                {
                    using (NetworkScope.Enter())
                    {
                        am.UnlockAchievement(achType);
                    }
                    CoopLogger.Info(LogCategory.Quest, $"Unlocked Co-op Achievement: {achType}!");
                }
            }
            catch (Exception ex)
            {
                CoopLogger.Debug(LogCategory.Quest, $"Client achievement unlock skipped: {ex.Message}");
            }
        }
    }
}
