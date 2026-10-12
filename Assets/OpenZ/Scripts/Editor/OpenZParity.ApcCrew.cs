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
    /// <summary>apc_v1: the parity report's DV15 row (APC driver + passenger squad, Sim/ZApcCrew.cs) and its editor toggle.
    /// </summary>
    public static partial class OpenZParity
    {
        sealed class ApcProbeResult
        {
            public double dmgA, ivA, dmgC, ivC, boardT = -1; public int shotsA, shotsC, radiusA, radiusC;
            public int passengers, drivers, firepower; public string driverType, passengerType, weaponC;
            public bool secondRefused, enterRefused, ejectOk, ejectAgain, apcOwnedAfter, driverStays, hpKept;
            public int outCount; public string outType; public Team ownerAfter;
            public int nDrivers, nPassengers, nFirepower; public string nWeapon;       // neutral APC boarded by a grunt squad
        }

        static ApcProbeResult ApcProbe()
        {
            var r = new ApcProbeResult();
            var w = Legacy(15);
            var apc = w.CreateObject("vehicle", "apc", 56 * 16, 14 * 16, Team.Red);
            var dummy = Dummy(w, "vehicle", "heavy", 61, 14, Team.Blue);                                    // 80 px east: in every crew weapon's range
            // A: the driver alone (Zod's one-grunt crew)
            (int shots, double dmg, double iv) Fire(double secs)
            {
                int shots = 0, hits = 0; long lost = 0; double first = -1, last = -1;
                double end = w.Time + secs;
                while (w.Time < end)
                {
                    int h0 = dummy.Health;
                    w.Tick(ZConst.TICK);
                    foreach (var e in w.Events)
                        if (e.objId == apc.Id && (e.type == ZEventType.Hitscan || e.type == ZEventType.MissileFired))
                        { shots++; if (e.type == ZEventType.MissileFired || e.flag) hits++; if (first < 0) first = w.Time; last = w.Time; }
                    lost += h0 - dummy.Health;
                    w.Events.Clear();
                }
                return (shots, hits > 0 ? (double)lost / hits : 0, shots > 1 ? (last - first) / (shots - 1) : 0);
            }
            (r.shotsA, r.dmgA, r.ivA) = Fire(20); r.radiusA = apc.AttackRadius;
            // B: a psycho squad (3) ordered to enter the owned APC, from 3 tiles west (away from the dummy)
            var psy = w.CreateRobotGroup("psycho", 52 * 16, 14 * 16, Team.Red);
            psy.Health = psy.MaxHealth / 2;                                                     // a wounded leader: his HP must come back out
            int hpLeader = psy.Health;
            w.SendWaypoints(Team.Red, psy, new[] { new ZWaypoint { mode = WpMode.Enter, refId = apc.Id, x = apc.CenterX, y = apc.CenterY } });
            r.enterRefused = psy.Waypoints.Count == 0;
            double t0 = w.Time;
            while (w.Time < t0 + 30 && apc.Passengers.Count == 0 && !r.enterRefused) { w.Tick(ZConst.TICK); w.Events.Clear(); }
            if (apc.Passengers.Count > 0) r.boardT = w.Time - t0;
            r.passengers = apc.Passengers.Count; r.drivers = apc.Drivers.Count; r.driverType = apc.DriverType; r.passengerType = apc.PassengerType;
            r.firepower = apc.CrewFirepower;
            // a second squad is refused while passengers are aboard
            var gr2 = w.CreateRobotGroup("grunt", 52 * 16, 16 * 16, Team.Red);
            w.SendWaypoints(Team.Red, gr2, new[] { new ZWaypoint { mode = WpMode.Enter, refId = apc.Id, x = apc.CenterX, y = apc.CenterY } });
            r.secondRefused = gr2.Waypoints.Count == 0 || apc.Passengers.Count == 0;
            foreach (var o in new[] { gr2 }.Concat(gr2.Minions).ToList()) w.Objects.Remove(o);          // out of the legacy: only the APC fires at the dummy
            // C: firing with the passengers aboard
            (r.shotsC, r.dmgC, r.ivC) = Fire(20); r.radiusC = apc.AttackRadius; r.weaponC = apc.CrewWeaponType;
            // D: EXIT
            var before = new HashSet<int>(w.Objects.Select(o => o.Id));
            r.ejectOk = w.Eject(Team.Red, apc);
            var outs = w.Objects.Where(o => !before.Contains(o.Id) && o.IsRobot).ToList();
            r.outCount = outs.Count; r.outType = outs.Count > 0 ? outs[0].Type : "-";
            r.hpKept = ZWorld.ApcCrewDeviation ? outs.Count > 0 && outs[0].Health == hpLeader : true;
            r.ownerAfter = apc.Owner; r.apcOwnedAfter = apc.Owner == Team.Red; r.driverStays = apc.Drivers.Count == 1;
            r.ejectAgain = w.Eject(Team.Red, apc);
            // E: a neutral APC boarded by a grunt squad
            var w2 = Legacy(16);
            var napc = w2.CreateObject("vehicle", "apc", 56 * 16, 14 * 16, Team.Neutral);
            Dummy(w2, "vehicle", "heavy", 20, 14, Team.Blue);                                   // a second team far away (one team = game over, DV10 freeze)
            var gs = w2.CreateRobotGroup("grunt", 52 * 16, 14 * 16, Team.Red);
            w2.SendWaypoints(Team.Red, gs, new[] { new ZWaypoint { mode = WpMode.Enter, refId = napc.Id, x = napc.CenterX, y = napc.CenterY } });
            double t1 = w2.Time;
            while (w2.Time < t1 + 30 && napc.Owner == Team.Neutral) { w2.Tick(ZConst.TICK); w2.Events.Clear(); }
            r.nDrivers = napc.Drivers.Count; r.nPassengers = napc.Passengers.Count; r.nFirepower = napc.CrewFirepower; r.nWeapon = napc.CrewWeaponType;
            return r;
        }

        static Deviation DevApcCrew(Func<string, int, int> getInt)
        {
            bool saved = ZWorld.ApcCrewDeviation;
            ApcProbeResult on, off;
            try { ZWorld.ApcCrewDeviation = true; on = ApcProbe(); ZWorld.ApcCrewDeviation = false; off = ApcProbe(); }
            finally { ZWorld.ApcCrewDeviation = saved; }
            bool pref = getInt("oz_apc_crew", 1) == 1 && getInt("oz_zod_exact", 0) == 0;
            var g = ZSettingsProbe();
            bool okOn = on.boardT > 0 && on.passengers == 3 && on.drivers == 1 && on.driverType == "grunt" && on.passengerType == "psycho" && on.firepower == 3
                        && on.secondRefused && !on.enterRefused && Math.Abs(on.dmgA - g.grunt) < 0.5 && Math.Abs(on.dmgC - 3 * g.psycho) < 0.5
                        && on.ejectOk && on.outCount == 3 && on.outType == "psycho" && on.hpKept && on.apcOwnedAfter && on.driverStays && !on.ejectAgain
                        && on.nDrivers == 1 && on.nPassengers == 2 && on.nFirepower == 3;
            bool okOff = off.enterRefused && off.ejectOk && off.outCount == 1 && off.outType == "grunt" && off.ownerAfter == Team.Neutral && off.nDrivers == 3 && off.nFirepower == 3;
            string F(double d) => d.ToString("0.#", System.Globalization.CultureInfo.InvariantCulture);
            string Iv(double d) => d.ToString("0.00", System.Globalization.CultureInfo.InvariantCulture);
            return new Deviation
            {
                id = "DV15", change = "APC driver + passenger squad (the grunt driver stays; a squad boards, fires from it and exits on order)",
                zod = "one crew list of one robot type (zobject.h:209-213): an owned APC has its 1 grunt driver and cannot be entered (only neutral vehicles, zobject.cpp:4723-4730); a squad entering a neutral APC all become drivers (zserver.cpp:1399-1411); fires the crew weapon x crew size (vapc.cpp:266-342); EXIT ejects the whole crew and the APC turns neutral (zserver_events.cpp:631-731)",
                openz = "ZApcCrew.cs: 1 driver who never leaves on an order + 1 passenger squad (type, per-robot HP, grenade pool); an owned crewed APC takes ONE same-team squad by the normal ENTER order (a second squad is refused: one weapon type per APC, squads stay Zod-sized); a neutral APC: the leader drives, the minions ride; fires the passengers' weapon x every robot aboard carrying it (Zod's VAPC rule; the driver counts when he is of the same type), else the driver's; EXIT = the passengers leave as their squad next to the APC with their HP and grenades, the driver stays, the APC stays owned (no passengers: EXIT refused, CanEjectCrew false); the APC turns neutral only if its driver dies (never snipable, as in Zod): the passengers get out alive; repair keeps them; destroyed = all die",
                measured = $"L02 test map, red APC vs an unkillable dummy 80 px away. DV15: driver only {F(on.dmgA)} dmg/hit every {Iv(on.ivA)} s (grunt {F(g.grunt)}, range {on.radiusA}); psycho squad boarded in {F(on.boardT)} s -> driver {on.drivers} {on.driverType} + {on.passengers} {on.passengerType}, fires {on.weaponC} x{on.firepower}: {F(on.dmgC)} dmg/hit every {Iv(on.ivC)} s (3 x psycho {F(3 * g.psycho)}, range {on.radiusC}); 2nd squad {(on.secondRefused ? "refused" : "ACCEPTED")}; EXIT {(on.ejectOk ? "ok" : "REFUSED")}: {on.outCount} {on.outType} out (leader HP {(on.hpKept ? "kept" : "LOST")}), driver {(on.driverStays ? "stays" : "GONE")}, APC {on.ownerAfter}; 2nd EXIT {(on.ejectAgain ? "ACCEPTED" : "refused")}; neutral APC + 3 grunts -> driver {on.nDrivers} + {on.nPassengers} passengers, {on.nWeapon} x{on.nFirepower} (Zod x3). " +
                           $"Zod exact: enter {(off.enterRefused ? "refused" : "ACCEPTED")}, EXIT -> {off.outCount} {off.outType} out, APC {off.ownerAfter}; neutral APC + 3 grunts -> {off.nDrivers} drivers x{off.nFirepower}",
                toggle = $"PlayerPrefs oz_apc_crew = {(getInt("oz_apc_crew", 1) == 1 ? "1 (on, default)" : "0 (off)")}; menu OpenZ/Deviations/APC Driver + Passengers; off in 'Zod exact' (oz_zod_exact = 1)",
                status = !(okOn && okOff) ? DevCheck : pref ? DevActive : DevActive + " (off by pref)",
            };
        }

        /// <summary>Zod per-hit damage of the crew robots (settings x MAX_UNIT_HEALTH, as ZSettings reads them).</summary>
        static (double grunt, double psycho) ZSettingsProbe()
        {
            var w = ZWorld.Create(SettingsText, MapText("L02"), 1);
            return (w.S.Unit("grunt").Damage, w.S.Unit("psycho").Damage);
        }

#if !OPENZ_HARNESS
        const string ApcCrewMenu = "OpenZ/Deviations/APC Driver + Passengers (oz_apc_crew)";

        [MenuItem(ApcCrewMenu)]
        static void ToggleApcCrew()
        {
            bool on = PlayerPrefs.GetInt("oz_apc_crew", 1) != 1;
            PlayerPrefs.SetInt("oz_apc_crew", on ? 1 : 0); PlayerPrefs.Save();
            Debug.Log($"OpenZ APC crew (deviation DV15) from the next Play: {(on ? "ON: the grunt driver stays, one squad boards as passengers, fires from the APC and exits on order" : "OFF: Zod's APC (only neutral APCs can be entered, EXIT ejects the whole crew)")}" +
                      (PlayerPrefs.GetInt("oz_zod_exact", 0) == 1 ? " (still off while 'Zod exact' oz_zod_exact = 1)" : ""));
        }

        [MenuItem(ApcCrewMenu, true)]
        static bool ToggleApcCrewCheck() { Menu.SetChecked(ApcCrewMenu, PlayerPrefs.GetInt("oz_apc_crew", 1) == 1); return true; }
#endif
    }
}
