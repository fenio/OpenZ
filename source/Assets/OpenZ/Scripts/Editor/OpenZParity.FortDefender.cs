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
    /// the same sim as the parity rows: L02's red fort, every map unit removed, cannons neutral, the DV7 fort gun off (it would shoot the
    /// probe's units), every other switch as the build has it. A blue heavy tank (unkillable; its splash cut to 24 px so it never reaches a
    /// ring and the numbers measure the defender alone) shells the fort from the east; an unarmed unkillable blue grunt 10 m beyond the
    /// ring is his target for 20 s; a blue grunt squad then kills him; the heavy keeps shelling until the next one is up; the heavy goes
    /// runs the full checklist (orders, selection, unit cap, flags, win rule, the CPU, ring changes, carried health).
    /// Menus: OpenZ/Deviations/Fort Defender (oz_fort_defender) and OpenZ/Deviations/Probe DV41 Fort Defender.
    /// </summary>
    public static partial class OpenZParity
    {
        public struct FortDefenderResult
        {
            public double firstHit, created, up, killedAfter, respawnAfter, goneAfterCalm;
            public int ring, shots, hits, damagePerHit, maxAlive, offSpawns, unitCountDelta, blastOutside, blastInside;
            public double gapMin, gapMax;
            public bool eastRing, selectable, ordered, enemiesTargeted;
        }

        /// <summary>DV41 probe on one map's red fort (see the class summary). Deterministic (seeded world, fixed tick).</summary>
        public static FortDefenderResult FortDefenderProbe(string lv = "L02")
        {
            var r = new FortDefenderResult { firstHit = -1, created = -1, up = -1, killedAfter = -1, respawnAfter = -1, goneAfterCalm = -1, ring = -1 };
            bool fg = ZWorld.FortGunDeviation, fd = ZWorld.FortDefenderDeviation; ZWorld.FortGunDeviation = false; ZWorld.FortDefenderDeviation = true;
            try
            {
                var w = ZWorld.Create(SettingsText, MapText(lv), 5, lv == "L17");
                BnClear(w, Team.Red, Team.Blue);
                var fort = NbFort(w, Team.Red);
                int units0 = w.UnitCount(Team.Red);
                ZObject Heavy(int dx)
                {
                    if (!BnSpot(w, (fort.CenterX >> 4) + dx, fort.CenterY >> 4, false, out int x, out int y)) return null;
                    var h = w.CreateObject("vehicle", "heavy", x, y, Team.Blue);
                    h.MaxHealth = h.Health = 1000000000; h.DamageRadius = 24;
                    w.SendWaypoints(Team.Blue, h, new[] { new ZWaypoint { mode = WpMode.Attack, refId = fort.Id, x = fort.CenterX, y = fort.CenterY } });
                    return h;
                }
                int Alive() => w.Objects.Count(o => o.IsFortDefender && o.DefenderFortId == fort.Id && !o.Destroyed && o.KillAt < 0);
                var east = Heavy(10);
                ZObject man = null;
                while (w.Time < 40 && r.up < 0)
                {
                    w.Tick(ZConst.TICK);
                    foreach (var e in w.Events) if (e.type == ZEventType.Created && e.otherId == fort.Id) { man = w.Get(e.objId); r.created = w.Time; r.ring = fort.FortDefenderRing; }
                    w.Events.Clear();
                    if (r.firstHit < 0 && fort.LastEnemyHit > 0) r.firstHit = fort.LastEnemyHit;
                    if (man != null && fort.FortDefenderState == FortDefenderPhase.Up) r.up = w.Time;
                    r.maxAlive = Math.Max(r.maxAlive, Alive());
                }
                if (man == null) return r;
                fort.FortDefenderRingCentre(r.ring, out int rx, out int ry);
                r.eastRing = rx > fort.CenterX;
                r.selectable = man.Selectable || w.OwnedSelectable(Team.Red).Contains(man);
                r.ordered = w.SendWaypoints(Team.Red, man, new[] { ZWaypoint.Move(man.CenterX + 64, man.CenterY) }) || man.Waypoints.Count > 0;
                r.unitCountDelta = w.UnitCount(Team.Red) - units0;
                // the nest's cover: a blue 1000 HP / 50 px blast 30 px from him (outside the nest), then one 6 px from him (inside it)
                foreach (var (d, inside) in new[] { (30, false), (6, true) })
                {
                    int hp0 = man.Health;
                    w.AddMissileForTest(new ZMissile { team = Team.Blue, attackerId = -1, targetId = man.Id, damage = 1000, radius = 50, x = man.CenterX + d, y = man.CenterY, explodeTime = w.Time + 0.02 });
                    for (int k = 0; k < 4; k++) { w.Tick(ZConst.TICK); w.Events.Clear(); }
                    if (inside) r.blastInside = hp0 - man.Health; else r.blastOutside = hp0 - man.Health;
                    if (man.Destroyed) break;
                    man.SetHealth(man.MaxHealth);
                }
                // his target: unarmed, unkillable, 80 px beyond the ring
                BnSpot(w, (rx + 80) >> 4, ry >> 4, true, out int tx, out int ty);
                var dummy = NbDummy(w, "robot", "grunt", tx, ty, Team.Blue);
                var times = new List<double>(); int hp = dummy.Health; double t0 = w.Time;
                while (w.Time < t0 + 20)
                {
                    w.Tick(ZConst.TICK);
                    foreach (var e in w.Events)
                        if (e.type == ZEventType.Hitscan && e.objId == man.Id) { r.shots++; times.Add(w.Time); if (e.flag) { r.hits++; r.damagePerHit = hp - dummy.Health; } hp = dummy.Health; }
                    w.Events.Clear();
                    r.maxAlive = Math.Max(r.maxAlive, Alive());
                }
                var gaps = times.Zip(times.Skip(1), (a, b) => b - a).ToList();
                r.gapMin = gaps.Count > 0 ? gaps.Min() : -1; r.gapMax = gaps.Count > 0 ? gaps.Max() : -1;
                dummy.SetHealth(0); w.UpdateObjectHealth(dummy);
                // an armed blue grunt squad beyond the ring kills him
                BnSpot(w, (rx + 72) >> 4, (ry + 16) >> 4, true, out int kx, out int ky);
                var squad = w.CreateRobotGroup("grunt", kx, ky, Team.Blue);
                var sq = new List<ZObject>(squad.Minions) { squad }; foreach (var m in sq) m.MaxHealth = m.Health = 1000000000;
                double tKill = -1; t0 = w.Time;
                while (w.Time < t0 + 120 && tKill < 0)
                {
                    w.Tick(ZConst.TICK);
                    if (sq.Any(m => m.AttackObject == man)) r.enemiesTargeted = true;
                    foreach (var e in w.Events) if (e.type == ZEventType.Death && e.objId == man.Id) tKill = w.Time;
                    w.Events.Clear();
                    r.maxAlive = Math.Max(r.maxAlive, Alive());
                }
                if (tKill >= 0) r.killedAfter = tKill - t0;
                foreach (var m in sq) if (!m.Destroyed) { m.SetHealth(0); w.UpdateObjectHealth(m); }
                // the next one after the cooldown (the heavy keeps shelling)
                t0 = w.Time;
                while (tKill >= 0 && w.Time < t0 + 40 && r.respawnAfter < 0)
                {
                    w.Tick(ZConst.TICK);
                    foreach (var e in w.Events) if (e.type == ZEventType.Created && e.otherId == fort.Id) r.respawnAfter = w.Time - tKill;
                    w.Events.Clear();
                    r.maxAlive = Math.Max(r.maxAlive, Alive());
                }
                // the attack ends: he goes down when the calm has passed (last hit: shells in the air still land)
                while (w.Time < t0 + 60 && fort.FortDefenderState != FortDefenderPhase.Up) { w.Tick(ZConst.TICK); w.Events.Clear(); }
                var man2 = fort.FortDefenderMan;
                east.SetHealth(0); w.UpdateObjectHealth(east);
                double gone = -1; t0 = w.Time;
                while (man2 != null && w.Time < t0 + 40 && gone < 0) { w.Tick(ZConst.TICK); w.Events.Clear(); if (w.Get(man2.Id) == null) gone = w.Time; r.maxAlive = Math.Max(r.maxAlive, Alive()); }
                if (gone >= 0) r.goneAfterCalm = gone - fort.LastEnemyHit;
                // DV41 off: the same attack sends nobody up
                ZWorld.FortDefenderDeviation = false;
                var w2 = ZWorld.Create(SettingsText, MapText(lv), 5, lv == "L17"); BnClear(w2, Team.Red, Team.Blue);
                var f2 = NbFort(w2, Team.Red);
                if (BnSpot(w2, (f2.CenterX >> 4) + 10, f2.CenterY >> 4, false, out int x2, out int y2))
                {
                    var h2 = w2.CreateObject("vehicle", "heavy", x2, y2, Team.Blue); h2.MaxHealth = h2.Health = 1000000000;
                    w2.SendWaypoints(Team.Blue, h2, new[] { new ZWaypoint { mode = WpMode.Attack, refId = f2.Id, x = f2.CenterX, y = f2.CenterY } });
                }
                while (w2.Time < 30) { w2.Tick(ZConst.TICK); foreach (var e in w2.Events) if (e.type == ZEventType.Created && e.otherId == f2.Id) r.offSpawns++; w2.Events.Clear(); }
                return r;
            }
            finally { ZWorld.FortGunDeviation = fg; ZWorld.FortDefenderDeviation = fd; }
        }

        /// <summary>
        /// (zserver.cpp:1091-1098: float mag, float / int ratio = float, then 1.0 - ratio and the product in double; x86-64 SSE, IEEE
        /// single): 30 px -> (int)(1000 x (1 - 0.6f)) = 399, 6 px -> 880. The DV41 nest (a design value of this deviation, 1/4 through
        /// the sandbags) makes 399 / 4 = 99 outside it. A literal, never the sim's own formula, so a rounding change in the sim shows up
        /// here (round 1 computed the expectation with the sim's formula and could not catch the 400 the editor's JIT produced).</summary>
        const int ZodSplash1000r50at30 = 399, ZodSplash1000r50at6 = 880, DefenderBlastOutside = 99;

        static Deviation DevFortDefender(Func<string, int, int> getInt)
        {
            var r = FortDefenderProbe("L02");
            var g = ZSettings.Parse(SettingsText).Unit("grunt");
            bool ok = r.created > 0 && r.created - r.firstHit <= 0.021 && Math.Abs(r.up - r.created - ZWorld.FortDefenderPop) <= 0.011 && r.eastRing
                      && !r.selectable && !r.ordered && r.unitCountDelta == 0 && r.shots >= 38 && r.damagePerHit == g.Damage
                      && Math.Abs(r.gapMin - g.attack_speed) < 0.011 && Math.Abs(r.gapMax - g.attack_speed) < 0.011
                      && r.enemiesTargeted && r.killedAfter > 0 && Math.Abs(r.respawnAfter - ZWorld.FortDefenderRespawn) <= 0.021
                      && Math.Abs(r.goneAfterCalm - ZWorld.FortDefenderCalm - ZWorld.FortDefenderLower) <= 0.05 && r.maxAlive == 1 && r.offSpawns == 0
                      && ZWorld.FortDefenderNestCover == 4 && r.blastOutside == DefenderBlastOutside && r.blastInside == ZodSplash1000r50at6;
            bool pref = getInt("oz_fort_defender", 1) == 1 && getInt("oz_zod_exact", 0) == 0;
            return new Deviation
            {
                id = "DV41", change = "Fort defender: a fort under attack sends one grunt up through a roof hatch into the sandbag nest on the attack's side; he shoots back until the attack ends",
                zod = "forts have no crew or weapon of their own (bfort.cpp); their only guns are the cannons on the 4 turret spots",
                openz = $"under attack = an enemy hit on the fort within {ZWorld.FortDefenderAlarm:0} s (the fort warning's window): a grunt of the fort's team comes up through the hatch in the centre of the flower-shaped sandbag nest nearest the attacker ({ZWorld.FortDefenderPop:0.0} s lid + climb, then he fires); Zod grunt stats ({g.Damage} HP at {g.attack_damage_chance * 100:0} % every {g.attack_speed} s, {g.attack_radius} px, {g.MaxHealth} HP, DV23 line of sight from the roof), no driver sniping; " +
                        $"target = the fort's last attacker in range, else the nearest enemy unit in range; targetable and killable like any robot, his sandbag nest takes 3/4 of a blast bursting outside it ({ZWorld.FortDefenderNestPx} px); never selectable or ordered, never leaves the roof or captures, not counted in the unit cap, the no-units rule or the game end, never ordered by the CPU; " +
                        $"one at a time; killed: the next one {ZWorld.FortDefenderRespawn:0} s later; no hit for {ZWorld.FortDefenderCalm:0} s and nobody in range: he goes down ({ZWorld.FortDefenderLower:0.0} s) keeping his health; " + (ZWorld.FortDefenderWalkDeviation
                            ? $"he walks the roof deck at grunt speed ({ZObject.FortDefenderDeckNodes}-node deck graph round the gun platform) to the nearest spot with a clear line past the four towers ({ZObject.FortDefenderTowerR:0.0} m bodies) and the gun block (sampled every {ZObject.FortDefenderLineStep:0.00} m), fires only along such a line, re-picks every {ZWorld.FortDefenderScan:0.0} s; no spot sees anyone (or on his way): he lobs grenades over the towers ({ZWorld.FortDefenderNades}, +1 per {ZWorld.FortDefenderNadeRefill:0} s, Zod's grenade cooldown; damage and radius: DV104); the sandbags cover him only inside a nest; back to the nearest hatch on foot when the attack ends" + (ZWorld.FortDefenderIdleDeviation ? $"; never idle while an enemy is near: from the 6.3 m deck his grenades carry {ZWorld.FortDefenderLobBonusPx} px farther, out of reach he walks to the deck spot nearest the enemy, out of grenades with no clear line he steps toward his target" : "")
                            : $"the attack moves to another nest's side for {ZWorld.FortDefenderSwitchHold:0} s: down and up there ({ZWorld.FortDefenderTransit:0} s through the fort)"),
                measured = $"L02 red fort, a heavy tank shelling it from the east: first hit {NbS(r.firstHit)}, defender created {(r.created - r.firstHit) * 1000:0} ms later on ring {r.ring} ({(r.eastRing ? "east" : "NOT east")}), firing {r.up - r.created:0.00} s after; 20 s at a dummy 10 m off: {r.shots} shots, {r.hits} hits, {r.damagePerHit} HP per hit, every {r.gapMin:0.00}-{r.gapMax:0.00} s; " +
                           $"selectable {r.selectable}, ordered {r.ordered}, unit count {r.unitCountDelta:+0;-0;+0}; a 1000 HP / 50 px blast 30 px off takes {r.blastOutside} HP (expected {DefenderBlastOutside} = Zod {ZodSplash1000r50at30} / {ZWorld.FortDefenderNestCover}), one 6 px off (in his nest) {r.blastInside} (Zod {ZodSplash1000r50at6}, in full); a blue grunt squad targeted him {r.enemiesTargeted} and killed him after {NbS(r.killedAfter)}; next one {NbS(r.respawnAfter)} after the kill; gone {NbS(r.goneAfterCalm)} after the last hit; at most {r.maxAlive} at a time; DV41 off: {r.offSpawns} sent up",
                toggle = $"PlayerPrefs oz_fort_defender = {(getInt("oz_fort_defender", 1) == 1 ? "1 (on, default)" : "0 (off)")}; menu OpenZ/Deviations/Fort Defender; off in 'Zod exact' (oz_zod_exact = 1) and in the parity report's Zod rows; bot-vs-bot balance: fort_defender_v1 README",
                status = !ok ? DevCheck : pref ? DevActive : DevActive + " (off by pref)",
            };
        }

#if !OPENZ_HARNESS
        const string MenuFortDefender = "OpenZ/Deviations/Fort Defender (oz_fort_defender)";
        [MenuItem(MenuFortDefender)] static void TFortDefender() => NbToggle("oz_fort_defender", 1, "fort defender (DV41)");
        [MenuItem(MenuFortDefender, true)] static bool TFortDefenderC() { Menu.SetChecked(MenuFortDefender, PlayerPrefs.GetInt("oz_fort_defender", 1) == 1); return true; }

        [MenuItem("OpenZ/Deviations/Probe DV41 Fort Defender")]
        static void ProbeFortDefenderMenu()
        {
            var d = DevFortDefender((k, def) => PlayerPrefs.GetInt(k, def));
            var path = System.IO.Path.GetFullPath(System.IO.Path.Combine(Application.dataPath, "../../../../work/records/fort_defender_dv41.md"));
            System.IO.File.WriteAllText(path, $"# DV41 fort defender probe {DateTime.Now:yyyy-MM-dd HH:mm}\n\n{d.status}\n\n{d.measured.Replace("; ", "\n- ")}\n");
            Debug.Log($"[DV41] {d.status}: {d.measured}");
        }

        [MenuItem("OpenZ/View/Fort Defender Show (Play)")]
        static void ShowMenu()
        {
            if (!EditorApplication.isPlaying) { Debug.LogError("[DV41] enter Play first (full look, 60 fps cap)"); return; }
            FortDefenderShow.Run("after");
        }
#endif
    }
}
