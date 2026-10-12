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
    /// headless in the same sim the parity rows use (each probe once with the switch on and once off, other deviations as the build
    /// has them; every switch restored). Compiles in the headless harness too (OPENZ_HARNESS: no UnityEditor). Menus:
    /// OpenZ/Deviations/... (PlayerPrefs, read by ZGame at every start; all off in 'Zod exact' oz_zod_exact = 1).
    /// </summary>
    public static partial class OpenZParity
    {
        static T WithSwitch<T>(Func<bool> get, Action<bool> set, bool value, Func<T> probe)
        {
            bool saved = get(); set(value);
            try { return probe(); } finally { set(saved); }
        }

        static ZObject NbFort(ZWorld w, Team t) => w.Objects.First(o => o.IsFort && o.Owner == t);
        static void NbQuietTeam(ZWorld w, Team t) { foreach (var o in w.Objects.Where(o => o.IsUnit && o.Owner == t).ToList()) { o.Owner = Team.Neutral; o.Drivers.Clear(); o.Waypoints.Clear(); o.Disengage(); } }
        static ZObject NbDummy(ZWorld w, string cat, string type, int x, int y, Team t)
        {
            var o = cat == "robot" ? w.CreateRobotGroup(type, x, y, t, 1) : w.CreateObject(cat, type, x, y, t);
            o.MaxHealth = o.Health = 1000000000; o.Damage = 0; o.MoveSpeed = 0; o.RealMoveSpeed = 0;
            return o;
        }
        static string NbS(double s) => s < 0 ? "never" : s.ToString("0.0", System.Globalization.CultureInfo.InvariantCulture) + " s";

        // ------------------------------------------------------------------ DV25 fort mortar (NB27)
        /// <summary>L02 blue fort, only the fort armed: an unkillable static red heavy tank 170 px west of its centre, 70 s: shots, their
        /// interval and flight. Then (mortar on) a red heavy tank ordered to attack the fort from 6 tiles off the gate: gun destroyed at.</summary>
        static (int shots, double interval, double flight, double gunDead, int gunHp) FortMortarProbe(bool mortar)
        {
            bool fg = ZWorld.FortGunDeviation, mo = ZWorld.FortMortarDeviation; ZWorld.FortGunDeviation = true; ZWorld.FortMortarDeviation = mortar;
            try
            {
                var w = ZWorld.Create(SettingsText, MapText("L02"), 1);
                var fort = NbFort(w, Team.Blue);
                NbQuietTeam(w, Team.Blue); NbQuietTeam(w, Team.Red);
                NbDummy(w, "robot", "grunt", 20 * 16, 60 * 16, Team.Blue);                                  // keeps blue alive (no game over)
                var tank = NbDummy(w, "vehicle", "heavy", fort.CenterX - 170 - 16, fort.CenterY - 8 - 16, Team.Red);
                var times = new List<double>(); var flights = new List<double>();
                while (w.Time < 70)
                {
                    w.Tick(ZConst.TICK);
                    foreach (var e in w.Events) if (e.type == ZEventType.MissileFired && e.objId == fort.Id) { times.Add(w.Time); flights.Add(e.value); }
                    w.Events.Clear();
                }
                double interval = times.Count >= 2 ? (times[times.Count - 1] - times[0]) / (times.Count - 1) : -1;
                double gunDead = -1; int hp = fort.FortGunHealth;
                if (mortar)
                {
                    var w2 = ZWorld.Create(SettingsText, MapText("L02"), 2);
                    var f2 = NbFort(w2, Team.Blue);
                    NbQuietTeam(w2, Team.Blue); NbQuietTeam(w2, Team.Red);
                    NbDummy(w2, "robot", "grunt", 20 * 16, 60 * 16, Team.Blue);
                    var hv = w2.CreateObject("vehicle", "heavy", f2.CenterX - 16, f2.LocY + f2.HeightPix + 96, Team.Red);
                    w2.SendWaypoints(Team.Red, hv, new[] { new ZWaypoint { mode = WpMode.Attack, refId = f2.Id, x = f2.CenterX, y = f2.CenterY } });
                    while (w2.Time < 120 && gunDead < 0 && !hv.Destroyed) { w2.Tick(ZConst.TICK); w2.Events.Clear(); if (f2.FortGunDestroyed) gunDead = w2.Time; }
                    hp = f2.FortGunHealth;
                }
                return (times.Count, interval, flights.Count > 0 ? flights.Average() : -1, gunDead, hp);
            }
            finally { ZWorld.FortGunDeviation = fg; ZWorld.FortMortarDeviation = mo; }
        }

        static Deviation DevFortMortar(Func<string, int, int> getInt)
        {
            var on = FortMortarProbe(true); var off = FortMortarProbe(false);
            bool pref = getInt("oz_fort_mortar", 1) == 1 && getInt("oz_fortgun_sim", 1) == 1 && getInt("oz_zod_exact", 0) == 0;
            bool ok = on.shots >= 2 && Math.Abs(on.interval - ZObject.FortMortarReload) < 0.1 && on.flight > 2.5 && on.gunDead > 0 && off.shots > on.shots;
            return new Deviation
            {
                id = "DV25", change = "Fort mortar: the DV7 fort gun reloads 30 s, lobs a slow dodgeable shell, and can be destroyed",
                zod = "forts have no weapon (bfort.cpp); DV7 gave the fort roof gun the Zod howitzer's numbers (4.86 s, 95 px/s shell with lead, 200 px), untargetable",
                openz = $"with DV7: one shot every {ZObject.FortMortarReload:0} s (after the 1.4 s lay), aimed at where the target stands (no lead) + Zod's +-16 px scatter, flight = a 60 deg lob under {ZObject.FortMortarGravity:0} m/s^2 from {ZObject.FortMortarMuzzleM:0} m (ZObject.FortMortarFlight: {ZObject.FortMortarFlight(144):0.0}-{ZObject.FortMortarFlight(200):0.0} s), the target still gets Zod's DodgeMissile; damage / splash = howitzer; " +
                        $"the gun has {ZObject.FortGunMaxHealth} HP: every enemy shell that damages the fort damages its gun by as much; destroyed = silent for the game; no fire after the game over; view: barrel at 60 deg, reload ring, heavier boom + whoosh, camera shake on screen",
                measured = $"L02 blue fort vs a static red heavy tank 170 px west, 70 s: {on.shots} shots every {NbS(on.interval)}, flight {NbS(on.flight)} (DV25 off: {off.shots} shots every {NbS(off.interval)}, flight {NbS(off.flight)}); " +
                           $"a red heavy tank ordered onto the fort from 6 tiles off the gate destroys the gun at {NbS(on.gunDead)} (gun HP left {on.gunHp})",
                toggle = $"PlayerPrefs oz_fort_mortar = {(getInt("oz_fort_mortar", 1) == 1 ? "1 (on, default)" : "0 (off: DV7 with the howitzer's numbers)")}; menu OpenZ/Deviations/Fort Mortar; needs DV7 (oz_fortgun_sim); off in 'Zod exact' and in the parity report's Zod rows (no fort gun)",
                status = !ok ? DevCheck : pref ? DevActive : DevActive + " (off by pref)",
            };
        }

        // ------------------------------------------------------------------ DV26 building reach (NB14)
        /// <summary>L02, a red heavy tank 10 tiles south of the blue fort's gate ordered to attack the fort (fort gun off, blue units
        /// quiet): time of its first shot, its distance to the fort's walls (the ZClearance mesh tiles) and to the fort centre then.</summary>
        static (double first, double wall, double centre, int hpLost) BuildingReachProbe(bool reach)
        {
            bool br = ZWorld.BuildingReachDeviation, fg = ZWorld.FortGunDeviation; ZWorld.BuildingReachDeviation = reach; ZWorld.FortGunDeviation = false;
            try
            {
                var w = ZWorld.Create(SettingsText, MapText("L02"), 5);
                var fort = NbFort(w, Team.Blue);
                NbQuietTeam(w, Team.Blue); NbQuietTeam(w, Team.Red);
                NbDummy(w, "robot", "grunt", 20 * 16, 60 * 16, Team.Blue);
                var hv = w.CreateObject("vehicle", "heavy", fort.CenterX - 16, fort.LocY + fort.HeightPix + 128, Team.Red);
                w.SendWaypoints(Team.Red, hv, new[] { new ZWaypoint { mode = WpMode.Attack, refId = fort.Id, x = fort.CenterX, y = fort.CenterY } });
                double first = -1, wall = -1, centre = -1; int hp0 = fort.Health;
                while (w.Time < 90 && first < 0)
                {
                    w.Tick(ZConst.TICK);
                    foreach (var e in w.Events)
                        if (e.type == ZEventType.MissileFired && e.objId == hv.Id && first < 0)
                        {
                            first = w.Time; centre = Math.Sqrt(Math.Pow(hv.CenterX - fort.CenterX, 2) + Math.Pow(hv.CenterY - fort.CenterY, 2));
                            wall = Math.Sqrt(fort.WallPoint(hv.CenterX, hv.CenterY, out _, out _));
                        }
                    w.Events.Clear();
                }
                double until = w.Time + 30; while (first >= 0 && w.Time < until) { w.Tick(ZConst.TICK); w.Events.Clear(); }
                return (first, wall, centre, hp0 - fort.Health);
            }
            finally { ZWorld.BuildingReachDeviation = br; ZWorld.FortGunDeviation = fg; }
        }

        static Deviation DevBuildingReach(Func<string, int, int> getInt)
        {
            var on = BuildingReachProbe(true); var off = BuildingReachProbe(false);
            bool pref = getInt("oz_building_reach", 1) == 1 && getInt("oz_zod_exact", 0) == 0;
            int R = SettingsUnit("heavy").attack_radius;
            bool ok = on.first >= 0 && on.wall <= R + 0.5 && on.wall >= R - 24 && on.hpLost > 0 && off.first >= 0 && off.centre <= R + 0.5;
            return new Deviation
            {
                id = "DV26", change = "Building reach: units shell the nearest wall of a building as soon as it is in range",
                zod = "range is centre to centre (WithinAttackRadius, points_within_distance); a shell at a building is aimed at its centre and its splash is measured from the centre (zserver.cpp:1080-1098): a tank must drive to ~50 px of a fort's wall before it fires",
                openz = "range, the aim point (the nearest wall, 4 px in, then Zod's scatter) and the aimed building's own blast falloff are measured to the building's walls: its mesh tiles (ZClearance coverage >= 40 %, the gate included; a type without a table: its rect); units, other buildings and every other blast keep Zod's centre rule",
                measured = $"L02, a heavy tank (range {R} px) 8 tiles off the blue fort's gate ordered to attack the fort: first shot at {NbS(on.first)} from {on.wall:0} px off the wall ({on.centre:0} px from the centre), fort HP -{on.hpLost} in the next 30 s; " +
                           $"Zod rule: {NbS(off.first)} from {off.wall:0} px off the wall ({off.centre:0} px from the centre), HP -{off.hpLost}",
                toggle = $"PlayerPrefs oz_building_reach = {(getInt("oz_building_reach", 1) == 1 ? "1 (on, default)" : "0 (off)")}; menu OpenZ/Deviations/Building Reach; off in 'Zod exact' and in the parity report's rows",
                status = !ok ? DevCheck : pref ? DevActive : DevActive + " (off by pref)",
            };
        }

        static ZUnitSettings SettingsUnit(string type) => ZSettings.Parse(SettingsText).Unit(type);

        // ------------------------------------------------------------------ DV27 exposed drivers (NB11)
        /// <summary>(a) a red grunt squad with a blue grunt (created first) and a blue jeep both in range: its first target.
        /// (b) a blue jeep (it fires back at a red decoy in front of it, so it faces the decoy) sniped by a red grunt squad standing
        /// in FRONT of it (beside the decoy) or BEHIND it: median time to the driver kill over 6 seeds.</summary>
        static (string first, double front, double rear, int rearRolls) ExposedProbe(bool on)
        {
            bool ex = ZWorld.ExposedDriverDeviation; ZWorld.ExposedDriverDeviation = on;
            try
            {
                string first;
                {
                    var w = ZWorld.Create(SettingsText, MapText("L02"), 1);
                    NbQuietTeam(w, Team.Blue); NbQuietTeam(w, Team.Red);
                    var g = NbDummy(w, "robot", "grunt", 48 * 16, 36 * 16, Team.Blue);
                    var j = NbDummy(w, "vehicle", "jeep", 48 * 16, 40 * 16, Team.Blue);
                    var sq = w.CreateRobotGroup("grunt", 42 * 16, 38 * 16, Team.Red);
                    ZObject t = null;
                    while (w.Time < 5 && t == null) { w.Tick(ZConst.TICK); w.Events.Clear(); t = sq.AttackObject; }
                    first = t == null ? "none" : t.Type;
                }
                double Snipe(bool rear, int seed)
                {
                    var w = ZWorld.Create(SettingsText, MapText("L02"), seed);
                    NbQuietTeam(w, Team.Blue); NbQuietTeam(w, Team.Red);
                    NbDummy(w, "robot", "grunt", 20 * 16, 60 * 16, Team.Blue);
                    var jeep = w.CreateObject("vehicle", "jeep", 48 * 16, 38 * 16, Team.Blue);
                    jeep.MaxHealth = jeep.Health = 1000000000; jeep.Damage = 1; jeep.CanSnipe = false; jeep.MoveSpeed = 0; jeep.RealMoveSpeed = 0;
                    NbDummy(w, "robot", "grunt", 54 * 16, 38 * 16, Team.Red).Damage = 0;              // the decoy east of the jeep: the jeep looks east
                    var sq = w.CreateRobotGroup("grunt", (rear ? 42 : 54) * 16, (rear ? 38 : 40) * 16, Team.Red);
                    w.SendWaypoints(Team.Red, sq, new[] { new ZWaypoint { mode = WpMode.Attack, refId = jeep.Id, x = jeep.CenterX, y = jeep.CenterY } });
                    while (w.Time < 120 && jeep.Owner != Team.Neutral) { w.Tick(ZConst.TICK); w.Events.Clear(); }
                    return jeep.Owner == Team.Neutral ? w.Time : 120;
                }
                int rolls0 = ZWorld.RearHitsCounted;
                var fr = Enumerable.Range(1, 6).Select(s => Snipe(false, s)).OrderBy(x => x).ToList();
                var rr = Enumerable.Range(1, 6).Select(s => Snipe(true, s)).OrderBy(x => x).ToList();
                return (first, fr[3], rr[3], ZWorld.RearHitsCounted - rolls0);
            }
            finally { ZWorld.ExposedDriverDeviation = ex; }
        }

        static Deviation DevExposedDrivers(Func<string, int, int> getInt)
        {
            var on = ExposedProbe(true); var off = ExposedProbe(false);
            bool pref = getInt("oz_exposed_drivers", 1) == 1 && getInt("oz_zod_exact", 0) == 0;
            bool ok = on.first == "jeep" && off.first == "grunt" && on.rear < on.front && on.rearRolls > 0 && off.rearRolls == 0;
            return new Deviation
            {
                id = "DV27", change = "Exposed drivers: snipers target an exposed driver first; a vehicle hit from behind loses its driver faster",
                zod = "no such rule: passive engagement takes the first enemy in range in object order (zobject.cpp:1603-1853); the snipe roll has no direction (zobject.cpp:2190, snipe_chance per hit); CanBeSniped is the lid only (zvehicle.cpp:178-186, zcannon.cpp:46-49)",
                openz = $"a unit that can snipe (Zod's can_snipe: grunt, psycho, sniper, pyro, laser, jeep, gatling) auto-targets an enemy in range whose driver is exposed (CanBeSniped: jeep / cannon crew, a tank with its lid open) before any other, and switches to one while it stands on no order; " +
                        $"a hit from inside the rear {ZWorld.RearDeg:0} deg cone of where the vehicle looks (its target, else its last heading) rolls the snipe at {ZWorld.RearSnipeFactor:0.#} x Zod's chance (max {ZWorld.RearSnipeCap:0.##}); the same one rand() draw; howitzers keep Zod's rule (a shell never snipes)",
                measured = $"red grunt squad, a blue grunt (older) and a blue jeep in range: first target {on.first} (Zod rule: {off.first}); blue jeep facing a decoy, sniped by a red grunt squad (median of 6): from the front {NbS(on.front)}, from behind {NbS(on.rear)} ({on.rearRolls} rear rolls) (Zod rule: {NbS(off.front)} / {NbS(off.rear)})",
                toggle = $"PlayerPrefs oz_exposed_drivers = {(getInt("oz_exposed_drivers", 1) == 1 ? "1 (on, default)" : "0 (off)")}; menu OpenZ/Deviations/Exposed Drivers; off in 'Zod exact' and in the parity report's rows",
                status = !ok ? DevCheck : pref ? DevActive : DevActive + " (off by pref)",
            };
        }

        // ------------------------------------------------------------------ DV28 attack approach (NB13)
        /// <summary>8 seeded scenarios on L02 per unit type (missile_launcher, heavy, grunt): an attack order on a static enemy light tank
        /// out of range (range + 48 / + 160 px, 2.2 x range, open spots, same A* component): fired within 90 s, median time, median path
        /// driven before the first shot.</summary>
        static (int n, int fired, double median, double driven) ApproachProbe(bool on, string type)
        {
            bool ap = ZWorld.ApproachDeviation, fg = ZWorld.FortGunDeviation; ZWorld.ApproachDeviation = on; ZWorld.FortGunDeviation = false;
            try
            {
                var rng = new System.Random(4242 + type.Length);
                int n = 0, fired = 0; var times = new List<double>(); var paths = new List<double>();
                var w0 = ZWorld.Create(SettingsText, MapText("L02"), 1);
                bool Veh(ZWorld w, int tx, int ty) { var c = w.Map.ClassAt(tx, ty); return (c == ZClass.Ground || c == ZClass.Road) && w.Path.TilePassable(tx, ty, false); }
                bool Free(ZWorld w, int tx, int ty) => Veh(w, tx, ty) && Veh(w, tx + 1, ty) && Veh(w, tx, ty + 1) && Veh(w, tx + 1, ty + 1) &&
                    !w.Objects.Any(o => !o.Destroyed && o.Type != "flag" && o.LocX < tx * 16 + 32 && tx * 16 < o.LocX + o.WidthPix && o.LocY < ty * 16 + 32 && ty * 16 < o.LocY + o.HeightPix);
                int R = w0.S.Unit(type).attack_radius;
                for (int k = 0; k < 40 && n < 8; k++)
                {
                    int sx = 4 + rng.Next(w0.Map.width - 8), sy = 4 + rng.Next(w0.Map.height - 8);
                    double d = k % 3 == 0 ? R + 48 : k % 3 == 1 ? R + 160 : 2.2 * R, b = rng.NextDouble() * 2 * Math.PI;
                    int tx = (int)(sx + Math.Cos(b) * d / 16), ty = (int)(sy + Math.Sin(b) * d / 16);
                    if (tx < 2 || ty < 2 || tx >= w0.Map.width - 3 || ty >= w0.Map.height - 3 || !Free(w0, sx, sy) || !Free(w0, tx, ty)) continue;
                    if (w0.Objects.Any(o => o.IsFort && Math.Abs(o.CenterX - sx * 16) + Math.Abs(o.CenterY - sy * 16) < 360)) continue;
                    var w = ZWorld.Create(SettingsText, MapText("L02"), 11 + k);
                    NbQuietTeam(w, Team.Blue); NbQuietTeam(w, Team.Red);
                    var tgt = NbDummy(w, "vehicle", "light", tx * 16, ty * 16, Team.Blue);
                    if (w.Pathing.Reach.Comp(sx, sy, false, true) != w.Pathing.Reach.Comp(tx, ty, false, true)) continue;
                    var a = ZSettings.Robots.Contains(type) ? w.CreateRobotGroup(type, sx * 16, sy * 16, Team.Red) : w.CreateObject("vehicle", type, sx * 16, sy * 16, Team.Red);
                    var ids = new HashSet<int> { a.Id }; foreach (var m in a.Minions) ids.Add(m.Id);
                    w.SendWaypoints(Team.Red, a, new[] { new ZWaypoint { mode = WpMode.Attack, refId = tgt.Id, x = tgt.CenterX, y = tgt.CenterY } });
                    n++; double first = -1, path = 0; int lx = a.CenterX, ly = a.CenterY;
                    while (w.Time < 90 && first < 0)
                    {
                        w.Tick(ZConst.TICK);
                        foreach (var e in w.Events) if ((e.type == ZEventType.MissileFired || e.type == ZEventType.Hitscan) && ids.Contains(e.objId) && e.otherId == tgt.Id) first = w.Time;
                        w.Events.Clear();
                        path += Math.Sqrt((double)(a.CenterX - lx) * (a.CenterX - lx) + (double)(a.CenterY - ly) * (a.CenterY - ly)); lx = a.CenterX; ly = a.CenterY;
                    }
                    if (first >= 0) { fired++; times.Add(first); paths.Add(path); }
                }
                double Med(List<double> l) => l.Count == 0 ? -1 : l.OrderBy(x => x).ElementAt(l.Count / 2);
                return (n, fired, Med(times), Med(paths));
            }
            finally { ZWorld.ApproachDeviation = ap; ZWorld.FortGunDeviation = fg; }
        }

        static Deviation DevApproach(Func<string, int, int> getInt)
        {
            bool pref = getInt("oz_attack_approach", 1) == 1 && getInt("oz_zod_exact", 0) == 0;
            var parts = new List<string>(); bool ok = true;
            foreach (var t in new[] { "missile_launcher", "heavy", "grunt" })
            {
                var on = ApproachProbe(true, t); var off = ApproachProbe(false, t);
                parts.Add($"{t}: fired {on.fired}/{on.n}, median {NbS(on.median)} after {on.driven:0} px driven (Zod rule {off.fired}/{off.n}, {NbS(off.median)}, {off.driven:0} px)");
                ok &= on.n > 0 && on.fired >= off.fired;
            }
            return new Deviation
            {
                id = "DV28", change = "Attack approach: an attack on a target out of range goes to the nearest reachable spot in range",
                zod = "an ATTACK waypoint walks toward the target itself (NearestAttackLoc, zobject.cpp:1985-2040) and stops when in range: a target across a river is reached by the long way round (a far bridge) even when the bank is in range; with OpenZ's bridge queue a unit can wait at a bridge 1 px out of range",
                openz = "the goal is the nearest footprint in range found by a breadth-first walk of the unit's own A* graph (DV26: to a building's walls; a clear line for direct fire, DV23; no Zod barrier; never on a bridge deck, never on a parked unit), and the unit walks that walk (round parked units); a unit that got stuck on the way falls back to Zod's goal and the pathing layer's detours; none in range anywhere = Zod's goal",
                measured = "L02, 8 seeded out-of-range attack orders per type on a static light tank: " + string.Join("; ", parts),
                toggle = $"PlayerPrefs oz_attack_approach = {(getInt("oz_attack_approach", 1) == 1 ? "1 (on, default)" : "0 (off)")}; menu OpenZ/Deviations/Attack Approach; off in 'Zod exact' and in the parity report's rows",
                status = !ok ? DevCheck : pref ? DevActive : DevActive + " (off by pref)",
            };
        }

        // ------------------------------------------------------------------ DV29 armour chip (NB5 proposal, default OFF)
        static (int grunts, int jeep) ChipProbe(bool on)
        {
            bool ac = ZWorld.ArmourChipDeviation; ZWorld.ArmourChipDeviation = on;
            try
            {
                int Hull(string type)
                {
                    var w = ZWorld.Create(SettingsText, MapText("L02"), 1);
                    NbQuietTeam(w, Team.Blue); NbQuietTeam(w, Team.Red);
                    var tank = NbDummy(w, "vehicle", "light", 50 * 16, 38 * 16, Team.Blue);           // lid shut (it never engages): every hit on the hull
                    var a = type == "grunt" ? w.CreateRobotGroup("grunt", 45 * 16, 38 * 16, Team.Red) : w.CreateObject("vehicle", type, 45 * 16, 38 * 16, Team.Red);
                    int hp0 = tank.Health;
                    while (w.Time < 10) { w.Tick(ZConst.TICK); w.Events.Clear(); }
                    return hp0 - tank.Health;
                }
                return (Hull("grunt"), Hull("jeep"));
            }
            finally { ZWorld.ArmourChipDeviation = ac; }
        }

        static Deviation DevArmourChip(Func<string, int, int> getInt)
        {
            var on = ChipProbe(true); var off = ChipProbe(false);
            bool pref = getInt("oz_armour_chip", 0) == 1 && getInt("oz_zod_exact", 0) == 0;
            bool ok = off.grunts > 0 && on.grunts > off.grunts && on.jeep > off.jeep;
            return new Deviation
            {
                id = "DV29", change = "Armour chip (proposal, default OFF): light automatic weapons chip vehicle / cannon hulls harder",
                zod = "bullets deal their full Zod damage to anything (zobject.cpp:2187-2199); measured time-to-kill matches Zod for every attacker / target pair: a grunt squad needs 73 s for a light tank's hull, 181 s for a heavy's (DC2 snipes the driver first: 32 / 57 s)",
                openz = $"OFF by default. On: grunt, psycho, jeep, gatling (and a bullet-crewed APC) bullets deal {ZWorld.ArmourChipFactor:0.#} x damage to a vehicle / cannon hull; driver hits, robots, rockets and shells unchanged",
                measured = $"hull damage to a lid-shut light tank in 10 s: grunt squad {off.grunts} (on: {on.grunts}), jeep {off.jeep} (on: {on.jeep})",
                toggle = $"PlayerPrefs oz_armour_chip = {(getInt("oz_armour_chip", 0) == 1 ? "1 (on)" : "0 (off, default)")}; menu OpenZ/Deviations/Armour Chip (proposal); off in 'Zod exact'",
                status = !ok ? DevCheck : pref ? DevActive : "OFF (proposal)",
            };
        }

        // ------------------------------------------------------------------ NB12 note to DV9
        static (int tankVsJeep, int jeepVsJeep, int tankVsTank) TankJeepProbe(bool noDuel)
        {
            bool nd = ZWorld.NoTankDuelVsJeep, dd = ZWorld.DuelDeviation; ZWorld.NoTankDuelVsJeep = noDuel; ZWorld.DuelDeviation = true;
            try
            {
                int Mans(string at, string tt)
                {
                    var w = ZWorld.Create(SettingsText, MapText("L02"), 1);
                    NbQuietTeam(w, Team.Blue); NbQuietTeam(w, Team.Red);
                    NbDummy(w, "robot", "grunt", 20 * 16, 60 * 16, Team.Blue);
                    var t = w.CreateObject("vehicle", tt, 52 * 16, 38 * 16, Team.Blue); t.Damage = 1; t.CanSnipe = false; t.MaxHealth = t.Health = 1000000000; t.MoveSpeed = 0; t.RealMoveSpeed = 0;
                    var a = w.CreateObject("vehicle", at, 46 * 16, 38 * 16, Team.Red);
                    w.SendWaypoints(Team.Red, a, new[] { new ZWaypoint { mode = WpMode.Attack, refId = t.Id, x = t.CenterX, y = t.CenterY } });
                    while (w.Time < 20) { w.Tick(ZConst.TICK); w.Events.Clear(); }
                    return a.DuelState?.Manoeuvres ?? 0;
                }
                return (Mans("heavy", "jeep"), Mans("jeep", "jeep"), Mans("heavy", "light"));
            }
            finally { ZWorld.NoTankDuelVsJeep = nd; ZWorld.DuelDeviation = dd; }
        }

        static Deviation DevDuelVsJeep(Func<string, int, int> getInt)
        {
            var on = TankJeepProbe(true); var off = TankJeepProbe(false);
            bool pref = getInt("oz_tank_duel_jeep", 0) == 0;
            bool ok = on.tankVsJeep == 0 && on.jeepVsJeep > 0 && on.tankVsTank > 0 && off.tankVsJeep > 0;
            return new Deviation
            {
                id = "DV9 (NB12)", change = "Tanks do not duel-manoeuvre against a jeep (back toward Zod inside DV9)",
                zod = "an ATTACK / AGRO waypoint whose target is in range stops and fires (zobject.cpp:2356-2362)",
                openz = "DV9 keeps its manoeuvres for jeeps against anything and for light / medium / heavy tanks against tanks, APCs, MLRS and cannons; a tank whose target is a jeep stops and fires like Zod",
                measured = $"20 s attack orders, DV9 on: heavy vs jeep {on.tankVsJeep} manoeuvres (before NB12: {off.tankVsJeep}), jeep vs jeep {on.jeepVsJeep}, heavy vs light tank {on.tankVsTank}",
                toggle = $"PlayerPrefs oz_tank_duel_jeep = {(getInt("oz_tank_duel_jeep", 0) == 1 ? "1 (tanks duel jeeps again)" : "0 (default)")}; menu OpenZ/Deviations/Tank Duels Against Jeeps; DV9 off = Zod",
                status = !ok ? DevCheck : pref ? DevActive : DevActive + " (off by pref)",
            };
        }

        /// <summary>combat_nb_v1: the rows above for CollectDeviations (each probe guarded like the others).</summary>
        static void AddCombatNbDeviations(List<Deviation> list, Func<string, int, int> getInt)
        {
            foreach (var (id, make) in new (string, Func<Func<string, int, int>, Deviation>)[] {
                         ("DV25", DevFortMortar), ("DV26", DevBuildingReach), ("DV27", DevExposedDrivers), ("DV28", DevApproach), ("DV29", DevArmourChip), ("DV9 (NB12)", DevDuelVsJeep) })
            {
                try { list.Add(make(getInt)); }
                catch (Exception ex) { list.Add(new Deviation { id = id, change = make.Method.Name, zod = "-", openz = "-", measured = "probe threw " + ex.GetType().Name + ": " + ex.Message, toggle = "-", status = DevCheck }); }
            }
        }

#if !OPENZ_HARNESS
        // ------------------------------------------------------------------ menus (PlayerPrefs, read by ZGame at every start)
        static void NbToggle(string key, int def, string what)
        {
            bool on = PlayerPrefs.GetInt(key, def) != 1;
            PlayerPrefs.SetInt(key, on ? 1 : 0); PlayerPrefs.Save();
            Debug.Log($"OpenZ {what} from the next Play: {(on ? "ON" : "OFF")} ({key} = {(on ? 1 : 0)})" + (PlayerPrefs.GetInt("oz_zod_exact", 0) == 1 ? " (still off while 'Zod exact' oz_zod_exact = 1)" : ""));
        }
        const string MenuMortar = "OpenZ/Deviations/Fort Mortar (oz_fort_mortar)", MenuReach = "OpenZ/Deviations/Building Reach (oz_building_reach)",
                     MenuExposed = "OpenZ/Deviations/Exposed Drivers (oz_exposed_drivers)", MenuApproach = "OpenZ/Deviations/Attack Approach (oz_attack_approach)",
                     MenuChip = "OpenZ/Deviations/Armour Chip - proposal (oz_armour_chip)", MenuTankJeep = "OpenZ/Deviations/Tank Duels Against Jeeps (oz_tank_duel_jeep)";
        [MenuItem(MenuMortar)] static void TMortar() => NbToggle("oz_fort_mortar", 1, "fort mortar (DV25)");
        [MenuItem(MenuMortar, true)] static bool TMortarC() { Menu.SetChecked(MenuMortar, PlayerPrefs.GetInt("oz_fort_mortar", 1) == 1); return true; }
        [MenuItem(MenuReach)] static void TReach() => NbToggle("oz_building_reach", 1, "building reach (DV26)");
        [MenuItem(MenuReach, true)] static bool TReachC() { Menu.SetChecked(MenuReach, PlayerPrefs.GetInt("oz_building_reach", 1) == 1); return true; }
        [MenuItem(MenuExposed)] static void TExposed() => NbToggle("oz_exposed_drivers", 1, "exposed drivers / rear snipes (DV27)");
        [MenuItem(MenuExposed, true)] static bool TExposedC() { Menu.SetChecked(MenuExposed, PlayerPrefs.GetInt("oz_exposed_drivers", 1) == 1); return true; }
        [MenuItem(MenuApproach)] static void TApproach() => NbToggle("oz_attack_approach", 1, "attack approach (DV28)");
        [MenuItem(MenuApproach, true)] static bool TApproachC() { Menu.SetChecked(MenuApproach, PlayerPrefs.GetInt("oz_attack_approach", 1) == 1); return true; }
        [MenuItem(MenuChip)] static void TChip() => NbToggle("oz_armour_chip", 0, "armour chip (DV29 proposal)");
        [MenuItem(MenuChip, true)] static bool TChipC() { Menu.SetChecked(MenuChip, PlayerPrefs.GetInt("oz_armour_chip", 0) == 1); return true; }
        [MenuItem(MenuTankJeep)] static void TTankJeep() => NbToggle("oz_tank_duel_jeep", 0, "tank duels against jeeps (pre-NB12 DV9)");
        [MenuItem(MenuTankJeep, true)] static bool TTankJeepC() { Menu.SetChecked(MenuTankJeep, PlayerPrefs.GetInt("oz_tank_duel_jeep", 0) == 1); return true; }
#endif
    }
}
