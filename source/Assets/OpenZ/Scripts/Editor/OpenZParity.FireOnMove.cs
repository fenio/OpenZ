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
    /// sim as the parity rows (the switch is off in every Zod row). Menu: OpenZ/Deviations/Fire On The Move (oz_fire_on_move).
    /// </summary>
    public static partial class OpenZParity
    {
        /// <summary>DV57 probe: a red `type` robot (squad of 1) walks 16 tiles east along row 14 of the empty L02 test map, past an
        /// unkillable, harmless blue grunt 3 tiles off its path. Returns shots fired while it moved, hits among them, its arrival time
        /// and the grenades it threw.</summary>
        static (int shots, int hits, int movingShots, double arrive, int nades, double chance) WalkProbe(string type, bool on)
        {
            bool saved = ZWorld.FireOnMoveDeviation; ZWorld.FireOnMoveDeviation = on;
            try
            {
                var w = Legacy(57);
                var a = w.CreateRobotGroup(type, 46 * 16, 14 * 16, Team.Red, 1);
                a.MaxHealth = a.Health = 1000000000;
                Dummy(w, "robot", "grunt", 54, 17, Team.Blue);
                w.SendWaypoints(Team.Red, a, new[] { ZWaypoint.Move(62 * 16 + 8, 14 * 16 + 8) });
                int shots = 0, hits = 0, moving = 0, nades = 0; double arrive = -1; bool started = false;
                for (int i = 0; i < 3000 && w.Time < 40; i++)
                {
                    w.Tick(ZConst.TICK);
                    if (a.IsMoving) started = true;
                    foreach (var e in w.Events)
                    {
                        if (e.objId != a.Id) continue;
                        if (e.type == ZEventType.Hitscan) { shots++; if (e.flag) hits++; if (a.IsMoving) moving++; }
                        else if (e.type == ZEventType.MissileFired) { shots++; if (a.IsMoving) moving++; if (e.flag) nades++; }
                    }
                    w.Events.Clear();
                    if (started && a.Waypoints.Count == 0 && !a.IsMoving) { arrive = w.Time; break; }
                }
                return (shots, hits, moving, arrive, nades, a.DamageChance);
            }
            finally { ZWorld.FireOnMoveDeviation = saved; }
        }

        static Deviation DevFireOnMove(Func<string, int, int> getInt)
        {
            var gOn = WalkProbe("grunt", true); var gOff = WalkProbe("grunt", false);
            var pOn = WalkProbe("psycho", true); var lOn = WalkProbe("laser", true);
            var tOn = WalkProbe("tough", true); var sOn = WalkProbe("sniper", true); var yOn = WalkProbe("pyro", true);
            var gOn2 = WalkProbe("grunt", true);
            string F(double v) => v.ToString("0.00", CultureInfo.InvariantCulture);
            string R(int h, int s) => s > 0 ? ((double)h / s).ToString("0%", CultureInfo.InvariantCulture) : "-";
            bool ok = gOn.movingShots >= 5 && pOn.movingShots >= 5 && lOn.movingShots >= 3 && gOff.movingShots == 0
                      && tOn.movingShots == 0 && sOn.movingShots == 0 && yOn.movingShots == 0 && gOn.nades == 0
                      && gOn.arrive > 0 && Math.Abs(gOn.arrive - gOff.arrive) < 0.05
                      && gOn2.shots == gOn.shots && gOn2.hits == gOn.hits;
            bool pref = getInt("oz_fire_on_move", 1) == 1 && getInt("oz_zod_exact", 0) == 0;
            return new Deviation
            {
                id = "DV57", change = "Fire on the move: grunt, psycho and laser robots shoot while walking (lower accuracy); tough, pyro and sniper stand to fire",
                zod = "robots never attack while moving: ProcessMove disengages every step (zobject.cpp:3519) and CheckPassiveEngage skips a moving robot, so a walking squad ignores the enemy it passes",
                openz = $"a walking grunt / psycho / laser (leaders and squad members, any walk but a fort rush or a repair trip) keeps its target and engages one in range on the 0.5 s passive check (engage only: the order goes on, no agro / grab waypoint); shots while walking: the gun only (no grenade), hit chance x{ZWorld.WalkHitFactor:0.0} of Zod's damage_chance, Zod's damage / rate / range, a target out of range is dropped before the shot. Tough, pyro, sniper and the Electrobot keep Zod. View: the torso twists to the target over the walking legs (UnitAim, <= 80 deg, else strafe / back-pedal), shot FX + sound from the Hitscan event",
                measured = $"L02 test map, a robot walks 16 tiles past a harmless blue grunt 3 tiles off its path: grunt {gOn.movingShots} shots while walking, {gOn.hits} hits ({R(gOn.hits, gOn.shots)}, standing chance {F(gOn.chance)}), grenades {gOn.nades}, arrived {F(gOn.arrive)} s; Zod exact: {gOff.movingShots} shots while walking, arrived {F(gOff.arrive)} s; psycho {pOn.movingShots} ({R(pOn.hits, pOn.shots)}, chance {F(pOn.chance)}); laser {lOn.movingShots} ({R(lOn.hits, lOn.shots)}, chance {F(lOn.chance)}); tough {tOn.movingShots}, sniper {sOn.movingShots}, pyro {yOn.movingShots} (they stand); two grunt runs identical {gOn2.shots == gOn.shots && gOn2.hits == gOn.hits}",
                toggle = $"ZWorld.FireOnMoveDeviation (PlayerPrefs oz_fire_on_move = {(getInt("oz_fire_on_move", 1) == 1 ? "1 (on, default)" : "0 (off)")}; menu OpenZ/Deviations/Fire On The Move; off in 'Zod exact' oz_zod_exact 1 and in every Zod row)",
                status = !ok ? DevCheck : pref ? DevActive : DevActive + " (off by pref)",
            };
        }

#if !OPENZ_HARNESS
        const string MenuFireOnMove = "OpenZ/Deviations/Fire On The Move (oz_fire_on_move)";
        [MenuItem(MenuFireOnMove)] static void TFireOnMove() => NbToggle("oz_fire_on_move", 1, "grunt / psycho / laser fire while walking (DV57)");
        [MenuItem(MenuFireOnMove, true)] static bool TFireOnMoveC() { Menu.SetChecked(MenuFireOnMove, PlayerPrefs.GetInt("oz_fire_on_move", 1) == 1); return true; }

        [MenuItem("OpenZ/Deviations/Probe DV57 Fire On The Move")]
        static void ProbeFireOnMoveMenu()
        {
            Func<string, int, int> get = (k, def) => PlayerPrefs.GetInt(k, def);
            var d = DevFireOnMove(get);
            var path = System.IO.Path.Combine(OpenZPaths.Records, "fire_on_move_dv57.md");
            System.IO.File.WriteAllText(path, $"# DV57 fire on the move probe {DateTime.Now:yyyy-MM-dd HH:mm}\n\n## DV57 {d.status}\n\n- {d.measured.Replace("; ", "\n- ")}\n");
            Debug.Log($"[DV57] {d.status}: {d.measured}\n-> {path}");
        }
#endif
    }
}
