using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using OpenZ.Sim;
#if !OPENZ_HARNESS
using UnityEditor;
using UnityEngine;
#endif

namespace OpenZ.EditorTools
{
    /// <summary>
    /// vehicles_nb_v1, deviations DV30 (heavy tank twin barrels) and DV31 (crane driver shoots back), Sim/ZObject.VehiclesNb.cs.
    /// Measured headless in the same sim the parity rows use, once with the switch on and once Zod exact:
    ///  * DevHeavyPair: a heavy tank against an unkillable, harmless light tank 6 tiles away on the empty L02 test map for
    ///    HeavyCycles cycles: shells per cycle, the one-two gap, the damage each shell carries (MissileExploded.value) and their
    ///    sum per cycle against Zod's Damage, the cycle length, and the hull damage the target really took per cycle.
    ///  * DevCraneGun: a crewed crane (huge hull) with a blue grunt 5 tiles away that attacks it, a second harmless blue grunt in
    ///    range that never attacks it: the crane's shots (only at its attacker), first shot after the attack started (pop-out),
    ///    cycle, hit rate and damage per hit against Zod's grunt, and that it never got a waypoint (no agro, orders kept).
    ///  * Menus OpenZ/Deviations/Heavy Tank Twin Barrels (oz_heavy_pair) and Crane Driver Shoots Back (oz_crane_gun).
    /// Compiles in the headless parity harness too (OPENZ_HARNESS: no UnityEditor).
    /// </summary>
    public static partial class OpenZParity
    {
        const int HeavyCycles = 40;

        static (int shells, int cycles, double gap, double gapMax, int pairSumOk, int firstDmg, int secondDmg, double cycle, double hullPerCycle, int zodDamage)
            HeavyPairProbe(bool on)
        {
            bool saved = ZWorld.HeavyPairDeviation, savedDuel = ZWorld.DuelDeviation;
            ZWorld.HeavyPairDeviation = on; ZWorld.DuelDeviation = false;                        // the heavy stands and fires (DV9 would manoeuvre)
            try
            {
                var w = Legacy(31);
                var a = w.CreateObject("vehicle", "heavy", 56 * 16, 14 * 16, Team.Red);
                var t = Dummy(w, "vehicle", "light", 50, 14, Team.Blue);
                t.MoveSpeed = 0; t.RealMoveSpeed = 0;                                             // never dodges: every shell lands on it (same splash on and off)
                Dummy(w, "robot", "grunt", 20, 40, Team.Blue);                                   // far away: blue is never eliminated (DV10)
                var fired = new List<(double at, bool second)>(); var exploded = new List<(double at, int dmg)>();
                int hp0 = t.Health; double engaged = -1;
                for (int i = 0; i < (int)((HeavyCycles + 2) * 4.2 / ZConst.TICK) && fired.Count(f => !f.second) < HeavyCycles + 1; i++)
                {
                    w.Tick(ZConst.TICK);
                    if (engaged < 0 && a.AttackObject != null) engaged = w.Time;
                    foreach (var e in w.Events)
                    {
                        if (e.objId != a.Id) continue;
                        if (e.type == ZEventType.MissileFired) fired.Add((w.Time, e.flag2));
                        if (e.type == ZEventType.MissileExploded && !e.flag2) exploded.Add((w.Time, (int)e.value));
                    }
                    w.Events.Clear();
                }
                // let the last shells land
                for (int i = 0; i < 300; i++) { w.Tick(ZConst.TICK); foreach (var e in w.Events) if (e.objId == a.Id && e.type == ZEventType.MissileExploded && !e.flag2) exploded.Add((w.Time, (int)e.value)); w.Events.Clear(); }
                var firsts = fired.Where(f => !f.second).Select(f => f.at).ToList();
                var seconds = fired.Where(f => f.second).Select(f => f.at).ToList();
                int cycles = Math.Min(HeavyCycles, firsts.Count - 1);
                double gapSum = 0, gapMax = 0; int gaps = 0;
                foreach (var s in seconds)
                {
                    var f = firsts.Where(x => x <= s + 1e-9).DefaultIfEmpty(-1).Max(); if (f < 0) continue;
                    gapSum += s - f; gapMax = Math.Max(gapMax, s - f); gaps++;
                }
                double cycle = cycles > 0 ? (firsts[cycles] - firsts[0]) / cycles : -1;
                // damage carried: shells in firing order (both land in order: same speed, the second leaves later)
                var dmg = exploded.Select(x => x.dmg).ToList();
                int per = on ? 2 : 1, pairOk = 0, fd = dmg.Count > 0 ? dmg[0] : 0, sd = on && dmg.Count > 1 ? dmg[1] : 0;
                for (int c = 0; c + per <= dmg.Count && c / per < cycles; c += per) if (dmg.Skip(c).Take(per).Sum() == a.Damage) pairOk++;
                double hull = cycles > 0 ? (double)(hp0 - t.Health) / Math.Max(1, exploded.Count / per) : 0;
                return (fired.Count, cycles, gaps > 0 ? gapSum / gaps : -1, gapMax, pairOk, fd, sd, cycle, hull, a.Damage);
            }
            finally { ZWorld.HeavyPairDeviation = saved; ZWorld.DuelDeviation = savedDuel; }
        }

        static Deviation DevHeavyPair(Func<string, int, int> getInt)
        {
            var on = HeavyPairProbe(true); var off = HeavyPairProbe(false);
            bool pref = getInt("oz_heavy_pair", 1) == 1 && getInt("oz_zod_exact", 0) == 0;
            double tick = ZConst.TICK + 1e-6;
            bool ok = on.cycles >= HeavyCycles - 1 && on.shells >= 2 * on.cycles && Math.Abs(on.gap - ZWorld.HeavyPairGap) <= tick && on.gapMax <= ZWorld.HeavyPairGap + tick
                      && on.pairSumOk >= on.cycles - 1 && on.firstDmg + on.secondDmg == on.zodDamage && Math.Abs(on.cycle - off.cycle) <= tick
                      && off.shells <= off.cycles + 2 && off.pairSumOk >= off.cycles - 1 && off.firstDmg == off.zodDamage
                      && Math.Abs(on.hullPerCycle - off.hullPerCycle) <= 0.12 * Math.Max(1, off.hullPerCycle);
            string S(double s) => s < 0 ? "never" : s.ToString("0.000", CultureInfo.InvariantCulture) + " s";
            return new Deviation
            {
                id = "DV30", change = "Heavy tank fires its two barrels one-two (half of Zod's damage each)",
                zod = $"one shell of Damage = {off.zodDamage} HP every attack_speed (zobject.cpp:2102-2213; heavy 4.088 s), splash 50 px, 135 px/s",
                openz = $"the cycle's shell carries floor(Damage / 2), a second shell with the rest leaves ZWorld.HeavyPairGap = {ZWorld.HeavyPairGap:0.00} s later with Zod's lead, scatter, speed and splash; the pair sums to Zod's Damage exactly; cycle, range and dodge unchanged",
                measured = $"L02 test map, heavy vs a harmless light tank 6 tiles away, {on.cycles} cycles: DV30 {on.shells} shells, gap {S(on.gap)} (max {S(on.gapMax)}), shells carry {on.firstDmg} + {on.secondDmg} = {on.firstDmg + on.secondDmg} HP (pairs summing to Zod's {on.zodDamage}: {on.pairSumOk}/{on.cycles}), cycle {S(on.cycle)}, hull damage {on.hullPerCycle:0} HP per cycle; Zod exact {off.shells} shells, {off.firstDmg} HP each, cycle {S(off.cycle)}, hull damage {off.hullPerCycle:0} HP per cycle",
                toggle = $"PlayerPrefs oz_heavy_pair = {(getInt("oz_heavy_pair", 1) == 1 ? "1 (on, default)" : "0 (off)")}; menu OpenZ/Deviations/Heavy Tank Twin Barrels; off in 'Zod exact' (oz_zod_exact = 1) and in the parity report's and the aim parity check's measurements",
                status = !ok ? DevCheck : pref ? DevActive : DevActive + " (off by pref)",
            };
        }

        static (int shots, int hits, int wrongTarget, double firstAfter, double cycle, double dmgPerHit, int waypoints, int zodDmg, double zodChance, double zodCycle)
            CraneGunProbe(bool on)
        {
            bool saved = ZWorld.CraneGunDeviation; ZWorld.CraneGunDeviation = on;
            try
            {
                var w = Legacy(32);
                var c = w.CreateObject("vehicle", "crane", 56 * 16, 14 * 16, Team.Red);
                c.MaxHealth = c.Health = 1000000000;
                var att = w.CreateRobotGroup("grunt", 51 * 16, 14 * 16, Team.Blue, 1);
                att.MaxHealth = att.Health = 1000000000;                                        // keeps shooting; its hull counts the crane's hits
                att.MoveSpeed = 0; att.RealMoveSpeed = 0;                                         // stands (no scuffle / dodge walk out of range)
                var calm = Dummy(w, "robot", "grunt", 56, 18, Team.Blue);                        // in range, never attacks (Damage 0)
                Dummy(w, "robot", "grunt", 20, 40, Team.Blue);
                int hp0 = att.Health; double attackAt = -1, first = -1; var shotTimes = new List<double>(); int hits = 0, wrong = 0, wp = 0;
                for (int i = 0; i < (int)(40 / ZConst.TICK); i++)
                {
                    w.Tick(ZConst.TICK);
                    if (attackAt < 0 && att.AttackObject == c) attackAt = w.Time;
                    foreach (var e in w.Events)
                    {
                        if (e.objId != c.Id || e.type != ZEventType.Hitscan) continue;
                        if (e.otherId != att.Id) wrong++;
                        shotTimes.Add(w.Time); if (e.flag) hits++;
                        if (first < 0) first = w.Time;
                    }
                    wp = Math.Max(wp, c.Waypoints.Count);
                    w.Events.Clear();
                }
                var u = w.S.Unit("grunt");
                double cycle = shotTimes.Count > 1 ? (shotTimes[shotTimes.Count - 1] - shotTimes[0]) / (shotTimes.Count - 1) : -1;
                return (shotTimes.Count, hits, wrong, first < 0 || attackAt < 0 ? -1 : first - attackAt, cycle, hits > 0 ? (double)(hp0 - att.Health) / hits : 0, wp,
                        u.Damage, u.attack_damage_chance, u.attack_speed);
            }
            finally { ZWorld.CraneGunDeviation = saved; }
        }

        static Deviation DevCraneGun(Func<string, int, int> getInt)
        {
            var on = CraneGunProbe(true); var off = CraneGunProbe(false);
            bool pref = getInt("oz_crane_gun", 1) == 1 && getInt("oz_zod_exact", 0) == 0;
            double tick = ZConst.TICK + 1e-6, rate = on.shots > 0 ? (double)on.hits / on.shots : 0;
            bool ok = on.shots >= 40 && on.wrongTarget == 0 && Math.Abs(on.cycle - on.zodCycle) <= 2 * tick && Math.Abs(on.dmgPerHit - on.zodDmg) < 1e-6   // cycle: Zod's own tick quantisation (0.5 s -> 0.51)
                      && Math.Abs(rate - on.zodChance) <= 0.12 && on.firstAfter >= ZWorld.CraneGunPopSeconds - tick && on.firstAfter <= ZWorld.CraneGunPopSeconds + 0.5 + 2 * tick
                      && on.waypoints == 0 && off.shots == 0 && off.waypoints == 0;
            string S(double s) => s < 0 ? "never" : s.ToString("0.00", CultureInfo.InvariantCulture) + " s";
            return new Deviation
            {
                id = "DV31", change = "Crane driver pops out of the cab hatch and shoots back at units attacking the crane",
                zod = "the crane has no weapon (attack_damage 0: never CanAttack, never engages or agros, cvehicle crane.cpp)",
                openz = $"a crewed crane picks, every 0.5 s, the first enemy unit ATTACKING it (its attack target or an Attack / Agro waypoint on it) in rifle range with a clear line; the driver pops out (ZWorld.CraneGunPopSeconds = {ZWorld.CraneGunPopSeconds:0.0} s) and fires the grunt's rifle ({on.zodDmg} HP, {on.zodChance:0%}, {on.zodCycle:0.0##} s, 120 px; no snipe), stays up {ZWorld.CraneGunLinger:0.0} s after his last shot; the crane keeps its orders, never agros, CanAttack() stays false",
                measured = $"L02 test map, crane attacked by a grunt 5 tiles away (a harmless grunt also in range): DV31 {on.shots} shots in 40 s, all at the attacker ({on.wrongTarget} at others), first {S(on.firstAfter)} after the attack began, cycle {S(on.cycle)}, hits {rate:0%}, {on.dmgPerHit:0.#} HP per hit, crane waypoints {on.waypoints}; Zod exact {off.shots} shots, waypoints {off.waypoints}",
                toggle = $"PlayerPrefs oz_crane_gun = {(getInt("oz_crane_gun", 1) == 1 ? "1 (on, default)" : "0 (off)")}; menu OpenZ/Deviations/Crane Driver Shoots Back; off in 'Zod exact' (oz_zod_exact = 1) and in the parity report's measurements",
                status = !ok ? DevCheck : pref ? DevActive : DevActive + " (off by pref)",
            };
        }

#if !OPENZ_HARNESS
        const string HeavyPairMenu = "OpenZ/Deviations/Heavy Tank Twin Barrels (oz_heavy_pair)";
        const string CraneGunMenu = "OpenZ/Deviations/Crane Driver Shoots Back (oz_crane_gun)";

        [MenuItem(HeavyPairMenu)]
        static void ToggleHeavyPair()
        {
            bool on = PlayerPrefs.GetInt("oz_heavy_pair", 1) != 1;
            PlayerPrefs.SetInt("oz_heavy_pair", on ? 1 : 0); PlayerPrefs.Save();
            Debug.Log($"OpenZ heavy tank twin barrels (deviation DV30) from the next Play: {(on ? "ON: left then right barrel, half of Zod's damage each" : "OFF: Zod's one shell of full damage")}" +
                      (PlayerPrefs.GetInt("oz_zod_exact", 0) == 1 ? " (still off while 'Zod exact' oz_zod_exact = 1)" : ""));
        }
        [MenuItem(HeavyPairMenu, true)]
        static bool ToggleHeavyPairCheck() { Menu.SetChecked(HeavyPairMenu, PlayerPrefs.GetInt("oz_heavy_pair", 1) == 1); return true; }

        [MenuItem(CraneGunMenu)]
        static void ToggleCraneGun()
        {
            bool on = PlayerPrefs.GetInt("oz_crane_gun", 1) != 1;
            PlayerPrefs.SetInt("oz_crane_gun", on ? 1 : 0); PlayerPrefs.Save();
            Debug.Log($"OpenZ crane driver shoots back (deviation DV31) from the next Play: {(on ? "ON" : "OFF: Zod's crane has no weapon")}" +
                      (PlayerPrefs.GetInt("oz_zod_exact", 0) == 1 ? " (still off while 'Zod exact' oz_zod_exact = 1)" : ""));
        }
        [MenuItem(CraneGunMenu, true)]
        static bool ToggleCraneGunCheck() { Menu.SetChecked(CraneGunMenu, PlayerPrefs.GetInt("oz_crane_gun", 1) == 1); return true; }
#endif
    }
}
