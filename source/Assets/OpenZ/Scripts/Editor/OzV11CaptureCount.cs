using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using OpenZ.Sim;

namespace OpenZ.EditorTools
{
    /// <summary>
    /// Counted check of kit b3_auto_capture, every row with the rule off (the game before version 1.1) and on, no capture order
    /// ever given. Three groups of rows:
    /// <para>A. 12 red robots (the player's) and one free flag or one empty jeep on flat open ground. "Claim" = a squad leader
    /// holds an own (not player-given) waypoint on the target. Counted: runs in which the target was taken, the time from the
    /// first unit able to act to the first claim and to the capture, runs with two claimants at once, and for the rows with a
    /// move order how many leaders still reached the ordered point.</para>
    /// <para>B. A way round: idle units that see the target across water they cannot cross (a water strip for vehicles, deep
    /// water for robots, the moat of the Desert map's island flag). The rule must not give up a unit that is on its way: taken
    /// as often as with the rule off, mean time at most 5 s worse.</para>
    /// <para>C. A march past a row of empty vehicles or guns (open ground, and the Car Park map's own parked vehicles): at most
    /// one robot of a squad leaves per order, the others end at the ordered point, a gun is never taken out of a march.</para>
    /// The calm start (DV69, the first 20 s of a game) is switched off for the check.
    /// </summary>
    public static class OzV11CaptureCount
    {
        public const int Runs = 20;
        const int CX = 640, CY = 640;

        enum Kind { Standing, HeldGroup, Boxed, OnTheWay, Retreat }

        sealed class Stat
        {
            public int runs, captured, claimIn1s, never, multi, arrived, leaders, fails, stalls; public double sumClaim, maxClaim, sumCap, maxCap; public long claimants;
            public readonly List<string> notes = new List<string>();
        }

        static readonly (string name, Kind kind, bool flag, bool squads, int off)[] Rows =
        {
            ("flag, 12 single robots stand 40-200 px round it", Kind.Standing, true, false, 0),
            ("flag, 5 squads (12 robots) stand 40-200 px round it", Kind.Standing, true, true, 0),
            ("empty jeep, 12 single robots stand 40-200 px round it", Kind.Standing, false, false, 0),
            ("empty jeep, 5 squads stand 40-200 px round it", Kind.Standing, false, true, 0),
            ("flag, the closest robot is shut in by 8 guns of its team, 11 more stand 100-200 px away", Kind.Boxed, true, false, 0),
            ("empty jeep, the closest robot is shut in by 8 guns of its team, 11 more stand 100-200 px away", Kind.Boxed, false, false, 0),
            ("empty jeep, 12 single robots arrive on one move order that ends 150 px short of it", Kind.HeldGroup, false, false, 150),
            ("empty jeep, 5 squads arrive on one move order that ends 150 px short of it", Kind.HeldGroup, false, true, 150),
            ("flag, 5 squads arrive on one move order that ends 150 px short of it", Kind.HeldGroup, true, true, 150),
            ("flag 64 px beside the way of 12 single robots (one move order past it)", Kind.OnTheWay, true, false, 64),
            ("flag 64 px beside the way of 5 squads", Kind.OnTheWay, true, true, 64),
            ("empty jeep 64 px beside the way of 12 single robots", Kind.OnTheWay, false, false, 64),
            ("empty jeep 64 px beside the way of 5 squads", Kind.OnTheWay, false, true, 64),
            ("flag 64 px beside the way of 5 squads on a move WITHOUT the attack-move flag (a retreat)", Kind.Retreat, true, true, 64),
        };

        /// <summary>B rows: (name, level or null = open ground with a strip of water, deep = robots cannot wade it, flag or an empty
        /// jeep, the idle units, how many, half the strip's length in tiles).</summary>
        static readonly (string name, string level, bool deep, bool flag, string unit, int count, int half)[] Round =
        {
            ("flag across a water strip, 1 idle light tank 176 px from it, about 20 tiles round", null, false, true, "light", 1, 10),
            ("flag across a water strip, 3 idle light tanks 176-200 px from it, about 20 tiles round", null, false, true, "light", 3, 10),
            ("flag across a water strip, 1 idle jeep 176 px from it, about 12 tiles round", null, false, true, "jeep", 1, 6),
            ("flag across a water strip, 1 idle light tank 176 px from it, about 8 tiles round", null, false, true, "light", 1, 4),
            ("flag across deep water, 2 idle grunt squads 176-200 px from it, about 16 tiles round", null, true, true, "grunt", 2, 8),
            ("empty jeep across deep water, 2 idle grunt squads 176-200 px from it, about 16 tiles round", null, true, false, "grunt", 2, 8),
            ("Desert map, the island flag at tile (39,40): 1 idle light tank 8 tiles east of it across the moat (the way is over the east bridge)", "L02", false, true, "light", 1, 0),
            ("Desert map, the island flag at tile (39,40): 3 idle jeeps 8-9 tiles east of it across the moat", "L02", false, true, "jeep", 3, 0),
        };

        /// <summary>C rows: (name, level or null = open ground, the squads on one move order, the empty things beside the way,
        /// how many, guns).</summary>
        static readonly (string name, string level, string[] squads, string empty, int count, bool gun)[] Chain =
        {
            ("1 grunt squad on one move order past 4 empty jeeps 56 px beside its way", null, new[] { "grunt" }, "jeep", 4, false),
            ("2 squads (grunt, psycho) on one move order past 6 empty light tanks 56 px beside their way", null, new[] { "grunt", "psycho" }, "light", 6, false),
            ("1 Tough squad on one move order past 3 empty gatling guns 56 px beside its way", null, new[] { "tough" }, "gatling", 3, true),
            ("Car Park map, 1 grunt squad on one move order through the west car park, tile (37,117) to (37,72), past its own parked vehicles", "L17", new[] { "grunt" }, null, 0, false),
        };

        /// <summary>The whole check. settings = default_settings, maps = the grid of a level ("L02", "L17"; null = the rows on the
        /// level maps are left out). Returns the report.</summary>
        public static string Run(string settings, Func<string, string> maps, int runs = Runs)
        {
            var sb = new StringBuilder();
            sb.AppendLine($"A. 12 red robots and one free flag or empty jeep, flat open ground, {runs} runs per row, the calm start (DV69) switched off for the check.");
            sb.AppendLine("row | rule | target taken | first claim within 1 s | never claimed | mean s to the first claim | worst | mean s to the capture | worst | runs with 2 claimants at once | claimants per run | leaders at the ordered point at the end | tries that failed / stalled | first runs without a capture");
            bool had = OzV11Kit.TrySet(typeof(ZWorld), "GrabCertainDeviation", true);
            ZWorld.CalmStartDeviation = false;
            int multiOn = 0, missOn = 0, lateOn = 0, faultOff = 0, retreatGrabs = 0, strayOn = 0, wayRuns = 0; double worstBoxed = 0;
            foreach (var row in Rows)
            {
                Stat off = null, on = null;
                foreach (bool rule in new[] { false, true })
                {
                    if (rule && !had) continue;
                    OzV11Kit.TrySet(typeof(ZWorld), "GrabCertainDeviation", rule);
                    var st = new Stat(); Count(st, true);
                    for (int seed = 1; seed <= runs; seed++) One(settings, seed, row.kind, row.flag, row.squads, row.off, st);
                    Count(st, false);
                    if (rule) on = st; else off = st;
                    int got = st.runs - st.never;
                    sb.AppendLine($"{row.name} | {(rule ? "on" : "off")} | {st.captured} of {st.runs} | {st.claimIn1s} | {st.never} | {(got > 0 ? st.sumClaim / got : 0):0.00} | {st.maxClaim:0.00} | {(st.captured > 0 ? st.sumCap / st.captured : 0):0.0} | {st.maxCap:0.0} | {st.multi} | {(double)st.claimants / st.runs:0.0} | {(row.kind == Kind.Standing || row.kind == Kind.Boxed ? "-" : st.arrived + " of " + st.leaders)} | {st.fails} / {st.stalls} | {string.Join("; ", st.notes.Take(2))}");
                }
                if (off != null && row.kind != Kind.Retreat && row.kind != Kind.Standing && off.captured < off.runs) faultOff++;
                if (on == null) continue;
                multiOn += on.multi;
                if (row.kind == Kind.Retreat) { retreatGrabs += on.runs - on.never; continue; }
                missOn += on.runs - on.captured;
                if (row.kind == Kind.Standing) lateOn += on.runs - on.claimIn1s;
                if (row.kind == Kind.Boxed) worstBoxed = Math.Max(worstBoxed, on.maxCap);
                if (row.kind == Kind.OnTheWay || row.kind == Kind.HeldGroup) { strayOn += on.leaders - on.arrived; wayRuns += on.leaders; }
            }

            // B. a way round
            sb.AppendLine($"B. A way round: idle units that see the target across water they cannot cross, {runs} runs per row, 150 s per run.");
            sb.AppendLine("row | rule | target taken | mean s to the capture | worst | tries that failed / stalled");
            int roundWorse = 0, roundRows = 0, roundStalls = 0; var worse = new List<string>();
            foreach (var row in Round)
            {
                if (row.level != null && maps == null) { sb.AppendLine($"{row.name} | left out (no level maps given)"); continue; }
                Stat off = null, on = null;
                foreach (bool rule in new[] { false, true })
                {
                    if (rule && !had) continue;
                    OzV11Kit.TrySet(typeof(ZWorld), "GrabCertainDeviation", rule);
                    var st = new Stat(); Count(st, true);
                    for (int seed = 1; seed <= runs; seed++) WayRound(settings, maps, seed, row.level, row.deep, row.flag, row.unit, row.count, row.half, st);
                    Count(st, false);
                    if (rule) on = st; else off = st;
                    sb.AppendLine($"{row.name} | {(rule ? "on" : "off")} | {st.captured} of {st.runs} | {(st.captured > 0 ? st.sumCap / st.captured : 0):0.0} | {st.maxCap:0.0} | {st.fails} / {st.stalls}");
                }
                if (on == null) continue;
                roundRows++; roundStalls += on.stalls;
                double mOff = off.captured > 0 ? off.sumCap / off.captured : 0, mOn = on.captured > 0 ? on.sumCap / on.captured : 0;
                if (on.captured < off.captured || mOn > mOff + 5.0) { roundWorse++; worse.Add(row.name); }
            }

            // C. a march past a row of empty vehicles or guns
            sb.AppendLine($"C. One move order past a row of empty vehicles or guns, {runs} runs per row. Gone = robots of a squad that are no longer on foot at the end.");
            sb.AppendLine("row | rule | vehicles or guns manned per run | runs with exactly one manned | most robots of one squad gone | robots on foot within 120 px of the ordered point at the end | robots on foot elsewhere");
            int jeepRuns = 0, jeepOne = 0, worstGone = 0, elsewhere = 0, onFoot = 0, gunsManned = 0, chainRows = 0;
            for (int r = 0; r < Chain.Length; r++)
            {
                var row = Chain[r];
                if (row.level != null && maps == null) { sb.AppendLine($"{row.name} | left out (no level maps given)"); continue; }
                foreach (bool rule in new[] { false, true })
                {
                    if (rule && !had) continue;
                    OzV11Kit.TrySet(typeof(ZWorld), "GrabCertainDeviation", rule);
                    int manned = 0, one = 0, gone = 0, at = 0, away = 0;
                    for (int seed = 1; seed <= runs; seed++)
                    {
                        March(settings, maps, seed, row.level, row.squads, row.empty, row.count, row.gun, out int m, out int g, out int a, out int e);
                        manned += m; if (m == 1) one++; gone = Math.Max(gone, g); at += a; away += e;
                    }
                    sb.AppendLine($"{row.name} | {(rule ? "on" : "off")} | {(double)manned / runs:0.00} | {one} of {runs} | {gone} | {at} | {away}");
                    if (!rule) continue;
                    chainRows++; worstGone = Math.Max(worstGone, gone); elsewhere += away; onFoot += at + away;
                    if (r == 0) { jeepRuns = runs; jeepOne = one; }
                    if (row.gun) gunsManned += manned;
                }
            }

            OzV11Kit.TrySet(typeof(ZWorld), "GrabCertainDeviation", true);
            if (!had) { sb.AppendLine("The rule is not installed (ZWorld.GrabCertainDeviation not found): only the 'off' rows were run."); return sb.ToString(); }
            sb.AppendLine(OzV11Kit.Verdict(missOn == 0, $"the target is taken in every run with the rule on (runs without a capture: {missOn})"));
            sb.AppendLine(OzV11Kit.Verdict(lateOn == 0, $"units standing round it: one claims within 1 s in every run (late or never: {lateOn})"));
            sb.AppendLine(OzV11Kit.Verdict(multiOn == 0, $"never two claimants at once (runs: {multiOn})"));
            sb.AppendLine(OzV11Kit.Verdict(worstBoxed <= 30, $"closest unit shut in: the next one has it within 30 s (worst {worstBoxed:0.0} s)"));
            sb.AppendLine(OzV11Kit.Verdict(retreatGrabs == 0, $"a move without the attack-move flag never turns off (claims in the retreat row: {retreatGrabs})"));
            sb.AppendLine(OzV11Kit.Verdict(strayOn * 20 <= wayRuns, $"the order still comes first: leaders that did not end within 120 px of the ordered point: {strayOn} of {wayRuns} (at most 5 %; the one that got into the jeep is not counted)"));
            sb.AppendLine(OzV11Kit.Verdict(faultOff >= 5, $"the rule off shows the faults (rows of the three fault kinds with a run without a capture: {faultOff} of 9; under 5 would mean this check no longer reaches them)"));
            sb.AppendLine(OzV11Kit.Verdict(roundRows > 0 && roundWorse == 0, $"a way round is walked, not given up: in each of the {roundRows} way-round rows the target is taken with the rule on at least as often as off and the mean time is at most 5 s worse (rows worse: {roundWorse}{(worse.Count > 0 ? ": " + string.Join("; ", worse) : "")}; tries the rule called stalled in these rows: {roundStalls})"));
            sb.AppendLine(OzV11Kit.Verdict(chainRows > 0 && jeepOne == jeepRuns && worstGone <= 1 && elsewhere * 20 <= onFoot, $"past a row of empty vehicles one robot of a squad leaves per order and the others arrive: exactly one jeep manned in {jeepOne} of {jeepRuns} runs of the jeep row, most robots of one squad gone {worstGone} (at most 1), robots on foot that did not end within 120 px of the ordered point {elsewhere} of {onFoot} (at most 5 %)"));
            sb.AppendLine(OzV11Kit.Verdict(gunsManned == 0, $"a gun is never taken out of a march (guns manned in the Tough row: {gunsManned})"));
            return sb.ToString();
        }

        /// <summary>The rule's own counters over a row: start (begin = true) and difference (begin = false).</summary>
        static void Count(Stat st, bool begin)
        {
            int f = OzV11Kit.Counter(typeof(ZWorld), "GrabFails"), s = OzV11Kit.Counter(typeof(ZWorld), "GrabStalls");
            if (begin) { st.fails = -f; st.stalls = -s; } else { st.fails += f; st.stalls += s; }
        }

        /// <summary>12 robots: singles (4 grunts, 3 psychos, 2 Toughs, 2 snipers, 1 Electrobot as twelve one-robot squads) or five full
        /// squads (grunt 3, grunt 3, psycho 3, Tough 2, Electrobot 1).</summary>
        static List<ZObject> Twelve(ZWorld w, bool squads, Func<int, (int x, int y)> pos)
        {
            var l = new List<ZObject>();
            string[] t = squads ? new[] { "grunt", "grunt", "psycho", "tough", "electrobot" }
                                : new[] { "grunt", "psycho", "tough", "sniper", "grunt", "psycho", "grunt", "tough", "sniper", "psycho", "grunt", "electrobot" };
            for (int i = 0; i < t.Length; i++) { var (x, y) = pos(i); l.Add(OzV11Kit.Place(w, t[i], x, y, Team.Red, squads ? -1 : 1)); }
            return l;
        }

        static void One(string settings, int seed, Kind kind, bool flag, bool squads, int off, Stat st)
        {
            var w = flag ? OzV11Kit.World(settings, OzV11Kit.Plain(80, 80, ("map_item:flag", CX / 16, CY / 16, "null")), seed)
                         : OzV11Kit.World(settings, OzV11Kit.Plain(80, 80), seed);
            ZObject target = flag ? w.Flags[0] : w.CreateObject("vehicle", "jeep", CX - 16, CY - 16, Team.Neutral);
            OzV11Kit.Dummy(w, 1230, 1230);
            var rng = new Random(seed * 977 + 13);
            List<ZObject> units; double t0 = -1; int gx = 0, gy = 0; bool ordered = false;
            int tx = target.CenterX, ty = target.CenterY;
            if (kind == Kind.Standing)
            {
                int n = squads ? 5 : 12;
                units = Twelve(w, squads, i => { double ang = i * Math.PI * 2 / n + rng.NextDouble() * 0.5, r = 40 + rng.NextDouble() * 160; return (tx + (int)(Math.Cos(ang) * r), ty + (int)(Math.Sin(ang) * r)); });
                t0 = w.Time;
            }
            else if (kind == Kind.Boxed)
            {
                int bx = tx + 70, by = ty;
                for (int k = 0; k < 8; k++) { double ang = k * Math.PI / 4; OzV11Kit.Place(w, "gatling", bx + (int)Math.Round(Math.Cos(ang) * 34), by + (int)Math.Round(Math.Sin(ang) * 34), Team.Red); }
                units = Twelve(w, false, i => { if (i == 0) return (bx, by); double ang = i * Math.PI * 2 / 11 + rng.NextDouble() * 0.3, r = 100 + rng.NextDouble() * 100; return (tx - 30 + (int)(Math.Cos(ang) * r), ty + (int)(Math.Sin(ang) * r)); });
                t0 = w.Time;
            }
            else if (kind == Kind.HeldGroup)
            {
                units = Twelve(w, squads, i => (CX - 480 - (i % 4) * 26 + rng.Next(6), CY - 30 + (i / 4) * 30 + rng.Next(6)));
                gx = tx - off; gy = ty;                                                          // the group stops short of it: nobody passes within 80 px
                foreach (var u in units) OzV11Kit.PlayerMove(w, u, gx, gy);
                ordered = true;
            }
            else
            {
                units = Twelve(w, squads, i => (CX - 420 - i * 30 + rng.Next(6), CY + off - 4 + rng.Next(8)));   // a column on the line of the order: nobody walks over the target
                gx = CX + 420; gy = CY + off;
                foreach (var u in units) OzV11Kit.PlayerMove(w, u, gx, gy, kind != Kind.Retreat);
                ordered = true; t0 = w.Time;
            }
            double tClaim = -1, tCap = -1; var claimed = new HashSet<int>(); bool multi = false;
            double cap = kind == Kind.OnTheWay || kind == Kind.Retreat ? 150 : 90, end = t0 >= 0 ? t0 + cap : double.MaxValue;
            while (w.Time < end && w.Time < 400)
            {
                w.Tick(ZConst.TICK); w.Events.Clear();
                var live = w.Objects.Where(o => o.Owner == Team.Red && o.IsRobot && !o.IsMinion && OzV11Kit.Alive(o)).ToList();
                if (t0 < 0 && (live.Any(o => o.Waypoints.Count == 0 && OzV11Kit.Dist(o, target) <= 300) || w.Time > 200)) { t0 = w.Time; end = t0 + cap; }
                int n = 0;
                foreach (var o in live)
                    if (o.Waypoints.Any(q => q.refId == target.Id && !q.playerGiven && (q.mode == WpMode.Move || q.mode == WpMode.Enter))) { n++; claimed.Add(o.Id); if (tClaim < 0) tClaim = Math.Max(0, w.Time - Math.Max(0, t0)); }
                if (n >= 2) multi = true;
                if (tCap < 0 && target.Owner == Team.Red) { tCap = w.Time - Math.Max(0, t0); if (!ordered) break; }
                if (ordered && tCap >= 0 && live.All(o => o.Waypoints.Count == 0)) break;       // captured and everybody has finished the order
            }
            st.runs++;
            if (tClaim < 0) st.never++; else { st.sumClaim += tClaim; st.maxClaim = Math.Max(st.maxClaim, tClaim); if (tClaim <= 1.0) st.claimIn1s++; }
            if (tCap >= 0) { st.captured++; st.sumCap += tCap; st.maxCap = Math.Max(st.maxCap, tCap); }
            else if (kind != Kind.Retreat)
                st.notes.Add($"seed {seed}: " + string.Join(", ", w.Objects.Where(o => o.Owner == Team.Red && o.IsRobot && !o.IsMinion && OzV11Kit.Alive(o)).OrderBy(o => OzV11Kit.Dist(o, target)).Take(2)
                    .Select(o => $"{o.Type} {OzV11Kit.Dist(o, target):0} px away, {(o.Waypoints.Count == 0 ? "no order" : o.Waypoints[0].mode.ToString())}{(o.PlayerHold ? ", held" : "")}{(o.PathUnreachable(target) ? ", gave it up" : "")}")));
            if (multi) st.multi++;
            st.claimants += claimed.Count;
            if (ordered)
                foreach (var o in w.Objects.Where(o => o.Owner == Team.Red && o.IsRobot && !o.IsMinion && OzV11Kit.Alive(o)))
                { st.leaders++; if (Math.Sqrt((double)(o.CenterX - gx) * (o.CenterX - gx) + (double)(o.CenterY - gy) * (o.CenterY - gy)) <= 120) st.arrived++; }
        }

        // ------------------------------------------------------------------ B. a way round
        /// <summary>A level map for the rows on the real grids: terrain and buildings (nobody's), no robots, guns or owned vehicles;
        /// only the flag at tile (flagX, flagY) stays (none by default), the unowned parked vehicles stay when keepParked.</summary>
        static string Level(string map, bool keepParked, int flagX = -1, int flagY = -1)
        {
            var m = ZMapData.FromJson(map);
            m.objects.RemoveAll(o =>
            {
                string c = o.kind.Split(':')[0];
                return c == "robot" || c == "cannon" || (c == "vehicle" && !(keepParked && o.owner == "null")) || (o.kind == "map_item:flag" && !(o.x == flagX && o.y == flagY)) || o.kind == "map_item:grenades";
            });
            foreach (var o in m.objects) if (o.owner == "blue") o.owner = "null";
            return UnityEngine.JsonUtility.ToJson(m);
        }

        /// <summary>Open ground 80 x 80 with a strip of water two tiles wide, 5 tiles east of the target, 2 x half + 1 tiles long:
        /// plain water (vehicles cannot cross, robots wade) or deep water (nobody crosses). Sight goes over both.</summary>
        static string Strip(bool deep, bool flag, int half)
        {
            var m = ZMapData.FromJson(flag ? OzV11Kit.Plain(80, 80, ("map_item:flag", CX / 16, CY / 16, "null")) : OzV11Kit.Plain(80, 80));
            for (int ty = CY / 16 - half; ty <= CY / 16 + half; ty++)
                for (int tx = CX / 16 + 5; tx <= CX / 16 + 6; tx++) m.classes[ty * m.width + tx] = (int)(deep ? ZClass.DeepWater : ZClass.Water);
            return UnityEngine.JsonUtility.ToJson(m);
        }

        static void WayRound(string settings, Func<string, string> maps, int seed, string level, bool deep, bool flag, string unit, int count, int half, Stat st)
        {
            var w = OzV11Kit.World(settings, level != null ? Level(maps(level), false, 39, 40) : Strip(deep, flag, half), seed);
            ZObject target = flag ? w.Flags[0] : w.CreateObject("vehicle", "jeep", CX - 16, CY - 16, Team.Neutral);
            if (level != null) OzV11Kit.Dummy(w, 40, 40); else OzV11Kit.Dummy(w, 1230, 1230);
            var rng = new Random(seed * 613 + 7);
            int tx = target.CenterX, ty = target.CenterY;
            for (int k = 0; k < count; k++)
            {
                int x, y;
                if (level != null) { x = (k == 0 ? 47 : 48) * 16 + 8; y = (k == 0 ? 40 : k == 1 ? 38 : 42) * 16 + 8; }        // dry ground east of the moat, 8-9 tiles from the flag
                else { x = tx + (k == 0 ? 176 : 190) + rng.Next(5); y = ty + (k == 0 ? 0 : k == 1 ? -44 : 44) + rng.Next(5); }
                OzV11Kit.Place(w, unit, x, y, Team.Red);
            }
            double tCap = -1;
            while (w.Time < 150 && tCap < 0)
            {
                w.Tick(ZConst.TICK); w.Events.Clear();
                if (target.Owner == Team.Red) tCap = w.Time;
            }
            st.runs++;
            if (tCap >= 0) { st.captured++; st.sumCap += tCap; st.maxCap = Math.Max(st.maxCap, tCap); }
        }

        // ------------------------------------------------------------------ C. a march past a row of empty vehicles or guns
        static void March(string settings, Func<string, string> maps, int seed, string level, string[] squads, string empty, int count, bool gun,
                          out int manned, out int worstGone, out int arrived, out int elsewhere)
        {
            ZWorld w; var empties = new List<ZObject>(); int sx, sy, gx, gy;
            if (level == null)
            {
                w = OzV11Kit.World(settings, OzV11Kit.Plain(80, 80), seed);
                sx = 120; sy = CY + 56; gx = 160 + count * 100 + 300; gy = sy;                   // the order ends 300 px past the last one: nobody stands within 220 px of an empty one at the end
                for (int k = 0; k < count; k++) empties.Add(OzV11Kit.Place(w, empty, 260 + k * 100, CY, Team.Neutral));
                OzV11Kit.Dummy(w, 1230, 1230);
            }
            else
            {
                w = OzV11Kit.World(settings, Level(maps(level), true), seed);                     // the map's own parked vehicles, nobody's and empty
                empties.AddRange(w.Objects.Where(o => o.Kind == ZKind.Vehicle && o.Owner == Team.Neutral));
                sx = 37 * 16 + 8; sy = 117 * 16 + 8; gx = 37 * 16 + 8; gy = 72 * 16 + 8;        // up the aisle of the west car park to open ground 19 tiles north of the last vehicle
                OzV11Kit.Dummy(w, 40, 40);
            }
            var rng = new Random(seed * 331 + 5);
            var members = new List<List<ZObject>>();
            for (int i = 0; i < squads.Length; i++)
            {
                var s = OzV11Kit.Place(w, squads[i], sx - i * 44 + rng.Next(5), sy + rng.Next(5), Team.Red);
                members.Add(OzV11Kit.Squad(s).ToList());
            }
            foreach (var m in members) OzV11Kit.PlayerMove(w, m[0], gx, gy);
            double idleSince = -1;
            while (w.Time < 240)
            {
                w.Tick(ZConst.TICK); w.Events.Clear();
                bool busy = w.Objects.Any(o => o.Owner == Team.Red && o.IsRobot && !o.IsMinion && OzV11Kit.Alive(o) && o.Waypoints.Count > 0);
                if (busy) idleSince = -1; else if (idleSince < 0) idleSince = w.Time; else if (w.Time - idleSince >= 5) break;   // every leader done for 5 s
            }
            manned = empties.Count(v => v.Owner == Team.Red);
            worstGone = 0; arrived = 0; elsewhere = 0;
            foreach (var m in members)
            {
                int gone = 0;
                foreach (var o in m)
                {
                    if (!w.Objects.Contains(o) || !OzV11Kit.Alive(o)) { gone++; continue; }
                    if (Math.Sqrt((double)(o.CenterX - gx) * (o.CenterX - gx) + (double)(o.CenterY - gy) * (o.CenterY - gy)) <= 120) arrived++; else elsewhere++;
                }
                worstGone = Math.Max(worstGone, gone);
            }
        }

#if UNITY_EDITOR
        [UnityEditor.MenuItem("OpenZ/V1.1/Count Automatic Captures (b3)")]
        static void Menu() => OzV11Kit.RunMenu("b3 automatic capture", () => Run(OzV11Kit.SettingsText, OzV11Kit.MapText));
#endif
    }
}
