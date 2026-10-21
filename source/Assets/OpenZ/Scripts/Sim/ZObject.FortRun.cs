using System;

namespace OpenZ.Sim
{
    /// <summary>
    /// OpenZ deviation DV102 (version 1.1, the fort run): robots of a human player ordered into the ENEMY fort through its door run in,
    /// fast, one after the other, without stumbling. Zod walks an ENTER_FORT robot to the gate's exit point and then inside
    /// (zobject.cpp:2774-2913); DV34 (ZObject.BoardingNB.cs) already lets a robot sprint through every unit, without dodging or turning
    /// off to fight, within FortRushRadius (112 px, 7 tiles, straight distance) of that point and inside the gate. A crowd of several
    /// squads still converged on the one exit pixel under the spacing layer before that ring: its members queued, side-stepped and gave
    /// way to each other on the tiles before it (counted: about five heading reversals per body on the Desert with eight squads).
    /// DV102 adds a WIDE RING for a robot of a HUMAN team on an ENTER_FORT order of an enemy fort, still walking to the gate: the way
    /// left along its own path to the exit point is at most FortRunRadius (256 px, 16 tiles; along the path, so a robot behind a wall or
    /// across a swamp is not "near" before its way really is). Outside DV34's ring, in the wide ring:
    /// - it passes through units of its OWN team and they through it (no spacing, queue, side-step or "make way" between them,
    ///   ZPathing.FortRunPair); it never steps aside from a shell (DodgeMissile) and is never sent out of a vehicle's way (ZYield);
    /// - towards ENEMY units everything stays as today: spacing, side-steps round them, being run over; it keeps its own speed (the
    ///   sprint stays DV34's last 7 tiles: counted, a sprint from 16 tiles brought the whole column to the door at once and drew the
    ///   fort's fire onto it, so the first robot in the gate was shot more often on the Jungle).
    /// In both rings, every 0.1 s, it runs straight at the farthest point of its way (the exit point at the end) whose straight line is
    /// CLEAR: Zod's own DirectPathPossible (walls, rocks, buildings, with the body's margin), no water under or beside the line, and not
    /// slower than its path to that point (each stretch weighted by its ground as Zod's search weighs it: road 1 / 1.689, water 1 / 0.7,
    /// the jungle swamp double for robots). So no jog of the tile grid makes it turn back and forth, and it keeps its path round water.
    /// Computer teams keep DV34 alone (their attacks stay as strong as before, like DV35's human-only rule).
    /// Off (Zod exact, the parity rows): DV34 exactly as before.
    /// </summary>
    public partial class ZObject
    {
        /// <summary>DV102: the way left along its path to the fort gate's exit point (px) within which a human team's robot on an
        /// ENTER_FORT order runs in (16 tiles; DV34's own ring is 112 px of straight distance).</summary>
        public static int FortRunRadius = 256;
        /// <summary>DV102 statistics (checks): ticks a robot spent in the wide ring outside DV34's own ring.</summary>
        public static int FortRunTicks;
        /// <summary>DV102 statistics (checks): ways cut to a clear straight line.</summary>
        public static int FortRunStraights;
        long fortRunTick = -10;
        double fortRunNextCut;

        /// <summary>DV102: this robot runs in the wide ring (outside DV34's ring) this tick; like DV34's flag it holds until its next step.</summary>
        public bool FortRunOn => World != null && fortRunTick >= World.TickIndex - 1;

        /// <summary>DV102, called by DV34's FortRushStep (ProcessEnterFortWP, every tick of an ENTER_FORT order, after DV34's own test).</summary>
        void FortRunStep(ZObject t)
        {
            fortRunTick = -10;
            if (!ZWorld.FortRunDeviation || !ZWorld.FortRushDeviation || World == null || !IsRobot || t == null || !t.CanEnterFort(Owner)) return;
            if (!World.IsHumanTeam(Owner) || cur.stage != Stage.GotoEntrance || !cur.gotPfResponse) return;
            if (FortWayLeft() > FortRunRadius) return;
            if (!fortRush) { fortRunTick = World.TickIndex; FortRunTicks++; }
            if (World.Time < fortRunNextCut || cur.pfPoints == null || cur.pfPoints.Count == 0 || PS.wait != ZPathWait.None) return;   // never in the middle of a step round an enemy
            fortRunNextCut = World.Time + 0.1;
            FortRunCut();
        }

        /// <summary>The way left along the path to the gate's exit point (px).</summary>
        double FortWayLeft()
        {
            double d = PathRemaining();
            var f = PathFinalPoint;
            if (f.x != cur.exitX || f.y != cur.exitY) d += Math.Sqrt((double)(f.x - cur.exitX) * (f.x - cur.exitX) + (double)(f.y - cur.exitY) * (f.y - cur.exitY));
            return d;
        }

        /// <summary>Run straight at the farthest point of the way whose straight line is clear and not slower than the path to it.</summary>
        void FortRunCut()
        {
            int sx = LocX + 8, sy = LocY + 8, n = cur.pfPoints.Count;
            var cost = new double[n + 1];                                                       // path cost (px of open ground) from here to pfPoints[k]; [n] = the exit point
            double c = FortLegCost(sx, sy, cur.x, cur.y);
            int px = cur.x, py = cur.y;
            for (int k = 0; k < n; k++) { var p = cur.pfPoints[k]; c += FortLegCost(px, py, p.x, p.y); cost[k] = c; px = p.x; py = p.y; }
            cost[n] = c + FortLegCost(px, py, cur.exitX, cur.exitY);
            bool exitIsLast = px == cur.exitX && py == cur.exitY;
            for (int k = exitIsLast ? n - 1 : n; k >= 0; k--)
            {
                int tx = k == n ? cur.exitX : cur.pfPoints[k].x, ty = k == n ? cur.exitY : cur.pfPoints[k].y;
                if (!FortLineClear(sx, sy, tx, ty, cost[k])) continue;
                if (k == n) cur.pfPoints.Clear();                                              // the exit point itself (Zod's stage goes on there)
                else cur.pfPoints.RemoveRange(0, k + 1);
                SetTarget(tx, ty); SetVelocity(); FortRunStraights++;
                return;
            }
        }

        /// <summary>The ground's travel weight at a pixel against open ground, as Zod's search weighs it (road 1 / 1.689, water 1 / 0.7,
        /// the jungle swamp double for robots); -1 = closed, deep water or outside the map.</summary>
        double FortGround(int x, int y)
        {
            if (x < 0 || y < 0) return -1;
            int tx = x >> 4, ty = y >> 4;
            if (tx >= World.Map.width || ty >= World.Map.height) return -1;
            var k = World.Map.ClassAt(tx, ty);
            if (k == ZClass.DeepWater || k == ZClass.Impassable) return -1;
            if (k == ZClass.Water) return (1 / 0.7) * World.Path.WaterCost;
            if (k == ZClass.Road) return 1 / 1.689;
            return 1;
        }

        /// <summary>One straight stretch's cost (px of open ground), sampled every 4 px.</summary>
        double FortLegCost(int ax, int ay, int bx, int by)
        {
            double dx = bx - ax, dy = by - ay, len = Math.Sqrt(dx * dx + dy * dy);
            if (len < 1) return 0;
            int steps = Math.Max(1, (int)Math.Ceiling(len / 4)); double c = 0, seg = len / steps;
            for (int i = 0; i < steps; i++)
            {
                double f = FortGround((int)(ax + dx * (i + 0.5) / steps), (int)(ay + dy * (i + 0.5) / steps));
                c += seg * (f < 0 ? 1 : f);
            }
            return c;
        }

        /// <summary>The straight line to (ex, ey) is clear (Zod's DirectPathPossible with the rocks closed), dry ground or road under the
        /// body and 6 px to each side, and costs no more than the path to that point.</summary>
        bool FortLineClear(int sx, int sy, int ex, int ey, double pathCost)
        {
            if (!World.Path.DirectPathPossible(sx, sy, ex, ey, true, false)) return false;
            double dx = ex - sx, dy = ey - sy, len = Math.Sqrt(dx * dx + dy * dy);
            if (len < 1) return true;
            double nx = -dy / len * 6, ny = dx / len * 6, c = 0;
            int steps = Math.Max(1, (int)Math.Ceiling(len / 4)); double seg = len / steps;
            for (int i = 0; i < steps; i++)
            {
                double x = sx + dx * (i + 0.5) / steps, y = sy + dy * (i + 0.5) / steps;
                double f = FortGround((int)x, (int)y), l = FortGround((int)(x + nx), (int)(y + ny)), r = FortGround((int)(x - nx), (int)(y - ny));
                if (f < 0 || f > 1 || l < 0 || l > 1 || r < 0 || r > 1) return false;          // water or a closed tile: the path's way round stays
                c += seg * f;
            }
            return c <= pathCost + 2;
        }
    }

    public sealed partial class ZPathing
    {
        /// <summary>DV102: two units of one team, one of them running in the wide ring of the enemy fort: they pass through each other.</summary>
        public static bool FortRunPair(ZObject a, ZObject b) => (a.FortRunOn || b.FortRunOn) && a.Owner == b.Owner && a.Owner != Team.Neutral;
    }

    public sealed partial class ZWorld
    {
        /// <summary>DV102 the fort run (off in the rule file; View/ZGame.cs turns it on at every start unless 'Zod exact').</summary>
        public static bool FortRunDeviation = false;
    }
}
