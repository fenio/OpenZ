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
    /// weapons_v2, deviation DV11 (ZObject.ToughPrepare.cs): the Tough's first rocket from the carry waits ZWorld.ToughPrepareSeconds
    /// (0.1 s zrobot.cpp:360 + 0.7 s rtough.cpp:211). Measured headless in the same sim the parity rows use:
    ///  * DevToughPrepare: the Deviations block row (fresh engage, the 1.442 s cycle, a re-engage inside the carry window, a new
    ///    engagement after the tube went back to the carry), once with DV11 and once Zod exact.
    ///  * ToughPrepareAimRow: the aim parity report's extra row (its Zod rows run Zod exact, like the parity report's).
    ///  * Menu OpenZ/Deviations/Tough Prepare Time (oz_tough_prepare): the editor toggle, read by ZGame at every start.
    /// Compiles in the headless parity harness too (OPENZ_HARNESS: no UnityEditor, no Row).
    /// </summary>
    public static partial class OpenZParity
    {
        /// <summary>One Tough (red, squad of 1) against unkillable, harmless blue grunts 5 tiles away on the empty L02 test map:
        /// first = engage -> first rocket; cycle = mean interval of the next rockets; reengage = the delay after target A is killed
        /// 1.3 s after a rocket and target B stands in range (inside the carry window: fires on the cycle); fresh2 = engage -> rocket
        /// for target C placed 3 s after the last target died (the tube is back in the carry).</summary>
        static (double first, double cycle, double reengage, double reengageCycleLeft, double fresh2) ToughPrepareProbe(bool on)
        {
            bool saved = ZWorld.ToughPrepareDeviation; ZWorld.ToughPrepareDeviation = on;
            try
            {
                var w = Legacy(21);
                var a = w.CreateRobotGroup("tough", 56 * 16, 14 * 16, Team.Red, 1);
                var t = Dummy(w, "robot", "grunt", 51, 14, Team.Blue);
                Dummy(w, "robot", "grunt", 20, 40, Team.Blue);                                   // far away, out of agro range: blue is never eliminated (no game over, DV10)
                double engaged = -1, first = -1; var rockets = new List<double>();
                for (int i = 0; i < 1200 && rockets.Count < 5; i++)
                {
                    w.Tick(ZConst.TICK);
                    if (engaged < 0 && a.AttackObject != null) engaged = w.Time;
                    foreach (var e in w.Events) if (e.objId == a.Id && e.type == ZEventType.MissileFired) rockets.Add(w.Time);
                    w.Events.Clear();
                }
                if (engaged < 0 || rockets.Count < 5) return (-1, -1, -1, -1, -1);
                first = rockets[0] - engaged;
                double cycle = (rockets[4] - rockets[0]) / 4;
                // target A dies 1.3 s after a rocket and B stands in range: the passive check re-engages within 0.5 s, inside the
                // carry window, so the rocket leaves as soon as Zod's cycle allows (a wrong prepare would add 0.8 s)
                double lastRocket = rockets[4];
                while (w.Time < lastRocket + 1.3) { w.Tick(ZConst.TICK); w.Events.Clear(); }
                var b = Dummy(w, "robot", "grunt", 51, 16, Team.Blue);
                t.SetHealth(0); w.UpdateObjectHealth(t, -1);
                double bEngaged = -1, bRocket = -1;
                for (int i = 0; i < 600 && bRocket < 0; i++)
                {
                    w.Tick(ZConst.TICK);
                    if (bEngaged < 0 && a.AttackObject == b) bEngaged = w.Time;
                    foreach (var e in w.Events) if (e.objId == a.Id && e.type == ZEventType.MissileFired) bRocket = w.Time;
                    w.Events.Clear();
                }
                double reengage = bEngaged < 0 || bRocket < 0 ? -1 : bRocket - bEngaged;
                double cycleLeft = bEngaged < 0 ? -1 : Math.Max(0, lastRocket + cycle - bEngaged);
                // B dies, nothing in range for 3 s (> ToughCarrySeconds), then C appears: a fresh prepare
                b.SetHealth(0); w.UpdateObjectHealth(b, -1);
                for (int i = 0; i < 300; i++) { w.Tick(ZConst.TICK); w.Events.Clear(); }
                var c = Dummy(w, "robot", "grunt", 52, 13, Team.Blue);
                double cEngaged = -1, cRocket = -1;
                for (int i = 0; i < 600 && cRocket < 0; i++)
                {
                    w.Tick(ZConst.TICK);
                    if (cEngaged < 0 && a.AttackObject == c) cEngaged = w.Time;
                    foreach (var e in w.Events) if (e.objId == a.Id && e.type == ZEventType.MissileFired) cRocket = w.Time;
                    w.Events.Clear();
                }
                double fresh2 = cEngaged < 0 || cRocket < 0 ? -1 : cRocket - cEngaged;
                return (first, cycle, reengage, cycleLeft, fresh2);
            }
            finally { ZWorld.ToughPrepareDeviation = saved; }
        }

        static Deviation DevToughPrepare(Func<string, int, int> getInt)
        {
            var on = ToughPrepareProbe(true); var off = ToughPrepareProbe(false);
            bool pref = getInt("oz_tough_prepare", 1) == 1 && getInt("oz_zod_exact", 0) == 0;
            double tick = ZConst.TICK + 1e-6, P = ZWorld.ToughPrepareSeconds;
            bool ok = on.first >= 0 && Math.Abs(on.first - P) <= tick && off.first >= 0 && off.first <= tick
                      && Math.Abs(on.cycle - off.cycle) <= tick && on.reengage >= 0 && on.reengage <= on.reengageCycleLeft + tick
                      && Math.Abs(on.fresh2 - P) <= tick && off.fresh2 >= 0 && off.fresh2 <= tick;
            string S(double s) => s < 0 ? "never" : s.ToString("0.00", System.Globalization.CultureInfo.InvariantCulture) + " s";
            return new Deviation
            {
                id = "DV11", change = "Tough prepare time: the first bazooka rocket of an engagement from the carry waits for the aim",
                zod = "the server fires the first rocket on the engage tick (zobject.cpp:2102-2213, no delay); the client shows the aim sprite 0.1 s after SetAttackObject (zrobot.cpp:360) and holds the aim 0.7-1.0 s before every LATER rocket (rtough.cpp:211)",
                openz = $"a Tough with no target for ZWorld.ToughCarrySeconds = {ZWorld.ToughCarrySeconds:0.0#} s (the bazooka is back in the carry) fires its first rocket ZWorld.ToughPrepareSeconds = {P:0.0#} s (0.1 + 0.7) after engaging; re-engages inside that window, the 1.442 s cycle, damage, splash, speed, lead, scatter, dodge and grenade throws are Zod's; no rand() draw",
                measured = $"L02 test map, tough vs a grunt 5 tiles away: engage -> first rocket {S(on.first)} (Zod exact {S(off.first)}); cycle {S(on.cycle)} (Zod exact {S(off.cycle)}); new target 1.3 s after the last rocket (inside the carry window): rocket {S(on.reengage)} after the engage (Zod's cycle had {S(on.reengageCycleLeft)} left: no prepare); new target after 3 s without one: {S(on.fresh2)} (Zod exact {S(off.fresh2)})",
                toggle = $"PlayerPrefs oz_tough_prepare = {(getInt("oz_tough_prepare", 1) == 1 ? "1 (on, default)" : "0 (off)")}; menu OpenZ/Deviations/Tough Prepare Time; off in 'Zod exact' (oz_zod_exact = 1) and in the parity report's and the aim parity check's Zod rows",
                status = !ok ? DevCheck : pref ? DevActive : DevActive + " (off by pref)",
            };
        }

#if !OPENZ_HARNESS
        /// <summary>The aim parity report's DV11 row (its Zod rows run with DV11 off, see RunAim).</summary>
        static void ToughPrepareAimRow(List<Row> rows)
        {
            var on = ToughPrepareProbe(true);
            double tick = ZConst.TICK + 1e-6;
            rows.Add(new Row
            {
                test = "tough (OpenZ deviation DV11 on): first rocket after engaging from the carry",
                expected = $"{ZWorld.ToughPrepareSeconds:0.00} s (0.1 s zrobot.cpp:360 + 0.7 s rtough.cpp:211); later rockets on Zod's 1.442 s cycle",
                measured = on.first < 0 ? "never fired" : $"{on.first * 1000:F0} ms, cycle {on.cycle:0.000} s, new engagement after 3 s {on.fresh2 * 1000:F0} ms",
                pass = on.first >= 0 && Math.Abs(on.first - ZWorld.ToughPrepareSeconds) <= tick && Math.Abs(on.cycle - 1.442) <= tick && Math.Abs(on.fresh2 - ZWorld.ToughPrepareSeconds) <= tick,
                note = "weapons_v2: the view raises + settles the bazooka in 0.6 s, so the rocket leaves a steady tube at Zod's speed"
            });
        }

        const string ToughPrepareMenu = "OpenZ/Deviations/Tough Prepare Time (oz_tough_prepare)";

        [MenuItem(ToughPrepareMenu)]
        static void ToggleToughPrepare()
        {
            bool on = PlayerPrefs.GetInt("oz_tough_prepare", 1) != 1;
            PlayerPrefs.SetInt("oz_tough_prepare", on ? 1 : 0); PlayerPrefs.Save();
            Debug.Log($"OpenZ Tough prepare time (deviation DV11) from the next Play: {(on ? $"ON: the first rocket from the carry waits {ZWorld.ToughPrepareSeconds:0.0} s" : "OFF: Zod fires on the engage tick (the view raises the tube while the rocket flies)")}" +
                      (PlayerPrefs.GetInt("oz_zod_exact", 0) == 1 ? " (still off while 'Zod exact' oz_zod_exact = 1)" : ""));
        }

        [MenuItem(ToughPrepareMenu, true)]
        static bool ToggleToughPrepareCheck() { Menu.SetChecked(ToughPrepareMenu, PlayerPrefs.GetInt("oz_tough_prepare", 1) == 1); return true; }
#endif
    }
}
