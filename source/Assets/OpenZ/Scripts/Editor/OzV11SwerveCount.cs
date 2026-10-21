using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using OpenZ.Sim;

namespace OpenZ.EditorTools
{
    /// <summary>
    /// Counted check of bug b49 "units swerve" (the rules: Sim/ZCalm.cs and Sim/ZObject.Calm.cs, the switch ZWorld.CalmMoveDeviation).
    /// Headless simulation; every case runs with the switch off (the game before) and on.
    ///  1. ONE unit alone on a map (a light tank, a squad of grunts), one move order: across the bridges of the Desert and the Jungle
    ///     from beside the bridge end, from beside the shore and from far away, over open ground and along a road. "Smooth" as numbers:
    ///     the track is cut into chords of 3 px; a turn = 15 degrees or more between two chords; a REVERSAL = a turn to one side and,
    ///     within 2 s, a turn to the other side; the detour = the way travelled / the way planned when the order started.
    ///     And ONE VEHICLE alone sent across a bridge from a few tiles beside one of its ends to a few tiles beside the other (a light
    ///     tank, a jeep): no turn-and-turn-back and no turn back (a turn of more than 120 degrees at one point: the unit drives through
    ///     a point and comes back).
    ///  2. Fifteen mixed groups of one team through the Desert's two-deck bridge and through the Jungle's bridge: reversals per body,
    ///     nobody left holding its order, no two stopped units on top of each other, no vehicle faster than its own speed.
    ///  3. A unit of the PLAYER on an attack-move past enemies that stand and shoot (a sniper squad, a medium tank) and through a gap
    ///     in which an enemy vehicle stands for 110 s (a pyro squad): the order is never lost, a stop to fight never takes it off its way.
    ///  4. Who steps aside from a shell or a grenade: Tough and Sniper squads never, grunt and psycho squads not for every grenade
    ///     (a squad that has just stepped aside rests), counted in fights against grunts that throw grenades.
    ///  5. With the switch off nothing of the rule acts.
    /// Every run has the game's other rules on (OzV11Kit.GameSwitches) and, when the project has it, the rule "faster units pass slower ones".
    /// Menu: OpenZ/V1.1/Count Calm Movement (b49). About two minutes in the editor. Not in Play mode.
    /// In Play mode: OpenZ/V1.1/Calm Movement Meter (Play)/Start, give the orders, then Stop and report: the same meter on the units
    /// of the player's team in the running game.
    /// </summary>
    public static class OzV11SwerveCount
    {
        public const string ReportPath = "OzV11SwerveCount.txt";
        public const double Chord = 3, TurnDeg = 15, Window = 2.0, PaceSlackPx = 2, BackDeg = 120;

        static string F(double v, string f = "0.0") => double.IsInfinity(v) || double.IsNaN(v) ? "never" : v.ToString(f, CultureInfo.InvariantCulture);

        /// <summary>The smoothness meter of one body.</summary>
        public sealed class Meter
        {
            public readonly ZObject U; public double Path, TurnSum; public int Reversals, Turns, Backs;
            double px, py, ax, ay, hx, hy, lastT = -100; bool has; int lastSign;
            public Meter(ZObject u) { U = u; px = ax = u.PosX; py = ay = u.PosY; }
            public void Step(double t)
            {
                var u = U; if (u.Destroyed) return;
                double dx = u.PosX - px, dy = u.PosY - py; Path += Math.Sqrt(dx * dx + dy * dy); px = u.PosX; py = u.PosY;
                double cx = u.PosX - ax, cy = u.PosY - ay, cl = Math.Sqrt(cx * cx + cy * cy);
                if (cl < Chord) return;
                cx /= cl; cy /= cl; ax = u.PosX; ay = u.PosY;
                if (has)
                {
                    double d = Math.Atan2(hx * cy - hy * cx, hx * cx + hy * cy) * 180 / Math.PI; TurnSum += Math.Abs(d);
                    if (Math.Abs(d) >= TurnDeg)
                    {
                        int sg = d > 0 ? 1 : -1; Turns++;
                        if (Math.Abs(d) > BackDeg) Backs++;
                        if (lastSign != 0 && sg != lastSign && t - lastT <= Window) Reversals++;
                        lastSign = sg; lastT = t;
                    }
                }
                hx = cx; hy = cy; has = true;
            }
            public double TurnPer10m => Path > 1 ? TurnSum / (Path / 80) : 0;
        }

        static readonly string[] WorldCounters = { "CalmOrderAsidesHeld", "CalmDodgesEnded", "CalmDodgesHeld", "CalmRobotDodgesHeld", "CalmShootsFirst", "CalmStoodFirm", "CalmChasesEnded" };
        static void Counters0() { foreach (var n in WorldCounters) OzV11Kit.TrySet(typeof(ZWorld), n, 0); }
        /// <summary>How often any part of the rule acted in a world (its pathing counters and the static fight counters).</summary>
        static int Acts(ZWorld w)
        {
            int n = 0;
            foreach (var s in WorldCounters) n += OzV11Kit.Counter(typeof(ZWorld), s);
            var p = w?.Pathing;
            if (p != null) n += p.CalmPointsCut + p.CalmFreshSkips + p.CalmRouteKeeps + p.CalmCloseUnits + p.CalmQueued + p.CalmRounds + p.CalmYieldRests + p.CalmGoalsKept + p.CalmMovesKept + p.CalmWedgesEnded;
            return n;
        }

        static ZWorld Bare(string settings, string map)
        {
            var m = ZMapData.FromJson(OzV11Kit.NoUnits(map));
            m.objects.RemoveAll(o => o.kind == "map_item:flag" || o.kind == "map_item:grenades");   // (a unit on an order takes those beside its way: here only its own way is measured)
            var w = OzV11Kit.World(settings, UnityEngine.JsonUtility.ToJson(m), 1);
            OzV11Kit.Dummy(w, 5 * 16 + 8, 5 * 16 + 8);
            return w;
        }

        // ------------------------------------------------------------------ 1. one unit alone
        public sealed class LoneResult { public int reversals, backs, followerReversals, acts; public double detour, turn, time; public bool arrived; }

        public static LoneResult Lone(string settings, string map, string type, int fx, int fy, int tx, int ty, bool on)
        {
            bool was = ZWorld.CalmMoveDeviation; ZWorld.CalmMoveDeviation = on; Counters0();
            try
            {
                var w = Bare(settings, map);
                var u = OzV11Kit.Place(w, type, fx * 16 + 16, fy * 16 + 16, Team.Red);
                var all = OzV11Kit.Squad(u).ToList();
                for (int i = 0; i < 300; i++) { w.Tick(ZConst.TICK); w.Events.Clear(); }
                double t0 = w.Time; int gx = tx * 16 + 8, gy = ty * 16 + 8;
                OzV11Kit.PlayerMove(w, u, gx, gy);
                var meters = all.Select(o => new Meter(o)).ToList(); double planned = double.NaN, arrived = double.PositiveInfinity;
                while (w.Time - t0 < 400)
                {
                    w.Tick(ZConst.TICK); w.Events.Clear();
                    foreach (var m in meters) m.Step(w.Time);
                    if (double.IsNaN(planned) && u.Waypoints.Count > 0) { double rest = u.CalmPathRest; if (rest >= 0) planned = rest + meters[0].Path; }   // the way planned when the order started
                    if (u.Waypoints.Count == 0) { arrived = w.Time - t0; break; }
                }
                double end = Math.Sqrt((double)(u.CenterX - gx) * (u.CenterX - gx) + (double)(u.CenterY - gy) * (u.CenterY - gy)) / 16;
                return new LoneResult { reversals = meters[0].Reversals, backs = meters[0].Backs, followerReversals = meters.Skip(1).Sum(m => m.Reversals), detour = planned > 1 ? meters[0].Path / planned : 1, turn = meters[0].TurnPer10m, time = arrived, arrived = !double.IsInfinity(arrived) && end <= 3, acts = Acts(w) };
            }
            finally { ZWorld.CalmMoveDeviation = was; }
        }

        /// <summary>The lone cases: level, name, from (tile), to (tile).</summary>
        public static readonly (string level, string name, int fx, int fy, int tx, int ty)[] LoneCases =
        {
            ("L02", "two-deck bridge, from its end", 23, 46, 55, 46), ("L02", "two-deck bridge, from beside the shore", 24, 40, 55, 46), ("L02", "two-deck bridge, from the other shore side", 24, 52, 55, 46),
            ("L02", "two-deck bridge, from far", 6, 50, 55, 46), ("L02", "two-deck bridge back, from its end", 49, 46, 17, 46), ("L02", "two-deck bridge back, from beside the shore", 48, 40, 17, 46),
            ("L02", "north bridge, from its end", 19, 23, 19, 41), ("L02", "north bridge, from far", 30, 7, 19, 41), ("L02", "south bridge, from its end", 53, 55, 49, 69), ("L02", "south bridge, from far", 61, 37, 49, 69),
            ("L02", "south bridge back, from far", 63, 82, 53, 49), ("L02", "open ground", 44, 22, 49, 36), ("L02", "road", 52, 20, 52, 40),
            ("L16", "bridge, from its end", 72, 66, 72, 89), ("L16", "bridge, from beside the shore", 69, 66, 72, 89), ("L16", "bridge, from far", 79, 47, 72, 89), ("L16", "bridge back, from its end", 72, 83, 74, 60),
            ("L16", "bridge back, from beside the shore", 78, 82, 74, 60), ("L16", "bridge back, from far", 81, 102, 74, 60), ("L16", "open ground", 70, 113, 93, 121), ("L16", "road", 30, 104, 90, 104),
            ("L17", "open ground", 32, 11, 96, 11), ("L17", "road", 86, 60, 74, 100), ("L17", "open ground 2", 86, 60, 94, 44),
        };

        /// <summary>One vehicle alone across a bridge, from a few tiles beside one end to a few tiles beside the other: level, name, from (tile), to (tile).</summary>
        public static readonly (string level, string name, int fx, int fy, int tx, int ty)[] BridgeEndCases =
        {
            ("L02", "two-deck bridge, west deck, back", 31, 49, 25, 49), ("L02", "two-deck bridge, west deck", 20, 49, 33, 47), ("L02", "two-deck bridge, east deck", 39, 42, 51, 46),
            ("L02", "two-deck bridge, east deck, back", 47, 45, 37, 46), ("L02", "north bridge", 16, 22, 23, 35), ("L02", "north bridge, back", 19, 32, 19, 20),
            ("L02", "south bridge", 49, 57, 55, 66), ("L02", "south bridge, back", 54, 66, 50, 55), ("L16", "bridge", 68, 66, 72, 87), ("L16", "bridge, back", 73, 86, 73, 64),
        };

        // ------------------------------------------------------------------ 2. groups of one team
        static readonly string[] Mix = { "jeep", "grunt", "light", "psycho", "medium", "tough", "heavy", "sniper", "light", "pyro", "medium", "laser", "apc", "grunt", "missile_launcher" };
        public sealed class GroupResult { public int bodies, reversals, holding, stopped, paceOver, paceWindows, acts; public double half, last; public double PerBody => (double)reversals / Math.Max(1, bodies); }

        public static GroupResult Group(string settings, string map, int n, int ax, int ay, int tx, int ty, bool on, double maxS = 600)
        {
            bool was = ZWorld.CalmMoveDeviation; ZWorld.CalmMoveDeviation = on; Counters0();
            try
            {
                var w = Bare(settings, map);
                var spots = new List<(long d, int tx, int ty)>();
                for (int y = 2; y < w.Map.height - 3; y++)
                    for (int x = 2; x < w.Map.width - 3; x++)
                    {
                        if ((x - ax) % 3 != 0 || (y - ay) % 3 != 0) continue;
                        bool ok = true;
                        for (int j = -1; j <= 2 && ok; j++) for (int i = -1; i <= 2; i++) { var k = w.Map.ClassAt(x + i, y + j); if (k != ZClass.Ground && k != ZClass.Road) { ok = false; break; } }
                        if (!ok) continue;
                        int cx = x * 16 + 16, cy = y * 16 + 16;
                        if (w.Objects.Any(o => !o.Destroyed && Math.Abs(o.CenterX - cx) < 40 + o.WidthPix / 2 && Math.Abs(o.CenterY - cy) < 40 + o.HeightPix / 2)) continue;
                        spots.Add(((long)(x - ax) * (x - ax) + (long)(y - ay) * (y - ay), x, y));
                    }
                spots.Sort((p, q) => p.d != q.d ? p.d.CompareTo(q.d) : p.ty != q.ty ? p.ty.CompareTo(q.ty) : p.tx.CompareTo(q.tx));
                var leaders = new List<ZObject>();
                for (int i = 0; i < spots.Count && leaders.Count < n; i++) leaders.Add(OzV11Kit.Place(w, Mix[leaders.Count % Mix.Length], spots[i].tx * 16 + 16, spots[i].ty * 16 + 16, Team.Red));
                var all = new List<ZObject>(); foreach (var l in leaders) all.AddRange(OzV11Kit.Squad(l));
                for (int i = 0; i < 300; i++) { w.Tick(ZConst.TICK); w.Events.Clear(); }
                int gx = tx * 16 + 8, gy = ty * 16 + 8; double t0 = w.Time;
                foreach (var l in leaders) OzV11Kit.PlayerMove(w, l, gx, gy);
                var meters = all.Select(o => new Meter(o)).ToList(); var ended = all.ToDictionary(o => o.Id, o => double.PositiveInfinity);
                var win = all.ToDictionary(o => o.Id, o => new double[4]); var moved = all.ToDictionary(o => o.Id, o => o.PathState.Displaced); var last = all.ToDictionary(o => o.Id, o => (x: o.PosX, y: o.PosY));
                var stopStart = new Dictionary<long, double>(); var stopFlag = new HashSet<long>();
                var r = new GroupResult { bodies = all.Count }; int quiet = 0;
                bool Own(ZObject o) { foreach (var q in o.Waypoints) if (q.playerGiven && q.refId == -1 && q.mode == WpMode.Move) return true; return false; }
                while (w.Time - t0 < maxS && quiet < 200)
                {
                    w.Tick(ZConst.TICK); w.Events.Clear();
                    bool any = false; double now = w.Time;
                    foreach (var m in meters)
                    {
                        var o = m.U; if (o.Destroyed) continue;
                        bool own = Own(o);
                        if (double.IsInfinity(ended[o.Id])) { m.Step(now); if (!own && !(o.IsMinion && o.Leader != null && Own(o.Leader))) ended[o.Id] = now - t0; }
                        if (own || o.IsMoving) any = true;
                        var p = last[o.Id]; double dx = o.PosX - p.x, dy = o.PosY - p.y; last[o.Id] = (o.PosX, o.PosY);
                        if (o.Kind != ZKind.Vehicle) continue;
                        var wv = win[o.Id]; wv[0] += Math.Sqrt(dx * dx + dy * dy); wv[1] += Math.Max(o.RealMoveSpeed, 0) * ZConst.TICK; wv[2]++;
                        if (o.PathPushedThisTick || o.PathState.Displaced != moved[o.Id]) { wv[3] = 1; moved[o.Id] = o.PathState.Displaced; }
                        if (wv[2] >= 25) { if (wv[3] == 0 && wv[1] > 0.5) { r.paceWindows++; if (wv[0] - wv[1] > PaceSlackPx) r.paceOver++; } wv[0] = wv[1] = wv[2] = wv[3] = 0; }
                    }
                    for (int i = 0; i < all.Count; i++)
                        for (int j = i + 1; j < all.Count; j++)
                        {
                            var a = all[i]; var b = all[j];
                            if (a.Waypoints.Count > 0 || b.Waypoints.Count > 0 || !OzV11Kit.Alive(a) || !OzV11Kit.Alive(b)) continue;
                            double dx = a.PosX + a.WidthPix * 0.5 - b.PosX - b.WidthPix * 0.5, dy = a.PosY + a.HeightPix * 0.5 - b.PosY - b.HeightPix * 0.5;
                            if (Math.Abs(dx) > 48 || Math.Abs(dy) > 48) continue;
                            long key = ((long)Math.Min(a.Id, b.Id) << 32) | (uint)Math.Max(a.Id, b.Id);
                            double min = ZPathing.MinSpacing(a, b) - 1.5;
                            if (dx * dx + dy * dy >= min * min || ZPathing.PairExempt(a, b)) { stopStart.Remove(key); continue; }
                            if (!stopStart.TryGetValue(key, out double s0)) stopStart[key] = now;
                            else if (now - s0 > 2.0 && stopFlag.Add(key)) r.stopped++;
                        }
                    quiet = any ? 0 : quiet + 1;
                }
                var times = all.Where(o => !o.Destroyed).Select(o => ended[o.Id]).OrderBy(t => t).ToList();
                r.reversals = meters.Sum(m => m.Reversals); r.holding = times.Count(double.IsInfinity); r.half = times[times.Count / 2]; r.last = times[times.Count - 1]; r.acts = Acts(w);
                return r;
            }
            finally { ZWorld.CalmMoveDeviation = was; }
        }

        // ------------------------------------------------------------------ 3. the player's order across a fight
        public sealed class OrderResult { public int runs, done, dead, lost, replaced, left; public double farthest, longest, time; }

        /// <summary>cs: "fight" = an attack-move past a grunt squad, a Tough squad and a gun that stand and shoot; "blocked" = a move
        /// through a gap two tiles wide in a wall, in which an enemy vehicle stands for 110 s.</summary>
        public static OrderResult Order(string settings, string cs, string type, int seed0, int n, bool on)
        {
            bool was = ZWorld.CalmMoveDeviation; ZWorld.CalmMoveDeviation = on; Counters0();
            var r = new OrderResult { runs = n };
            try
            {
                for (int k = 0; k < n; k++)
                {
                    string map = OzV11Kit.Plain(96, 40);
                    if (cs == "blocked") { var md = ZMapData.FromJson(map); for (int y = 0; y < 40; y++) if (y != 19 && y != 20) { md.classes[y * 96 + 46] = (int)ZClass.Impassable; md.classes[y * 96 + 47] = (int)ZClass.Impassable; } map = UnityEngine.JsonUtility.ToJson(md); }
                    var w = OzV11Kit.World(settings, map, seed0 + k);
                    OzV11Kit.Dummy(w, 3 * 16 + 8, 3 * 16 + 8);
                    void Foe(string t, int x, int y) { var e = OzV11Kit.Place(w, t, x * 16 + 8, y * 16 + 8, Team.Blue); foreach (var b in OzV11Kit.Squad(e)) { b.MoveSpeed = 0; b.MaxHealth = b.Health = (int)(b.MaxHealth * 1.5); } }
                    ZObject plug = null;
                    if (cs == "fight") { Foe("grunt", 40, 13); Foe("tough", 52, 27); Foe("gun", 62, 13); }
                    else { plug = OzV11Kit.Place(w, "medium", 47 * 16, 20 * 16, Team.Blue); plug.MoveSpeed = 0; plug.Damage = 0; plug.MaxHealth = plug.Health = 1000000000; }
                    var u = OzV11Kit.Place(w, type, 8 * 16 + 16, 20 * 16 + 16, Team.Red);
                    var all = OzV11Kit.Squad(u).ToList();
                    while (w.Time < 21) { w.Tick(ZConst.TICK); w.Events.Clear(); }                 // (the calm start of a player's team is over)
                    int gx = 84 * 16 + 8, gy = 20 * 16 + 8;
                    w.SendWaypoints(Team.Red, u, new[] { ZWaypoint.Move(gx, gy, cs == "fight") });
                    var ord = u.Waypoints.Count > 0 ? u.Waypoints[u.Waypoints.Count - 1] : ZWaypoint.Move(gx, gy);
                    double t0 = w.Time, away = -1, ax = 0, ay = 0, far = 0; var lead = u; string end = null;
                    while (w.Time - t0 < 400)
                    {
                        w.Tick(ZConst.TICK); w.Events.Clear();
                        if (plug != null && w.Time - t0 >= 110) { plug.KillAt = w.Time; plug = null; }
                        if (!OzV11Kit.Alive(lead) || lead.Owner != Team.Red) { var nl = all.FirstOrDefault(o => OzV11Kit.Alive(o) && !o.IsMinion && o.Owner == Team.Red); if (nl == null) { end = "dead"; break; } lead = nl; }
                        int at = -1; for (int i = 0; i < lead.Waypoints.Count; i++) { var q = lead.Waypoints[i]; if (q.playerGiven && q.mode == ord.mode && q.refId == ord.refId) { at = i; break; } }
                        if (at >= 0 && (Math.Abs(lead.Waypoints[at].x - ord.x) > 32 || Math.Abs(lead.Waypoints[at].y - ord.y) > 32)) { r.replaced++; end = "replaced"; break; }
                        bool fights = at > 0 && lead.Waypoints[0].mode == WpMode.Attack;      // an attack on the way in front of the order
                        if (fights && away < 0) { away = w.Time; ax = lead.CenterX; ay = lead.CenterY; far = 0; r.left++; }
                        if (away >= 0) far = Math.Max(far, Math.Sqrt((lead.CenterX - ax) * (lead.CenterX - ax) + (lead.CenterY - ay) * (lead.CenterY - ay)));
                        if (!fights && away >= 0) { r.longest = Math.Max(r.longest, w.Time - away); r.farthest = Math.Max(r.farthest, far / 16); away = -1; }
                        if (at < 0)
                        {
                            double d = Math.Sqrt((double)(lead.CenterX - gx) * (lead.CenterX - gx) + (double)(lead.CenterY - gy) * (lead.CenterY - gy));
                            end = d <= 64 ? "done" : "lost"; break;
                        }
                    }
                    if (end == "done") { r.done++; r.time += w.Time - t0; } else if (end == "dead") r.dead++; else if (end == "lost") r.lost++;
                }
                if (r.done > 0) r.time /= r.done;
                return r;
            }
            finally { ZWorld.CalmMoveDeviation = was; }
        }

        // ------------------------------------------------------------------ 4. who steps aside
        public sealed class FightResult { public double wins, asideShare, shots, left; public int asides, held; }

        /// <summary>n fights on a plain map, three red squads of `red` against three blue grunt squads that carry grenades; both sides are
        /// computer teams that give no orders of their own and get the computer's attack order on each other at the start.</summary>
        public static FightResult Fight(string settings, string red, int n, bool on)
        {
            bool was = ZWorld.CalmMoveDeviation; ZWorld.CalmMoveDeviation = on; Counters0();
            var r = new FightResult(); double sec = 0, aside = 0; int shots = 0;
            try
            {
                for (int k = 0; k < n; k++)
                {
                    var w = ZWorld.Create(settings, OzV11Kit.Plain(64, 40), 1 + k);
                    w.Bots.Add(new ZBot(w, Team.Blue, 102 + k) { Ignored = true }); w.Bots.Add(new ZBot(w, Team.Red, 1 + k) { Ignored = true });
                    w.Events.Clear();
                    var R = new List<ZObject>(); var B = new List<ZObject>();
                    for (int i = 0; i < 3; i++) R.Add(OzV11Kit.Place(w, red, 25 * 16, (20 + (i - 1) * 4) * 16 + (k % 3) * 8, Team.Red));
                    for (int i = 0; i < 3; i++) { var g = OzV11Kit.Place(w, "grunt", 39 * 16, (20 + (i - 1) * 4) * 16 - (k % 2) * 8, Team.Blue); g.Grenades = 20; B.Add(g); }
                    var rb = new List<ZObject>(); foreach (var l in R) rb.AddRange(OzV11Kit.Squad(l));
                    var bb = new List<ZObject>(); foreach (var l in B) bb.AddRange(OzV11Kit.Squad(l));
                    var ids = new HashSet<int>(rb.Select(o => o.Id)); var was2 = new HashSet<int>();
                    for (int i = 0; i < 100; i++) { w.Tick(ZConst.TICK); w.Events.Clear(); }
                    for (int i = 0; i < 3; i++) { OzV11Kit.CpuAttack(w, R[i], B[i]); OzV11Kit.CpuAttack(w, B[i], R[i]); }
                    double t0 = w.Time; bool ra = true, ba = true;
                    while (w.Time - t0 < 180 && ra && ba)
                    {
                        w.Tick(ZConst.TICK);
                        foreach (var e in w.Events) if ((e.type == ZEventType.Hitscan || e.type == ZEventType.MissileFired) && ids.Contains(e.objId)) shots++;
                        w.Events.Clear();
                        ra = false; ba = bb.Any(o => OzV11Kit.Alive(o) && o.Owner == Team.Blue);
                        foreach (var o in rb)
                        {
                            if (!OzV11Kit.Alive(o) || o.Owner != Team.Red) continue;
                            ra = true; sec += ZConst.TICK;
                            bool dg = o.Waypoints.Count > 0 && o.Waypoints[0].mode == WpMode.Dodge;
                            if (dg) { aside += ZConst.TICK; if (was2.Add(o.Id)) r.asides++; } else was2.Remove(o.Id);
                        }
                    }
                    if (ra && !ba) r.wins += 1; else if (ra == ba) r.wins += 0.5;
                    r.left += rb.Count(o => OzV11Kit.Alive(o) && o.Owner == Team.Red);
                }
                r.wins = r.wins / n * 100; r.left /= n; r.asideShare = aside / Math.Max(1, sec) * 100; r.shots = shots / Math.Max(1, sec) * 10;
                r.held = OzV11Kit.Counter(typeof(ZWorld), "CalmRobotDodgesHeld") + OzV11Kit.Counter(typeof(ZWorld), "CalmShootsFirst");   // steps aside the rule did not let a light robot take
                return r;
            }
            finally { ZWorld.CalmMoveDeviation = was; }
        }

        // ------------------------------------------------------------------ the whole check
        /// <summary>settings = default_settings, maps = the grid of a level ("L02", "L16", "L17").</summary>
        public static string Run(string settings, Func<string, string> maps)
        {
            // the game's own state: faster units pass slower ones (the overtake rule), when the project has it; put back at the end
            var pass = typeof(ZWorld).GetField("OvertakeDeviation"); object passWas = pass?.GetValue(null); pass?.SetValue(null, true);
            try { return RunCore(settings, maps, pass != null); }
            finally { pass?.SetValue(null, passWas); }
        }

        static string RunCore(string settings, Func<string, string> maps, bool passing)
        {
            var sb = new StringBuilder(); bool ok = true; int offActs = 0;
            sb.AppendLine("Calm movement (b49), counted with the switch off (the game before) and on. A reversal = a turn of " + TurnDeg + " degrees or more to one side and, within " + Window + " s, one to the other side. A turn back = a turn of more than " + BackDeg + " degrees at one point." + (passing ? " The rule 'faster units pass slower ones' is on in every run, as in the game." : ""));
            sb.AppendLine();

            // 1. alone
            sb.AppendLine("1. ONE UNIT ALONE on the map, one move order. level, case | unit | reversals off -> on | turning deg/10 m off -> on | way travelled / way planned off -> on | time s off -> on");
            int revOff = 0, revOn = 0, notThere = 0, runs = 0; double worst = 0, turnOff = 0, turnOn = 0; string worstName = "";
            foreach (var c in LoneCases)
            {
                string map; try { map = maps(c.level); } catch (Exception) { map = null; }
                if (string.IsNullOrEmpty(map)) { sb.AppendLine(OzV11Kit.Verdict(false, "no grid found for level " + c.level)); ok = false; continue; }
                foreach (var type in new[] { "light", "grunt" })
                {
                    var a = Lone(settings, map, type, c.fx, c.fy, c.tx, c.ty, false); var b = Lone(settings, map, type, c.fx, c.fy, c.tx, c.ty, true);
                    sb.AppendLine(c.level + ", " + c.name + " | " + type + " | " + a.reversals + " -> " + b.reversals + " | " + F(a.turn, "0") + " -> " + F(b.turn, "0") + " | " + F(a.detour, "0.000") + " -> " + F(b.detour, "0.000") + " | " + F(a.time) + " -> " + F(b.time));
                    revOff += a.reversals; revOn += b.reversals; turnOff += a.turn; turnOn += b.turn; runs++; offActs += a.acts;
                    if (!b.arrived) notThere++;
                    if (b.detour > worst) { worst = b.detour; worstName = c.level + " " + c.name + " " + type; }
                }
            }
            bool v1 = revOn * 2 <= revOff; ok &= v1;
            sb.AppendLine(OzV11Kit.Verdict(v1, "a unit alone turns one way and at once the other way far less often: " + revOn + " reversals in " + runs + " orders with the switch on, " + revOff + " off (pass: at most half); mean turning " + F(turnOn / Math.Max(1, runs), "0") + " degrees per 10 m on, " + F(turnOff / Math.Max(1, runs), "0") + " off"));
            bool v2 = notThere == 0 && worst <= 1.03; ok &= v2;
            sb.AppendLine(OzV11Kit.Verdict(v2, "with the switch on every one of them arrives and travels at most 1.03 x its planned way: not there " + notThere + ", longest " + F(worst, "0.000") + " x (" + worstName + ")"));
            sb.AppendLine();

            // 1b. one vehicle alone at a bridge end
            sb.AppendLine("1b. ONE VEHICLE ALONE, sent across a bridge from a few tiles beside one end to a few tiles beside the other. level, case | unit | reversals off -> on | turns back off -> on | turning deg/10 m off -> on | time s off -> on");
            int beRevOff = 0, beRevOn = 0, beBackOff = 0, beBackOn = 0, beNotThere = 0, beRuns = 0;
            foreach (var c in BridgeEndCases)
            {
                string map; try { map = maps(c.level); } catch (Exception) { map = null; }
                if (string.IsNullOrEmpty(map)) { sb.AppendLine(OzV11Kit.Verdict(false, "no grid found for level " + c.level)); ok = false; continue; }
                foreach (var type in new[] { "light", "jeep" })
                {
                    var a = Lone(settings, map, type, c.fx, c.fy, c.tx, c.ty, false); var b = Lone(settings, map, type, c.fx, c.fy, c.tx, c.ty, true);
                    sb.AppendLine(c.level + ", " + c.name + " | " + type + " | " + a.reversals + " -> " + b.reversals + " | " + a.backs + " -> " + b.backs + " | " + F(a.turn, "0") + " -> " + F(b.turn, "0") + " | " + F(a.time) + " -> " + F(b.time));
                    beRevOff += a.reversals; beRevOn += b.reversals; beBackOff += a.backs; beBackOn += b.backs; beRuns++; offActs += a.acts;
                    if (!b.arrived) beNotThere++;
                }
            }
            bool v2b = beRuns > 0 && beNotThere == 0 && (beRevOn + beBackOn) * 4 <= beRevOff + beBackOff; ok &= v2b;
            sb.AppendLine(OzV11Kit.Verdict(v2b, "a vehicle alone at a bridge end drives one line: " + beRevOn + " reversals and " + beBackOn + " turns back in " + beRuns + " trips with the switch on, " + beRevOff + " and " + beBackOff + " off (pass: at most a quarter, and every one arrives: not there " + beNotThere + ")"));
            sb.AppendLine();

            // 2. groups
            sb.AppendLine("2. FIFTEEN MIXED GROUPS of one team on one order through a bridge. case | bodies | reversals per body off -> on | half of them at the end of their order s off -> on | the last one off -> on | still on the order | stopped on top of each other | vehicles faster than their own speed (quarter seconds)");
            double gOff = 0, gOn = 0; int hold = 0, stopped = 0, pace = 0, paceN = 0;
            foreach (var g in new[] { ("L02", "Desert, the two-deck bridge", 12, 46, 55, 46), ("L16", "Jungle, the bridge", 74, 58, 72, 89) })
            {
                string map; try { map = maps(g.Item1); } catch (Exception) { map = null; }
                if (string.IsNullOrEmpty(map)) { sb.AppendLine(OzV11Kit.Verdict(false, "no grid found for level " + g.Item1)); ok = false; continue; }
                var a = Group(settings, map, 15, g.Item3, g.Item4, g.Item5, g.Item6, false); var b = Group(settings, map, 15, g.Item3, g.Item4, g.Item5, g.Item6, true);
                sb.AppendLine(g.Item2 + " | " + b.bodies + " | " + F(a.PerBody, "0.00") + " -> " + F(b.PerBody, "0.00") + " | " + F(a.half) + " -> " + F(b.half) + " | " + F(a.last) + " -> " + F(b.last) + " | " + a.holding + " -> " + b.holding + " | " + a.stopped + " -> " + b.stopped + " | " + a.paceOver + " of " + a.paceWindows + " -> " + b.paceOver + " of " + b.paceWindows);
                gOff += a.PerBody; gOn += b.PerBody; hold += b.holding; stopped += b.stopped; pace += b.paceOver; paceN += b.paceWindows; offActs += a.acts;
            }
            bool v3 = gOn <= gOff * 0.95; ok &= v3;
            sb.AppendLine(OzV11Kit.Verdict(v3, "a group at a bridge weaves less: " + F(gOn, "0.00") + " reversals per body with the switch on (the two cases together), " + F(gOff, "0.00") + " off (pass: at least 5 % fewer)"));
            bool v4 = hold == 0 && stopped == 0; ok &= v4;
            sb.AppendLine(OzV11Kit.Verdict(v4, "with the switch on nobody is left holding its order and no two stopped units stand on top of each other (closer than their spacing - 1.5 px for 2 s): " + hold + " and " + stopped));
            bool v5 = pace == 0; ok &= v5;
            sb.AppendLine(OzV11Kit.Verdict(v5, "with the switch on no vehicle is faster than its own speed (0.25 s windows without a push, more than " + F(PaceSlackPx, "0") + " px over): " + pace + " of " + paceN));
            sb.AppendLine();

            // 3. the player's order
            sb.AppendLine("3. A UNIT OF THE PLAYER on an order across a fight (10 runs a row). case, unit | order carried out off -> on | all dead | ORDER LOST | destination replaced | stops to fight | farthest from where it left its way, tiles | longest stop s");
            int lostOn = 0, replOn = 0, lostOff = 0; double farRobot = 0, farVeh = 0; int doneOff = 0, doneOn = 0;
            foreach (var c in new[] { ("fight", "sniper", 1, 10), ("fight", "medium", 1, 10), ("blocked", "pyro", 12, 10) })
            {
                var a = Order(settings, c.Item1, c.Item2, c.Item3, c.Item4, false); var b = Order(settings, c.Item1, c.Item2, c.Item3, c.Item4, true);
                sb.AppendLine((c.Item1 == "fight" ? "an attack-move past two squads and a gun" : "a move through a gap an enemy vehicle stands in for 110 s") + ", " + c.Item2 + " | " + a.done + " -> " + b.done + " | " + a.dead + " -> " + b.dead + " | " + a.lost + " -> " + b.lost + " | " + a.replaced + " -> " + b.replaced + " | " + a.left + " -> " + b.left + " | " + F(a.farthest) + " -> " + F(b.farthest) + " | " + F(a.longest) + " -> " + F(b.longest));
                lostOn += b.lost; replOn += b.replaced; lostOff += a.lost; doneOff += a.done; doneOn += b.done;
                if (c.Item2 == "medium") farVeh = Math.Max(farVeh, b.farthest); else farRobot = Math.Max(farRobot, b.farthest);
            }
            bool v6 = lostOn == 0 && replOn == 0; ok &= v6;
            sb.AppendLine(OzV11Kit.Verdict(v6, "with the switch on the player's order is never lost and its destination never replaced: " + lostOn + " and " + replOn + " (off: " + lostOff + " lost)"));
            bool v7 = farRobot <= 0.5 && farVeh <= ZWorld.CalmOrderFightFar / 16 + 1; ok &= v7;
            sb.AppendLine(OzV11Kit.Verdict(v7, "a stop to fight does not take the unit off its way: a robot at most " + F(farRobot) + " tiles (pass: 0.5), a vehicle at most " + F(farVeh) + " tiles (pass: " + F(ZWorld.CalmOrderFightFar / 16 + 1) + ")"));
            bool v8 = doneOn >= doneOff - 2; ok &= v8;
            sb.AppendLine(OzV11Kit.Verdict(v8, "the order is carried out about as often as before: " + doneOn + " of 30 with the switch on, " + doneOff + " off (pass: not more than 2 fewer)"));
            sb.AppendLine();

            // 4. who steps aside
            sb.AppendLine("4. WHO STEPS ASIDE. Three squads against three grunt squads that throw grenades, 20 fights a row. squads | wins % off -> on | bodies left | share of its time stepping aside % | steps aside | shots per body per 10 s | steps aside not taken with the switch on (the squad rests after one)");
            var fr = new Dictionary<string, (FightResult a, FightResult b)>();
            foreach (var kind in new[] { "tough", "sniper", "grunt", "psycho" })
            {
                var a = Fight(settings, kind, 20, false); var b = Fight(settings, kind, 20, true); fr[kind] = (a, b);
                sb.AppendLine(kind + " | " + F(a.wins) + " -> " + F(b.wins) + " | " + F(a.left, "0.00") + " -> " + F(b.left, "0.00") + " | " + F(a.asideShare) + " -> " + F(b.asideShare) + " | " + a.asides + " -> " + b.asides + " | " + F(a.shots, "0.00") + " -> " + F(b.shots, "0.00") + " | " + b.held);
            }
            bool v9 = fr["tough"].b.asides == 0 && fr["sniper"].b.asides == 0 && fr["tough"].a.asides > 0 && fr["sniper"].a.asides > 0; ok &= v9;
            sb.AppendLine(OzV11Kit.Verdict(v9, "Toughs and Snipers never step aside with the switch on: " + fr["tough"].b.asides + " and " + fr["sniper"].b.asides + " steps aside (off: " + fr["tough"].a.asides + " and " + fr["sniper"].a.asides + ")"));
            bool v10 = fr["tough"].b.shots >= fr["tough"].a.shots && fr["tough"].b.wins >= fr["tough"].a.wins - 5; ok &= v10;
            sb.AppendLine(OzV11Kit.Verdict(v10, "Toughs under grenades fire more and win as often: " + F(fr["tough"].b.shots, "0.00") + " shots per body per 10 s on, " + F(fr["tough"].a.shots, "0.00") + " off; wins " + F(fr["tough"].b.wins) + " % on, " + F(fr["tough"].a.wins) + " % off (pass: not 5 points fewer)"));
            double lightOff = fr["grunt"].a.asideShare + fr["psycho"].a.asideShare, lightOn = fr["grunt"].b.asideShare + fr["psycho"].b.asideShare;
            bool v11 = fr["grunt"].b.held > 0 && fr["psycho"].b.held > 0 && fr["grunt"].b.asides > 0 && fr["psycho"].b.asides > 0 && fr["grunt"].a.held == 0 && fr["psycho"].a.held == 0; ok &= v11;
            sb.AppendLine(OzV11Kit.Verdict(v11, "grunt and psycho squads still step aside, but not for every grenade: " + fr["grunt"].b.held + " and " + fr["psycho"].b.held + " steps aside not taken with the switch on, " + fr["grunt"].b.asides + " and " + fr["psycho"].b.asides + " taken (off: " + fr["grunt"].a.asides + " and " + fr["psycho"].a.asides + " taken, none held back); the share of their time stepping aside " + F(lightOn / 2) + " % on, " + F(lightOff / 2) + " % off (pass: some not taken, some taken)"));
            sb.AppendLine();

            // 5. off
            bool v12 = offActs == 0; ok &= v12;
            sb.AppendLine(OzV11Kit.Verdict(v12, "with the switch off nothing of the rule acted in the runs of parts 1, 1b and 2: " + offActs));
            sb.AppendLine();
            sb.AppendLine(ok ? "ALL PASS" : "SOME FAIL");
            return sb.ToString();
        }

#if UNITY_EDITOR
        [UnityEditor.MenuItem("OpenZ/V1.1/Count Calm Movement (b49)")]
        static void Menu()
        {
            OzV11Kit.RunMenu("Count Calm Movement (b49)", () =>
            {
                string report = Run(OzV11Kit.SettingsText, OzV11Kit.MapText);
                try { System.IO.Directory.CreateDirectory(System.IO.Path.GetDirectoryName(ReportPath)); System.IO.File.WriteAllText(ReportPath, report.Replace("\r", "")); report += "\nwritten: " + ReportPath; }
                catch (Exception e) { report += "\nreport not written: " + e.Message; }
                return report;
            });
        }

        // ------------------------------------------------------------------ Play mode: the same meter on the running game
        public const string PlayReportPath = "OzV11SwerveMeter_play.txt";
        static Dictionary<int, Meter> playMeters; static double playStart;

        [UnityEditor.MenuItem("OpenZ/V1.1/Calm Movement Meter (Play)/Start")]
        static void PlayStartMenu()
        {
            if (!UnityEngine.Application.isPlaying || !ZGame.I || ZGame.I.World == null) { UnityEngine.Debug.LogWarning("[OpenZ V1.1] Calm movement meter: enter Play mode and start a level first."); return; }
            playMeters = new Dictionary<int, Meter>(); playStart = ZGame.I.World.Time; Counters0();
            UnityEditor.EditorApplication.update -= PlayStep; UnityEditor.EditorApplication.update += PlayStep;
            UnityEngine.Debug.Log("[OpenZ V1.1] Calm movement meter started at " + F(playStart) + " s of the game clock; the switch is " + (ZWorld.CalmMoveDeviation ? "ON" : "off") + ". It measures every unit of the player's team from now on. Give the orders, then Stop.");
        }

        static void PlayStep()
        {
            if (!UnityEngine.Application.isPlaying || !ZGame.I || ZGame.I.World == null || playMeters == null) { UnityEditor.EditorApplication.update -= PlayStep; return; }
            var w = ZGame.I.World;
            foreach (var o in w.Objects)
            {
                if (!o.IsUnit || o.Destroyed || o.Owner != ZGame.I.player || o.Kind == ZKind.Cannon) continue;
                if (!playMeters.TryGetValue(o.Id, out var m)) playMeters[o.Id] = m = new Meter(o);
                m.Step(w.Time);
            }
        }

        /// <summary>For the comparison in Play mode only: the rule off (or on again) in the running level. The game sets the switch again
        /// at the next level start. Not a setting: nothing is saved.</summary>
        [UnityEditor.MenuItem("OpenZ/V1.1/Calm Movement Meter (Play)/Rule OFF until the next level start")]
        static void PlayOffMenu() { if (!UnityEngine.Application.isPlaying) { UnityEngine.Debug.LogWarning("[OpenZ V1.1] Calm movement: only in Play mode."); return; } ZWorld.CalmMoveDeviation = false; UnityEngine.Debug.Log("[OpenZ V1.1] calm movement (b49): off for this level (comparison)"); }
        [UnityEditor.MenuItem("OpenZ/V1.1/Calm Movement Meter (Play)/Rule ON again")]
        static void PlayOnMenu() { if (!UnityEngine.Application.isPlaying) { UnityEngine.Debug.LogWarning("[OpenZ V1.1] Calm movement: only in Play mode."); return; } ZWorld.CalmMoveDeviation = true; UnityEngine.Debug.Log("[OpenZ V1.1] calm movement (b49): ON"); }

        [UnityEditor.MenuItem("OpenZ/V1.1/Calm Movement Meter (Play)/Stop and report")]
        static void PlayStopMenu()
        {
            UnityEditor.EditorApplication.update -= PlayStep;
            if (playMeters == null || !UnityEngine.Application.isPlaying || !ZGame.I || ZGame.I.World == null) { UnityEngine.Debug.LogWarning("[OpenZ V1.1] Calm movement meter: it was not started in this Play session."); return; }
            var w = ZGame.I.World; var sb = new StringBuilder(); var p = w.Pathing;
            var moved = playMeters.Values.Where(m => m.Path >= 16).OrderByDescending(m => m.Reversals).ThenByDescending(m => m.TurnPer10m).ToList();
            double way = moved.Sum(m => m.Path); int rev = moved.Sum(m => m.Reversals), backs = moved.Sum(m => m.Backs);
            sb.AppendLine("Calm movement meter, Play mode, level " + ZGame.I.level + ", game clock " + F(playStart) + " to " + F(w.Time) + " s; the switch is " + (ZWorld.CalmMoveDeviation ? "ON" : "off") + ".");
            sb.AppendLine("A reversal = a turn of " + TurnDeg + " degrees or more to one side and, within " + Window + " s, one to the other side (the track cut into chords of " + Chord + " px; read once a frame). A turn back = a turn of more than " + BackDeg + " degrees at one point.");
            sb.AppendLine("Bodies of the player's team that moved a tile or more: " + moved.Count + "; way " + F(way / 16, "0") + " tiles; reversals " + rev + " = " + F(way > 0 ? rev / (way / 1600) : 0, "0.0") + " per 100 tiles; turns back " + backs + "; mean turning " + F(moved.Count > 0 ? moved.Average(m => m.TurnPer10m) : 0, "0") + " degrees per 10 m.");
            sb.AppendLine("Since Start: steps aside not taken by Toughs and Snipers " + ZWorld.CalmStoodFirm + ", by the squad's rest " + ZWorld.CalmRobotDodgesHeld + ", because it shoots first " + ZWorld.CalmShootsFirst + "; steps aside ended at the blast " + ZWorld.CalmDodgesEnded + "; rows of steps aside ended on an order " + ZWorld.CalmOrderAsidesHeld + "; stops to fight ended without a chase " + ZWorld.CalmChasesEnded + ".");
            if (p != null) sb.AppendLine("Since the level began: path points left out " + p.CalmPointsCut + "; routes kept " + p.CalmRouteKeeps + "; units that came closer " + p.CalmCloseUnits + "; queued instead of weaving " + p.CalmQueued + "; the player's moves kept far from their goal " + (p.CalmMovesKept + p.CalmGoalsKept) + "; walks back to a leader ended at a parked unit " + p.CalmWedgesEnded + ".");
            sb.AppendLine("body | way tiles | reversals | turns back | turning deg/10 m");
            foreach (var m in moved.Take(40)) sb.AppendLine(m.U.Type + " #" + m.U.Id + (m.U.IsMinion ? " (squad member)" : "") + " | " + F(m.Path / 16) + " | " + m.Reversals + " | " + m.Backs + " | " + F(m.TurnPer10m, "0"));
            string report = sb.ToString();
            try { System.IO.Directory.CreateDirectory(System.IO.Path.GetDirectoryName(PlayReportPath)); System.IO.File.WriteAllText(PlayReportPath, report.Replace("\r", "")); report += "\nwritten: " + PlayReportPath; }
            catch (Exception e) { report += "\nreport not written: " + e.Message; }
            UnityEngine.Debug.Log("[OpenZ V1.1] " + report);
            playMeters = null;
        }
#endif
    }
}
