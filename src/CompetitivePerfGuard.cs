using System;
using System.Reflection;
using HarmonyLib;
using UnityEngine;

namespace BigAmbitionsMP
{
    /// <summary>
    /// Keeps the read-only competitive pages cheap on the HOST.
    ///
    /// Both pages can derive a full rivals snapshot. On a large city that means a
    /// registration/rival walk, so doing it every rendered UI second is pointless.
    /// Price sync itself runs on a 5s cadence; a ~2.5s UI cadence remains more than
    /// responsive enough while cutting the worst host-side scan rate by >50%.
    /// Clients mostly read already-received caches, but Market Intel also scans
    /// registrations, so the same cadence keeps both machines predictable.
    /// </summary>
    internal static class CompetitivePerfGuard
    {
        private const float MinRefreshSeconds = 2.5f;
        private static float _nextDashboard;
        private static float _nextIntel;
        private static bool _loggedDashboard;
        private static bool _loggedIntel;

        internal static bool AllowDashboard(float now)
        {
            if (now + 0.001f < _nextDashboard) return false;
            _nextDashboard = now + MinRefreshSeconds;
            if (!_loggedDashboard)
            {
                _loggedDashboard = true;
                Plugin.Logger.LogInfo($"[CompetitivePerf] Dashboard heavy refresh capped at ~{MinRefreshSeconds:0.0}s cadence while open.");
            }
            return true;
        }

        internal static bool AllowIntel(float now)
        {
            if (now + 0.001f < _nextIntel) return false;
            _nextIntel = now + MinRefreshSeconds;
            if (!_loggedIntel)
            {
                _loggedIntel = true;
                Plugin.Logger.LogInfo($"[CompetitivePerf] Market Intel scan capped at ~{MinRefreshSeconds:0.0}s cadence while open.");
            }
            return true;
        }

        internal static void ResetDashboard() => _nextDashboard = 0f;
        internal static void ResetIntel() => _nextIntel = 0f;
    }

    [HarmonyPatch]
    public static class Patch_CompetitiveDashboard_RefreshThrottle
    {
        static MethodBase TargetMethod()
            => AccessTools.Method(typeof(CompetitiveDashboard), "Refresh", new[] { typeof(float) });

        static bool Prefix(float now) => CompetitivePerfGuard.AllowDashboard(now);
    }

    [HarmonyPatch]
    public static class Patch_CompetitiveMarketIntel_RefreshThrottle
    {
        static MethodBase TargetMethod()
            => AccessTools.Method(typeof(CompetitiveMarketIntel), "Refresh", new[] { typeof(float) });

        static bool Prefix(float now) => CompetitivePerfGuard.AllowIntel(now);
    }

    // Opening either page should refresh immediately rather than inherit a stale
    // throttle deadline from its previous open.
    [HarmonyPatch]
    public static class Patch_CompetitiveDashboard_ShowPerfReset
    {
        static MethodBase TargetMethod()
            => AccessTools.Method(typeof(CompetitiveDashboard), "Show");

        static void Prefix() => CompetitivePerfGuard.ResetDashboard();
    }

    [HarmonyPatch]
    public static class Patch_CompetitiveMarketIntel_ShowPerfReset
    {
        static MethodBase TargetMethod()
            => AccessTools.Method(typeof(CompetitiveMarketIntel), "Show");

        static void Prefix() => CompetitivePerfGuard.ResetIntel();
    }
}