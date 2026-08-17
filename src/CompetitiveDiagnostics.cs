using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using HarmonyLib;

namespace BigAmbitionsMP
{
    /// <summary>
    /// Field evidence for the competitive layer. The upstream bug reporter already
    /// collects logs/content/version/integrity; this appends the compact economy
    /// view we need so the first two-player run can answer most follow-up questions
    /// without asking both players to reproduce the session.
    /// </summary>
    internal static class CompetitiveDiagnostics
    {
        internal static string BuildReportSection()
        {
            var sb = new System.Text.StringBuilder();
            try
            {
                var humans = HumanIds();
                var stats = Stats(humans);
                var regs = SaveGameManager.Current?.BuildingRegistrations;
                int totalRegs = 0, playerBusinesses = 0, pricedBusinesses = 0;
                var byOwner = new Dictionary<string, int>(StringComparer.Ordinal);
                var pricedByOwner = new Dictionary<string, int>(StringComparer.Ordinal);
                var rows = new List<string>();

                if (regs != null)
                {
                    foreach (var reg in regs)
                    {
                        if (reg == null) continue;
                        totalRegs++;
                        string owner = "";
                        try
                        {
                            if (reg.RentedByPlayer) owner = MPConfig.PlayerId;
                            else
                            {
                                string rid = reg.businessOwnerRivalId?.ToString() ?? "";
                                if (humans.Contains(rid)) owner = rid;
                            }
                        }
                        catch { }
                        if (string.IsNullOrEmpty(owner)) continue;

                        string type = "";
                        try { type = reg.businessTypeName ?? ""; } catch { }
                        if (string.IsNullOrEmpty(type) || type == "ba:businesstype_empty") continue;

                        playerBusinesses++;
                        byOwner.TryGetValue(owner, out int ob); byOwner[owner] = ob + 1;
                        int priceCount = 0;
                        try
                        {
                            if (reg.retailPrices != null)
                                foreach (var rp in reg.retailPrices)
                                    if (rp != null && !string.IsNullOrEmpty(rp.itemName) && rp.price > 0f) priceCount++;
                        }
                        catch { }
                        if (priceCount > 0)
                        {
                            pricedBusinesses++;
                            pricedByOwner.TryGetValue(owner, out int op); pricedByOwner[owner] = op + 1;
                        }
                        if (rows.Count < 40)
                            rows.Add($"- {GameStateReader.AddressKey(reg)} | owner={Name(owner)} ({owner}) | type={type} | prices={priceCount} | name={Safe(reg.BusinessName?.ToString())}");
                    }
                }

                sb.AppendLine();
                sb.AppendLine("## Competitive Diagnostics");
                sb.AppendLine($"CompetitiveRole: {(MPServer.IsRunning ? "host" : MPClient.IsConnected ? "client-live" : MPClient.InMpGame ? "client-link-down" : "offline")}");
                sb.AppendLine($"Humans: {humans.Count} [{string.Join(", ", humans.Select(x => Name(x) + " (" + x + ")"))}]");
                sb.AppendLine($"StatsRows: {stats.Count}/{humans.Count}");
                sb.AppendLine($"Registrations: {totalRegs}");
                sb.AppendLine($"PlayerBusinessesResolved: {playerBusinesses}");
                sb.AppendLine($"PricedPlayerBusinesses: {pricedBusinesses}");
                sb.AppendLine($"PriceSyncEnabled: {MPPriceSync.Enabled}");
                sb.AppendLine($"Protocol: {ProtocolInfo.Version}");
                sb.AppendLine();
                sb.AppendLine("### Human rival stats");
                foreach (var p in stats.OrderByDescending(x => x.WeeklyIncome))
                    sb.AppendLine($"- {Name(p.Id)} ({p.Id}): weekly={p.WeeklyIncome:0.##}, businesses={p.OwnedBusinessesCount}, buildings={p.OwnedBuildingsCount}, breakdown={p.Businesses?.Count ?? 0}, neighborhood={Safe(p.MostActiveNeighborhood)}");
                foreach (var id in humans.Where(id => stats.All(x => x.Id != id)))
                    sb.AppendLine($"- MISSING STATS: {Name(id)} ({id})");

                sb.AppendLine();
                sb.AppendLine("### Ownership/price resolution by player");
                foreach (var id in humans)
                {
                    byOwner.TryGetValue(id, out int b);
                    pricedByOwner.TryGetValue(id, out int p);
                    sb.AppendLine($"- {Name(id)} ({id}): resolvedBusinesses={b}, pricedBusinesses={p}");
                }

                sb.AppendLine();
                sb.AppendLine("### Resolved player businesses (max 40)");
                if (rows.Count == 0) sb.AppendLine("- none");
                else foreach (var row in rows) sb.AppendLine(row);

                // Compact pairwise overlap evidence: same business type + same priced product.
                var overlaps = PriceOverlaps(humans);
                sb.AppendLine();
                sb.AppendLine($"### Direct price overlaps ({overlaps.Count})");
                if (overlaps.Count == 0) sb.AppendLine("- none");
                else foreach (var line in overlaps.Take(40)) sb.AppendLine("- " + line);
            }
            catch (Exception ex)
            {
                sb.AppendLine();
                sb.AppendLine("## Competitive Diagnostics");
                sb.AppendLine($"ERROR building competitive section: {ex.GetType().Name}: {ex.Message}");
            }
            return sb.ToString();
        }

        private static HashSet<string> HumanIds()
        {
            var h = new HashSet<string>(StringComparer.Ordinal) { MPConfig.PlayerId };
            try { foreach (var p in GameStatePatcher.ClientPlayerRoster.Keys) if (!string.IsNullOrEmpty(p)) h.Add(p); } catch { }
            try
            {
                var list = MPServer.IsRunning ? MPServer.LobbyPlayers : MPClient.LobbyPlayers;
                foreach (var p in list) if (!string.IsNullOrEmpty(p)) h.Add(p);
            }
            catch { }
            return h;
        }

        private static List<RivalStatsInfo> Stats(HashSet<string> humans)
        {
            var list = new List<RivalStatsInfo>();
            try
            {
                if (MPServer.IsRunning)
                {
                    var snap = MPServer.BuildRivalsStatsSnapshot();
                    if (snap?.Stats != null) list.AddRange(snap.Stats.Where(x => x != null && humans.Contains(x.Id)));
                }
                else
                {
                    foreach (var kv in GameStatePatcher.ClientRivalStats)
                        if (kv.Value != null && humans.Contains(kv.Key)) list.Add(kv.Value);
                }
            }
            catch { }

            try
            {
                RivalSelfStats.Build(out int buildings, out float weekly, out string hood, out List<RivalBusinessInfo> rows);
                list.RemoveAll(x => x.Id == MPConfig.PlayerId);
                list.Add(new RivalStatsInfo
                {
                    Id = MPConfig.PlayerId, Name = Name(MPConfig.PlayerId), WeeklyIncome = weekly,
                    OwnedBuildingsCount = buildings, OwnedBusinessesCount = rows?.Count ?? 0,
                    MostActiveNeighborhood = hood ?? "", Businesses = rows ?? new List<RivalBusinessInfo>()
                });
            }
            catch { }
            return list.GroupBy(x => x.Id).Select(g => g.OrderByDescending(x => Math.Abs(x.WeeklyIncome)).First()).ToList();
        }

        private sealed class PriceCell
        {
            public string Owner = "";
            public string Type = "";
            public string Item = "";
            public float Price;
        }

        private static List<string> PriceOverlaps(HashSet<string> humans)
        {
            var cells = new List<PriceCell>();
            try
            {
                var regs = SaveGameManager.Current?.BuildingRegistrations;
                if (regs == null) return new List<string>();
                foreach (var reg in regs)
                {
                    if (reg == null) continue;
                    string owner = "";
                    if (reg.RentedByPlayer) owner = MPConfig.PlayerId;
                    else
                    {
                        string rid = reg.businessOwnerRivalId?.ToString() ?? "";
                        if (humans.Contains(rid)) owner = rid;
                    }
                    if (string.IsNullOrEmpty(owner)) continue;
                    string type = reg.businessTypeName ?? "";
                    if (reg.retailPrices == null) continue;
                    foreach (var rp in reg.retailPrices)
                        if (rp != null && !string.IsNullOrEmpty(rp.itemName) && rp.price > 0f)
                            cells.Add(new PriceCell { Owner = owner, Type = type, Item = rp.itemName, Price = rp.price });
                }
            }
            catch { }

            var outLines = new List<string>();
            var groups = cells.GroupBy(x => x.Type + "\u001f" + x.Item);
            foreach (var g in groups)
            {
                var ownerPrices = g.GroupBy(x => x.Owner).Select(og => new { Owner = og.Key, Price = og.Average(x => x.Price) }).ToList();
                if (ownerPrices.Count < 2) continue;
                for (int i = 0; i < ownerPrices.Count; i++)
                    for (int j = i + 1; j < ownerPrices.Count; j++)
                    {
                        var a = ownerPrices[i]; var b = ownerPrices[j];
                        var sample = g.First();
                        outLines.Add($"{sample.Type} / {sample.Item}: {Name(a.Owner)}={a.Price:0.##} vs {Name(b.Owner)}={b.Price:0.##}");
                    }
            }
            return outLines;
        }

        private static string Name(string id)
        {
            if (string.IsNullOrEmpty(id)) return "?";
            try { if (GameStatePatcher.ClientRivalNames.TryGetValue(id, out var n) && !string.IsNullOrWhiteSpace(n)) return Safe(n); } catch { }
            try { if (MPServer.IsRunning && MPServer._characterNamesByPlayerId.TryGetValue(id, out var n) && !string.IsNullOrWhiteSpace(n)) return Safe(n); } catch { }
            return Safe(id);
        }

        private static string Safe(string? s)
        {
            if (string.IsNullOrWhiteSpace(s)) return "(blank)";
            return s!.Replace("\r", " ").Replace("\n", " ").Replace("|", "/");
        }
    }

    /// <summary>Append our evidence AFTER the upstream report writer finishes.
    /// This changes no upstream report behavior and fails open if anything goes wrong.</summary>
    [HarmonyPatch]
    public static class Patch_CompetitiveDiagnostics_BugReport
    {
        static MethodBase TargetMethod()
            => AccessTools.Method(typeof(MPBugReport), "WriteReport", new[] { typeof(string), typeof(string) });

        static void Postfix(string path, string reason)
        {
            try
            {
                if (string.IsNullOrEmpty(path) || !File.Exists(path)) return;
                File.AppendAllText(path, CompetitiveDiagnostics.BuildReportSection());
                Plugin.Logger.LogInfo("[CompetitiveDiag] appended competitive evidence to report.md.");
            }
            catch (Exception ex) { Plugin.Logger.LogWarning($"[CompetitiveDiag] report append: {ex.Message}"); }
        }
    }
}