#if !OPENZ_HARNESS
using System;
using System.Collections.Generic;
using System.Linq;
using OpenZ.Sim;

namespace OpenZ.EditorTools
{
    /// <summary>The Deviations row DV117 "burning ground hurts an enemy who stays in it" (the rule: Sim/ZWorld.GroundFire.cs), measured
    /// headless on the level 2 map without its start units. Every case is its own world: one unit placed on free open ground, a patch
    /// of burning ground lit at it (or beside it) for a team, the world runs; one case lets a real Pyro squad flame an enemy tank.</summary>
    public static partial class OpenZParity
    {
        static void AddGroundFireDeviation(List<Deviation> list)
        {
            try { list.Add(DevGroundFire()); }
            catch (Exception ex) { list.Add(new Deviation { id = "DV117", change = "DevGroundFire", zod = "-", openz = "-", measured = "probe threw " + ex.GetType().Name + ": " + ex.Message, toggle = "-", status = DevCheck }); }
        }

        /// <summary>One case's numbers: the unit's health before and after, the HP burning ground took, the patches lit.</summary>
        internal sealed class GfResult
        {
            public bool placed; public int hp0, hp1, taken, lit;
            public string Text => !placed ? "no free spot" : $"{hp0} -> {hp1} HP ({taken} by burning ground, {lit} patch{(lit == 1 ? "" : "es")} lit)";
        }

        /// <summary>The level 2 map without its robots, vehicles and guns.</summary>
        internal static string GfNoUnits(string mapJson)
        {
            var m = ZMapData.FromJson(mapJson);
            m.objects.RemoveAll(o => { string c = o.kind.Split(':')[0]; return c == "robot" || c == "vehicle" || c == "cannon"; });
            return UnityEngine.JsonUtility.ToJson(m);
        }

        /// <summary>Free open ground: 9 x 9 walkable land tiles round it (no water, no building), the nearest such spot to the map's middle.</summary>
        static bool GfSpot(ZWorld w, out int px, out int py)
        {
            int cx = w.Map.width / 2, cy = w.Map.height / 2;
            for (int r = 0; r < Math.Max(w.Map.width, w.Map.height); r++)
                for (int k = 0; k < Math.Max(1, 8 * r); k++)
                {
                    double a = k * 2 * Math.PI / Math.Max(1, 8 * r);
                    int tx = cx + (int)Math.Round(r * Math.Cos(a)), ty = cy + (int)Math.Round(r * Math.Sin(a));
                    bool ok = true;
                    for (int i = -4; i <= 4 && ok; i++)
                        for (int j = -4; j <= 4 && ok; j++)
                        {
                            if (!w.Path.TilePassable(tx + i, ty + j, true) || w.Map.ClassAt(tx + i, ty + j) == ZClass.Water) { ok = false; break; }
                            int x = (tx + i) * 16 + 8, y = (ty + j) * 16 + 8;
                            foreach (var o in w.Objects) if (o.Kind == ZKind.Building && o.Contains(x, y)) { ok = false; break; }
                        }
                    if (!ok) continue;
                    px = tx * 16; py = ty * 16;
                    return true;
                }
            px = py = 0;
            return false;
        }

        static ZWorld GfWorld(int seed)
        {
            var w = ZWorld.Create(SettingsText, GfNoUnits(MapText("L02")), seed);
            w.Bots.Clear();
            foreach (var (kt, kx) in new[] { (Team.Red, 16), (Team.Blue, (w.Map.width - 2) * 16) })
            {   // one red and one blue grunt in far corners that cannot die and do no harm: neither side ever has no units left
                var d = w.CreateObject("robot", "grunt", kx, 16, kt);
                d.MaxHealth = d.Health = 1000000000; d.Damage = 0;
            }
            return w;
        }

        /// <summary>One case: a <paramref name="type"/> ("grunt" or "jeep") of <paramref name="team"/> stands still; burning ground of
        /// <paramref name="fire"/> is lit <paramref name="offsetPx"/> px east of its centre at the start (and again every 2 s when
        /// <paramref name="keepLit"/>, as a Pyro flaming there would); the world runs <paramref name="secs"/> s; rule = the DV117 switch.</summary>
        internal static GfResult GfCase(string type, Team team, Team fire, int offsetPx, bool keepLit, double secs, bool rule, int seed)
        {
            bool was = ZWorld.GroundFireDeviation;
            ZWorld.GroundFireDeviation = rule;
            var r = new GfResult();
            try
            {
                var w = GfWorld(seed);
                if (!GfSpot(w, out int sx, out int sy)) return r;
                r.placed = true;
                var u = w.CreateObject(type == "grunt" ? "robot" : "vehicle", type, sx, sy, team);
                u.Damage = 0;
                r.hp0 = u.Health;
                int fx = u.CenterX + offsetPx, fy = u.CenterY;
                double t0 = w.Time, nextLight = t0;
                while (w.Time < t0 + secs)
                {
                    if (w.Time >= nextLight - 1e-9 && (keepLit || nextLight == t0)) { w.GroundFireAt(fx, fy, fire); nextLight += 2.0; }
                    w.Tick(ZConst.TICK);
                    w.Events.Clear();
                }
                r.hp1 = u.Health; r.taken = u.GroundFireTaken; r.lit = w.GroundFiresLit;
                return r;
            }
            finally { ZWorld.GroundFireDeviation = was; }
        }

        /// <summary>The real thing: a red Pyro squad 6 tiles from a blue medium tank (crewed; it cannot die and does no harm) flames it
        /// for <paramref name="secs"/> s on an attack order. Returns the tank's ground-fire HP, the squad's, the patches lit, whether
        /// every patch is red and one lies under the tank at the end.</summary>
        internal static (bool placed, int tank, int squad, int lit, bool allRed, bool underTank, int hits) GfPyroCase(double secs, int seed)
        {
            bool was = ZWorld.GroundFireDeviation;
            ZWorld.GroundFireDeviation = true;
            try
            {
                var w = GfWorld(seed);
                if (!GfSpot(w, out int sx, out int sy)) return (false, 0, 0, 0, false, false, 0);
                var tank = w.CreateObject("vehicle", "medium", sx, sy, Team.Blue);
                tank.MaxHealth = tank.Health = 1000000000; tank.Damage = 0;
                var lead = w.CreateRobotGroup("pyro", sx - 96, sy, Team.Red);
                var squad = new List<ZObject> { lead }; squad.AddRange(lead.Minions);
                var ids = new HashSet<int>(squad.Select(o => o.Id));
                var wp = ZWaypoint.Move(tank.CenterX, tank.CenterY, false, tank.Id);
                wp.mode = WpMode.Attack;
                w.SendWaypoints(Team.Red, lead, new[] { wp });
                int hits = 0;
                while (w.Time < secs)
                {
                    w.Tick(ZConst.TICK);
                    foreach (var e in w.Events) if (e.type == ZEventType.Hitscan && e.flag && e.otherId == tank.Id && ids.Contains(e.objId)) hits++;
                    w.Events.Clear();
                }
                var under = w.GroundFireUnder(tank);
                return (true, tank.GroundFireTaken, squad.Sum(o => o.GroundFireTaken), w.GroundFiresLit, w.GroundFires.All(f => f.team == Team.Red), under != null, hits);
            }
            finally { ZWorld.GroundFireDeviation = was; }
        }

        static Deviation DevGroundFire()
        {
            string measured; bool pass;
            try
            {
                var set = ZSettings.Parse(SettingsText);
                int grunt = set.Unit("grunt").MaxHealth, jeep = set.Unit("jeep").MaxHealth;
                int rb = ZWorld.GroundFireRobotHp, vb = ZWorld.GroundFireVehicleHp;
                var a = GfCase("grunt", Team.Blue, Team.Red, 0, true, 10.25, true, 21);             // an enemy grunt 10 s in a burning patch
                var a2 = GfCase("grunt", Team.Blue, Team.Red, 0, true, 10.25, true, 22);            // the same, another seed: the same numbers
                var b = GfCase("grunt", Team.Red, Team.Red, 0, true, 10.25, true, 21);              // the patch's own team
                var c = GfCase("jeep", Team.Blue, Team.Red, 0, true, 10.25, true, 23);              // an enemy jeep (crewed)
                var d = GfCase("grunt", Team.Blue, Team.Red, 24, true, 10.25, true, 24);            // 3 m from the patch's centre: inside
                var e = GfCase("grunt", Team.Blue, Team.Red, 40, true, 10.25, true, 25);            // 5 m from it: outside
                var f = GfCase("grunt", Team.Blue, Team.Red, 0, false, 10.25, true, 26);            // lit once: it goes out after 7 s
                var g = GfCase("grunt", Team.Blue, Team.Red, 0, true, 10.25, false, 27);            // the rule off
                var p = GfPyroCase(12.0, 28);                                                       // a real Pyro squad flames an enemy tank
                bool Lost(GfResult x, int hp) => x.placed && x.taken == hp && x.hp0 - x.hp1 == hp;
                pass = Lost(a, 10 * rb) && a2.placed && a2.hp1 == a.hp1 && a2.taken == a.taken && Lost(b, 0) && Lost(c, 10 * vb) && Lost(d, 10 * rb) && Lost(e, 0)
                       && Lost(f, 6 * rb) && Lost(g, 0) && g.lit == 0
                       && p.placed && p.hits > 0 && p.lit > 0 && p.allRed && p.underTank && p.tank > 0 && p.tank % vb == 0 && p.tank <= 12 * vb && p.squad == 0;
                measured = $"a bite after each whole second inside: robot {rb} HP ({100.0 * rb / grunt:0.0} % of a grunt's {grunt}), vehicle {vb} HP ({100.0 * vb / jeep:0.0} % of a jeep's {jeep}); " +
                           $"patch {ZWorld.GroundFireRadiusPx} px, {ZWorld.GroundFireLife:0} s after its last shot | " +
                           $"enemy grunt 10 s in it: {a.Text} | again, another seed: {a2.Text} | the patch's own team: {b.Text} | enemy jeep 10 s: {c.Text} | " +
                           $"enemy grunt 3 m from its centre: {d.Text} | 5 m from it: {e.Text} | lit once (out after 7 s): {f.Text} | rule off: {g.Text} | " +
                           $"a red Pyro squad flames a blue tank 12 s: {p.hits} hits, {p.lit} patches lit, all red {p.allRed}, one under the tank {p.underTank}, " +
                           $"the tank {p.tank} HP by burning ground ({p.tank / Math.Max(1, vb)} bites), the Pyros {p.squad}";
            }
            catch (Exception ex) { measured = "the probe threw " + ex.GetType().Name + ": " + ex.Message; pass = false; }
            return new Deviation
            {
                id = "DV117", change = "Burning ground hurts an enemy who stays in it: a little every second, never nothing",
                zod = "the Pyro's flame hits its target only (rpyro.cpp, zobject.cpp:2187-2199); there is no burning ground in the rules",
                openz = "Sim/ZWorld.GroundFire.cs: every Pyro shot (hit or miss, also a hatch driver's) lights a patch at what it aims at (28 px, burns 7 s after its last shot, never on water, never moves); " +
                        "a robot on foot or a crewed vehicle of the OTHER team standing in one loses 16 HP (robot) / 8 HP (vehicle) after each whole second inside; never the patch's own team, guns, buildings, " +
                        "map items or roof defenders; no random draw. View: unchanged (the drawn patches are lit at the same shots)",
                measured = measured,
                toggle = "ZWorld.GroundFireDeviation: on in the game, off in 'Zod exact' (View/ZGame.cs); off by default headless, in the parity rows and in the counted checks",
                status = pass ? DevActive : DevCheck,
            };
        }
    }
}
#endif
