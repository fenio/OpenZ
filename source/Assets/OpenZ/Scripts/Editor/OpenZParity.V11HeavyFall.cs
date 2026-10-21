#if !OPENZ_HARNESS
using System;
using System.Collections.Generic;
using System.Linq;
using OpenZ.Sim;

namespace OpenZ.EditorTools
{
    /// <summary>The Deviations row DV124 "a big faller fells the tree it comes down on" (the rule: Sim/ZWorld.HeavyFall.cs; the view:
    /// View/OzTreeCrush.cs). Measured headless on level 16's own data: (1) a medium tank destroyed beside a tree, its turret's debris
    /// point put on the tree's drawn trunk: at the blast the tree is destroyed by the rules' own death path with the tank as the
    /// attacker and its tile opens, every other tree stands; (2) the playable shuttle flown over the trees and shot down in the air
    /// where its hull comes down on a tree: the trees within its cover fall when it lands, no other; (3) a fort's cannon shot to
    /// pieces while the fort stands, on a heading whose landing lies on a tree: the trees under the landed cannon fall when it lands,
    /// no other; (4) the same three with the rule off: every tree stands; (5) whole CPU matches with the rule on and off in step: the
    /// same object state every tick until the first tree a faller fells (none on level 17); (6) a replay: identical.</summary>
    public static partial class OpenZParity
    {
        static void AddHeavyFallDeviation(List<Deviation> list)
        {
            string measured; bool pass;
            bool was = ZWorld.HeavyFallDeviation, wasPlay = ZWorld.ShuttlePlayableDeviation, wasOn = ZWorld.ShuttleOn; double wasRock = ZWorld.HeavyTurretRockClearM;
            try
            {
                ZWorld.HeavyTurretRockClearM = 0;
                var sb = new System.Text.StringBuilder();
                bool okT = HfTurret(true, sb), okTo = HfTurret(false, sb);
                bool okS = HfShuttle(true, sb), okSo = HfShuttle(false, sb);
                bool okG = HfGun(true, sb), okGo = HfGun(false, sb);
                bool okM = HfStep("L16", 3, 3000, sb) && HfStep("L17", 3, 1500, sb) && HfStep("L02", 3, 1500, sb);
                ZWorld.HeavyFallDeviation = true;
                bool det = HfReplay() == HfReplay();
                sb.Append($"replay (L16, CPU on both sides, 900 ticks): {(det ? "identical" : "DIFFERENT")}");
                pass = okT && okTo && okS && okSo && okG && okGo && okM && det;
                measured = sb.ToString();
            }
            catch (Exception ex) { measured = "the probe threw " + ex.GetType().Name + ": " + ex.Message; pass = false; }
            finally { ZWorld.HeavyFallDeviation = was; ZWorld.ShuttlePlayableDeviation = wasPlay; ZWorld.ShuttleOn = wasOn; ZWorld.HeavyTurretRockClearM = wasRock; }
            list.Add(new Deviation
            {
                id = "DV124", change = "A big faller (the shot-down shuttle, the fort's cannon, a blown-off turret) fells the tree it comes down on",
                zod = "no trees in the rules: a turret's debris missile (vlight.cpp:261-278, cgatling.cpp:279-296) is a 40 HP blast that never destroys a tree; Zod has no shuttle and no fort cannon toss",
                openz = "ZWorld.HeavyFall.cs: the trees of the rules (DV88 level 16 tree tiles, DV67 level 2 palms and cacti, Zod's level 2 map-object cacti) with their DRAWN trunks (ZHeavyFallData, read from the scenes); " +
                        $"a tank's or gun's turret lands on its debris blast: the trees within {ZWorld.TurretCoverM("light"):0.0}-{ZWorld.TurretCoverM("heavy"):0.0} m (half its short side as it lies) of that point are destroyed then; " +
                        $"the shuttle shot down in the air (at least {ZWorld.ShuttleFallMinM:0.0} m up) comes down after min({ZWorld.ShuttleFallMaxSec:0.0} s, sqrt(2 h / g)) carried on by its velocity: the trees within {ZWorld.ShuttleCoverM:0.0} m of that point are destroyed when it lands; " +
                        $"the fort cannon shot to pieces while its fort stands (DV25) flies on the rules' toss (its aim's heading + the fort's own seeded offset, distance and landing turn, moved off the rocks the way the view's free landing searches): the trees under its landed pieces' outlines are destroyed {ZWorld.FortGunTossSec:0} s later; " +
                        "each by the rules' own death path (Death event with the faller as the attacker, the tile opens). No draw of the rules' random stream. With its fort (the game over) the view fells what the cannon lands on. " +
                        "View only: View/OzTreeCrush.cs (the drawn break waits until the drawn faller reaches the tree; the shuttle's pieces steered onto the rules' place; the trunks round a crash solid for the big pieces: a standing tree stops them, a drawn tree with no rules falls when one touches it)",
                measured = measured,
                toggle = "ZWorld.HeavyFallDeviation: on in the game, off in 'Zod exact' (oz_zod_exact) and in every counted check unless it sets it itself",
                status = pass ? DevActive : DevCheck,
            });
        }

        // ------------------------------------------------------------------ helpers
        static ZWorld HfWorld(string lv, int seed, bool on)
        {
            ZWorld.HeavyFallDeviation = on;
            var w = ZWorld.Create(SettingsText, MapText(lv), seed, lv == "L17");
            w.Events.Clear();
            return w;
        }

        static List<int> HfStanding(ZWorld w) { var l = new List<int>(); for (int i = 0; i < w.HeavyTreeCount; i++) { var o = w.HeavyTreeAt(i, out _, out _); if (w.HeavyTreeStanding(o)) l.Add(o.Id); } return l; }

        static (int x, int y) HfFoot(ZWorld w, int id) { for (int i = 0; i < w.HeavyTreeCount; i++) { var o = w.HeavyTreeAt(i, out double fx, out double fy); if (o.Id == id) return ((int)Math.Round(fx), (int)Math.Round(fy)); } return (0, 0); }

        /// <summary>A tile of open ground (vehicles pass) about `distTiles` from (tx, ty), else (-1, -1).</summary>
        static (int x, int y) HfOpenNear(ZWorld w, int tx, int ty, int distTiles)
        {
            for (int r = distTiles; r < distTiles + 8; r++)
                for (int k = 0; k < 16; k++)
                {
                    double a = k * Math.PI / 8; int x = tx + (int)Math.Round(Math.Cos(a) * r), y = ty + (int)Math.Round(Math.Sin(a) * r);
                    if (x < 2 || y < 2 || x >= w.Map.width - 3 || y >= w.Map.height - 3) continue;
                    bool ok = true;
                    for (int dy = 0; dy < 2 && ok; dy++) for (int dx = 0; dx < 2 && ok; dx++) ok = w.Path.TilePassable(x + dx, y + dy, false);
                    if (ok) return (x, y);
                }
            return (-1, -1);
        }

        static string HfIds(IEnumerable<int> ids) { var a = ids.ToList(); return a.Count == 0 ? "none" : "#" + string.Join(", #", a); }

        // ------------------------------------------------------------------ (1) the turret
        static bool HfTurret(bool on, System.Text.StringBuilder sb)
        {
            var w = HfWorld("L16", 3, on);
            var standing = HfStanding(w);
            // a tree with open ground 6-8 tiles away for the tank
            int treeId = -1; (int x, int y) spot = (-1, -1);
            foreach (int id in standing)
            {
                var o = w.Get(id); spot = HfOpenNear(w, o.LocX >> 4, o.LocY >> 4, 6);
                if (spot.x >= 0) { treeId = id; break; }
            }
            if (treeId < 0) { sb.Append("turret: no tree with open ground beside it; "); return false; }
            var tank = w.CreateObject("vehicle", "medium", spot.x * 16, spot.y * 16, Team.Blue);
            tank.SetHealth(0); w.UpdateObjectHealth(tank);
            ZMissile m = null; foreach (var x in w.QueuedMissiles) if (x.debris && x.attackerId == tank.Id) m = x;
            if (m == null) { sb.Append("turret: the tank left no debris missile; "); return false; }
            var foot = HfFoot(w, treeId); m.x = foot.x + 2; m.y = foot.y + 1;                          // the turret comes down on the tree's drawn trunk
            double due = m.explodeTime; int killer = -1; double killedAt = -1;
            while (w.Time < due + 0.5)
            {
                w.Tick(ZConst.TICK);
                foreach (var e in w.Events) if (e.type == ZEventType.Death && e.objId == treeId) { killer = e.otherId; killedAt = w.Time; }
                w.Events.Clear();
            }
            var after = HfStanding(w); var gone = standing.Except(after).ToList();
            var f = w.HeavyFallOf(tank.Id);
            int tx = w.Get(treeId) == null ? 1 : 0;
            bool ok = on ? gone.Count >= 1 && gone.Contains(treeId) && killer == tank.Id && Math.Abs(killedAt - due) <= 0.15 && f != null && f.trees.Count == gone.Count && f.leftStanding == 0
                           && gone.All(id => { var p = HfFoot(w, id); double d = Math.Sqrt((p.x - m.x) * (p.x - m.x) + (p.y - m.y) * (p.y - m.y)) / 8.0; return d <= ZWorld.TurretCoverM("medium") + 1e-6; })
                         : gone.Count == 0 && f == null;
            sb.Append($"turret {(on ? "ON" : "off")}: a medium tank destroyed {Math.Sqrt(Math.Pow(spot.x * 16 + 16 - foot.x, 2) + Math.Pow(spot.y * 16 + 16 - foot.y, 2)) / 8:0} m from tree #{treeId}, its turret's blast on the trunk at {due:0.00} s: " +
                      $"felled {HfIds(gone)}{(on ? $" (by #{killer} at {killedAt:0.00} s, the tile {(tx == 1 ? "open" : "still closed")})" : "")}, {after.Count} of {standing.Count} trees stand {(ok ? "PASS" : "FAIL")}; ");
            return ok;
        }

        // ------------------------------------------------------------------ (2) the shuttle
        static bool HfShuttle(bool on, System.Text.StringBuilder sb)
        {
            ZWorld.ShuttlePlayableDeviation = true; ZWorld.ShuttleOn = true;
            var w = HfWorld("L16", 5, on);
            var feet = new List<(int id, int x, int y)>();
            for (int i = 0; i < w.HeavyTreeCount; i++) { var o = w.HeavyTreeAt(i, out double fx, out double fy); feet.Add((o.Id, (int)Math.Round(fx), (int)Math.Round(fy))); }
            var open = new List<(int x, int y)>();
            for (int y = 6; y < w.Map.height - 6; y += 3) for (int x = 6; x < w.Map.width - 6; x += 3) if (w.Path.TilePassable(x, y, false) && w.Path.TilePassable(x + 1, y + 1, false)) open.Add((x, y));
            var rng = new Random(5);
            // flights between open places; shot down the first time its hull would come down on a standing tree (within 2.5 m of a trunk)
            for (int n = 0; n < 60 && open.Count > 1; n++)
            {
                var a = open[rng.Next(open.Count)]; var b = open[rng.Next(open.Count)];
                if (Math.Abs(a.x - b.x) + Math.Abs(a.y - b.y) < 25) continue;
                var (sx, sy) = w.ShuttleClamp(a.x * 16 + 8, a.y * 16 + 8); var (gx, gy) = w.ShuttleClamp(b.x * 16 + 8, b.y * 16 + 8);
                var ship = w.CreatePlayableShuttle(sx, sy, Team.Red, (float)(Math.Atan2(gx - sx, -(gy - sy)) * 180.0 / Math.PI));
                if (ship == null) continue;
                w.SendWaypoints(ship.Owner, ship, new[] { new ZWaypoint { mode = WpMode.Move, refId = -1, x = gx, y = gy, playerGiven = true } });
                double t00 = w.Time;
                while (w.Time < t00 + 60 && !ship.Destroyed)
                {
                    w.Tick(ZConst.TICK); w.Events.Clear();
                    if (ship.ShuttleState == ZShuttleState.Landed && w.Time - t00 > 4) break;
                    if (!ZWorld.HeavyShuttlePoint(ship, out double px, out double py, out double T) || ship.ShuttleHeightM < 6.0) continue;
                    int treeId = -1; double d = 1e9;
                    foreach (var q in feet) { var o = w.Get(q.id); if (o == null || !w.HeavyTreeStanding(o)) continue; double dd = Math.Sqrt((px - q.x) * (px - q.x) + (py - q.y) * (py - q.y)) / 8.0; if (dd < d) { d = dd; treeId = q.id; } }
                    if (d > 2.5) continue;
                    // shot down here: its hull comes down on the tree
                    var before = HfStanding(w); double t0 = w.Time; double h = ship.ShuttleHeightM;
                    ship.SetHealth(0); w.UpdateObjectHealth(ship);
                    var killedAt = new Dictionary<int, double>();
                    while (w.Time < t0 + 3.0)
                    {
                        w.Tick(ZConst.TICK);
                        foreach (var e in w.Events) if (e.type == ZEventType.Death && e.otherId == ship.Id && before.Contains(e.objId)) killedAt[e.objId] = w.Time;
                        w.Events.Clear();
                    }
                    var gone = before.Except(HfStanding(w)).ToList();
                    var f = w.HeavyFallOf(ship.Id);
                    bool inside = gone.All(id => { var p = HfFoot(w, id); return Math.Sqrt((p.x - px) * (p.x - px) + (p.y - py) * (p.y - py)) / 8.0 <= ZWorld.ShuttleCoverM + 1e-6; });
                    bool allIn = before.All(id => { var p = HfFoot(w, id); return Math.Sqrt((p.x - px) * (p.x - px) + (p.y - py) * (p.y - py)) / 8.0 > ZWorld.ShuttleCoverM + 1e-6 || gone.Contains(id); });
                    bool timed = killedAt.Values.All(x => Math.Abs(x - (t0 + T)) <= 0.15);
                    bool ok = on ? gone.Contains(treeId) && inside && allIn && timed && f != null && f.trees.Count == gone.Count && f.leftStanding == 0 : gone.Count == 0 && f == null;
                    sb.Append($"shuttle {(on ? "ON" : "off")}: flight {n + 1} shot down {h:0.0} m up, {Math.Sqrt(ship.Dx * ship.Dx + ship.Dy * ship.Dy) / 8:0.0} m/s, its hull due down after {T:0.00} s {d:0.0} m from tree #{treeId}: felled {HfIds(gone)}" +
                              $"{(on ? $" (all within {ZWorld.ShuttleCoverM:0.0} m {(inside ? "yes" : "NO")}, none left inside {(allIn ? "yes" : "NO")}, at the landing {(timed ? "yes" : "NO")})" : "")} {(ok ? "PASS" : "FAIL")}; ");
                    return ok;
                }
            }
            sb.Append($"shuttle {(on ? "ON" : "off")}: no flight over a tree in 60 FAIL; ");
            return false;
        }

        // ------------------------------------------------------------------ (3) the fort cannon
        static bool HfGun(bool on, System.Text.StringBuilder sb)
        {
            var w = HfWorld("L16", 7, true);                                                              // the preview needs the rule; the run below sets it
            foreach (var fort in w.Objects.Where(o => o.IsFort).ToList())
            {
                for (int h = 0; h < 360; h += 3)
                {
                    var pre = w.HeavyGunToss(fort, h, false);
                    if (pre == null || pre.movedM > 0 || pre.movedDeg != 0) continue;
                    var under = w.HeavyTreesUnder(pre);
                    if (under.Count == 0) continue;
                    var z = HfWorld("L16", 7, on);
                    var fz = z.Get(fort.Id); var before = HfStanding(z);
                    ZWorld.HeavyGunHeadingTest = h;
                    fz.FortGunHealth = 0; fz.FortGunKilledAt = z.Time;
                    z.HeavyFallSync();
                    ZWorld.HeavyGunHeadingTest = double.NaN;
                    var toss = z.HeavyFallGunToss(fort.Id);
                    double t0 = z.Time; var killedAt = new Dictionary<int, double>();
                    while (z.Time < t0 + ZWorld.FortGunTossSec + 1.0)
                    {
                        z.Tick(ZConst.TICK);
                        foreach (var e in z.Events) if (e.type == ZEventType.Death && e.otherId == fort.Id && before.Contains(e.objId)) killedAt[e.objId] = z.Time;
                        z.Events.Clear();
                    }
                    var gone = before.Except(HfStanding(z)).ToList();
                    bool same = on ? gone.Count == under.Count && under.All(gone.Contains) && toss != null && Math.Abs(toss.x - pre.x) < 1e-6 && Math.Abs(toss.y - pre.y) < 1e-6 : gone.Count == 0 && toss == null;
                    bool timed = killedAt.Values.All(x => Math.Abs(x - (t0 + ZWorld.FortGunTossSec)) <= 0.15);
                    bool ok = same && timed && fz.Destroyed == false;
                    sb.Append($"fort cannon {(on ? "ON" : "off")}: fort {fort.Type} #{fort.Id} cannon shot to pieces (the fort stands), aim heading {h} deg, landing {Math.Sqrt(Math.Pow(pre.x - fort.CenterX, 2) + Math.Pow(pre.y - fort.CenterY, 2)) / 8:0.0} m out at {pre.headingDeg:0.0} deg: " +
                              $"under its pieces {HfIds(under)}, felled {HfIds(gone)}{(on ? $" at the landing ({ZWorld.FortGunTossSec:0} s) {(timed ? "yes" : "NO")}" : "")} {(ok ? "PASS" : "FAIL")}; ");
                    return ok;
                }
            }
            sb.Append($"fort cannon {(on ? "ON" : "off")}: no heading lands on a tree FAIL; ");
            return false;
        }

        // ------------------------------------------------------------------ (5) on and off in step
        static string HfState(ZWorld w) => string.Join(";", w.Objects.Select(o => $"{o.Id}:{o.LocX},{o.LocY},{o.Health},{(o.Destroyed ? 1 : 0)},{o.Waypoints.Count}"));

        static bool HfStep(string lv, int seed, int ticks, System.Text.StringBuilder sb)
        {
            var a = HfWorld(lv, seed, true); var b = HfWorld(lv, seed, false);
            a.Bots.Add(new ZBot(a, Team.Red, seed)); a.Bots.Add(new ZBot(a, Team.Blue, seed + 101));
            b.Bots.Add(new ZBot(b, Team.Red, seed)); b.Bots.Add(new ZBot(b, Team.Blue, seed + 101));
            int firstDiff = -1, firstFell = -1, falls = 0;
            for (int i = 0; i < ticks && !a.GameOver; i++)
            {
                ZWorld.HeavyFallDeviation = true; a.Tick(ZConst.TICK); a.Events.Clear();
                ZWorld.HeavyFallDeviation = false; b.Tick(ZConst.TICK); b.Events.Clear();
                if (firstFell < 0 && a.HeavyFallTrees > 0) firstFell = i;
                if (firstDiff < 0 && HfState(a) != HfState(b)) firstDiff = i;
                if (firstDiff >= 0 && firstFell >= 0) break;
            }
            foreach (var f in a.HeavyFalls) if (f.trees.Count > 0) falls++;
            bool ok = firstDiff < 0 ? true : firstFell >= 0 && firstDiff >= firstFell;
            sb.Append($"{lv} seed {seed} on / off in step ({ticks} ticks): {(firstDiff < 0 ? "the same every tick" : $"the first difference at tick {firstDiff}, the first tree a faller felled at tick {firstFell}")}" +
                      $"{(a.HeavyFalls.Count > 0 ? $", {a.HeavyFalls.Count} big falls ({falls} on a tree)" : ", no big fall")} {(ok ? "PASS" : "FAIL")}; ");
            ZWorld.HeavyFallDeviation = true;
            return ok;
        }

        static string HfReplay()
        {
            var r = HfWorld("L16", 11, true);
            r.Bots.Add(new ZBot(r, Team.Red, 11)); r.Bots.Add(new ZBot(r, Team.Blue, 112));
            for (int i = 0; i < 900; i++) { r.Tick(ZConst.TICK); r.Events.Clear(); }
            return HfState(r) + "|" + r.HeavyFallTrees;
        }
    }
}
#endif
