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
    /// Zod (zobject.cpp:2102-2213, ProcessAttackDamage): a robot whose squad carries
    /// grenades throws one at whatever it attacks, robots included. Measured headless on L02 open ground (bots removed): per robot
    /// type, a red squad carrying 6 grenades attacks (a) a blue grunt squad and (b) a crewed blue light tank for 20 s (targets
    /// unarmed so the fight runs its length), once with DV40 and once Zod exact: grenades thrown, weapon shots, grenade damage.
    /// </summary>
    public static partial class OpenZParity
    {
        public static readonly string[] GrenadeTypes = { "grunt", "psycho", "sniper", "tough", "pyro", "laser" };

        /// <summary>One short bot-vs-bot duel: (grenades thrown at the target, other shots at it, grenade damage per throw).</summary>
        public static (int nades, int shots, int dmg) GrenadeProbe(string type, bool vsVehicle, bool dev)
        {
            bool keep = ZWorld.GrenadeRuleDeviation; ZWorld.GrenadeRuleDeviation = dev;
            try
            {
                var w = ZWorld.Create(SettingsText, MapText("L02"), 7);
                w.Bots.Clear();
                foreach (var o in w.Objects.ToList())
                    if (o.IsUnit && !o.Destroyed && Math.Abs(o.CenterX / 16 - 44) < 16 && Math.Abs(o.CenterY / 16 - 19) < 12) { o.SetHealth(0); w.UpdateObjectHealth(o); }
                w.Tick(ZConst.TICK); w.Events.Clear();
                var red = w.CreateRobotGroup(type, 40 * 16, 19 * 16, Team.Red);
                red.Grenades = 6;
                ZObject target;
                if (vsVehicle) target = w.CreateObject("vehicle", "light", 46 * 16, 19 * 16, Team.Blue);
                else target = w.CreateRobotGroup("grunt", 46 * 16, 19 * 16, Team.Blue);
                var blues = new List<ZObject> { target }; blues.AddRange(target.Minions);
                foreach (var b in blues) { b.Damage = 0; b.MaxHealth = 1000000; b.SetHealth(b.MaxHealth); }
                w.SendWaypoints(Team.Red, red, new List<ZWaypoint> { new ZWaypoint { mode = WpMode.Attack, refId = target.Id, x = target.CenterX, y = target.CenterY } });
                var redIds = new HashSet<int> { red.Id }; foreach (var m in red.Minions) redIds.Add(m.Id);
                var blueIds = new HashSet<int>(blues.Select(b => b.Id));
                int nades = 0, shots = 0, dmg = 0;
                while (w.Time < 20)
                {
                    w.Tick(ZConst.TICK);
                    foreach (var e in w.Events)
                    {
                        if (!redIds.Contains(e.objId) || !blueIds.Contains(e.otherId)) continue;
                        if (e.type == ZEventType.MissileFired && e.flag) nades++;
                        else if (e.type == ZEventType.MissileFired || e.type == ZEventType.Hitscan) shots++;
                    }
                    w.Events.Clear();
                }
                foreach (var m in w.PendingMissiles) if (m.grenade) { dmg = m.damage; break; }
                if (dmg == 0 && nades > 0) dmg = (int)(w.S.grenade_damage * ZConst.MAX_UNIT_HEALTH);
                return (nades, shots, dmg);
            }
            finally { ZWorld.GrenadeRuleDeviation = keep; }
        }

        static Deviation DevGrenadeRule(Func<string, int, int> getInt)
        {
            var sb = new System.Text.StringBuilder(); bool ok = true;
            foreach (var t in GrenadeTypes)
            {
                var rob = GrenadeProbe(t, false, true); var veh = GrenadeProbe(t, true, true); var zod = GrenadeProbe(t, false, false);
                bool pass = (t == "grunt" ? rob.nades > 0 : rob.nades == 0 && rob.shots > 0) && veh.nades > 0 && zod.nades > 0;
                ok &= pass;
                sb.Append($"{t}: vs robots {rob.nades} grenades / {rob.shots} shots, vs light tank {veh.nades} grenades, Zod vs robots {zod.nades} grenades{(pass ? "" : " FAIL")}; ");
            }
            bool pref = getInt("oz_grenade_rule", 1) == 1 && getInt("oz_zod_exact", 0) == 0;
            return new Deviation
            {
                id = "DV40", change = "Grenades: every robot type but the grunt fights robots with its own weapon and throws grenades only at vehicles, guns and buildings",
                zod = "a robot whose squad carries grenades throws one at whatever it attacks, robots included (zobject.cpp:2102-2213 ProcessAttackDamage)",
                openz = "psycho, sniper, tough, pyro and laser keep their weapon against robots and throw only at vehicles, cannons, buildings, rocks; grunts throw at anything as in Zod; grenade damage, radius, throw speed and ammo stay Zod's",
                measured = "L02 headless duels (20 s, 6 grenades, unarmed targets): " + sb,
                toggle = $"PlayerPrefs oz_grenade_rule = {(getInt("oz_grenade_rule", 1) == 1 ? "1 (on, default)" : "0 (off)")}; off in 'Zod exact'",
                status = !ok ? DevCheck : pref ? DevActive : DevActive + " (off by pref)",
            };
        }

#if !OPENZ_HARNESS
        [MenuItem("OpenZ/Deviations/Probe DV40 Grenade Rule")]
        static void ProbeGrenadeMenu()
        {
            var d = DevGrenadeRule((k, def) => PlayerPrefs.GetInt(k, def));
            var path = System.IO.Path.GetFullPath(System.IO.Path.Combine(Application.dataPath, "../../../../work/records/grenade_rule_dv40.md"));
            System.IO.File.WriteAllText(path, $"# DV40 grenade rule probe {DateTime.Now:yyyy-MM-dd HH:mm}\n\n{d.status}\n\n{d.measured.Replace("; ", "\n- ")}\n");
            Debug.Log($"[DV40] {d.status}: {d.measured}");
        }
#endif
    }
}
