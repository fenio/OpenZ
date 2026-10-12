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
    /// The Deviations rows DV51 (no shot while knocked down) and DV52 (Tough / missile cannon rockets a bit slower;
    /// Sim/ZObject.CombatFeel.cs), measured headless in the same sim as the parity rows (both switches off in every Zod row).
    /// Menus: OpenZ/Deviations/Knock Hold (oz_knock_hold), OpenZ/Deviations/Rocket Speed (oz_rocket_speed).
    /// </summary>
    public static partial class OpenZParity
    {
        static void AddCombatFeelDeviations(List<Deviation> list, Func<string, int, int> getInt)
        {
            try { list.Add(DevKnockHold(getInt)); }
            catch (Exception ex) { list.Add(new Deviation { id = "DV51", change = "DevKnockHold", zod = "-", openz = "-", measured = "probe threw " + ex.GetType().Name + ": " + ex.Message, toggle = "-", status = DevCheck }); }
            try { list.Add(DevRocketSpeed(getInt)); }
            catch (Exception ex) { list.Add(new Deviation { id = "DV52", change = "DevRocketSpeed", zod = "-", openz = "-", measured = "probe threw " + ex.GetType().Name + ": " + ex.Message, toggle = "-", status = DevCheck }); }
        }

        /// <summary>DV51 probe: an unkillable red Tough fires at a harmless blue grunt on the empty L02 test map; after its 2nd rocket a blue
        /// heavy tank's shell (injected, 60 px splash) bursts `offPx` from it (grenade = a grenade blast instead). Returns the knock window,
        /// rockets fired inside it, the first rocket after it, the rockets' normal cycle and the knock count.</summary>
        static (double knockAt, double knockUntil, int inWindow, double firstAfter, double cycle, int knocks, int held) KnockProbe(bool on, int offPx, bool grenade)
        {
            bool saved = ZWorld.KnockHoldDeviation; ZWorld.KnockHoldDeviation = on;
            double savedChance = ZWorld.KnockChanceForTest; ZWorld.KnockChanceForTest = 1;
            try
            {
                var w = Legacy(31);
                var a = w.CreateRobotGroup("tough", 56 * 16, 14 * 16, Team.Red, 1);
                a.MaxHealth = a.Health = 1000000000;
                Dummy(w, "robot", "grunt", 51, 14, Team.Blue);
                var heavy = Dummy(w, "vehicle", "heavy", 20, 40, Team.Blue);                          // the shell's owner; far away (blue never eliminated)
                var rockets = new List<double>(); double blast = -1, until = -1, lastKnock = -1; bool injected = false; int aKnocks = 0;
                for (int i = 0; i < 1500 && w.Time < 20; i++)
                {
                    w.Tick(ZConst.TICK);
                    foreach (var e in w.Events) if (e.objId == a.Id && e.type == ZEventType.MissileFired) rockets.Add(w.Time);
                    w.Events.Clear();
                    if (!injected && rockets.Count == 2 && w.Time > rockets[1] + 0.2)
                    {
                        var m = new ZMissile { team = Team.Blue, attackerId = heavy.Id, targetId = a.Id, x = a.CenterX + offPx, y = a.CenterY, damage = 50, radius = 60, grenade = grenade };
                        m.CalcExplodeTime(m.x, m.y, 1000, w.Time); w.AddMissileForTest(m); injected = true;
                    }
                    if (a.KnockAt >= 0 && a.KnockAt != lastKnock) { lastKnock = a.KnockAt; aKnocks++; }   // the Tough's own knockdowns (its rockets knock the grunt too)
                    if (injected && blast < 0 && a.KnockAt >= 0) { blast = a.KnockAt; until = a.KnockUntil; }
                    if (injected && rockets.Count >= 6) break;
                }
                double cycle = rockets.Count >= 2 ? rockets[1] - rockets[0] : -1;
                double b0 = blast >= 0 ? blast : rockets.Count > 1 ? rockets[1] + 0.2 : -1, b1 = until >= 0 ? until : b0 + ZWorld.KnockBaseSec;
                int inside = rockets.Count(t => t > b0 + 1e-9 && t < b1 - 1e-9);
                double after = rockets.Where(t => t >= b1 - 1e-9).DefaultIfEmpty(-1).First();
                return (blast, until, inside, after, cycle, aKnocks, a.KnockHeldTicks);
            }
            finally { ZWorld.KnockHoldDeviation = saved; ZWorld.KnockChanceForTest = savedChance; }
        }

        /// <summary>
        /// red grunt, 2.6 s apart (it is always up again); returns knockdowns, rolls, never-twice skips and the most knockdowns in a row.</summary>
        static (int knocks, int rolls, int skips, int maxRow) KnockChanceProbe(string shooter, int n)
        {
            bool saved = ZWorld.KnockHoldDeviation; ZWorld.KnockHoldDeviation = true;
            try
            {
                var w = Legacy(33);
                var g = Dummy(w, "robot", "grunt", 56, 14, Team.Red);
                var src = Dummy(w, shooter == "howitzer" ? "cannon" : "vehicle", shooter, 20, 40, Team.Blue);
                int knocks = 0, row = 0, maxRow = 0; double last = -1;
                for (int k = 0; k < n; k++)
                {
                    var m = new ZMissile { team = Team.Blue, attackerId = src.Id, targetId = g.Id, x = g.CenterX + 4, y = g.CenterY, damage = 1, radius = 60 };
                    m.CalcExplodeTime(m.x, m.y, 1000, w.Time); w.AddMissileForTest(m);
                    double t1 = w.Time + 2.6;
                    while (w.Time < t1) { w.Tick(ZConst.TICK); w.Events.Clear(); }
                    if (g.KnockAt != last && g.KnockAt >= 0) { last = g.KnockAt; knocks++; row++; if (row > maxRow) maxRow = row; } else row = 0;
                }
                return (knocks, w.KnockRolls, w.KnockSkips, maxRow);
            }
            finally { ZWorld.KnockHoldDeviation = saved; }
        }

        static Deviation DevKnockHold(Func<string, int, int> getInt)
        {
            var on = KnockProbe(true, 8, false); var on2 = KnockProbe(true, 8, false); var off = KnockProbe(false, 8, false);
            var far = KnockProbe(true, 50, false); var nade = KnockProbe(true, 8, true);
            var tank = KnockChanceProbe("heavy", 120); var how = KnockChanceProbe("howitzer", 120);
            double pT = tank.rolls > 0 ? (double)tank.knocks / tank.rolls : 0, pH = how.rolls > 0 ? (double)how.knocks / how.rolls : 0;
            bool chanceOk = pT >= 0.10 && pT <= 0.32 && pH >= 0.45 && pH <= 0.75 && tank.maxRow == 1 && how.maxRow == 1;
            double tick = ZConst.TICK + 1e-6, len = on.knockUntil - on.knockAt;
            bool ok = on.knockAt >= 0 && len >= ZWorld.KnockBaseSec - tick && len <= ZWorld.KnockBaseSec + ZWorld.KnockSpreadSec + tick && on.inWindow == 0
                      && on.firstAfter >= on.knockUntil - tick && on.held > 0 && off.knockAt < 0 && off.inWindow >= 1
                      && far.knocks == 0 && nade.knocks == 0 && on2.knockUntil == on.knockUntil && on2.firstAfter == on.firstAfter && chanceOk;
            bool pref = getInt("oz_knock_hold", 1) == 1 && getInt("oz_zod_exact", 0) == 0;
            string S(double s) => s < 0 ? "never" : s.ToString("0.00", CultureInfo.InvariantCulture) + " s";
            return new Deviation
            {
                id = "DV51", change = "No shot while knocked down: a robot blown off its feet by a heavy blast gets up before it fires again",
                zod = "no blast reaction: a robot hit by splash keeps firing on its cooldown (zobject.cpp:2102-2213; zserver.cpp:1080-1110 only removes health)",
                openz = $"only a strong DIRECT hit can knock a robot down: a rocket / tank / cannon / howitzer / MLRS / missile cannon / fort gun blast centred on it (inside {ZWorld.KnockDirectFrac:0.00} of the radius, or the shell aimed at it inside {ZWorld.KnockTargetFrac:0.00}; never a grenade or turret debris, never the fort defender), then a hashed roll: {ZWorld.KnockChanceHowitzer:0%} for the howitzer and the fort's gun, {ZWorld.KnockChance:0%} for all others, and never twice in a row (a robot that was knocked down skips the roll on its next such hit); every other hit is a view-only flinch that does not stop it or its attack timer. A knocked robot is down for {ZWorld.KnockBaseSec:0.0}-{ZWorld.KnockBaseSec + ZWorld.KnockSpreadSec:0.0} s (hashed from the robot and the blast, no rand() draw; already down = no refresh; at most {ZWorld.KnockMaxDown} down at once): its attack waits (no cooldown used), then it aims and fires as usual (a Tough prepares its bazooka again, DV11). The view (DV19) plays the fall / lie / get-up to end on that time. All robot types, the Electrobot too",
                measured = $"L02 test map, an unkillable Tough firing at a grunt, a heavy-tank shell 8 px away after its 2nd rocket: down {S(on.knockAt)} -> {S(on.knockUntil)} ({S(len)}), rockets inside {on.inWindow}, first rocket after it at {S(on.firstAfter)} (+{S(on.firstAfter - on.knockUntil)}: the DV11 prepare), {on.held} attack ticks held; Zod exact: rockets inside the same window {off.inWindow} (cycle {S(off.cycle)}); (roll forced to 1 for this window test) shell 50 px away (outside {ZWorld.KnockDirectFrac:0.00} x 60 px): {far.knocks} knockdowns of the Tough; grenade 8 px away: {nade.knocks}; two runs identical {on2.knockUntil == on.knockUntil && on2.firstAfter == on.firstAfter}; 120 direct heavy-tank shells on a grunt: {tank.knocks} knockdowns / {tank.rolls} rolls ({pT:0%}), {tank.skips} skipped (never twice), most in a row {tank.maxRow}; 120 direct howitzer shells: {how.knocks} / {how.rolls} ({pH:0%}), {how.skips} skipped, most in a row {how.maxRow}",
                toggle = $"ZWorld.KnockHoldDeviation (PlayerPrefs oz_knock_hold = {(getInt("oz_knock_hold", 1) == 1 ? "1 (on, default)" : "0 (off)")}; menu OpenZ/Deviations/Knock Hold; off in 'Zod exact' oz_zod_exact 1 and in every Zod row)",
                status = !ok ? DevCheck : pref ? DevActive : DevActive + " (off by pref)",
            };
        }

        /// <summary>DV52 probe: the missile speeds of the rocket units and one Tough rocket's flight time at a known distance.</summary>
        static (int tough, int mc, int mlrs, int mech, int apcTough, double flight, double dist) RocketProbe(bool on)
        {
            bool saved = ZWorld.RocketSpeedDeviation; ZWorld.RocketSpeedDeviation = on;
            try
            {
                var w = Legacy(32);
                var a = w.CreateRobotGroup("tough", 56 * 16, 14 * 16, Team.Red, 1);
                var t = Dummy(w, "robot", "grunt", 51, 14, Team.Blue);
                var mc = w.CreateObject("cannon", "missile_cannon", 40 * 16, 30 * 16, Team.Neutral);
                var ml = w.CreateObject("vehicle", "missile_launcher", 44 * 16, 30 * 16, Team.Neutral);
                var me = w.CreateObject("vehicle", "medium", 48 * 16, 30 * 16, Team.Neutral);
                var apc = w.CreateObject("vehicle", "apc", 52 * 16, 30 * 16, Team.Red);
                apc.Drivers.Clear(); apc.DriverType = "tough"; apc.Drivers.Add(1081); apc.ResetDamageInfo();
                double flight = -1, dist = -1;
                for (int i = 0; i < 600 && flight < 0; i++)
                {
                    w.Tick(ZConst.TICK);
                    foreach (var e in w.Events)
                        if (e.objId == a.Id && e.type == ZEventType.MissileFired && flight < 0) { flight = e.value; dist = Math.Sqrt((double)(e.x - a.CenterX) * (e.x - a.CenterX) + (double)(e.y - a.CenterY) * (e.y - a.CenterY)); }
                    w.Events.Clear();
                }
                return (a.MissileSpeed, mc.MissileSpeed, ml.MissileSpeed, me.MissileSpeed, apc.MissileSpeed, flight, dist);
            }
            finally { ZWorld.RocketSpeedDeviation = saved; }
        }

        static Deviation DevRocketSpeed(Func<string, int, int> getInt)
        {
            var on = RocketProbe(true); var off = RocketProbe(false);
            var s = ZSettings.Parse(SettingsText);
            int zt = s.Unit("tough").attack_missile_speed, zc = s.Unit("missile_cannon").attack_missile_speed, zl = s.Unit("missile_launcher").attack_missile_speed, zm = s.Unit("medium").attack_missile_speed;
            int wt = (int)Math.Round(zt * ZWorld.RocketSpeedFactor), wc = (int)Math.Round(zc * ZWorld.RocketSpeedFactor);
            bool ok = on.tough == wt && on.mc == wc && on.mlrs == zl && on.mech == zm && on.apcTough == wt && off.tough == zt && off.mc == zc && off.apcTough == zt
                      && on.flight > 0 && Math.Abs(on.flight - on.dist / wt) < 1e-4 && off.flight > 0 && Math.Abs(off.flight - off.dist / zt) < 1e-4;
            bool pref = getInt("oz_rocket_speed", 1) == 1 && getInt("oz_zod_exact", 0) == 0;
            string F(double v) => v.ToString("0.000", CultureInfo.InvariantCulture);
            return new Deviation
            {
                id = "DV52", change = "Tough and missile cannon rockets a bit slower",
                zod = $"attack_missile_speed (default_settings.txt, the live numbers = Zod's): tough {zt}, missile_cannon {zc}, missile_launcher {zl}, medium {zm} px/s; flight = distance / speed (zobject.cpp:2173)",
                openz = $"x{ZWorld.RocketSpeedFactor:0.00}: the Tough's bazooka {wt} px/s (also an APC's Tough crew) and the missile cannon {wc} px/s; MLRS and the mech's rockets keep Zod's; lead, scatter, damage, splash and the target's dodge use the slower speed like any Zod missile. View (not in this row): the Tough, MLRS and missile cannon rockets ease out of the tube (60 % speed, smooth ramp) and land on the sim's blast (OpenZFxPlayer.RocketEasing)",
                measured = $"speeds on: tough {on.tough}, missile_cannon {on.mc}, missile_launcher {on.mlrs}, medium {on.mech}, APC with a Tough crew {on.apcTough} (off: {off.tough} / {off.mc} / {off.mlrs} / {off.mech} / {off.apcTough}); a Tough rocket over {F(on.dist)} px flies {F(on.flight)} s ({F(on.dist / Math.Max(1e-9, on.flight))} px/s), Zod exact {F(off.flight)} s over {F(off.dist)} px",
                toggle = $"ZWorld.RocketSpeedDeviation (PlayerPrefs oz_rocket_speed = {(getInt("oz_rocket_speed", 1) == 1 ? "1 (on, default)" : "0 (off)")}; menu OpenZ/Deviations/Rocket Speed; off in 'Zod exact' oz_zod_exact 1, in every Zod row and in DV5's stat table)",
                status = !ok ? DevCheck : pref ? DevActive : DevActive + " (off by pref)",
            };
        }

#if !OPENZ_HARNESS
        const string MenuKnock = "OpenZ/Deviations/Knock Hold (oz_knock_hold)", MenuRocketSpeed = "OpenZ/Deviations/Rocket Speed (oz_rocket_speed)";
        [MenuItem(MenuKnock)] static void TKnock() => NbToggle("oz_knock_hold", 1, "no shot while knocked down (DV51)");
        [MenuItem(MenuKnock, true)] static bool TKnockC() { Menu.SetChecked(MenuKnock, PlayerPrefs.GetInt("oz_knock_hold", 1) == 1); return true; }
        [MenuItem(MenuRocketSpeed)] static void TRocketSpeed() => NbToggle("oz_rocket_speed", 1, "Tough / missile cannon rockets x0.88 (DV52)");
        [MenuItem(MenuRocketSpeed, true)] static bool TRocketSpeedC() { Menu.SetChecked(MenuRocketSpeed, PlayerPrefs.GetInt("oz_rocket_speed", 1) == 1); return true; }

        [MenuItem("OpenZ/Deviations/Probe DV51 DV52 Combat Feel")]
        static void ProbeCombatFeelMenu()
        {
            Func<string, int, int> get = (k, def) => PlayerPrefs.GetInt(k, def);
            var d51 = DevKnockHold(get); var d52 = DevRocketSpeed(get);
            var path = System.IO.Path.Combine(OpenZPaths.Records, "combat_feel_dv51_dv52.md");
            System.IO.File.WriteAllText(path, $"# DV51 / DV52 combat feel probe {DateTime.Now:yyyy-MM-dd HH:mm}\n\n## DV51 {d51.status}\n\n- {d51.measured.Replace("; ", "\n- ")}\n\n## DV52 {d52.status}\n\n- {d52.measured.Replace("; ", "\n- ")}\n");
            Debug.Log($"[DV51] {d51.status}: {d51.measured}\n[DV52] {d52.status}: {d52.measured}\n-> {path}");
        }
#endif
    }
}
