using System;
using System.Collections.Generic;
using System.Linq;
using OpenZ.Sim;

namespace OpenZ.EditorTools
{
    /// <summary>
    /// los_clearance_v1: the parity report's DV22 (building clearance) and DV23 (line of sight) rows, measured headless on the
    /// live sim like every other Deviations row (called from CollectDeviations in OpenZParity.Deviations.cs).
    /// </summary>
    public static partial class OpenZParity
    {
        // ------------------------------------------------------------------ DV22 building clearance
        /// <summary>Both levels: tiles DV22 blocks / keeps open; then at the L02 red fort (fort_back 48,72) a red grunt squad and
        /// a heavy tank are ordered through every Zod-walkable tile of the fort's rect + 1 ring, once with DV22 off (Zod grid)
        /// and once on: unit samples (every 0.05 s) whose centre stands on a tile the fort's mesh covers by >= 50 %, outside
        /// Zod's gate route.</summary>
        static Deviation DevClearance(Func<string, int, int> getInt)
        {
            bool saved = ZWorld.ClearanceDeviation, freeze = ZWorld.EndFreezeDeviation, gun = ZWorld.FortGunDeviation;
            ZWorld.EndFreezeDeviation = false; ZWorld.FortGunDeviation = false;
            try
            {
                var parts = new List<string>();
                ZWorld.ClearanceDeviation = true;
                foreach (var lv in new[] { "L02", "L17" })
                {
                    var w = ZWorld.Create(SettingsText, MapText(lv), 1);
                    parts.Add($"{lv}: {w.Clearance.Added.Count} tiles blocked ({string.Join(", ", w.Clearance.Added.GroupBy(t => t.type).Select(g => g.Key + " " + g.Count()))}), {w.Clearance.KeptOpen.Count} gate-route tiles kept open");
                }
                bool pc44 = ZWorld.PathV3Deviation; int off, offDone;                  // pathing_v3: the Zod column without DV44 / DV47
                try { ZWorld.PathV3Deviation = false; off = ClearanceWalk(false, out offDone); } finally { ZWorld.PathV3Deviation = pc44; }
                int on = ClearanceWalk(true, out int onDone);
                bool pref = getInt("oz_clearance_sim", 1) == 1 && getInt("oz_zod_exact", 0) == 0;
                return new Deviation
                {
                    id = "DV22", change = "Building clearance (units keep off the 3D building meshes)",
                    zod = "a building blocks its own tile pattern only (bfort.cpp:550-620, brobot / bvehicle / brepair / bradar); the 3D fort is 20 x 19.8 m in Zod's 10 x 12 / 10 x 11 rect, so its front / back wall rows 9-10 and some tower corners stand on Zod-walkable tiles, the radar dish base on its open corner (3,2)",
                    openz = "every Zod-walkable tile the building mesh covers (measured from the models, ZClearance.Mesh) is blocked at map load, except Zod's gate route through the fort (creation point to exit, ENTER_FORT); pushes / sidesteps never go deeper into a 2 px (robot) / 4 px (vehicle) margin round mesh and prop tiles, idle units ease out of it, group spots prefer spots clear of it; the view never slides a model into a mesh",
                    measured = string.Join("; ", parts) + $"; L02 red fort perimeter walk (grunt squad + heavy): unit samples standing in the fort's walls Zod grid {off} -> DV22 {on} (walks finished within 300 s: {offDone}/2 -> {onDone}/2; DV22 turns the blocked goals into detours)",
                    toggle = $"ZWorld.ClearanceDeviation (static, read when a world is created); PlayerPrefs oz_clearance_sim = {(getInt("oz_clearance_sim", 1) == 1 ? "1 (on, default)" : "0 (off)")}; off in 'Zod exact' (oz_zod_exact = 1)",
                    status = !pref ? DevActive + " (off)" : on == 0 && off > 0 ? DevActive : DevCheck,
                };
            }
            finally { ZWorld.ClearanceDeviation = saved; ZWorld.EndFreezeDeviation = freeze; ZWorld.FortGunDeviation = gun; }
        }

        static int ClearanceWalk(bool on, out int done)
        {
            ZWorld.ClearanceDeviation = on;
            var w = ZWorld.Create(SettingsText, MapText("L02"), 1);
            w.Objects.RemoveAll(o => o.IsUnit || o.Type == "grenades" || o.Type == "flag");
            w.Flags.Clear();
            foreach (var b in w.Objects) if (b.Kind == ZKind.Building) b.StopBuildingProduction();
            var fort = w.Objects.First(o => o.Type == "fort_back" && o.LocX == 48 * 16 && o.LocY == 72 * 16);
            int fx = fort.LocX >> 4, fy = fort.LocY >> 4;
            var goals = new List<(int x, int y, double a)>();
            for (int ty = fy - 1; ty <= fy + 11; ty++)
                for (int tx = fx - 1; tx <= fx + 10; tx++)
                {
                    bool gate = (tx == fx + 4 || tx == fx + 5) && ty >= fy && ty <= fy + 2;
                    bool zodWalk = w.Path.TilePassable(tx, ty, true) || w.Clearance.Added.Contains((tx, ty, "fort_back"));
                    if (zodWalk && !gate) goals.Add((tx * 16 + 8, ty * 16 + 8, Math.Atan2(ty - fy - 5.5, tx - fx - 5.0)));
                }
            goals.Sort((p, q) => p.a.CompareTo(q.a));
            var list = goals.Select(g => ZWaypoint.Move(g.x, g.y)).ToList();
            var squad = w.CreateRobotGroup("grunt", (fx - 4) * 16, (fy + 5) * 16, Team.Red);
            var heavy = w.CreateObject("vehicle", "heavy", (fx + 12) * 16, (fy + 5) * 16, Team.Red);
            var units = new List<ZObject> { squad, heavy }; units.AddRange(squad.Minions);
            w.SendWaypoints(Team.Red, squad, list);
            w.SendWaypoints(Team.Red, heavy, list.Skip(list.Count / 2).Concat(list.Take(list.Count / 2)).ToList());
            int inside = 0, ticks = 0;
            while (w.Time < 300 && (squad.Waypoints.Count > 0 || heavy.Waypoints.Count > 0))
            {
                w.Tick(ZConst.TICK); w.Events.Clear();
                if (++ticks % 5 != 0) continue;
                foreach (var u in units)
                {
                    if (w.Get(u.Id) != u) continue;
                    int tx = u.CenterX >> 4, ty = u.CenterY >> 4;
                    if (w.Clearance.MeshOwner(tx, ty) == fort.Id && w.Clearance.Cover(tx, ty) >= 5) inside++;
                }
            }
            done = (squad.Waypoints.Count == 0 ? 1 : 0) + (heavy.Waypoints.Count == 0 ? 1 : 0);
            return inside;
        }

        // ------------------------------------------------------------------ DV23 line of sight
        /// <summary>L02 red fort (fort_back 48,72) and robot factory (61,8): a light tank with a single blue grunt behind the wall
        /// (grunt passive), idle 30 s and then with an attack order; a howitzer in the same spot. Every direct shot is checked
        /// against ZSight's uncached walk at the moment it is fired.</summary>
        static Deviation DevLineOfSight(Func<string, int, int> getInt)
        {
            bool saved = ZWorld.LosDeviation, freeze = ZWorld.EndFreezeDeviation, gun = ZWorld.FortGunDeviation;
            ZWorld.EndFreezeDeviation = false; ZWorld.FortGunDeviation = false;
            try
            {
                var rows = new List<string>(); int through = 0, idleShots = 0, orderedShots = 0, howShots = 0, zodIdle = 0;
                foreach (var (name, sx, sy, tx, ty, hx, hy, htx, hty) in new[] { ("red fort wall", 45, 74, 50, 69, 45, 77, 49, 69), ("robot factory", 62, 14, 59, 9, 61, 14, 59, 6) })
                {
                    ZWorld.LosDeviation = false;
                    var z = LosRun("light", sx, sy, tx, ty, false, out _, out _, out _);
                    zodIdle += z.shots;
                    ZWorld.LosDeviation = true;
                    var idle = LosRun("light", sx, sy, tx, ty, false, out int t1, out _, out _);
                    var ord = LosRun("light", sx, sy, tx, ty, true, out int t2, out double first, out double moved);
                    var how = LosRun("howitzer", hx, hy, htx, hty, false, out _, out _, out _);
                    through += t1 + t2; idleShots += idle.shots; orderedShots += ord.shots; howShots += how.arcs;
                    rows.Add($"{name}: Zod {z.shots} shots through the wall in 30 s idle; DV23 idle {idle.shots} shots, attack order: first shot at {(first < 0 ? "never" : $"{first:0.00} s")}, {moved:0} px from its start, howitzer {how.arcs} shells over");
                }
                bool pref = getInt("oz_los_sim", 1) == 1 && getInt("oz_zod_exact", 0) == 0;
                bool ok = through == 0 && idleShots == 0 && orderedShots > 0 && howShots > 0 && zodIdle > 0;
                return new Deviation
                {
                    id = "DV23", change = "Line of sight for direct fire (never through walls; arcing fire goes over)",
                    zod = "range + destroyable barriers only (zmap.cpp:2266-2295): a 2D unit shoots through buildings, fort walls and cliffs",
                    openz = "direct fire (robot guns, lasers, jeep, tanks, gun, gatling, missile cannon, APC, bazooka) needs a clear tile line past cliffs, building meshes (>= 40 % of a tile), rocks, huts and tall props (ZSight, symmetric Bresenham, memoised); auto-targeting, attack-move and agro skip blocked targets, an ordered target is shot from the nearest spot in range with a line (none = the order ends); howitzer, Mobile Missile, the fort gun and grenade throws fire over; a tower gun sees over its own fort; automatic moves (idle agro, attack-move picks, auto-grab of flags / empty vehicles / cannons / crates) need the unit's own line of sight for every weapon, arcing included, and an automatic agro ends when the target goes out of sight; explicit player orders never need it; Zod's damage, ranges and rates unchanged",
                    measured = string.Join("; ", rows) + $"; direct shots through a blocked line {through}",
                    toggle = $"ZWorld.LosDeviation (static); PlayerPrefs oz_los_sim = {(getInt("oz_los_sim", 1) == 1 ? "1 (on, default)" : "0 (off)")}; off in 'Zod exact' (oz_zod_exact = 1)",
                    status = !pref ? DevActive + " (off)" : ok ? DevActive : DevCheck,
                };
            }
            finally { ZWorld.LosDeviation = saved; ZWorld.EndFreezeDeviation = freeze; ZWorld.FortGunDeviation = gun; }
        }

        static (int shots, int arcs) LosRun(string type, int sx, int sy, int tx, int ty, bool order, out int through, out double first, out double moved)
        {
            var w = ZWorld.Create(SettingsText, MapText("L02"), 1);
            w.Objects.RemoveAll(o => o.IsUnit || o.Type == "grenades" || o.Type == "flag");
            w.Flags.Clear();
            foreach (var b in w.Objects) if (b.Kind == ZKind.Building) b.StopBuildingProduction();
            var s = w.CreateObject(type == "howitzer" ? "cannon" : "vehicle", type, sx * 16, sy * 16, Team.Red);
            var t = w.CreateRobotGroup("grunt", tx * 16, ty * 16, Team.Blue, 1);
            t.Damage = 0;
            int x0 = s.CenterX, y0 = s.CenterY;
            if (order) w.SendWaypoints(Team.Red, s, new[] { new ZWaypoint { mode = WpMode.Attack, refId = t.Id, x = t.LocX + 8, y = t.LocY + 8 } });
            int shots = 0, arcs = 0; through = 0; first = -1; moved = 0;
            while (w.Time < 30 && w.Get(t.Id) == t)
            {
                w.Tick(ZConst.TICK);
                foreach (var e in w.Events)
                {
                    if (e.objId != s.Id || (e.type != ZEventType.Hitscan && e.type != ZEventType.MissileFired)) continue;
                    if (first < 0) { first = w.Time; moved = Math.Sqrt((double)(s.CenterX - x0) * (s.CenterX - x0) + (double)(s.CenterY - y0) * (s.CenterY - y0)); }
                    if (s.ArcingFire) { arcs++; continue; }
                    shots++;
                    if (ZWorld.LosDeviation && !w.Sight.ClearUncached(s.CenterX, s.CenterY, w.Sight.ExemptOf(s), t.CenterX, t.CenterY, 0)) through++;
                }
                w.Events.Clear();
            }
            return (shots, arcs);
        }
    }
}
