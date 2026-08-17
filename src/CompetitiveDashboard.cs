using System;
using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using TMPro;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.UI;

namespace BigAmbitionsMP
{
    /// <summary>
    /// Competitive Dashboard v1.
    ///
    /// Deliberately READ-ONLY: this feature does not add protocol messages, mutate
    /// saves, or become an economy authority. It consumes the already-existing
    /// rivals-stat channel that both sides use today. That keeps the first
    /// competitive layer isolated from the synchronization core.
    ///
    /// v1 "market share" is explicitly an INCOME SHARE proxy because the current
    /// wire format exposes weekly business income, not gross units/revenue. A later
    /// protocol version can replace the numerator without changing this UI shell.
    /// </summary>
    public static class CompetitiveDashboard
    {
        private static GameObject? _button;
        private static GameObject? _selectedIcon;
        private static GameObject? _page;
        private static TextMeshProUGUI? _title;
        private static TextMeshProUGUI? _subtitle;
        private static TextMeshProUGUI? _ranking;
        private static TextMeshProUGUI? _markets;
        private static TMP_FontAsset? _font;
        private static float _nextRefreshAt;
        private static float _nextRequestAt;
        private static bool _building;

        private static readonly Color Bg = new(0.075f, 0.075f, 0.09f, 0.98f);
        private static readonly Color Panel = new(0.11f, 0.11f, 0.135f, 0.98f);
        private static readonly Color Accent = new(0.20f, 0.52f, 0.90f, 1f);
        private static readonly Color Muted = new(0.68f, 0.70f, 0.76f, 1f);
        private static readonly Color White = Color.white;
        private static readonly Color Gold = new(1f, 0.82f, 0.30f, 1f);

        public static bool PageActive => _page != null && _page.activeInHierarchy;

        public static void Tick()
        {
            try
            {
                if (!MPHubNativePage.Ready || MPHubNativePage.ContentRoot == null)
                {
                    if (_page != null || _button != null) Reset();
                    return;
                }

                if (_page == null || _button == null) TryBuild();
                if (!PageActive) return;

                HoldNativeTitle();

                float now = Time.unscaledTime;
                if (MPClient.IsConnected && now >= _nextRequestAt)
                {
                    _nextRequestAt = now + 5f;
                    MPClient.SendRivalsStatsRequest();
                }

                if (now >= _nextRefreshAt)
                {
                    _nextRefreshAt = now + 1f;
                    Refresh();
                }
            }
            catch (Exception ex)
            {
                Plugin.Logger.LogWarning($"[Competitive] Tick: {ex.Message}");
            }
        }

        public static void Reset()
        {
            try { if (_button != null) UnityEngine.Object.Destroy(_button); } catch { }
            try { if (_page != null) UnityEngine.Object.Destroy(_page); } catch { }
            _button = null;
            _selectedIcon = null;
            _page = null;
            _title = null;
            _subtitle = null;
            _ranking = null;
            _markets = null;
            _font = null;
            _nextRefreshAt = 0f;
            _nextRequestAt = 0f;
            _building = false;
        }

        /// <summary>Called after the normal Business page is shown.</summary>
        public static void OnBusinessShown()
        {
            try
            {
                if (_page != null && _page.activeSelf) _page.SetActive(false);
                if (_selectedIcon != null) _selectedIcon.SetActive(false);
            }
            catch { }
        }

        /// <summary>Called when MPHubNativePage hides for a native app.</summary>
        public static void OnNativePageShown()
        {
            try
            {
                if (_page != null && _page.activeSelf) _page.SetActive(false);
                if (_selectedIcon != null) _selectedIcon.SetActive(false);
            }
            catch { }
        }

        private static void TryBuild()
        {
            if (_building) return;
            _building = true;
            try
            {
                var content = MPHubNativePage.ContentRoot;
                if (content == null) return;

                // ContentRoot -> BAMP_BusinessApp -> AppsContainer -> Canvas.
                var businessPage = content.parent;
                var apps = businessPage?.parent;
                var canvas = apps?.parent;
                var row = canvas?.Find("Topbar/AppButtons");
                var sourceButton = row?.Find("BAMP_Business");
                if (businessPage == null || apps == null || canvas == null || row == null || sourceButton == null)
                    return;

                // Reuse the already-normalized BAMP button rather than cloning a
                // native FullMenuAppButton (which would bring native behavior back).
                _button = UnityEngine.Object.Instantiate(sourceButton.gameObject, row);
                _button.name = "BAMP_Competition";

                var titleT = _button.transform.Find("Title");
                var titleLbl = titleT != null ? titleT.GetComponent<TextMeshProUGUI>() : null;
                if (titleLbl != null)
                {
                    titleLbl.text = "Competition";
                    _font = titleLbl.font;
                }

                _selectedIcon = _button.transform.Find("SelectedIcon")?.gameObject;
                _selectedIcon?.SetActive(false);

                var btn = _button.GetComponent<Button>();
                if (btn != null)
                {
                    btn.interactable = true;
                    btn.enabled = true;
                    btn.onClick = new Button.ButtonClickedEvent();
                    btn.onClick.AddListener((UnityAction)Show);
                }
                _button.SetActive(true);

                _page = new GameObject("BAMP_CompetitionApp");
                _page.transform.SetParent(apps, false);
                var pageRt = _page.AddComponent<RectTransform>();
                pageRt.anchorMin = Vector2.zero;
                pageRt.anchorMax = Vector2.one;
                pageRt.offsetMin = Vector2.zero;
                pageRt.offsetMax = Vector2.zero;
                _page.AddComponent<CanvasGroup>();

                var root = new GameObject("Root");
                root.transform.SetParent(_page.transform, false);
                var rootRt = root.AddComponent<RectTransform>();
                rootRt.anchorMin = rootRt.anchorMax = rootRt.pivot = new Vector2(0.5f, 0.5f);
                rootRt.sizeDelta = new Vector2(1920f, 875f);
                rootRt.localScale = new Vector3(2f, 2f, 1f);

                AddPanel(root.transform, "Background", Vector2.zero, new Vector2(1840f, 805f), Bg);
                _title = AddText(root.transform, "Title", "COMPETITIVE DASHBOARD", new Vector2(-860f, 348f),
                    new Vector2(1720f, 55f), 28, FontStyles.Bold, TextAlignmentOptions.Left, White);
                _subtitle = AddText(root.transform, "Subtitle", "Live player-vs-player economy overview", new Vector2(-860f, 302f),
                    new Vector2(1720f, 34f), 15, FontStyles.Normal, TextAlignmentOptions.Left, Muted);

                AddPanel(root.transform, "RankingPanel", new Vector2(-442f, -32f), new Vector2(900f, 625f), Panel);
                AddText(root.transform, "RankingHeader", "COMPANY RANKING", new Vector2(-842f, 242f),
                    new Vector2(800f, 38f), 19, FontStyles.Bold, TextAlignmentOptions.Left, Accent);
                _ranking = AddText(root.transform, "Ranking", "Waiting for multiplayer statistics…", new Vector2(-842f, 190f),
                    new Vector2(800f, 510f), 17, FontStyles.Normal, TextAlignmentOptions.TopLeft, White);

                AddPanel(root.transform, "MarketsPanel", new Vector2(442f, -32f), new Vector2(820f, 625f), Panel);
                AddText(root.transform, "MarketsHeader", "MARKET LEADERS", new Vector2(72f, 242f),
                    new Vector2(720f, 38f), 19, FontStyles.Bold, TextAlignmentOptions.Left, Accent);
                _markets = AddText(root.transform, "Markets", "Waiting for business breakdown…", new Vector2(72f, 190f),
                    new Vector2(720f, 510f), 16, FontStyles.Normal, TextAlignmentOptions.TopLeft, White);

                AddText(root.transform, "Footnote",
                    "v1 uses weekly income as the competitive-share proxy. No new network protocol or save data is introduced.",
                    new Vector2(-860f, -382f), new Vector2(1720f, 28f), 13, FontStyles.Italic,
                    TextAlignmentOptions.Left, Muted);

                _page.SetActive(false);
                Plugin.Logger.LogInfo("[Competitive] Dashboard v1 injected (read-only, existing rivals-stat channel).");
            }
            catch (Exception ex)
            {
                Plugin.Logger.LogWarning($"[Competitive] build: {ex.Message}");
                Reset();
            }
            finally { _building = false; }
        }

        private static void Show()
        {
            try
            {
                if (_page == null || MPHubNativePage.ContentRoot == null) return;

                // Reuse Business' proven path: this hides native pages + selected
                // dots. Our ShowPage postfix hides us first; we activate right after.
                MPHubNativePage.ShowPage();
                var businessPage = MPHubNativePage.ContentRoot.parent?.gameObject;
                businessPage?.SetActive(false);
                _page.SetActive(true);
                SetSelectedDots();
                HoldNativeTitle();
                _nextRefreshAt = 0f;
                _nextRequestAt = 0f;
                Refresh();
                if (MPClient.IsConnected) MPClient.SendRivalsStatsRequest();
            }
            catch (Exception ex) { Plugin.Logger.LogWarning($"[Competitive] show: {ex.Message}"); }
        }

        private static void HoldNativeTitle()
        {
            try
            {
                var canvas = MPHubNativePage.ContentRoot?.parent?.parent?.parent;
                var appName = canvas?.Find("Topbar/AppName");
                if (appName == null) return;
                foreach (var c in appName.GetComponents(typeof(Component)))
                {
                    var b = c as Behaviour;
                    if (b != null && b.GetType().Name.Contains("Localization") && b.enabled) b.enabled = false;
                }
                var lbl = appName.GetComponent<TextMeshProUGUI>();
                if (lbl != null && lbl.text != "Competition") lbl.text = "Competition";
            }
            catch { }
        }

        private static void SetSelectedDots()
        {
            try
            {
                var canvas = MPHubNativePage.ContentRoot?.parent?.parent?.parent;
                var row = canvas?.Find("Topbar/AppButtons");
                if (row == null) return;
                for (int i = 0; i < row.childCount; i++)
                {
                    var dot = row.GetChild(i).Find("SelectedIcon");
                    if (dot != null) dot.gameObject.SetActive(row.GetChild(i).gameObject == _button);
                }
            }
            catch { }
        }

        private static void Refresh()
        {
            if (_ranking == null || _markets == null || _subtitle == null) return;

            var players = CollectPlayerStats();
            if (players.Count == 0)
            {
                _subtitle.text = "Start or join a multiplayer world to compare companies.";
                _ranking.text = "No player statistics available yet.";
                _markets.text = "No player businesses available yet.";
                return;
            }

            players = players
                .GroupBy(p => p.Id)
                .Select(g => g.OrderByDescending(x => Math.Abs(x.WeeklyIncome)).First())
                .OrderByDescending(p => p.WeeklyIncome)
                .ToList();

            float positiveTotal = players.Sum(p => Math.Max(0f, p.WeeklyIncome));
            _subtitle.text = $"{players.Count} company{(players.Count == 1 ? "" : "ies")} • "
                           + $"combined weekly income {Money(players.Sum(p => p.WeeklyIncome))} • live comparison";

            var rank = new System.Text.StringBuilder();
            rank.AppendLine("<color=#AEB5C3>#   COMPANY                         WEEKLY       BUS.    INCOME SHARE</color>");
            rank.AppendLine("────────────────────────────────────────────────────────────────────");
            for (int i = 0; i < players.Count && i < 10; i++)
            {
                var p = players[i];
                float share = positiveTotal > 0f ? Math.Max(0f, p.WeeklyIncome) / positiveTotal * 100f : 0f;
                string medal = i == 0 ? "<color=#FFD24C>1</color>" : (i + 1).ToString();
                string name = SafeName(p.Name, p.Id, 29);
                rank.Append(medal.PadLeft(2)).Append("  ")
                    .Append(name.PadRight(30)).Append(" ")
                    .Append(Money(p.WeeklyIncome).PadLeft(12)).Append("   ")
                    .Append(p.OwnedBusinessesCount.ToString().PadLeft(3)).Append("     ")
                    .Append(share.ToString("0.0").PadLeft(5)).AppendLine("%");
            }
            _ranking.text = rank.ToString();

            var perType = BuildTypeLeaders(players);
            if (perType.Count == 0)
            {
                _markets.text = "No per-business income has been reported yet.\n\n"
                              + "This fills automatically as player businesses begin trading.";
                return;
            }

            var markets = new System.Text.StringBuilder();
            markets.AppendLine("<color=#AEB5C3>TYPE                    LEADER                 SHARE</color>");
            markets.AppendLine("──────────────────────────────────────────────────────");
            foreach (var t in perType.Take(12))
            {
                string type = PrettyType(t.Type, 22);
                string leader = SafeName(t.Leader, t.LeaderId, 22);
                markets.Append(type.PadRight(23)).Append(" ")
                    .Append(leader.PadRight(23)).Append(" ")
                    .Append(t.Share.ToString("0.0").PadLeft(5)).AppendLine("%");
            }
            _markets.text = markets.ToString();
        }

        private static List<RivalStatsInfo> CollectPlayerStats()
        {
            var humanIds = new HashSet<string>(StringComparer.Ordinal) { MPConfig.PlayerId };
            try
            {
                foreach (var p in GameStatePatcher.ClientPlayerRoster.Keys)
                    if (!string.IsNullOrEmpty(p)) humanIds.Add(p);
            }
            catch { }
            try
            {
                var lobby = MPServer.IsRunning ? MPServer.LobbyPlayers : MPClient.LobbyPlayers;
                foreach (var p in lobby)
                    if (!string.IsNullOrEmpty(p)) humanIds.Add(p);
            }
            catch { }

            var result = new List<RivalStatsInfo>();
            if (MPServer.IsRunning)
            {
                try
                {
                    var snap = MPServer.BuildRivalsStatsSnapshot();
                    if (snap?.Stats != null)
                        result.AddRange(snap.Stats.Where(s => s != null && humanIds.Contains(s.Id)));
                }
                catch (Exception ex) { Plugin.Logger.LogWarning($"[Competitive] host stats: {ex.Message}"); }
            }
            else if (MPClient.IsConnected || MPClient.InMpGame)
            {
                try
                {
                    foreach (var kv in GameStatePatcher.ClientRivalStats)
                        if (kv.Value != null && humanIds.Contains(kv.Key)) result.Add(kv.Value);
                }
                catch { }
            }

            // Always overlay our own live native figures. On a client this also
            // avoids showing a stale self row during the request/response beat.
            try
            {
                RivalSelfStats.Build(out int ownedBuildings, out float weeklyIncome,
                    out string neighborhood, out List<RivalBusinessInfo> rows);
                string name = MPConfig.PlayerId;
                if (GameStatePatcher.ClientRivalNames.TryGetValue(MPConfig.PlayerId, out var n) && !string.IsNullOrWhiteSpace(n)) name = n;
                result.RemoveAll(x => x.Id == MPConfig.PlayerId);
                result.Add(new RivalStatsInfo
                {
                    Id = MPConfig.PlayerId,
                    Name = name,
                    WeeklyIncome = weeklyIncome,
                    OwnedBuildingsCount = ownedBuildings,
                    OwnedBusinessesCount = rows?.Count ?? 0,
                    MostActiveNeighborhood = neighborhood ?? "",
                    Businesses = rows ?? new List<RivalBusinessInfo>()
                });
            }
            catch { }

            return result;
        }

        private sealed class TypeLeader
        {
            public string Type = "";
            public string LeaderId = "";
            public string Leader = "";
            public float Share;
            public float Total;
        }

        private static List<TypeLeader> BuildTypeLeaders(List<RivalStatsInfo> players)
        {
            var totals = new Dictionary<string, Dictionary<string, float>>(StringComparer.Ordinal);
            var names = players.ToDictionary(p => p.Id, p => SafeName(p.Name, p.Id, 60), StringComparer.Ordinal);

            foreach (var p in players)
            {
                if (p.Businesses == null) continue;
                foreach (var b in p.Businesses)
                {
                    if (b == null || string.IsNullOrEmpty(b.BusinessType)) continue;
                    float income = Math.Max(0f, b.WeeklyIncome);
                    if (!totals.TryGetValue(b.BusinessType, out var byPlayer))
                    {
                        byPlayer = new Dictionary<string, float>(StringComparer.Ordinal);
                        totals[b.BusinessType] = byPlayer;
                    }
                    byPlayer.TryGetValue(p.Id, out var prev);
                    byPlayer[p.Id] = prev + income;
                }
            }

            var output = new List<TypeLeader>();
            foreach (var kv in totals)
            {
                float total = kv.Value.Values.Sum();
                if (total <= 0f) continue;
                var best = kv.Value.OrderByDescending(x => x.Value).First();
                output.Add(new TypeLeader
                {
                    Type = kv.Key,
                    LeaderId = best.Key,
                    Leader = names.TryGetValue(best.Key, out var nm) ? nm : best.Key,
                    Share = best.Value / total * 100f,
                    Total = total
                });
            }
            return output.OrderByDescending(x => x.Total).ThenBy(x => x.Type).ToList();
        }

        private static string Money(float value)
        {
            float abs = Math.Abs(value);
            string sign = value < 0 ? "-" : "";
            if (abs >= 1_000_000_000f) return $"{sign}${abs / 1_000_000_000f:0.0}B";
            if (abs >= 1_000_000f) return $"{sign}${abs / 1_000_000f:0.0}M";
            if (abs >= 1_000f) return $"{sign}${abs / 1_000f:0.0}K";
            return $"{sign}${abs:0}";
        }

        private static string SafeName(string? name, string fallback, int max)
        {
            string s = string.IsNullOrWhiteSpace(name) ? fallback : name!;
            s = s.Replace("\n", " ").Replace("\r", " ");
            return s.Length <= max ? s : s.Substring(0, Math.Max(1, max - 1)) + "…";
        }

        private static string PrettyType(string raw, int max)
        {
            string s = raw ?? "";
            const string prefix = "ba:businesstype_";
            if (s.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)) s = s.Substring(prefix.Length);
            s = s.Replace("_", " ");
            if (s.Length > 0) s = char.ToUpperInvariant(s[0]) + s.Substring(1);
            return s.Length <= max ? s : s.Substring(0, Math.Max(1, max - 1)) + "…";
        }

        private static GameObject AddPanel(Transform parent, string name, Vector2 pos, Vector2 size, Color color)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            var rt = go.AddComponent<RectTransform>();
            rt.anchorMin = rt.anchorMax = rt.pivot = new Vector2(0.5f, 0.5f);
            rt.sizeDelta = size;
            rt.anchoredPosition = pos;
            var img = go.AddComponent<Image>();
            img.color = color;
            return go;
        }

        private static TextMeshProUGUI AddText(Transform parent, string name, string value, Vector2 pos,
            Vector2 size, int fontSize, FontStyles style, TextAlignmentOptions align, Color color)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            var rt = go.AddComponent<RectTransform>();
            rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 0.5f);
            rt.pivot = new Vector2(0f, 1f);
            rt.sizeDelta = size;
            rt.anchoredPosition = pos;
            var txt = go.AddComponent<TextMeshProUGUI>();
            if (_font != null) txt.font = _font;
            txt.text = value;
            txt.fontSize = fontSize;
            txt.fontStyle = style;
            txt.alignment = align;
            txt.color = color;
            txt.enableWordWrapping = false;
            txt.richText = true;
            txt.raycastTarget = false;
            return txt;
        }
    }

    // The existing loader patches every [HarmonyPatch] class separately. Keeping
    // these hooks isolated means a game update can only disable the dashboard,
    // never the multiplayer core.
    [HarmonyPatch(typeof(MPHubNativePage), nameof(MPHubNativePage.Tick))]
    public static class Patch_CompetitiveDashboard_Tick
    {
        static void Postfix() => CompetitiveDashboard.Tick();
    }

    [HarmonyPatch(typeof(MPHubNativePage), nameof(MPHubNativePage.ShowPage))]
    public static class Patch_CompetitiveDashboard_BusinessShown
    {
        static void Postfix() => CompetitiveDashboard.OnBusinessShown();
    }

    [HarmonyPatch(typeof(MPHubNativePage), nameof(MPHubNativePage.HidePage))]
    public static class Patch_CompetitiveDashboard_NativeShown
    {
        static void Postfix() => CompetitiveDashboard.OnNativePageShown();
    }
}