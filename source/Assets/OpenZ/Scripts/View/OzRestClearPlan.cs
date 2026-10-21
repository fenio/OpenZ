using System.Collections.Generic;

namespace OpenZ
{
    /// <summary>
    /// The shot-down shuttle's pieces never stay inside each other at rest (rule DV118, view only): the plain part, shared by the game
    /// (View/OzShuttleBreakup.RestClear.cs) and the kit's twin. No engine type, no clock and no random draw of its own (the heap hands in
    /// every number).
    /// <list type="bullet">
    /// <item>WHEN: once every piece of a wreck lies still (frozen, its own settle glide done), and again after any piece settles anew, at
    /// most <see cref="MaxPasses"/> times per wreck.</item>
    /// <item>WHAT: the pair of pieces deepest inside each other (more than <see cref="ClearDepth"/> m) first. The piece ON TOP (the higher
    /// bottom) is moved out of the one under it together with whatever lies on it: along the way out when that way does not point into the
    /// ground, else straight up off it, else sideways; the shortest of those moves that clears the pair (to <see cref="TargetDepth"/> m or
    /// less), pushes none of the moved pieces deeper into another piece or into still scenery (a rock, a trunk) than before, takes none
    /// deeper into the ground than <see cref="IntoGroundM"/> m, and leaves every piece near them that lay on something still lying on something (the ground, a piece,
    /// a rock, a trunk). When the top piece cannot be moved, the one under it is tried the same way (without the top piece). A piece moves
    /// at most <see cref="MoveCapM"/> m in all. The game glides every moved piece to its new place in a short settle.</item>
    /// </list>
    /// </summary>
    public static class OzRestClearPlan
    {
        /// <summary>Two resting pieces more than this inside each other (m) are cleared; a cleared pair ends at most TargetDepth (m) in.</summary>
        public const double ClearDepth = 0.02, TargetDepth = 0.012;
        /// <summary>The longest single move straight up (m), sideways (m), a piece's whole move (m); how much deeper into the ground a move
        /// may take a piece (m: its rest is 3 cm in by design).</summary>
        public const double LiftM = 0.6, SideM = 0.8, MoveCapM = 0.8, IntoGroundM = 0.06;
        /// <summary>The way out may point down this much at most (its up part) before the move goes straight up or sideways instead.</summary>
        public const double DownWayMax = -0.05;
        /// <summary>A piece lies on another when it is inside it or this close over it (m).</summary>
        public const double RideGapM = 0.03;
        /// <summary>A move may take a piece at most this much deeper into still scenery (a rock, a trunk) than it lay (m).</summary>
        public const double SolidSlackM = 0.003;
        /// <summary>Pairs cleared per pass at most; passes per wreck at most; pieces carried along with a moved one at most.</summary>
        public const int MaxSteps = 12, MaxPasses = 4, MaxRiders = 6;

        /// <summary>A wreck's resting pieces as the plan sees them, where they are now.</summary>
        public interface IHeap
        {
            int Count { get; }
            /// <summary>Lies still and takes part (frozen, settled, its collider on, not in the water, not sinking away).</summary>
            bool Eligible(int i);
            /// <summary>Its bottom's height (m): the piece with the higher bottom is the one on top.</summary>
            double Bottom(int i);
            /// <summary>Piece i is inside piece j by d (m); (nx, ny, nz) = the way out of it for i (unit).</summary>
            bool Pen(int i, int j, out double nx, out double ny, out double nz, out double d);
            /// <summary>Its lowest point over the ground (m, negative = in the ground).</summary>
            double Gap(int i);
            /// <summary>How deep it is inside still scenery (a rock, a trunk: never a piece of the wreck, never the ground), m.</summary>
            double Solid(int i);
            /// <summary>It lies on something: its lower half touches the ground or another body (a piece, a rock, a trunk, a lying body).</summary>
            bool Supported(int i);
            /// <summary>The two pieces are near enough to hold each other up (their boxes within a short distance).</summary>
            bool Near(int i, int j);
            /// <summary>Moves it by the world offset (the plan undoes a trial by the opposite offset).</summary>
            void Shift(int i, double dx, double dy, double dz);
            /// <summary>How far it has been moved in all so far (m).</summary>
            double Moved(int i);
            /// <summary>The plan kept a move of piece i out of piece `from` (for the log).</summary>
            void Kept(int i, int from, double dx, double dy, double dz);
        }

        public static double Depth(IHeap h, int i, int j) => h.Pen(i, j, out _, out _, out _, out double d) ? d : 0;

        /// <summary>The deepest pair of eligible pieces (m) and how many pairs are more than ClearDepth inside each other.</summary>
        public static double Worst(IHeap h, out int over)
        {
            double worst = 0; over = 0;
            for (int i = 0; i < h.Count; i++)
            {
                if (!h.Eligible(i)) continue;
                for (int j = i + 1; j < h.Count; j++)
                {
                    if (!h.Eligible(j)) continue;
                    double d = Depth(h, i, j);
                    if (d > ClearDepth) over++;
                    if (d > worst) worst = d;
                }
            }
            return worst;
        }

        /// <summary>One pass over a resting wreck in one go (the twin; the game spreads it over frames with <see cref="Step"/>). Returns the
        /// moves kept; before / after = the deepest pair (m); left = pairs still more than ClearDepth inside each other.</summary>
        public static int Pass(IHeap h, out double before, out double after, out int left)
        {
            before = Worst(h, out _);
            int moves = 0; var skip = new HashSet<long>();
            for (int step = 0; step < MaxSteps; step++) { if (!Step(h, skip, out bool moved)) break; if (moved) moves++; }
            after = Worst(h, out left);
            return moves;
        }

        /// <summary>One step of a pass: the deepest pair not given up on (skip) is cleared, or given up on. false = no pair left to try.</summary>
        public static bool Step(IHeap h, HashSet<long> skip, out bool moved)
        {
            moved = false;
            int a = -1, b = -1; double worst = ClearDepth;
            for (int i = 0; i < h.Count; i++)
            {
                if (!h.Eligible(i)) continue;
                for (int j = i + 1; j < h.Count; j++)
                {
                    if (!h.Eligible(j) || skip.Contains((long)i * 4096 + j)) continue;
                    double d = Depth(h, i, j);
                    if (d > worst) { worst = d; a = i; b = j; }
                }
            }
            if (a < 0) return false;
            int top = h.Bottom(a) >= h.Bottom(b) ? a : b, under = top == a ? b : a;
            if (Clear(h, top, under) || Clear(h, under, top)) moved = true;
            else skip.Add((long)a * 4096 + b);
            return true;
        }

        /// <summary>The pieces lying on piece m (inside it or just over it, their bottom higher), and on those, never one of `not`.</summary>
        public static List<int> Riders(IHeap h, int m, HashSet<int> not)
        {
            var got = new List<int>(); var open = new Queue<int>(); open.Enqueue(m);
            while (open.Count > 0 && got.Count < MaxRiders)
            {
                int c = open.Dequeue();
                for (int r = 0; r < h.Count && got.Count < MaxRiders; r++)
                {
                    if (r == m || r == c || !h.Eligible(r) || not.Contains(r) || got.Contains(r) || h.Bottom(r) <= h.Bottom(c)) continue;
                    bool on = Depth(h, r, c) > 0;
                    if (!on) { h.Shift(r, 0, -RideGapM, 0); on = Depth(h, r, c) > 0; h.Shift(r, 0, RideGapM, 0); }
                    if (on) { got.Add(r); open.Enqueue(r); }
                }
            }
            return got;
        }

        static void ShiftAll(IHeap h, List<int> g, double x, double y, double z) { foreach (int i in g) h.Shift(i, x, y, z); }

        /// <summary>Moves piece m (with what lies on it) out of piece o (true = cleared, or not inside it any more).</summary>
        public static bool Clear(IHeap h, int m, int o)
        {
            if (!h.Pen(m, o, out double nx, out double ny, out double nz, out double d) || d <= ClearDepth) return true;
            var not = new HashSet<int> { o };
            foreach (int r in Riders(h, o, new HashSet<int> { m })) not.Add(r);          // what lies on o stays (unless it lies on m too: then it rides with m)
            not.Remove(m);
            var group = new List<int> { m };
            foreach (int r in Riders(h, m, not)) group.Add(r);
            var inGroup = new HashSet<int>(group);
            // before: each moved piece's deepest overlap with a piece that stays (o not counted for m), its ground gap; who lies on something
            var deep0 = new Dictionary<int, double>(); var gap0 = new Dictionary<int, double>(); var solid0 = new Dictionary<int, double>();
            foreach (int g in group)
            {
                double dd = 0;
                for (int j = 0; j < h.Count; j++) if (!inGroup.Contains(j) && h.Eligible(j) && !(g == m && j == o)) dd = System.Math.Max(dd, Depth(h, g, j));
                deep0[g] = dd; gap0[g] = h.Gap(g); solid0[g] = h.Solid(g);
            }
            var held = new List<int>();
            for (int k = 0; k < h.Count; k++)
            {
                if (!h.Eligible(k)) continue;
                bool near = inGroup.Contains(k); foreach (int g in group) if (!near && h.Near(k, g)) near = true;
                if (near && h.Supported(k)) held.Add(k);
            }
            double bx = 0, by = 0, bz = 0, best = double.MaxValue; bool found = false;
            bool Valid()
            {
                if (Depth(h, m, o) > ClearDepth) return false;
                foreach (int g in group)
                {
                    for (int j = 0; j < h.Count; j++)
                        if (!inGroup.Contains(j) && h.Eligible(j) && Depth(h, g, j) > System.Math.Max(ClearDepth, deep0[g]) + 0.002) return false;
                    if (h.Gap(g) < System.Math.Min(gap0[g], -IntoGroundM)) return false;
                    if (h.Solid(g) > solid0[g] + SolidSlackM) return false;                                // never deeper into a rock or a trunk
                }
                foreach (int k in held) if (!h.Supported(k)) return false;
                return true;
            }
            void Try(double x, double y, double z)
            {
                double len = System.Math.Sqrt(x * x + y * y + z * z);
                if (len >= best) return;
                foreach (int g in group) if (h.Moved(g) + len > MoveCapM) return;
                ShiftAll(h, group, x, y, z); bool ok = Valid(); ShiftAll(h, group, -x, -y, -z);
                if (ok) { best = len; bx = x; by = y; bz = z; found = true; }
            }
            if (ny >= DownWayMax) Try(nx * (d + 0.01), ny * (d + 0.01), nz * (d + 0.01));           // the way out (not into the ground)
            double up = Along(h, group, m, o, 0, 1, 0, LiftM);                                        // straight up off it
            if (up > 0) Try(0, up, 0);
            double fl = System.Math.Sqrt(nx * nx + nz * nz);                                          // sideways, along the way out's flat part
            if (fl > 0.2)
            {
                double sx = nx / fl, sz = nz / fl, s = Along(h, group, m, o, sx, 0, sz, SideM);
                if (s > 0) Try(sx * s, 0, sz * s);
            }
            if (!found) return false;
            ShiftAll(h, group, bx, by, bz);
            foreach (int g in group) h.Kept(g, g == m ? o : m, bx, by, bz);
            return true;
        }

        /// <summary>The shortest move of the group along the unit way (ux, uy, uz), at most `most` m, that leaves piece m at most TargetDepth
        /// inside piece o (0 = none within `most`).</summary>
        public static double Along(IHeap h, List<int> group, int m, int o, double ux, double uy, double uz, double most)
        {
            bool In(double s) { ShiftAll(h, group, ux * s, uy * s, uz * s); bool r = Depth(h, m, o) > TargetDepth; ShiftAll(h, group, -ux * s, -uy * s, -uz * s); return r; }
            if (In(most)) return 0;
            double lo = 0, hi = most;
            for (int k = 0; k < 10; k++) { double mid = (lo + hi) * 0.5; if (In(mid)) lo = mid; else hi = mid; }
            return hi + 0.003;
        }
    }
}
