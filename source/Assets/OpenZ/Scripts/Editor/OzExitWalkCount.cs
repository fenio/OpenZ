using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using OpenZ.Sim;

namespace OpenZ.EditorTools
{
    /// <summary>
    /// no scene, nothing saved; public members only). Every stage lets a squad out UNDER FIRE: a blue grunt squad is placed about 10 m off
    /// the spot where the robots will stand, ordered onto that spot (it shoots whatever it meets) and, the moment the squad is out, ordered to
    /// attack its first robot. Every tick the rules' shots (Hitscan, MissileFired) by and at each robot let out are counted, and its place.
    ///  S1 the shuttle (level 16's own ship, a red grunt squad of 4): each robot's getting-out time = its ramp-foot moment + its walk to its
    ///     back slot at ZWorld.ExitWalkMps + ExitHandSec; until then it fires nothing and nobody shoots at it (the attack order on it is
    ///     refused); after it the squad fights (it fires within 3 s);
    ///  S2 the same with a MOVE order given the tick the squad is out: nobody takes a step before its own getting-out time, each walks after it;
    ///  H  ALAN &amp; BRAD let out of a ship they took empty (Brad its pilot): the same for the heroes (ShuttleHeroesOut);
    ///  A  an APC: a red grunt squad of 3 let out: its getting-out times (the ramp, 0.55 s a place, the 3.2 s walk), no shot by or at
    ///     them before, the squad fights after;
    ///  C  Zod's own crew exit (EJECT from a gatling gun): unchanged: no getting-out time, the gunner is never held (he fires as with the
    ///     rule off), the same shots, places and hit points with the rule on and off;
    ///  O  the rule off (S1 again): the robots fire / are shot at before their figures are out (each such shot squeezed the old exit walk:
    ///     the jump of item 443);
    ///  D  S1 twice from fresh worlds: the same digest (deterministic).
    /// </summary>
    public static class OzExitWalkCount
    {
        const double Tick = ZConst.TICK;
        static readonly CultureInfo Inv = CultureInfo.InvariantCulture;
        public const int SquadSize = 4;
        static string settings, m16;
        static StringBuilder sb; static int pass, fail;
        static void V(bool ok, string what) { sb.AppendLine(OzV11Kit.Verdict(ok, what)); if (ok) pass++; else fail++; }
        static void L(string s) => sb.AppendLine("      " + s);
        static string F(double v, string f = "0.00") => v.ToString(f, Inv);
        static bool Alive(ZObject o) => OzV11Kit.Alive(o);

        /// <summary>One robot let out: its rules' times, the shots by and at it, its place every tick (for the kit's headless trace too).</summary>
        public sealed class Robot
        {
            public int id, k; public string type = "", member = "";
            public double created, foot = -1, outAt = -1, slotWalk;
            public double firstShot = -1, firstShotAt = -1, firstMove = -1, movedBeforeOutPx;
            public int shotsBefore, shotAtBefore, shotsAfter, shotAtAfter, walkTicksBefore;
            public double x0, y0;
            /// <summary>Sim times of the shots by or at it (each one set the old exit walk's hurry).</summary>
            public readonly List<double> acts = new List<double>();
            /// <summary>Every tick: sim time, its middle (px, double), moving.</summary>
            public readonly List<(double t, double x, double y, bool moving)> track = new List<(double, double, double, bool)>();
        }

        /// <summary>One staged exit under fire.</summary>
        public sealed class Stage
        {
            public string what = ""; public ZWorld w; public ZObject carrier, blue; public List<Robot> robots = new List<Robot>();
            public double exitAt = -1, outAt = -1, endAt = -1; public int footX, footY; public bool orderRefused, ok = true; public string note = "";
        }

        public static void Prepare(string settingsText, string level16) { settings = settingsText; m16 = level16; }

        static void Run(ZWorld w, double secs, Func<bool> until = null)
        {
            double end = w.Time + secs;
            while (w.Time < end - 1e-9) { w.Tick(Tick); w.Events.Clear(); if (until != null && until()) return; }
        }

        static ZWorld W16(int seed, bool heroes)
        {
            ZWorld.ShuttlePlayableDeviation = true; ZWorld.ShuttleOn = true; ZWorld.ShuttleTeam = Team.Red; ZWorld.ShuttleRoughProofNext = false;
            bool was = ZWorld.HeroSquadDeviation; if (heroes) ZWorld.HeroSquadDeviation = true;
            var w = OzV11Kit.World(settings, m16, seed);
            ZWorld.HeroSquadDeviation = was;
            return w;
        }

        static void ClearAround(ZWorld w, int cx, int cy, int r, ICollection<ZObject> keep)
        {
            foreach (var o in w.Objects.ToList())
            {
                if (!(o.IsRobot || (o.Kind == ZKind.Vehicle && !o.IsShuttle) || o.Kind == ZKind.Cannon) || !Alive(o) || (keep != null && keep.Contains(o))) continue;
                double d = Math.Sqrt((double)(o.CenterX - cx) * (o.CenterX - cx) + (double)(o.CenterY - cy) * (o.CenterY - cy));
                if (d < r) o.KillAt = w.Time;
            }
            Run(w, 0.2);
        }

        static bool FreeSpot(ZWorld w, int x, int y, out int fx, out int fy, ZObject keepFrom = null, double minFrom = 0, int reach = 6)
        {
            fx = fy = 0; double best = 1e18;
            for (int ty = (y >> 4) - reach; ty <= (y >> 4) + reach; ty++)
                for (int tx = (x >> 4) - reach; tx <= (x >> 4) + reach; tx++)
                {
                    if (tx < 2 || ty < 2 || tx >= w.Map.width - 2 || ty >= w.Map.height - 2) continue;
                    var c = w.Map.ClassAt(tx, ty);
                    if ((c != ZClass.Ground && c != ZClass.Road) || !w.Path.TilePassable(tx, ty, true) || !w.Path.TilePassable(tx, ty, false)) continue;
                    int mx = tx * 16 + 8, my = ty * 16 + 8;
                    if (w.Objects.Any(o => !o.Destroyed && o.Contains(mx, my))) continue;
                    if (keepFrom != null && Math.Sqrt((double)(mx - keepFrom.CenterX) * (mx - keepFrom.CenterX) + (double)(my - keepFrom.CenterY) * (my - keepFrom.CenterY)) < minFrom) continue;
                    double d = (double)(mx - x) * (mx - x) + (double)(my - y) * (my - y);
                    if (d < best) { best = d; fx = mx; fy = my; }
                }
            return best < 1e18;
        }

        /// <summary>A blue grunt squad of 3 about 10 m to the side of (px, py) (on open ground, clear of the carrier), ordered onto that spot
        /// (attack-move: it shoots whatever it meets on the way).</summary>
        static ZObject Enemy(ZWorld w, ZObject carrier, int px, int py, double sideX, double sideY)
        {
            foreach (int sgn in new[] { 1, -1 })
                foreach (int dist in new[] { 80, 96, 64, 112 })
                {
                    if (!FreeSpot(w, px + (int)(sgn * sideX * dist), py + (int)(sgn * sideY * dist), out int fx, out int fy, carrier, 72, 3)) continue;
                    var b = w.CreateRobotGroup("grunt", fx - 8, fy - 8, Team.Blue, 3);
                    if (b == null) continue;
                    OzV11Kit.PlayerMove(w, b, px, py, true);
                    return b;
                }
            return null;
        }

        static Robot Track(ZObject m, int k, double t)
        {
            var r = new Robot { id = m.Id, k = k, type = m.Type ?? "", member = m.HeroMemberName ?? "", created = t, outAt = m.ExitOutAt, x0 = m.PosX, y0 = m.PosY };
            return r;
        }

        /// <summary>Ticks the world for `secs`, counting the shots by / at the robots let out and their places.</summary>
        static void Watch(Stage st, double secs, Action<ZWorld> eachTick = null)
        {
            var w = st.w; double end = w.Time + secs;
            var byId = st.robots.ToDictionary(r => r.id);
            while (w.Time < end - 1e-9)
            {
                eachTick?.Invoke(w);
                w.Tick(Tick);
                double t = w.Time;
                foreach (var e in w.Events)
                {
                    if (e.type != ZEventType.Hitscan && e.type != ZEventType.MissileFired) continue;
                    if (byId.TryGetValue(e.objId, out var a))
                    {
                        a.acts.Add(t);
                        bool before = a.outAt >= 0 && t < a.outAt - 1e-9;
                        if (before) a.shotsBefore++; else a.shotsAfter++;
                        if (a.firstShot < 0) a.firstShot = t;
                        if (!before && a.firstShotAt < 0) a.firstShotAt = t;
                    }
                    if (byId.TryGetValue(e.otherId, out var b))
                    {
                        b.acts.Add(t);
                        if (b.outAt >= 0 && t < b.outAt - 1e-9) b.shotAtBefore++; else b.shotAtAfter++;
                    }
                }
                w.Events.Clear();
                foreach (var r in st.robots)
                {
                    var o = w.Get(r.id);
                    if (o == null) continue;
                    r.track.Add((t, o.PosX, o.PosY, o.IsMoving));
                    double moved = Math.Sqrt((o.PosX - r.x0) * (o.PosX - r.x0) + (o.PosY - r.y0) * (o.PosY - r.y0));
                    bool beforeOut = r.outAt >= 0 && t < r.outAt - 1e-9;
                    if (beforeOut) { r.movedBeforeOutPx = Math.Max(r.movedBeforeOutPx, moved); if (o.IsMoving) r.walkTicksBefore++; }   // displacement: also the pathing layer's push off a hull; IsMoving: a step of its own
                    if (r.firstMove < 0 && o.IsMoving) r.firstMove = t;
                }
            }
            st.endAt = w.Time;
        }

        static (double x, double y) Axis(ZObject s)
        {
            var (ax, ay) = s.ShuttleBackPoint(1); var (bx, by) = s.ShuttleBackPoint(4);
            double dx = bx - ax, dy = by - ay, l = Math.Sqrt(dx * dx + dy * dy);
            return l > 1e-6 ? (dx / l, dy / l) : (1, 0);
        }

        // ------------------------------------------------------------------ the shuttle (S1, S2, O, D) and the heroes (H)
        /// <summary>Level 16's own ship with a red grunt squad of 4 aboard (or ALAN &amp; BRAD in a ship they took empty), EXIT under fire.
        /// order: a MOVE order to the squad the tick it is out. hold: the DV132 switch.</summary>
        public static Stage Shuttle(bool hold, bool order, int seed, bool heroes = false, double watch = 0)
        {
            bool was = ZWorld.ExitWalkHoldOn; ZWorld.ExitWalkHoldOn = hold;
            var st = new Stage { what = heroes ? "H" : "S" };
            try
            {
                var w = st.w = W16(seed, heroes);
                ZObject s, lead;
                if (!heroes)
                {
                    s = w.PlayableShuttle;
                    if (s == null) { st.ok = false; st.note = "no ship"; return st; }
                    ClearAround(w, s.CenterX, s.CenterY, 300, null);
                    var (bx, by) = s.ShuttleBackPoint(5);
                    FreeSpot(w, bx, by, out int fx, out int fy, s, 70);
                    lead = w.CreateRobotGroup("grunt", fx - 8, fy - 8, Team.Red, SquadSize);
                    OzV11Kit.PlayerEnter(w, lead, s);
                    Run(w, 60, () => s.Passengers.Count > 0);
                }
                else
                {
                    lead = w.Objects.FirstOrDefault(o => o.IsHero && o.HeroMemberName == "brad" && Alive(o) && o.Owner == Team.Red);
                    if (lead == null) { st.ok = false; st.note = "no hero squad"; return st; }
                    s = null;
                    for (int r = 200; r <= 700 && s == null; r += 32)
                        for (int a = 0; a < 16 && s == null; a++)
                        {
                            double g = a * Math.PI / 8;
                            s = w.SpawnShuttle(lead.CenterX + (int)(Math.Cos(g) * r), lead.CenterY + (int)(Math.Sin(g) * r), Team.Neutral, -90f, out _);
                        }
                    if (s == null) { st.ok = false; st.note = "no empty drop"; return st; }
                    var keep = new HashSet<ZObject>(OzV11Kit.Squad(lead)) { s };
                    ClearAround(w, s.CenterX, s.CenterY, 300, keep);
                    OzV11Kit.PlayerEnter(w, lead, s);
                    Run(w, 60, () => s.Drivers.Count > 0);
                }
                st.carrier = s;
                var ww = w;
                Run(w, 25, () => s.ShuttleRampPhase == ZShuttleRamp.Shut && !s.ShuttleExitPending && ww.Time >= s.ShuttleRampHoldUntil);
                if (!s.ShuttleHasLeavers) { st.ok = false; st.note = "nobody aboard"; return st; }
                var (sx, sy) = Axis(s); var (mx, my) = s.ShuttleBackPoint(2);
                st.blue = Enemy(w, s, mx, my, -sy, sx);
                if (st.blue == null) { st.ok = false; st.note = "no spot for the enemy"; return st; }
                st.exitAt = w.Time;
                if (!w.Eject(Team.Red, s)) { st.ok = false; st.note = "EXIT refused"; return st; }
                Run(w, 12, () => s.ShuttleFootIds.Count > 0 && s.ShuttleLastRampUse == ZShuttleRampUse.Unloaded && (heroes ? s.Drivers.Count == 0 : s.Passengers.Count == 0));
                st.outAt = w.Time;
                (st.footX, st.footY) = s.ShuttleBackPoint(0);
                for (int k = 0; k < s.ShuttleFootIds.Count; k++)
                {
                    var m = w.Get(s.ShuttleFootIds[k]); if (m == null) continue;
                    var r = Track(m, k, w.Time); r.foot = k < s.ShuttleFootTimes.Count ? s.ShuttleFootTimes[k] : -1;
                    double dx = m.CenterX - st.footX, dy = m.CenterY - st.footY;
                    r.slotWalk = Math.Max(ZWorld.ExitWalkMinSec, Math.Sqrt(dx * dx + dy * dy) / 8.0 / ZWorld.ExitWalkMps);
                    if (!hold) r.outAt = r.foot + r.slotWalk + ZWorld.ExitHandSec;          // the rule off: the moment its figure would be out, for the counts
                    st.robots.Add(r);
                }
                if (st.robots.Count > 0)
                {
                    var first = w.Get(st.robots[0].id);
                    int n0 = st.blue.Waypoints.Count;
                    OzV11Kit.CpuAttack(w, st.blue, first);                                       // an enemy told to shoot the first robot out
                    st.orderRefused = st.blue.Waypoints.Count == 0 || st.blue.Waypoints[0].mode != WpMode.Attack || st.blue.Waypoints[0].refId != first.Id;
                    if (st.orderRefused && n0 > 0) OzV11Kit.PlayerMove(w, st.blue, mx, my, true); // refused (DV132): it keeps its walk onto the spot
                    if (order)
                    {
                        var ld = first.Leader ?? first;
                        double d = 56; var (tx, ty) = (st.footX + (int)(sx * (64 + d)), st.footY + (int)(sy * (64 + d)));
                        FreeSpot(w, tx, ty, out int gx, out int gy);
                        OzV11Kit.PlayerMove(w, ld, gx, gy, false);
                    }
                }
                double last = st.robots.Count > 0 ? st.robots.Max(r => r.outAt) : w.Time;
                Watch(st, watch > 0 ? watch : Math.Max(4.0, last - w.Time + 4.0));
            }
            finally { ZWorld.ExitWalkHoldOn = was; }
            return st;
        }

        // ------------------------------------------------------------------ the APC (A)
        public static Stage Apc(bool hold, int seed)
        {
            bool was = ZWorld.ExitWalkHoldOn; ZWorld.ExitWalkHoldOn = hold;
            var st = new Stage { what = "A" };
            try
            {
                var w = st.w = W16(seed, false);
                var s = w.PlayableShuttle;
                int cx = s != null ? s.CenterX : w.Map.width * 8, cy = s != null ? s.CenterY : w.Map.height * 8;
                ClearAround(w, cx, cy, 300, null);
                if (!FreeSpot(w, cx + 96, cy + 96, out int ax, out int ay, s, 90)) { st.ok = false; st.note = "no APC spot"; return st; }
                var apc = w.CreateObject("vehicle", "apc", ax - 16, ay - 16, Team.Red);
                st.carrier = apc;
                var lead = w.CreateRobotGroup("grunt", apc.LocX + 8, apc.LocY + 8, Team.Red, 3);         // staged on the APC (the boarding walk is not what is counted here)
                OzV11Kit.PlayerEnter(w, lead, apc);
                Run(w, 10, () => apc.Passengers.Count > 0);
                if (apc.Passengers.Count == 0) { st.ok = false; st.note = "nobody boarded the APC"; return st; }
                Run(w, 1.0);
                st.blue = Enemy(w, apc, apc.CenterX, apc.CenterY, 0.7071, 0.7071);
                if (st.blue == null) { st.ok = false; st.note = "no spot for the enemy"; return st; }
                st.exitAt = w.Time;
                if (!w.Eject(Team.Red, apc)) { st.ok = false; st.note = "EXIT refused"; return st; }
                st.outAt = w.Time;
                var lead2 = w.Objects.Where(o => o.IsRobot && o.Owner == Team.Red && Alive(o)).OrderByDescending(o => o.Id).FirstOrDefault(o => !o.IsMinion);
                var squad = lead2 != null ? OzV11Kit.Squad(lead2).ToList() : new List<ZObject>();
                for (int i = 0; i < squad.Count; i++) st.robots.Add(Track(squad[i], i, w.Time));
                if (!hold) foreach (var r in st.robots) r.outAt = st.outAt + ZWorld.ApcExitOpenSec + r.k * ZWorld.ApcExitGapSec + ZWorld.ApcExitWalkSec + ZWorld.ExitHandSec;
                if (st.robots.Count > 0) { OzV11Kit.CpuAttack(w, st.blue, w.Get(st.robots[0].id)); st.orderRefused = st.blue.Waypoints.Count == 0 || st.blue.Waypoints[0].mode != WpMode.Attack; }
                if (st.orderRefused) OzV11Kit.PlayerMove(w, st.blue, apc.CenterX, apc.CenterY, true);
                double last = st.robots.Count > 0 ? st.robots.Max(r => r.outAt) : w.Time;
                Watch(st, Math.Max(4.0, last - w.Time + 4.0));
            }
            finally { ZWorld.ExitWalkHoldOn = was; }
            return st;
        }

        // ------------------------------------------------------------------ Zod's own crew exit (C)
        public static Stage Cannon(bool hold, int seed)
        {
            bool was = ZWorld.ExitWalkHoldOn; ZWorld.ExitWalkHoldOn = hold;
            var st = new Stage { what = "C" };
            try
            {
                var w = st.w = W16(seed, false);
                var s = w.PlayableShuttle;
                int cx = s != null ? s.CenterX : w.Map.width * 8, cy = s != null ? s.CenterY : w.Map.height * 8;
                ClearAround(w, cx, cy, 300, null);
                if (!FreeSpot(w, cx + 96, cy + 96, out int gx, out int gy, s, 90)) { st.ok = false; st.note = "no gun spot"; return st; }
                var gun = w.CreateObject("cannon", "gatling", gx - 16, gy - 16, Team.Red);
                st.carrier = gun;
                Run(w, 0.5);
                int maxId = w.Objects.Max(o => o.Id);
                st.exitAt = w.Time;
                if (!w.Eject(Team.Red, gun)) { st.ok = false; st.note = "EJECT refused"; return st; }
                st.outAt = w.Time;
                st.blue = Enemy(w, gun, gun.CenterX, gun.CenterY, 0.7071, 0.7071);              // placed the moment he is out (the manned gatling would have shot it first)
                if (st.blue == null) { st.ok = false; st.note = "no spot for the enemy"; return st; }
                var crew = w.Objects.Where(o => o.IsRobot && o.Owner == Team.Red && Alive(o) && o.Id > maxId).OrderBy(o => o.Id).ToList();
                for (int i = 0; i < crew.Count; i++) { var r = Track(crew[i], i, w.Time); r.outAt = -1; st.robots.Add(r); }
                Watch(st, 4.0);
            }
            finally { ZWorld.ExitWalkHoldOn = was; }
            return st;
        }

        static string Digest(Stage st) =>
            st.what + "|" + string.Join(";", st.robots.Select(r => $"{r.k}:{F(r.outAt, "0.000")}:{F(r.firstShot, "0.000")}:{r.shotsBefore}/{r.shotAtBefore}/{r.shotsAfter}/{r.shotAtAfter}:{(r.track.Count > 0 ? F(r.track[r.track.Count - 1].x, "0.0") + "," + F(r.track[r.track.Count - 1].y, "0.0") : "-")}")) +
            "|" + string.Join(";", st.robots.Select(r => { var o = st.w.Get(r.id); return o == null ? "gone" : o.Health.ToString(Inv); }));

        static string Line(Stage st, Robot r) =>
            $"{st.what} #{r.k} {r.type}{(r.member != "" ? " (" + r.member + ")" : "")}: {(r.outAt < 0 ? "no getting-out time" : "out at +" + F(r.outAt - st.outAt) + " s")}" + (r.foot >= 0 ? $" (ramp foot +{F(r.foot - st.outAt)}, walk to its slot {F(r.slotWalk)} s)" : "") +
            $"; shots by it before / after {r.shotsBefore} / {r.shotsAfter} (first {(r.firstShot < 0 ? "none" : "+" + F(r.firstShot - st.outAt))}), at it before / after {r.shotAtBefore} / {r.shotAtAfter}; walking ticks before out {r.walkTicksBefore} (pushed / moved {F(r.movedBeforeOutPx / 8.0)} m), first step {(r.firstMove < 0 ? "none" : "+" + F(r.firstMove - st.outAt))}";

        // ------------------------------------------------------------------ the whole check
        public static string Check(string settingsText, string level16)
        {
            Prepare(settingsText, level16);
            sb = new StringBuilder(); pass = fail = 0;
            bool was = ZWorld.ShuttlePlayableDeviation, wasOn = ZWorld.ShuttleOn, heroWas = ZWorld.HeroSquadDeviation, holdWas = ZWorld.ExitWalkHoldOn; var team0 = ZWorld.ShuttleTeam;
            try
            {
                sb.AppendLine($"K441 / DV132 out of the carrier first (level 16, under fire: a blue grunt squad about 10 m off; the shuttle's walk to the slot at {F(ZWorld.ExitWalkMps, "0.0")} m/s, " +
                              $"the APC's ramp {F(ZWorld.ApcExitOpenSec)} s + {F(ZWorld.ApcExitGapSec)} s a place + its {F(ZWorld.ApcExitWalkSec, "0.0")} s walk, the hand-over {F(ZWorld.ExitHandSec)} s)");
                V(ZWorld.ExitWalkHoldOn, "E0 the DV132 switch is on (ZWorld.ExitWalkHoldOn)");

                // S1
                var s1 = Shuttle(true, false, 21);
                sb.AppendLine("S1. the shuttle, a red grunt squad of 4 let out under fire");
                if (!s1.ok) { V(false, "S1 staged: " + s1.note); }
                else
                {
                    foreach (var r in s1.robots) L(Line(s1, r));
                    bool formula = s1.robots.Count == SquadSize && s1.robots.All(r => r.foot >= 0 && Math.Abs(r.outAt - (r.foot + r.slotWalk + ZWorld.ExitHandSec)) < 1e-9);
                    V(formula, $"S1 {s1.robots.Count} robots out of the ship, each with its getting-out time = its ramp-foot moment + its walk to its slot + {F(ZWorld.ExitHandSec)} s ({string.Join(", ", s1.robots.Select(r => "+" + F(r.outAt - s1.outAt)))} s after they are let out)");
                    V(s1.robots.Count > 0 && s1.robots.All(r => r.shotsBefore == 0), $"S1 no robot fired before its figure was out (shots by them before: {s1.robots.Sum(r => r.shotsBefore)})");
                    V(s1.orderRefused && s1.robots.All(r => r.shotAtBefore == 0), $"S1 nobody shot at a robot still getting out (shots at them before: {s1.robots.Sum(r => r.shotAtBefore)}; the enemy's attack order on the first robot refused: {s1.orderRefused})");
                    V(s1.robots.All(r => r.walkTicksBefore == 0 && r.movedBeforeOutPx < 2.0), $"S1 nobody moved before it was out (walking ticks {s1.robots.Sum(r => r.walkTicksBefore)}, most {F(s1.robots.Max(r => r.movedBeforeOutPx) / 8.0)} m)");
                    bool fight = s1.robots.Any(r => r.firstShotAt >= 0 && r.firstShotAt <= r.outAt + 3.0);
                    V(fight, $"S1 once out the squad fights: {s1.robots.Count(r => r.shotsAfter > 0)} of {s1.robots.Count} fired after their getting-out time (first after it: {string.Join(", ", s1.robots.Select(r => r.firstShotAt < 0 ? "none" : "+" + F(r.firstShotAt - r.outAt)))} s)");
                }

                // S2
                var s2 = Shuttle(true, true, 21);
                sb.AppendLine("S2. the same, a MOVE order given the tick the squad is out");
                if (!s2.ok) V(false, "S2 staged: " + s2.note);
                else
                {
                    foreach (var r in s2.robots) L(Line(s2, r));
                    V(s2.robots.All(r => r.walkTicksBefore == 0 && r.movedBeforeOutPx < 2.0), $"S2 nobody took a step before its own getting-out time although ordered (walking ticks {s2.robots.Sum(r => r.walkTicksBefore)}, most {F(s2.robots.Max(r => r.movedBeforeOutPx) / 8.0)} m)");
                    V(s2.robots.All(r => r.firstMove >= r.outAt - 1e-9 && r.firstMove <= r.outAt + 1.5), $"S2 each walks off once out (first step after its getting-out time: {string.Join(", ", s2.robots.Select(r => r.firstMove < 0 ? "none" : "+" + F(r.firstMove - r.outAt)))} s)");
                }

                // H
                var h = Shuttle(true, false, 11, heroes: true);
                sb.AppendLine("H. ALAN & BRAD let out of a ship they took empty (Brad its pilot)");
                if (!h.ok) V(false, "H staged: " + h.note);
                else
                {
                    foreach (var r in h.robots) L(Line(h, r));
                    bool both = h.robots.Count == 2 && h.robots.Any(r => r.member == "brad") && h.robots.Any(r => r.member == "alan");
                    V(both && h.robots.All(r => r.outAt > h.outAt && Math.Abs(r.outAt - (r.foot + r.slotWalk + ZWorld.ExitHandSec)) < 1e-9), $"H both heroes as themselves, each with its getting-out time ({string.Join(", ", h.robots.Select(r => r.member + " +" + F(r.outAt - h.outAt)))} s)");
                    V(h.robots.All(r => r.shotsBefore == 0 && r.shotAtBefore == 0 && r.walkTicksBefore == 0), $"H no shot by or at a hero and no step before he was out (by {h.robots.Sum(r => r.shotsBefore)}, at {h.robots.Sum(r => r.shotAtBefore)})");
                    V(h.robots.Any(r => r.firstShotAt >= 0 && r.firstShotAt <= r.outAt + 3.0), $"H once out the heroes fight ({h.robots.Count(r => r.shotsAfter > 0)} of {h.robots.Count} fired)");
                }

                // A
                var a = Apc(true, 31);
                sb.AppendLine("A. an APC, a red grunt squad of 3 let out under fire");
                if (!a.ok) V(false, "A staged: " + a.note);
                else
                {
                    foreach (var r in a.robots) L(Line(a, r));
                    bool times = a.robots.Count > 0 && a.robots.All(r => Math.Abs(r.outAt - (a.outAt + ZWorld.ApcExitOpenSec + r.k * ZWorld.ApcExitGapSec + ZWorld.ApcExitWalkSec + ZWorld.ExitHandSec)) < 1e-9);
                    V(times, $"A {a.robots.Count} robots out of the APC, each with its getting-out time ({string.Join(", ", a.robots.Select(r => "+" + F(r.outAt - a.outAt)))} s)");
                    V(a.robots.All(r => r.shotsBefore == 0 && r.shotAtBefore == 0 && r.walkTicksBefore == 0), $"A no shot by or at them and no step of their own before they were out (by {a.robots.Sum(r => r.shotsBefore)}, at {a.robots.Sum(r => r.shotAtBefore)}, walking ticks {a.robots.Sum(r => r.walkTicksBefore)}; the pathing layer pushed them off the APC's hull at most {F(a.robots.Select(r => r.movedBeforeOutPx).DefaultIfEmpty(0).Max() / 8.0)} m, as with the rule off)");
                    V(a.robots.Any(r => r.firstShotAt >= 0 && r.firstShotAt <= r.outAt + 3.0), $"A once out the squad fights ({a.robots.Count(r => r.shotsAfter > 0)} of {a.robots.Count} fired)");
                }

                // C
                var c1 = Cannon(true, 41); var c0 = Cannon(false, 41);
                sb.AppendLine("C. Zod's own crew exit (EJECT from a gatling gun), unchanged");
                if (!c1.ok || !c0.ok) V(false, "C staged: " + c1.note + " " + c0.note);
                else
                {
                    foreach (var r in c1.robots) L(Line(c1, r));
                    var g1 = c1.w.Get(c1.robots.Count > 0 ? c1.robots[0].id : -1);
                    V(c1.robots.Count > 0 && c1.robots.All(r => r.outAt < 0) && (g1 == null || g1.ExitOutAt < 0), $"C the gunner gets no getting-out time ({c1.robots.Count} robot(s) out)");
                    V(c1.robots.Count > 0 && c0.robots.Count > 0 && c1.robots[0].firstShot >= 0 && Math.Abs(c1.robots[0].firstShot - c0.robots[0].firstShot) < 1e-9, $"C he is not held: he fires as he does with the rule off (first shot +{(c1.robots.Count > 0 && c1.robots[0].firstShot >= 0 ? F(c1.robots[0].firstShot - c1.outAt) : "none")} s, rule off +{(c0.robots.Count > 0 && c0.robots[0].firstShot >= 0 ? F(c0.robots[0].firstShot - c0.outAt) : "none")} s: when the enemy walked into his range)");
                    string d1 = Digest(c1), d0 = Digest(c0);
                    V(d1.Replace("C|", "") == d0.Replace("C|", ""), "C the same shots, places and hit points with the rule on and off");
                    L("C on:  " + d1); L("C off: " + d0);
                }

                // O
                var o = Shuttle(false, false, 21);
                sb.AppendLine("O. the rule off (S1 again): what the old exit walks met");
                if (!o.ok) V(false, "O staged: " + o.note);
                else
                {
                    foreach (var r in o.robots) L(Line(o, r) + $"; acts before its figure was out {o.robots.First(x => x.id == r.id).acts.Count(t => t < r.outAt)}");
                    int acted = o.robots.Count(r => r.acts.Any(t => t < r.outAt));
                    V(acted > 0, $"O with the rule off {acted} of {o.robots.Count} robots fired or were shot at while their figures were still walking out (each such shot squeezed the old walk into 0.2 s: the jump of item 443)");
                }

                // D
                var d2 = Shuttle(true, false, 21);
                bool same = s1.ok && d2.ok && Digest(s1) == Digest(d2);
                V(same, "D S1 again from a fresh world: the same getting-out times, shots, places and hit points");
                L("S1 digest: " + (s1.ok ? Digest(s1) : "-"));
            }
            catch (Exception ex) { V(false, "the check threw " + ex.GetType().Name + ": " + ex.Message + " " + ex.StackTrace); }
            finally { ZWorld.ShuttlePlayableDeviation = was; ZWorld.ShuttleOn = wasOn; ZWorld.HeroSquadDeviation = heroWas; ZWorld.ExitWalkHoldOn = holdWas; ZWorld.ShuttleTeam = team0; ZWorld.ShuttleRoughProofNext = false; }
            sb.AppendLine($"K441 result: {pass} PASS, {fail} FAIL");
            return sb.ToString();
        }
    }
}
