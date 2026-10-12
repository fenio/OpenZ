using System;
using System.Collections.Generic;
using OpenZ.Sim;
#if !OPENZ_HARNESS
using UnityEditor;
using UnityEngine;
#endif

namespace OpenZ.EditorTools
{
    /// <summary>
    /// The Deviations row DV81 "Ctrl to attack an empty vehicle or gun" (Sim/ZWorld.EmptyTargets.cs), counted headless in the same
    /// sim as the parity rows. Every case runs in its own world on the L02 test map without the map's units: the empty target at
    /// tile 62,14 and one red unit (the player's) at tile 57,14, their centres 80 to 88 px apart, inside every weapon's range.
    /// Menu: OpenZ/Deviations/Probe DV81 Ctrl Attack Empty.
    /// </summary>
    public static partial class OpenZParity
    {
        static void AddCtrlAttackEmptyDeviation(List<Deviation> list, Func<string, int, int> getInt)
        {
            try { list.Add(DevCtrlAttackEmpty(getInt)); }
            catch (Exception ex) { list.Add(new Deviation { id = "DV81", change = "DevCtrlAttackEmpty", zod = "-", openz = "-", measured = "probe threw " + ex.GetType().Name + ": " + ex.Message, toggle = "-", status = DevCheck }); }
        }

        /// <summary>The armed unit types that click (7 robot squads, 5 vehicles, 4 guns) and the empty types they click on, each in turn.</summary>
        static readonly string[] CaeUnits = { "grunt", "psycho", "sniper", "tough", "pyro", "laser", "electrobot", "jeep", "light", "medium", "heavy", "missile_launcher", "gatling", "gun", "howitzer", "missile_cannon" };
        static readonly string[] CaeTargets = { "jeep", "gatling", "light", "gun", "medium", "howitzer", "heavy", "missile_cannon", "apc", "missile_launcher", "crane" };
        const int CaeN = 20;

        static bool CaeIsGun(string t) => t == "gatling" || t == "gun" || t == "howitzer" || t == "missile_cannon";
        static bool CaeIsRobot(string t) => t == "grunt" || t == "psycho" || t == "sniper" || t == "tough" || t == "pyro" || t == "laser" || t == "electrobot";

        static ZWorld CaeWorld(int seed, string unitType, string targetType, out ZObject u, out ZObject h)
        {
            var w = Legacy(seed);
            h = w.CreateObject(CaeIsGun(targetType) ? "cannon" : "vehicle", targetType, 62 * 16, 14 * 16, Team.Neutral);
            if (CaeIsRobot(unitType)) u = w.CreateRobotGroup(unitType, 57 * 16, 14 * 16, Team.Red);
            else u = w.CreateObject(CaeIsGun(unitType) ? "cannon" : "vehicle", unitType, 57 * 16, 14 * 16, Team.Red);
            return w;
        }

        /// <summary>The click's base waypoint as the input builds it: a move to the clicked point that refers to the object under the pointer.</summary>
        static ZWaypoint CaeClick(ZObject h) => ZWaypoint.Move(h.CenterX, h.CenterY, true, h.Id);

        /// <summary>Shots at h among this tick's events: a bullet, a missile or grenade, an Electrobot's windup or bolt.</summary>
        static int CaeShotsAt(ZWorld w, ZObject h)
        {
            int n = 0;
            foreach (var e in w.Events)
                if (e.otherId == h.Id && (e.type == ZEventType.Hitscan || e.type == ZEventType.MissileFired || e.type == ZEventType.ElectroCharge || e.type == ZEventType.ElectroBolt)) n++;
            return n;
        }

        /// <summary>Red units that aim at h right now: it is their target, or their first waypoint is an attack on it.</summary>
        static int CaeAimedAt(ZWorld w, ZObject h)
        {
            int n = 0;
            foreach (var o in w.Objects)
            {
                if (o.Owner != Team.Red) continue;
                bool wp = o.Waypoints.Count > 0 && (o.Waypoints[0].mode == WpMode.Attack || o.Waypoints[0].mode == WpMode.Agro) && o.Waypoints[0].refId == h.Id;
                if (o.AttackObject == h || wp) n++;
            }
            return n;
        }

        static double CaeDist(ZObject a, ZObject b) =>
            Math.Sqrt((double)(a.CenterX - b.CenterX) * (a.CenterX - b.CenterX) + (double)(a.CenterY - b.CenterY) * (a.CenterY - b.CenterY));

        struct CaePlain { public bool robot, gun, attackBuilt, plainMove, refused, entered, harmless, stayed; }

        /// <summary>One right-click without Ctrl: the order the rule builds (a robot: the unchanged ENTER row of the order table), sent
        /// through SEND_WAYPOINTS and run (a robot: until it is inside, 30 s at most; a vehicle or gun: 15 s).</summary>
        static CaePlain CaePlainClick(int i)
        {
            string ut = CaeUnits[i % CaeUnits.Length], tt = CaeTargets[i % CaeTargets.Length];
            var w = CaeWorld(900 + i, ut, tt, out var u, out var h);
            var r = new CaePlain { robot = CaeIsRobot(ut), gun = CaeIsGun(ut) };
            var wp = CaeClick(h);
            bool handled = ZWorld.EmptyClickWaypoint(u, h, false, ref wp);
            if (!handled && r.robot) { wp.mode = WpMode.Enter; wp.attackTo = false; wp.x = h.CenterX; wp.y = h.CenterY; }   // the robot row, unchanged (zplayer.cpp:2837-2843)
            r.attackBuilt = wp.mode == WpMode.Attack;
            r.plainMove = handled && wp.mode == WpMode.Move && wp.refId == -1;
            double d0 = CaeDist(u, h);
            w.SendWaypoints(Team.Red, u, new[] { wp });
            r.refused = u.Waypoints.Count == 0;
            int shots = 0, aimed = 0;
            for (int k = 0; k < 3000; k++)
            {
                w.Tick(ZConst.TICK);
                shots += CaeShotsAt(w, h); aimed += CaeAimedAt(w, h);
                w.Events.Clear();
                if (r.robot && h.Owner == Team.Red) break;
                if (!r.robot && k >= 1500) break;
            }
            r.entered = h.Owner == Team.Red && h.Drivers.Count > 0;
            r.harmless = shots == 0 && aimed == 0 && h.Health == h.MaxHealth;
            r.stayed = CaeDist(u, h) <= d0 + 16;
            return r;
        }

        struct CaeStand { public int shots, aimedTicks, hurt; public bool built, accepted, damaged; }

        /// <summary>One unit left alone for 60 s in range of the empty target with no enemy on the map (the target is kept empty: no auto
        /// entry, the DV58 lock), then the same unit ordered onto it with Ctrl (30 s at most to its first damage).</summary>
        static CaeStand CaeStandThenCtrl(int i)
        {
            string ut = CaeUnits[i % CaeUnits.Length], tt = CaeTargets[i % CaeTargets.Length];
            var w = CaeWorld(800 + i, ut, tt, out var u, out var h);
            h.NoAutoEnterUntilTick = long.MaxValue;
            var r = new CaeStand();
            for (int k = 0; k < 6000; k++)
            {
                w.Tick(ZConst.TICK);
                r.shots += CaeShotsAt(w, h);
                if (CaeAimedAt(w, h) > 0) r.aimedTicks++;
                w.Events.Clear();
            }
            r.hurt = h.MaxHealth - h.Health;
            var wp = CaeClick(h);
            r.built = ZWorld.EmptyClickWaypoint(u, h, true, ref wp) && wp.mode == WpMode.Attack && wp.refId == h.Id;
            w.SendWaypoints(Team.Red, u, new[] { wp });
            r.accepted = u.Waypoints.Count == 1 && u.Waypoints[0].mode == WpMode.Attack && u.Waypoints[0].refId == h.Id;
            for (int k = 0; k < 3000 && h.Health >= h.MaxHealth; k++) { w.Tick(ZConst.TICK); w.Events.Clear(); }
            r.damaged = h.Health < h.MaxHealth;
            return r;
        }

        /// <summary>A red light tank ordered with Ctrl onto an empty heavy tank (made unbreakable); after its first hit a red grunt is
        /// ordered in. Returns: 1 = hit, then the grunt got in; shots at it after that tick; health it lost after that tick; 1 = the
        /// light tank still has an order or a target 10 s later.</summary>
        static int[] CaeOwnCrewStops()
        {
            var w = CaeWorld(880, "light", "heavy", out var u, out var h);
            h.MaxHealth = h.Health = 1000000000;
            h.NoAutoEnterUntilTick = long.MaxValue;
            var wp = CaeClick(h);
            ZWorld.EmptyClickWaypoint(u, h, true, ref wp);
            w.SendWaypoints(Team.Red, u, new[] { wp });
            for (int k = 0; k < 3000 && h.Health >= h.MaxHealth; k++) { w.Tick(ZConst.TICK); w.Events.Clear(); }
            bool hit = h.Health < h.MaxHealth;
            var g = w.CreateRobotGroup("grunt", h.LocX - 16, h.LocY, Team.Red, 1);
            w.SendWaypoints(Team.Red, g, new[] { new ZWaypoint { mode = WpMode.Enter, refId = h.Id, x = h.CenterX, y = h.CenterY } });
            for (int k = 0; k < 3000 && h.Owner != Team.Red; k++) { w.Tick(ZConst.TICK); w.Events.Clear(); }
            bool inside = h.Owner == Team.Red;
            int hp = h.Health, shots = 0;
            for (int k = 0; k < 1000; k++) { w.Tick(ZConst.TICK); shots += CaeShotsAt(w, h); w.Events.Clear(); }
            bool still = u.AttackObject == h || u.Waypoints.Count > 0;
            return new[] { hit && inside ? 1 : 0, shots, hp - h.Health, still ? 1 : 0 };
        }

        /// <summary>The waiting order. A red sniper squad gets the same attack order twice (the right-click on a manned blue jeep,
        /// given again with Shift) and shoots the jeep's driver out through the game's own rules. Zod drops the first waypoint of
        /// every attacker only (zserver.cpp:1849-1870): the second one, given while the jeep was manned, would go on as an attack on
        /// a jeep that is now empty. The jeep never shoots, never dies and is kept empty afterwards (the DV58 lock).
        /// Returns: 1 = the driver was shot out while the second order was still waiting; attack orders on the jeep left in the red
        /// lists right after; shots at the empty jeep in the next 30 s; ticks a red unit aimed at it; health it lost.</summary>
        static int[] CaeStaleOrder(bool ruleOn)
        {
            bool was = ZWorld.CtrlAttackEmptyDeviation;
            ZWorld.CtrlAttackEmptyDeviation = ruleOn;
            try
            {
                var w = Legacy(883);
                var h = Dummy(w, "vehicle", "jeep", 62, 14, Team.Blue);
                h.NoAutoEnterUntilTick = long.MaxValue;
                var s = w.CreateRobotGroup("sniper", 57 * 16, 14 * 16, Team.Red);
                var a = new ZWaypoint { mode = WpMode.Attack, refId = h.Id, x = h.CenterX, y = h.CenterY };
                w.SendWaypoints(Team.Red, s, new[] { a, a });
                bool waiting = false;
                for (int k = 0; k < 12000 && h.Owner == Team.Blue; k++) { waiting = s.Waypoints.Count == 2; w.Tick(ZConst.TICK); w.Events.Clear(); }
                bool reached = waiting && h.Owner == Team.Neutral;
                int left = 0;
                foreach (var o in w.Objects)
                {
                    if (o.Owner != Team.Red) continue;
                    foreach (var q in o.Waypoints)
                        if ((q.mode == WpMode.Attack || q.mode == WpMode.Agro) && q.refId == h.Id) left++;
                }
                int hp = h.Health, shots = 0, aimed = 0;
                for (int k = 0; k < 3000; k++)
                {
                    w.Tick(ZConst.TICK);
                    shots += CaeShotsAt(w, h);
                    if (CaeAimedAt(w, h) > 0) aimed++;
                    w.Events.Clear();
                }
                return new[] { reached ? 1 : 0, left, shots, aimed, hp - h.Health };
            }
            finally { ZWorld.CtrlAttackEmptyDeviation = was; }
        }

        /// <summary>The cursor over an empty vehicle for every kind of selection. Returns: kinds of selection; "attack" answers without
        /// Ctrl; answers that differ from the table (Ctrl: attack, unarmed nono; no Ctrl: enter with a robot, else move, guns only cannon).</summary>
        static int[] CaeCursorTable()
        {
            var w = Legacy(881);
            var h = w.CreateObject("vehicle", "jeep", 62 * 16, 14 * 16, Team.Neutral);
            int cases = 0, plainAttack = 0, wrong = 0;
            for (int m = 0; m < 16; m++)
            {
                bool ctrl = (m & 1) != 0, robots = (m & 2) != 0, canMove = (m & 4) != 0, canAttack = (m & 8) != 0;
                if (robots && !canMove) continue;                                                     // a robot can always move
                cases++;
                string c = ZWorld.EmptyClickCursor(h, ctrl, robots, canMove, canAttack, false);
                string want = ctrl ? (canAttack ? "attack" : "nono") : robots ? "enter" : canMove ? "move" : "cannon";
                if (!ctrl && c == "attack") plainAttack++;
                if (c != want) wrong++;
            }
            return new[] { cases, plainAttack, wrong };
        }

        static Deviation DevCtrlAttackEmpty(Func<string, int, int> getInt)
        {
            bool was = ZWorld.CtrlAttackEmptyDeviation, lockWas = ZWorld.EjectLockDeviation, freeze = ZWorld.EndFreezeDeviation, calm = ZWorld.CalmStartDeviation;
            string got = "-"; bool ok = false;
            try
            {
                ZWorld.CtrlAttackEmptyDeviation = true;
                ZWorld.EjectLockDeviation = true;                                                     // the count keeps its targets empty with the DV58 lock
                ZWorld.EndFreezeDeviation = false;                                                    // one team only on the map: the match must not count as over
                ZWorld.CalmStartDeviation = false;                                                    // the whole 60 s count, not 40 s of it
                int n = CaeN, robots = 0, vehicles = 0, guns = 0, plainAttack = 0, entered = 0, moved = 0, refused = 0, harmed = 0;
                for (int i = 0; i < n; i++)
                {
                    var p = CaePlainClick(i);
                    if (p.attackBuilt) plainAttack++;
                    if (!p.harmless) harmed++;
                    if (p.robot) { robots++; if (p.entered) entered++; }
                    else if (p.gun) { guns++; if (p.plainMove && p.refused) refused++; }
                    else { vehicles++; if (p.plainMove && !p.refused && p.stayed) moved++; }
                }
                int shots = 0, aimed = 0, hurt = 0, built = 0, accepted = 0, damaged = 0;
                for (int i = 0; i < n; i++)
                {
                    var s = CaeStandThenCtrl(i);
                    shots += s.shots; aimed += s.aimedTicks;
                    if (s.hurt != 0) hurt++;
                    if (s.built) built++;
                    if (s.accepted) accepted++;
                    if (s.damaged) damaged++;
                }
                var crew = CaeOwnCrewStops();
                var stale = CaeStaleOrder(true);
                var staleOff = CaeStaleOrder(false);                                                  // the same case as Zod has it: shows what the rule removes
                var table = CaeCursorTable();
                // a selection that cannot attack (a crane): Ctrl builds the attack order, the rules refuse it (the cursor says so: nono)
                var wc = CaeWorld(882, "crane", "gun", out var crane, out var cg);
                var cw = CaeClick(cg);
                bool craneBuilt = ZWorld.EmptyClickWaypoint(crane, cg, true, ref cw) && cw.mode == WpMode.Attack;
                wc.SendWaypoints(Team.Red, crane, new[] { cw });
                bool craneOk = craneBuilt && (crane.CanAttack() ? crane.Waypoints.Count == 1 : crane.Waypoints.Count == 0);
                // the switch off: the rule steps aside for every unit and for the cursor (Zod's table applies)
                ZWorld.CtrlAttackEmptyDeviation = false;
                var t1 = CaeClick(cg); var t2 = CaeClick(cg);
                bool off = !ZWorld.EmptyClickWaypoint(crane, cg, false, ref t1) && !ZWorld.EmptyClickWaypoint(crane, cg, true, ref t2)
                           && t1.mode == WpMode.Move && t1.refId == cg.Id && t2.mode == WpMode.Move && ZWorld.EmptyClickCursor(cg, true, true, true, true, true) == null;
                ZWorld.CtrlAttackEmptyDeviation = true;
                got = $"L02 test map, one red unit 5 tiles from an empty vehicle or gun, every case in its own world ({CaeUnits.Length} armed types in turn: 7 robot squads, jeep, light, medium and heavy tank, missile launcher, 4 guns; {CaeTargets.Length} empty types in turn). " +
                      $"{n} right-clicks without Ctrl: {plainAttack} attack orders; {entered} of {robots} robot squads got in; {moved} of {vehicles} vehicles took a plain move and stayed by it; {refused} of {guns} guns took no order; the empty target was shot at, aimed at or hurt in {harmed} of {n}. " +
                      $"{n} units left alone for 60 s in range, no enemy on the map: {shots} shots at the empty target, {aimed} ticks aimed at it, {hurt} targets hurt. " +
                      $"The same {n} units ordered with Ctrl: {built} attack orders, {accepted} accepted by the rules, {damaged} targets damaged within 30 s. " +
                      $"A light tank ordered with Ctrl onto an empty heavy tank, then a grunt of its team is sent in: {(crew[0] == 1 ? "hit, then entered" : "NOT hit and entered")}; afterwards {crew[1]} shots at it, {crew[2]} health lost, order or target kept: {(crew[3] == 0 ? "no" : "YES")}. " +
                      $"A sniper squad whose attack order on a manned jeep is queued twice shoots its driver out: {(stale[0] == 1 ? "shot out while the second order waited" : "NOT reached, the second order was not waiting")}; attack orders on the empty jeep left in the lists: {stale[1]}; in the next 30 s {stale[2]} shots at it, {stale[3]} ticks aimed at it, {stale[4]} health lost (the same case with the rule off, as in Zod: {(staleOff[0] == 1 ? "shot out" : "NOT reached")}, {staleOff[1]} orders left, {staleOff[2]} shots at the empty jeep). " +
                      $"Cursor: {table[0]} kinds of selection, the attack cursor without Ctrl {table[1]} times, {table[2]} answers off the table. " +
                      $"A crane ordered with Ctrl: {(craneOk ? (crane.CanAttack() ? "attacks" : "refused, it has no weapon") : "CHECK")}. Switch off: {(off ? "the rule steps aside" : "CHECK")}";
                ok = plainAttack == 0 && entered == robots && moved == vehicles && refused == guns && harmed == 0
                     && shots == 0 && aimed == 0 && hurt == 0 && built == n && accepted == n && damaged == n
                     && crew[0] == 1 && crew[1] == 0 && crew[2] == 0 && crew[3] == 0 && table[1] == 0 && table[2] == 0 && craneOk && off
                     && stale[0] == 1 && stale[1] == 0 && stale[2] == 0 && stale[3] == 0 && stale[4] == 0
                     && staleOff[0] == 1 && staleOff[1] > 0 && staleOff[2] > 0;
            }
            finally { ZWorld.CtrlAttackEmptyDeviation = was; ZWorld.EjectLockDeviation = lockWas; ZWorld.EndFreezeDeviation = freeze; ZWorld.CalmStartDeviation = calm; }
            return new Deviation
            {
                id = "DV81", change = "Ctrl to attack an empty vehicle or gun: a plain right-click on one is never an attack order",
                zod = "the order table gives every selected vehicle and gun an ATTACK order on anything that is not of its team, the null team included ('they attack everything', zplayer.cpp:2797-2826); robots get ENTER on an empty vehicle or gun (zplayer.cpp:2837-2843) and cannot be told to attack one; the cursor shows ENTER as soon as one robot is selected (zplayer.cpp:2235), also when the vehicles of the same selection will attack. Nothing fires on the null team by itself (zobject.cpp:1649, 3301); when a driver is shot out every unit attacking it lets go, but only the FIRST waypoint of each is dropped: an attack order on it that waits further back in a list stays and is carried out on the empty vehicle later (zserver.cpp:1808-1874)",
                openz = "the player's input, and one rule for human teams; the CPU's orders are untouched. A right-click on an empty vehicle or gun without Ctrl: robots get the ENTER order as before, vehicles a plain move to the clicked point (they stop next to it), guns no order (refused, with the refused sound); the cursor is ENTER with a robot in the selection, else MOVE, guns only the gun cursor, never ATTACK. With Ctrl held, the key that attacks a neutral building: the ATTACK order for every selected unit, robots included, with the ATTACK cursor, mark and sounds (a selection with no weapon: the no-go cursor, refused). As in Zod nothing fires on an empty vehicle or gun by itself, an attack ends when a robot of the attacker's team gets in (CanAttackObject), and units attacking a vehicle whose driver is shot out let go of it: finishing it off takes Ctrl. New in the rules, human teams only: when a crew is shot out, an attack order on that vehicle or gun that still waits in a unit's list (a Shift queue, or an order pushed back by a step aside, a back-off or a rock in the way) is dropped as well, so it is never carried out on the empty vehicle; CPU teams keep Zod's rule (Sim/ZWorld.EmptyTargets.cs; Sim/ZWorld.cs UpdateObjectDriverHealth; View/PlayerInput.cs BuildWaypoint; UI/OpenZCursors.cs Decide)",
                measured = got,
                toggle = "ZWorld.CtrlAttackEmptyDeviation (code switch, on by default in the game and headless; off in every Zod row of the parity report. One rule of the simulation reads it: the dropped waiting orders, human teams only)",
                status = ok ? DevActive : DevCheck,
            };
        }

#if !OPENZ_HARNESS
        [MenuItem("OpenZ/Deviations/Probe DV81 Ctrl Attack Empty")]
        static void ProbeCtrlAttackEmptyMenu()
        {
            Func<string, int, int> get = (k, def) => PlayerPrefs.GetInt(k, def);
            var d = DevCtrlAttackEmpty(get);
            var path = System.IO.Path.Combine(OpenZPaths.Records, "ctrl_attack_empty_dv81.md");
            System.IO.File.WriteAllText(path, $"# DV81 Ctrl to attack an empty vehicle or gun, probe {DateTime.Now:yyyy-MM-dd HH:mm}\n\n## {d.status}\n\n- {d.measured.Replace(". ", ".\n- ")}\n");
            Debug.Log($"[DV81] {d.status}: {d.measured}\n-> {path}");
        }

        /// <summary>The CPU's game with the rule on or off: L02, the CPU (blue) against an idle red side, 20 min of game time, seed 12.
        /// The rule's only part in the simulation is for human teams (here the idle red side), and every time it acts it is counted
        /// (ZWorld.StaleEmptyOrdersDropped): with 0 drops the two games must be the same line for line.
        /// Returns what the CPU did: a first line with the vehicles and guns entered, the flags it took and its order count, then its
        /// whole order log.</summary>
        static string CaeCpuGame(bool on)
        {
            bool was = ZWorld.CtrlAttackEmptyDeviation;
            ZWorld.CtrlAttackEmptyDeviation = on;
            try
            {
                var w = ZWorld.Create(SettingsText, MapText("L02"), 12);
                var bots = ZBot.Attach(w, Team.Red, false, 12);
                foreach (var b in bots) b.Verbose = true;
                int entered = 0, flags = 0;
                while (w.Time < 1200)
                {
                    w.Tick(ZConst.TICK);
                    foreach (var e in w.Events)
                    {
                        if (e.type == ZEventType.Entered) entered++;
                        if (e.type == ZEventType.FlagCaptured && (Team)(int)e.value == Team.Blue) flags++;
                    }
                    w.Events.Clear();
                }
                var sb = new System.Text.StringBuilder();
                sb.Append("vehicles and guns entered ").Append(entered).Append(", flags taken by the CPU ").Append(flags);
                foreach (var b in bots)
                {
                    sb.Append(", CPU orders ").Append(b.Orders).Append(", order log lines ").Append(b.Log.Count);
                    foreach (var line in b.Log) sb.Append('\n').Append(line);
                }
                return sb.ToString();
            }
            finally { ZWorld.CtrlAttackEmptyDeviation = was; }
        }

        [MenuItem("OpenZ/Deviations/Probe DV81 CPU Game Rule On And Off (20 min, slow)")]
        static void ProbeCtrlAttackEmptyCpuMenu()
        {
            int before = ZWorld.StaleEmptyOrdersDropped;
            string on = CaeCpuGame(true);
            int dropped = ZWorld.StaleEmptyOrdersDropped - before;
            string off = CaeCpuGame(false);
            string same = on == off ? "IDENTICAL" : "DIFFERENT", head = on.Split('\n')[0];
            string verdict = on == off ? "" : dropped == 0 ? " FAULT: the rule dropped nothing, yet the two games differ." : " The rule acted on units of the idle red side; from its first drop on the two games may differ.";
            Debug.Log("[DV81] CPU game, L02, the CPU (blue) against an idle red side, 20 min of game time, seed 12: " + same + " with the rule on and off. Waiting attack orders of the red side dropped by the rule: " + dropped + "." + verdict + " (" + head + ")");
        }
#endif
    }
}
