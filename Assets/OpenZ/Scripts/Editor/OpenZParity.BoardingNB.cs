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
    /// parity rows use (each probe once with the switch on and once off, every other switch as the build has it, restored afterwards).
    /// Compiles in the headless harness too (OPENZ_HARNESS: no UnityEditor). Menus OpenZ/Deviations/... (PlayerPrefs read by ZGame at
    /// every start; all four off in 'Zod exact' oz_zod_exact = 1). NB17 (capture after the climb-in) is view only, not a deviation.
    /// </summary>
    public static partial class OpenZParity
    {
        /// <summary>A static, harmless, unkillable unit per team in a map corner (no team is eliminated), every map robot removed (not
        /// frozen: a frozen neutral robot is a parked obstacle the real game never has), the given teams' cannons neutral, production off.</summary>
        static void BnClear(ZWorld w, params Team[] cannonTeams)
        {
            var keep = new List<ZObject>
            {
                NbDummy(w, "robot", "grunt", 16, 16, Team.Red),
                NbDummy(w, "robot", "grunt", (w.Map.width - 2) * 16, 16, Team.Blue),
            };
            foreach (var o in w.Objects.Where(o => o.IsUnit && !keep.Contains(o)).ToList())
            {
                if (o.Kind == ZKind.Cannon) { if (cannonTeams.Contains(o.Owner)) { o.Owner = Team.Neutral; o.Drivers.Clear(); o.Waypoints.Clear(); o.Disengage(); o.Damage = 0; } continue; }
                if (o.Kind == ZKind.Vehicle && o.Owner == Team.Neutral) continue;
                o.SetHealth(0); w.UpdateObjectHealth(o);
            }
            foreach (var b in w.Objects) if (b.IsProducer) b.StopBuildingProduction();
            w.Tick(ZConst.TICK); w.Events.Clear();
        }

        /// <summary>Nearest tile to (tx, ty) (rings up to 6) a robot / a vehicle footprint can stand on, in the given reach component.</summary>
        static bool BnSpot(ZWorld w, int tx, int ty, bool robot, out int x, out int y)
        {
            x = y = 0;
            for (int r = 0; r <= 6; r++)
                for (int j = ty - r; j <= ty + r; j++)
                    for (int i = tx - r; i <= tx + r; i++)
                    {
                        if (Math.Max(Math.Abs(i - tx), Math.Abs(j - ty)) != r) continue;
                        if (w.Pathing.Reach.Comp(i, j, robot, true) < 0) continue;
                        var c = w.Map.ClassAt(i, j); if (c == ZClass.Water || c == ZClass.DeepWater) continue;
                        x = i * 16; y = j * 16; return true;
                    }
            return false;
        }

        // ------------------------------------------------------------------ DV34 fort rush (NB15)
        /// <summary>Three blue squads (grunt, psycho, pyro) 10 tiles in front of the red fort's gate, one group ENTER_FORT order, the
        /// defence removed (movement only): seconds to the entry and robot-ticks held by the spacing layer within 96 px of the gate exit.
        /// L17 includes DV32's parked empty red medium tank, which the map parks on the red gate's exit point.</summary>
        static (double entry, long held) FortRushProbe(bool on, string lv)
        {
            return WithSwitch(() => ZWorld.FortRushDeviation, v => ZWorld.FortRushDeviation = v, on, () =>
            {
                bool fg = ZWorld.FortGunDeviation; ZWorld.FortGunDeviation = false;
                try
                {
                    var w = ZWorld.Create(SettingsText, MapText(lv), 1, lv == "L17");
                    BnClear(w, Team.Red, Team.Blue);
                    var fort = NbFort(w, Team.Red);
                    fort.GetCreationMovePoint(out int ex, out int ey);
                    int dir = fort.Type == "fort_front" ? 1 : -1;
                    var sq = new List<ZObject>();
                    var types = new[] { "grunt", "psycho", "pyro" };
                    for (int i = 0; i < 3; i++)
                    {
                        if (!BnSpot(w, (ex >> 4) - 2 + 2 * i, (ey >> 4) + dir * 10, true, out int x, out int y)) continue;
                        var l = w.CreateRobotGroup(types[i], x, y, Team.Blue);
                        sq.Add(l); sq.AddRange(l.Minions);
                    }
                    for (int k = 0; k < 150; k++) { w.Tick(ZConst.TICK); w.Events.Clear(); }
                    var wp = new ZWaypoint { mode = WpMode.EnterFort, refId = fort.Id, x = fort.CenterX, y = fort.CenterY };
                    foreach (var l in sq.Where(o => !o.IsMinion && !o.Destroyed).ToList()) w.SendWaypoints(Team.Blue, l, new[] { wp });
                    double t0 = w.Time, entry = -1; long held = 0;
                    while (w.Time - t0 < 90 && entry < 0)
                    {
                        w.Tick(ZConst.TICK);
                        foreach (var e in w.Events) if (e.type == ZEventType.FortEntered) entry = w.Time - t0;
                        w.Events.Clear();
                        foreach (var o in sq)
                        {
                            if (o.Destroyed || o.KillAt >= 0) continue;
                            if (Math.Abs(o.CenterX - ex) + Math.Abs(o.CenterY - ey) > 136) continue;
                            var s = o.PathWait; if (s == ZPathWait.Held || s == ZPathWait.Queue || s == ZPathWait.Sidestep || s == ZPathWait.Lane) held++;
                        }
                    }
                    return (entry, held);
                }
                finally { ZWorld.FortGunDeviation = fg; }
            });
        }

        static Deviation DevFortRush(Func<string, int, int> getInt)
        {
            var on02 = FortRushProbe(true, "L02"); var off02 = FortRushProbe(false, "L02");
            var on17 = FortRushProbe(true, "L17"); var off17 = FortRushProbe(false, "L17");
            bool pref = getInt("oz_fort_rush", 1) == 1 && getInt("oz_zod_exact", 0) == 0;
            bool ok = on02.entry >= 0 && on17.entry >= 0 && (off02.entry < 0 || on02.entry < off02.entry) && on02.held <= off02.held && on17.held <= off17.held;
            return new Deviation
            {
                id = "DV34", change = "Fort rush: robots near an enemy fort's door run and push through it",
                zod = "an ENTER_FORT robot walks to the gate exit point and then 80 px through the gate at walking speed (zobject.cpp:2774-2913); it never runs there, a CPU order still dodges every shell (DodgeMissile) and diverts into attack-move fights (attack_to); with OpenZ's spacing layer a squad converging on the one exit pixel queues and sidesteps in the doorway under the tower guns",
                openz = $"a robot on an ENTER_FORT order within {ZObject.FortRushRadius} px of the gate exit point, and inside the gate, runs at Zod's running speed (run_unit_speed, no stamina used), passes through other units (no spacing gate, no pushes: Zod has no unit collision), never dodges and never turns off to fight; the view shows it running",
                measured = $"3 blue squads (grunt, psycho, pyro) 10 tiles off the red gate, one group order, defence removed: L02 entry at {NbS(on02.entry)}, {on02.held} robot-ticks held at the door (off: {NbS(off02.entry)}, {off02.held}); " +
                           $"L17 (DV32 parks the empty red medium on the gate's exit point) {NbS(on17.entry)}, {on17.held} held (off: {NbS(off17.entry)}, {off17.held})",
                toggle = $"PlayerPrefs oz_fort_rush = {(getInt("oz_fort_rush", 1) == 1 ? "1 (on, default)" : "0 (off)")}; menu OpenZ/Deviations/Fort Rush; off in 'Zod exact' and in the parity report's rows",
                status = !ok ? DevCheck : pref ? DevActive : DevActive + " (off by pref)",
            };
        }

        // ------------------------------------------------------------------ DV35 nearest robot boards (NB16)
        /// <summary>L02, a red grunt squad settled in open ground; an empty jeep is parked 6 px beyond the minion farthest from the
        /// leader; the squad is ordered to enter it: seconds to the boarding and which member got in (0 = the leader).</summary>
        static (double t, int member) BoardNearestProbe(bool on)
        {
            return WithSwitch(() => ZWorld.BoardNearestDeviation, v => ZWorld.BoardNearestDeviation = v, on, () =>
            {
                bool fg = ZWorld.FortGunDeviation; ZWorld.FortGunDeviation = false;
                try
                {
                    var w = ZWorld.Create(SettingsText, MapText("L02"), 3);
                    BnClear(w, Team.Red, Team.Blue);
                    BnSpot(w, 30, 56, true, out int x, out int y);
                    var l = w.CreateRobotGroup("grunt", x, y, Team.Red);
                    for (int k = 0; k < 200; k++) { w.Tick(ZConst.TICK); w.Events.Clear(); }
                    var far = l.Minions.OrderByDescending(m => (m.CenterX - l.CenterX) * (m.CenterX - l.CenterX) + (m.CenterY - l.CenterY) * (m.CenterY - l.CenterY)).ThenBy(m => m.Id).First();
                    double dx = far.CenterX - l.CenterX, dy = far.CenterY - l.CenterY, d = Math.Max(1, Math.Sqrt(dx * dx + dy * dy));
                    int vx = (int)Math.Round(far.CenterX + dx / d * 22) - 16, vy = (int)Math.Round(far.CenterY + dy / d * 22) - 16;
                    var jeep = w.CreateObject("vehicle", "jeep", vx, vy, Team.Neutral);
                    w.SendWaypoints(Team.Red, l, new[] { new ZWaypoint { mode = WpMode.Enter, refId = jeep.Id, x = jeep.CenterX, y = jeep.CenterY } });
                    double t0 = w.Time;
                    while (w.Time - t0 < 30)
                    {
                        w.Tick(ZConst.TICK);
                        foreach (var e in w.Events)
                            if (e.type == ZEventType.Entered && e.otherId == jeep.Id) { var r = w.GetAny(e.objId); return (w.Time - t0, r == null ? -1 : r.MemberIndex); }
                        w.Events.Clear();
                    }
                    return (-1.0, -1);
                }
                finally { ZWorld.FortGunDeviation = fg; }
            });
        }

        static Deviation DevBoardNearest(Func<string, int, int> getInt)
        {
            var on = BoardNearestProbe(true); var off = BoardNearestProbe(false);
            bool pref = getInt("oz_board_nearest", 1) == 1 && getInt("oz_zod_exact", 0) == 0;
            bool ok = on.t >= 0 && off.t >= 0 && on.member > 0 && off.member == 0 && on.t < off.t;
            return new Deviation
            {
                id = "DV35", change = "Boarding: the squad member that reaches the empty vehicle first gets in",
                zod = "only the squad leader can enter (zobject.cpp:2584-2667, `if(!IsMinion())`): minions that reach the vehicle first stop on it while the leader walks round them to its centre",
                openz = "the first member of a human player's squad whose centre is on the empty vehicle / cannon enters it (the same RobotEnterObject, zserver.cpp:1375-1421); the rest stays a squad; an APC keeps Zod's rule (the whole squad boards); CPU squads keep Zod's leader-only rule (with it the Zod bot got its boarding squads back idle at once and L02 bot vs bot moved from 54 % to 61 % red over 360 seeds)",
                measured = $"L02, a grunt squad ordered onto an empty jeep parked beside its farthest minion: boarded after {NbS(on.t)} by member {on.member} (off: {NbS(off.t)}, member {off.member} = the leader)",
                toggle = $"PlayerPrefs oz_board_nearest = {(getInt("oz_board_nearest", 1) == 1 ? "1 (on, default)" : "0 (off)")}; menu OpenZ/Deviations/Nearest Robot Boards; off in 'Zod exact' and in the parity report's rows",
                status = !ok ? DevCheck : pref ? DevActive : DevActive + " (off by pref)",
            };
        }

        // ------------------------------------------------------------------ DV36 parked-aware paths (NB18)
        /// <summary>(a) L02 seed 40: a red light tank from tile (42,84) to (22,20); Zod's path runs through the pass by the neutral gatling
        /// (14,24): arrival (s) and the distance left when the order ended. (b) L02: a lone red jeep over the east bridge (51,58) southward
        /// with an empty jeep parked in the far approach's keep-clear area: seconds held at the deck. (c) L02: the west bridge (17,26)
        /// destroyed, a light tank ordered from its north approach to the south one, the bridge back at 8 s: arrival (s).</summary>
        static (double aArr, double aLeft, double bHeld, double cArr) BnParkedProbe(bool on)
        {
            return WithSwitch(() => ZWorld.ParkedPathDeviation, v => ZWorld.ParkedPathDeviation = v, on, () =>
            {
                bool fg = ZWorld.FortGunDeviation; ZWorld.FortGunDeviation = false;
                try
                {
                    // (a)
                    var w = ZWorld.Create(SettingsText, MapText("L02"), 40);
                    BnClear(w, Team.Red, Team.Blue);
                    var v = w.CreateObject("vehicle", "light", 42 * 16, 84 * 16, Team.Red);
                    int gx = 22 * 16 + 16, gy = 20 * 16 + 16;
                    w.SendWaypoints(Team.Red, v, new[] { ZWaypoint.Move(gx, gy) });
                    double aArr = -1, t0 = w.Time;
                    while (w.Time - t0 < 200 && aArr < 0) { w.Tick(ZConst.TICK); w.Events.Clear(); if (v.Waypoints.Count == 0 || Math.Abs(v.CenterX - gx) + Math.Abs(v.CenterY - gy) <= 24) aArr = w.Time - t0; }
                    double aLeft = Math.Sqrt(Math.Pow(v.CenterX - gx, 2) + Math.Pow(v.CenterY - gy, 2));
                    if (aLeft > 48) aArr = -1;
                    // (b)
                    var w2 = ZWorld.Create(SettingsText, MapText("L02"), 3);
                    BnClear(w2, Team.Red, Team.Blue);
                    var br = w2.Objects.First(o => o.IsBridge && (o.LocX >> 4) == 51 && (o.LocY >> 4) == 58);
                    int axis = br.LocX + 32;
                    w2.CreateObject("vehicle", "jeep", axis + 30 - 16, br.LocY + br.HeightPix + 40 - 16, Team.Neutral);   // parked in the far keep-clear area, beside the axis
                    var j = w2.CreateObject("vehicle", "jeep", axis - 16, br.LocY - 200 - 16, Team.Red);
                    w2.SendWaypoints(Team.Red, j, new[] { ZWaypoint.Move(axis - 32, br.LocY + br.HeightPix + 160) });
                    double bHeld = 0; t0 = w2.Time;
                    while (w2.Time - t0 < 90 && j.Waypoints.Count > 0) { w2.Tick(ZConst.TICK); w2.Events.Clear(); if (w2.Pathing.BridgeQueuedAt(j) != null || j.PathWait == ZPathWait.Bridge) bHeld += ZConst.TICK; }
                    // (c)
                    var w3 = ZWorld.Create(SettingsText, MapText("L02"), 4);
                    BnClear(w3, Team.Red, Team.Blue);
                    var wb = w3.Objects.First(o => o.IsBridge && (o.LocX >> 4) == 17 && (o.LocY >> 4) == 26);
                    wb.SetHealth(0); w3.UpdateObjectHealth(wb);
                    w3.Tick(ZConst.TICK); w3.Events.Clear();
                    BnSpot(w3, 17, 19, false, out int lx, out int ly); BnSpot(w3, 17, 38, false, out int cx, out int cy); cx += 16; cy += 16;
                    var lt = w3.CreateObject("vehicle", "light", lx, ly, Team.Red);
                    w3.SendWaypoints(Team.Red, lt, new[] { ZWaypoint.Move(cx, cy) });
                    double cArr = -1; t0 = w3.Time; bool back = false;
                    while (w3.Time - t0 < 240 && cArr < 0)
                    {
                        w3.Tick(ZConst.TICK); w3.Events.Clear();
                        if (!back && w3.Time - t0 >= 8) { back = true; wb.SetHealth(wb.MaxHealth); wb.HasProcessedDeath = false; wb.DoAutoRepair = false; }
                        if (Math.Abs(lt.CenterX - cx) + Math.Abs(lt.CenterY - cy) <= 24) cArr = w3.Time - t0;
                    }
                    return (aArr, aLeft, bHeld, cArr);
                }
                finally { ZWorld.FortGunDeviation = fg; }
            });
        }

        static Deviation DevParkedPaths(Func<string, int, int> getInt)
        {
            var on = BnParkedProbe(true); var off = BnParkedProbe(false);
            bool pref = getInt("oz_parked_paths", 1) == 1 && getInt("oz_zod_exact", 0) == 0;
            bool ok = on.aArr >= 0 && on.bHeld <= 0.5 && on.cArr >= 0 && (off.cArr < 0 || on.cArr < off.cArr);
            return new Deviation
            {
                id = "DV36", change = "Vehicle paths plan round parked vehicles and cannons, re-plan when a bridge comes back or goes down",
                zod = "Zod's A* sees terrain only (zpath_finding_astar.cpp) and a path is fixed when the order is given; with OpenZ's spacing layer a vehicle stops at a parked empty vehicle / cannon on its path, sidesteps and detours round it every few seconds, and gives up in a pass it blocks; DV8's box-junction rule held a lone vehicle up to 6 s for a parked unit to clear the far approach (it never does)",
                openz = "a vehicle whose Zod path or straight line passes within the spacing of a unit that never moves (empty vehicle, cannon, unit without speed) gets a time-weighted A* on its own graph with those footprints closed (Zod's path when there is no way round); the stuck guard's detour tries the same search over the whole map before any give-up; a bridge that comes back (crane / auto repair) re-plans every vehicle on its way somewhere, a destroyed one the vehicles whose path crosses it; parked units no longer block DV8's exit rule",
                measured = $"L02 light tank past the neutral gatling's pass: arrived {NbS(on.aArr)} (off: {(off.aArr < 0 ? $"gave up {off.aLeft:0} px short" : NbS(off.aArr))}); lone jeep over the east bridge with an empty jeep in the far approach: held {on.bHeld:0.0} s (off {off.bHeld:0.0} s); " +
                           $"light tank ordered over the destroyed west bridge, bridge back at 8 s: arrived {NbS(on.cArr)} (off {NbS(off.cArr)})",
                toggle = $"PlayerPrefs oz_parked_paths = {(getInt("oz_parked_paths", 1) == 1 ? "1 (on, default)" : "0 (off)")}; menu OpenZ/Deviations/Parked-Aware Paths; off in 'Zod exact' and in the parity report's rows",
                status = !ok ? DevCheck : pref ? DevActive : DevActive + " (off by pref)",
            };
        }

        // ------------------------------------------------------------------ DV37 car park drive (NB20)
        /// <summary>L17, the blue CPU (Normal) against an idle red human, 3 seeds x 300 s: empty vehicles blue boarded by 150 / 300 s
        /// (car park = the 20 empty vehicles below row 88) and the CPU's own count of car-park-pass orders.</summary>
        static (double at150, double at300, double park300, int orders) CarParkProbe(bool on)
        {
            return WithSwitch(() => ZWorld.CarParkBotDeviation, v => ZWorld.CarParkBotDeviation = v, on, () =>
            {
                double a = 0, b = 0, c = 0; int ord = 0;
                for (int seed = 1; seed <= 3; seed++)
                {
                    var w = ZWorld.Create(SettingsText, MapText("L17"), seed, true);
                    var bots = ZBot.Attach(w, Team.Red, false, seed);
                    var park = new HashSet<int>(w.Objects.Where(o => o.Kind == ZKind.Vehicle && o.Owner == Team.Neutral && o.LocY >= 88 * 16).Select(o => o.Id));
                    while (w.Time < 300 && !w.GameOver)
                    {
                        w.Tick(ZConst.TICK);
                        foreach (var e in w.Events)
                        {
                            if (e.type != ZEventType.Entered) continue;
                            var r = w.GetAny(e.objId); if (r == null || r.Owner != Team.Blue) continue;
                            if (w.Time <= 150) a++;
                            b++;
                            if (park.Contains(e.otherId)) c++;
                        }
                        w.Events.Clear();
                    }
                    foreach (var bt in bots) ord += bt.CarParkOrders;
                }
                return (a / 3, b / 3, c / 3, ord);
            });
        }

        static Deviation DevCarPark(Func<string, int, int> getInt)
        {
            bool elbBot = ZWorld.ElectrobotBotDeviation; ZWorld.ElectrobotBotDeviation = false;   // electrobot_v1: DV37 against Zod's production (DV46 off)
            (double at150, double at300, double park300, int orders) on, off;
            try { on = CarParkProbe(true); off = CarParkProbe(false); } finally { ZWorld.ElectrobotBotDeviation = elbBot; }
            bool pref = getInt("oz_carpark_bot", 1) == 1 && getInt("oz_zod_exact", 0) == 0;
            bool ok = on.orders > 0 && off.orders == 0 && on.at300 >= off.at300;
            return new Deviation
            {
                id = "DV37", change = "L17 CPU car park drive: the standard CPU sends idle robot squads to the empty vehicles first",
                zod = "every order round (4-12 s) the Zod bot sends a random 15-35 % (+1) of its idle units to their mutually nearest targets among flags, grenade boxes, the enemy fort, empty vehicles and cannons (zbot.cpp:157-414): on L17 the car park's empty vehicles wait minutes",
                openz = $"on L17, Normal CPU only (Easy keeps its handicap): before Zod's random wave, up to {ZBot.CarParkPerRound} idle robot squads per round (nearest to an empty vehicle first) are paired with the empty vehicles no unit of the bot is after (Zod's region check and mutual-nearest pairing) while fewer than {ZBot.CarParkMaxEnRoute} squads are on their way to one; then Zod's round; the bot's knowledge is unchanged",
                measured = $"L17 blue CPU vs an idle red, 3 seeds: empty vehicles boarded by blue {on.at150:0.0} by 150 s, {on.at300:0.0} by 300 s ({on.park300:0.0} of the car park) (off: {off.at150:0.0}, {off.at300:0.0}, {off.park300:0.0}); car-park-pass orders {on.orders}",
                toggle = $"PlayerPrefs oz_carpark_bot = {(getInt("oz_carpark_bot", 1) == 1 ? "1 (on, default)" : "0 (off)")}; menu OpenZ/Deviations/L17 CPU Car Park Drive; off in 'Zod exact' and in the parity report's rows; bot-vs-bot balance: see boarding_nb_v1 README",
                status = !ok ? DevCheck : pref ? DevActive : DevActive + " (off by pref)",
            };
        }

        static void AddBoardingNbDeviations(List<Deviation> list, Func<string, int, int> getInt)
        {
            foreach (var (id, make) in new (string, Func<Func<string, int, int>, Deviation>)[] {
                         ("DV34", DevFortRush), ("DV35", DevBoardNearest), ("DV36", DevParkedPaths), ("DV37", DevCarPark) })
            {
                try { list.Add(make(getInt)); }
                catch (Exception ex) { list.Add(new Deviation { id = id, change = make.Method.Name, zod = "-", openz = "-", measured = "probe threw " + ex.GetType().Name + ": " + ex.Message, toggle = "-", status = DevCheck }); }
            }
        }

#if !OPENZ_HARNESS
        const string MenuRush = "OpenZ/Deviations/Fort Rush (oz_fort_rush)", MenuBoard = "OpenZ/Deviations/Nearest Robot Boards (oz_board_nearest)",
                     MenuParked = "OpenZ/Deviations/Parked-Aware Paths (oz_parked_paths)", MenuCarPark = "OpenZ/Deviations/L17 CPU Car Park Drive (oz_carpark_bot)",
                     MenuCapture = "OpenZ/View/Capture After The Climb-In (oz_capture_after_board)";
        [MenuItem(MenuRush)] static void TRush() => NbToggle("oz_fort_rush", 1, "fort rush (DV34)");
        [MenuItem(MenuRush, true)] static bool TRushC() { Menu.SetChecked(MenuRush, PlayerPrefs.GetInt("oz_fort_rush", 1) == 1); return true; }
        [MenuItem(MenuBoard)] static void TBoard() => NbToggle("oz_board_nearest", 1, "nearest robot boards (DV35)");
        [MenuItem(MenuBoard, true)] static bool TBoardC() { Menu.SetChecked(MenuBoard, PlayerPrefs.GetInt("oz_board_nearest", 1) == 1); return true; }
        [MenuItem(MenuParked)] static void TParked() => NbToggle("oz_parked_paths", 1, "parked-aware paths (DV36)");
        [MenuItem(MenuParked, true)] static bool TParkedC() { Menu.SetChecked(MenuParked, PlayerPrefs.GetInt("oz_parked_paths", 1) == 1); return true; }
        [MenuItem(MenuCarPark)] static void TCarPark() => NbToggle("oz_carpark_bot", 1, "L17 CPU car park drive (DV37)");
        [MenuItem(MenuCarPark, true)] static bool TCarParkC() { Menu.SetChecked(MenuCarPark, PlayerPrefs.GetInt("oz_carpark_bot", 1) == 1); return true; }
        [MenuItem(MenuCapture)] static void TCapture() => NbToggle("oz_capture_after_board", 1, "capture after the climb-in (NB17, view only)");
        [MenuItem(MenuCapture, true)] static bool TCaptureC() { Menu.SetChecked(MenuCapture, PlayerPrefs.GetInt("oz_capture_after_board", 1) == 1); return true; }

        [MenuItem("OpenZ/Deviations/Probe DV34-DV37 (boarding_nb_v1)")]
        static void ProbeBoardingNbMenu()
        {
            var l = new List<Deviation>(); AddBoardingNbDeviations(l, (k, d) => PlayerPrefs.GetInt(k, d));
            foreach (var d in l) Debug.Log($"[{d.id}] {d.status}: {d.measured}");
        }
#endif
    }
}
