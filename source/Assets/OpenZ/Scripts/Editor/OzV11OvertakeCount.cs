using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using OpenZ.Sim;

namespace OpenZ.EditorTools
{
    /// <summary>
    /// Counted check of feature f40 "fast units pass slow ones" (the rule: Sim/ZOvertake.cs, the switch ZWorld.OvertakeDeviation).
    /// Headless simulation on the three maps of version 1.1 without their start units; the other team gives no orders.
    ///  1. Convoys. A column (first = front, 3 tiles apart) gets ONE move order; arrival = first time within R tiles of the clicked
    ///     point. Each column runs free (the spacing layer off: units drive through each other, what each type does alone), with
    ///     the switch off (the game before the feature) and with it on.
    ///  2. The same roads from other starts (the front moved along the road, three mixes), switch off against on: nobody behind the
    ///     front may come later than before, no column may be slower as a whole.
    ///  3. The same order again every second for 5 s (a player who clicks again and again): nobody later than with the switch off.
    ///  4. At a bridge the switch changes nothing: two columns that start inside a bridge approach, on = off to the tenth of a second.
    ///  5. A group order into contact (an enemy within ZPathing.ContactRange of the clicked point): the same run with the switch on
    ///     and off, and no pass in it.
    ///  6. Units marked ZObject.KeepTogether (the computer's strike groups): the same column with the switch on and off.
    ///  7. With the switch on: no two units closer than their spacing - 1.5 px for more than 0.5 s (the stress test's measure; a
    ///     vehicle driving on through robots of its own team is the rule 'vehicles first' and counted apart), nobody left holding
    ///     an order, the pathing layer's last resort no more often than with the
    ///     switch off, no vehicle or squad leader faster than its own speed (the parity report's 0.25 s windows; more than
    ///     PaceSlackPx over what its speed allows = a unit set back onto a path point it ran past, or pushed along), and with
    ///     the switch off no pass at all.
    /// Menu: OpenZ/V1.1/Count Faster Units Passing (f40). About three minutes in the editor. Not in Play mode.
    /// </summary>
    public static class OzV11OvertakeCount
    {
#if UNITY_EDITOR
        /// <summary></summary>
        public static string ReportPath
        {
            get
            {
                string all = System.IO.Path.GetFullPath(System.IO.Path.Combine(UnityEngine.Application.dataPath, "../../../../work/records/milestones")), stage = "v1_1";
                if (System.IO.Directory.Exists(all)) { var ds = System.IO.Directory.GetDirectories(all, "v1_1*"); Array.Sort(ds, StringComparer.Ordinal); if (ds.Length > 0) stage = System.IO.Path.GetFileName(ds[ds.Length - 1]); }
                return System.IO.Path.Combine(all, stage, "f40_overtake", "OzV11OvertakeCount.txt");
            }
        }
#endif
        /// <summary>A unit that never comes counts this many s in a sum.</summary>
        public const double NeverS = 400;
        /// <summary>A 0.25 s window counts as faster than the unit's own speed when its way is more than this many px over what the
        /// speed allows (Zod sets a unit onto its path point when it reaches it: up to a pixel and a half with the switch on or off).</summary>
        public const double PaceSlackPx = 2;

        public sealed class Result
        {
            public string[] types; public double[] arrive, near; public int notThere, holding, overlaps, hullPairs, passes, returns, contact, lastResort, paceWindows, paceOver; public double farthest, paceExcess, ran;
            public double Sum => arrive.Where(t => !double.IsInfinity(t)).Sum();
            public int Never => arrive.Count(double.IsInfinity);
            /// <summary>All units of the column together: first time within 12 tiles of the clicked point (the game's own arrival ring of a group).</summary>
            public double NearSum => near.Sum(t => Math.Min(t, NeverS));
        }

        static string F(double v, string f = "0.0") => double.IsInfinity(v) ? "never" : v.ToString(f, CultureInfo.InvariantCulture);

        /// <summary>One column, one order. layer false = pure Zod movement (the free reference). enemy: a gun of the other team is put
        /// 6 tiles beyond the clicked point (the order then goes into contact). together: every unit is marked KeepTogether.
        /// again / until: the same order is sent again every `again` s until `until` s after the first.</summary>
        public static Result Convoy(string settings, string map, string level, int fx, int fy, int sx, int sy, int tx, int ty, int rTiles, string types, bool layer, bool feature, bool enemy = false, bool together = false, double maxS = 400, double again = 0, double until = 0)
        {
            bool wasLayer = ZPathing.DefaultEnabled, wasSwitch = ZWorld.OvertakeDeviation;
            ZPathing.DefaultEnabled = layer; ZWorld.OvertakeDeviation = feature;
            try
            {
                var w = OzV11Kit.World(settings, OzV11Kit.NoUnits(map), 1);
                OzV11Kit.Dummy(w, 5 * 16 + 8, 5 * 16 + 8);
                var tl = types.Split(',');
                var leaders = new List<ZObject>();
                for (int i = 0; i < tl.Length; i++) leaders.Add(OzV11Kit.Place(w, tl[i], (fx + i * sx) * 16 + 16, (fy + i * sy) * 16 + 16, Team.Red));
                if (together) foreach (var l in leaders) foreach (var b in OzV11Kit.Squad(l)) b.KeepTogether = true;
                int gx = tx * 16 + 8, gy = ty * 16 + 8;
                if (enemy) { var g = OzV11Kit.Place(w, "gun", gx + Math.Sign(tx - fx) * 96, gy + Math.Sign(ty - fy) * 96, Team.Blue); g.Damage = 0; g.MaxHealth = g.Health = 1000000000; }   // it stands there; it neither hurts nor dies
                var all = new List<ZObject>(); foreach (var l in leaders) all.AddRange(OzV11Kit.Squad(l));
                for (int i = 0; i < 100; i++) { w.Tick(ZConst.TICK); w.Events.Clear(); }
                double t0 = w.Time, R = rTiles * 16.0, next = again;
                foreach (var l in leaders) w.SendWaypoints(Team.Red, l, new[] { ZWaypoint.Move(gx, gy) });
                var first = leaders.ToDictionary(o => o.Id, o => double.PositiveInfinity);
                var near = leaders.ToDictionary(o => o.Id, o => double.PositiveInfinity);
                var pairStart = new Dictionary<long, double>(); var flagged = new HashSet<long>();
                var last = all.ToDictionary(o => o.Id, o => (x: o.PosX, y: o.PosY));
                var win = leaders.ToDictionary(o => o.Id, o => new double[4]);              // per vehicle / squad leader, a 0.25 s window: way made, way its own speed allows, ticks, touched by a push
                var moved = leaders.ToDictionary(o => o.Id, o => o.PathState.Displaced);
                var res = new Result { types = tl };
                int quiet = 0;
                while (w.Time - t0 < maxS && quiet < 300)
                {
                    if (again > 0 && w.Time - t0 <= until + 1e-9 && w.Time - t0 >= next - 1e-9)
                    {                                                                         // the same order again (a player who clicks again)
                        foreach (var l in leaders) if (OzV11Kit.Alive(l)) w.SendWaypoints(Team.Red, l, new[] { ZWaypoint.Move(gx, gy) });
                        next += again;
                    }
                    w.Tick(ZConst.TICK); w.Events.Clear();
                    bool any = false; double now = w.Time;
                    foreach (var o in all)
                    {
                        if (!OzV11Kit.Alive(o)) continue;
                        if (o.IsMoving || o.Waypoints.Count > 0) any = true;
                        var p = last[o.Id]; double dx = o.PosX - p.x, dy = o.PosY - p.y, step = Math.Sqrt(dx * dx + dy * dy); last[o.Id] = (o.PosX, o.PosY);
                        if (o.IsMinion) continue;
                        // the parity report's pace measure: 0.25 s windows of a vehicle or squad leader, its way against what its own speed allows on the tiles it
                        // was on; a window in which the layer pushed or moved it aside is left out (those moves are the spacing layer's own, with the switch on or off)
                        var wv = win[o.Id]; wv[0] += step; wv[1] += Math.Max(o.RealMoveSpeed, 0) * ZConst.TICK; wv[2]++;
                        if (o.PathPushedThisTick || o.PathState.Displaced != moved[o.Id]) { wv[3] = 1; moved[o.Id] = o.PathState.Displaced; }
                        if (wv[2] >= 25)
                        {
                            if (wv[3] == 0 && wv[1] > 0.5) { res.paceWindows++; double excess = wv[0] - wv[1]; if (excess > res.paceExcess) res.paceExcess = excess; if (excess > PaceSlackPx) res.paceOver++; }
                            wv[0] = wv[1] = wv[2] = wv[3] = 0;
                        }
                    }
                    foreach (var l in leaders)
                    {
                        double dx = l.CenterX - gx, dy = l.CenterY - gy, d2 = dx * dx + dy * dy;
                        if (double.IsInfinity(first[l.Id]) && d2 <= R * R) first[l.Id] = now - t0;
                        if (double.IsInfinity(near[l.Id]) && d2 <= 192.0 * 192.0) near[l.Id] = now - t0;
                    }
                    if (layer)
                        for (int i = 0; i < all.Count; i++)
                            for (int j = i + 1; j < all.Count; j++)
                            {
                                var a = all[i]; var b = all[j];
                                double dx = a.PosX + a.WidthPix * 0.5 - b.PosX - b.WidthPix * 0.5, dy = a.PosY + a.HeightPix * 0.5 - b.PosY - b.HeightPix * 0.5;
                                if (Math.Abs(dx) > 48 || Math.Abs(dy) > 48) continue;
                                long key = ((long)Math.Min(a.Id, b.Id) << 32) | (uint)Math.Max(a.Id, b.Id);
                                double min = ZPathing.MinSpacing(a, b) - 1.5;
                                if (dx * dx + dy * dy >= min * min || ZPathing.PairExempt(a, b)) { pairStart.Remove(key); continue; }
                                if (!pairStart.TryGetValue(key, out double s0)) { pairStart[key] = now; continue; }
                                if (now - s0 > 0.5 && flagged.Add(key)) { if ((a.Kind == ZKind.Vehicle) != (b.Kind == ZKind.Vehicle) && (a.IsRobot || b.IsRobot)) res.hullPairs++; else res.overlaps++; }
                            }
                    quiet = any ? 0 : quiet + 1;
                }
                res.arrive = leaders.Select(l => first[l.Id]).ToArray(); res.near = leaders.Select(l => near[l.Id]).ToArray(); res.ran = w.Time - t0;
                foreach (var o in all)
                {
                    if (!OzV11Kit.Alive(o)) continue;
                    double d = Math.Sqrt((double)(o.CenterX - gx) * (o.CenterX - gx) + (double)(o.CenterY - gy) * (o.CenterY - gy)) / 16;
                    if (d > res.farthest) res.farthest = d;
                    if (o.Waypoints.Count > 0) res.holding++;
                    if (o.Waypoints.Count > 0 || d > 12) res.notThere++;
                }
                if (w.Pathing != null) { res.passes = w.Pathing.Overtakes; res.returns = w.Pathing.PassReturns; res.contact = w.Pathing.ContactOrders; res.lastResort = w.Pathing.Ghosts; }
                return res;
            }
            finally { ZPathing.DefaultEnabled = wasLayer; ZWorld.OvertakeDeviation = wasSwitch; }
        }

        static string Line(string name, Result r, Result free)
        {
            var sb = new StringBuilder("    " + name.PadRight(5));
            for (int i = 0; i < r.types.Length; i++)
            {
                sb.Append(i > 0 ? "; " : "").Append(r.types[i]).Append(' ').Append(F(r.arrive[i]));
                if (free != null && !double.IsInfinity(r.arrive[i]) && !double.IsInfinity(free.arrive[i]) && free.arrive[i] > 0) sb.Append(" (x").Append((r.arrive[i] / free.arrive[i]).ToString("0.00", CultureInfo.InvariantCulture)).Append(')');
            }
            sb.Append("; sum ").Append(F(r.Sum, "0")).Append(r.Never > 0 ? " + " + r.Never + " never" : "");
            if (free != null) sb.Append(" | not there ").Append(r.notThere).Append(" (farthest ").Append(F(r.farthest)).Append(" tiles), holding an order ").Append(r.holding).Append(", overlaps ").Append(r.overlaps)
                .Append(r.hullPairs > 0 ? " (+ " + r.hullPairs + " vehicle through robots of its team)" : "").Append(", passes ").Append(r.passes).Append(", back on its line ").Append(r.returns).Append(", last-resort pass-throughs ").Append(r.lastResort).Append(Fast(r));
            return sb.ToString();
        }

        /// <summary>Named only when it happened: 0.25 s windows in which a vehicle or squad leader made more way than its own speed allows.</summary>
        static string Fast(Result r) => r.paceOver > 0 ? ", windows faster than its own speed " + r.paceOver + " (" + r.paceExcess.ToString("0.0", CultureInfo.InvariantCulture) + " px over)" : "";

        static string Near(Result r) => string.Join(" / ", r.near.Select(t => F(t))) + " (together " + F(r.NearSum, "0") + ")";

        static bool Same(Result a, Result b)
        {
            if (a.arrive.Length != b.arrive.Length) return false;
            for (int i = 0; i < a.arrive.Length; i++) if (a.arrive[i] != b.arrive[i] && !(double.IsInfinity(a.arrive[i]) && double.IsInfinity(b.arrive[i]))) return false;
            return a.farthest == b.farthest && a.ran == b.ran;
        }

        /// <summary>(level, what, room beside the whole way, front, step, goal, R, columns)</summary>
        static readonly (string level, string what, bool open, int fx, int fy, int sx, int sy, int tx, int ty, int r, string[] cols)[] Rows =
        {
            ("L17", "Car Park, open ground north of the long road (room on both sides)", true, 32, 11, -3, 0, 96, 11, 8, new[] { "heavy,jeep,jeep,light,light", "missile_launcher,jeep,light,medium,apc" }),
            ("L16", "Jungle, the south road west to east, 60 tiles (2 tiles wide: to pass, a unit must leave the road)", false, 30, 104, -3, 0, 90, 104, 8, new[] { "heavy,jeep,jeep,light,light", "missile_launcher,jeep,light,medium,apc" }),
            ("L16", "Jungle, open ground south of the south road", true, 69, 108, -3, 0, 88, 108, 8, new[] { "heavy,jeep,jeep,light,light" }),
            ("L17", "Car Park, the long road west to east (2 tiles wide)", false, 32, 13, -3, 0, 100, 13, 8, new[] { "heavy,jeep,jeep,light,light" }),
        };

        /// <summary>The same two roads from other starts: (level, what, the front's tiles along the road, row, goal).</summary>
        static readonly (string level, string what, int[] fronts, int fy, int tx, int ty)[] Shifted =
        {
            ("L16", "Jungle, the south road", new[] { 26, 33, 36, 40, 44 }, 104, 90, 104),
            ("L17", "Car Park, the long road", new[] { 30, 38, 46 }, 13, 100, 13),
        };
        static readonly string[] ShiftedCols = { "heavy,light,light,jeep,jeep", "heavy,medium,jeep,jeep", "heavy,jeep,light,jeep,light,medium" };

        public static string Run(string settings, Func<string, string> maps)
        {
            var sb = new StringBuilder();
            sb.AppendLine("Faster units pass slower ones (f40): counted, headless. A column 3 tiles apart (first = front) gets ONE move order; arrival = first time within R tiles; s of game time.");
            sb.AppendLine("free = the spacing layer off (units drive through each other); off = the switch off; on = the switch on. (x..) = against the unit's free time.");
            bool ok = true; double sumOff = 0, sumOn = 0, openOff = 0, openOn = 0; int overlapsOn = 0, holdingOn = 0, offActs = 0, lateBy = 0, behind = 0, slower = 0, columns = 0, lastOn = 0, lastOff = 0; double paceOn = 0, paceOff = 0; int jeepsOk = 0, jeeps = 0, overOn = 0, overOff = 0, winOn = 0;
            void Count(Result off, Result on)
            {                                                                                 // what every column is judged on, switch on against off
                columns++;
                overlapsOn += on.overlaps; holdingOn += on.holding; offActs += off.passes + off.returns; paceOn = Math.Max(paceOn, on.paceExcess); paceOff = Math.Max(paceOff, off.paceExcess); overOn += on.paceOver; overOff += off.paceOver; winOn += on.paceWindows;
                lastOn += on.lastResort; lastOff += off.lastResort;
                for (int i = 1; i < on.near.Length; i++) { behind++; if (on.near[i] > off.near[i] + 3) lateBy++; }   // a unit behind the front that comes clearly later than before (12 tiles: the game's own arrival ring of a group)
                if (on.NearSum > off.NearSum + 1) slower++;
            }
            foreach (var row in Rows)
            {
                string map = maps(row.level);
                sb.AppendLine().AppendLine(row.what + ": front (" + row.fx + "," + row.fy + ") -> (" + row.tx + "," + row.ty + "), R " + row.r + " tiles");
                foreach (var col in row.cols)
                {
                    var free = Convoy(settings, map, row.level, row.fx, row.fy, row.sx, row.sy, row.tx, row.ty, row.r, col, false, false);
                    var off = Convoy(settings, map, row.level, row.fx, row.fy, row.sx, row.sy, row.tx, row.ty, row.r, col, true, false);
                    var on = Convoy(settings, map, row.level, row.fx, row.fy, row.sx, row.sy, row.tx, row.ty, row.r, col, true, true);
                    sb.AppendLine("  front first: " + col).AppendLine(Line("free", free, null)).AppendLine(Line("off", off, free)).AppendLine(Line("on", on, free));
                    sumOff += off.Sum + NeverS * off.Never; sumOn += on.Sum + NeverS * on.Never;
                    if (row.open) { openOff += off.Sum + NeverS * off.Never; openOn += on.Sum + NeverS * on.Never; }
                    Count(off, on);
                    if (row.open) for (int i = 1; i < on.arrive.Length; i++) if (on.types[i] == "jeep") { jeeps++; if (on.arrive[i] <= 1.25 * free.arrive[i]) jeepsOk++; }
                }
            }
            sb.AppendLine().AppendLine("The same roads from other starts (the front moved along the road), switch off against on; first time within 12 tiles, front first (together = all of the column, a unit that never came counts " + F(NeverS, "0") + " s):");
            double shOff = 0, shOn = 0;
            foreach (var sh in Shifted)
            {
                string map = maps(sh.level);
                foreach (int fx in sh.fronts)
                    foreach (var col in ShiftedCols)
                    {
                        var off = Convoy(settings, map, sh.level, fx, sh.fy, -3, 0, sh.tx, sh.ty, 12, col, true, false);
                        var on = Convoy(settings, map, sh.level, fx, sh.fy, -3, 0, sh.tx, sh.ty, 12, col, true, true);
                        sb.AppendLine("  " + sh.what + ", front (" + fx + "," + sh.fy + ") -> (" + sh.tx + "," + sh.ty + "), " + col + ": off " + Near(off) + "; on " + Near(on) + ", passes " + on.passes + ", last-resort pass-throughs " + on.lastResort + " (off " + off.lastResort + ")" + (off.paceOver > 0 ? "; off" + Fast(off) : "") + (on.paceOver > 0 ? "; on" + Fast(on) : ""));
                        shOff += off.NearSum; shOn += on.NearSum;
                        Count(off, on);
                    }
            }
            Result reOff, reOn; int reLate = 0;
            {
                var r = Rows[1];
                reOff = Convoy(settings, maps(r.level), r.level, r.fx, r.fy, r.sx, r.sy, r.tx, r.ty, 12, r.cols[0], true, false, again: 1, until: 5);
                reOn = Convoy(settings, maps(r.level), r.level, r.fx, r.fy, r.sx, r.sy, r.tx, r.ty, 12, r.cols[0], true, true, again: 1, until: 5);
                for (int i = 0; i < reOn.near.Length; i++) if (reOn.near[i] > reOff.near[i] + 3) reLate++;
                sb.AppendLine("  The same order again every 1 s for 5 s (6 orders), Jungle, the south road, front (" + r.fx + "," + r.fy + "), " + r.cols[0] + ": off " + Near(reOff) + "; on " + Near(reOn));
                overlapsOn += reOn.overlaps; holdingOn += reOn.holding; lastOn += reOn.lastResort; lastOff += reOff.lastResort; offActs += reOff.passes + reOff.returns;
            }
            sb.AppendLine();
            bool v1 = openOn <= 0.80 * openOff && sumOn <= sumOff && shOn <= shOff; ok &= v1;
            sb.AppendLine(OzV11Kit.Verdict(v1, "the columns arrive sooner: on open ground all arrival times together " + F(openOn, "0") + " s with the switch on, " + F(openOff, "0") + " s off (pass: at most 80 % of off); all " + Rows.Sum(r => r.cols.Length) + " columns above " + F(sumOn, "0") + " s on, " + F(sumOff, "0") + " s off; the roads from other starts " + F(shOn, "0") + " s on, " + F(shOff, "0") + " s off (pass: not more than off; a unit that never came counts " + F(NeverS, "0") + " s)"));
            bool v2 = jeepsOk == jeeps && jeeps > 0; ok &= v2;
            sb.AppendLine(OzV11Kit.Verdict(v2, "on open ground the jeeps behind a slow front vehicle arrive within 1.25 times their free time: " + jeepsOk + " of " + jeeps + " (on a road two tiles wide passing means leaving the road: not judged there)"));
            bool v3 = lateBy == 0; ok &= v3;
            sb.AppendLine(OzV11Kit.Verdict(v3, "no unit behind the front comes within 12 tiles of the clicked point more than 3 s later than with the switch off: " + lateBy + " of " + behind + " do (" + columns + " columns)"));
            bool v3b = slower == 0; ok &= v3b;
            sb.AppendLine(OzV11Kit.Verdict(v3b, "no column is slower as a whole than with the switch off (all its units together, first time within 12 tiles, more than 1 s): " + slower + " of " + columns + " columns"));
            bool v3c = reLate == 0; ok &= v3c;
            sb.AppendLine(OzV11Kit.Verdict(v3c, "the same order again every 1 s for 5 s: no unit comes more than 3 s later than with the switch off under the same clicks: " + reLate + " of " + reOn.near.Length + " do"));

            // at a bridge nothing changes
            int bridgeSame = 0, bridgeRuns = 0;
            foreach (var col in new[] { "heavy,jeep,jeep,light,light", "missile_launcher,jeep,jeep,light,light" })
            {
                var off = Convoy(settings, maps("L02"), "L02", 24, 45, -3, 0, 56, 45, 6, col, true, false);
                var on = Convoy(settings, maps("L02"), "L02", 24, 45, -3, 0, 56, 45, 6, col, true, true);
                bridgeRuns++; if (Same(off, on) && on.passes == 0) bridgeSame++;
                lastOn += on.lastResort; lastOff += off.lastResort;
                sb.AppendLine("  Desert, the road through two bridge decks, the column inside the first approach, " + col + ": on " + string.Join(" / ", on.arrive.Select(t => F(t))) + ", off " + string.Join(" / ", off.arrive.Select(t => F(t))));
            }
            bool v4 = bridgeSame == bridgeRuns; ok &= v4;
            sb.AppendLine(OzV11Kit.Verdict(v4, "a column inside a bridge approach: the same run with the switch on and off, no pass: " + bridgeSame + " of " + bridgeRuns));

            // a group order into contact; units kept together
            {
                var r = Rows[0];
                var off = Convoy(settings, maps(r.level), r.level, r.fx, r.fy, r.sx, r.sy, r.tx, r.ty, r.r, r.cols[0], true, false, enemy: true);
                var on = Convoy(settings, maps(r.level), r.level, r.fx, r.fy, r.sx, r.sy, r.tx, r.ty, r.r, r.cols[0], true, true, enemy: true);
                bool v5 = Same(off, on) && on.passes == 0 && on.contact == 1; ok &= v5;
                sb.AppendLine(OzV11Kit.Verdict(v5, "a group order with an enemy gun 6 tiles from the clicked point (Car Park, open ground): the same run with the switch on and off (" + string.Join(" / ", on.arrive.Select(t => F(t))) + "), orders into contact " + on.contact + ", passes " + on.passes));
                var offT = Convoy(settings, maps(r.level), r.level, r.fx, r.fy, r.sx, r.sy, r.tx, r.ty, r.r, r.cols[0], true, false, together: true);
                var onT = Convoy(settings, maps(r.level), r.level, r.fx, r.fy, r.sx, r.sy, r.tx, r.ty, r.r, r.cols[0], true, true, together: true);
                bool v6 = Same(offT, onT) && onT.passes == 0; ok &= v6;
                sb.AppendLine(OzV11Kit.Verdict(v6, "the same column with every unit marked KeepTogether (the mark for units that march as one body): the same run with the switch on and off, passes " + onT.passes));
            }

            // the rules that must hold
            bool v7 = overlapsOn == 0; ok &= v7;
            sb.AppendLine(OzV11Kit.Verdict(v7, "with the switch on no two units closer than their spacing - 1.5 px for more than 0.5 s in the columns above: " + overlapsOn + " pairs"));
            bool v8 = holdingOn == 0; ok &= v8;
            sb.AppendLine(OzV11Kit.Verdict(v8, "with the switch on nobody is left holding an order: " + holdingOn));
            bool v8b = lastOn <= lastOff; ok &= v8b;
            sb.AppendLine(OzV11Kit.Verdict(v8b, "the pathing layer's last resort (no movement for 6 s in a jam: the unit passes through the others) fires no more often with the switch on than off: " + lastOn + " times on, " + lastOff + " off"));
            bool v9 = overOn == 0; ok &= v9;
            sb.AppendLine(OzV11Kit.Verdict(v9, "with the switch on no vehicle or squad leader is faster than its own speed (the parity report's 0.25 s windows without a push; over = more than " + F(PaceSlackPx, "0") + " px more way than its speed allows): " + overOn + " of " + winOn + " windows over, the most " + paceOn.ToString("0.0", CultureInfo.InvariantCulture) + " px; with the switch off " + overOff + " over, the most " + paceOff.ToString("0.0", CultureInfo.InvariantCulture) + " px"));
            bool v10 = offActs == 0; ok &= v10;
            sb.AppendLine(OzV11Kit.Verdict(v10, "with the switch off no pass and no way back: " + offActs));
            sb.AppendLine();
            sb.AppendLine(ok ? "ALL PASS" : "SOME FAIL");
            return sb.ToString();
        }

#if UNITY_EDITOR
        [UnityEditor.MenuItem("OpenZ/V1.1/Count Faster Units Passing (f40)")]
        static void Menu()
        {
            OzV11Kit.RunMenu("Count Faster Units Passing (f40)", () =>
            {
                string report = Run(OzV11Kit.SettingsText, OzV11Kit.MapText);
                try { System.IO.Directory.CreateDirectory(System.IO.Path.GetDirectoryName(ReportPath)); System.IO.File.WriteAllText(ReportPath, report.Replace("\r", "")); report += "\nwritten: " + ReportPath; }
                catch (Exception e) { report += "\nreport not written: " + e.Message; }
                return report;
            });
        }
#endif
    }
}
