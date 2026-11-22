using System;
using EmberwardCoop.Core;
using EmberwardCoop.Localization;
using EmberwardCoop.Sync;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace EmberwardCoop.UI
{
    public static class PostRunSummaryOverlay
    {
        public static void AttachToPopup(Transform popupTransform, bool isVictory)
        {
            var s = SessionController.Instance;
            if (s == null || !s.Net.IsConnected || popupTransform == null) return;

            try
            {
                var existing = popupTransform.Find("EWCoop_TeamworkSummary");
                if (existing != null) UnityEngine.Object.Destroy(existing.gameObject);

                NativeStyleProvider.HarvestFromScene();

                var panel = NativeStyleProvider.CreatePanel(popupTransform, "EWCoop_TeamworkSummary", new Color(0.09f, 0.08f, 0.13f, 0.94f), addBorder: true);
                var rt = panel.GetComponent<RectTransform>();
                rt.anchorMin = new Vector2(0.5f, 0f);
                rt.anchorMax = new Vector2(0.5f, 0f);
                rt.pivot = new Vector2(0.5f, 0f);
                rt.anchoredPosition = new Vector2(0f, 22f);
                rt.sizeDelta = new Vector2(820f, 185f);

                var vlg = panel.AddComponent<VerticalLayoutGroup>();
                vlg.padding = new RectOffset(18, 18, 12, 12);
                vlg.spacing = 6f;
                vlg.childControlWidth = true;
                vlg.childControlHeight = true;
                vlg.childForceExpandWidth = true;
                vlg.childForceExpandHeight = false;

                string headerTitle = isVictory
                    ? $"{CoopLoc.T("summary.victory_title")}  ·  {s.Settings.GetCompactSummary()}"
                    : $"{CoopLoc.T("summary.defeat_title")}  ·  {s.Settings.GetCompactSummary()}";
                NativeStyleProvider.CreateLabel(panel.transform, headerTitle, 16f, NativeStyleProvider.EmberGold, TextAlignmentOptions.Center, FontStyles.Bold);

                CreateStatRow(panel.transform, Color.clear,
                    CoopLoc.T("summary.col_player"),
                    CoopLoc.T("summary.col_damage"),
                    CoopLoc.T("summary.col_debuffs"),
                    CoopLoc.T("summary.col_path"),
                    CoopLoc.T("summary.col_towers"),
                    CoopLoc.T("summary.col_gifted"),
                    isHeader: true);

                int topDpsId = -1, topPathId = -1, topSupportId = -1;
                long maxDmg = 0; int maxPath = 0; int maxSupport = 0;

                foreach (var kv in s.Net.Roster)
                {
                    var st = s.Ownership.GetOrCreateStats(kv.Key);
                    if (st.DamageDealt > maxDmg) { maxDmg = st.DamageDealt; topDpsId = kv.Key; }
                    if (st.PathLengthAdded > maxPath) { maxPath = st.PathLengthAdded; topPathId = kv.Key; }
                    int sup = st.EnemiesDebuffed + st.GoldGifted;
                    if (sup > maxSupport) { maxSupport = sup; topSupportId = kv.Key; }
                }

                foreach (var kv in s.Net.Roster)
                {
                    var peer = kv.Value;
                    var st = s.Ownership.GetOrCreateStats(peer.PlayerId);
                    Color pCol = CoopConfig.GetPlayerColor(peer.ColorIndex);

                    string badge = "";
                    if (peer.PlayerId == topDpsId && maxDmg > 0) badge += " [DPS]";
                    if (peer.PlayerId == topPathId && maxPath > 0) badge += " [MAZE]";
                    if (peer.PlayerId == topSupportId && maxSupport > 0) badge += " [SUP]";

                    CreateStatRow(
                        panel.transform,
                        pCol,
                        $"{peer.Name}{badge}",
                        FormatCompact(st.DamageDealt),
                        st.EnemiesDebuffed.ToString(),
                        $"+{st.PathLengthAdded}",
                        st.TowersBuilt.ToString(),
                        $"{st.GoldGifted}g",
                        isHeader: false
                    );
                }

                CoopLogger.Info(LogCategory.UI, "Attached Co-op Teamwork Breakdown panel to end-of-run popup.");
            }
            catch (Exception ex)
            {
                CoopLogger.Warn(LogCategory.UI, $"Could not attach PostRunSummaryOverlay: {ex.Message}");
            }
        }

        private static void CreateStatRow(Transform parent, Color swatchColor, string name, string dmg, string debuffs, string path, string towers, string gifted, bool isHeader)
        {
            var row = new GameObject(isHeader ? "HeaderRow" : $"Row_{name}");
            row.transform.SetParent(parent, false);
            var hlg = row.AddComponent<HorizontalLayoutGroup>();
            hlg.spacing = 8f;
            hlg.childAlignment = TextAnchor.MiddleLeft;
            hlg.childControlWidth = true;
            hlg.childControlHeight = true;
            hlg.childForceExpandWidth = false;

            var le = row.AddComponent<LayoutElement>();
            le.minHeight = isHeader ? 22f : 24f;
            le.preferredHeight = isHeader ? 22f : 24f;

            var dot = new GameObject("Dot");
            dot.transform.SetParent(row.transform, false);
            var dle = dot.AddComponent<LayoutElement>();
            dle.minWidth = 10f; dle.preferredWidth = 10f; dle.minHeight = 10f; dle.preferredHeight = 10f;
            var dImg = dot.AddComponent<Image>();
            dImg.color = isHeader ? Color.clear : swatchColor;

            Color c = isHeader ? NativeStyleProvider.TextMuted : NativeStyleProvider.TextLight;
            FontStyles st = isHeader ? FontStyles.Bold : FontStyles.Normal;
            float sz = isHeader ? 12f : 14f;

            var nLbl = NativeStyleProvider.CreateLabel(row.transform, name, sz, isHeader ? c : swatchColor, TextAlignmentOptions.MidlineLeft, FontStyles.Bold);
            nLbl.gameObject.AddComponent<LayoutElement>().flexibleWidth = 1f;

            AddCol(row.transform, dmg, 100f, sz, c, st);
            AddCol(row.transform, debuffs, 85f, sz, c, st);
            AddCol(row.transform, path, 80f, sz, c, st);
            AddCol(row.transform, towers, 75f, sz, c, st);
            AddCol(row.transform, gifted, 85f, sz, isHeader ? c : NativeStyleProvider.EmberGold, st);
        }

        private static void AddCol(Transform parent, string text, float width, float fontSize, Color color, FontStyles style)
        {
            var tmp = NativeStyleProvider.CreateLabel(parent, text, fontSize, color, TextAlignmentOptions.MidlineRight, style);
            var le = tmp.gameObject.AddComponent<LayoutElement>();
            le.minWidth = width;
            le.preferredWidth = width;
        }

        private static string FormatCompact(long value)
        {
            if (value >= 1_000_000) return $"{value / 1_000_000f:0.0}M";
            if (value >= 1_000) return $"{value / 1_000f:0.0}K";
            return value.ToString();
        }
    }
}
