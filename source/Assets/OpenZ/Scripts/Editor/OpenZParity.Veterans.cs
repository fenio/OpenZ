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
    /// veterancy_v1, deviation DV12 "veteran crews" (Sim/ZVeterancy.cs), measured headless in the same sim the parity rows use:
    ///  * promotion: a red grunt squad on the empty L02 test map kills blue grunts one by one; promoted at exactly VeteranKills
    ///    kills (Promoted event, every squad member veteran); its leader then boards a neutral jeep: the jeep's crew is veteran;
    ///  * buttoned up: 3 red grunts fire at an unkillable blue jeep (driver pool unlimited) for 4 x 60 s, normal crew vs veteran
    ///    crew: the driver share of hits = Zod's snipe chance (0.30) vs x VeteranSnipeFactor;
    ///  * accuracy: the same runs with the grunts veteran: hit share of shots = damage_chance (0.70) vs x VeteranHitFactor;
    ///  * off: with ZWorld.VeteranDeviation = false nobody is promoted and a veteran flag changes nothing.
    /// Menu OpenZ/Deviations/Veteran Crews (oz_veterans): the editor toggle, read by ZGame at every start.
    /// Compiles in the headless parity harness too (OPENZ_HARNESS: no UnityEditor).
    /// </summary>
    public static partial class OpenZParity
    {
        /// <summary>(kills at promotion or -1, promotion time, all members veteran, jeep veteran after boarding, promoted events)</summary>
        static (int killsAt, double at, bool squadVet, bool jeepVet, int events) VeteranPromotionProbe(bool on)
        {
            bool saved = ZWorld.VeteranDeviation; ZWorld.VeteranDeviation = on;
            try
            {
                var w = Legacy(21);
                var a = w.CreateRobotGroup("grunt", 56 * 16, 14 * 16, Team.Red, 3);
                foreach (var m in a.Minions.Concat(new[] { a })) m.MaxHealth = m.Health = 1000000000;   // the squad never dies
                Dummy(w, "robot", "grunt", 20, 40, Team.Blue);                                       // far away: blue is never eliminated (DV10)
                int killsAt = -1, events = 0; double at = -1;
                for (int k = 0; k < ZWorld.VeteranKills + 2 && killsAt < 0; k++)
                {
                    var t = w.CreateRobotGroup("grunt", 60 * 16, 14 * 16, Team.Blue, 1); t.Damage = 0; t.MoveSpeed = 0; t.RealMoveSpeed = 0;
                    Until(w, 60, e => { if (e.type == ZEventType.Promoted && e.objId == a.Id) { events++; killsAt = a.Kills; at = w.Time; } return false; },
                          () => t.Destroyed || t.KillAt >= 0);
                    for (int i = 0; i < 20; i++) { w.Tick(ZConst.TICK); foreach (var e in w.Events) if (e.type == ZEventType.Promoted) { events++; if (killsAt < 0) { killsAt = a.Kills; at = w.Time; } } w.Events.Clear(); }
                }
                bool squadVet = a.Veteran && a.Minions.All(m => m.Veteran);
                var jeep = w.CreateObject("vehicle", "jeep", 58 * 16, 17 * 16, Team.Neutral);
                w.SendWaypoints(Team.Red, a, new List<ZWaypoint> { new ZWaypoint { mode = WpMode.Enter, refId = jeep.Id, x = jeep.CenterX, y = jeep.CenterY } });
                Until(w, 30, null, () => jeep.Owner == Team.Red);
                return (killsAt, at, squadVet, jeep.Owner == Team.Red && jeep.Veteran, events);
            }
            finally { ZWorld.VeteranDeviation = saved; }
        }

        /// <summary>3 red grunts vs an unkillable blue jeep: (shots, hits, driver hits) over seeds x seconds.</summary>
        static (long shots, long hits, long driver) VeteranSnipeProbe(bool on, bool vetTarget, bool vetShooters, int seeds = 4, double seconds = 60)
        {
            bool saved = ZWorld.VeteranDeviation; ZWorld.VeteranDeviation = on;
            try
            {
                long shots = 0, hits = 0, drv = 0;
                for (int seed = 1; seed <= seeds; seed++)
                {
                    var w = Legacy(100 + seed);
                    var a = w.CreateRobotGroup("grunt", 56 * 16, 14 * 16, Team.Red, 3);
                    var t = Dummy(w, "vehicle", "jeep", 60, 14, Team.Blue);
                    t.MoveSpeed = 0; t.RealMoveSpeed = 0; t.Drivers[0] = 1000000000; t.Veteran = vetTarget;
                    if (vetShooters) { a.Veteran = true; foreach (var m in a.Minions) m.Veteran = true; }
                    w.SendWaypoints(Team.Red, a, new List<ZWaypoint> { new ZWaypoint { mode = WpMode.Attack, refId = t.Id, x = t.CenterX, y = t.CenterY } });
                    double end = w.Time + seconds;
                    while (w.Time < end)
                    {
                        w.Tick(ZConst.TICK);
                        foreach (var e in w.Events) if (e.type == ZEventType.Hitscan && e.otherId == t.Id) { shots++; if (e.flag) { hits++; if (e.flag2) drv++; } }
                        w.Events.Clear();
                    }
                }
                return (shots, hits, drv);
            }
            finally { ZWorld.VeteranDeviation = saved; }
        }

        static Deviation DevVeterans(Func<string, int, int> getInt)
        {
            var pr = VeteranPromotionProbe(true); var prOff = VeteranPromotionProbe(false);
            var normal = VeteranSnipeProbe(true, false, false);
            var vetT = VeteranSnipeProbe(true, true, false);
            var vetS = VeteranSnipeProbe(true, false, true);
            var offT = VeteranSnipeProbe(false, true, true);
            double S(long a, long b) => b == 0 ? 0 : (double)a / b;
            double Grid(double c) => (Math.Floor(c * 10000 + 1e-9) + 1) / 10000.0;                  // Zod rolls <= chance on a 1/10000 grid
            bool Near(long k, long n, double p) { if (n == 0) return false; double m = (double)k / n, sd = Math.Sqrt(Math.Max(p * (1 - p), 1e-4) / n); return Math.Abs(m - p) <= 4 * sd + 1e-4; }
            var w0 = Legacy(1); var g = w0.S.Unit("grunt");
            double snipe = g.attack_snipe_chance, hit = g.attack_damage_chance;
            double pSnipe = Grid(snipe), pSnipeVet = Grid(snipe * ZWorld.VeteranSnipeFactor);
            double pHit = Grid(hit), pHitVet = Math.Min(1.0, hit * ZWorld.VeteranHitFactor);   // hit = roll <= chance (zobject.cpp:2187)
            bool okPromo = pr.killsAt == ZWorld.VeteranKills && pr.squadVet && pr.jeepVet && pr.events == 1 && prOff.killsAt < 0 && prOff.events == 0;
            bool okSnipe = Near(normal.driver, normal.hits, pSnipe) && Near(vetT.driver, vetT.hits, pSnipeVet) && Near(offT.driver, offT.hits, pSnipe);
            bool okHit = Near(normal.hits, normal.shots, pHit) && Near(vetS.hits, vetS.shots, Grid(pHitVet)) && Near(offT.hits, offT.shots, pHit);
            bool pref = getInt("oz_veterans", 1) == 1 && getInt("oz_zod_exact", 0) == 0;
            return new Deviation
            {
                id = "DV12", change = "Veteran crews: kills promote a squad / crew; veterans stay buttoned up (much harder to snipe), aim a bit better, gold star",
                zod = "the Zod Engine has no veterancy, experience or rank (no kill count in zobject.h/.cpp; ZUnitRating in zbot.cpp is a static per-type table for the CPU's targeting; the only level is a building's production level); the 1996 Z had none either as far as known",
                openz = $"a kill (enemy robot / vehicle / cannon destroyed, or a driver sniped, by a live unit) is credited to the squad (every member carries the count) or to the vehicle's / cannon's crew; promotion at ZWorld.VeteranKills = {ZWorld.VeteranKills}; the rank travels with the crew (vehicle enter / eject, repair station), a sniped driver loses it; perks: snipe chance against a veteran crew x{ZWorld.VeteranSnipeFactor:0.##} (Zod's roll and lid kept, the view never pops a veteran commander out of the hatch), hitscan damage_chance x{ZWorld.VeteranHitFactor:0.##} (cap 1), missile scatter x{ZWorld.VeteranScatterNum}/{ZWorld.VeteranScatterDen}, turret / torso slew x{1.35:0.##} (view only); gold star at the health bar and on the portrait, Alan's promotion line (Rachel off screen); no random draws of its own, deterministic",
                measured = $"L02 test map: red grunt squad promoted after {pr.killsAt} kills at {pr.at:0.0} s ({pr.events} Promoted event, squad {(pr.squadVet ? "all veteran" : "NOT all veteran")}), its leader boarded a jeep: {(pr.jeepVet ? "veteran crew" : "NOT veteran")}; off: {(prOff.killsAt < 0 ? "no promotion" : "PROMOTED")}. 3 grunts vs a jeep, 4 x 60 s: driver share of hits normal crew {S(normal.driver, normal.hits):0.000} ({normal.hits} hits, Zod {pSnipe:0.000}), veteran crew {S(vetT.driver, vetT.hits):0.000} ({vetT.hits} hits, expected {pSnipeVet:0.000}), veteran with DV12 off {S(offT.driver, offT.hits):0.000}; hit share normal {S(normal.hits, normal.shots):0.000} ({normal.shots} shots, Zod {pHit:0.000}), veteran grunts {S(vetS.hits, vetS.shots):0.000} (expected {Grid(pHitVet):0.000})",
                toggle = $"PlayerPrefs oz_veterans = {(getInt("oz_veterans", 1) == 1 ? "1 (on, default)" : "0 (off)")}; menu OpenZ/Deviations/Veteran Crews; off in 'Zod exact' (oz_zod_exact = 1), in this parity report's measurements and in the bot checks",
                status = !(okPromo && okSnipe && okHit) ? DevCheck : pref ? DevActive : DevActive + " (off by pref)",
            };
        }

#if !OPENZ_HARNESS
        const string VeteransMenu = "OpenZ/Deviations/Veteran Crews (oz_veterans)";

        [MenuItem(VeteransMenu)]
        static void ToggleVeterans()
        {
            bool on = PlayerPrefs.GetInt("oz_veterans", 1) != 1;
            PlayerPrefs.SetInt("oz_veterans", on ? 1 : 0); PlayerPrefs.Save();
            Debug.Log($"OpenZ veteran crews (deviation DV12) from the next Play: {(on ? $"ON: {ZWorld.VeteranKills} kills promote a squad / crew (gold star, buttoned up, a little more accurate)" : "OFF: no ranks, like Zod")}" +
                      (PlayerPrefs.GetInt("oz_zod_exact", 0) == 1 ? " (still off while 'Zod exact' oz_zod_exact = 1)" : ""));
        }

        [MenuItem(VeteransMenu, true)]
        static bool ToggleVeteransCheck() { Menu.SetChecked(VeteransMenu, PlayerPrefs.GetInt("oz_veterans", 1) == 1); return true; }
#endif
    }
}
