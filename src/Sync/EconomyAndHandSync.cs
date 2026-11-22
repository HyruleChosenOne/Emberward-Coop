using System;
using System.Collections.Generic;
using System.IO;
using EmberwardCoop.Core;
using EmberwardCoop.Networking;
using EmberwardCoop.Serialization;

namespace EmberwardCoop.Sync
{
    public sealed class EconomyAndHandSync
    {
        private readonly NetworkManager _net;
        private readonly Func<SessionController> _session;

        private bool _dirty;
        private float _lastCheckTime;
        private float _lastHashBroadcastTime;

        private int _lastCoin = int.MinValue;
        private int _lastEnergy = int.MinValue;
        private int _lastHP = int.MinValue;
        private int _lastArmor = int.MinValue;
        private int _lastScore = int.MinValue;
        private int _lastDrawCost = int.MinValue;
        private int _lastFreeDraw = int.MinValue;
        private int _lastCosmoShard = int.MinValue;
        private int _lastExtraHand = int.MinValue;
        private int _lastDeckCount = int.MinValue;
        private ulong _lastHandHash;
        private ulong _lastLoadoutHash;

        private int _consecutiveHashMismatches;
        public bool IsDesyncWarningActive { get; private set; }
        public ulong LastComputedStateHash { get; private set; }

        public EconomyAndHandSync(NetworkManager net, Func<SessionController> session)
        {
            _net = net;
            _session = session;
        }

        public void MarkDirty() => _dirty = true;

        public void ResetBattleCache()
        {
            _lastCoin = int.MinValue;
            _lastEnergy = int.MinValue;
            _lastHP = int.MinValue;
            _lastArmor = int.MinValue;
            _lastScore = int.MinValue;
            _lastDrawCost = int.MinValue;
            _lastFreeDraw = int.MinValue;
            _lastCosmoShard = int.MinValue;
            _lastExtraHand = int.MinValue;
            _lastDeckCount = int.MinValue;
            _lastHandHash = 0;
            _lastLoadoutHash = 0;
            _consecutiveHashMismatches = 0;
            IsDesyncWarningActive = false;
        }

        public void Tick(float now)
        {
            if (!_net.IsHost || _net.RemotePeerCount == 0 || !GameRefs.IsInBattle) return;

            if (_dirty || now - _lastCheckTime >= 0.05f)
            {
                _lastCheckTime = now;
                CheckHostEconomyAndHand(_dirty);
                _dirty = false;
            }

            if (now - _lastHashBroadcastTime >= 2.0f)
            {
                _lastHashBroadcastTime = now;
                BroadcastStateHash();
            }
        }

        private static ulong HashCombine(ulong h, ulong v) => (h ^ v) * 1099511628211UL;
        private static ulong HashString(ulong h, string s)
        {
            if (string.IsNullOrEmpty(s)) return HashCombine(h, 0);
            for (int i = 0; i < s.Length; i++) h = HashCombine(h, s[i]);
            return h;
        }

        public static ulong ComputeHandHash(IngameData ig)
        {
            var hand = ig?.list_HandCardData;
            if (hand == null) return 0;
            ulong h = 14695981039346656037UL;
            h = HashCombine(h, (ulong)hand.Count);
            for (int i = 0; i < hand.Count; i++)
            {
                var c = hand[i];
                if (c == null) continue;
                if (string.IsNullOrEmpty(c.RuntimeCardId)) c.EnsureRuntimeCardId();
                h = HashString(h, c.RuntimeCardId);
                h = HashCombine(h, (ulong)(int)c.ItemType);
                h = HashCombine(h, (ulong)((c.IsCorrupted ? 1 : 0) | (c.IsBanned ? 2 : 0) | (c.CardUseCountLimit << 2)));
                var tc = c.TryCast<TetrisCardData>();
                if (tc != null)
                {
                    h = HashCombine(h, (ulong)tc.Seed);
                    h = HashCombine(h, (ulong)(int)tc.SpecialRune);
                    var runes = tc.list_Runes;
                    if (runes != null)
                    {
                        h = HashCombine(h, (ulong)runes.Count);
                        for (int r = 0; r < runes.Count; r++) h = HashCombine(h, (ulong)(int)runes[r]);
                    }
                }
            }
            return h;
        }

        private static ulong ComputeLoadoutHash()
        {
            var list = GameDataManager.instance?.GameplayData?.list_LoadoutTowerData;
            if (list == null) return 0;
            ulong h = 14695981039346656037UL;
            h = HashCombine(h, (ulong)list.Count);
            for (int i = 0; i < list.Count; i++)
            {
                var t = list[i];
                if (t == null) continue;
                h = HashCombine(h, (ulong)(int)t.ItemType);
                h = HashCombine(h, (ulong)t.Level);
            }
            return h;
        }

        public ulong ComputeBattleStateHash(out int round, out int coin, out int hp, out int towerCount, out int towerLevelSum, out int tetrisCount)
        {
            var mg = MainGame.Instance;
            var ig = mg?.IngameData;
            round = mg != null ? mg.Round : 0;
            coin = ig != null ? ig.Coin : 0;
            hp = ig != null ? ig.HP : 0;
            var towers = TowerManager.Instance?.GetAllTowersOnField();
            towerCount = towers?.Count ?? 0;
            towerLevelSum = 0;
            if (towers != null)
            {
                for (int i = 0; i < towers.Count; i++)
                    if (towers[i] != null) towerLevelSum += towers[i].Level;
            }
            tetrisCount = TetrisManager.Instance?.GetTetrisOnFieldCount() ?? 0;

            bool syncEco = _session()?.Ruleset.SyncsGlobalEconomyAndHand ?? true;
            ulong h = 14695981039346656037UL;
            h = HashCombine(h, (ulong)round);
            if (syncEco) h = HashCombine(h, (ulong)coin);
            h = HashCombine(h, (ulong)hp);
            h = HashCombine(h, (ulong)(ig?.Armor ?? 0));
            h = HashCombine(h, (ulong)towerCount);
            h = HashCombine(h, (ulong)towerLevelSum);
            h = HashCombine(h, (ulong)tetrisCount);
            if (syncEco) h = HashCombine(h, ComputeHandHash(ig));
            LastComputedStateHash = h;
            return h;
        }

        public void CheckHostEconomyAndHand(bool force)
        {
            if (!_net.IsHost || _net.RemotePeerCount == 0) return;
            var ig = MainGame.Instance?.IngameData;
            if (ig == null) return;

            ulong handHash = ComputeHandHash(ig);
            ulong loadoutHash = ComputeLoadoutHash();
            int deckCount = ig.GetDeckCardCount();

            bool changed = force
                || ig.Coin != _lastCoin
                || ig.Energy != _lastEnergy
                || ig.HP != _lastHP
                || ig.Armor != _lastArmor
                || ig.Score != _lastScore
                || ig.DrawCardCost != _lastDrawCost
                || ig.FreeDrawCardChance != _lastFreeDraw
                || ig.CosmoShard != _lastCosmoShard
                || ig.ExtraHandCardLimit != _lastExtraHand
                || deckCount != _lastDeckCount
                || handHash != _lastHandHash
                || loadoutHash != _lastLoadoutHash;

            if (changed)
            {
                _lastCoin = ig.Coin;
                _lastEnergy = ig.Energy;
                _lastHP = ig.HP;
                _lastArmor = ig.Armor;
                _lastScore = ig.Score;
                _lastDrawCost = ig.DrawCardCost;
                _lastFreeDraw = ig.FreeDrawCardChance;
                _lastCosmoShard = ig.CosmoShard;
                _lastExtraHand = ig.ExtraHandCardLimit;
                _lastDeckCount = deckCount;
                _lastHandHash = handHash;
                _lastLoadoutHash = loadoutHash;
                BroadcastSnapshot(-1);
            }
        }

        public void BroadcastSnapshot(int targetPeerId = -1)
        {
            if (!_net.IsHost) return;
            var ig = MainGame.Instance?.IngameData;
            if (ig == null) return;
            var gd = GameDataManager.instance?.GameplayData;

            Action<BinaryWriter> writer = w =>
            {
                w.Write(ig.Coin);
                w.Write(ig.Energy);
                w.Write(ig.HP);
                w.Write(ig.Armor);
                w.Write(ig.Score);
                w.Write(ig.DrawCardCost);
                w.Write(ig.FreeDrawCardChance);
                w.Write(ig.CosmoShard);
                w.Write(ig.ExtraHandCardLimit);
                w.Write(ig.GetDeckCardCount());
                w.Write(ig.GetDiscardCardCount());

                var loadout = gd?.list_LoadoutTowerData;
                int lCount = loadout?.Count ?? 0;
                w.Write(lCount);
                for (int i = 0; i < lCount; i++)
                {
                    var t = loadout[i];
                    w.Write(t != null ? (int)t.ItemType : 0);
                    w.Write(t != null ? t.Level : 1);
                }

                var hand = ig.list_HandCardData;
                int hCount = hand?.Count ?? 0;
                w.Write(hCount);
                for (int i = 0; i < hCount; i++)
                    GameSerializers.CaptureCard(hand[i]).Write(w);
            };

            if (targetPeerId >= 0) _net.SendToPeer(targetPeerId, PacketType.EconomyAndHandSnapshot, writer);
            else _net.BroadcastToClients(PacketType.EconomyAndHandSnapshot, writer);
        }

        public void HandleSnapshot(BinaryReader r)
        {
            int coin = r.ReadInt32();
            int energy = r.ReadInt32();
            int hp = r.ReadInt32();
            int armor = r.ReadInt32();
            int score = r.ReadInt32();
            int drawCost = r.ReadInt32();
            int freeDraw = r.ReadInt32();
            int cosmoShard = r.ReadInt32();
            int extraHand = r.ReadInt32();
            int deckCount = r.ReadInt32();
            _ = r.ReadInt32();

            int lCount = r.ReadInt32();
            var loadoutTypes = new int[lCount];
            var loadoutLevels = new int[lCount];
            for (int i = 0; i < lCount; i++) { loadoutTypes[i] = r.ReadInt32(); loadoutLevels[i] = r.ReadInt32(); }

            int hCount = r.ReadInt32();
            var hostCards = new NetCardInfo[hCount];
            for (int i = 0; i < hCount; i++) hostCards[i] = NetCardInfo.Read(r);

            var ig = MainGame.Instance?.IngameData;
            if (ig == null) return;

            bool syncGold = _session()?.Ruleset.SyncsGlobalGold ?? true;
            bool syncDeckHand = _session()?.Ruleset.SyncsGlobalDeckAndHand ?? true;

            using (NetworkScope.Enter())
            {
                // Core Health, Armor, and Score are ALWAYS a single shared pool across all players.
                if (ig.HP != hp) ig.SetHP(hp);
                if (ig.Armor != armor) ig.SetArmor(armor);
                if (ig.Score != score) ig.SetScore(score);

                if (syncGold)
                {
                    if (ig.Coin != coin) ig.SetCoin(coin);
                    if (ig.Energy != energy)
                    {
                        ig._Energy_k__BackingField = energy;
                        GameRefs.GetPlayerStatus()?.OnEnergyChanged(energy);
                    }
                    if (ig.CosmoShard != cosmoShard) ig.SetCosmoShard(cosmoShard);
                }

                if (!syncDeckHand)
                    return;

                ig._FreeDrawCardChance_k__BackingField = freeDraw;
                ig._ExtraHandCardLimit_k__BackingField = extraHand;

                var deckUI = GameRefs.GetPlayerDeck();
                if (ig.DrawCardCost != drawCost)
                {
                    ig._DrawCardCost_k__BackingField = drawCost;
                    deckUI?.OnDrawCardCostChanged(drawCost);
                }
                if (deckUI != null && deckUI.text_DeckCardCount != null)
                    deckUI.text_DeckCardCount.text = deckCount.ToString();

                var gd = GameDataManager.instance?.GameplayData;
                if (gd?.list_LoadoutTowerData != null && lCount > 0)
                {
                    bool diff = gd.list_LoadoutTowerData.Count != lCount;
                    if (!diff)
                    {
                        for (int i = 0; i < lCount; i++)
                        {
                            if ((int)gd.list_LoadoutTowerData[i].ItemType != loadoutTypes[i] ||
                                gd.list_LoadoutTowerData[i].Level != loadoutLevels[i])
                            { diff = true; break; }
                        }
                    }
                    if (diff)
                    {
                        gd.list_LoadoutTowerData.Clear();
                        for (int i = 0; i < lCount; i++)
                            gd.list_LoadoutTowerData.Add(new TowerIngameData((eItemType)loadoutTypes[i], loadoutLevels[i]));
                        GameRefs.GetPlayerTowerList()?.UpdateCards(gd.list_LoadoutTowerData);
                        GameRefs.SendEvent0(eGameEvents.OnTowerCardChanged);
                    }
                }

                ReconcileClientHand(ig, hostCards);
            }
        }

        private static void ReconcileClientHand(IngameData ig, NetCardInfo[] hostCards)
        {
            var hand = ig.list_HandCardData;
            if (hand == null) return;

            var cc = GameRefs.GetCardController();
            var hostIds = new HashSet<string>(StringComparer.Ordinal);
            for (int i = 0; i < hostCards.Length; i++)
                if (!string.IsNullOrEmpty(hostCards[i].RuntimeId)) hostIds.Add(hostCards[i].RuntimeId);

            // Cancel local placement if another player already placed the held card
            var oph = MainGame.Instance?.ObjectPlacementHandler;
            if (oph != null && oph.IsCurrentInPlacement() && oph.currentPlacementCardData != null)
            {
                string holdingId = oph.currentPlacementCardData.RuntimeCardId ?? "";
                if (!string.IsNullOrEmpty(holdingId) && !hostIds.Contains(holdingId))
                    GameRefs.CancelLocalPlacementIfActive();
            }

            var seenClientIds = new HashSet<string>(StringComparer.Ordinal);
            for (int i = hand.Count - 1; i >= 0; i--)
            {
                var c = hand[i];
                if (c == null || string.IsNullOrEmpty(c.RuntimeCardId) ||
                    !hostIds.Contains(c.RuntimeCardId) || !seenClientIds.Add(c.RuntimeCardId))
                {
                    if (c != null) ig.OnRequestRemoveCardFromHand(c);
                    else hand.RemoveAt(i);
                }
            }

            var existingById = new Dictionary<string, CardData>(StringComparer.Ordinal);
            for (int i = 0; i < hand.Count; i++)
            {
                var c = hand[i];
                if (c != null && !string.IsNullOrEmpty(c.RuntimeCardId))
                    existingById[c.RuntimeCardId] = c;
            }

            bool propsUpdated = false;
            var addedIds = new HashSet<string>(StringComparer.Ordinal);
            for (int i = 0; i < hostCards.Length; i++)
            {
                var hc = hostCards[i];
                if (string.IsNullOrEmpty(hc.RuntimeId) || !addedIds.Add(hc.RuntimeId)) continue;

                if (existingById.TryGetValue(hc.RuntimeId, out var existing))
                {
                    if (existing.isCorrupted != hc.IsCorrupted ||
                        existing.isBanned != hc.IsBanned ||
                        existing.cardUseCountLimit != hc.UseCountLimit)
                    {
                        existing.isCorrupted = hc.IsCorrupted;
                        existing.isBanned = hc.IsBanned;
                        existing.cardUseCountLimit = hc.UseCountLimit;
                        propsUpdated = true;
                    }
                    var tc = existing.TryCast<TetrisCardData>();
                    if (tc != null && hc.IsTetris)
                    {
                        tc.seed = hc.Seed;
                        tc.specialRune = (eItemType)hc.SpecialRune;
                        if (tc.list_Runes != null && hc.Runes != null)
                        {
                            bool runesDiff = tc.list_Runes.Count != hc.Runes.Length;
                            if (!runesDiff)
                            {
                                for (int rIdx = 0; rIdx < hc.Runes.Length; rIdx++)
                                    if ((int)tc.list_Runes[rIdx] != hc.Runes[rIdx]) { runesDiff = true; break; }
                            }
                            if (runesDiff)
                            {
                                tc.list_Runes.Clear();
                                for (int rIdx = 0; rIdx < hc.Runes.Length; rIdx++)
                                    tc.list_Runes.Add((eItemType)hc.Runes[rIdx]);
                                propsUpdated = true;
                            }
                        }
                    }
                }
                else
                {
                    var newCard = GameSerializers.MaterializeCard(hc);
                    if (newCard != null)
                    {
                        ig.AddCardToPlayerHand(newCard);
                        existingById[hc.RuntimeId] = newCard;
                    }
                }
            }

            if (cc?.list_Cards != null)
            {
                var seenVisualIds = new HashSet<string>(StringComparer.Ordinal);
                for (int i = cc.list_Cards.Count - 1; i >= 0; i--)
                {
                    var uiCard = cc.list_Cards[i];
                    if (uiCard == null) continue;
                    var cd = uiCard.CardData;
                    if (cd == null || string.IsNullOrEmpty(cd.RuntimeCardId) ||
                        !hostIds.Contains(cd.RuntimeCardId) || !seenVisualIds.Add(cd.RuntimeCardId))
                    {
                        uiCard.DiscardCard();
                    }
                    else if (propsUpdated)
                    {
                        uiCard.ToggleCorrupted(cd.IsCorrupted);
                        uiCard.ToggleBanned(cd.IsBanned);
                    }
                }
                if (propsUpdated) cc.OnCardChanged(hand);
            }
        }

        private void BroadcastStateHash()
        {
            ulong hash = ComputeBattleStateHash(out int round, out int coin, out int hp, out int towers, out int lvlSum, out int tetris);
            _net.BroadcastToClients(PacketType.EvtStateHash, w =>
            {
                w.Write(hash);
                w.Write(round);
                w.Write(coin);
                w.Write(hp);
                w.Write(towers);
                w.Write(lvlSum);
                w.Write(tetris);
            }, DeliveryMode.Unreliable);
            CoopLogger.Debug(LogCategory.Hash, $"Host StateHash={hash:X16} R={round} G={coin} HP={hp} T={towers}(Lv{lvlSum}) B={tetris}");
        }

        public void HandleEvtStateHash(BinaryReader r)
        {
            ulong hostHash = r.ReadUInt64();
            int hRound = r.ReadInt32();
            int hCoin = r.ReadInt32();
            int hHp = r.ReadInt32();
            int hTowers = r.ReadInt32();
            int hLvlSum = r.ReadInt32();
            int hTetris = r.ReadInt32();

            if (!GameRefs.IsInBattle) return;

            ulong localHash = ComputeBattleStateHash(out int lRound, out int lCoin, out int lHp, out int lTowers, out int lLvlSum, out int lTetris);
            if (localHash == hostHash)
            {
                _consecutiveHashMismatches = 0;
                IsDesyncWarningActive = false;
                return;
            }

            _consecutiveHashMismatches++;
            CoopLogger.Debug(LogCategory.Hash,
                $"Hash drift #{_consecutiveHashMismatches}: Host({hostHash:X8} R={hRound} G={hCoin} HP={hHp} T={hTowers}/L{hLvlSum} B={hTetris}) vs Local({localHash:X8} R={lRound} G={lCoin} HP={lHp} T={lTowers}/L{lLvlSum} B={lTetris})");

            if (_consecutiveHashMismatches >= 2)
            {
                IsDesyncWarningActive = true;
                _consecutiveHashMismatches = 0;
                CoopLogger.Warn(LogCategory.Hash, $"State drift detected (T:{lTowers}->{hTowers}, B:{lTetris}->{hTetris}, G:{lCoin}->{hCoin}) — auto-reconciling!");
                _net.SendToHost(PacketType.CmdDesyncReport, w =>
                {
                    w.Write(localHash);
                    w.Write(lTowers);
                    w.Write(lTetris);
                    w.Write(lCoin);
                });
            }
        }

        public void HandleCmdDesyncReport(int senderId, BinaryReader r)
        {
            ulong clientHash = r.ReadUInt64();
            int cTowers = r.ReadInt32();
            int cTetris = r.ReadInt32();
            int cCoin = r.ReadInt32();
            CoopLogger.Debug(LogCategory.Hash, $"Reconciling Peer #{senderId} (hash={clientHash:X8}, T={cTowers}, B={cTetris}, G={cCoin})");
            BroadcastSnapshot(senderId);
            _session()?.Board.BroadcastBoardSnapshot(senderId);
        }
    }
}
