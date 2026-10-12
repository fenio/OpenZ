using System;
using System.Collections.Generic;
using System.Linq;
using OpenZ.Sim;
#if !OPENZ_HARNESS
using UnityEditor;
using UnityEngine;
#endif

namespace OpenZ.EditorTools
{
    /// <summary>
    /// Sim/ZPathV3.cs zones) and DV47 "soft unstick" (Sim/ZPathV3.cs), measured headless in the same sim as the parity rows, every probe
    /// once with pathing v3 off (the live pathing layer: the rollback) and once on, same seed:
    ///   open  a 6-grunt squad round the L02 vehicle factory (40,24 -> 53,24): reversals, heading flips, re-plans (DV44 applies);
    ///   zone  a 6-grunt squad over the L02 N-S bridge (52,54 -> 53,67): the share of unit-ticks in a zone (live rules there);
    ///   mass  30 units ordered at once on L02's south field: most path requests started in one tick (PT2 budget);
    ///   rest  4 vehicles ordered to one point on open ground: settle time and micro-moves (PT3);
    ///   jam   L17: a wreck closes one tile of the 2-tile rock-wall opening (68-70, 37-38), a 6-grunt squad and two 3-grunt squads meet in it
    ///         head-on: the longest stand with an order, DV47 unsticks, unit-ticks on a hard blocked tile (must be 0);
    ///   pace  a 6-grunt squad through the L17 canal and along L02's longest road (OpenZParity.PathV3Rows.cs, also the squad-pace rows).
    /// PathV3Check.Run() (menu OpenZ/View/Pathing v3 Check (Play)). Menus: OpenZ/Deviations/Pathing v3 (oz_path_v3) and
    /// OpenZ/Deviations/Probe DV44 DV47 Pathing v3.
    /// </summary>
    public static partial class OpenZParity
    {
        public struct PathV3Run { public int units, reversals, flips, replans, maxSearches, maxStarts, micro, left, unsticks, hardTicks, softUnits; public double settle, arrived, stand, zonedShare; public string standWho; }

        /// <summary>One pathing v3 probe world (see the class summary). Deterministic (seeded world, fixed tick).</summary>
        public static PathV3Run PathV3Probe(string what, bool on)
        {
            bool pc = ZWorld.PathV3Deviation, ef = ZWorld.EndFreezeDeviation, fg = ZWorld.FortGunDeviation, fd = ZWorld.FortDefenderDeviation;
            ZWorld.PathV3Deviation = on; ZWorld.EndFreezeDeviation = false; ZWorld.FortGunDeviation = false; ZWorld.FortDefenderDeviation = false;
            try
            {
                string lv = what == "jam" ? "L17" : "L02";
                var w = ZWorld.Create(SettingsText, MapText(lv), 1, false);
                foreach (var o in w.Objects)
                    if (o.Kind == ZKind.Robot || o.Kind == ZKind.Cannon || (o.Kind == ZKind.Vehicle && o.Owner != Team.Neutral)) o.KillAt = w.Time;
                for (int i = 0; i < 100; i++) w.Tick(ZConst.TICK);
                w.Events.Clear();
                int sx, sy, gx, gy;
                if (what == "open") { sx = 40; sy = 24; gx = 53; gy = 24; }
                else if (what == "zone") { sx = 52; sy = 54; gx = 53; gy = 67; }
                else if (what == "mass") { sx = 26; sy = 80; gx = 12; gy = 63; }
                else if (what == "jam") { sx = 62; sy = 38; gx = 76; gy = 38; }
                else { sx = 18; sy = 80; gx = 30; gy = 79; }
                ZObject wreck = null;
                if (what == "jam") { wreck = w.AddTestWreck(68, 37, 3, 1); for (int i = 0; i < 20; i++) { w.Tick(ZConst.TICK); w.Events.Clear(); } }
                var ordered = new List<ZObject>(); var back = new List<ZObject>(); var taken = new List<(int x, int y, int r)>();
                int rr = what == "mass" ? 2 : 3;
                void Sq(List<ZObject> into, string type, int n, int ax, int ay) { var (x, y) = PcSpot(w, ax, ay, true, taken, rr); into.Add(w.CreateRobotGroup(type, x * 16, y * 16, Team.Red, n)); }
                void Ve(string type) { var (x, y) = PcSpot(w, sx, sy, false, taken, rr); ordered.Add(w.CreateObject("vehicle", type, x * 16, y * 16, Team.Red)); }
                if (what == "open" || what == "zone") Sq(ordered, "grunt", 6, sx, sy);
                else if (what == "jam") { Sq(ordered, "grunt", 6, sx, sy); Sq(back, "grunt", 3, gx, gy); Sq(back, "grunt", 3, gx, gy); }
                else if (what == "mass")
                {
                    string[] rt = { "grunt", "psycho", "sniper" }, vt = { "jeep", "light", "medium", "heavy", "apc", "missile_launcher" };
                    foreach (var t in rt) Sq(ordered, t, 3, sx, sy);
                    for (int i = 0; i < 21; i++) Ve(vt[i % vt.Length]);
                }
                else { Ve("light"); Ve("medium"); Ve("jeep"); Ve("apc"); }
                var all = new List<ZObject>();
                foreach (var o in ordered.Concat(back)) { all.Add(o); all.AddRange(o.Minions); o.PlayerHold = true; }
                var last = all.Select(o => (o.PosX, o.PosY)).ToList(); int still = 0;
                for (int i = 0; i < 1200; i++)
                {                                                                               // stacked squads unstack, then everyone stands still
                    w.Tick(ZConst.TICK); w.Events.Clear();
                    bool moved = false;
                    for (int k = 0; k < all.Count; k++) { var p = (all[k].PosX, all[k].PosY); if (Math.Abs(p.Item1 - last[k].Item1) + Math.Abs(p.Item2 - last[k].Item2) > 1e-9) moved = true; last[k] = p; }
                    still = moved ? 0 : still + 1;
                    if (i >= 300 && still >= 100) break;
                }
                w.Pathing?.ResetSearchStats();
                int unst0 = w.Pathing != null ? w.Pathing.Unsticks : 0;
                foreach (var o in ordered) w.SendWaypoints(Team.Red, o, new List<ZWaypoint> { ZWaypoint.Move(gx * 16 + 8, gy * 16 + 8, true) });
                foreach (var o in back) w.SendWaypoints(Team.Red, o, new List<ZWaypoint> { ZWaypoint.Move(sx * 16 + 8, sy * 16 + 8, true) });
                var pos = all.Select(_ => new List<(double x, double y)>()).ToList();
                var ord = all.Select(_ => new List<bool>()).ToList();
                var r = new PathV3Run { units = all.Count, arrived = -1 };
                double t0 = w.Time, idle = -1, limit = what == "mass" ? 30 : 120; long zonedTicks = 0, ticks = 0;
                int[] replan = new int[all.Count]; object[] lastPts = new object[all.Count]; var soft = new HashSet<int>();
                var tight = all.Select(_ => new List<bool>()).ToList();
                while (w.Time - t0 < limit)
                {
                    w.Tick(ZConst.TICK); w.Events.Clear();
                    bool busy = false;
                    for (int k = 0; k < all.Count; k++)
                    {
                        var o = all[k]; bool has = o.Waypoints.Count > 0; busy |= has;
                        pos[k].Add((o.PosX + o.WidthPix * 0.5, o.PosY + o.HeightPix * 0.5)); ord[k].Add(has);
                        var pts = (object)o.PathPoints;
                        if (has && pts != null && lastPts[k] != null && !ReferenceEquals(pts, lastPts[k])) replan[k]++;
                        if (pts != null) lastPts[k] = pts;
                        tight[k].Add(w.Pathing != null && w.Pathing.TightAt(o.CenterX, o.CenterY));
                        if (w.Get(o.Id) != o) continue;
                        ticks++; if (o.PathZoned) zonedTicks++;
                        if (w.Pathing != null && w.Pathing.SoftPassOn(o)) soft.Add(o.Id);
                        if (PvHardTile(w, o, wreck)) r.hardTicks++;
                    }
                    if (!busy) { if (idle < 0) { idle = w.Time; r.arrived = w.Time - t0; } if (w.Time - idle >= 4) break; } else idle = -1;
                }
                r.maxSearches = w.Pathing != null ? w.Pathing.MaxSearchesPerTick : -1; r.maxStarts = w.Pathing != null ? w.Pathing.MaxStartsPerTick : -1;
                r.left = all.Count(o => o.Waypoints.Count > 0);
                r.unsticks = (w.Pathing != null ? w.Pathing.Unsticks : 0) - unst0; r.softUnits = soft.Count;
                r.zonedShare = ticks > 0 ? (double)zonedTicks / ticks : 0;
                for (int k = 0; k < all.Count; k++)
                {
                    var (rev, fl) = PcTurns(pos[k]); r.reversals += rev; r.flips += fl; r.replans += Math.Max(0, replan[k] - 1);
                    var (st, mi) = PcSettle(pos[k], ord[k]); r.settle = Math.Max(r.settle, st); r.micro += mi;
                    int a = 0;
                    for (int j = 0; j < pos[k].Count; j++)
                    {                                                                           // the longest stand with an order (< 2 px moved)
                        double dx = pos[k][j].x - pos[k][a].x, dy = pos[k][j].y - pos[k][a].y;
                        if (!ord[k][j] || dx * dx + dy * dy >= 4) { a = j; continue; }
                        if ((j - a) * ZConst.TICK > r.stand) { r.stand = (j - a) * ZConst.TICK; r.standWho = $"{all[k].Type}#{all[k].Id}{(all[k].IsMinion ? " (minion)" : "")} from {a * ZConst.TICK:0.0} s at ({(int)pos[k][a].x >> 4},{(int)pos[k][a].y >> 4}){(j < tight[k].Count && tight[k][j] ? ", tight place" : ", open")}"; }
                    }
                }
                return r;
            }
            finally { ZWorld.PathV3Deviation = pc; ZWorld.EndFreezeDeviation = ef; ZWorld.FortGunDeviation = fg; ZWorld.FortDefenderDeviation = fd; }
        }

        /// <summary>The unit's box (shrunk 2 px, like the stress suite) on a tile its own grid blocks that is not a soft blocker.</summary>
        static bool PvHardTile(ZWorld w, ZObject u, ZObject wreck)
        {
            if (u.PathScriptedMove || u.Destroyed) return false;
            int x0 = u.LocX + 2, y0 = u.LocY + 2, x1 = u.LocX + u.WidthPix - 3, y1 = u.LocY + u.HeightPix - 3;
            for (int ty = y0 >> 4; ty <= y1 >> 4; ty++)
                for (int tx = x0 >> 4; tx <= x1 >> 4; tx++)
                {
                    if (w.Path.TilePassable(tx, ty, u.IsRobot) || (w.Pathing != null && w.Pathing.SoftTileAt(tx, ty))) continue;
                    if (wreck != null && tx >= wreck.LocX >> 4 && tx < (wreck.LocX + wreck.WidthPix) >> 4 && ty >= wreck.LocY >> 4 && ty < (wreck.LocY + wreck.HeightPix) >> 4) continue;
                    return true;
                }
            return false;
        }

        static (int x, int y) PcSpot(ZWorld w, int tx, int ty, bool robot, List<(int x, int y, int r)> taken, int r)
        {
            bool Free(int cx, int cy)
            {
                int n = robot ? 1 : 2;
                for (int dy = -1; dy <= n; dy++) for (int dx = -1; dx <= n; dx++)
                    {
                        int x = cx + dx, y = cy + dy;
                        if (!w.Path.TilePassable(x, y, robot) || !w.Path.TilePassable(x, y, false)) return false;
                        var c = w.Map.ClassAt(x, y); if (c == ZClass.Water || c == ZClass.DeepWater) return false;
                    }
                foreach (var o in w.Objects)
                    if (!o.Destroyed && o.KillAt < 0 && (o.IsUnit || o.Kind == ZKind.Building || o.Type == "rock") &&
                        o.LocX < (cx + n + 1) * 16 && (cx - 1) * 16 < o.LocX + o.WidthPix && o.LocY < (cy + n + 1) * 16 && (cy - 1) * 16 < o.LocY + o.HeightPix) return false;
                foreach (var t in taken) if (Math.Max(Math.Abs(t.x - cx), Math.Abs(t.y - cy)) < t.r + r) return false;
                return true;
            }
            for (int ring = 0; ring < 40; ring++)
                for (int y = ty - ring; y <= ty + ring; y++)
                    for (int x = tx - ring; x <= tx + ring; x++)
                    {
                        if (Math.Max(Math.Abs(x - tx), Math.Abs(y - ty)) != ring || !Free(x, y)) continue;
                        taken.Add((x, y, r)); return (x, y);
                    }
            return (tx, ty);
        }

        /// <summary>Reversals (> 135 deg between 0.25 s moves of >= 1 px) and heading flips (> 90 deg between 0.1 s moves of >= 0.5 px); a
        /// chain restarts after 1 s at rest (the harness's definitions).</summary>
        static (int rev, int flips) PcTurns(List<(double x, double y)> p)
        {
            int Count(int win, double min2, double deg)
            {
                int n = 0; double still = 0; (double x, double y)? prev = null;
                double cmin = Math.Cos(deg * Math.PI / 180);
                for (int k = win; k < p.Count; k += win)
                {
                    double dx = p[k].x - p[k - win].x, dy = p[k].y - p[k - win].y;
                    if (dx * dx + dy * dy < min2) { still += win * ZConst.TICK; if (still >= 1.0) prev = null; continue; }
                    still = 0;
                    if (prev != null)
                    {
                        double c = (prev.Value.x * dx + prev.Value.y * dy) / Math.Sqrt((prev.Value.x * prev.Value.x + prev.Value.y * prev.Value.y) * (dx * dx + dy * dy));
                        if (c < cmin) n++;
                    }
                    prev = (dx, dy);
                }
                return n;
            }
            return (Count(25, 1.0, 135), Count(10, 0.25, 90));
        }

        /// <summary>Settle = last movement - first time within 1 m (8 px) of the final rest position; micro = movement episodes after the
        /// order ended that never took the unit 0.8 px (0.1 m) from where they started.</summary>
        static (double settle, int micro) PcSettle(List<(double x, double y)> p, List<bool> ord)
        {
            int n = p.Count; if (n < 2) return (0, 0);
            var (fx, fy) = p[n - 1];
            int lastMove = 0, near = -1, end = -1, micro = 0;
            for (int k = 1; k < n; k++) if (Math.Abs(p[k].x - p[k - 1].x) + Math.Abs(p[k].y - p[k - 1].y) > 1e-6) lastMove = k;
            for (int k = 0; k < n; k++) if ((p[k].x - fx) * (p[k].x - fx) + (p[k].y - fy) * (p[k].y - fy) <= 64) { near = k; break; }
            for (int k = 1; k < n; k++) if (ord[k - 1] && !ord[k]) { end = k; break; }
            if (end >= 0)
            {
                int k = end;
                while (k < n)
                {
                    while (k + 1 < n && Math.Abs(p[k + 1].x - p[k].x) + Math.Abs(p[k + 1].y - p[k].y) <= 1e-6) k++;
                    if (k + 1 >= n) break;
                    int s0 = k, rest = 0; double mx = 0; k++;
                    while (k < n && rest < 20)
                    {
                        if (Math.Abs(p[k].x - p[k - 1].x) + Math.Abs(p[k].y - p[k - 1].y) <= 1e-6) rest++; else rest = 0;
                        mx = Math.Max(mx, Math.Sqrt((p[k].x - p[s0].x) * (p[k].x - p[s0].x) + (p[k].y - p[s0].y) * (p[k].y - p[s0].y))); k++;
                    }
                    if (mx < 0.8) micro++;
                }
            }
            return (near < 0 ? 0 : Math.Max(0, (lastMove - near) * ZConst.TICK), micro);
        }

        static string PvToggle(Func<string, int, int> getInt) =>
            $"ZWorld.PathV3Deviation: PlayerPrefs oz_path_v3 = {(getInt("oz_path_v3", 1) == 1 ? "1 (on, the default since 6 Oct)" : "0 (off = the rollback: the classic pathing layer bit for bit)")}; menu OpenZ/Deviations/Pathing v3; off in 'Zod exact' (oz_zod_exact = 1), in the parity report's Zod rows and in the DV22 / DV24 probes' Zod arms; per-world parts World.Pathing.CommitFollow / CommitBudget / CommitSettle / ZonesOn / UnstickOn (tests only); the tuning values are constants";

        static Deviation DevPathV3(Func<string, int, int> getInt)
        {
            var o0 = PathV3Probe("open", false); var o1 = PathV3Probe("open", true);
            var z0 = PathV3Probe("zone", false); var z1 = PathV3Probe("zone", true);
            var m1 = PathV3Probe("mass", true);
            var a0 = PathV3Probe("rest", false); var a1 = PathV3Probe("rest", true);
            var sw = PathCommitSquadPace("L17", 47, 7, 62, 7); var sr = PathCommitSquadPace("L02", 14, 45, 58, 45);
            bool pv = ZWorld.PathV3Deviation; SquadPace sw0, sr0;                                  // the same squads with pathing v3 off (the live pathing: DV24's pace)
            try { ZWorld.PathV3Deviation = false; sw0 = PathCommitSquadPace("L17", 47, 7, 62, 7); sr0 = PathCommitSquadPace("L02", 14, 45, 58, 45); } finally { ZWorld.PathV3Deviation = pv; }
            bool ok = o1.reversals <= o0.reversals && o1.left == 0 && z1.zonedShare >= 0.3 && z1.left == 0 && z1.arrived <= z0.arrived * 1.15 + 5
                      && m1.maxStarts >= 0 && m1.maxStarts <= ZPathing.SearchCap && a1.micro == 0 && a1.settle < 1.5 && a1.left == 0
                      && sw.waterOver <= sw0.waterOver * 1.2 + 2 && sr.roadOver <= Math.Max(sr0.roadOver, 1) && sr.groundOver + sw.groundOver <= sr0.groundOver + sw0.groundOver + 2;   // (in a zone a minion walks DV24's live pace)
            bool pref = getInt("oz_path_v3", 1) == 1 && getInt("oz_zod_exact", 0) == 0;
            string Run(PathV3Run r) => $"{r.reversals} reversals, {r.flips} flips, {r.replans} re-plans, all stopped after {NbS(r.arrived)}";
            string cu = ZFormation.CatchUp.ToString("0.0#", System.Globalization.CultureInfo.InvariantCulture);
            return new Deviation
            {
                id = "DV44", change = "Pathing v3 - path commit outside bridges and bottlenecks: squads walk their leader's trail (V, single file), path searches are budgeted per tick, arrivals settle without micro-corrections; bridges, water crossings, fort gates and the bottleneck passages (one way: for the units going through; head-on: while traffic opposes) keep the live pathing (their queues / back-and-forth)",
                zod = "no unit collision: every minion runs its own A* to the leader's goal and the squad stacks on one point, each unit at its own speed; Zod starts one path thread per request at once (zpath_finding.cpp:885, the threads run concurrently, no per-tick limit); a squad's minions stop the moment the leader's MOVE ends; units snap onto their goal",
                openz = $"ZONES (derived per grid from the Zod grid at level load, again when passability changes; robots ask the robot grid, vehicles their 2x2 footprint): every bridge (intact or destroyed) + {ZPathing.BridgeEnd} tiles past each end and its deck lane's keep-clear area; wadeable water crossings (water runs <= {ZPathing.WaterCrossRun} tiles between dry land: rivers, the canal, harbour arms; not the open basins); the fort gate routes + {ZPathing.GateGrow}; bottleneck passages (a free run <= {ZPathing.NarrowRun} across on the unit's own grid with no way round within {ZPathing.DetourMax} steps) only WHILE traffic opposes in them (two squads / units within {ZPathing.NarrowSense} tiles heading opposite ways; then every unit within {ZPathing.NarrowReach} tiles); one way a passage of walkable width <= 2 is live for the units whose own way enters it within {ZPathing.PassLook / 16:0} tiles from within {ZPathing.NarrowSense} steps of it - a squad as one, until {ZPathing.PassExit} steps past it (to the end of an order that ends next to it) - and a group order whose members' shortest ways go through one gets the live spots and the live rules there; DV44's one-tile lanes at such a passage leave the lane list while only live traffic is at them. " +
                        $"A unit switches when its tile or its way ahead ({ZPathing.LookAhead / 16:0} tiles) enters a zone (before the passage, not in its mouth; a squad goes onto a bridge / water crossing / gate as one when its leader's way enters it within {ZPathing.StaticSquadLook / 16:0} tiles) and leaves it {ZPathing.ZoneExitGrow} tiles clear after {ZPathing.ZoneExitHold} s; in a static zone each squad member switches where it reaches it, an encounter / active passage zones the squad, and in a tight place a unit within {ZPathing.EncStart / 16:0} tiles of a zoned unit of another order whose goal lies the other way (> {ZPathing.NarrowOppose:0} deg) is zoned too until {ZPathing.EncEnd / 16:0} tiles apart for {ZPathing.EncHold:0} s (one rule set per encounter). A new order decides the zone state anew once its own way is known (nothing carries over from an earlier move); a zoned unit stays zoned {ZPathing.ZonedHold:0.#} s after its last reason and switches back within {ZPathing.MinDwell:0.#} s only across a real zone edge, never more than twice within {ZPathing.MinDwell:0.#} s. A passage whose free run is 1 tile, or one a wreck narrows, is a zone for squads even one way (each minion its own Zod path, live single file). " +
                        $"The zone reasons apply in tight places (a zone or its band, a narrow run + {ZPathing.TightGrow}, a lane's keep-clear area, a passage) and within {ZPathing.ConfineBand} tile of them; {ZPathing.ZoneExitHold} s farther out a squad is DV44 again (a vehicle on a live group order keeps the live rules on the whole order). A group order whose clicked point lies within {ZPathing.GroupZoneReach} tiles of a zone, or whose way crosses one, gets the live spots and the live arrivals for every member until its next order; a player's ENTER order (and DV59's walk to the spot next to a vehicle someone else got first) runs the live rules too. " +
                        $"IN a zone the live layer: own paths (a minion that followed the trail walks on along its leader's way - the trail, then his path - no stop, never turned round to its leader: one level with / ahead of him keeps its own Zod path forward), the lanes' and the bridge queue's alternation, the live pushes and arrivals; only with a head-on reason (an active passage, a squeeze, an encounter - not one-way traffic, not on a bridge / water crossing / gate tile or its band, not for a live group order: those keep the live rules exactly, the live DV24 step uncapped and Zod's path at the hand-over) a minion blocked by a mate queues behind it (DV44's gating) and a held unit walks again once the gap has grown (no stop-go every other tick); a minion keeps its own path >= {ZPathing.RejoinHold:0} s after the zone and re-joins the trail where it runs straight (a stalled follower of a squad just out of a zone or in a tight place takes its own path after 1 s). " +
                        $"OUTSIDE the zones (DV44): PT1 a squad leader records the trail he walks; his minions keep their DV24 V slots measured ALONG that trail and go single file on it where the V does not fit, with hysteresis (file >= {ZPathing.FileMin:0} s, V back after {ZPathing.VReturnTicks / 100.0:0.0} s of free slots); minions make no searches of their own while they follow. " +
                        $"PACE: the leader and every unit on its own path walk at their Zod speed; a minion's top speed is its OWN Zod speed x DV24's catch-up ({cu}) on dry ground and x 1 in water or on any slow tile (following the trail and, under v3, in the live DV24 step too); it stops under {ZPathing.StopSpeed} px/s and walks again over {ZPathing.GoSpeed} px/s. " +
                        $"PT2 path searches are budgeted by their measured work: at most {ZPathing.SearchCap} requests start per tick and only while the tick's work is under {ZPathing.WorkBudget:N0} units; the rest queue first come first served (a unit in a zone plans at once, like live). " +
                        $"PT3 pushes between standing units have a {ZPathing.DeadBand} px dead-band and run per pair until clear; an arrived unit takes no share of a push against a unit that still has an order; a unit blocked near its goal by an arrived group mate arrives (robots {ZPathing.ArriveTolRobot} px, vehicles {ZPathing.ArriveTolVehicle} px, at most {ZPathing.GroupArriveMax} px short) or takes a free spot on its own side; group spots keep the group's shape within the live ring; long detours round standing units to a near point are refused; a new waypoint keeps the sub-pixel carry. " +
                        "Ranges, walkable tiles, water / bridge rules and unit behaviour are Zod's; speeds are Zod's except the following-minion pace above (DV24's catch-up, never in water)",
                measured = $"L02 round the vehicle factory (open ground), 6-grunt squad: v3 off {Run(o0)}; on {Run(o1)}. L02 N-S bridge, 6-grunt squad: off {Run(z0)}; on {Run(z1)}, {z1.zonedShare:0%} of its unit-ticks in a zone (live rules). " +
                           $"L02, 30 units ordered at once: path requests started in one tick (first 30 s) at most {m1.maxStarts} (cap {ZPathing.SearchCap}), {m1.maxSearches} searches with clean-ups. L02, 4 vehicles to one point: off settle {a0.settle:0.00} s, {a0.micro} micro-moves; on settle {a1.settle:0.00} s, {a1.micro} micro-moves. " +
                           $"Squad minions' pace against their own Zod speed (0.25 s windows; v3 on / off = the live DV24 pace): L17 canal water {sw.waterOver}/{sw.waterWin} over 1.00 x (max {sw.waterMax:0.00}) / off {sw0.waterOver}/{sw0.waterWin} (max {sw0.waterMax:0.00}), L02 road {sr.roadOver}/{sr.roadWin} over {cu} x (max {sr.roadMax:0.00}) / off {sr0.roadOver}/{sr0.roadWin} (max {sr0.roadMax:0.00}), ground {sr.groundOver + sw.groundOver}/{sr.groundWin + sw.groundWin} over {cu} x / off {sr0.groundOver + sw0.groundOver}/{sr0.groundWin + sw0.groundWin}",
                toggle = PvToggle(getInt),
                status = !ok ? DevCheck : pref ? DevActive : DevActive + " (off by pref)",
            };
        }

        static Deviation DevSoftUnstick(Func<string, int, int> getInt)
        {
            var j0 = PathV3Probe("jam", false); var j1 = PathV3Probe("jam", true);
            bool ok = j1.unsticks >= 1 && j1.stand <= 6.0 && j1.hardTicks == 0 && j1.left == 0 && j0.hardTicks == 0;
            bool pref = getInt("oz_path_v3", 1) == 1 && getInt("oz_zod_exact", 0) == 0;
            return new Deviation
            {
                id = "DV47", change = "Soft unstick (pathing v3): a unit stuck in a tight place for 5 s may step over soft debris - a wreck (DV39's harbour wrecks on L17; on L02 there is none) - never through a building, rock, prop, wall, cliff, water (vehicles) or a unit; in matches a destroyed vehicle, cannon or robot leaves no sim blocker, so the rule only matters at the L17 harbour wrecks",
                zod = "wrecks are impassable tiles of the Zod grid (DV39's harbour wrecks like every destroyable map item, omapobject.cpp:77): a unit without explosives paths round them or stops at the nearest reachable point; units with explosives blast them (zobject.cpp:3716-3747)",
                openz = $"a unit (a squad leader for his squad) with a move / attack order in a tight place (a zone, its band, a narrow passage, a lane's keep-clear area) that made < {ZPathing.UnstickProgress / 16:0} tile of NET progress along its way to the order's goal over {ZPathing.UnstickAfter:0} s of active time (helper moves and path waits pause the clock; back-and-forth is no progress) plans (every {ZPathing.UnstickRetry} s) a path on its own grid with the soft tiles open - the units in the way gone round first, then ignored; with it it may step onto soft tiles (Zod's wall test lets a step through when its box touches only free and soft tiles), a lane does not hold it (the debris is its second lane), and on the debris it never slides into a hard tile; the permission ends {ZPathing.UnstickClear} s after it is off every soft tile, when the order ends, or after {ZPathing.UnstickHold:0} s - never while it still stands on one. No debris near: the clock hands the unit and its squad to the live rules (at a passage, a zone or a lane) until it is {ZPathing.PassExit} steps past the passage and 2 tiles on, its order changes or {ZPathing.StuckLiveMax:0} s. A goal out of reach behind debris: the unit waits at its resolved point, then plans over the debris from there; debris closing a passage whose way round costs more than the wait + {ZPathing.ShortcutMargin:0} s: a robot walks to the debris, waits, then goes over. Soft = 'wreck' map items only; intact props (barrels, crates, stop signs, cacti), rocks, city blocks, huts, buildings, cliffs and water stay walls. The view draws the unit over the debris (its clearance push-out skips only the soft tiles; buildings and props still push it out)",
                measured = $"L17, a wreck closes one tile of the 2-tile rock-wall opening (68-70, 37-38), a 6-grunt squad and two 3-grunt squads meet in it head-on: v3 off (live) longest stand with an order {j0.stand:0.0} s, all stopped after {NbS(j0.arrived)}, {j0.reversals} reversals; v3 on {j1.stand:0.0} s ({j1.standWho}), all stopped after {NbS(j1.arrived)}, {j1.reversals} reversals, {j1.unsticks} unsticks, {j1.softUnits} units stepped over the wreck; unit-ticks on a hard blocked tile off {j0.hardTicks}, on {j1.hardTicks}; orders left off {j0.left}, on {j1.left}",
                toggle = PvToggle(getInt),
                status = !ok ? DevCheck : pref ? DevActive : DevActive + " (off by pref)",
            };
        }

#if !OPENZ_HARNESS
        const string MenuPathV3 = "OpenZ/Deviations/Pathing v3 (oz_path_v3)";
        [MenuItem(MenuPathV3)] static void TPathV3() => NbToggle("oz_path_v3", 1, "pathing v3: path commit outside bridges / bottlenecks + soft unstick (DV44 + DV47); off = the live pathing");
        [MenuItem(MenuPathV3, true)] static bool TPathV3C() { Menu.SetChecked(MenuPathV3, PlayerPrefs.GetInt("oz_path_v3", 1) == 1); return true; }

        [MenuItem("OpenZ/Deviations/Probe DV44 DV47 Pathing v3")]
        static void ProbePathV3Menu()
        {
            Func<string, int, int> get = (k, def) => PlayerPrefs.GetInt(k, def);
            var d = DevPathV3(get); var s = DevSoftUnstick(get);
            var path = System.IO.Path.Combine(OpenZPaths.Records, "path_v3_dv44_dv47.md");
            System.IO.File.WriteAllText(path, $"# Pathing v3 probes {DateTime.Now:yyyy-MM-dd HH:mm}\n\n## DV44 {d.status}\n\n- {d.measured.Replace(". ", "\n- ")}\n\n## DV47 {s.status}\n\n- {s.measured.Replace("; ", "\n- ")}\n");
            Debug.Log($"[DV44] {d.status}: {d.measured}\n[DV47] {s.status}: {s.measured}\n-> {path}");
        }

        [MenuItem("OpenZ/View/Pathing v3 Check (Play)")]
        static void PathV3CheckMenu()
        {
            if (!EditorApplication.isPlaying) { Debug.LogError("[pathing v3] enter Play first (L02 or L17, full look)"); return; }
            PathV3Check.Run();
        }
#endif
    }
}
