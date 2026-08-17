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
    /// Competitive Market Intel — read-only companion to CompetitiveDashboard.
    ///
    /// Purpose before the first multiplayer field test:
    ///   1) surface actual player-vs-player product price overlaps ("price wars"),
    ///   2) show which business categories are real battlegrounds,
    ///   3) keep a tiny local event feed for lead/undercut flips,
    ///   4) expose a live diagnostic line so one test run tells us whether the
    ///      expected player businesses + retail-price replicas actually resolved.
    ///
    /// No protocol/save/economy mutation. Everything here is derived from the
    /// already-synchronised BuildingRegistration + rivals-stat mirrors.
    /// </summary>
    public static class CompetitiveMarketIntel
    {
        private static GameObject? _button;
        private static GameObject? _selectedIcon;
        private static GameObject? _page;
        private static TMP_FontAsset? _font;
        private static TextMeshProUGUI? _summary;
        private static TextMeshProUGUI? _wars;
        private static TextMeshProUGUI? _battles;
        private static TextMeshProUGUI? _events;
        private static TextMeshProUGUI? _diag;
        private static float _nextRefreshAt;
        private static float _nextRequestAt;
        private static float _nextEventSampleAt;
        private static bool _building;
        private static string _lastOverallLeader = "";
        private static readonly Dictionary<string, string> _lastTypeLeader = new(StringComparer.Ordinal);
        private static readonly Dictionary<string, int> _lastPriceRelation = new(StringComparer.Ordinal);
        private static readonly List<EventLine> _eventFeed = new();
        private static string _lastDiagSig = "";

        private static readonly Color Bg = new(0.070f, 0.073f, 0.090f, 0.99f);
        private static readonly Color Panel = new(0.105f, 0.11f, 0.135f, 0.99f);
        private static readonly Color Accent = new(0.20f, 0.52f, 0.90f, 1f);
        private static readonly Color Cyan = new(0.20f, 0.85f, 0.90f, 1f);
        private static readonly Color Gold = new(1f, 0.82f, 0.30f, 1f);
        private static readonly Color White = Color.white;
        private static readonly Color Muted = new(0.68f, 0.70f, 0.76f, 1f);

        private sealed class EventLine
        {
            public float At;
            public string Text = "";
        }

        private sealed class ShopRow
        {
            public string OwnerId = "";
            public string OwnerName = "";
            public string Address = "";
            public string Type = "";
            public string BusinessName = "";
            public Dictionary<string, float> Prices = new(StringComparer.Ordinal);
        }

        private sealed class PriceWar
        {
            public string RivalId = "";
            public string RivalName = "";
            public string Type = "";
            public string Product = "";
            public float MyPrice;
            public float RivalPrice;
            public float DiffPct;
        }

        private sealed class TypeBattle
        {
            public string Type = "";
            public string LeaderId = "";
            public string LeaderName = "";
            public float LeaderIncome;
            public float TotalIncome;
            public int PlayerCount;
            public float LeaderShare;
        }

        private sealed class Snapshot
        {
            public HashSet<string> Humans = new(StringComparer.Ordinal);
            public Dictionary<string, string> Names = new(StringComparer.Ordinal);
            public List<RivalStatsInfo> Stats = new();
            public List<ShopRow> Shops = new();
            public List<PriceWar> Wars = new();
            public List<TypeBattle> Battles = new();
            public int TotalRegs;
            public int PricedPlayerShops;
            public int PlayerShops;
            public string Source = "OFFLINE";
        }

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
                    Refresh(now);
                }
            }
            catch (Exception ex)
            {
                Plugin.Logger.LogWarning($"[MarketIntel] Tick: {ex.Message}");
            }
        }

        public static void Reset()
        {
            try { if (_button != null) UnityEngine.Object.Destroy(_button); } catch { }
            try { if (_page != null) UnityEngine.Object.Destroy(_page); } catch { }
            _button = null;
            _selectedIcon = null;
            _page = null;
            _font = null;
            _summary = null;
            _wars = null;
            _battles = null;
            _events = null;
            _diag = null;
            _nextRefreshAt = 0f;
            _nextRequestAt = 0f;
            _nextEventSampleAt = 0f;
            _lastOverallLeader = "";
            _lastTypeLeader.Clear();
            _lastPriceRelation.Clear();
            _eventFeed.Clear();
            _lastDiagSig = "";
            _building = false;
        }

        public static void OnBusinessShown() => HideOwnPage();
        public static void OnNativePageShown() => HideOwnPage();

        private static void HideOwnPage()
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
                var businessPage = content.parent;
                var apps = businessPage?.parent;
                var canvas = apps?.parent;
                var row = canvas?.Find("Topbar/AppButtons");
                var sourceButton = row?.Find("BAMP_Competition") ?? row?.Find("BAMP_Business");
                if (businessPage == null || apps == null || canvas == null || row == null || sourceButton == null) return;

                _button = UnityEngine.Object.Instantiate(sourceButton.gameObject, row);
                _button.name = "BAMP_MarketIntel";
                var titleT = _button.transform.Find("Title");
                var titleLbl = titleT != null ? titleT.GetComponent<TextMeshProUGUI>() : null;
                if (titleLbl != null)
                {
                    titleLbl.text = "Market Intel";
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

                _page = new GameObject("BAMP_MarketIntelApp");
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
                AddText(root.transform, "Title", "MARKET INTEL", new Vector2(-860f, 348f), new Vector2(1720f, 50f),
                    28, FontStyles.Bold, TextAlignmentOptions.Left, White);
                _summary = AddText(root.transform, "Summary", "Scanning competitive state…", new Vector2(-860f, 302f),
                    new Vector2(1720f, 35f), 15, FontStyles.Normal, TextAlignmentOptions.Left, Muted);

                AddPanel(root.transform, "WarsPanel", new Vector2(-435f, 105f), new Vector2(880f, 330f), Panel);
                AddText(root.transform, "WarsHeader", "PRICE WARS", new Vector2(-825f, 245f), new Vector2(780f, 32f),
                    19, FontStyles.Bold, TextAlignmentOptions.Left, Gold);
                _wars = AddText(root.transform, "Wars", "No direct product overlap yet.", new Vector2(-825f, 205f),
                    new Vector2(780f, 250f), 15, FontStyles.Normal, TextAlignmentOptions.TopLeft, White);

                AddPanel(root.transform, "BattlesPanel", new Vector2(435f, 105f), new Vector2(820f, 330f), Panel);
                AddText(root.transform, "BattlesHeader", "BATTLEGROUNDS", new Vector2(75f, 245f), new Vector2(720f, 32f),
                    19, FontStyles.Bold, TextAlignmentOptions.Left, Accent);
                _battles = AddText(root.transform, "Battles", "Waiting for business income breakdown…", new Vector2(75f, 205f),
                    new Vector2(720f, 250f), 15, FontStyles.Normal, TextAlignmentOptions.TopLeft, White);

                AddPanel(root.transform, "EventsPanel", new Vector2(0f, -205f), new Vector2(1720f, 255f), Panel);
                AddText(root.transform, "EventsHeader", "LIVE COMPETITIVE FEED", new Vector2(-820f, -100f), new Vector2(1640f, 32f),
                    18, FontStyles.Bold, TextAlignmentOptions.Left, Cyan);
                _events = AddText(root.transform, "Events", "Baseline will be captured when another player is present.",
                    new Vector2(-820f, -140f), new Vector2(1640f, 165f), 15, FontStyles.Normal, TextAlignmentOptions.TopLeft, White);

                _diag = AddText(root.transform, "Diagnostics", "Diagnostics: initializing…", new Vector2(-860f, -360f),
                    new Vector2(1720f, 30f), 13, FontStyles.Normal, TextAlignmentOptions.Left, Muted);

                _page.SetActive(false);
                Plugin.Logger.LogInfo("[MarketIntel] page injected (price wars + battlegrounds + diagnostics; read-only).");
            }
            catch (Exception ex)
            {
                Plugin.Logger.LogWarning($"[MarketIntel] build: {ex.Message}");
                Reset();
            }
            finally { _building = false; }
        }

        private static void Show()
        {
            try
            {
                if (_page == null || MPHubNativePage.ContentRoot == null) return;
                MPHubNativePage.ShowPage();
                MPHubNativePage.ContentRoot.parent?.gameObject.SetActive(false);
                _page.SetActive(true);
                SetSelectedDots();
                HoldNativeTitle();
                _nextRefreshAt = 0f;
                _nextRequestAt = 0f;
                _nextEventSampleAt = 0f;
                _lastOverallLeader = "";
                _lastTypeLeader.Clear();
                _lastPriceRelation.Clear();
                _eventFeed.Clear();
                Refresh(Time.unscaledTime);
                if (MPClient.IsConnected) MPClient.SendRivalsStatsRequest();
            }
            catch (Exception ex) { Plugin.Logger.LogWarning($"[MarketIntel] show: {ex.Message}"); }
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
                if (lbl != null && lbl.text != "Market Intel") lbl.text = "Market Intel";
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

        private static void Refresh(float now)
        {
            if (_summary == null || _wars == null || _battles == null || _events == null || _diag == null) return;
            var snap = BuildSnapshot();

            _summary.text = $"{snap.Humans.Count} human player{(snap.Humans.Count == 1 ? "" : "s")} • "
                          + $"{snap.PlayerShops} player businesses • {snap.PricedPlayerShops} priced shops • "
                          + $"{snap.Wars.Count} direct product matchups";

            BuildWarsText(snap);
            BuildBattlesText(snap);

            if (now >= _nextEventSampleAt)
            {
                _nextEventSampleAt = now + 5f;
                SampleEvents(snap, now);
            }
            BuildEventText(now);
            BuildDiagnostics(snap);
        }

        private static Snapshot BuildSnapshot()
        {
            var s = new Snapshot();
            s.Humans.Add(MPConfig.PlayerId);

            try
            {
                foreach (var p in GameStatePatcher.ClientPlayerRoster.Keys)
                    if (!string.IsNullOrEmpty(p)) s.Humans.Add(p);
            }
            catch { }
            try
            {
                var lobby = MPServer.IsRunning ? MPServer.LobbyPlayers : MPClient.LobbyPlayers;
                foreach (var p in lobby)
                    if (!string.IsNullOrEmpty(p)) s.Humans.Add(p);
            }
            catch { }

            foreach (var id in s.Humans) s.Names[id] = ResolveName(id);

            if (MPServer.IsRunning)
            {
                s.Source = "HOST LIVE";
                try
                {
                    var rs = MPServer.BuildRivalsStatsSnapshot();
                    if (rs?.Stats != null)
                        s.Stats.AddRange(rs.Stats.Where(x => x != null && s.Humans.Contains(x.Id)));
                }
                catch { }
            }
            else if (MPClient.IsConnected || MPClient.InMpGame)
            {
                s.Source = MPClient.IsConnected ? "CLIENT CACHE / LIVE LINK" : "CLIENT CACHE / LINK DOWN";
                try
                {
                    foreach (var kv in GameStatePatcher.ClientRivalStats)
                        if (kv.Value != null && s.Humans.Contains(kv.Key)) s.Stats.Add(kv.Value);
                }
                catch { }
            }

            OverlayLocalStats(s.Stats);
            s.Stats = s.Stats.GroupBy(x => x.Id).Select(g => g.OrderByDescending(x => Math.Abs(x.WeeklyIncome)).First()).ToList();
            foreach (var p in s.Stats)
                if (!string.IsNullOrWhiteSpace(p.Name)) s.Names[p.Id] = p.Name;

            try
            {
                var gi = SaveGameManager.Current;
                if (gi?.BuildingRegistrations != null)
                {
                    foreach (var reg in gi.BuildingRegistrations)
                    {
                        if (reg == null) continue;
                        s.TotalRegs++;
                        string owner = "";
                        try
                        {
                            if (reg.RentedByPlayer) owner = MPConfig.PlayerId;
                            else
                            {
                                string rid = reg.businessOwnerRivalId?.ToString() ?? "";
                                if (s.Humans.Contains(rid)) owner = rid;
                            }
                        }
                        catch { }
                        if (string.IsNullOrEmpty(owner)) continue;

                        string type = "";
                        try { type = reg.businessTypeName ?? ""; } catch { }
                        if (string.IsNullOrEmpty(type) || type == "ba:businesstype_empty") continue;

                        var shop = new ShopRow
                        {
                            OwnerId = owner,
                            OwnerName = s.Names.TryGetValue(owner, out var nm) ? nm : owner,
                            Type = type,
                            Address = GameStateReader.AddressKey(reg),
                            BusinessName = reg.BusinessName?.ToString() ?? ""
                        };

                        try
                        {
                            if (reg.retailPrices != null)
                                foreach (var rp in reg.retailPrices)
                                    if (rp != null && !string.IsNullOrEmpty(rp.itemName) && rp.price > 0f)
                                        shop.Prices[rp.itemName] = rp.price;
                        }
                        catch { }

                        s.Shops.Add(shop);
                        s.PlayerShops++;
                        if (shop.Prices.Count > 0) s.PricedPlayerShops++;
                    }
                }
            }
            catch (Exception ex) { Plugin.Logger.LogWarning($"[MarketIntel] registration scan: {ex.Message}"); }

            s.Wars = BuildPriceWars(s.Shops, s.Names);
            s.Battles = BuildTypeBattles(s.Stats, s.Names);
            return s;
        }

        private static void OverlayLocalStats(List<RivalStatsInfo> stats)
        {
            try
            {
                RivalSelfStats.Build(out int buildings, out float income, out string hood, out List<RivalBusinessInfo> rows);
                stats.RemoveAll(x => x.Id == MPConfig.PlayerId);
                stats.Add(new RivalStatsInfo
                {
                    Id = MPConfig.PlayerId,
                    Name = ResolveName(MPConfig.PlayerId),
                    WeeklyIncome = income,
                    OwnedBuildingsCount = buildings,
                    OwnedBusinessesCount = rows?.Count ?? 0,
                    MostActiveNeighborhood = hood ?? "",
                    Businesses = rows ?? new List<RivalBusinessInfo>()
                });
            }
            catch { }
        }

        private static List<PriceWar> BuildPriceWars(List<ShopRow> shops, Dictionary<string, string> names)
        {
            var byOwnerTypeProduct = new Dictionary<string, List<float>>(StringComparer.Ordinal);
            foreach (var shop in shops)
            {
                foreach (var rp in shop.Prices)
                {
                    string key = shop.OwnerId + "\u001f" + shop.Type + "\u001f" + rp.Key;
                    if (!byOwnerTypeProduct.TryGetValue(key, out var vals))
                    {
                        vals = new List<float>();
                        byOwnerTypeProduct[key] = vals;
                    }
                    vals.Add(rp.Value);
                }
            }

            var output = new List<PriceWar>();
            var localKeys = byOwnerTypeProduct.Keys.Where(k => k.StartsWith(MPConfig.PlayerId + "\u001f", StringComparison.Ordinal)).ToList();
            foreach (var lk in localKeys)
            {
                var parts = lk.Split('\u001f');
                if (parts.Length != 3) continue;
                string type = parts[1], product = parts[2];
                float mine = byOwnerTypeProduct[lk].Average();
                if (mine <= 0f) continue;

                foreach (var owner in names.Keys)
                {
                    if (owner == MPConfig.PlayerId) continue;
                    string rk = owner + "\u001f" + type + "\u001f" + product;
                    if (!byOwnerTypeProduct.TryGetValue(rk, out var rivalVals) || rivalVals.Count == 0) continue;
                    float rival = rivalVals.Average();
                    if (rival <= 0f) continue;
                    output.Add(new PriceWar
                    {
                        RivalId = owner,
                        RivalName = names.TryGetValue(owner, out var nm) ? nm : owner,
                        Type = type,
                        Product = product,
                        MyPrice = mine,
                        RivalPrice = rival,
                        DiffPct = (mine - rival) / rival * 100f
                    });
                }
            }
            return output.OrderByDescending(x => Math.Abs(x.DiffPct)).ThenBy(x => x.Type).ThenBy(x => x.Product).ToList();
        }

        private static List<TypeBattle> BuildTypeBattles(List<RivalStatsInfo> stats, Dictionary<string, string> names)
        {
            var table = new Dictionary<string, Dictionary<string, float>>(StringComparer.Ordinal);
            foreach (var p in stats)
            {
                if (p.Businesses == null) continue;
                foreach (var b in p.Businesses)
                {
                    if (b == null || string.IsNullOrEmpty(b.BusinessType)) continue;
                    if (!table.TryGetValue(b.BusinessType, out var byPlayer))
                    {
                        byPlayer = new Dictionary<string, float>(StringComparer.Ordinal);
                        table[b.BusinessType] = byPlayer;
                    }
                    byPlayer.TryGetValue(p.Id, out float prev);
                    byPlayer[p.Id] = prev + Math.Max(0f, b.WeeklyIncome);
                }
            }

            var result = new List<TypeBattle>();
            foreach (var kv in table)
            {
                if (kv.Value.Count < 2) continue;
                float total = kv.Value.Values.Sum();
                var leader = kv.Value.OrderByDescending(x => x.Value).First();
                result.Add(new TypeBattle
                {
                    Type = kv.Key,
                    LeaderId = leader.Key,
                    LeaderName = names.TryGetValue(leader.Key, out var nm) ? nm : leader.Key,
                    LeaderIncome = leader.Value,
                    TotalIncome = total,
                    PlayerCount = kv.Value.Count,
                    LeaderShare = total > 0f ? leader.Value / total * 100f : 0f
                });
            }
            return result.OrderByDescending(x => x.TotalIncome).ThenByDescending(x => x.PlayerCount).ToList();
        }

        private static void BuildWarsText(Snapshot s)
        {
            if (_wars == null) return;
            if (s.Wars.Count == 0)
            {
                _wars.text = s.Humans.Count < 2
                    ? "Waiting for another player.\n\nWhen two player businesses sell the same product in the same business type, the live synced prices will appear here."
                    : "No direct product overlap detected yet.\n\nThis is valid if the players currently operate different business types or do not share priced products.";
                return;
            }

            var sb = new System.Text.StringBuilder();
            sb.AppendLine("<color=#AEB5C3>TYPE / PRODUCT              YOU      RIVAL      GAP</color>");
            sb.AppendLine("──────────────────────────────────────────────────────────");
            foreach (var w in s.Wars.Take(7))
            {
                string label = PrettyType(w.Type, 13) + " / " + PrettyProduct(w.Product, 18);
                string gap = w.DiffPct < -0.05f ? $"<color=#59EB8F>{w.DiffPct:0.0}%</color>"
                           : w.DiffPct > 0.05f ? $"<color=#FF7777>+{w.DiffPct:0.0}%</color>"
                           : "0.0%";
                sb.Append(label.PadRight(32)).Append(" ")
                  .Append(Money(w.MyPrice).PadLeft(8)).Append("  ")
                  .Append(Money(w.RivalPrice).PadLeft(8)).Append("  ")
                  .Append(gap).AppendLine();
                sb.Append("  vs ").Append(Safe(w.RivalName, 25)).AppendLine();
            }
            _wars.text = sb.ToString();
        }

        private static void BuildBattlesText(Snapshot s)
        {
            if (_battles == null) return;
            if (s.Battles.Count == 0)
            {
                _battles.text = s.Humans.Count < 2
                    ? "Waiting for another player."
                    : "No shared business category with income data yet.";
                return;
            }

            var sb = new System.Text.StringBuilder();
            sb.AppendLine("<color=#AEB5C3>TYPE                 LEADER              SHARE</color>");
            sb.AppendLine("───────────────────────────────────────────────────");
            foreach (var b in s.Battles.Take(7))
            {
                sb.Append(PrettyType(b.Type, 20).PadRight(21)).Append(" ")
                  .Append(Safe(b.LeaderName, 20).PadRight(21)).Append(" ")
                  .Append(b.LeaderShare.ToString("0.0").PadLeft(5)).Append("%")
                  .Append("  [").Append(b.PlayerCount).AppendLine("p]");
            }
            _battles.text = sb.ToString();
        }

        private static void SampleEvents(Snapshot s, float now)
        {
            var ordered = s.Stats.OrderByDescending(x => x.WeeklyIncome).ToList();
            string overall = ordered.Count > 0 ? ordered[0].Id : "";
            if (string.IsNullOrEmpty(_lastOverallLeader)) _lastOverallLeader = overall;
            else if (!string.IsNullOrEmpty(overall) && overall != _lastOverallLeader)
            {
                AddEvent(now, $"Overall income lead changed: {NameOf(s, overall)} moved ahead of {NameOf(s, _lastOverallLeader)}.");
                _lastOverallLeader = overall;
            }

            var liveTypes = new HashSet<string>(StringComparer.Ordinal);
            foreach (var b in s.Battles)
            {
                liveTypes.Add(b.Type);
                if (!_lastTypeLeader.TryGetValue(b.Type, out var old)) _lastTypeLeader[b.Type] = b.LeaderId;
                else if (old != b.LeaderId)
                {
                    AddEvent(now, $"{PrettyType(b.Type, 24)} lead changed: {NameOf(s, b.LeaderId)} is now #1.");
                    _lastTypeLeader[b.Type] = b.LeaderId;
                }
            }
            foreach (var t in _lastTypeLeader.Keys.ToList()) if (!liveTypes.Contains(t)) _lastTypeLeader.Remove(t);

            var liveRelations = new HashSet<string>(StringComparer.Ordinal);
            foreach (var w in s.Wars)
            {
                string key = w.RivalId + "|" + w.Type + "|" + w.Product;
                liveRelations.Add(key);
                int rel = Math.Abs(w.MyPrice - w.RivalPrice) < 0.005f ? 0 : (w.MyPrice < w.RivalPrice ? -1 : 1);
                if (!_lastPriceRelation.TryGetValue(key, out int oldRel)) _lastPriceRelation[key] = rel;
                else if (rel != oldRel)
                {
                    string action = rel < 0 ? "You undercut" : rel > 0 ? "Rival undercut" : "Prices matched";
                    AddEvent(now, $"{action}: {PrettyProduct(w.Product, 24)} vs {Safe(w.RivalName, 20)} ({Money(w.MyPrice)} / {Money(w.RivalPrice)}). ");
                    _lastPriceRelation[key] = rel;
                }
            }
            foreach (var k in _lastPriceRelation.Keys.ToList()) if (!liveRelations.Contains(k)) _lastPriceRelation.Remove(k);
        }

        private static void AddEvent(float now, string text)
        {
            _eventFeed.Insert(0, new EventLine { At = now, Text = text });
            while (_eventFeed.Count > 8) _eventFeed.RemoveAt(_eventFeed.Count - 1);
            Plugin.Logger.LogInfo($"[MarketIntel] EVENT: {text}");
        }

        private static void BuildEventText(float now)
        {
            if (_events == null) return;
            if (_eventFeed.Count == 0)
            {
                _events.text = "No competitive lead/undercut changes since this page opened.\n"
                             + "Baseline is live; changes will appear here automatically.";
                return;
            }

            var sb = new System.Text.StringBuilder();
            foreach (var e in _eventFeed.Take(6))
            {
                float age = Math.Max(0f, now - e.At);
                string ago = age < 60f ? $"{age:0}s ago" : $"{age / 60f:0.0}m ago";
                sb.Append("• ").Append(e.Text).Append("  <color=#AEB5C3>(").Append(ago).AppendLine(")</color>");
            }
            _events.text = sb.ToString();
        }

        private static void BuildDiagnostics(Snapshot s)
        {
            if (_diag == null) return;
            int missingStats = Math.Max(0, s.Humans.Count - s.Stats.Select(x => x.Id).Distinct().Count());
            string state = missingStats == 0 ? "<color=#59EB8F>DATA READY</color>" : $"<color=#FFD24C>WAITING {missingStats} STATS ROW(S)</color>";
            string priceState = s.PricedPlayerShops > 0 ? "prices present" : "no player price tables yet";
            _diag.text = $"{state} • source {s.Source} • regs {s.TotalRegs} • player shops {s.PlayerShops} • {priceState} • price matchups {s.Wars.Count}";

            string sig = $"{s.Source}|{s.Humans.Count}|{s.Stats.Count}|{s.TotalRegs}|{s.PlayerShops}|{s.PricedPlayerShops}|{s.Wars.Count}|{s.Battles.Count}|{missingStats}";
            if (sig != _lastDiagSig)
            {
                _lastDiagSig = sig;
                Plugin.Logger.LogInfo($"[MarketIntel] DIAG source={s.Source}; humans={s.Humans.Count}; stats={s.Stats.Count}; regs={s.TotalRegs}; playerShops={s.PlayerShops}; priced={s.PricedPlayerShops}; wars={s.Wars.Count}; battlegrounds={s.Battles.Count}; missingStats={missingStats}.");
            }
        }

        private static string ResolveName(string id)
        {
            if (string.IsNullOrEmpty(id)) return "?";
            try
            {
                if (GameStatePatcher.ClientRivalNames.TryGetValue(id, out var n) && !string.IsNullOrWhiteSpace(n)) return n;
            }
            catch { }
            try
            {
                if (MPServer.IsRunning && MPServer._characterNamesByPlayerId.TryGetValue(id, out var hn) && !string.IsNullOrWhiteSpace(hn)) return hn;
            }
            catch { }
            return id;
        }

        private static string NameOf(Snapshot s, string id)
            => s.Names.TryGetValue(id ?? "", out var n) && !string.IsNullOrWhiteSpace(n) ? n : (id ?? "?");

        private static string PrettyType(string raw, int max)
        {
            string s = raw ?? "";
            const string prefix = "ba:businesstype_";
            if (s.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)) s = s.Substring(prefix.Length);
            s = s.Replace("_", " ");
            if (s.Length > 0) s = char.ToUpperInvariant(s[0]) + s.Substring(1);
            return Safe(s, max);
        }

        private static string PrettyProduct(string raw, int max)
        {
            string s = raw ?? "";
            int colon = s.LastIndexOf(':');
            if (colon >= 0 && colon + 1 < s.Length) s = s.Substring(colon + 1);
            s = s.Replace("_", " ");
            if (s.Length > 0) s = char.ToUpperInvariant(s[0]) + s.Substring(1);
            return Safe(s, max);
        }

        private static string Safe(string? s, int max)
        {
            string v = string.IsNullOrWhiteSpace(s) ? "?" : s!;
            v = v.Replace("\n", " ").Replace("\r", " ");
            return v.Length <= max ? v : v.Substring(0, Math.Max(1, max - 1)) + "…";
        }

        private static string Money(float value)
        {
            float abs = Math.Abs(value);
            string sign = value < 0 ? "-" : "";
            if (abs >= 1_000_000f) return $"{sign}${abs / 1_000_000f:0.0}M";
            if (abs >= 1_000f) return $"{sign}${abs / 1_000f:0.0}K";
            return $"{sign}${abs:0.##}";
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

        private static TextMeshProUGUI AddText(Transform parent, string name, string value, Vector2 pos, Vector2 size,
            int fontSize, FontStyles style, TextAlignmentOptions align, Color color)
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

    [HarmonyPatch(typeof(MPHubNativePage), nameof(MPHubNativePage.Tick))]
    public static class Patch_CompetitiveMarketIntel_Tick
    {
        static void Postfix() => CompetitiveMarketIntel.Tick();
    }

    [HarmonyPatch(typeof(MPHubNativePage), nameof(MPHubNativePage.ShowPage))]
    public static class Patch_CompetitiveMarketIntel_BusinessShown
    {
        static void Postfix() => CompetitiveMarketIntel.OnBusinessShown();
    }

    [HarmonyPatch(typeof(MPHubNativePage), nameof(MPHubNativePage.HidePage))]
    public static class Patch_CompetitiveMarketIntel_NativeShown
    {
        static void Postfix() => CompetitiveMarketIntel.OnNativePageShown();
    }
}