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

                var panel = NativeStyleProvider.CreatePanel(popupTransform, "EWCoop_TeamworkSummary", NativeStyleProvider.PanelBg, addBorder: true);
                var rt = panel.GetComponent<RectTransform>();

                // Safe-zone placement:
                // - UI_Victory_Popup buttons sit at Y_top=[915..1015], Node_Reward at [692..792], Node_Details on right [X=1015..1765]
                // - UI_Defeat_Popup buttons sit at Y_top=[866..966]
                bool hasRightDetails = false;
                if (isVictory)
                {
                    var details = popupTransform.Find("Node_Animator/Node_Details") ?? popupTransform.Find("Node_Details");
                    if (details != null && details.gameObject.activeInHierarchy)
                        hasRightDetails = true;
                }

                if (isVictory && hasRightDetails)
                {
                    // Place cleanly on the open left half beside Node_Details, well above bottom action buttons
                    rt.anchorMin = new Vector2(0f, 0.5f);
                    rt.anchorMax = new Vector2(0f, 0.5f);
                    rt.pivot = new Vector2(0f, 0.5f);
                    rt.anchoredPosition = new Vector2(44f, -10f);
                    rt.sizeDelta = new Vector2(680f, 150f);
                }
                else if (isVictory)
                {
                    // Place cleanly in the open central corridor between Image_Banner (Y_top=430) and Node_Reward (Y_top=692)
                    rt.anchorMin = new Vector2(0.5f, 0.5f);
                    rt.anchorMax = new Vector2(0.5f, 0.5f);
                    rt.pivot = new Vector2(0.5f, 0.5f);
                    rt.anchoredPosition = new Vector2(0f, -18f);
                    rt.sizeDelta = new Vector2(740f, 150f);
                }
                else
                {
                    // Defeat popup: place cleanly above Node_Button (Y_from_bottom=114..214)
                    rt.anchorMin = new Vector2(0.5f, 0f);
                    rt.anchorMax = new Vector2(0.5f, 0f);
                    rt.pivot = new Vector2(0.5f, 0f);
                    rt.anchoredPosition = new Vector2(0f, 224f);
                    rt.sizeDelta = new Vector2(740f, 150f);
                }

                var fit = panel.AddComponent<ContentSizeFitter>();
                fit.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

                var vlg = panel.AddComponent<VerticalLayoutGroup>();
                vlg.padding = new RectOffset(20, 20, 16, 16);
                vlg.spacing = 5f;
                vlg.childControlWidth = true;
                vlg.childControlHeight = true;
                vlg.childForceExpandWidth = true;
                vlg.childForceExpandHeight = false;

                string headerTitle = isVictory
                    ? $"{CoopLoc.T("summary.victory_title")}  ·  {s.Settings.GetCompactSummary()}"
                    : $"{CoopLoc.T("summary.defeat_title")}  ·  {s.Settings.GetCompactSummary()}";
                NativeStyleProvider.CreateLabel(panel.transform, headerTitle, 14.5f, NativeStyleProvider.EmberGold, TextAlignmentOptions.Center, FontStyles.Bold, autoSize: true, minFontSize: 11f, addShadow: true);

                NativeStyleProvider.CreateDivider(panel.transform, "SumDiv");

                CreateStatRow(panel.transform, Color.clear, null,
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
                        peer.AvatarHeroId,
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

        private static void CreateStatRow(Transform parent, Color swatchColor, string avatarHeroId, string name, string dmg, string debuffs, string path, string towers, string gifted, bool isHeader)
        {
            var row = isHeader
                ? new GameObject("HeaderRow")
                : NativeStyleProvider.CreatePanel(parent, $"Row_{name}", NativeStyleProvider.CardBg, addBorder: false, isSubPanel: true);
            if (isHeader) row.transform.SetParent(parent, false);

            var hlg = row.AddComponent<HorizontalLayoutGroup>();
            hlg.padding = new RectOffset(6, 6, 2, 2);
            hlg.spacing = 6f;
            hlg.childAlignment = TextAnchor.MiddleLeft;
            hlg.childControlWidth = true;
            hlg.childControlHeight = true;
            hlg.childForceExpandWidth = false;

            var le = row.AddComponent<LayoutElement>();
            le.minHeight = isHeader ? 20f : 28f;
            le.preferredHeight = isHeader ? 20f : 28f;

            if (isHeader)
            {
                var spacer = new GameObject("AvatarSpacer");
                spacer.transform.SetParent(row.transform, false);
                var sle = spacer.AddComponent<LayoutElement>();
                sle.minWidth = 24f; sle.preferredWidth = 24f;
            }
            else
            {
                NativeStyleProvider.CreateHeroAvatarWidget(row.transform, avatarHeroId, swatchColor, 24f, clickable: false);
            }

            Color c = isHeader ? NativeStyleProvider.TextMuted : NativeStyleProvider.TextLight;
            FontStyles st = isHeader ? FontStyles.Bold : FontStyles.Normal;
            float sz = isHeader ? 11f : 13f;

            var nLbl = NativeStyleProvider.CreateLabel(row.transform, name, sz, isHeader ? c : swatchColor, TextAlignmentOptions.MidlineLeft, FontStyles.Bold);
            nLbl.overflowMode = TextOverflowModes.Ellipsis;
            nLbl.gameObject.AddComponent<LayoutElement>().flexibleWidth = 1f;

            AddCol(row.transform, dmg, 84f, sz, c, st);
            AddCol(row.transform, debuffs, 74f, sz, c, st);
            AddCol(row.transform, path, 68f, sz, c, st);
            AddCol(row.transform, towers, 68f, sz, c, st);
            AddCol(row.transform, gifted, 76f, sz, isHeader ? c : NativeStyleProvider.EmberGold, st);
        }

        private static void AddCol(Transform parent, string text, float width, float fontSize, Color color, FontStyles style)
        {
            var tmp = NativeStyleProvider.CreateLabel(parent, text, fontSize, color, TextAlignmentOptions.MidlineRight, style, autoSize: true, minFontSize: 9f);
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
