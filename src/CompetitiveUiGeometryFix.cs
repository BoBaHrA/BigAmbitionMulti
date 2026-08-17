using System.Reflection;
using HarmonyLib;
using UnityEngine;
using UnityEngine.UI;

namespace BigAmbitionsMP
{
    /// <summary>
    /// Field-smoke hotfix for the native-skin pass.
    ///
    /// CompetitiveUiPolish.SetRect originally forced every element to a centered
    /// pivot. Panels are centered, but all dashboard text is intentionally built
    /// with a top-left pivot. Re-centering those text rects moved them hundreds of
    /// logical pixels and clipped the left side of both pages at 1920x1080.
    ///
    /// This patch preserves each element's original anchor/pivot around SetRect,
    /// so the polish layer may still change position/size without changing the
    /// coordinate system the element was authored in.
    /// </summary>
    [HarmonyPatch]
    public static class Patch_CompetitiveUiPolish_PreserveRectBasis
    {
        private struct RectBasis
        {
            public RectTransform? Rect;
            public Vector2 AnchorMin;
            public Vector2 AnchorMax;
            public Vector2 Pivot;
        }

        static MethodBase TargetMethod()
            => AccessTools.Method(typeof(CompetitiveUiPolish), "SetRect");

        static void Prefix(RectTransform root, string name, out RectBasis __state)
        {
            __state = default;
            try
            {
                var rt = root.Find(name)?.GetComponent<RectTransform>();
                if (rt == null) return;
                __state.Rect = rt;
                __state.AnchorMin = rt.anchorMin;
                __state.AnchorMax = rt.anchorMax;
                __state.Pivot = rt.pivot;
            }
            catch { }
        }

        static void Postfix(RectBasis __state)
        {
            try
            {
                if (__state.Rect == null) return;
                __state.Rect.anchorMin = __state.AnchorMin;
                __state.Rect.anchorMax = __state.AnchorMax;
                __state.Rect.pivot = __state.Pivot;
            }
            catch { }
        }
    }

    /// <summary>
    /// The runtime Business-panel probe is intentionally permissive so it survives
    /// UI hierarchy changes, but the first native-skin smoke found a prototype whose
    /// source tint made cloned panel sprites effectively disappear. Keep any sampled
    /// native sprite/material/9-slice, while applying a controlled Business-like
    /// translucent slate tint so the cards remain readable on the blurred game shell.
    /// </summary>
    [HarmonyPatch]
    public static class Patch_CompetitiveUiPolish_VisibleNativeCards
    {
        private static readonly Color CardTint = new(0.24f, 0.27f, 0.33f, 0.78f);

        static MethodBase TargetMethod()
            => AccessTools.Method(typeof(CompetitiveUiPolish), "ApplyNativeSkin");

        static void Postfix(RectTransform root, bool isIntel)
        {
            try
            {
                string[] names = isIntel
                    ? new[] { "WarsPanel", "BattlesPanel", "EventsPanel" }
                    : new[] { "DuelPanel", "RankingPanel", "MarketsPanel" };

                foreach (string name in names)
                {
                    var img = root.Find(name)?.GetComponent<Image>();
                    if (img == null) continue;
                    img.color = CardTint;
                    img.raycastTarget = false;
                }
            }
            catch { }
        }
    }
}
