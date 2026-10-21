using System;
using System.Collections.Generic;

namespace OpenZ.Sim
{
    /// <summary>
    /// DV117, version 1.1: burning ground hurts an enemy who stays in it, very little but never nothing.
    ///   where      every Pyro shot (a hit or a miss; a squad member's own and a tank hatch driver's) lights the ground at what it
    ///              aims at: a robot's feet, the near side of a vehicle or gun (12 px toward the Pyro), the foot of a building's wall
    ///              (the nearest wall point, 8 px toward the Pyro). Never on a water tile. A shot within GroundFireMergePx of a live
    ///              patch of the same team only keeps that patch burning (GroundFireLife s from the last shot, the drawn patch's
    ///              7 s); the patch never moves (as the drawn one, View/Pyro/OpenZPyroGroundFire.cs, Pinned).
    ///   who        a robot on foot or a vehicle (crewed: an empty one is neutral) of the OTHER team whose centre is within
    ///              GroundFireRadiusPx of a patch's centre. Never the patch's own team, never guns, buildings, map items, a fort's
    ///              roof defender, a robot inside a vehicle or fort (it is not on the map then).
    ///   how much   one bite after each whole game second spent inside (the first one second after it stepped in; stepping out
    ///              starts the count again): GroundFireRobotHp for a robot (16 HP = 1.5 % of a grunt's 1081, 0.5 % of a Tough's
    ///              3378), GroundFireVehicleHp for a vehicle (8 HP = 0.5 % of a jeep's 1756, 0.1 % of a medium tank's 6756).
    ///              Several patches at one place bite once. A unit brought to 0 dies a fire death; the patch's last Pyro gets the
    ///              kill (veterancy, as a shot's).
    /// Deterministic: whole pixels, the sim clock, list order; no random draw (Zod's rand() stream is untouched), no events.
    /// Switch: ZWorld.GroundFireDeviation (the game sets it at every start: on, off in 'Zod exact'; headless default off, so every
    /// parity row and counted check runs without it). With the switch off no patch is made and nothing changes, tick for tick.
    /// The drawn fire (View/Pyro/OpenZPyroFire.cs) is not changed: it lights its patches at the same shots, within about 2 m.
    /// </summary>
    public sealed partial class ZWorld
    {
        public static bool GroundFireDeviation = false;
        /// <summary>The bite per whole second inside (HP), the seconds between bites, a patch's life after its last shot (s), its
        /// radius and the reach within which a new shot keeps a live patch burning (px, 8 px = 1 m), the patches kept at once.</summary>
        public const int GroundFireRobotHp = 16, GroundFireVehicleHp = 8;
        public const double GroundFireEvery = 1.0, GroundFireLife = 7.0;
        public const int GroundFireRadiusPx = 28, GroundFireMergePx = 28, GroundFireMax = 64;

        /// <summary>One burning patch: its centre (px), the team whose Pyro lit it, that Pyro (the last one that shot into it; the
        /// kill goes to it), when it was lit and when it goes out (game s).</summary>
        public sealed class GroundFire { public int x, y, by; public Team team; public double lit, until; }

        /// <summary>The patches burning now (oldest first).</summary>
        public readonly List<GroundFire> GroundFires = new List<GroundFire>();
        /// <summary>This world's patches lit and HP taken by burning ground so far (checks).</summary>
        public int GroundFiresLit, GroundFireDealt;

        /// <summary>Lights the ground at (x, y) for team's Pyro <paramref name="by"/> (or keeps the live patch there burning).</summary>
        public void GroundFireAt(int x, int y, Team team, int by = -1)
        {
            if (!GroundFireDeviation || team == Team.Neutral) return;
            if (Map.ClassAt(x >> 4, y >> 4) == ZClass.Water || x < 0 || y < 0) return;          // fire over water: steam only (the view's rule)
            GroundFire best = null; long bestD = long.MaxValue, reach = (long)GroundFireMergePx * GroundFireMergePx;
            foreach (var f in GroundFires)
            {
                if (f.team != team) continue;
                long dx = f.x - x, dy = f.y - y, d = dx * dx + dy * dy;
                if (d <= reach && d < bestD) { best = f; bestD = d; }
            }
            if (best != null) { best.until = Math.Max(best.until, Time + GroundFireLife); best.by = by; return; }
            if (GroundFires.Count >= GroundFireMax)
            {   // the one closest to its end makes room (the first of them in list order)
                int k = 0;
                for (int i = 1; i < GroundFires.Count; i++) if (GroundFires[i].until < GroundFires[k].until) k = i;
                GroundFires.RemoveAt(k);
            }
            GroundFires.Add(new GroundFire { x = x, y = y, team = team, by = by, lit = Time, until = Time + GroundFireLife });
            GroundFiresLit++;
        }

        /// <summary>The first patch of another team under o's centre, null if none.</summary>
        public GroundFire GroundFireUnder(ZObject o)
        {
            long r2 = (long)GroundFireRadiusPx * GroundFireRadiusPx;
            foreach (var f in GroundFires)
            {
                if (f.team == o.Owner) continue;
                long dx = f.x - o.CenterX, dy = f.y - o.CenterY;
                if (dx * dx + dy * dy <= r2) return f;
            }
            return null;
        }

        /// <summary>Once a tick (Tick, after the run-overs): patches go out, then every enemy inside one gets his bite per whole second.</summary>
        void GroundFireTick()
        {
            if (GroundFires.Count == 0) return;
            for (int i = GroundFires.Count - 1; i >= 0; i--) if (Time >= GroundFires[i].until) GroundFires.RemoveAt(i);
            if (GroundFires.Count == 0 || GameOver) return;
            int n = Objects.Count;
            for (int i = 0; i < n && i < Objects.Count; i++)
            {
                var o = Objects[i];
                if (!o.IsMobile || o.Owner == Team.Neutral || o.Destroyed || o.KillAt >= 0 || o.IsFortDefender || o.MaxHealth <= 0 || o.ShuttleAirborne) continue;   // DV118: a flying shuttle is not in the burning ground
                var f = GroundFireUnder(o);
                if (f == null) continue;
                if (o.GroundFireSeen != TickIndex - 1) o.GroundFireNext = Time + GroundFireEvery;   // stepped in this tick: the first bite one second on
                o.GroundFireSeen = TickIndex;
                if (Time + 1e-9 < o.GroundFireNext) continue;
                o.GroundFireNext += GroundFireEvery;
                int hp0 = o.Health;
                o.DamageHealth(o.IsRobot ? GroundFireRobotHp : GroundFireVehicleHp);
                o.LastFireHit = Time;                                                              // a death in it is a fire death
                o.GroundFireTaken += hp0 - o.Health; GroundFireDealt += hp0 - o.Health;
                UpdateObjectHealth(o, f.by);
            }
        }

        /// <summary>Turns the rule off until it is disposed, then puts back what was there. The parity report holds one while it
        /// measures (a Play session leaves the switch on); its own row turns the rule on by itself.</summary>
        public sealed class GroundFireHold : IDisposable
        {
            readonly bool was;
            internal GroundFireHold() { was = GroundFireDeviation; GroundFireDeviation = false; }
            public void Dispose() { GroundFireDeviation = was; }
        }
        public static GroundFireHold GroundFireOff() => new GroundFireHold();
    }

    public partial class ZObject
    {
        /// <summary>DV117 (ZWorld.GroundFire.cs): the tick this unit was last inside an enemy patch, when its next bite is due, the HP
        /// burning ground has taken from it so far.</summary>
        internal long GroundFireSeen = -10;
        internal double GroundFireNext;
        public int GroundFireTaken;

        /// <summary>DV117: a Pyro's shot at t (a hit or a miss) lights the ground at what it aims at: a robot's feet, the near side of
        /// a vehicle or gun, the foot of a building's wall. No random draw.</summary>
        internal void GroundFireShot(ZObject t)
        {
            if (!ZWorld.GroundFireDeviation || t == null || Owner == Team.Neutral || t.ShuttleAirborne) return;   // DV118: a flame at the flying shuttle lights nothing on the ground under it (the view draws none)
            int x = t.CenterX, y = t.CenterY;
            if (t.Kind == ZKind.Building) { t.WallPoint(CenterX, CenterY, out x, out y); GroundFireStep(ref x, ref y, CenterX, CenterY, 8); }
            else if (t.Kind != ZKind.Robot) GroundFireStep(ref x, ref y, CenterX, CenterY, 12);
            World.GroundFireAt(x, y, Owner, Id);
        }

        /// <summary>(x, y) moved px toward (fx, fy), never past it.</summary>
        static void GroundFireStep(ref int x, ref int y, int fx, int fy, int px)
        {
            int dx = fx - x, dy = fy - y;
            if (dx == 0 && dy == 0) return;
            double l = Math.Sqrt((double)dx * dx + (double)dy * dy);
            if (l <= px) { x = fx; y = fy; return; }
            x += (int)Math.Round(dx / l * px); y += (int)Math.Round(dy / l * px);
        }
    }
}
