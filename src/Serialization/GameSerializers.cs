using System;
using System.Collections.Generic;
using System.IO;
using EmberwardCoop.Networking;

namespace EmberwardCoop.Serialization
{
    public struct NetCardInfo
    {
        public string RuntimeId;
        public int CardType;
        public int ItemType;
        public bool IsCorrupted;
        public bool IsBanned;
        public int UseCountLimit;
        public bool IsTetris;
        public int Seed;
        public int SpecialRune;
        public bool DoOverrideSocket;
        public int OverriddenSocketCount;
        public int[] Runes;

        public void Write(BinaryWriter w)
        {
            w.WriteSafeString(RuntimeId);
            w.Write(CardType);
            w.Write(ItemType);
            w.Write(IsCorrupted);
            w.Write(IsBanned);
            w.Write(UseCountLimit);
            w.Write(IsTetris);
            w.Write(Seed);
            w.Write(SpecialRune);
            w.Write(DoOverrideSocket);
            w.Write(OverriddenSocketCount);
            int rCount = Runes?.Length ?? 0;
            w.Write(rCount);
            for (int i = 0; i < rCount; i++) w.Write(Runes[i]);
        }

        public static NetCardInfo Read(BinaryReader r)
        {
            var info = new NetCardInfo
            {
                RuntimeId = r.ReadString(),
                CardType = r.ReadInt32(),
                ItemType = r.ReadInt32(),
                IsCorrupted = r.ReadBoolean(),
                IsBanned = r.ReadBoolean(),
                UseCountLimit = r.ReadInt32(),
                IsTetris = r.ReadBoolean(),
                Seed = r.ReadInt32(),
                SpecialRune = r.ReadInt32(),
                DoOverrideSocket = r.ReadBoolean(),
                OverriddenSocketCount = r.ReadInt32()
            };
            int rCount = r.ReadBoundedCount(64);
            info.Runes = rCount > 0 ? new int[rCount] : Array.Empty<int>();
            for (int i = 0; i < rCount; i++) info.Runes[i] = r.ReadInt32();
            return info;
        }
    }

    public static class GameSerializers
    {
        public static NetCardInfo CaptureCard(CardData c)
        {
            if (c == null)
                return new NetCardInfo { RuntimeId = "", Runes = Array.Empty<int>() };

            if (string.IsNullOrEmpty(c.RuntimeCardId))
                c.EnsureRuntimeCardId();

            var tc = c.TryCast<TetrisCardData>();
            int[] runes = Array.Empty<int>();
            if (tc?.list_Runes != null && tc.list_Runes.Count > 0)
            {
                runes = new int[tc.list_Runes.Count];
                for (int i = 0; i < tc.list_Runes.Count; i++)
                    runes[i] = (int)tc.list_Runes[i];
            }

            return new NetCardInfo
            {
                RuntimeId = c.RuntimeCardId ?? "",
                CardType = (int)c.CardType,
                ItemType = (int)c.ItemType,
                IsCorrupted = c.IsCorrupted,
                IsBanned = c.IsBanned,
                UseCountLimit = c.CardUseCountLimit,
                IsTetris = tc != null,
                Seed = tc != null ? tc.Seed : 0,
                SpecialRune = tc != null ? (int)tc.SpecialRune : 0,
                DoOverrideSocket = tc != null && tc.DoOverrideSocketCount,
                OverriddenSocketCount = tc != null ? tc.OverriddenSocketCount : 0,
                Runes = runes
            };
        }

        public static CardData MaterializeCard(NetCardInfo info)
        {
            var rm = ResourceManager.Instance;
            if (rm == null) return null;
            var setting = rm.GetItemDataByType((eItemType)info.ItemType);
            if (setting == null) return null;

            if (info.IsTetris)
            {
                var tc = TetrisCardData.CreateCardData(setting, false);
                tc.runtimeCardId = info.RuntimeId ?? "";
                tc.isCorrupted = info.IsCorrupted;
                tc.isBanned = info.IsBanned;
                tc.cardUseCountLimit = info.UseCountLimit;
                tc.seed = info.Seed;
                tc.specialRune = (eItemType)info.SpecialRune;
                tc.doOverrideSocketCount = info.DoOverrideSocket;
                tc.overriddenSocketCount = info.OverriddenSocketCount;
                if (tc.list_Runes != null)
                {
                    tc.list_Runes.Clear();
                    if (info.Runes != null)
                    {
                        for (int i = 0; i < info.Runes.Length; i++)
                            tc.list_Runes.Add((eItemType)info.Runes[i]);
                    }
                }
                return tc;
            }
            else
            {
                var cd = CardData.CreateCardData(setting, false);
                cd.runtimeCardId = info.RuntimeId ?? "";
                cd.isCorrupted = info.IsCorrupted;
                cd.isBanned = info.IsBanned;
                cd.cardUseCountLimit = info.UseCountLimit;
                return cd;
            }
        }

        public static void WriteMapNode(BinaryWriter w, MapNodeData n)
        {
            if (n == null) { w.Write(false); return; }
            w.Write(true);

            var shopNode = n.TryCast<ShopMapNodeData>();
            w.Write(shopNode != null);

            w.Write(n.Index);
            w.Write(n.Step);
            w.Write(n.IndexInStep);
            w.Write((int)n.MapNodeType);
            w.Write((int)n.State);
            w.Write(n.randomSeed);
            w.Write(n.connectedFromCount);
            w.Write(n.difficulty);
            w.WriteSafeString(n.stageEnvSceneName);
            w.Write((int)n.anomalyPerkType);
            w.Write((int)n.extraAnomalyPerkType);
            w.Write((int)n.buffAnomalyPerkType);
            w.Write(n.isCleared);

            var conn = n.connectedIndex;
            int cCount = conn?.Count ?? 0;
            w.Write(cCount);
            for (int i = 0; i < cCount; i++) w.Write(conn[i]);

            var bless = n.list_BlessingPerkType;
            int bCount = bless?.Count ?? 0;
            w.Write(bCount);
            for (int i = 0; i < bCount; i++) w.Write((int)bless[i]);

            var extra = n.List_ExtraData;
            int eCount = extra?.Count ?? 0;
            w.Write(eCount);
            for (int i = 0; i < eCount; i++) w.Write(extra[i]);

            var sr = n.stageReward;
            if (sr == null)
            {
                w.Write(false);
            }
            else
            {
                w.Write(true);
                w.Write(sr.Exp);
                w.Write(sr.Gem);
                w.Write(sr.isDisplayAsUnknown);
                var dic = sr.dic_RewardEntries;
                if (dic == null)
                {
                    w.Write(0);
                }
                else
                {
                    var entries = new List<StageRewardEntry>();
                    foreach (var kv in dic)
                        if (kv.Value != null) entries.Add(kv.Value);
                    w.Write(entries.Count);
                    for (int i = 0; i < entries.Count; i++)
                    {
                        w.Write((int)entries[i].RewardType);
                        w.Write((int)entries[i].ItemType);
                        w.Write(entries[i].count);
                    }
                }
            }

            if (shopNode != null)
            {
                w.Write(shopNode.IsCreatedShopContent);
                w.Write(shopNode.rerollCost);
                w.Write(shopNode.isHealSold);

                var items = shopNode.list_ShopContent;
                int sCount = items?.Count ?? 0;
                w.Write(sCount);
                for (int i = 0; i < sCount; i++)
                    CaptureCard(items[i]).Write(w);

                var bought = shopNode.list_IsCardBought;
                int boughtCount = bought?.Count ?? 0;
                w.Write(boughtCount);
                for (int i = 0; i < boughtCount; i++) w.Write(bought[i]);

                var disc = shopNode.list_IsDiscount;
                int discCount = disc?.Count ?? 0;
                w.Write(discCount);
                for (int i = 0; i < discCount; i++) w.Write(disc[i]);
            }
        }

        public static MapNodeData ReadMapNode(BinaryReader r)
        {
            if (!r.ReadBoolean()) return null;
            bool isShop = r.ReadBoolean();

            int index = r.ReadInt32();
            int step = r.ReadInt32();
            int indexInStep = r.ReadInt32();
            var nodeType = (eStageType)r.ReadInt32();
            var state = (eMapNodeState)r.ReadInt32();
            int seed = r.ReadInt32();
            int connFrom = r.ReadInt32();
            int diff = r.ReadInt32();
            string envScene = r.ReadString();
            var anom1 = (eItemType)r.ReadInt32();
            var anom2 = (eItemType)r.ReadInt32();
            var anomBuff = (eItemType)r.ReadInt32();
            bool isCleared = r.ReadBoolean();

            var node = new MapNodeData(index, step, indexInStep, nodeType, state)
            {
                randomSeed = seed,
                connectedFromCount = connFrom,
                difficulty = diff,
                stageEnvSceneName = envScene,
                anomalyPerkType = anom1,
                extraAnomalyPerkType = anom2,
                buffAnomalyPerkType = anomBuff,
                isCleared = isCleared
            };

            int cCount = r.ReadBoundedCount(128);
            node.connectedIndex ??= new Il2CppSystem.Collections.Generic.List<int>();
            node.connectedIndex.Clear();
            for (int i = 0; i < cCount; i++) node.connectedIndex.Add(r.ReadInt32());

            int bCount = r.ReadBoundedCount(128);
            node.list_BlessingPerkType ??= new Il2CppSystem.Collections.Generic.List<eItemType>();
            node.list_BlessingPerkType.Clear();
            for (int i = 0; i < bCount; i++) node.list_BlessingPerkType.Add((eItemType)r.ReadInt32());

            int eCount = r.ReadBoundedCount(128);
            node.List_ExtraData ??= new Il2CppSystem.Collections.Generic.List<int>();
            node.List_ExtraData.Clear();
            for (int i = 0; i < eCount; i++) node.List_ExtraData.Add(r.ReadInt32());

            if (r.ReadBoolean())
            {
                int exp = r.ReadInt32();
                int gem = r.ReadInt32();
                bool unknown = r.ReadBoolean();
                var sr = new StageRewardData(exp, gem) { isDisplayAsUnknown = unknown };
                int entryCount = r.ReadBoundedCount(64);
                for (int i = 0; i < entryCount; i++)
                {
                    var rt = (eStageRewardType)r.ReadInt32();
                    var it = (eItemType)r.ReadInt32();
                    int cnt = r.ReadInt32();
                    sr.AddRewardEntry(rt, it, cnt);
                }
                node.stageReward = sr;
            }

            if (isShop)
            {
                bool created = r.ReadBoolean();
                int rerollCost = r.ReadInt32();
                bool healSold = r.ReadBoolean();

                int sCount = r.ReadBoundedCount(64);
                var cards = new NetCardInfo[sCount];
                for (int i = 0; i < sCount; i++) cards[i] = NetCardInfo.Read(r);

                int boughtCount = r.ReadBoundedCount(64);
                var bought = new bool[boughtCount];
                for (int i = 0; i < boughtCount; i++) bought[i] = r.ReadBoolean();

                int discCount = r.ReadBoundedCount(64);
                var disc = new bool[discCount];
                for (int i = 0; i < discCount; i++) disc[i] = r.ReadBoolean();

                try
                {
                    var shopNode = ShopMapNodeData.TurnToShopMapNodeData(node);
                    if (shopNode != null)
                    {
                        shopNode.IsCreatedShopContent = created;
                        shopNode.rerollCost = rerollCost;
                        shopNode.isHealSold = healSold;
                        if (created && sCount > 0)
                        {
                            shopNode.list_ShopContent ??= new Il2CppSystem.Collections.Generic.List<CardData>();
                            shopNode.list_ShopContent.Clear();
                            for (int i = 0; i < sCount; i++)
                            {
                                var c = MaterializeCard(cards[i]);
                                if (c != null) shopNode.list_ShopContent.Add(c);
                            }
                            shopNode.list_IsCardBought ??= new Il2CppSystem.Collections.Generic.List<bool>();
                            shopNode.list_IsCardBought.Clear();
                            for (int i = 0; i < boughtCount; i++) shopNode.list_IsCardBought.Add(bought[i]);

                            shopNode.list_IsDiscount ??= new Il2CppSystem.Collections.Generic.List<bool>();
                            shopNode.list_IsDiscount.Clear();
                            for (int i = 0; i < discCount; i++) shopNode.list_IsDiscount.Add(disc[i]);
                        }
                        return shopNode;
                    }
                }
                catch { }
            }

            return node;
        }

        public static void WriteQuestData(BinaryWriter w, QuestData q)
        {
            if (q == null || q.questType == eQuestType.NONE) { w.Write(false); return; }
            w.Write(true);
            w.Write((int)q.questType);
            w.Write((int)q.difficulty);
            w.Write((int)q.RewardType);
            w.Write((int)q.ItemType);
            w.Write(q.RewardAmount);
            w.Write(q.RequireItemCount);
            w.Write((int)q.Requirement_item_1);
            w.Write((int)q.Requirement_item_2);
            w.Write((int)q.Requirement_item_3);
            w.Write(q.RequireValueCount);
            w.Write(q.Requirement_value_1);
            w.Write(q.Requirement_value_2);
            w.Write(q.Requirement_value_3);
        }

        public static QuestData ReadQuestData(BinaryReader r)
        {
            if (!r.ReadBoolean()) return null;
            return new QuestData
            {
                questType = (eQuestType)r.ReadInt32(),
                difficulty = (eQuestDifficulty)r.ReadInt32(),
                RewardType = (eQuestRewardType)r.ReadInt32(),
                ItemType = (eItemType)r.ReadInt32(),
                RewardAmount = r.ReadInt32(),
                RequireItemCount = r.ReadInt32(),
                Requirement_item_1 = (eItemType)r.ReadInt32(),
                Requirement_item_2 = (eItemType)r.ReadInt32(),
                Requirement_item_3 = (eItemType)r.ReadInt32(),
                RequireValueCount = r.ReadInt32(),
                Requirement_value_1 = r.ReadInt32(),
                Requirement_value_2 = r.ReadInt32(),
                Requirement_value_3 = r.ReadInt32()
            };
        }

        public static void WriteAltarPacts(BinaryWriter w, Il2CppSystem.Collections.Generic.List<AltarPactData> pacts)
        {
            int count = pacts?.Count ?? 0;
            w.Write(count);
            for (int i = 0; i < count; i++)
            {
                var p = pacts[i];
                w.Write(p != null ? (int)p.altarEffectType : 0);
                w.Write(p != null ? (int)p.perkEffect : 0);
                w.Write(p != null ? (int)p.rewardType : 0);
            }
        }

        public static void ReadAltarPactsInto(BinaryReader r, GameplayData gd)
        {
            int count = r.ReadBoundedCount(64);
            if (gd == null)
            {
                for (int i = 0; i < count; i++) { r.ReadInt32(); r.ReadInt32(); r.ReadInt32(); }
                return;
            }
            gd.list_AltarPactData ??= new Il2CppSystem.Collections.Generic.List<AltarPactData>();
            gd.list_AltarPactData.Clear();
            for (int i = 0; i < count; i++)
            {
                var eff = (eAltarEffectTypeV2)r.ReadInt32();
                var perk = (eItemType)r.ReadInt32();
                var rew = (eItemType)r.ReadInt32();
                gd.list_AltarPactData.Add(new AltarPactData
                {
                    altarEffectType = eff,
                    perkEffect = perk,
                    rewardType = rew
                });
            }
        }

        public static void WriteHardModeSetting(BinaryWriter w, HardModeSetting hm)
        {
            var list = hm?.GetShardLevelList();
            if (list == null) { w.Write(0); return; }
            w.Write(list.Count);
            for (int i = 0; i < list.Count; i++) w.Write(list[i]);
        }

        public static void ReadHardModeSettingInto(BinaryReader r, GameplayData gd)
        {
            int count = r.ReadBoundedCount(64);
            if (count <= 0) return;
            var levels = new Il2CppSystem.Collections.Generic.List<int>(count);
            for (int i = 0; i < count; i++) levels.Add(r.ReadInt32());
            if (gd != null)
            {
                gd.hardModeData ??= new HardModeSetting();
                try { gd.hardModeData.SetShardLevels(levels); } catch { }
            }
        }

        public static void WriteCharacterExtras(BinaryWriter w, GameplayData gd)
        {
            var kd = gd?.kingData;
            w.Write(kd != null);
            if (kd != null)
            {
                w.Write(kd.level_Pawn);
                w.Write(kd.level_Knight);
                w.Write(kd.level_Bishop);
                w.Write(kd.level_Rook);
                w.Write(kd.level_Queen);
            }

            var sm = gd?.scrapMasterData;
            w.Write(sm != null);
            if (sm != null)
            {
                w.Write(sm.exp);
                w.Write(sm.level);
                w.Write(sm.isFirstSkillLearned);
            }
        }

        public static void ReadCharacterExtrasInto(BinaryReader r, GameplayData gd)
        {
            if (r.ReadBoolean())
            {
                int pawn = r.ReadInt32(), knight = r.ReadInt32(), bishop = r.ReadInt32(), rook = r.ReadInt32(), queen = r.ReadInt32();
                if (gd?.kingData != null)
                {
                    gd.kingData.level_Pawn = pawn;
                    gd.kingData.level_Knight = knight;
                    gd.kingData.level_Bishop = bishop;
                    gd.kingData.level_Rook = rook;
                    gd.kingData.level_Queen = queen;
                }
            }
            if (r.ReadBoolean())
            {
                int exp = r.ReadInt32(), lvl = r.ReadInt32();
                bool first = r.ReadBoolean();
                if (gd?.scrapMasterData != null)
                {
                    gd.scrapMasterData.exp = exp;
                    gd.scrapMasterData.level = lvl;
                    gd.scrapMasterData.isFirstSkillLearned = first;
                }
            }
        }
    }
}
