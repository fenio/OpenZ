using System;
using System.Collections.Generic;

namespace OpenZ.Sim
{
    /// <summary>
    /// OpenZ deviation DV103 (version 1.1, walking round rocks). Zod plans the path of a unit that carries explosives (a Tough, a
    /// robot with grenades, a vehicle with a missile gun) on the grid on which destroyable rocks are open ground
    /// (zpath_finding.cpp:810-900, the "no rocks" grids), and the unit blasts every rock that stands on that line or path when it
    /// bumps into it (zobject.cpp:3716-3747), even when the rock only clips a corner and the way round is hardly longer.
    /// With DV103, when such a unit's path or straight line runs through a destroyable rock, its way round on the normal grid is
    /// measured too; when the way round exists and is at most RockRatio times as long AND at most RockExtra px longer, the unit
    /// walks or drives round (it plans on the normal grid); otherwise (no way round, or clearly longer) it blasts as in Zod.
    /// The choice is kept for the same goal for RockKeep s (no change of mind while it walks). Lengths are geometric (tile steps,
    /// 16 px a side, 22.6 px a diagonal, each step weighted by its ground as Zod's search weighs it: a road shorter, water longer),
    /// measured by the rule's own small search; Zod's own search is not touched.
    /// Every team alike. Off (Zod exact, the parity rows): Zod's choice exactly.
    /// </summary>
    public sealed partial class ZPathing
    {
        /// <summary>DV103: the way round is taken when it is at most this many times as long as the way through the rocks ...</summary>
        public static double RockRatio = 1.25;
        /// <summary>... and at most this many px longer (8 tiles).</summary>
        public static double RockExtra = 128;
        /// <summary>DV103: a choice holds for the same goal (within 2 tiles) this long, s.</summary>
        public static double RockKeep = 20;
        /// <summary>DV103 statistics: choices made (a rock in the way), ways round taken, blasts kept (no way round / clearly longer).</summary>
        public int RockChoices, RockRounds, RockBlastsKept;

        readonly Dictionary<int, (int gx, int gy, bool round, double until)> rockChoice = new Dictionary<int, (int, int, bool, double)>();

        /// <summary>DV103: the "has explosives" argument of the unit's path search (Zod: HasExplosives()). False = plan on the normal grid
        /// (rocks are walls): the way round.</summary>
        public bool PlansThroughRocks(ZObject o, int sx, int sy, int ex, int ey)
        {
            bool zod = o.HasExplosives();
            if (!zod || !ZWorld.RockRoundDeviation || !Enabled) return zod;
            try { return !RockRoundCore(o, sx, sy, ex, ey); } catch (Exception e) { Fail(e); return zod; }
        }

        bool RockRoundCore(ZObject o, int sx, int sy, int ex, int ey)
        {
            double now = W.Time;
            if (rockChoice.TryGetValue(o.Id, out var c) && now < c.until && Math.Abs(c.gx - ex) <= 32 && Math.Abs(c.gy - ey) <= 32) return c.round;
            if (!RockMeasure(o.IsRobot, sx, sy, ex, ey, out double through, out double round)) return false;   // nothing to choose: no rock on its way
            bool take = round >= 0 && round <= through * RockRatio && round - through <= RockExtra;
            RockChoices++; if (take) RockRounds++; else RockBlastsKept++;
            rockChoice[o.Id] = (ex, ey, take, now + RockKeep);
            if (rockChoice.Count > 512) { var old = new List<int>(); foreach (var kv in rockChoice) if (kv.Value.until <= now) old.Add(kv.Key); foreach (var k in old) rockChoice.Remove(k); }
            Log(o, $"DV103 rocks between ({sx},{sy}) and ({ex},{ey}): through {through:F0} px, round {(round < 0 ? "none" : round.ToString("F0"))} px: {(take ? "walks round" : "blasts")}");
            return take;
        }

        /// <summary>DV103's measure for the checks: the way through the rocks and the way round (px, -1 = none) between two points, and
        /// whether the way through really crosses a rock; false = nothing to choose (a clear line without the rocks).</summary>
        public bool RockMeasure(bool robot, int sx, int sy, int ex, int ey, out double through, out double round)
        {
            through = round = -1;
            var pf = W.Path;
            if (pf.DirectPathPossible(sx, sy, ex, ey, robot, false)) return false;          // a clear line without the rocks: nothing to choose
            bool direct = pf.DirectPathPossible(sx, sy, ex, ey, robot, true);            // Zod's straight line through the rocks
            through = RockLen(sx, sy, ex, ey, robot, true, out bool crosses);
            if (direct) crosses = LineCrossesRock(sx, sy, ex, ey, robot);
            if (through < 0 || !crosses) return false;                                     // no way at all, or its way does not touch a rock
            round = RockLen(sx, sy, ex, ey, robot, false, out _);
            return true;
        }

        /// <summary>The straight line (Zod's DirectPathPossible on the rock grid) runs over a destroyable rock tile.</summary>
        bool LineCrossesRock(int sx, int sy, int ex, int ey, bool robot)
        {
            double dx = ex - sx, dy = ey - sy, len = Math.Sqrt(dx * dx + dy * dy);
            int n = (int)(len / 4) + 1;
            for (int i = 0; i <= n; i++)
            {
                double f = Math.Min(1.0, i * 4 / Math.Max(len, 1e-9));
                int tx = (int)((sx + dx * f) / 16), ty = (int)((sy + dy * f) / 16);
                if (IsRockTile(tx, ty, robot)) return true;
            }
            return false;
        }

        bool IsRockTile(int tx, int ty, bool robot) => W.Path.TilePassableGrid(tx, ty, robot, true) && !W.Path.TilePassableGrid(tx, ty, robot, false);

        int[] rkG; int[] rkFrom; bool[] rkClosed; readonly List<int> rkTouched = new List<int>();
        readonly List<(int f, int i)> rkHeap = new List<(int, int)>();
        void HeapPush(int f, int i)
        {
            rkHeap.Add((f, i)); int k = rkHeap.Count - 1;
            while (k > 0) { int p = (k - 1) >> 1; if (rkHeap[p].f <= rkHeap[k].f) break; var x = rkHeap[p]; rkHeap[p] = rkHeap[k]; rkHeap[k] = x; k = p; }
        }
        int HeapPop()
        {
            int top = rkHeap[0].i, last = rkHeap.Count - 1; rkHeap[0] = rkHeap[last]; rkHeap.RemoveAt(last);
            int k = 0, n = rkHeap.Count;
            while (true)
            {
                int l = 2 * k + 1, r = l + 1, m = k;
                if (l < n && rkHeap[l].f < rkHeap[m].f) m = l;
                if (r < n && rkHeap[r].f < rkHeap[m].f) m = r;
                if (m == k) break;
                var x = rkHeap[m]; rkHeap[m] = rkHeap[k]; rkHeap[k] = x; k = m;
            }
            return top;
        }
        const int RockMaxExpansions = 30000;

        /// <summary>The travel weight of a tile against open ground, as Zod's search weighs it (zpath_finding.cpp: a road 1 / 1.689, water
        /// 1 / 0.7, and on a level whose swamp weighs double for robots, twice that for a robot).</summary>
        double StepWeight(int tx, int ty, bool robot)
        {
            var c = W.Map.ClassAt(tx, ty);
            if (c == ZClass.Water) return (1 / 0.7) * (robot ? W.Path.WaterCost : 1f);
            if (c == ZClass.Road) return 1 / 1.689;
            return 1;
        }

        /// <summary>Length (px of open ground: each tile step weighted as above) of the shortest 8-direction tile path from (sx, sy) to (ex, ey) on the grid with (rocks open) or
        /// without the rocks, by the same step rule as Zod's search (ZPathFinder.StepOk); -1 = none. crosses = the path runs over a rock.</summary>
        double RockLen(int sx, int sy, int ex, int ey, bool robot, bool rocksOpen, out bool crosses)
        {
            crosses = false;
            var pf = W.Path; int w = pf.W, h = pf.H, n = w * h;
            int ax = sx / 16, ay = sy / 16, bx = ex / 16, by = ey / 16;
            if (ax < 0 || ay < 0 || ax >= w || ay >= h || bx < 0 || by < 0 || bx >= w || by >= h) return -1;
            if (!pf.StepOk(ax, ay, ax, ay, robot, rocksOpen) || !pf.StepOk(bx, by, bx, by, robot, rocksOpen)) return -1;
            if (rkG == null || rkG.Length != n) { rkG = new int[n]; rkFrom = new int[n]; rkClosed = new bool[n]; for (int i = 0; i < n; i++) rkG[i] = int.MaxValue; }
            foreach (int i in rkTouched) { rkG[i] = int.MaxValue; rkClosed[i] = false; }
            rkTouched.Clear(); rkHeap.Clear();
            int start = ay * w + ax, goal = by * w + bx;
            rkG[start] = 0; rkFrom[start] = -1; rkTouched.Add(start);
            int H(int i) { int dx = Math.Abs(i % w - bx), dy = Math.Abs(i / w - by); return (int)((10 * Math.Max(dx, dy) + 4 * Math.Min(dx, dy)) / 1.689); }
            int expansions = 0;
            HeapPush(H(start), start);
            while (rkHeap.Count > 0 && expansions++ < RockMaxExpansions)
            {
                int cur = HeapPop();
                if (rkClosed[cur]) continue;
                rkClosed[cur] = true;
                if (cur == goal)
                {
                    for (int i = cur; i >= 0; i = rkFrom[i]) if (IsRockTile(i % w, i / w, robot)) { crosses = true; break; }
                    return rkG[cur] * 1.6;                                                   // 10 = one side of a tile (16 px)
                }
                int cx = cur % w, cy = cur / w;
                for (int dy = -1; dy <= 1; dy++)
                    for (int dx = -1; dx <= 1; dx++)
                    {
                        if (dx == 0 && dy == 0) continue;
                        int nx = cx + dx, ny = cy + dy;
                        if (nx < 0 || ny < 0 || nx >= w || ny >= h) continue;
                        int ni = ny * w + nx;
                        if (rkClosed[ni] || !pf.StepOk(cx, cy, nx, ny, robot, rocksOpen)) continue;
                        int g = rkG[cur] + (int)Math.Round((dx != 0 && dy != 0 ? 14 : 10) * StepWeight(nx, ny, robot));
                        if (g >= rkG[ni]) continue;
                        if (rkG[ni] == int.MaxValue) rkTouched.Add(ni);
                        rkG[ni] = g; rkFrom[ni] = cur; HeapPush(g + H(ni), ni);
                    }
            }
            return -1;
        }
    }

    public partial class ZObject
    {
        /// <summary>DV103 hook of the path searches (ZObject.StartPath, ZPathV3's own start): Zod's HasExplosives() unless the way round a
        /// rock is taken (ZPathing.RockRound.cs).</summary>
        bool RockPath() => World.Pathing != null ? World.Pathing.PlansThroughRocks(this, LocX + 8, LocY + 8, cur.x, cur.y) : HasExplosives();
    }

    public sealed partial class ZWorld
    {
        /// <summary>DV103 walking round rocks (off in the rule file; View/ZGame.cs turns it on at every start unless 'Zod exact').</summary>
        public static bool RockRoundDeviation = false;
    }
}
