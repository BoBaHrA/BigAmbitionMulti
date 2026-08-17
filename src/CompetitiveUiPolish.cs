using System;
using System.Reflection;
using HarmonyLib;
using TMPro;
using UnityEngine;

namespace BigAmbitionsMP
{
    /// <summary>
    /// Visual-only corrections from the first real 1920x1080 smoke test.
    ///
    /// The competitive pages deliberately stay independent of the MP/economy
    /// logic.  This layer only normalises RectTransforms after the pages have
    /// been injected so their geometry follows the proven Business page root
    /// instead of relying on duplicated scale assumptions.
    /// </summary>
    internal static class CompetitiveUiPolish
    {
        private static bool _loggedDashboard;
        private static bool _loggedIntel;

        internal static void ApplyDashboard()
        {
            try
            {
                var root = FindRoot("BAMP_CompetitionApp");
                if (root == null) return;
                MirrorBusinessRoot(root);

                // Common page frame / headings.
                SetRect(root, "Background", 0f, 0f, 1840f, 790f);
                SetRect(root, "Title", -820f, 342f, 1640f, 50f);
                SetRect(root, "Subtitle", -820f, 297f, 1640f, 32f);

                // Head-to-head: leave breathing room under the subtitle.
                SetRect(root, "DuelPanel", 0f, 164f, 1720f, 168f);
                SetRect(root, "DuelHeader", -820f, 226f, 1640f, 32f);
                SetRect(root, "Duel", -820f, 190f, 1640f, 104f);

                // First smoke test exposed intentionally rough v1 geometry:
                // 900px vs 820px columns.  Make both halves exactly symmetric.
                SetRect(root, "RankingPanel", -445f, -123f, 830f, 410f);
                SetRect(root, "MarketsPanel", 445f, -123f, 830f, 410f);

                SetRect(root, "RankingHeader", -820f, 48f, 750f, 36f);
                SetRect(root, "Ranking", -820f, 4f, 750f, 326f);
                SetRect(root, "MarketsHeader", 70f, 48f, 750f, 36f);
                SetRect(root, "Markets", 70f, 4f, 750f, 326f);

                // Was partly clipped at 1080p in the first field screenshot.
                SetRect(root, "Footnote", -820f, -354f, 1640f, 26f);
                var foot = FindText(root, "Footnote");
                if (foot != null) foot.fontSize = 12f;

                if (!_loggedDashboard)
                {
                    _loggedDashboard = true;
                    Plugin.Logger.LogInfo("[CompetitiveUI] Dashboard layout normalised after visual smoke test.");
                }
            }
            catch (Exception ex)
            {
                Plugin.Logger.LogWarning($"[CompetitiveUI] Dashboard polish: {ex.Message}");
            }
        }

        internal static void ApplyIntel()
        {
            try
            {
                var root = FindRoot("BAMP_MarketIntelApp");
                if (root == null) return;
                MirrorBusinessRoot(root);

                SetRect(root, "Background", 0f, 0f, 1840f, 790f);
                SetRect(root, "Title", -820f, 342f, 1640f, 50f);
                SetRect(root, "Summary", -820f, 297f, 1640f, 32f);

                // Symmetric top cards: v1 used 880px / 820px.
                SetRect(root, "WarsPanel", -445f, 100f, 830f, 320f);
                SetRect(root, "BattlesPanel", 445f, 100f, 830f, 320f);

                SetRect(root, "WarsHeader", -820f, 232f, 750f, 32f);
                SetRect(root, "Wars", -820f, 194f, 750f, 238f);
                SetRect(root, "BattlesHeader", 70f, 232f, 750f, 32f);
                SetRect(root, "Battles", 70f, 194f, 750f, 238f);

                SetRect(root, "EventsPanel", 0f, -205f, 1720f, 245f);
                SetRect(root, "EventsHeader", -820f, -104f, 1640f, 32f);
                SetRect(root, "Events", -820f, -142f, 1640f, 154f);
                SetRect(root, "Diagnostics", -820f, -354f, 1640f, 26f);

                if (!_loggedIntel)
                {
                    _loggedIntel = true;
                    Plugin.Logger.LogInfo("[CompetitiveUI] Market Intel layout normalised after visual smoke test.");
                }
            }
            catch (Exception ex)
            {
                Plugin.Logger.LogWarning($"[CompetitiveUI] Market Intel polish: {ex.Message}");
            }
        }

        private static RectTransform? FindRoot(string pageName)
        {
            var businessRoot = MPHubNativePage.ContentRoot;
            var apps = businessRoot?.parent?.parent;
            var page = apps?.Find(pageName);
            return page?.Find("Root")?.GetComponent<RectTransform>();
        }

        private static void MirrorBusinessRoot(RectTransform target)
        {
            if (MPHubNativePage.ContentRoot is not RectTransform source) return;

            target.anchorMin = source.anchorMin;
            target.anchorMax = source.anchorMax;
            target.pivot = source.pivot;
            target.sizeDelta = source.sizeDelta;
            target.anchoredPosition = source.anchoredPosition;
            target.localScale = source.localScale;
            target.localRotation = source.localRotation;
        }

        private static void SetRect(RectTransform root, string name, float x, float y, float width, float height)
        {
            var rt = root.Find(name)?.GetComponent<RectTransform>();
            if (rt == null) return;
            rt.anchoredPosition = new Vector2(x, y);
            rt.sizeDelta = new Vector2(width, height);
        }

        private static TextMeshProUGUI? FindText(RectTransform root, string name)
            => root.Find(name)?.GetComponent<TextMeshProUGUI>();
    }

    // Re-apply after construction and each open. Re-applying is intentionally
    // idempotent and also makes UI-scale/resolution changes between opens safe.
    [HarmonyPatch]
    public static class Patch_CompetitiveDashboard_UiPolishBuild
    {
        static MethodBase TargetMethod()
            => AccessTools.Method(typeof(CompetitiveDashboard), "TryBuild");
        static void Postfix() => CompetitiveUiPolish.ApplyDashboard();
    }

    [HarmonyPatch]
    public static class Patch_CompetitiveDashboard_UiPolishShow
    {
        static MethodBase TargetMethod()
            => AccessTools.Method(typeof(CompetitiveDashboard), "Show");
        static void Postfix() => CompetitiveUiPolish.ApplyDashboard();
    }

    [HarmonyPatch]
    public static class Patch_CompetitiveIntel_UiPolishBuild
    {
        static MethodBase TargetMethod()
            => AccessTools.Method(typeof(CompetitiveMarketIntel), "TryBuild");
        static void Postfix() => CompetitiveUiPolish.ApplyIntel();
    }

    [HarmonyPatch]
    public static class Patch_CompetitiveIntel_UiPolishShow
    {
        static MethodBase TargetMethod()
            => AccessTools.Method(typeof(CompetitiveMarketIntel), "Show");
        static void Postfix() => CompetitiveUiPolish.ApplyIntel();
    }
}
