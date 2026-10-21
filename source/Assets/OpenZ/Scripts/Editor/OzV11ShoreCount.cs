using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using OpenZ.Sim;

namespace OpenZ.EditorTools
{
    /// <summary>
    /// Counted check of kit b4_shore_jitter: vehicles ordered to the shore. Headless simulation on the three maps of version 1.1
    /// (Desert L02, Jungle L16, Car Park L17), each without its start units; red vehicles start 9 to 16 tiles from each of 40
    /// shore points and get one move order to it, 60 s per run.
    /// Rows: one jeep, one light tank, one medium tank to a dry tile at the water; three jeeps on one order to a dry tile at the
    /// water; three jeeps and two light tanks on one order to a water tile at the shore (the order ends at the nearest dry spots).
    /// Every row is run with the rule off (the game before version 1.1) and on. A vehicle "shook" when it made 4 or more
    /// reversals inside one second (a reversal = a step pointing more than 90 degrees away from the step before it).
    /// </summary>
    public static class OzV11ShoreCount
    {
        public const int Points = 40;
        public const double RunSeconds = 60, RestSeconds = 2.0;
        public const int ShakeReversals = 4;

        sealed class Row
        {
            public int vehicles, rested, shook, orderLeft, worstSecond, worstReversals;
            public double sumRest, maxRest, reversals, path, endDist;
            public readonly List<string> worst = new List<string>();
        }

        static readonly (string name, string type, int count, bool wet)[] Cases =
        {
            ("1 jeep to a dry tile at the water", "jeep", 1, false),
            ("1 light tank to a dry tile at the water", "light", 1, false),
            ("1 medium tank to a dry tile at the water", "medium", 1, false),
            ("3 jeeps, one order to a dry tile at the water", "jeep", 3, false),
            ("3 jeeps + 2 light tanks, one order to a water tile at the shore", "mix", 5, true),
        };

        static readonly (string level, string name)[] Maps = { ("L02", "Desert"), ("L16", "Jungle"), ("L17", "Car Park") };

        /// <summary>The whole check on the three maps. settings = default_settings, maps = the grid of a level ("L02", "L16",
        /// "L17"). Returns the report: per map its table and its six verdict lines.</summary>
        public static string Run(string settings, Func<string, string> maps, int points = Points)
        {
            var sb = new StringBuilder();
            foreach (var (level, name) in Maps)
            {
                string map;
                try { map = maps(level); } catch (Exception) { map = null; }
                if (string.IsNullOrEmpty(map)) { sb.AppendLine(OzV11Kit.Verdict(false, $"{name}: no grid found for level {level}")); continue; }
                sb.Append(RunMap(settings, map, name, points));
            }
            return sb.ToString();
        }

        /// <summary>The check on one map. settings = default_settings, map = the level's grid, name = the map's name for the report.</summary>
        public static string RunMap(string settings, string map, string name, int points = Points)
        {
            string bare = OzV11Kit.NoUnits(map);
            var sb = new StringBuilder();
            sb.AppendLine($"Vehicles ordered to the shore, {name} map without its start units, {points} shore points per row, {RunSeconds:0} s per run.");
            sb.AppendLine("row | rule | vehicles | at rest by 60 s | mean s to rest | worst s | shook (4+ reversals in one second) | most reversals in one second | reversals per vehicle | most by one vehicle | px driven per vehicle | px from the ordered point at the end | order still held at 60 s");
            bool had = OzV11Kit.TrySet(typeof(ZPathing), "ZoneSideCommit", true);
            int shookOff = 0, shookOn = 0, restOff = 0, restOn = 0, n = 0, worstOff = 0, worstOn = 0, mostOff = 0, mostOn = 0; double endOff = 0, endOn = 0; var notes = new List<string>();
            foreach (var c in Cases)
            {
                Row off = null, on = null;
                foreach (bool rule in new[] { false, true })
                {
                    if (!had && rule) continue;
                    OzV11Kit.TrySet(typeof(ZPathing), "ZoneSideCommit", rule);
                    var r = new Row();
                    var pts = ShorePoints(OzV11Kit.World(settings, bare, 1), points, c.wet);
                    for (int i = 0; i < pts.Count; i++) One(settings, bare, i + 1, pts[i], c.type, c.count, r);
                    if (rule) on = r; else off = r;
                    sb.AppendLine($"{c.name} | {(rule ? "on" : "off")} | {r.vehicles} | {r.rested} | {(r.rested > 0 ? r.sumRest / r.rested : 0):0.0} | {r.maxRest:0.0} | {r.shook} | {r.worstSecond} | {r.reversals / Math.Max(1, r.vehicles):0.0} | {r.worstReversals} | {r.path / Math.Max(1, r.vehicles):0} | {r.endDist / Math.Max(1, r.vehicles):0} | {r.orderLeft}");
                    foreach (var w in r.worst.Take(3)) notes.Add($"  {c.name}, rule {(rule ? "on" : "off")}: {w}");
                }
                if (off != null) { shookOff += off.shook; restOff += off.rested; endOff += off.endDist; n += off.vehicles; worstOff = Math.Max(worstOff, off.worstSecond); mostOff = Math.Max(mostOff, off.worstReversals); }
                if (on != null) { shookOn += on.shook; restOn += on.rested; endOn += on.endDist; worstOn = Math.Max(worstOn, on.worstSecond); mostOn = Math.Max(mostOn, on.worstReversals); }
            }
            OzV11Kit.TrySet(typeof(ZPathing), "ZoneSideCommit", true);
            if (!had) { sb.AppendLine($"{name}: the rule is not installed (ZPathing.ZoneSideCommit not found): only the 'off' rows were run."); return sb.ToString(); }
            sb.AppendLine("Vehicles that shook or never came to rest:");
            foreach (var l in notes) sb.AppendLine(l);
            sb.AppendLine(OzV11Kit.Verdict(shookOn * 10 <= shookOff * 6, $"{name}: fewer vehicles shake: at most 6 for every 10 before (off {shookOff}, on {shookOn} of {n})"));
            sb.AppendLine(OzV11Kit.Verdict(worstOn <= 30 && worstOn < worstOff, $"{name}: the worst shaking is gone: most reversals of one vehicle in one second at most 30 and below the value before (off {worstOff}, on {worstOn})"));
            sb.AppendLine(OzV11Kit.Verdict(mostOn <= 40 && mostOn < mostOff, $"{name}: every vehicle calms down: most reversals of one vehicle over its whole run at most 40 and below the value before (off {mostOff}, on {mostOn})"));
            sb.AppendLine(OzV11Kit.Verdict(shookOff >= 8 && worstOff >= 30, $"{name}: the rule off shows the fault (shook {shookOff}, worst second {worstOff}; under 8 or under 30 would mean this check no longer reaches the case)"));
            sb.AppendLine(OzV11Kit.Verdict(restOn >= restOff - 2, $"{name}: as many vehicles at rest by 60 s, give or take 2 (off {restOff}, on {restOn} of {n})"));
            sb.AppendLine(OzV11Kit.Verdict(endOn / Math.Max(1, n) <= endOff / Math.Max(1, n) + 8, $"{name}: they end as near the ordered point (mean px: off {endOff / Math.Max(1, n):0.0}, on {endOn / Math.Max(1, n):0.0}; at most 8 px more)"));
            return sb.ToString();
        }

        static void One(string settings, string bare, int seed, (int x, int y) goal, string type, int count, Row r)
        {
            var w = OzV11Kit.World(settings, bare, seed);
            OzV11Kit.Dummy(w, 40, 40);
            foreach (var f in w.Flags.ToList()) w.AwardZone(f, Team.Red);            // no flag to wander off to after the arrival
            var rng = new Random(seed * 7 + 3);
            var units = new List<ZObject>();
            for (int k = 0; k < count; k++)
            {
                if (!StartSpot(w, goal.x, goal.y, rng, out int sx, out int sy)) break;
                units.Add(OzV11Kit.Place(w, type == "mix" ? (k < 3 ? "jeep" : "light") : type, sx, sy, Team.Red));
            }
            if (units.Count == 0) return;
            for (int i = 0; i < 50; i++) { w.Tick(ZConst.TICK); w.Events.Clear(); }
            foreach (var u in units) OzV11Kit.PlayerMove(w, u, goal.x, goal.y);
            double t0 = w.Time;
            var m = units.Select(u => new OzV11Kit.Motion(u)).ToArray();
            while (w.Time - t0 < RunSeconds)
            {
                w.Tick(ZConst.TICK); w.Events.Clear();
                for (int i = 0; i < units.Count; i++) m[i].Step(units[i], w.Time - t0);
            }
            for (int i = 0; i < units.Count; i++)
            {
                var u = units[i];
                r.vehicles++;
                bool rest = RunSeconds - m[i].LastMove >= RestSeconds;
                if (rest) { r.rested++; double tr = Math.Max(0, m[i].LastMove); r.sumRest += tr; r.maxRest = Math.Max(r.maxRest, tr); }
                if (u.Waypoints.Count > 0) r.orderLeft++;
                bool shook = m[i].WorstSecond >= ShakeReversals;
                if (shook) r.shook++;
                r.worstSecond = Math.Max(r.worstSecond, m[i].WorstSecond); r.worstReversals = Math.Max(r.worstReversals, m[i].Reversals);
                r.reversals += m[i].Reversals; r.path += m[i].Path;
                r.endDist += Math.Sqrt((double)(u.CenterX - goal.x) * (u.CenterX - goal.x) + (double)(u.CenterY - goal.y) * (u.CenterY - goal.y));
                if (shook || !rest) r.worst.Add($"{u.Type} sent to tile ({goal.x >> 4},{goal.y >> 4}): {m[i].Reversals} reversals, {m[i].WorstSecond} in one second, {(rest ? "at rest after " + m[i].LastMove.ToString("0.0") + " s" : "still moving at 60 s")}, ends at px ({u.CenterX},{u.CenterY})");
            }
        }

        /// <summary>Shore points: dry tiles (ground / road) with water within one tile, or (wet) water tiles with dry ground within one
        /// tile; n of them spread evenly over the map in scan order.</summary>
        public static List<(int x, int y)> ShorePoints(ZWorld w, int n, bool wet)
        {
            var all = new List<(int x, int y)>();
            for (int ty = 2; ty < w.Map.height - 2; ty++)
                for (int tx = 2; tx < w.Map.width - 2; tx++)
                {
                    var c = w.Map.ClassAt(tx, ty);
                    bool dry = c == ZClass.Ground || c == ZClass.Road;
                    if (wet ? c != ZClass.Water : !dry) continue;
                    bool near = false;
                    for (int dy = -1; dy <= 1 && !near; dy++)
                        for (int dx = -1; dx <= 1; dx++)
                        {
                            var k = w.Map.ClassAt(tx + dx, ty + dy);
                            if (wet ? (k == ZClass.Ground || k == ZClass.Road) : k == ZClass.Water) { near = true; break; }
                        }
                    if (near) all.Add((tx * 16 + 8, ty * 16 + 8));
                }
            var pick = new List<(int x, int y)>();
            for (int i = 0; i < n && all.Count > 0; i++) pick.Add(all[(int)(((long)i * all.Count / n + all.Count / (2 * n)) % all.Count)]);
            return pick;
        }

        /// <summary>A free vehicle spot 9 to 16 tiles from the goal, in the vehicle-reachable area nearest the goal, on dry ground with a
        /// dry tile all round and no building or unit within 3 tiles.</summary>
        static bool StartSpot(ZWorld w, int gx, int gy, Random rng, out int sx, out int sy)
        {
            sx = sy = 0;
            var reach = w.Pathing.Reach;
            int comp = -1; long best = long.MaxValue;
            for (int ty = (gy >> 4) - 4; ty <= (gy >> 4) + 4; ty++)
                for (int tx = (gx >> 4) - 4; tx <= (gx >> 4) + 4; tx++)
                {
                    int c = reach.Comp(tx, ty, false, false); if (c < 0) continue;
                    long d = (long)(tx * 16 + 16 - gx) * (tx * 16 + 16 - gx) + (long)(ty * 16 + 16 - gy) * (ty * 16 + 16 - gy);
                    if (d < best) { best = d; comp = c; }
                }
            if (comp < 0) return false;
            for (int tries = 0; tries < 4000; tries++)
            {
                double ang = rng.NextDouble() * Math.PI * 2, r = 144 + rng.NextDouble() * 112;
                int x = gx + (int)(Math.Cos(ang) * r), y = gy + (int)(Math.Sin(ang) * r);
                int tx = (x - 8) >> 4, ty = (y - 8) >> 4;
                if (reach.Comp(tx, ty, false, false) != comp) continue;
                bool ok = true;
                for (int j = -1; j <= 2 && ok; j++) for (int i = -1; i <= 2; i++) { var k = w.Map.ClassAt(tx + i, ty + j); if (k != ZClass.Ground && k != ZClass.Road) { ok = false; break; } }
                if (!ok) continue;
                int cx = tx * 16 + 16, cy = ty * 16 + 16;
                if (w.Objects.Any(o => !o.Destroyed && o.Kind != ZKind.MapItem && Math.Abs(o.CenterX - cx) < 48 + o.WidthPix / 2 && Math.Abs(o.CenterY - cy) < 48 + o.HeightPix / 2)) continue;
                sx = cx; sy = cy; return true;
            }
            return false;
        }

#if UNITY_EDITOR
        [UnityEditor.MenuItem("OpenZ/V1.1/Count Vehicles At The Shore (b4)")]
        static void Menu() => OzV11Kit.RunMenu("b4 shore jitter", () => Run(OzV11Kit.SettingsText, OzV11Kit.MapText));
#endif
    }
}
