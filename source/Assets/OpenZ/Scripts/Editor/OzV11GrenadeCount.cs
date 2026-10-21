using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using OpenZ.Sim;

namespace OpenZ.EditorTools
{
    /// <summary>
    /// Counted check of the grenade rule of kit KC2 (rule DV104, the final form of b31: a thrown grenade's blast radius x 1.2;
    /// a DIRECT hit (the grenade lands on the target's own body) takes Zod's damage x 1.15 with Zod's fall-off, an AREA hit Zod's
    /// damage less one tenth with the same fall-off; Sim/ZWorld.GrenadeBoost.cs). The blast rule below (Splash) is an independent
    /// copy of the rule, and every row checks that the health really lost is the rule's.
    /// Headless simulation on an empty plain map with the game's switches; every row is run with the rule off and on.
    ///   1  the numbers of a thrown grenade, and what one grenade takes from a tank that cannot move
    ///   2  the thrower's own side is never hurt
    ///   3  the first grenade from the squad's range on a normal light and medium tank of the CPU (it steps aside): the throw, the
    ///      landing point and the tank's sidestep are the same with the rule on and off; only the blast differs
    ///   4  fights: one squad with a box against a tank that stands still; one and three squads against a normal tank of the CPU;
    ///      a grunt squad with a box against a grunt squad without; one and two squads against a gun and a howitzer of the CPU
    ///   4b the other way round: the PLAYER's light and medium tank on an attack order (a direct order: it is not told to step
    ///      aside, DV16) against one, two and three squads of the CPU that carry a box each
    ///   5  one grenade on a whole squad (every scatter offset of a throw aimed at the leader)
    ///   6  the fort's roof defender: his grenades on a squad at the gate
    ///   7  what must NOT change: the blasts of a destroyed grenade crate, a Tough's rocket
    /// The switch is set by name, so the same file also runs against rules code without the kit (then only the "off" rows).
    /// The worlds of a row are NOT seeded 1, 2, 3 ...: see Seed.
    /// </summary>
    public static class OzV11GrenadeCount
    {
        public const int Runs = 100;
        const int Big = 1000000000, GX = 20 * 16 + 8, GY = 20 * 16 + 8;
        const string Switch = "GrenadeBoostDeviation";
        static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

        /// <summary>Whether the rule is on for the rows now running (Splash follows it), and an area hit's damage at the centre
        /// (Zod's less one tenth), both set by Rule / Run.</summary>
        static bool ruleOn; static int areaDamage = 1499;
        static bool Rule(bool on) { bool ok = OzV11Kit.TrySet(typeof(ZWorld), Switch, on); ruleOn = on && ok; return ok; }

        /// <summary>Which set of worlds the rows are counted on: 0 = the check's own. The headless tool runs other sets to show how
        /// far a row moves from one set of worlds to the next.</summary>
        public static int SeedSet = 0;

        /// <summary>The world seed of fight n of a row. NOT n itself: the rules draw from System.Random(seed) (Sim/ZConst.cs ZRng), and
        /// the draws of worlds seeded with neighbouring numbers follow a fixed pattern from one world to the next (they are not
        /// independent). Counted on worlds 1 to 300 the first grenade from the squad's range on a light tank that steps aside
        /// "did nothing" in 98 of 100; on seeds spread over the whole range it is 93 of 100 (2400 worlds).
        /// A fixed whole-number scramble, the same on every machine.</summary>
        public static int Seed(int n)
        {
            unchecked
            {
                uint x = (uint)(n + SeedSet * 1000003) * 0x9E3779B1u;
                x ^= x >> 15; x *= 0x85EBCA77u; x ^= x >> 13; x *= 0xC2B2AE3Du; x ^= x >> 16;
                return (int)(x & 0x3FFFFFFF) + 1;
            }
        }
        static string F(double v, string f = "0.0") => v.ToString(f, Inv);
        static int zodDamageNow = 1666, zodRadiusNow = 30;
        static string Tag(bool rule) => rule ? "rule on " : "rule off";

        /// <summary>What one blast takes from one object: ZWorld.cs ProcessMissileDamage (zserver.cpp:1082-1098).</summary>
        public static int Splash(int damage, int radius, int bx, int by, ZObject o)
        {
            int dx = bx - o.CenterX, dy = by - o.CenterY;
            if (Math.Abs(dx) > radius || Math.Abs(dy) > radius) return 0;
            float mag = (float)Math.Sqrt((float)(dx * dx + dy * dy));
            if (mag >= radius) return 0;
            float ratio = (float)(mag / radius);
            if (!ruleOn) return (int)(damage * (1.0 - ratio));                                  // Zod's line
            bool direct = bx >= o.LocX && by >= o.LocY && bx < o.LocX + o.WidthPix && by < o.LocY + o.HeightPix;   // the grenade lands on the target's own box
            return (int)((direct ? damage : areaDamage) * (1.0 - ratio));                        // direct: the grenade's own damage; area: Zod's less one tenth
        }

        /// <summary>Whether a blast point lies on a target's own box (a direct hit).</summary>
        public static bool Direct(int bx, int by, ZObject o) => bx >= o.LocX && by >= o.LocY && bx < o.LocX + o.WidthPix && by < o.LocY + o.HeightPix;

        /// <summary>An empty map; blue is a CPU team that gives no orders of its own (red counts as the player's team, or the other
        /// way round); one robot of each side far away that cannot move, shoot or die, so the game is never over.</summary>
        static ZWorld NewWorld(string settings, string plain, int seed, bool redCpu = false)
        {
            var w = ZWorld.Create(settings, plain, seed);
            w.Bots.Add(new ZBot(w, redCpu ? Team.Red : Team.Blue, seed + 101) { Ignored = true });
            w.Events.Clear();
            var d = w.CreateRobotGroup("grunt", 4 * 16, 4 * 16, redCpu ? Team.Red : Team.Blue, 1);
            d.MoveSpeed = 0; d.Damage = 0; d.MaxHealth = d.Health = Big;
            var d2 = w.CreateRobotGroup("grunt", 60 * 16, 40 * 16, redCpu ? Team.Blue : Team.Red, 1);
            d2.MoveSpeed = 0; d2.Damage = 0; d2.MaxHealth = d2.Health = Big;
            return w;
        }

        static void Freeze(ZObject o, bool harmless, bool immortal)
        {
            foreach (var m in OzV11Kit.Squad(o)) { m.MoveSpeed = 0; m.RealMoveSpeed = 0; if (harmless) m.Damage = 0; if (immortal) { m.MaxHealth = Big; m.SetHealth(m.MaxHealth); } }
        }

        static bool GrenadesInAir(ZWorld w) => w.PendingMissiles.Any(m => m.grenade) || w.QueuedMissiles.Any(m => m.grenade);

        // ------------------------------------------------------------------ 1: the numbers, and one grenade on a still tank
        public sealed class Still { public long grenades, sum, zero, healthLost, direct, directSum, areaSum, zodSum; public int max, damage = -1, radius = -1; public bool oneKind = true, sameAsRule; public double flight, gap; }

        public static Still StillTarget(string settings, string plain, string target, int dist, int seeds, int perSeed)
        {
            var r = new Still(); double flight = 0, gapSum = 0; int flights = 0, gaps = 0;
            for (int n = 1; n <= seeds; n++)
            {
                int seed = Seed(n);
                var w = NewWorld(settings, plain, seed);
                var t = OzV11Kit.Place(w, target, GX + dist, GY, Team.Blue);
                foreach (var m in OzV11Kit.Squad(t)) m.Damage = 0;
                for (int i = 0; i < 800; i++) { w.Tick(ZConst.TICK); w.Events.Clear(); }
                Freeze(t, true, true);
                var squad = OzV11Kit.Squad(t).ToList();
                long h0 = squad.Sum(m => (long)m.Health);
                var red = OzV11Kit.Place(w, "grunt", GX, GY, Team.Red, 1);
                red.Grenades = perSeed + 4;                                                      // 4 spare: he never falls back to his rifle while we count
                OzV11Kit.PlayerAttack(w, red, t);
                double lastThrow = -1; int guard = 0, got = 0;
                while (guard++ < 400000 && got < perSeed)
                {
                    w.Tick(ZConst.TICK);
                    foreach (var e in w.Events)
                    {
                        if (e.type == ZEventType.MissileFired && e.flag && e.objId == red.Id)
                        { flight += e.value; flights++; if (lastThrow >= 0) { gapSum += w.Time - lastThrow; gaps++; } lastThrow = w.Time; }
                        else if (e.type == ZEventType.MissileExploded && e.flag && e.objId == red.Id && got < perSeed)
                        {
                            got++; r.grenades++; int d = 0;
                            if (r.damage < 0) { r.damage = (int)e.value; r.radius = e.radius; } else if (r.damage != (int)e.value || r.radius != e.radius) r.oneKind = false;
                            foreach (var m in squad) d += Splash((int)e.value, e.radius, e.x, e.y, m);
                            r.sum += d; if (d == 0) r.zero++; if (d > r.max) r.max = d;
                            bool dir = squad.Any(m => Direct(e.x, e.y, m)); if (dir) { r.direct++; r.directSum += d; } else r.areaSum += d;
                            bool was = ruleOn; ruleOn = false; foreach (var m in squad) r.zodSum += Splash(zodDamageNow, zodRadiusNow, e.x, e.y, m); ruleOn = was;   // what Zod's grenade would have taken from the same blast point
                        }
                    }
                    w.Events.Clear();
                }
                r.healthLost += h0 - squad.Sum(m => (long)m.Health);
            }
            r.sameAsRule = r.healthLost == r.sum; r.flight = flight / Math.Max(1, flights); r.gap = gapSum / Math.Max(1, gaps);
            return r;
        }

        // ------------------------------------------------------------------ 2: the thrower's own side
        public sealed class Own { public long grenades, covered, ownLost, wouldBe; }

        /// <summary>One grunt throws at an enemy tank that cannot move, shoot or die, 60 px away; three robots of the THROWER's side
        /// stand at the tank (on its centre, 10 px and 20 px beside it) and cannot move. covered = blasts whose radius reached one
        /// of them; wouldBe = what the blast rule would take from them if they were enemies; ownLost = what they really lost.</summary>
        public static Own OwnSide(string settings, string plain, int seeds, int perSeed)
        {
            var r = new Own();
            for (int n = 1; n <= seeds; n++)
            {
                int seed = Seed(n);
                var w = NewWorld(settings, plain, seed);
                var t = OzV11Kit.Place(w, "light", GX + 60, GY, Team.Blue);
                Freeze(t, true, true);
                var mates = new List<ZObject>();
                foreach (int off in new[] { 0, 10, 20 })
                {
                    var m = w.CreateRobotGroup("grunt", t.CenterX - 8, t.CenterY + off - 8, Team.Red, 1);
                    m.MoveSpeed = 0; m.RealMoveSpeed = 0; m.Damage = 0; mates.Add(m);
                }
                long h0 = mates.Sum(m => (long)m.Health);
                var red = OzV11Kit.Place(w, "grunt", GX, GY, Team.Red, 1);
                red.Grenades = perSeed + 4;
                OzV11Kit.PlayerAttack(w, red, t);
                int guard = 0, got = 0;
                while (guard++ < 400000 && got < perSeed)
                {
                    w.Tick(ZConst.TICK);
                    foreach (var e in w.Events)
                        if (e.type == ZEventType.MissileExploded && e.flag && e.objId == red.Id && got < perSeed)
                        {
                            got++; r.grenades++; int d = 0;
                            foreach (var m in mates) d += Splash((int)e.value, e.radius, e.x, e.y, m);
                            if (d > 0) r.covered++; r.wouldBe += d;
                        }
                    w.Events.Clear();
                }
                r.ownLost += h0 - mates.Sum(m => (long)m.Health) + mates.Count(m => m.Destroyed);
                r.ownLost += red.MaxHealth - red.Health;
            }
            return r;
        }

        // ------------------------------------------------------------------ 3: the first grenade on a tank that steps aside
        public sealed class First { public int n, dodged, zero, hp; public long dmg; public double from, away, flight; public List<string> throws = new List<string>(); }

        /// <summary>One grunt of the player (cannot die), ordered to attack ONE normal crewed tank of the CPU (it shoots back, it is
        /// free to move) from a set distance; only the FIRST grenade of each world is counted. throws = per world the landing point,
        /// the flight time and the tank's sidestep point at the throw: they must not depend on the rule.</summary>
        public static First FirstGrenade(string settings, string plain, string target, int dist, int seeds)
        {
            var r = new First();
            for (int n = 1; n <= seeds; n++)
            {
                int seed = Seed(n);
                var w = NewWorld(settings, plain, seed);
                var t = OzV11Kit.Place(w, target, GX + dist, GY, Team.Blue);
                r.hp = t.MaxHealth;
                var red = OzV11Kit.Place(w, "grunt", GX, GY, Team.Red, 1);
                red.Grenades = 20; red.MaxHealth = Big; red.SetHealth(red.MaxHealth);
                OzV11Kit.PlayerAttack(w, red, t);
                bool fired = false, done = false; int guard = 0; string sig = "none";
                while (guard++ < 6000 && !done)
                {
                    w.Tick(ZConst.TICK);
                    foreach (var e in w.Events)
                    {
                        if (!fired && e.type == ZEventType.MissileFired && e.flag && e.objId == red.Id)
                        {
                            fired = true; r.from += OzV11Kit.Dist(red, t); r.flight += e.value;
                            bool dodge = t.Waypoints.Count > 0 && t.Waypoints[0].mode == WpMode.Dodge;
                            if (dodge) r.dodged++;
                            sig = e.x + "," + e.y + "," + ((double)e.value).ToString("R", Inv) + "," + (dodge ? t.Waypoints[0].x + "," + t.Waypoints[0].y : "no sidestep");
                        }
                        else if (fired && e.type == ZEventType.MissileExploded && e.flag && e.objId == red.Id)
                        {
                            int k = Splash((int)e.value, e.radius, e.x, e.y, t);
                            r.n++; r.dmg += k; if (k == 0) r.zero++;
                            r.away += Math.Sqrt((double)(e.x - t.CenterX) * (e.x - t.CenterX) + (double)(e.y - t.CenterY) * (e.y - t.CenterY));
                            sig += "," + t.CenterX + "," + t.CenterY;                              // where the tank is when the grenade lands
                            done = true; break;
                        }
                    }
                    w.Events.Clear();
                }
                r.throws.Add(sig);
            }
            return r;
        }

        static string FirstText(First f)
        {
            double m = Math.Max(1, f.n);
            return $"{f.n} first grenades, thrown from {F(f.from / m)} px, flight {F(f.flight / m, "0.00")} s, the tank is told to step aside {F(100.0 * f.dodged / m)} %, " +
                   $"it lands {F(f.away / m)} px from the tank's centre: {F(f.dmg / m)} HP on average ({F(100.0 * f.dmg / m / Math.Max(1, f.hp), "0.00")} % of its health), did nothing {F(100.0 * f.zero / m)} %";
        }

        // ------------------------------------------------------------------ 4: fights
        public sealed class Fight
        {
            public int fights, targetDead, squadDead, boxEmpty, timeout, throwerBodies; public long thrown, exploded, dmg, zero, hpLeftPct, throwersLeft;
            public List<double> secs = new List<double>(); public List<int> landedAtKill = new List<int>();
            public string Text()
            {
                double ex = Math.Max(1, exploded);
                string s = $"target destroyed {targetDead,3}/{fights}, throwers destroyed first {squadDead,3}, box empty first {boxEmpty,3}" + (timeout > 0 ? $", no end in 180 s {timeout}" : "") +
                           $"; thrown per fight {F((double)thrown / fights)}; per grenade landed {F(dmg / ex)} HP on the target, {F(100.0 * zero / ex)} % did nothing; " +
                           $"target health left {hpLeftPct / fights} %, throwers alive at the end {F((double)throwersLeft / fights)} of {throwerBodies}";
                if (landedAtKill.Count > 0) s += $"; at a kill: {F(landedAtKill.Average())} grenades landed, {F(secs.Average())} s after the first throw";
                return s;
            }
        }

        /// <summary>The thrower squads (the player's, a box of 20 each, ordered to attack) against one target of the CPU.
        /// targetMoves false: the target stands still (targetShoots: it fires back or not).</summary>
        public static Fight Run1(string settings, string plain, string thrower, string target, int dist, int seeds, bool targetMoves, bool targetShoots, int squads = 1)
        {
            var f = new Fight { fights = seeds };
            for (int n = 1; n <= seeds; n++)
            {
                int seed = Seed(n);
                var w = NewWorld(settings, plain, seed);
                var t = OzV11Kit.Place(w, target, GX + dist, GY, Team.Blue);
                var squad = OzV11Kit.Squad(t).ToList();
                if (!targetMoves)
                {
                    int keep = squad[0].Damage; foreach (var m in squad) m.Damage = 0;
                    for (int i = 0; i < 800; i++) { w.Tick(ZConst.TICK); w.Events.Clear(); }
                    foreach (var m in squad) m.Damage = targetShoots ? (m.U != null ? m.U.Damage : keep) : 0;
                    Freeze(t, !targetShoots, false);
                }
                long hpMax = squad.Sum(m => (long)m.MaxHealth);
                var reds = new List<ZObject>();
                for (int q = 0; q < squads; q++)
                {
                    var one = OzV11Kit.Place(w, thrower, GX, GY + (q - (squads - 1) / 2) * 48, Team.Red);
                    one.Grenades = 20; reds.AddRange(OzV11Kit.Squad(one));
                    OzV11Kit.PlayerAttack(w, one, t);
                }
                var redIds = new HashSet<int>(reds.Select(r => r.Id));
                double first = -1, overAt = -1; int guard = 0, th = 0, landed = 0; string end = "timeout";
                var prev = new Dictionary<int, int>();
                while (guard++ < 18000)
                {
                    foreach (var m in squad) prev[m.Id] = m.Health;
                    w.Tick(ZConst.TICK);
                    foreach (var e in w.Events)
                    {
                        if (e.type == ZEventType.MissileFired && e.flag && redIds.Contains(e.objId)) { th++; if (first < 0) first = w.Time; }
                        else if (e.type == ZEventType.MissileExploded && e.flag && redIds.Contains(e.objId))
                        {
                            f.exploded++; landed++; int d = 0;
                            foreach (var m in squad)
                            {
                                if (prev[m.Id] <= 0) continue;                                   // dead before this tick: the rule skips it
                                d += Math.Min(prev[m.Id], Splash((int)e.value, e.radius, e.x, e.y, m));
                            }
                            f.dmg += d; if (d == 0) f.zero++;
                        }
                    }
                    w.Events.Clear();
                    if (squad.All(m => m.Destroyed)) { end = "target"; break; }
                    if (reds.All(r => r.Destroyed)) { end = "squad"; if (overAt < 0) overAt = w.Time; if (!GrenadesInAir(w) || w.Time - overAt > 6) break; continue; }   // the grenades already thrown still land
                    if (th > 0 && reds.Where(r => !r.Destroyed).Sum(r => r.Grenades) == 0 && !GrenadesInAir(w)) { end = "box"; break; }
                }
                f.thrown += th;
                if (end == "target") { f.targetDead++; f.secs.Add(w.Time - first); f.landedAtKill.Add(landed); } else if (end == "squad") f.squadDead++; else if (end == "box") f.boxEmpty++; else f.timeout++;
                f.throwerBodies = reds.Count;
                f.hpLeftPct += squad.Where(m => !m.Destroyed).Sum(m => (long)m.Health) * 100 / hpMax;
                f.throwersLeft += reds.Count(r => !r.Destroyed);
            }
            return f;
        }

        // ------------------------------------------------------------------ 4b: the other way round, the player's vehicle on an attack order
        public sealed class Ordered
        {
            public int fights, vehicleDead, deadAfterLast, squadsDead, timeout; public long thrown, landed, dmg, zero, hpLeftPct, ticks, sidestepTicks; public double secs;
            public string Text()
            {
                double l = Math.Max(1, landed);
                return $"vehicle destroyed {vehicleDead,3}/{fights} ({deadAfterLast} of them by grenades that were in the air when the last robot fell), vehicle left alive {squadsDead,3}" + (timeout > 0 ? $", no end in 180 s {timeout}" : "") +
                       $"; thrown per fight {F((double)thrown / fights)}, landed {F((double)landed / fights)}; per grenade landed {F(dmg / l)} HP on the vehicle, {F(100.0 * zero / l)} % did nothing; " +
                       $"vehicle health left {hpLeftPct / fights} %; it had a sidestep order in {F(100.0 * sidestepTicks / Math.Max(1, ticks), "0.00")} % of the ticks; mean length {F(secs / fights)} s";
            }
        }

        static ZObject Nearest(ZObject from, List<ZObject> all)
        {
            ZObject best = null; double bd = double.MaxValue;
            foreach (var o in all) { if (!OzV11Kit.Alive(o)) continue; double d = OzV11Kit.Dist(from, o); if (d < bd) { bd = d; best = o; } }
            return best;
        }

        /// <summary>The PLAYER's vehicle, ordered to attack, against grunt squads of the CPU that carry a box of 20 grenades each and
        /// attack it with the CPU's own order. A vehicle on a direct order of the player is not told to step aside (DV16:
        /// ZObject.DodgeMissile, HumanDirectOrder); the CPU's vehicles always are. When its target is gone the player orders it onto
        /// the nearest robot left, within 10 ticks, as an attentive player does. Until one side is destroyed or 180 s have passed;
        /// the grenades that are in the air when the last robot falls still land on the vehicle, which then has no order and
        /// stands (a vehicle they destroy counts as destroyed; deadAfterLast counts these, a player who drives it away at once
        /// would save some of them).</summary>
        public static Ordered PlayerVehicle(string settings, string plain, string vehicle, int squads, int seeds)
        {
            var r = new Ordered { fights = seeds };
            for (int n = 1; n <= seeds; n++)
            {
                var w = NewWorld(settings, plain, Seed(n));                                      // red = the player, blue = the CPU
                var v = OzV11Kit.Place(w, vehicle, GX, GY, Team.Red);
                var blues = new List<ZObject>(); ZObject lead = null;
                for (int q = 0; q < squads; q++)
                {
                    var one = OzV11Kit.Place(w, "grunt", GX + 220, GY + (q - (squads - 1) / 2) * 48, Team.Blue);
                    one.Grenades = 20; blues.AddRange(OzV11Kit.Squad(one)); if (lead == null) lead = one;
                    OzV11Kit.CpuAttack(w, one, v);
                }
                var ids = new HashSet<int>(blues.Select(b => b.Id));
                OzV11Kit.PlayerAttack(w, v, lead);
                int guard = 0; string end = "timeout"; double overAt = -1;
                while (guard++ < 18000)
                {
                    int hp0 = v.Health;
                    if (guard % 10 == 0 && !v.Destroyed && !(v.Waypoints.Count > 0 && v.Waypoints[0].mode == WpMode.Attack))
                    { var next = Nearest(v, blues); if (next != null) OzV11Kit.PlayerAttack(w, v, next); }
                    w.Tick(ZConst.TICK);
                    r.ticks++; if (v.Waypoints.Count > 0 && v.Waypoints[0].mode == WpMode.Dodge) r.sidestepTicks++;
                    foreach (var e in w.Events)
                    {
                        if (e.type == ZEventType.MissileFired && e.flag && ids.Contains(e.objId)) r.thrown++;
                        else if (e.type == ZEventType.MissileExploded && e.flag && ids.Contains(e.objId))
                        { r.landed++; int k = hp0 > 0 ? Math.Min(hp0, Splash((int)e.value, e.radius, e.x, e.y, v)) : 0; r.dmg += k; if (k == 0) r.zero++; }
                    }
                    w.Events.Clear();
                    if (v.Destroyed) { end = "vehicle"; if (overAt >= 0) r.deadAfterLast++; break; }
                    if (blues.All(b => b.Destroyed)) { if (overAt < 0) overAt = w.Time; if (!GrenadesInAir(w) || w.Time - overAt > 6) { end = "squads"; break; } }
                }
                if (end == "vehicle") r.vehicleDead++; else if (end == "squads") r.squadsDead++; else r.timeout++;
                r.secs += w.Time;
                r.hpLeftPct += v.Destroyed ? 0 : (long)v.Health * 100 / Math.Max(1, v.MaxHealth);
            }
            return r;
        }

        // ------------------------------------------------------------------ 5: one grenade on a whole squad
        public sealed class OnSquad { public double hp, hurt, killed, apart; }

        public static OnSquad OneOnSquad(string settings, string plain, string type, bool walked, int damage, int radius)
        {
            double pairSum = 0; int pairs = 0, runs = 0; long exact = 0, within = 0, kills = 0; const int j = 24;
            for (int n = 1; n <= 20; n++)
            {
                int seed = Seed(n);
                var w = NewWorld(settings, plain, seed);
                var s = OzV11Kit.Place(w, type, GX, GY, Team.Blue);
                foreach (var m in OzV11Kit.Squad(s)) m.Damage = 0;
                if (walked) w.SendWaypoints(Team.Blue, s, new[] { ZWaypoint.Move(GX + 200, GY + 40, false) });
                for (int i = 0; i < (walked ? 4000 : 1000); i++) { w.Tick(ZConst.TICK); w.Events.Clear(); }
                var sq = OzV11Kit.Squad(s).ToList();
                for (int a = 0; a < sq.Count; a++) for (int b = a + 1; b < sq.Count; b++) { pairSum += OzV11Kit.Dist(sq[a], sq[b]); pairs++; }
                for (int dx = -j; dx < j; dx++) for (int dy = -j; dy < j; dy++)                 // every scatter offset of a throw aimed at the leader (zobject.cpp:2131-2132)
                    foreach (var m in sq) { int k = Splash(damage, radius, s.CenterX + dx, s.CenterY + dy, m); exact += k; if (k > 0) within++; if (k >= m.MaxHealth) kills++; }
                runs++;
            }
            double all = 4.0 * j * j * runs;
            return new OnSquad { hp = exact / all, hurt = within / all, killed = kills / all, apart = pairSum / Math.Max(1, pairs) };
        }

        // ------------------------------------------------------------------ 6: the fort's roof defender
        public sealed class Fort { public int runs, came, lobs, damage = -1, radius = -1, squadsDestroyed; public bool oneKind = true; public long dmg, zero, rifleShots, bodiesLost; public double firstLob; public int firstLobs; public List<double> destroyedAt = new List<double>(); }

        static string FortMap() => OzV11Kit.Plain(96, 96, ("building:fort_front", 40, 10, "blue"));

        /// <summary>A blue fort (its big gun switched off for this row, so the count is the defender's alone) and a red grunt squad
        /// that stands 40 px before its gate. still: the squad cannot shoot or die and, once it has taken its places (8 s), cannot
        /// move, 90 s in all (what his grenades take from it);
        /// else a normal squad ordered to attack the fort, until it is destroyed or 120 s have passed.</summary>
        public static Fort FortGate(string settings, bool still, int seeds)
        {
            var r = new Fort { runs = seeds };
            bool gun = ZWorld.FortGunDeviation; ZWorld.FortGunDeviation = false;
            try
            {
                string map = FortMap();
                for (int n = 1; n <= seeds; n++)
                {
                    int seed = Seed(n);
                    var w = NewWorld(settings, map, seed);
                    var fort = w.Objects.First(o => o.IsFort);
                    if (!fort.GetCreationMovePoint(out int ex, out int ey)) { ex = fort.CenterX; ey = fort.LocY + fort.HeightPix + 16; }
                    var sq = OzV11Kit.Place(w, "grunt", ex, ey + 40, Team.Red);
                    var squad = OzV11Kit.Squad(sq).ToList();
                    if (still) foreach (var m in squad) { m.Damage = 0; m.MaxHealth = Big; m.SetHealth(m.MaxHealth); }   // it may still take its places (a new squad stands on one spot)
                    else OzV11Kit.PlayerAttack(w, sq, fort);
                    double limit = still ? 90 : 120, holdAt = still ? 8 : -1; bool came = false; double firstLob = -1;
                    var prev = new Dictionary<int, int>();
                    while (w.Time < limit)
                    {
                        foreach (var m in squad) prev[m.Id] = m.Health;
                        if (holdAt >= 0 && w.Time >= holdAt) { holdAt = -1; Freeze(sq, true, true); }
                        w.Tick(ZConst.TICK);
                        foreach (var e in w.Events)
                        {
                            var a = w.GetAny(e.objId);
                            if (a == null || !a.IsFortDefender) continue;
                            came = true;
                            if (e.type == ZEventType.Hitscan) r.rifleShots++;
                            else if (e.type == ZEventType.MissileFired && e.flag) { r.lobs++; if (firstLob < 0) firstLob = w.Time; }
                            else if (e.type == ZEventType.MissileExploded && e.flag)
                            {
                                if (r.damage < 0) { r.damage = (int)e.value; r.radius = e.radius; } else if (r.damage != (int)e.value || r.radius != e.radius) r.oneKind = false;
                                int d = 0;
                                foreach (var m in squad) { if (prev[m.Id] <= 0) continue; d += Math.Min(prev[m.Id], Splash((int)e.value, e.radius, e.x, e.y, m)); }
                                r.dmg += d; if (d == 0) r.zero++;
                            }
                        }
                        w.Events.Clear();
                        if (!came && w.Objects.Any(o => o.IsFortDefender)) came = true;
                        if (!still && squad.All(m => m.Destroyed)) { r.squadsDestroyed++; r.destroyedAt.Add(w.Time); break; }
                    }
                    if (came) r.came++;
                    if (firstLob >= 0) { r.firstLob += firstLob; r.firstLobs++; }
                    r.bodiesLost += squad.Count(m => m.Destroyed);
                }
            }
            finally { ZWorld.FortGunDeviation = gun; }
            return r;
        }

        // ------------------------------------------------------------------ 7: what must not change
        /// <summary>The blasts of a destroyed grenade crate (20 grenades in it) and of one Tough rocket: (count, damage, radius) of the crate's
        /// blasts as "n x damage / radius" and the rocket's "damage / radius".</summary>
        public static string Others(string settings, string plain)
        {
            var w = NewWorld(settings, plain, 3);
            var crate = w.CreateObject("map_item", "grenades", GX, GY, Team.Neutral);
            var kinds = new Dictionary<string, int>();
            string rocket = "no rocket";
            if (crate != null)
            {
                crate.Grenades = 20;
                crate.SetHealth(0); w.UpdateObjectHealth(crate);
                foreach (var m in w.QueuedMissiles.Concat(w.PendingMissiles)) if (m.debris && m.attackerId == crate.Id) { string k = m.damage + " / " + m.radius; kinds[k] = kinds.TryGetValue(k, out int n) ? n + 1 : 1; }
            }
            var w2 = NewWorld(settings, plain, 4);
            var t = OzV11Kit.Place(w2, "light", GX + 100, GY, Team.Blue); Freeze(t, true, true);
            var tough = OzV11Kit.Place(w2, "tough", GX, GY, Team.Red, 1);
            OzV11Kit.PlayerAttack(w2, tough, t);
            for (int i = 0; i < 3000 && rocket == "no rocket"; i++)
            {
                w2.Tick(ZConst.TICK);
                foreach (var e in w2.Events) if (e.type == ZEventType.MissileExploded && !e.flag && e.objId == tough.Id) rocket = (int)e.value + " / " + e.radius;
                w2.Events.Clear();
            }
            string c = crate == null ? "no crate could be made" : kinds.Count == 0 ? "the crate threw no blast" : string.Join(", ", kinds.OrderBy(k => k.Key).Select(k => k.Value + " x " + k.Key));
            return "a destroyed grenade crate: " + c + " (damage / radius px); a Tough's rocket: " + rocket;
        }

        // ------------------------------------------------------------------ the whole check
        /// <summary>settings = default_settings. runs = fights per row (100 = the full count; the parity row uses fewer).</summary>
        public static string Run(string settings, int runs = Runs)
        {
            var sb = new StringBuilder();
            string plain = OzV11Kit.Plain(64, 44);
            var saved = new List<KeyValuePair<string, object>>();
            foreach (string name in new[] { "DriverFireDeviation", Switch }) { var fi = typeof(ZWorld).GetField(name); if (fi != null) saved.Add(new KeyValuePair<string, object>(name, fi.GetValue(null))); }
            OzV11Kit.TrySet(typeof(ZWorld), "DriverFireDeviation", true);                       // a game switch that the shared switch list does not hold
            bool had = Rule(true);
            var z = ZSettings.Parse(settings);
            int zodDamage = (int)(z.grenade_damage * ZConst.MAX_UNIT_HEALTH), zodRadius = z.grenade_damage_radius;
            int wantDamage = (zodDamage * 115 + 50) / 100, wantRadius = (zodRadius * 120 + 50) / 100;
            areaDamage = (zodDamage * 90 + 50) / 100; zodDamageNow = zodDamage; zodRadiusNow = zodRadius;
            int seedsA = Math.Max(2, runs * 8 / 100), firsts = Math.Max(20, runs * 3);
            var modes = had ? new[] { false, true } : new[] { false };
            sb.AppendLine($"Thrown grenades, counted with the game's switches on an empty plain map; {runs} fights per row{(SeedSet != 0 ? ", set of worlds " + SeedSet : "")}. 16 px = 2 m.");
            sb.AppendLine($"Zod's grenade (the settings): {zodDamage} HP at the centre, radius {zodRadius} px, linear fall-off. With the rule on: radius {wantRadius} px; a DIRECT hit (the grenade lands on the target's own box) {wantDamage} HP at the centre, an AREA hit {areaDamage} HP at the centre, both with the linear fall-off to {wantRadius} px.");
            if (!had) sb.AppendLine("The rule is not installed (ZWorld.GrenadeBoostDeviation not found): only the 'rule off' rows are run.");
            sb.AppendLine();

            // 1
            var still = new Dictionary<bool, Still>();
            sb.AppendLine($"1. One grunt throws at a light tank that cannot move, shoot or die, 100 px away ({seedsA} worlds x 250 grenades).");
            foreach (bool rule in modes)
            {
                Rule(rule);
                var s = still[rule] = StillTarget(settings, plain, "light", 100, seedsA, 250);
                double n = Math.Max(1, s.grenades);
                sb.AppendLine($"  {Tag(rule)}: {s.grenades} grenades, each {s.damage} HP / radius {s.radius} px{(s.oneKind ? "" : " (NOT ALL THE SAME)")}; on the tank {F(s.sum / n)} HP per grenade, did nothing {F(100.0 * s.zero / n)} %, best {s.max}; " +
                              $"flight {F(s.flight, "0.00")} s, {F(s.gap, "0.000")} s between throws; health really lost {s.healthLost}, by the blast rule {s.sum}: {(s.sameAsRule ? "same" : "DIFFERENT")}; " +
                              $"direct hits {s.direct} ({F(100.0 * s.direct / n)} %), {F(s.directSum / (double)Math.Max(1, s.direct))} HP each; area hits {F(s.areaSum / (double)Math.Max(1, s.grenades - s.direct))} HP each; Zod's grenade on the same blast points {F(s.zodSum / n)} HP per grenade");
            }
            sb.AppendLine();

            // 2
            var own = new Dictionary<bool, Own>();
            sb.AppendLine("2. The thrower's own side: three robots of his side stand at the tank he throws at (on it, 10 px and 20 px beside it), 60 px from him.");
            foreach (bool rule in modes)
            {
                Rule(rule);
                var o = own[rule] = OwnSide(settings, plain, Math.Max(2, seedsA / 2), 100);
                sb.AppendLine($"  {Tag(rule)}: {o.grenades} grenades, {o.covered} of them reached at least one of the three (an enemy there would have lost {o.wouldBe} HP in all); health the thrower and the three really lost: {o.ownLost}");
            }
            sb.AppendLine();

            // 3
            var first = new Dictionary<(bool, string, int), First>();
            sb.AppendLine($"3. The first grenade on a normal tank of the CPU that is free to move ({firsts} worlds per row; the thrower cannot die).");
            foreach (string tg in new[] { "light", "medium" })
                foreach (int d in new[] { 118, 60, 40 })
                    foreach (bool rule in modes)
                    {
                        Rule(rule);
                        var f = first[(rule, tg, d)] = FirstGrenade(settings, plain, tg, d, firsts);
                        sb.AppendLine($"  {Tag(rule)} {tg,-6} set {d,3} px: {FirstText(f)}");
                    }
            sb.AppendLine();

            // 4
            var fights = new Dictionary<(bool, string), Fight>();
            sb.AppendLine("4. Fights (the player's grunt squads, a box of 20 grenades each, ordered to attack one unit of the CPU; the CPU's vehicles are told to step aside).");
            foreach (var c in new (string key, string text, string target, int dist, bool moves, bool shoots, int squads)[]
                     {
                         ("still light", "one squad -> a light tank that stands still and does not shoot", "light", 100, false, false, 1),
                         ("still medium", "one squad -> a medium tank that stands still and does not shoot", "medium", 100, false, false, 1),
                         ("still heavy", "one squad -> a heavy tank that stands still and does not shoot", "heavy", 100, false, false, 1),
                         ("light", "one squad -> a normal light tank (shoots back, free to move)", "light", 220, true, true, 1),
                         ("medium", "one squad -> a normal medium tank (shoots back, free to move)", "medium", 220, true, true, 1),
                         ("3 light", "THREE squads -> a normal light tank (shoots back, free to move)", "light", 220, true, true, 3),
                         ("3 medium", "THREE squads -> a normal medium tank (shoots back, free to move)", "medium", 220, true, true, 3),
                         ("jeep", "one squad -> a normal jeep (shoots back, free to move)", "jeep", 220, true, true, 1),
                         ("grunt", "one squad with a box -> a grunt squad without (shoots back, free to move)", "grunt", 220, true, true, 1),
                         ("psycho", "one squad with a box -> a psycho squad without (shoots back, free to move)", "psycho", 220, true, true, 1),
                         ("gun", "one squad -> a normal gun (shoots back, cannot move)", "gun", 220, true, true, 1),
                         ("2 gun", "TWO squads -> a normal gun (shoots back, cannot move)", "gun", 220, true, true, 2),
                         ("howitzer", "one squad -> a normal howitzer (shoots back, cannot move)", "howitzer", 220, true, true, 1),
                         ("2 howitzer", "TWO squads -> a normal howitzer (shoots back, cannot move)", "howitzer", 220, true, true, 2),
                     })
                foreach (bool rule in modes)
                {
                    Rule(rule);
                    var f = fights[(rule, c.key)] = Run1(settings, plain, "grunt", c.target, c.dist, runs, c.moves, c.shoots, c.squads);
                    sb.AppendLine($"  {Tag(rule)} {c.text}: {f.Text()}");
                }
            sb.AppendLine();

            // 4b
            var ordered = new Dictionary<(bool, string), Ordered>();
            sb.AppendLine("4b. The other way round: the PLAYER's tank on an attack order (a direct order: it is not told to step aside; the CPU's tanks always are) against grunt squads of the CPU, a box of 20 grenades each. The player orders it onto the next robot when its target is gone.");
            foreach (var c in new (string vehicle, int squads)[] { ("light", 1), ("light", 2), ("light", 3), ("medium", 1), ("medium", 2), ("medium", 3) })
                foreach (bool rule in modes)
                {
                    Rule(rule);
                    var o = ordered[(rule, c.vehicle + " " + c.squads)] = PlayerVehicle(settings, plain, c.vehicle, c.squads, runs);
                    sb.AppendLine($"  {Tag(rule)} the player's {c.vehicle,-6} tank on an attack order <- {c.squads} squad(s) of the CPU with a box: {o.Text()}");
                }
            sb.AppendLine();

            // 5
            var onSquad = new Dictionary<(bool, string), OnSquad>();
            sb.AppendLine("5. One grenade on a whole squad (every scatter offset of a throw aimed at the leader; 20 squads per row).");
            foreach (var c in new (string type, bool walked)[] { ("grunt", true), ("grunt", false), ("psycho", true), ("tough", true) })
                foreach (bool rule in modes)
                {
                    Rule(rule);
                    var s = still[rule];
                    var o = onSquad[(rule, c.type + (c.walked ? " walked" : " new"))] = OneOnSquad(settings, plain, c.type, c.walked, s.damage, s.radius);
                    sb.AppendLine($"  {Tag(rule)} {c.type,-6} squad {(c.walked ? "after a 200 px walk" : "that never moved   ")} (members {F(o.apart)} px apart): {F(o.hp)} HP on the squad in all, {F(o.hurt, "0.00")} members hurt, {F(o.killed, "0.00")} members destroyed outright");
                }
            sb.AppendLine();

            // 6
            var fort = new Dictionary<(bool, bool), Fort>();
            int fortRuns = Math.Max(10, runs / 2);
            sb.AppendLine($"6. The fort's roof defender and a grunt squad 40 px before the gate ({fortRuns} worlds per row; the fort's big gun is off in this row).");
            foreach (bool stillSquad in new[] { true, false })
                foreach (bool rule in modes)
                {
                    Rule(rule);
                    var f = fort[(rule, stillSquad)] = FortGate(settings, stillSquad, fortRuns);
                    double n = Math.Max(1, f.lobs);
                    sb.AppendLine($"  {Tag(rule)} {(stillSquad ? "the squad cannot shoot or die and stands still once it has taken its places, 90 s" : "a normal squad ordered to attack the fort, 120 s")}: a defender came up in {f.came}/{f.runs}; grenades thrown {f.lobs} ({F((double)f.lobs / f.runs)} per world, " +
                                  $"the first after {(f.firstLobs > 0 ? F(f.firstLob / f.firstLobs) : "-")} s), each {f.damage} HP / radius {f.radius} px{(f.oneKind ? "" : " (NOT ALL THE SAME)")}; on the squad {F(f.dmg / n)} HP per grenade, did nothing {F(100.0 * f.zero / n)} %; his rifle shots {f.rifleShots}" +
                                  (stillSquad ? "" : $"; squads destroyed {f.squadsDestroyed}/{f.runs}{(f.destroyedAt.Count > 0 ? " after " + F(f.destroyedAt.Average()) + " s" : "")}, robots lost {f.bodiesLost}"));
                }
            sb.AppendLine();

            // 7
            var others = new Dictionary<bool, string>();
            sb.AppendLine("7. What must not change.");
            foreach (bool rule in modes) { Rule(rule); others[rule] = Others(settings, plain); sb.AppendLine($"  {Tag(rule)}: {others[rule]}"); }
            sb.AppendLine();

            // verdicts
            var off = still[false];
            sb.AppendLine(OzV11Kit.Verdict(off.damage == zodDamage && off.radius == zodRadius && off.oneKind && off.sameAsRule, $"rule off: every thrown grenade is Zod's ({off.damage} HP, {off.radius} px; wanted {zodDamage}, {zodRadius})"));
            sb.AppendLine(OzV11Kit.Verdict(own[false].ownLost == 0 && own[false].covered > 0, $"rule off: the thrower's own side lost {own[false].ownLost} HP under {own[false].covered} blasts that reached it"));
            if (had)
            {
                var on = still[true];
                sb.AppendLine(OzV11Kit.Verdict(on.damage == wantDamage && on.radius == wantRadius && on.oneKind && on.sameAsRule, $"rule on: every thrown grenade has {on.damage} HP and {on.radius} px (wanted {wantDamage}, {wantRadius}), and the health lost is the blast rule's"));
                sb.AppendLine(OzV11Kit.Verdict(on.sum > off.sum && on.zero <= off.zero && Math.Abs(on.flight - off.flight) < 1e-9 && Math.Abs(on.gap - off.gap) < 1e-9,
                    $"rule on: a still tank takes {F((double)on.sum / Math.Max(1, on.grenades))} HP per grenade (rule off {F((double)off.sum / Math.Max(1, off.grenades))}); flight time and time between throws unchanged"));
                sb.AppendLine(OzV11Kit.Verdict(on.direct > 0 && on.direct == off.direct && on.direct < on.grenades && on.sameAsRule,
                    $"rule on: the tank is judged by where each grenade lands: {on.direct} of {on.grenades} blasts on its body (rule off: the same {off.direct} blast points), {F(on.directSum / (double)Math.Max(1, on.direct))} HP per direct hit, {F(on.areaSum / (double)Math.Max(1, on.grenades - on.direct))} HP per area hit; the health lost is the rule's"));
                sb.AppendLine(OzV11Kit.Verdict(own[true].ownLost == 0 && own[true].covered >= own[false].covered && own[true].covered > 0, $"rule on: the thrower's own side lost {own[true].ownLost} HP under {own[true].covered} blasts that reached it"));
                int same = 0, all = 0, dodgeOn = 0, dodgeOff = 0, n = 0;
                foreach (var k in first.Keys.Where(q => q.Item1).ToList())
                {
                    var a = first[k]; var b = first[(false, k.Item2, k.Item3)];
                    for (int i = 0; i < a.throws.Count && i < b.throws.Count; i++) { all++; if (a.throws[i] == b.throws[i]) same++; }
                    dodgeOn += a.dodged; dodgeOff += b.dodged; n += a.n;
                }
                sb.AppendLine(OzV11Kit.Verdict(all > 0 && same == all && dodgeOn == dodgeOff && dodgeOn == n,
                    $"stepping aside is untouched: of {all} first throws at a tank, {same} have the same landing point, flight time, sidestep point and tank position at the blast with the rule on and off; told to step aside {dodgeOn} of {n} (rule off {dodgeOff})"));
                var f118on = first[(true, "light", 118)]; var f118off = first[(false, "light", 118)];
                sb.AppendLine(OzV11Kit.Verdict(f118on.dmg >= f118off.dmg && f118on.zero <= f118off.zero,
                    $"the first grenade from the squad's range on a normal light tank: {F((double)f118on.dmg / Math.Max(1, f118on.n))} HP, did nothing {F(100.0 * f118on.zero / Math.Max(1, f118on.n))} % (rule off {F((double)f118off.dmg / Math.Max(1, f118off.n))} HP, {F(100.0 * f118off.zero / Math.Max(1, f118off.n))} %)"));
                bool fightsOk = true; string worse = "";
                foreach (var k in fights.Keys.Where(q => q.Item1).ToList()) if (fights[k].targetDead < fights[(false, k.Item2)].targetDead - Math.Max(3, runs / 10)) { fightsOk = false; worse += " " + k.Item2; }
                sb.AppendLine(OzV11Kit.Verdict(fightsOk, $"no fight row is won clearly less often with the rule on{(worse.Length > 0 ? " (worse:" + worse + ")" : "")}: three squads on a light tank {fights[(true, "3 light")].targetDead}/{runs} (rule off {fights[(false, "3 light")].targetDead}), " +
                    $"a squad with a box on a grunt squad {fights[(true, "grunt")].targetDead}/{runs} (rule off {fights[(false, "grunt")].targetDead})"));
                bool orderedOk = true; string orderedWorse = "";
                foreach (var k in ordered.Keys.Where(q => q.Item1).ToList()) if (ordered[k].vehicleDead < ordered[(false, k.Item2)].vehicleDead - Math.Max(3, runs / 10)) { orderedOk = false; orderedWorse += " " + k.Item2; }
                sb.AppendLine(OzV11Kit.Verdict(orderedOk, $"the other way round, the player's tank on an attack order (it is not told to step aside): no row is lost clearly less often with the rule on{(orderedWorse.Length > 0 ? " (less often:" + orderedWorse + ")" : "")}: " +
                    $"three squads of the CPU destroy the player's medium tank {ordered[(true, "medium 3")].vehicleDead}/{runs} (rule off {ordered[(false, "medium 3")].vehicleDead}), two squads {ordered[(true, "medium 2")].vehicleDead}/{runs} (rule off {ordered[(false, "medium 2")].vehicleDead}); " +
                    $"two squads destroy its light tank {ordered[(true, "light 2")].vehicleDead}/{runs} (rule off {ordered[(false, "light 2")].vehicleDead}); two squads of the player destroy a gun of the CPU {fights[(true, "2 gun")].targetDead}/{runs} (rule off {fights[(false, "2 gun")].targetDead}), " +
                    $"a howitzer {fights[(true, "2 howitzer")].targetDead}/{runs} (rule off {fights[(false, "2 howitzer")].targetDead})"));
                var gOn = onSquad[(true, "grunt walked")]; var gOff = onSquad[(false, "grunt walked")];
                sb.AppendLine(OzV11Kit.Verdict(gOn.hp > gOff.hp, $"one grenade on a grunt squad that walked: {F(gOn.hp)} HP (rule off {F(gOff.hp)}), members destroyed outright {F(gOn.killed, "0.00")} (rule off {F(gOff.killed, "0.00")})"));
                var fdOn = fort[(true, true)]; var fdOff = fort[(false, true)];
                sb.AppendLine(OzV11Kit.Verdict(fdOn.lobs > 0 && fdOn.damage == wantDamage && fdOn.radius == wantRadius && fdOn.oneKind && fdOff.damage == zodDamage && fdOff.radius == zodRadius && fdOn.lobs == fdOff.lobs,
                    $"the fort defender's grenades: {fdOn.lobs} thrown, each {fdOn.damage} HP / {fdOn.radius} px (rule off {fdOff.lobs} thrown, {fdOff.damage} HP / {fdOff.radius} px); on the squad {F(fdOn.dmg / (double)Math.Max(1, fdOn.lobs))} HP per grenade (rule off {F(fdOff.dmg / (double)Math.Max(1, fdOff.lobs))})"));
                sb.AppendLine(OzV11Kit.Verdict(others[true] == others[false] && others[true].Contains("20 x " + zodDamage + " / 40"), "a destroyed grenade crate and a Tough's rocket are the same with the rule on and off: " + others[true]));
            }
            foreach (var kv in saved) OzV11Kit.TrySet(typeof(ZWorld), kv.Key, kv.Value);
            return sb.ToString();
        }

#if UNITY_EDITOR
        [UnityEditor.MenuItem("OpenZ/V1.1/Count Grenades (b31)")]
        static void Menu() => OzV11Kit.RunMenu("Count Grenades (b31)", () => Run(OzV11Kit.SettingsText));
#endif
    }
}
