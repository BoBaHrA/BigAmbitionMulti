using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using HarmonyLib;
using TMPro;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.UI;

namespace BigAmbitionsMP
{
    /// <summary>
    /// Visual-only competitive UI skin.
    ///
    /// The first real 1920x1080 smoke test proved the data/pages work, but also
    /// showed that the original dark dashboard looked like a separate admin UI.
    /// This layer intentionally changes presentation only: it samples the already
    /// working Business page at runtime and reuses its tab/panel visual language.
    /// Networking, saves, economy authority and competitive calculations are not
    /// touched here.
    /// </summary>
    internal static class CompetitiveUiPolish
    {
        private static bool _loggedDashboard;
        private static bool _loggedIntel;
        private static bool _loggedNativeSkin;

        private static readonly Color FallbackPanel = new(0.20f, 0.23f, 0.29f, 0.78f);
        private static readonly Color FallbackSelected = new(0.16f, 0.43f, 0.72f, 1f);
        private static readonly Color FallbackIdle = new(0.16f, 0.18f, 0.21f, 1f);
        private static readonly Color TextWhite = new(0.96f, 0.97f, 0.99f, 1f);
        private static readonly Color TextMuted = new(0.78f, 0.81f, 0.86f, 1f);

        internal static void ApplyDashboard()
        {
            try
            {
                var root = FindRoot("BAMP_CompetitionApp");
                if (root == null) return;
                MirrorBusinessRoot(root);
                HideStandaloneIntelTopButton();
                ApplyNativeSkin(root, isIntel: false);

                // No full-screen black rectangle: let the native blurred FullMenu
                // background show through, just like the normal Business page.
                SetActive(root, "Background", false);
                SetActive(root, "Title", false);

                EnsureInternalTabs(root, intelSelected: false);

                SetRect(root, "Subtitle", -820f, 246f, 1640f, 32f);
                SetTextStyle(root, "Subtitle", 15f, FontStyles.Normal, TextMuted);

                // Head-to-head card.
                SetRect(root, "DuelPanel", 0f, 112f, 1720f, 190f);
                SetRect(root, "DuelHeader", -810f, 181f, 1600f, 34f);
                SetRect(root, "Duel", -810f, 140f, 1600f, 120f);
                SetTextStyle(root, "DuelHeader", 17f, FontStyles.Bold, TextWhite);
                SetTextStyle(root, "Duel", 16f, FontStyles.Normal, TextWhite);

                // Two equal native-style cards.
                SetRect(root, "RankingPanel", -445f, -150f, 830f, 300f);
                SetRect(root, "MarketsPanel", 445f, -150f, 830f, 300f);
                SetRect(root, "RankingHeader", -810f, -26f, 750f, 34f);
                SetRect(root, "Ranking", -810f, -68f, 750f, 220f);
                SetRect(root, "MarketsHeader", 80f, -26f, 750f, 34f);
                SetRect(root, "Markets", 80f, -68f, 750f, 220f);
                SetTextStyle(root, "RankingHeader", 17f, FontStyles.Bold, TextWhite);
                SetTextStyle(root, "MarketsHeader", 17f, FontStyles.Bold, TextWhite);
                SetTextStyle(root, "Ranking", 15f, FontStyles.Normal, TextWhite);
                SetTextStyle(root, "Markets", 15f, FontStyles.Normal, TextWhite);

                SetRect(root, "Footnote", -810f, -335f, 1620f, 24f);
                SetTextStyle(root, "Footnote", 11.5f, FontStyles.Italic, TextMuted);

                if (!_loggedDashboard)
                {
                    _loggedDashboard = true;
                    Plugin.Logger.LogInfo("[CompetitiveUI] Dashboard switched to native Business-style layout.");
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
                HideStandaloneIntelTopButton();
                ApplyNativeSkin(root, isIntel: true);

                SetActive(root, "Background", false);
                SetActive(root, "Title", false);

                EnsureInternalTabs(root, intelSelected: true);

                SetRect(root, "Summary", -820f, 246f, 1640f, 32f);
                SetTextStyle(root, "Summary", 15f, FontStyles.Normal, TextMuted);

                SetRect(root, "WarsPanel", -445f, 84f, 830f, 280f);
                SetRect(root, "BattlesPanel", 445f, 84f, 830f, 280f);
                SetRect(root, "WarsHeader", -810f, 193f, 750f, 34f);
                SetRect(root, "Wars", -810f, 151f, 750f, 190f);
                SetRect(root, "BattlesHeader", 80f, 193f, 750f, 34f);
                SetRect(root, "Battles", 80f, 151f, 750f, 190f);
                SetTextStyle(root, "WarsHeader", 17f, FontStyles.Bold, TextWhite);
                SetTextStyle(root, "BattlesHeader", 17f, FontStyles.Bold, TextWhite);
                SetTextStyle(root, "Wars", 15f, FontStyles.Normal, TextWhite);
                SetTextStyle(root, "Battles", 15f, FontStyles.Normal, TextWhite);

                SetRect(root, "EventsPanel", 0f, -183f, 1720f, 220f);
                SetRect(root, "EventsHeader", -810f, -92f, 1620f, 34f);
                SetRect(root, "Events", -810f, -133f, 1620f, 135f);
                SetTextStyle(root, "EventsHeader", 17f, FontStyles.Bold, TextWhite);
                SetTextStyle(root, "Events", 15f, FontStyles.Normal, TextWhite);

                SetRect(root, "Diagnostics", -810f, -335f, 1620f, 24f);
                SetTextStyle(root, "Diagnostics", 11.5f, FontStyles.Normal, TextMuted);

                if (!_loggedIntel)
                {
                    _loggedIntel = true;
                    Plugin.Logger.LogInfo("[CompetitiveUI] Market Intel switched to native Business-style layout.");
                }
            }
            catch (Exception ex)
            {
                Plugin.Logger.LogWarning($"[CompetitiveUI] Market Intel polish: {ex.Message}");
            }
        }

        /// <summary>
        /// Market Intel is now a sub-tab of Competition rather than a second
        /// top-row app.  We keep the underlying button object alive because the
        /// existing MarketIntel class owns it, but hide it from the row.
        /// </summary>
        private static void HideStandaloneIntelTopButton()
        {
            try
            {
                var row = GetTopButtonRow();
                var intel = row?.Find("BAMP_MarketIntel");
                if (intel != null && intel.gameObject.activeSelf) intel.gameObject.SetActive(false);
            }
            catch { }
        }

        private static void ApplyNativeSkin(RectTransform root, bool isIntel)
        {
            var panelPrototype = FindBusinessPanelPrototype();
            string[] panelNames = isIntel
                ? new[] { "WarsPanel", "BattlesPanel", "EventsPanel" }
                : new[] { "DuelPanel", "RankingPanel", "MarketsPanel" };

            foreach (string name in panelNames)
            {
                var img = root.Find(name)?.GetComponent<Image>();
                if (img == null) continue;

                if (panelPrototype != null)
                {
                    img.sprite = panelPrototype.sprite;
                    img.type = panelPrototype.type;
                    img.material = panelPrototype.material;
                    img.pixelsPerUnitMultiplier = panelPrototype.pixelsPerUnitMultiplier;
                    img.color = panelPrototype.color;
                    img.raycastTarget = false;
                }
                else
                {
                    img.color = FallbackPanel;
                    img.raycastTarget = false;
                }
            }

            if (!_loggedNativeSkin && panelPrototype != null)
            {
                _loggedNativeSkin = true;
                Plugin.Logger.LogInfo("[CompetitiveUI] Native panel skin sampled from Business/Incoming Offers.");
            }
        }

        private static Image? FindBusinessPanelPrototype()
        {
            var business = MPHubNativePage.ContentRoot;
            if (business == null) return null;

            foreach (var text in business.GetComponentsInChildren<TextMeshProUGUI>(true))
            {
                string value = StripMarkup(text.text).Trim();
                if (!value.Equals("INCOMING OFFERS", StringComparison.OrdinalIgnoreCase) &&
                    !value.Equals("YOUR PENDING OFFERS", StringComparison.OrdinalIgnoreCase))
                    continue;

                Transform? t = text.transform;
                for (int i = 0; i < 6 && t != null && t != business; i++, t = t.parent)
                {
                    var image = t.GetComponent<Image>();
                    if (image != null && image.rectTransform.rect.width > 300f) return image;
                }
            }
            return null;
        }

        private sealed class TabPrototype
        {
            public GameObject Object = null!;
            public Image? Image;
            public float Score;
        }

        private static void EnsureInternalTabs(RectTransform root, bool intelSelected)
        {
            var oldOverview = root.Find("CompetitiveTab_Overview");
            var oldIntel = root.Find("CompetitiveTab_Intel");

            if (oldOverview == null)
            {
                var selectedProto = FindTabPrototype(selected: true);
                var idleProto = FindTabPrototype(selected: false);
                var overview = CloneTab(root, "CompetitiveTab_Overview", "Overview",
                    intelSelected ? idleProto : selectedProto,
                    new Vector2(-690f, 294f), selected: !intelSelected,
                    () => SwitchPage(typeof(CompetitiveDashboard)));
                oldOverview = overview?.transform;
            }

            if (oldIntel == null)
            {
                var selectedProto = FindTabPrototype(selected: true);
                var idleProto = FindTabPrototype(selected: false);
                var intel = CloneTab(root, "CompetitiveTab_Intel", "Market Intel",
                    intelSelected ? selectedProto : idleProto,
                    new Vector2(-420f, 294f), selected: intelSelected,
                    () => SwitchPage(typeof(CompetitiveMarketIntel)));
                oldIntel = intel?.transform;
            }

            // If resolution/UI scale changed, keep the tabs pinned to the same
            // native 1920x875 logical coordinates as the Business content root.
            if (oldOverview is RectTransform rtOverview)
            {
                rtOverview.anchoredPosition = new Vector2(-690f, 294f);
                rtOverview.sizeDelta = new Vector2(245f, 52f);
            }
            if (oldIntel is RectTransform rtIntel)
            {
                rtIntel.anchoredPosition = new Vector2(-420f, 294f);
                rtIntel.sizeDelta = new Vector2(245f, 52f);
            }
        }

        private static TabPrototype? FindTabPrototype(bool selected)
        {
            var business = MPHubNativePage.ContentRoot;
            if (business == null) return null;

            var list = new List<TabPrototype>();
            foreach (var text in business.GetComponentsInChildren<TextMeshProUGUI>(true))
            {
                string value = StripMarkup(text.text).Trim();
                if (!value.Equals("Transfers", StringComparison.OrdinalIgnoreCase) &&
                    !value.Equals("Loans", StringComparison.OrdinalIgnoreCase) &&
                    !value.Equals("Permissions", StringComparison.OrdinalIgnoreCase))
                    continue;

                Transform? t = text.transform;
                Button? button = null;
                for (int i = 0; i < 4 && t != null && t != business; i++, t = t.parent)
                {
                    button = t.GetComponent<Button>();
                    if (button != null) break;
                }
                if (button == null) continue;

                var image = button.targetGraphic as Image ?? button.GetComponent<Image>();
                Color c = image != null ? image.color : Color.black;
                // Selected Business tabs are the brighter saturated-blue member;
                // idle tabs are the darker charcoal members.
                float score = c.b * 1.15f + c.g * 0.75f + c.r * 0.15f +
                              (c.r + c.g + c.b) * 0.20f;
                list.Add(new TabPrototype { Object = button.gameObject, Image = image, Score = score });
            }

            if (list.Count == 0) return null;
            return selected
                ? list.OrderByDescending(x => x.Score).First()
                : list.OrderBy(x => x.Score).First();
        }

        private static GameObject? CloneTab(RectTransform root, string name, string label,
            TabPrototype? prototype, Vector2 pos, bool selected, UnityAction action)
        {
            GameObject go;
            Button button;
            TextMeshProUGUI text;

            if (prototype != null)
            {
                go = UnityEngine.Object.Instantiate(prototype.Object, root);
                go.name = name;

                // Disable any localization inherited from a cloned Business tab.
                foreach (var c in go.GetComponentsInChildren(typeof(Component), true))
                {
                    if (c is Behaviour b && b.GetType().Name.Contains("Localization")) b.enabled = false;
                }

                button = go.GetComponent<Button>();
                text = go.GetComponentsInChildren<TextMeshProUGUI>(true).FirstOrDefault()!;
                if (text == null)
                {
                    text = go.AddComponent<TextMeshProUGUI>();
                    text.alignment = TextAlignmentOptions.Center;
                }
            }
            else
            {
                go = new GameObject(name);
                go.transform.SetParent(root, false);
                var image = go.AddComponent<Image>();
                image.color = selected ? FallbackSelected : FallbackIdle;
                button = go.AddComponent<Button>();
                button.targetGraphic = image;

                var labelGo = new GameObject("Label");
                labelGo.transform.SetParent(go.transform, false);
                var lrt = labelGo.AddComponent<RectTransform>();
                lrt.anchorMin = Vector2.zero;
                lrt.anchorMax = Vector2.one;
                lrt.offsetMin = Vector2.zero;
                lrt.offsetMax = Vector2.zero;
                text = labelGo.AddComponent<TextMeshProUGUI>();
                text.alignment = TextAlignmentOptions.Center;
                text.color = Color.white;
                text.fontSize = 15f;
                text.fontStyle = FontStyles.Bold;
            }

            var rt = go.GetComponent<RectTransform>();
            if (rt == null) rt = go.AddComponent<RectTransform>();
            rt.anchorMin = rt.anchorMax = rt.pivot = new Vector2(0.5f, 0.5f);
            rt.anchoredPosition = pos;
            rt.sizeDelta = new Vector2(245f, 52f);
            rt.localScale = Vector3.one;

            text.text = label;
            text.color = Color.white;
            text.fontSize = 15f;
            text.fontStyle = FontStyles.Bold;
            text.alignment = TextAlignmentOptions.Center;

            // Preserve native hover/pressed transitions from the cloned button,
            // but own its click action completely.
            button.interactable = true;
            button.enabled = true;
            button.onClick = new Button.ButtonClickedEvent();
            button.onClick.AddListener(action);
            go.SetActive(true);
            return go;
        }

        private static void SwitchPage(Type type)
        {
            try
            {
                var show = AccessTools.Method(type, "Show");
                show?.Invoke(null, null);
            }
            catch (Exception ex)
            {
                Plugin.Logger.LogWarning($"[CompetitiveUI] tab switch: {ex.GetBaseException().Message}");
            }
        }

        internal static void ForceCompetitionShell()
        {
            try
            {
                var apps = MPHubNativePage.ContentRoot?.parent?.parent;
                var dash = apps?.Find("BAMP_CompetitionApp")?.gameObject;
                var intel = apps?.Find("BAMP_MarketIntelApp")?.gameObject;
                bool active = (dash != null && dash.activeInHierarchy) || (intel != null && intel.activeInHierarchy);
                if (!active) return;

                HideStandaloneIntelTopButton();
                var row = GetTopButtonRow();
                if (row != null)
                {
                    for (int i = 0; i < row.childCount; i++)
                    {
                        var child = row.GetChild(i);
                        var dot = child.Find("SelectedIcon");
                        if (dot != null)
                            dot.gameObject.SetActive(child.name == "BAMP_Competition");
                    }
                }

                var canvas = MPHubNativePage.ContentRoot?.parent?.parent?.parent;
                var appName = canvas?.Find("Topbar/AppName");
                var lbl = appName?.GetComponent<TextMeshProUGUI>();
                if (lbl != null) lbl.text = "Competition";
            }
            catch { }
        }

        private static Transform? GetTopButtonRow()
        {
            var canvas = MPHubNativePage.ContentRoot?.parent?.parent?.parent;
            return canvas?.Find("Topbar/AppButtons");
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
            rt.anchorMin = rt.anchorMax = rt.pivot = new Vector2(0.5f, 0.5f);
            rt.anchoredPosition = new Vector2(x, y);
            rt.sizeDelta = new Vector2(width, height);
        }

        private static void SetActive(RectTransform root, string name, bool active)
        {
            var t = root.Find(name);
            if (t != null && t.gameObject.activeSelf != active) t.gameObject.SetActive(active);
        }

        private static void SetTextStyle(RectTransform root, string name, float size, FontStyles style, Color color)
        {
            var text = root.Find(name)?.GetComponent<TextMeshProUGUI>();
            if (text == null) return;
            text.fontSize = size;
            text.fontStyle = style;
            text.color = color;
        }

        private static string StripMarkup(string value)
        {
            if (string.IsNullOrEmpty(value)) return string.Empty;
            try
            {
                return System.Text.RegularExpressions.Regex.Replace(value, "<.*?>", string.Empty);
            }
            catch { return value; }
        }
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
        static void Postfix()
        {
            CompetitiveUiPolish.ApplyDashboard();
            CompetitiveUiPolish.ForceCompetitionShell();
        }
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
        static void Postfix()
        {
            CompetitiveUiPolish.ApplyIntel();
            CompetitiveUiPolish.ForceCompetitionShell();
        }
    }

    // MarketIntel owns a private title holder which runs every Tick. Keep the
    // shell title as Competition because Intel is now an internal sub-tab.
    [HarmonyPatch]
    public static class Patch_CompetitiveIntel_CompetitionShellTitle
    {
        static MethodBase TargetMethod()
            => AccessTools.Method(typeof(CompetitiveMarketIntel), "HoldNativeTitle");
        static void Postfix() => CompetitiveUiPolish.ForceCompetitionShell();
    }
}
