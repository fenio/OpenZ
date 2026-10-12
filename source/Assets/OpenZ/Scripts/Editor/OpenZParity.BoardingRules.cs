using System;
using System.Collections.Generic;
using System.Globalization;
using OpenZ.Sim;
#if !OPENZ_HARNESS
using UnityEditor;
using UnityEngine;
#endif

namespace OpenZ.EditorTools
{
    /// <summary>
    /// (Sim/ZObject.BoardingRules.cs), measured headless in the same sim as the parity rows (both switches off in every Zod row).
    /// Menus: OpenZ/Deviations/Eject Lock (oz_eject_lock), OpenZ/Deviations/Enter Fallback (oz_enter_fallback), Probe DV58 + DV59.
    /// </summary>
    public static partial class OpenZParity
    {
        static void AddBoardingRulesDeviations(List<Deviation> list, Func<string, int, int> getInt)
        {
            try { list.Add(DevEjectLock(getInt)); }
            catch (Exception ex) { list.Add(new Deviation { id = "DV58", change = "DevEjectLock", zod = "-", openz = "-", measured = "probe threw " + ex.GetType().Name + ": " + ex.Message, toggle = "-", status = DevCheck }); }
            try { list.Add(DevEnterFallback(getInt)); }
            catch (Exception ex) { list.Add(new Deviation { id = "DV59", change = "DevEnterFallback", zod = "-", openz = "-", measured = "probe threw " + ex.GetType().Name + ": " + ex.Message, toggle = "-", status = DevCheck }); }
            AddCtrlAttackEmptyDeviation(list, getInt);                                       // DV81 Ctrl to attack an empty vehicle or gun (OpenZParity.CtrlAttackEmpty.cs)
        }

        // ------------------------------------------------------------------ DV58
        /// <summary>DV58 probe: a red gun with its grunt (empty L02 test map), an idle red grunt squad 3 tiles off; the player ejects the gun
        /// at 1 s. Returns seconds from the eject to the next entry (-1 = none in 25 s) and, with `order`, a player ENTER order at 3 s.</summary>
        static (double reEnter, bool auto, int locks) EjectProbe(bool on, bool order)
        {
            bool saved = ZWorld.EjectLockDeviation, freeze = ZWorld.EndFreezeDeviation; ZWorld.EjectLockDeviation = on; ZWorld.EndFreezeDeviation = false;   // one-team legacy: no DV10 end freeze
            try
            {
                var w = Legacy(58);
                var gun = w.CreateObject("cannon", "gun", 56 * 16, 14 * 16, Team.Red);
                var sq = w.CreateRobotGroup("grunt", 56 * 16, 17 * 16, Team.Red);
                double tEject = -1, tIn = -1; bool auto = true, ordered = false;
                for (int i = 0; i < 4000 && w.Time < 27; i++)
                {
                    w.Tick(ZConst.TICK); w.Events.Clear();
                    if (tEject < 0 && w.Time >= 1.0) { w.Eject(Team.Red, gun); tEject = w.Time; }
                    if (order && !ordered && tEject > 0 && w.Time >= 3.0)
                    {
                        ordered = true;
                        w.SendWaypoints(Team.Red, sq, new[] { new ZWaypoint { mode = WpMode.Enter, refId = gun.Id, x = gun.CenterX, y = gun.CenterY } });
                    }
                    if (tEject > 0 && gun.Owner == Team.Red && gun.Drivers.Count > 0) { tIn = w.Time; auto = !ordered; break; }
                }
                return (tIn > 0 ? tIn - tEject : -1, auto, w.EjectLocks);
            }
            finally { ZWorld.EjectLockDeviation = saved; ZWorld.EndFreezeDeviation = freeze; }
        }

        static Deviation DevEjectLock(Func<string, int, int> getInt)
        {
            var on = EjectProbe(true, false); var off = EjectProbe(false, false); var ord = EjectProbe(true, true);
            string F(double v) => v < 0 ? "never" : v.ToString("0.00", CultureInfo.InvariantCulture) + " s";
            bool ok = on.reEnter >= 15.0 && on.reEnter < 16.5 && on.auto && off.reEnter >= 0 && off.reEnter < 3.0 && ord.reEnter > 0 && ord.reEnter < 8.0 && !ord.auto && on.locks == 1;
            bool pref = getInt("oz_eject_lock", 1) == 1 && getInt("oz_zod_exact", 0) == 0;
            return new Deviation
            {
                id = "DV58", change = "Eject lock: a gun the player ejected is not auto-entered by anyone for 15 s (explicit orders still enter)",
                zod = "only the ejected crew is kept out of cannons (just_left_cannon until its next order); any other idle robot within auto_grab_vehicle_distance walks straight back in (zobject.cpp:1603-1853), so the player's eject is undone at once",
                openz = $"EJECT_VEHICLE on a cannon by a human team (ZWorld.Eject) locks it for {ZObject.EjectLockTicks} sim ticks (15 s): no unit of any team auto-enters it (idle auto-grab, DV55 closest grabber, an auto ENTER waypoint already on its way); a player click or a CPU bot order enters at once; afterwards the normal auto-grab resumes. Also: the ejected crew no longer counts as DV55's closer grabber (it never takes a cannon back). The CPU never ejects",
                measured = $"L02 test map, red gun + idle red grunt squad 3 tiles off, player eject at 1 s: re-entered {F(on.reEnter)} after the eject (auto-grab, lock {on.locks}); Zod exact: {F(off.reEnter)}; player ENTER order 2 s after the eject: entered {F(ord.reEnter)} after the eject (order, during the lock)",
                toggle = $"ZWorld.EjectLockDeviation (PlayerPrefs oz_eject_lock = {(getInt("oz_eject_lock", 1) == 1 ? "1 (on, default)" : "0 (off)")}; menu OpenZ/Deviations/Eject Lock; off in 'Zod exact' oz_zod_exact 1 and in every Zod row)",
                status = !ok ? DevCheck : pref ? DevActive : DevActive + " (off by pref)",
            };
        }

        // ------------------------------------------------------------------ DV59
        struct FallbackCase { public string name; public bool firstIn, secondOk, oneDriver; public int fallbacks; public double dist2nd; public string end2nd; }

        /// <summary>DV59 probe cases on the empty L02 test map (red = the player). kind 0: two red squads to one neutral jeep; 1: two red squads
        /// to one neutral APC; 2: one squad to a jeep destroyed on the way (2 s); 3: one squad to a jeep a blue robot captures on the way.
        /// "second" = the squad that does not get in: where it ends (px from the target spot), its state.</summary>
        static FallbackCase FallbackProbe(int kind, bool on)
        {
            bool saved = ZWorld.EnterFallbackDeviation, freeze = ZWorld.EndFreezeDeviation; ZWorld.EnterFallbackDeviation = on; ZWorld.EndFreezeDeviation = false;
            try
            {
                var w = Legacy(59 + kind);
                string vt = kind == 1 ? "apc" : "jeep";
                var v = w.CreateObject("vehicle", vt, 62 * 16, 14 * 16, Team.Neutral);
                int vx = v.CenterX, vy = v.CenterY;
                var a = kind <= 1 ? w.CreateRobotGroup("grunt", 57 * 16, 14 * 16, Team.Red) : null;
                var b = w.CreateRobotGroup("grunt", 48 * 16, 16 * 16, Team.Red);
                ZObject blue = null;
                if (kind == 3)
                {   // a clean race: nobody shoots (no knock-downs, no grenade splash on the jeep), the jeep is unkillable
                    blue = w.CreateRobotGroup("grunt", 59 * 16, 13 * 16, Team.Blue, 1);   // the west approach the other cases use blue.Damage = 0; blue.MaxHealth = blue.Health = 1000000000;
                    b.Damage = 0; foreach (var m in b.Minions) m.Damage = 0;
                    v.MaxHealth = v.Health = 1000000000;
                }
                foreach (var u in new[] { a, b, blue })
                    if (u != null) w.SendWaypoints(u.Owner, u, new[] { new ZWaypoint { mode = WpMode.Enter, refId = v.Id, x = vx, y = vy } });
                bool destroyed = false, captured = false;
                for (int i = 0; i < 3000 && w.Time < 30; i++)
                {
                    w.Tick(ZConst.TICK); w.Events.Clear();
                    if (kind == 2 && !destroyed && w.Time >= 2.0) { destroyed = true; v.DamageHealth(v.Health); }
                    if (kind == 3 && v.Owner == Team.Blue) { v.Damage = 0; if (!captured) { captured = true; v.MaxHealth = v.Health = 1000000000; } }   // the captured jeep never shoots and stays (the red squad fights it)
                }
                var c = new FallbackCase { name = kind == 0 ? "2 squads -> 1 jeep" : kind == 1 ? "2 squads -> 1 APC" : kind == 2 ? "1 squad -> jeep destroyed on the way" : "1 squad -> jeep captured by blue on the way" };
                c.fallbacks = w.EnterFallbacks;
                if (kind == 3 && !captured) { var bb = w.Get(blue.Id); c.name += bb == null ? " (blue gone)" : $" (blue at {bb.CenterX >> 4},{bb.CenterY >> 4}, {(bb.Waypoints.Count > 0 ? bb.Waypoints[0].mode.ToString() : "idle")}, jeep {v.Owner} {(v.Destroyed ? "destroyed" : "")})"; }
                c.firstIn = kind == 0 ? v.Owner == Team.Red && v.Drivers.Count == 1 : kind == 1 ? v.Owner == Team.Red && v.Drivers.Count == 1 && v.Passengers.Count > 0 : kind == 2 ? v.Destroyed : captured;
                c.oneDriver = v.Drivers.Count <= 1;
                var live = w.Get(b.Id) ?? b.Successor;
                if (live == null || live.Destroyed || live.KillAt >= 0) { c.end2nd = "entered"; c.dist2nd = 0; c.secondOk = false; return c; }
                double d = Math.Sqrt((double)(live.CenterX - vx) * (live.CenterX - vx) + (double)(live.CenterY - vy) * (live.CenterY - vy));
                c.dist2nd = d;
                c.end2nd = live.Waypoints.Count == 0 ? (live.IsMoving ? "moving" : "stopped") : live.Waypoints[0].mode.ToString();
                int minionsFar = 0; foreach (var m in live.Minions) if (m != null && Math.Sqrt((double)(m.CenterX - vx) * (m.CenterX - vx) + (double)(m.CenterY - vy) * (m.CenterY - vy)) > 80) minionsFar++;
                c.secondOk = live.Waypoints.Count == 0 && !live.IsMoving && d <= 56 && minionsFar == 0;
                if (kind == 3 && !c.secondOk)                                                          // captured by the enemy: arrived, then it fights the blue jeep (normal)
                {
                    bool fights = live.AttackObject == v || (live.Waypoints.Count > 0 && (live.Waypoints[0].mode == WpMode.Agro || live.Waypoints[0].mode == WpMode.Attack) && live.Waypoints[0].refId == v.Id);
                    if (fights) { c.end2nd = "fighting the blue jeep"; c.secondOk = d <= 160; }
                }
                return c;
            }
            finally { ZWorld.EnterFallbackDeviation = saved; ZWorld.EndFreezeDeviation = freeze; }
        }

        static Deviation DevEnterFallback(Func<string, int, int> getInt)
        {
            var on = new FallbackCase[4]; var off = new FallbackCase[4];
            for (int k = 0; k < 4; k++) { on[k] = FallbackProbe(k, true); off[k] = FallbackProbe(k, false); }
            var on2 = FallbackProbe(0, true);
            string P(double v) => (v / 16.0).ToString("0.0", CultureInfo.InvariantCulture);
            bool same = on2.dist2nd == on[0].dist2nd && on2.end2nd == on[0].end2nd, ok = same;
            var m = new List<string>();
            for (int k = 0; k < 4; k++)
            {
                ok &= on[k].firstIn && on[k].secondOk && on[k].oneDriver && on[k].fallbacks >= 1 && off[k].fallbacks == 0;
                m.Add($"{on[k].name}: first in {on[k].firstIn}, one driver {on[k].oneDriver}; the other squad {on[k].end2nd} {P(on[k].dist2nd)} tiles from the target spot (Zod exact: {off[k].end2nd} {P(off[k].dist2nd)} tiles)");
            }
            m.Add($"two runs identical {same}");
            bool pref = getInt("oz_enter_fallback", 1) == 1 && getInt("oz_zod_exact", 0) == 0;
            return new Deviation
            {
                id = "DV59", change = "A player's enter order on a vehicle / gun / APC that another unit got first, or that was destroyed or captured on the way, becomes a walk to the spot next to it",
                zod = "an ENTER waypoint ends the moment its target can no longer be entered (zobject.cpp:2584-2667, KillWP): the squad freezes wherever it stands mid-path",
                openz = "a human team's ENTER order (playerGiven) whose target can no longer be entered turns into a plain MOVE (no attack-move) to the reachable spot next to the target nearest the squad (8 directions, half its size + 12 px; the order's last spot when the target is gone); the squad walks on in formation and stops there; the first to arrive still gets in (one driver; an APC takes one passenger squad). Minions wait one tick for their leader's new order. CPU squads keep Zod's KillWP (bot-vs-bot balance, like DV35). View: BoardingView starts a boarding only at the sim's Entered event, so nothing is ever half-played",
                measured = "L02 test map, red = the player: " + string.Join("; ", m),
                toggle = $"ZWorld.EnterFallbackDeviation (PlayerPrefs oz_enter_fallback = {(getInt("oz_enter_fallback", 1) == 1 ? "1 (on, default)" : "0 (off)")}; menu OpenZ/Deviations/Enter Fallback; off in 'Zod exact' oz_zod_exact 1 and in every Zod row)",
                status = !ok ? DevCheck : pref ? DevActive : DevActive + " (off by pref)",
            };
        }

#if !OPENZ_HARNESS
        const string MenuEjectLock = "OpenZ/Deviations/Eject Lock (oz_eject_lock)", MenuEnterFallback = "OpenZ/Deviations/Enter Fallback (oz_enter_fallback)";
        [MenuItem(MenuEjectLock)] static void TEjectLock() => NbToggle("oz_eject_lock", 1, "no auto entry into a gun the player ejected for 15 s (DV58)");
        [MenuItem(MenuEjectLock, true)] static bool TEjectLockC() { Menu.SetChecked(MenuEjectLock, PlayerPrefs.GetInt("oz_eject_lock", 1) == 1); return true; }
        [MenuItem(MenuEnterFallback)] static void TEnterFallback() => NbToggle("oz_enter_fallback", 1, "an enter order on a taken / lost vehicle walks on (DV59)");
        [MenuItem(MenuEnterFallback, true)] static bool TEnterFallbackC() { Menu.SetChecked(MenuEnterFallback, PlayerPrefs.GetInt("oz_enter_fallback", 1) == 1); return true; }

        [MenuItem("OpenZ/Deviations/Probe DV58 DV59 Boarding Rules")]
        static void ProbeBoardingRulesMenu()
        {
            Func<string, int, int> get = (k, def) => PlayerPrefs.GetInt(k, def);
            var a = DevEjectLock(get); var b = DevEnterFallback(get);
            var path = System.IO.Path.Combine(OpenZPaths.Records, "boarding_rules_dv58_dv59.md");
            System.IO.File.WriteAllText(path, $"# DV58 / DV59 boarding rules probe {DateTime.Now:yyyy-MM-dd HH:mm}\n\n## DV58 {a.status}\n\n- {a.measured.Replace("; ", "\n- ")}\n\n## DV59 {b.status}\n\n- {b.measured.Replace("; ", "\n- ")}\n");
            Debug.Log($"[DV58] {a.status}: {a.measured}\n[DV59] {b.status}: {b.measured}\n-> {path}");
        }
#endif
    }
}
