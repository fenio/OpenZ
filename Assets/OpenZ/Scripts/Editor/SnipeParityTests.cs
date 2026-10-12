using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using NUnit.Framework;
using OpenZ.Sim;
#if UNITY_EDITOR
using UnityEngine;
#endif

namespace OpenZ.EditorTools
{
    /// <summary>
    /// Driver sniping parity (EditMode, headless sim, no scene). Zod sources: can_snipe / can_be_sniped / has_lid per class
    /// (rgrunt.cpp:11, rpsycho.cpp:10, rsniper.cpp:10, rpyro.cpp:10, rlaser.cpp:10, vjeep.cpp:25-26, vlight.cpp:22,39,
    /// vmedium.cpp:23,40, vheavy.cpp:37-38, cgatling.cpp:13, zcannon.cpp:9-10), CanBeSniped (zobject.cpp:4693-4696,
    /// zvehicle.cpp:178-186, zcannon.cpp:46-49), the lid (zobject.cpp:1559-1594, zvehicle.cpp:198-240), the roll
    /// (zobject.cpp:2187-2199: hit, then (rand()%10000)/10000.0 &lt;= attack_snipe_chance), the driver HP pool
    /// (zobject.cpp:334-359) and the server follow-up (zserver.cpp:1808-1874).
    /// Every test runs Zod exact (ZWorld.SnipeDeviation = false, restored afterwards) except section 6, which checks the
    /// listed OpenZ deviation DC2 (driver-hit floor per target, snipers x3, open-seat snipe chance) against its targets.
    /// </summary>
    [TestFixture]
    public class SnipeParityTests
    {
        bool savedDeviation;
        [SetUp] public void ZodExact() { savedDeviation = ZWorld.SnipeDeviation; ZWorld.SnipeDeviation = false; savedFortGun = ZWorld.FortGunDeviation; ZWorld.FortGunDeviation = false; savedDuel = ZWorld.DuelDeviation; ZWorld.DuelDeviation = false; }
        bool savedFortGun, savedDuel;
        [TearDown] public void RestoreDeviation() { ZWorld.SnipeDeviation = savedDeviation; ZWorld.FortGunDeviation = savedFortGun; ZWorld.DuelDeviation = savedDuel; }

        // ------------------------------------------------------------------ fixtures
        public static string HarnessResources = System.Environment.GetEnvironmentVariable("OPENZ_RESOURCES") ?? System.IO.Path.Combine("Assets", "OpenZ", "Resources", "OpenZ") + System.IO.Path.DirectorySeparatorChar;   // headless harness: no private path
        static string Text(string res)
        {
#if UNITY_EDITOR
            return Resources.Load<TextAsset>("OpenZ/" + res).text;
#else
            return File.ReadAllText(Path.Combine(HarnessResources, res.Replace('/', Path.DirectorySeparatorChar) + (res.StartsWith("Maps") ? ".json" : ".txt")));
#endif
        }
        static string settings, flat;
        static string Settings => settings ??= Text("default_settings");
        static string Flat => flat ??= BuildFlat(96, 96);
        static string BuildFlat(int w, int h)
        {
            var sb = new StringBuilder("{\"file\":\"snipe_flat\",\"planet\":\"desert\",\"width\":" + w + ",\"height\":" + h + ",\"classes\":[");
            sb.Append(string.Join(",", Enumerable.Repeat("0", w * h))).Append("],\"tiles\":[").Append(string.Join(",", Enumerable.Repeat("0", w * h)));
            sb.Append("],\"zones\":[{\"x\":0,\"y\":0,\"w\":" + w + ",\"h\":" + h + "}],\"objects\":[]}");
            return sb.ToString();
        }
        static ZWorld NewWorld(int seed) { var w = ZWorld.Create(Settings, Flat, seed); w.Events.Clear(); return w; }

        const int CX = 640, CY = 640, BIG = 1_000_000_000;
        static readonly string[] Robots = { "grunt", "psycho", "sniper", "tough", "pyro", "laser" };
        static readonly string[] Vehicles = { "jeep", "light", "medium", "heavy", "apc", "missile_launcher", "crane" };
        static readonly string[] Cannons = { "gatling", "gun", "howitzer", "missile_cannon" };
        static string Cat(string t) => Robots.Contains(t) ? "robot" : Vehicles.Contains(t) ? "vehicle" : "cannon";
        static ZObject Place(ZWorld w, string t, int cx, int cy, Team team, int n = 1) =>
            Cat(t) == "robot" ? w.CreateRobotGroup(t, cx - 8, cy - 8, team, n) : w.CreateObject(Cat(t), t, cx - 16, cy - 16, team);
        static IEnumerable<ZObject> Squad(ZObject o) { yield return o; foreach (var m in o.Minions) yield return m; }
        static void Order(ZWorld w, ZObject a, ZObject t, WpMode m = WpMode.Attack) =>
            w.SendWaypoints(a.Owner, a, new List<ZWaypoint> { new ZWaypoint { mode = m, refId = t.Id, x = t.CenterX, y = t.CenterY } });
        static void Freeze(ZObject o) { o.Damage = 0; o.MoveSpeed = 0; o.RealMoveSpeed = 0; }

        // ------------------------------------------------------------------ 1. who can snipe, who can be sniped
        [Test]
        public void CapabilityListsMatchZod()
        {
            var w = NewWorld(1);
            var snipers = new HashSet<string> { "grunt", "psycho", "sniper", "pyro", "laser", "jeep", "gatling" };
            var snipable = new HashSet<string> { "jeep", "light", "medium", "heavy", "gatling", "gun", "howitzer", "missile_cannon" };
            var lidded = new HashSet<string> { "light", "medium", "heavy" };
            var chance = new Dictionary<string, double> { ["grunt"] = 0.3, ["psycho"] = 0.3, ["sniper"] = 0.8, ["pyro"] = 0.0, ["laser"] = 0.6, ["jeep"] = 0.4, ["gatling"] = 0.4 };
            foreach (var t in Robots.Concat(Vehicles).Concat(Cannons))
            {
                var o = Place(w, t, CX, CY, Team.Red);
                Assert.AreEqual(snipers.Contains(t), o.CanSnipe, $"{t}.can_snipe");
                Assert.AreEqual(snipable.Contains(t), o.CanBeSnipedFlag, $"{t}.can_be_sniped");
                Assert.AreEqual(lidded.Contains(t), o.HasLid, $"{t}.has_lid");
                double exp = chance.TryGetValue(t, out var c) ? c : 0.0;
                Assert.LessOrEqual(Math.Abs(o.SnipeChance - exp), 1e-9, $"{t}.attack_snipe_chance {o.SnipeChance} vs {exp}");
                if (o.Kind != ZKind.Robot) Assert.AreEqual(1081, o.Drivers.Count == 1 ? o.Drivers[0] : -1, $"{t}: one full-health grunt driver (zvehicle.cpp:155-168)");
            }
        }

        [Test]
        public void CanBeSnipedRules()
        {
            var w = NewWorld(2);
            foreach (var r in Robots) Assert.IsFalse(Place(w, r, CX, CY, Team.Red).CanBeSniped(), $"robot {r} never snipable");
            foreach (var v in new[] { "apc", "missile_launcher", "crane" }) Assert.IsFalse(Place(w, v, CX, CY, Team.Red).CanBeSniped(), $"crewed {v} never snipable");
            var jeep = Place(w, "jeep", CX, CY, Team.Red);
            Assert.IsTrue(jeep.CanBeSniped(), "crewed jeep (no lid) is always snipable");
            Assert.IsFalse(Place(w, "jeep", CX, CY, Team.Neutral).CanBeSniped(), "empty jeep is not snipable");
            foreach (var t in new[] { "light", "medium", "heavy" })
            {
                var tank = Place(w, t, CX, CY, Team.Red);
                Assert.IsFalse(tank.LidOpen, $"{t} starts with the lid closed");
                Assert.IsFalse(tank.CanBeSniped(), $"{t} with the lid closed is not snipable");
                tank.LidOpen = true;
                Assert.IsTrue(tank.CanBeSniped(), $"{t} with the lid open is snipable");
            }
            foreach (var c in Cannons)
            {
                var o = Place(w, c, CX, CY, Team.Red);
                Assert.IsTrue(o.CanBeSniped(), $"ejectable crewed {c} is snipable");
                o.Ejectable = false;
                Assert.IsFalse(o.CanBeSniped(), $"fort-turret (non-ejectable) {c} is never snipable (zcannon.cpp:46-49)");
            }
        }

        // ------------------------------------------------------------------ 2. the lid
        [Test]
        public void LidOpensInFourOfFiveEngagesOfSnipeCapableTargetsOnly()
        {
            var w = NewWorld(3);
            var tank = Place(w, "light", CX, CY, Team.Blue); Freeze(tank);
            var g = Place(w, "grunt", CX + 60, CY, Team.Red, 1); Freeze(g);
            var tough = Place(w, "tough", CX - 60, CY, Team.Red, 1); Freeze(tough);
            int n = 2000, open = 0;
            for (int i = 0; i < n; i++) { tank.Disengage(); w.Tick(2.0); tank.Engage(g); if (tank.LidOpen) open++; }
            double p = (double)open / n;
            Assert.LessOrEqual(Math.Abs(p - 0.8), 0.03, $"lid open on engaging a grunt: {p:P1}, Zod 80 % (zvehicle.cpp:198-207)");
            int openTough = 0;
            for (int i = 0; i < 300; i++) { tank.Disengage(); w.Tick(2.0); tank.Engage(tough); if (tank.LidOpen) openTough++; }
            Assert.AreEqual(0, openTough, "a Tough cannot snipe: the lid stays closed");
        }

        [Test]
        public void TankOnlyBeingShotKeepsTheLidClosedAndCannotBeSniped()
        {
            long hits = 0, driverHits = 0;
            for (int seed = 1; seed <= 5; seed++)
            {
                var w = NewWorld(seed);
                var g = Place(w, "grunt", CX, CY, Team.Red, 3);
                var tank = Place(w, "light", CX + 80, CY, Team.Blue); Freeze(tank); tank.MaxHealth = tank.Health = BIG;
                Order(w, g, tank);
                while (w.Time < 30)
                {
                    w.Tick(ZConst.TICK);
                    Assert.IsFalse(tank.LidOpen, "a tank that never engages never opens its lid");
                    foreach (var e in w.Events) if (e.type == ZEventType.Hitscan && e.otherId == tank.Id && e.flag) { hits++; if (e.flag2) driverHits++; }
                    w.Events.Clear();
                }
            }
            Assert.Greater(hits, 100, "grunts hit the tank");
            Assert.AreEqual(0L, driverHits, "no driver hits through a closed lid");
        }

        /// <summary>Zod's SignalLidShouldOpen never cancels a close already scheduled by the last Disengage
        /// (zvehicle.cpp:198-220): after a kill the lid shuts within 1.4 s even if the tank engages the next grunt at once.</summary>
        [Test]
        public void RetargetDoesNotCancelAPendingLidClose()
        {
            var w = NewWorld(4);
            var tank = Place(w, "light", CX, CY, Team.Blue); Freeze(tank);
            var a = Place(w, "grunt", CX + 60, CY, Team.Red, 1); Freeze(a);
            var b = Place(w, "grunt", CX - 60, CY, Team.Red, 1); Freeze(b);
            int trials = 200, stillOpen = 0;
            for (int i = 0; i < trials; i++)
            {
                for (int k = 0; k < 50 && !tank.LidOpen; k++) { tank.Disengage(); w.Tick(2.0); tank.Engage(a); }
                Assert.IsTrue(tank.LidOpen, "lid opened on A");
                tank.Disengage();                       // A gone: close scheduled 0.0-1.4 s later
                tank.Engage(b);                         // B engaged in the same tick
                for (int k = 0; k < 150; k++) w.Tick(ZConst.TICK);
                if (tank.LidOpen) stillOpen++;
                w.Events.Clear();
            }
            Assert.AreEqual(0, stillOpen, $"lid still open after the retarget in {stillOpen}/{trials} trials (Zod: always closed)");
        }

        // ------------------------------------------------------------------ 3. the roll
        static (long hits, long driverHits) Rate(string attacker, int n, string target, int seeds, double seconds)
        {
            long hits = 0, sn = 0;
            for (int seed = 1; seed <= seeds; seed++)
            {
                var w = NewWorld(seed);
                var a = Place(w, attacker, CX, CY, Team.Red, n);
                var t = Place(w, target, CX + 80, CY, Team.Blue);
                Freeze(t); t.MaxHealth = t.Health = BIG; t.Drivers[0] = BIG;
                Order(w, a, t);
                while (w.Time < seconds)
                {
                    w.Tick(ZConst.TICK);
                    foreach (var e in w.Events) if (e.type == ZEventType.Hitscan && e.otherId == t.Id && e.flag) { hits++; if (e.flag2) sn++; }
                    w.Events.Clear();
                }
            }
            return (hits, sn);
        }

        [TestCase("grunt", 3, 0.3)]
        [TestCase("psycho", 3, 0.3)]
        [TestCase("sniper", 3, 0.8)]
        [TestCase("laser", 4, 0.6)]
        [TestCase("jeep", 1, 0.4)]
        [TestCase("gatling", 1, 0.4)]
        [TestCase("pyro", 4, 0.0)]
        public void DriverShareOfHitsEqualsSnipeChance(string attacker, int n, double chance)
        {
            foreach (var target in new[] { "jeep", "gatling" })
            {
                var (hits, sn) = Rate(attacker, n, target, 10, 60);
                Assert.Greater(hits, 1000, $"{attacker} hits {target}");
                double exp = (Math.Floor(chance * 10000 + 1e-9) + 1) / 10000.0;          // Zod rolls <= chance on a 1/10000 grid
                double m = (double)sn / hits, sd = Math.Sqrt(Math.Max(exp * (1 - exp), 1e-4) / hits);
                Assert.LessOrEqual(Math.Abs(m - exp), 4 * sd + 1e-4, $"{attacker} -> {target}: driver share {m:F4} over {hits} hits, Zod {exp:F4}");
            }
        }

        [TestCase("tough", 1)]
        [TestCase("light", 1)]
        [TestCase("gun", 1)]
        public void MissileWeaponsNeverSnipe(string attacker, int n)
        {
            long driverHits = 0, fired = 0;
            for (int seed = 1; seed <= 3; seed++)
            {
                var w = NewWorld(seed);
                var a = Place(w, attacker, CX, CY, Team.Red, n);
                var t = Place(w, "jeep", CX + 80, CY, Team.Blue); Freeze(t); t.MaxHealth = t.Health = BIG;
                int d0 = t.Drivers[0];
                Order(w, a, t);
                while (w.Time < 40)
                {
                    w.Tick(ZConst.TICK);
                    foreach (var e in w.Events) { if (e.type == ZEventType.MissileFired && e.objId == a.Id) fired++; if (e.type == ZEventType.DriverHit || e.type == ZEventType.Sniped) driverHits++; }
                    w.Events.Clear();
                }
                Assert.AreEqual(d0, t.Drivers[0], $"{attacker}: driver HP untouched (missile branch, zobject.cpp:2102-2185)");
            }
            Assert.Greater(fired, 0L, $"{attacker} fired");
            Assert.AreEqual(0L, driverHits, $"{attacker} never hits a driver");
        }

        // ------------------------------------------------------------------ 4. driver HP pool and the snipe itself
        [Test]
        public void DriverHitsDrainAPoolThenTheSnipeEmptiesTheVehicle()
        {
            var w = NewWorld(6);
            var s = Place(w, "sniper", CX, CY, Team.Red, 1);
            var jeep = Place(w, "jeep", CX + 80, CY, Team.Blue); Freeze(jeep); jeep.MaxHealth = jeep.Health = BIG;
            Order(w, s, jeep);
            int driverHits = 0, driverHitEvents = 0, sniped = 0, firstLeft = -1;
            while (w.Time < 60 && sniped == 0)
            {
                w.Tick(ZConst.TICK);
                foreach (var e in w.Events)
                {
                    if (e.type == ZEventType.Hitscan && e.otherId == jeep.Id && e.flag2) { driverHits++; if (firstLeft < 0 && jeep.Drivers.Count > 0) firstLeft = jeep.Drivers[0]; }
                    if (e.type == ZEventType.DriverHit && e.objId == jeep.Id) driverHitEvents++;
                    if (e.type == ZEventType.Sniped && e.objId == jeep.Id) sniped++;
                }
                w.Events.Clear();
            }
            int dmg = s.Damage, need = (1081 + dmg - 1) / dmg;
            Assert.AreEqual(1081 - dmg, firstLeft, "a driver hit subtracts the attacker's normal damage (zobject.cpp:334-359)");
            Assert.AreEqual(1, sniped, "sniped once");
            Assert.AreEqual(need, driverHits, $"{need} sniper driver hits empty a 1081 HP driver");
            Assert.AreEqual(need - 1, driverHitEvents, "every non-lethal driver hit raises DriverHit (zserver.cpp:1810-1818)");
        }

        [Test]
        public void SnipedVehicleTurnsNeutralStopsAndEitherTeamCanReenter()
        {
            foreach (var team in new[] { Team.Red, Team.Blue })
            {
                var w = NewWorld(5);
                var a = Place(w, "sniper", CX, CY, Team.Red, 3);
                var jeep = Place(w, "jeep", CX + 90, CY, Team.Blue); jeep.Damage = 0; jeep.MaxHealth = jeep.Health = BIG;
                w.SendWaypoints(Team.Blue, jeep, new List<ZWaypoint> { new ZWaypoint { mode = WpMode.Move, refId = -1, x = CX + 90, y = CY + 600 } });
                Order(w, a, jeep);
                bool sniped = false, toNeutral = false;
                while (w.Time < 60 && !sniped)
                {
                    w.Tick(ZConst.TICK);
                    foreach (var e in w.Events)
                    {
                        if (e.type == ZEventType.Sniped && e.objId == jeep.Id) sniped = true;
                        if (e.type == ZEventType.OwnerChanged && e.objId == jeep.Id && (Team)(int)e.value == Team.Neutral) toNeutral = true;
                    }
                    w.Events.Clear();
                }
                Assert.IsTrue(sniped, "3 snipers empty a moving jeep within 60 s");
                Assert.IsTrue(toNeutral, "OwnerChanged -> Neutral raised for the view (grey paint)");
                Assert.AreEqual(Team.Neutral, jeep.Owner, "neutral");
                Assert.AreEqual(0, jeep.Drivers.Count, "no driver");
                Assert.AreEqual(0, jeep.Waypoints.Count, "waypoints cleared (zserver.cpp:1840-1848)");
                Assert.IsFalse(jeep.IsMoving, "stopped");
                Assert.IsTrue(jeep.AttackObject == null, "disengaged");
                Assert.IsTrue(Squad(a).All(m => m.AttackObject != jeep), "every attacker disengaged (zserver.cpp:1850-1870)");
                Assert.IsTrue(jeep.CanBeEntered(), "enterable");
                foreach (var m in Squad(a)) m.Damage = 0;
                var g = Place(w, "grunt", jeep.CenterX - 70, jeep.CenterY, team, 1);
                Order(w, g, jeep, WpMode.Enter);
                bool entered = false;
                double t0 = w.Time;
                while (w.Time < t0 + 30 && !entered)
                {
                    w.Tick(ZConst.TICK);
                    foreach (var e in w.Events) if (e.type == ZEventType.Entered && e.otherId == jeep.Id) entered = true;
                    w.Events.Clear();
                }
                Assert.IsTrue(entered, $"{team} grunt re-enters the sniped jeep");
                Assert.AreEqual(team, jeep.Owner, $"jeep now {team}");
                Assert.AreEqual(1, jeep.Drivers.Count, "one driver");
                Assert.AreEqual("grunt", jeep.DriverType, "grunt driver");
            }
        }

        /// <summary>Needs patches/02_sim_ZWorld.diff: the Sniped event names the sniper and the team that lost the driver
        /// (Zod's client drew the flying body in the old owner's colour, zplayer_events.cpp:1133).</summary>
        [Test]
        public void SnipedEventCarriesSniperAndOldOwner()
        {
            var w = NewWorld(7);
            var a = Place(w, "sniper", CX, CY, Team.Red, 3);
            var jeep = Place(w, "jeep", CX + 90, CY, Team.Blue); Freeze(jeep); jeep.MaxHealth = jeep.Health = BIG;
            Order(w, a, jeep);
            ZEvent? ev = null;
            while (w.Time < 60 && ev == null)
            {
                w.Tick(ZConst.TICK);
                foreach (var e in w.Events) if (e.type == ZEventType.Sniped && e.objId == jeep.Id) ev = e;
                w.Events.Clear();
            }
            Assert.IsTrue(ev.HasValue, "sniped");
            Assert.AreEqual(Team.Blue, (Team)(int)ev.Value.value, "Sniped.value = team that lost the driver");
            Assert.IsTrue(Squad(a).Any(m => m.Id == ev.Value.otherId), "Sniped.otherId = the sniper");
        }

        // ------------------------------------------------------------------ 6. OpenZ deviation DC2 (switch ON)
        [Test]
        public void DeviationDriverHitDamageTable()
        {
            var w = NewWorld(21);
            ZObject U(string t) => Place(w, t, CX, CY, Team.Red, 1);
            var cases = new (string a, string t, int zod, int dev)[]
            {
                ("grunt", "medium", 11, 19), ("grunt", "heavy", 11, 15), ("psycho", "heavy", 26, 26),
                ("psycho", "jeep", 26, 57), ("psycho", "medium", 26, 26), ("sniper", "jeep", 70, 210), ("sniper", "heavy", 70, 210),
                ("laser", "jeep", 177, 177), ("jeep", "medium", 27, 27), ("gatling", "medium", 39, 39), ("gatling", "jeep", 39, 57), ("jeep", "jeep", 27, 57), ("gatling", "gatling", 39, 39), ("sniper", "gatling", 70, 210), ("sniper", "light", 70, 210),
            };
            foreach (var c in cases)
            {
                var a = U(c.a); var t = U(c.t);
                ZWorld.SnipeDeviation = false; int zod = w.DriverHitDamage(a, t);
                ZWorld.SnipeDeviation = true; int dev = w.DriverHitDamage(a, t);
                Assert.AreEqual(c.zod, zod, $"{c.a} -> {c.t} Zod exact");
                Assert.AreEqual(c.dev, dev, $"{c.a} -> {c.t} DC2");
            }
        }

        [Test]
        public void DeviationKeepsZodRulesRollsAndLid()
        {
            ZWorld.SnipeDeviation = true;
            CapabilityListsMatchZod();
            CanBeSnipedRules();
            TankOnlyBeingShotKeepsTheLidClosedAndCannotBeSniped();
            RetargetDoesNotCancelAPendingLidClose();
            var (hits, sn) = Rate("grunt", 3, "jeep", 5, 60);
            double m = (double)sn / hits;
            Assert.LessOrEqual(Math.Abs(m - 0.3001), 4 * Math.Sqrt(0.3 * 0.7 / hits) + 1e-4, $"driver share with DC2 on {m:F4} (chance unchanged)");
        }

        /// <summary>3 grunts keep firing (immortal) at a crewed stationary target that fires back (so the tank lid opens by
        /// Zod's rule): mean time to kill the driver near the DC2 targets, and the hull does not usually die first (it does in
        /// the ~20 % of fights where Zod's lid roll keeps the commander inside).</summary>
        static List<(int outcome, double t)> Duels(string attacker, int n, string target, int seeds, double cap, bool passive = false, bool mortal = false)
        {
            var r = new List<(int, double)>();
            for (int seed = 1; seed <= seeds; seed++)
            {
                var w = NewWorld(seed);
                var t = Place(w, target, CX + 80, CY, Team.Blue); t.MoveSpeed = 0; t.RealMoveSpeed = 0; if (passive) t.Damage = 0;
                var a = Place(w, attacker, CX, CY, Team.Red, n);
                if (!mortal) foreach (var m in Squad(a)) m.MaxHealth = m.Health = BIG;
                var squad = Squad(a).ToList();
                Order(w, a, t);
                int outcome = 3; double at = cap;
                while (w.Time < cap && outcome == 3)
                {
                    w.Tick(ZConst.TICK);
                    foreach (var e in w.Events)
                    {
                        if (e.type == ZEventType.Sniped && e.objId == t.Id && outcome == 3) { outcome = 0; at = w.Time; }
                        if (e.type == ZEventType.Death && e.objId == t.Id && outcome == 3) { outcome = 1; at = w.Time; }
                    }
                    w.Events.Clear();
                    if (mortal && outcome == 3 && squad.All(m => m.Destroyed)) { outcome = 2; at = w.Time; }   // the attackers lost
                }
                r.Add((outcome, at));
            }
            return r;
        }

        [TestCase("jeep", 15.0)]
        [TestCase("medium", 45.0)]
        [TestCase("heavy", 60.0)]
        public void DeviationGruntSquadMeetsDriverTargets(string target, double seconds)
        {
            ZWorld.SnipeDeviation = true;
            var r = Duels("grunt", 3, target, 30, 200);
            var sn = r.Where(x => x.outcome == 0).Select(x => x.t).ToList();
            double share = (double)sn.Count / r.Count, hull = (double)r.Count(x => x.outcome == 1) / r.Count;
            Assert.Greater(sn.Count, 0, $"{target}: sniped at least once");
            double mean = sn.Average();
            Assert.LessOrEqual(Math.Abs(mean - seconds), 0.2 * seconds, $"{target}: mean time to kill the driver {mean:F1} s, target ~{seconds} s");
            Assert.GreaterOrEqual(share, 0.65, $"{target}: driver killed in {share:P0} of fights");
            Assert.LessOrEqual(hull, 0.3, $"{target}: hull died first in {hull:P0}");
        }

        [Test]
        public void DeviationStillNeedsTheLidOpen()
        {
            ZWorld.SnipeDeviation = true;
            var r = Duels("grunt", 3, "light", 10, 60, passive: true);
            Assert.AreEqual(0, r.Count(x => x.outcome == 0), "a tank that never engages keeps its lid shut: no snipe even with DC2");
        }

        [Test]
        public void DeviationSnipersAreTheFastestDriverKillers()
        {
            ZWorld.SnipeDeviation = true;
            foreach (var target in new[] { "jeep", "light" })
            {
                double T(string a, int n) { var s = Duels(a, n, target, 15, 120).Where(x => x.outcome == 0).Select(x => x.t).ToList(); return s.Count > 0 ? s.Average() : 999; }
                double sniper = T("sniper", 3);
                foreach (var (a, n) in new[] { ("laser", 4), ("psycho", 3), ("grunt", 3), ("jeep", 1), ("gatling", 1) })
                {
                    double other = T(a, n);
                    Assert.Greater(other, sniper, $"{target}: snipers {sniper:F2} s must beat {a} {other:F2} s");
                }
            }
        }

        [Test]
        public void ZodExactGruntsStillLoseToTheHull()
        {
            var r = Duels("grunt", 3, "jeep", 10, 120);
            Assert.AreEqual(0, r.Count(x => x.outcome == 0), "Zod exact: 3 grunts never empty a jeep (99 driver hits) before its hull dies");
        }

        /// <summary>DC2 open seats (switch on), counted: the driver share of the hits on a jeep / a gatling is Zod's
        /// attack_snipe_chance (no pair of ZWorld.OpenSeatPair raises the chance on these two targets).</summary>
        [TestCase("psycho", 3, "jeep", 0.3)]
        [TestCase("psycho", 3, "gatling", 0.3)]
        [TestCase("jeep", 1, "jeep", 0.4)]
        [TestCase("jeep", 1, "gatling", 0.4)]
        [TestCase("grunt", 3, "gatling", 0.3)]
        [TestCase("gatling", 1, "jeep", 0.4)]
        public void DeviationOpenSeatDriverShare(string attacker, int n, string target, double chance)
        {
            ZWorld.SnipeDeviation = true;
            var (hits, sn) = Rate(attacker, n, target, 10, 60);
            Assert.Greater(hits, 1000, $"{attacker} hits {target}");
            double exp = (Math.Floor(chance * 10000 + 1e-9) + 1) / 10000.0;          // the roll is <= chance on a 1/10000 grid
            double m = (double)sn / hits, sd = Math.Sqrt(exp * (1 - exp) / hits);
            Assert.LessOrEqual(Math.Abs(m - exp), 4 * sd + 1e-4, $"{attacker} -> {target}: driver share {m:F4} over {hits} hits, expected {exp:F4}");
        }

        /// <summary>DC2 open seats: every pair of ZWorld.OpenSeatPair uses its own driver-hit HP (never below the attacker's
        /// damage) and its own snipe chance (never below Zod's); with the switch off both are Zod's.</summary>
        [Test]
        public void DeviationOpenSeatPairValues()
        {
            var w = NewWorld(22);
            Assert.Greater(ZWorld.OpenSeatPair.Count, 0, "the pair table is not empty");
            foreach (var kv in ZWorld.OpenSeatPair)
            {
                var a = Place(w, kv.Key.attacker, CX, CY, Team.Red, 1); var t = Place(w, kv.Key.target, CX, CY, Team.Red, 1);
                double zodChance = w.S.Unit(kv.Key.attacker).attack_snipe_chance;
                ZWorld.SnipeDeviation = false;
                Assert.AreEqual(a.Damage, w.DriverHitDamage(a, t), $"{kv.Key} Zod exact: driver hit");
                Assert.AreEqual(zodChance, ZWorld.SnipeChanceOn(a, t), 1e-6, $"{kv.Key} Zod exact: chance");
                ZWorld.SnipeDeviation = true;
                int hit = kv.Value.hit > 0 ? kv.Value.hit : ZWorld.DriverHitFloor.TryGetValue(kv.Key.target, out int floor) ? floor : 0;
                Assert.AreEqual(Math.Max(a.Damage, hit), w.DriverHitDamage(a, t), $"{kv.Key} DC2: driver hit");
                Assert.AreEqual(Math.Max(zodChance, kv.Value.chance), ZWorld.SnipeChanceOn(a, t), 1e-6, $"{kv.Key} DC2: chance");
            }
        }

        /// <summary>DC2 open seats: a psycho squad sometimes empties a crewed gatling that fires back and more often destroys
        /// it (ZWorld.OpenSeatPair; Zod exact: the hull dies first almost every time).</summary>
        [Test]
        public void DeviationPsychoSquadSometimesEmptiesAGatling()
        {
            ZWorld.SnipeDeviation = true;
            var r = Duels("psycho", 3, "gatling", 30, 60);
            int sniped = r.Count(x => x.outcome == 0), hull = r.Count(x => x.outcome == 1);
            Assert.That(sniped, Is.InRange(3, 19), $"gatling crew sniped in {sniped}/30 fights, hull first in {hull}");
            Assert.GreaterOrEqual(hull, 8, $"the gatling is still destroyed in most fights (hull first in {hull}/30)");
        }

        /// <summary>DC2 open seats, everything mortal, the lid by Zod's rule: one attacker (a full squad for robots) empties a
        /// healthy light tank that fires back in some fights and loses most (ZWorld.OpenSeatPair; with the plain floor: 0 of 40).
        /// A grunt squad alone is shot before it lands its driver hits (almost never); its pair value shows when the squad is
        /// left alone (DeviationGruntSquadIsQuickOnALightTank).</summary>
        [TestCase("psycho", 3, 2, 18)]
        [TestCase("jeep", 1, 1, 15)]
        [TestCase("gatling", 1, 4, 21)]
        [TestCase("grunt", 3, 0, 4)]
        public void DeviationOpenSeatAttackersCanEmptyALightTank(string attacker, int n, int atLeast, int atMost)
        {
            ZWorld.SnipeDeviation = true;
            var r = Duels(attacker, n, "light", 40, 60, mortal: true);
            int sniped = r.Count(x => x.outcome == 0), lost = r.Count(x => x.outcome == 2);
            Assert.That(sniped, Is.InRange(atLeast, atMost), $"{attacker} -> light tank: driver sniped in {sniped}/40 fights, attackers lost {lost}");
        }

        /// <summary>DC2 open seats: a crewed gatling usually empties a single jeep before the jeep shoots its gunner out; the
        /// jeep wins about one fight in five (ZWorld.OpenSeatPair).</summary>
        [Test]
        public void DeviationGatlingUsuallyBeatsAJeep()
        {
            ZWorld.SnipeDeviation = true;
            var r = Duels("jeep", 1, "gatling", 40, 60);
            int sniped = r.Count(x => x.outcome == 0);
            Assert.That(sniped, Is.InRange(2, 16), $"gatling crew sniped by the jeep in {sniped}/40 fights");
        }

        /// <summary>DC2 open seats: on a light tank a grunt's driver hit is the pair value of ZWorld.OpenSeatPair, so a squad
        /// that keeps firing at a light tank with its lid open empties it within seconds (the plain floor: about 30 s).</summary>
        [Test]
        public void DeviationGruntSquadIsQuickOnALightTank()
        {
            ZWorld.SnipeDeviation = true;
            var r = Duels("grunt", 3, "light", 30, 200);
            var sn = r.Where(x => x.outcome == 0).Select(x => x.t).ToList();
            Assert.GreaterOrEqual((double)sn.Count / r.Count, 0.65, $"light: driver killed in {sn.Count}/{r.Count} fights");
            Assert.Less(sn.Average(), 8.0, $"light: mean time to kill the driver {sn.Average():F1} s");
        }

        // ------------------------------------------------------------------ 5. real maps: the pipeline fires in bot play
        [Test, Category("Slow")]
        public void BotMatchRaisesDriverHitsAndEveryDriverHitIsReported()
        {
            foreach (var lv in new[] { "L02", "L17" })
            {
                var w = ZWorld.Create(Settings, Text($"Maps/{lv}_grid"), 2);
                ZBot.Attach(w, Team.Red, true, 2);
                w.Events.Clear();
                long flag2 = 0, driverHit = 0, sniped = 0;
                for (int i = 0; i < 18000; i++)
                {
                    w.Tick(ZConst.TICK);
                    foreach (var e in w.Events)
                    {
                        if (e.type == ZEventType.Hitscan && e.flag2) flag2++;
                        if (e.type == ZEventType.DriverHit) driverHit++;
                        if (e.type == ZEventType.Sniped) sniped++;
                    }
                    w.Events.Clear();
                }
                Assert.Greater(flag2, 0L, $"{lv}: snipe-capable units land driver hits in 180 s of bot play");
                // one driver hit per report, except when two shooters hit the same driver in the tick that kills him
                // (the first report disengages the second shooter, zserver.cpp:1850-1856), so allow a few percent
                Assert.LessOrEqual(driverHit + sniped, flag2, $"{lv}: no report without a driver hit");
                Assert.GreaterOrEqual(driverHit + sniped, (long)Math.Floor(0.95 * flag2), $"{lv}: every driver hit becomes a DriverHit or Sniped event ({driverHit}+{sniped} of {flag2})");
            }
        }
    }
}
