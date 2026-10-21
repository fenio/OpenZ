using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.Linq;
using System.Text;
using OpenZ.Sim;

namespace OpenZ.EditorTools
{
    /// <summary>
    /// Counted check of kit b16_spacing: what one rule tick costs when a large group gets ONE move order. Headless simulation on
    /// the three maps of version 1.1 (Jungle L16, Desert L02, Car Park L17), each with its start units and a CPU that gives no
    /// orders; 40 and 60 red units (three vehicles for every robot squad) stand on open ground 3 tiles apart and get one order to
    /// one place: near (a short move, the group arrives), far (a long move on its own side) and across the map (roads, bridges).
    /// Timed per 10 ms rule tick: 200 idle ticks, the first tick after the order, ticks 2 to 10, the next 300 ticks while the
    /// group moves. The first order of an editor session also compiles the code once; three orders are run before the count and
    /// not timed.
    /// A group order runs one of two rule sets (ZPathV3.cs SpreadGroupV3), named in the "group rule" column: "own spots" (every
    /// unit gets its spot, the path searches wait in a queue, at most 4 start per tick) or "live rules" (the clicked point lies at
    /// a bridge, a water crossing, a gate or a passage, or the way goes through one: every unit plans its path in the tick after
    /// the order). The verdict lines keep the two apart.
    /// The last column is a checksum of every object's position, health and orders after the run: the kit changes what the rules
    /// cost, not what they do, so one build of the game gives the same checksums with and without the kit.
    /// </summary>
    public static class OzV11MassOrderCount
    {
        public const int SettleTicks = 300, IdleTicks = 200, MovingTicks = 300;
        /// <summary>The order's numbers: no first tick after the order over FirstTickMs; while the group moves no frame longer than 1.5
        /// times the normal frame (23 ms in Play, 3 to 4 rule ticks a frame): 4 ticks may add 11.5 ms, MovingMeanMs a tick on average;
        /// and no single tick over MovingWorstMs.</summary>
        public const double FirstTickMs = 10, MovingMeanMs = 2.9, MovingWorstMs = 10;
        public const string ReportPath = "after_spread_tick_cost.txt";

        static readonly string[] VehicleTypes = { "jeep", "light", "medium", "heavy", "medium", "light", "apc", "missile_launcher" };
        static readonly string[] SquadTypes = { "grunt", "psycho", "tough", "sniper", "pyro", "laser" };

        /// <summary>Per level: its name, where the group stands (tile) and the three places it is ordered to (tile).</summary>
        public static readonly (string level, string name, int ax, int ay, (int x, int y) near, (int x, int y) far, (int x, int y) across)[] Levels =
        {
            ("L16", "Jungle", 70, 113, (62, 104), (93, 121), (40, 24)),
            ("L02", "Desert", 52, 80, (60, 68), (26, 80), (20, 12)),
            ("L17", "Car Park", 86, 60, (94, 44), (74, 100), (20, 30)),
        };
        /// <summary>Per level (same order): a place AT a zone (the near end of a bridge) and a place THROUGH it (beyond the bridge): the
        /// two orders that run the live rules, whose first path requests the kit's second form spreads over ticks.</summary>
        public static readonly ((int x, int y) at, (int x, int y) through)[] ZoneOrders =
        {
            ((71, 81), (71, 64)),
            ((52, 70), (52, 54)),
            ((64, 95), (49, 95)),
        };
        /// <summary>The second form's number: every body of the group is under way within StartSeconds of the order.</summary>
        public const double StartSeconds = 0.5;

        public sealed class Row
        {
            public string level, name, target; public int units, bodies, movingAtEnd, firstTickSearches; public bool live;
            public double idle, orderCall, first, next9, mean, worst; public ulong checksum;
            /// <summary>Seconds from the order until the last body left its place (bodies that never left it in the timed ticks: notStarted); maxStarts = most path requests started in one tick.</summary>
            public double lastStart, queueWait; public int notStarted, maxStarts;
        }

        /// <summary>n selectable units of one team round the anchor tile: on free 2x2 spots of dry ground with dry ground all round,
        /// 3 tiles apart, in the vehicle-reachable area of the anchor, nearest the anchor first; every fourth one a robot squad.</summary>
        public static List<ZObject> Spawn(ZWorld w, int n, int ax, int ay, Team team)
        {
            var reach = w.Pathing.Reach;
            int comp = -1;
            for (int r = 0; r < 12 && comp < 0; r++)
                for (int ty = ay - r; ty <= ay + r && comp < 0; ty++)
                    for (int tx = ax - r; tx <= ax + r && comp < 0; tx++)
                        comp = reach.Comp(tx, ty, false, false);
            var spots = new List<(long d, int tx, int ty)>();
            for (int ty = 2; ty < w.Map.height - 3; ty++)
                for (int tx = 2; tx < w.Map.width - 3; tx++)
                {
                    if ((tx - ax) % 3 != 0 || (ty - ay) % 3 != 0 || reach.Comp(tx, ty, false, false) != comp) continue;
                    bool ok = true;
                    for (int j = -1; j <= 2 && ok; j++) for (int i = -1; i <= 2; i++) { var k = w.Map.ClassAt(tx + i, ty + j); if (k != ZClass.Ground && k != ZClass.Road) { ok = false; break; } }
                    if (!ok) continue;
                    int cx = tx * 16 + 16, cy = ty * 16 + 16;
                    if (w.Objects.Any(o => !o.Destroyed && Math.Abs(o.CenterX - cx) < 40 + o.WidthPix / 2 && Math.Abs(o.CenterY - cy) < 40 + o.HeightPix / 2)) continue;
                    spots.Add(((long)(tx - ax) * (tx - ax) + (long)(ty - ay) * (ty - ay), tx, ty));
                }
            spots.Sort((a, b) => a.d != b.d ? a.d.CompareTo(b.d) : a.ty != b.ty ? a.ty.CompareTo(b.ty) : a.tx.CompareTo(b.tx));
            var units = new List<ZObject>();
            int v = 0, s = 0;
            for (int i = 0; i < n && i < spots.Count; i++)
            {
                int cx = spots[i].tx * 16 + 16, cy = spots[i].ty * 16 + 16;
                units.Add(i % 4 == 3 ? OzV11Kit.Place(w, SquadTypes[s++ % SquadTypes.Length], cx, cy, team) : OzV11Kit.Place(w, VehicleTypes[v++ % VehicleTypes.Length], cx, cy, team));
            }
            return units;
        }

        /// <summary>A level as the game starts it (L17 with its parked start vehicles), with a blue CPU that gives no orders.</summary>
        public static ZWorld World(string settings, string map, string level, int seed)
        {
            var w = ZWorld.Create(settings, map, seed, level == "L17");
            w.Bots.Add(new ZBot(w, Team.Blue, seed + 101) { Ignored = true });
            w.Events.Clear();
            return w;
        }

        /// <summary>The player's group order: the same move to one point for every selected unit, in one tick.</summary>
        public static void Order(ZWorld w, IEnumerable<ZObject> units, (int x, int y) tile)
        {
            foreach (var u in units) if (OzV11Kit.Alive(u)) OzV11Kit.PlayerMove(w, u, tile.x * 16 + 8, tile.y * 16 + 8);
        }

        /// <summary>Checksum of every object's position (with the sub-pixel carry), health, owner and orders.</summary>
        public static ulong Checksum(ZWorld w)
        {
            ulong h = 1469598103934665603UL;
            void Mix(long v) { h ^= (ulong)v; h *= 1099511628211UL; h ^= h >> 31; }
            Mix(w.Objects.Count);
            foreach (var o in w.Objects)
            {
                Mix(o.Id); Mix((long)o.Owner); Mix(o.LocX); Mix(o.LocY); Mix(BitConverter.DoubleToInt64Bits(o.XOver)); Mix(BitConverter.DoubleToInt64Bits(o.YOver));
                Mix(o.Health); Mix(o.Waypoints.Count);
                foreach (var p in o.Waypoints) { Mix((long)p.mode); Mix(p.refId); Mix(p.x); Mix(p.y); }
            }
            return h;
        }

        /// <summary>One run: the level, the group, the settle and idle ticks, the order, the timed ticks after it.</summary>
        public static Row One(string settings, string map, string level, string name, int n, string target, int ax, int ay, (int x, int y) goal, int movingTicks = MovingTicks)
        {
            var w = World(settings, map, level, 1);
            var units = Spawn(w, n, ax, ay, Team.Red);
            var r = new Row { level = level, name = name, target = target, units = units.Count, bodies = units.Sum(u => 1 + u.Minions.Count) };
            var sw = new Stopwatch();
            for (int t = 0; t < SettleTicks; t++) { w.Tick(ZConst.TICK); w.Events.Clear(); }
            GC.Collect();
            for (int t = 0; t < IdleTicks; t++) { sw.Restart(); w.Tick(ZConst.TICK); sw.Stop(); r.idle += sw.Elapsed.TotalMilliseconds; w.Events.Clear(); }
            r.idle /= IdleTicks;
            int live0 = w.Pathing.LiveGroupOrders, searches0 = w.Pathing.SearchesTotal;
            var bodies = units.SelectMany(u => OzV11Kit.Squad(u)).Where(b => b != null).ToList();
            var from = bodies.Select(b => (x: b.LocX, y: b.LocY)).ToArray(); var movedAt = new int[bodies.Count]; int tick = 0;
            void Moved() { tick++; for (int i = 0; i < bodies.Count; i++) if (movedAt[i] == 0 && (bodies[i].LocX != from[i].x || bodies[i].LocY != from[i].y)) movedAt[i] = tick; }
            w.Pathing.ResetSearchStats();
            sw.Restart(); Order(w, units, goal); sw.Stop(); r.orderCall = sw.Elapsed.TotalMilliseconds;
            sw.Restart(); w.Tick(ZConst.TICK); sw.Stop(); r.first = sw.Elapsed.TotalMilliseconds; w.Events.Clear(); Moved();
            r.live = w.Pathing.LiveGroupOrders > live0; r.firstTickSearches = w.Pathing.SearchesTotal - searches0;
            for (int t = 0; t < 9; t++) { sw.Restart(); w.Tick(ZConst.TICK); sw.Stop(); r.next9 = Math.Max(r.next9, sw.Elapsed.TotalMilliseconds); w.Events.Clear(); Moved(); }
            for (int t = 0; t < movingTicks; t++)
            {
                sw.Restart(); w.Tick(ZConst.TICK); sw.Stop(); double ms = sw.Elapsed.TotalMilliseconds; r.mean += ms; r.worst = Math.Max(r.worst, ms); w.Events.Clear(); Moved();
            }
            r.mean /= Math.Max(1, movingTicks);
            r.notStarted = movedAt.Count(m => m == 0); r.lastStart = movedAt.Length > 0 ? movedAt.Max() * ZConst.TICK : 0;
            r.maxStarts = w.Pathing.MaxStartsPerTick; r.queueWait = w.Pathing.MaxQueueWait;
            r.movingAtEnd = units.Count(u => OzV11Kit.Alive(u) && u.Waypoints.Count > 0);
            r.checksum = Checksum(w);
            return r;
        }

        /// <summary>The whole count. settings = default_settings, maps = the grid of a level ("L16", "L02", "L17").</summary>
        public static string Run(string settings, Func<string, string> maps)
        {
            var inv = CultureInfo.InvariantCulture;
            var sb = new StringBuilder();
            sb.AppendLine($"Rule tick cost when a large group gets ONE move order. Headless simulation, each level with its start units, the CPU gives no orders; the group stands on open ground 3 tiles apart ({SettleTicks} ticks to settle), {IdleTicks} idle ticks timed, the order, the first tick timed, ticks 2 to 10, then {MovingTicks} ticks timed while it moves. ms per 10 ms rule tick.");
            sb.AppendLine("level | units (bodies) | order to | group rule | idle tick | order call | first tick | worst of ticks 2 to 10 | next " + MovingTicks + ": mean | worst | path searches in the first tick | still on the order at the end | checksum of the end state | most path requests started in one tick | longest wait in the path queue, s | last body under way after, s (bodies that never left their place) | the same with the spread switched off");
            var rows = new List<Row>();
            var warm = Levels[0];
            string warmMap; try { warmMap = maps(warm.level); } catch (Exception) { warmMap = null; }
            if (!string.IsNullOrEmpty(warmMap))
                foreach (var (target, goal) in new[] { ("near", warm.near), ("far", warm.far), ("across", warm.across) }) One(settings, warmMap, warm.level, warm.name, 40, target, warm.ax, warm.ay, goal, 120);   // (compiles the code: not counted)
            foreach (var lv in Levels)
            {
                string map; try { map = maps(lv.level); } catch (Exception) { map = null; }
                if (string.IsNullOrEmpty(map)) { sb.AppendLine(OzV11Kit.Verdict(false, $"{lv.name}: no grid found for level {lv.level}")); continue; }
                var zo = ZoneOrders[Array.IndexOf(Levels, lv)];
                foreach (int n in new[] { 40, 60 })
                    foreach (var (target, goal) in new[] { ("near", lv.near), ("far", lv.far), ("across", lv.across), ("at a zone", zo.at), ("through a zone", zo.through) })
                    {
                        var r = One(settings, map, lv.level, lv.name, n, target, lv.ax, lv.ay, goal);
                        rows.Add(r);
                        string off = "-";
                        if (r.live && ZPathing.LiveOrderSpread)
                        {   // the same order with the spread switched off: when the last body got under way before the kit's second form
                            ZPathing.LiveOrderSpread = false;
                            try { var b = One(settings, map, lv.level, lv.name, n, target, lv.ax, lv.ay, goal, 120); off = b.lastStart.ToString("0.00", inv) + " s (" + b.notStarted + "), first tick " + b.first.ToString("0.0", inv) + " ms with " + b.firstTickSearches + " searches"; }
                            finally { ZPathing.LiveOrderSpread = true; }
                        }
                        sb.AppendLine(string.Format(inv, "{0} {1} | {2} ({3}) | {4}, tile {5},{6} | {7} | {8:0.000} | {9:0.00} | {10:0.00} | {11:0.00} | {12:0.000} | {13:0.00} | {14} | {15} of {16} | {17:x16} | {18} | {19:0.00} | {20:0.00} ({21}) | {22}",
                            r.name, r.level, r.units, r.bodies, target, goal.x, goal.y, r.live ? "live rules" : "own spots", r.idle, r.orderCall, r.first, r.next9, r.mean, r.worst, r.firstTickSearches, r.movingAtEnd, r.units, r.checksum, r.maxStarts, r.queueWait, r.lastStart, r.notStarted, off));
                    }
            }
            foreach (var lv in Levels)
                foreach (int n in new[] { 40, 60 })
                {
                    var set = rows.Where(r => r.level == lv.level && r.units == n).ToList();
                    if (set.Count == 0) continue;
                    var own = set.Where(r => !r.live).ToList(); var live = set.Where(r => r.live).ToList();
                    string Txt(List<Row> l, Func<Row, double> f, string fmt) => string.Join(", ", l.Select(r => r.target + " " + f(r).ToString(fmt, inv)));
                    if (own.Count > 0) sb.AppendLine(OzV11Kit.Verdict(own.All(r => r.first <= FirstTickMs), $"{lv.name}, {n} units, orders with own spots: no first tick over {FirstTickMs:0} ms ({Txt(own, r => r.first, "0.0")})"));
                    if (live.Count > 0) sb.AppendLine(OzV11Kit.Verdict(live.All(r => r.first <= FirstTickMs), $"{lv.name}, {n} units, orders with the live rules: no first tick over {FirstTickMs:0} ms ({string.Join(", ", live.Select(r => r.target + " " + r.first.ToString("0.0", inv) + " with " + r.firstTickSearches + " path searches in it"))})"));
                    sb.AppendLine(OzV11Kit.Verdict(set.All(r => r.mean <= MovingMeanMs), $"{lv.name}, {n} units: a tick while the group moves costs at most {MovingMeanMs.ToString("0.0", inv)} ms on average, 4 ticks less than half a 23 ms frame ({Txt(set, r => r.mean, "0.00")}: at most {(4 * set.Max(r => r.mean)).ToString("0.0", inv)} ms a frame of 4 ticks; idle {set.Average(r => r.idle).ToString("0.00", inv)})"));
                    sb.AppendLine(OzV11Kit.Verdict(set.All(r => Math.Max(r.worst, r.next9) <= MovingWorstMs), $"{lv.name}, {n} units: no tick over {MovingWorstMs:0} ms after the first ({Txt(set, r => Math.Max(r.worst, r.next9), "0.0")})"));
                    var zone = set.Where(r => r.target == "at a zone" || r.target == "through a zone").ToList();
                    if (zone.Count > 0)
                    {
                        sb.AppendLine(OzV11Kit.Verdict(zone.All(r => r.live), $"{lv.name}, {n} units: the orders at and through a zone run the live rules ({string.Join(", ", zone.Select(r => r.target + (r.live ? " yes" : " NO: own spots")))})"));
                        sb.AppendLine(OzV11Kit.Verdict(zone.All(r => r.maxStarts <= ZPathing.SearchCap), $"{lv.name}, {n} units, at and through a zone: at most {ZPathing.SearchCap} path requests started in one tick ({Txt(zone, r => r.maxStarts, "0")}; searches in the first tick {Txt(zone, r => r.firstTickSearches, "0")})"));
                        sb.AppendLine(OzV11Kit.Verdict(zone.All(r => Math.Max(r.first, Math.Max(r.worst, r.next9)) <= FirstTickMs), $"{lv.name}, {n} units, at and through a zone: no rule tick over {FirstTickMs:0} ms ({Txt(zone, r => Math.Max(r.first, Math.Max(r.worst, r.next9)), "0.0")})"));
                        sb.AppendLine(OzV11Kit.Verdict(zone.All(r => r.lastStart <= StartSeconds && r.notStarted == 0), $"{lv.name}, {n} units, at and through a zone: every body under way within {StartSeconds.ToString("0.0", inv)} s of the order ({string.Join(", ", zone.Select(r => r.target + " " + r.lastStart.ToString("0.00", inv) + " s" + (r.notStarted > 0 ? ", " + r.notStarted + " never left their place" : "")))}; longest wait in the path queue {Txt(zone, r => r.queueWait, "0.00")} s)"));
                    }
                }
            return sb.ToString();
        }

#if UNITY_EDITOR
        const string SessionKey = "OpenZ.V11.MassOrderCount";   // 0 = not run in this editor session, 1 = running, 2 = ran

        [UnityEditor.MenuItem("OpenZ/V1.1/Count Mass Order Tick Cost (b16)")]
        static void Menu()
        {
            if (UnityEngine.Application.isPlaying) { UnityEngine.Debug.LogWarning("[OpenZ V1.1] b16 mass order tick cost: leave Play mode first (the check sets the simulation's static switches)."); return; }
            int state = UnityEditor.SessionState.GetInt(SessionKey, 0);
            if (state != 0) { UnityEngine.Debug.Log("[OpenZ V1.1] b16 mass order tick cost: " + (state == 1 ? "is running" : "has run") + " in this editor session, nothing done. Its report: " + ReportPath); return; }
            UnityEditor.SessionState.SetInt(SessionKey, 1);
            try
            {
                OzV11Kit.RunMenu("b16 mass order tick cost", () =>
                {
                    string report = Run(OzV11Kit.SettingsText, OzV11Kit.MapText);
                    System.IO.Directory.CreateDirectory(System.IO.Path.GetDirectoryName(ReportPath));
                    System.IO.File.WriteAllText(ReportPath, report);
                    return report + "written to " + ReportPath;
                });
            }
            finally { UnityEditor.SessionState.SetInt(SessionKey, 2); }
        }
#endif
    }
}
