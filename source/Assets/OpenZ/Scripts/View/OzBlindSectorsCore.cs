using System;
using System.Collections.Generic;
using OpenZ.Sim;

namespace OpenZ
{
    /// <summary>
    /// Plain C#, no engine types: AttackRangeRings.BlindSectors.cs lays the result on the
    /// ground with the gun's range ring; the counted checks run it beside the rules.
    ///
    /// The rules (read here, never changed): a gun fires at a unit when the unit's centre lies within its range of the gun's centre
    /// (Zod points_within_distance), no destroyable barrier (rock, tree, hut, map object, harbour wreck) lies on the tile line
    /// between them (ZWorld.EngageBarrierBetween: every gun, the howitzer too; both directions, the end tiles included) and, for
    /// direct fire (every gun but the howitzer, with the line-of-sight rule on), no sight blocker (ZSight: cliffs, buildings,
    /// rocks, huts, tall props; a tower gun's own fort exempt) lies on that line (both directions, the end tiles excluded).
    ///
    /// Zod's tile line (ZPathFinder.Bresenham) from the gun's centre tile over n columns (x-major: |dx| >= |dy|, slope s =
    /// |dy| / |dx| &lt; 1) puts column k at row floor(s (k + 1)) going out and at row ceil(s (k - 1)) coming back; the exact
    /// diagonal is the diagonal. So a blocker k columns and m rows from the gun's tile stops every line that runs further than k
    /// columns with a slope in [m / (k + 1), (m + 1) / (k + 1)) (out) or in ((m - 1) / (k - 1), m / (k - 1)] (back; k = 1:
    /// m = 0 stops the whole octant), and the diagonal when m = k: a wedge from the gun's tile centre that starts behind the
    /// blocker and runs to the ring. A barrier also closes its own tile (the end tile counts). The union of these wedges, octant
    /// by octant, is exactly the set of refused cell centres, and between the centres it has straight edges: the sectors drawn.
    ///
    /// Compute() reads only public rule queries (Path.HasDestroyableBarrier, Sight.BlockedTile, and Sight.ClearUncached for the
    /// tiles of an exempt fort); it changes nothing. Signature() is the cheap per-frame test that something in the box changed
    /// (a tree felled, a rock / hut / building destroyed or rebuilt). Lists are reused: no allocations once they have grown.
    /// </summary>
    public sealed partial class OzBlindSectorsCore                                                   // K333b: the side lines from the gun and the marks in OzBlindSectorsCore.FromGun.cs
    {
        public const int Tile = 16;

        // ------------------------------------------------------------------ look of the mesh (px; 8 px = 1 m)
        /// <summary>Distance between the rings of vertices along a spoke (px).</summary>
        public double RadialStep = 5.0;
        /// <summary>Largest angle between two spokes inside a sector (rad).</summary>
        public double SpokeStep = 0.026;
        /// <summary>The soft start of a sector behind its blocker: alpha 0 at near - NearSoft, full at near + NearSoft (px).</summary>
        public double NearSoft = 3.0;
        /// <summary>The end at the ring: full alpha to range + OuterIn, 0 at range + OuterOut (the ring line, range + 3 px).</summary>
        public double OuterIn = 0.5, OuterOut = 3.0;
        /// <summary>A sector edge is moved this much (rad) to the side its edge ray belongs to (a cell centre on the ray lands on the right side).</summary>
        public double EdgeEps = 1e-4;
        /// <summary>The edge lines: the first dot this far from the gun's tile centre (px), then one every DotSpacing px.</summary>
        public double DotStart = 22.0, DotSpacing = 7.2;
        /// <summary>An edge gets its line only when the sector and the open gap beside it are both at least this wide at the ring (px).</summary>
        public double LineMinArc = 12.0;
        /// <summary>The line's strength in front of the sector (between the gun and the blocker), as a share of its strength along it.</summary>
        public double LineLead = 0.2;

        // ------------------------------------------------------------------ inputs of the last Compute
        public int GunX, GunY, RangePx, ExemptId;
        public bool Direct;
        /// <summary>The centre of the drawn ring (px): the gun's view. The same as the gun's centre, except for a fort's tower gun,
        /// whose model sits on its tower pad, a few metres from the spot the rules measure from (View/TowerMounts.cs): the sectors
        /// then end at the nearer of the two circles, so they never pass the drawn ring.</summary>
        public double RingX, RingY;
        /// <summary>The gun's centre tile (the apex of every wedge), its centre in px.</summary>
        public int Ax, Ay;
        public double ApexX => Ax * Tile + 8;
        public double ApexY => Ay * Tile + 8;
        /// <summary>The box of tiles read (inclusive).</summary>
        public int X0, Y0, X1, Y1;

        // ------------------------------------------------------------------ results
        public struct Elem
        {
            public int oct;                 // octant 0..7 (in angle order in map space from +x: see OctXMajor / OctSx / OctSy)
            public double sLo, sHi;         // slope range inside the octant (minor / major); sLo == sHi: a single ray
            public bool loIn, hiIn;
            public double thA, thB;         // the same as angles (map space, rad, 0 .. 2 pi), thA <= thB
            public bool aIn, bIn;
            public bool covered;
            public double near;             // covered: blind beyond this many tiles along the octant's major axis
        }

        /// <summary>The circle around the apex as points and open intervals in angle order (covered = blind beyond near).</summary>
        public readonly List<Elem> Elems = new List<Elem>(256);
        /// <summary>The sectors: runs of covered elements (first index into Elems, count). A run may wrap past 2 pi.</summary>
        public readonly List<(int first, int count)> Runs = new List<(int, int)>(32);
        public bool FullCircle;
        public int BarrierTiles, SightTiles, Pieces;

        public struct V { public double x, y; public float a; public double dC; }      // map px, base alpha 0..1, distance to the drawn ring's centre (px)
        public readonly List<V> Verts = new List<V>(2048);
        public readonly List<int> Tris = new List<int>(8192);
        public struct Dot { public double x, y, dx, dy; public float a; public double dC; public int kind, line; }   // map px, unit direction along the edge, strength 0..1; kind 0: a side line's dash, 1: a mark's arm; line: its SightLine (K333b)
        public readonly List<Dot> Dots = new List<Dot>(256);
        public int LineEdges;

        // ------------------------------------------------------------------ octants
        // 0: x-major +x, dy >= 0   theta [0, 45]        1: y-major +y, dx >= 0  (45, 90]      2: y-major +y, dx < 0  (90, 135)
        // 3: x-major -x, dy >= 0   [135, 180]           4: x-major -x, dy < 0   (180, 225]    5: y-major -y, dx < 0  (225, 270)
        // 6: y-major -y, dx >= 0   [270, 315)           7: x-major +x, dy < 0   [315, 360)
        static readonly bool[] OctXMajor = { true, false, false, true, true, false, false, true };
        static readonly int[] OctSx = { 1, 1, -1, -1, -1, -1, 1, 1 };
        static readonly int[] OctSy = { 1, 1, 1, 1, -1, -1, -1, -1 };
        static readonly bool[] OctLoIn = { true, true, false, true, false, false, true, false };   // slope 0 belongs to the octant
        static readonly bool[] OctHiIn = { true, false, false, true, true, false, false, true };   // slope 1 belongs to the octant
        static readonly bool[] OctRising = { true, false, true, false, true, false, true, false }; // the angle grows with the slope

        static double Theta(int oct, double s)
        {
            double t = Math.Atan(s);
            switch (oct)
            {
                case 0: return t;
                case 1: return Math.PI * 0.5 - t;
                case 2: return Math.PI * 0.5 + t;
                case 3: return Math.PI - t;
                case 4: return Math.PI + t;
                case 5: return Math.PI * 1.5 - t;
                case 6: return Math.PI * 1.5 + t;
                default: return Math.PI * 2.0 - t;
            }
        }

        /// <summary>The octant and slope of a direction in whole tiles (not both 0), as the rules' tile line sees it.</summary>
        public static int OctantOf(int dx, int dy, out double s, out int major)
        {
            int ax = Math.Abs(dx), ay = Math.Abs(dy);
            if (ax >= ay)
            {
                s = (double)ay / ax; major = ax;
                if (dx > 0) return dy >= 0 ? 0 : 7;
                return dy >= 0 ? 3 : 4;
            }
            s = (double)ax / ay; major = ay;
            if (dy > 0) return dx >= 0 ? 1 : 2;
            return dx >= 0 ? 6 : 5;
        }

        // ------------------------------------------------------------------ the rules for one cell (checks and the audit)
        /// <summary>The cannon types that fire over obstacles (ZObject.ArcingFire for a cannon: the howitzer); a gun of any other
        /// type fires directly when the line-of-sight rule is on. For a gun that is only being placed (no object yet).</summary>
        public static bool DirectFireType(string cannonType) => ZWorld.LosDeviation && cannonType != "howitzer";

        /// <summary>The object whose sight tiles a gun centred on (cx, cy) px will ignore, before the gun exists: as ZSight.ExemptOf
        /// for a cannon, the fort whose rectangle holds the centre (a tower pad), else 0. No allocation.</summary>
        public static int ExemptAt(ZWorld w, int cx, int cy)
        {
            var list = w.Objects;
            for (int i = 0; i < list.Count; i++)
            {
                var b = list[i];
                if (b.IsFort && cx >= b.LocX && cx < b.LocX + b.WidthPix && cy >= b.LocY && cy < b.LocY + b.HeightPix) return b.Id;
            }
            return 0;
        }

        /// <summary>The rules' verdict for a unit whose centre is (tx, ty) px, fired at by a gun centred on (gx, gy) with range r:
        /// out of range, a destroyable barrier on the line, or (direct fire) a sight blocker on it. Allocates (the rules' own tile
        /// walks): checks and the audit only.</summary>
        public static bool Refused(ZWorld w, int gx, int gy, int range, bool direct, int exemptId, int tx, int ty)
        {
            double dx = gx - tx, dy = gy - ty;
            if (Math.Sqrt(dx * dx + dy * dy) > range) return true;                                        // points_within_distance
            if (w.EngageBarrierBetween(gx, gy, tx, ty)) return true;
            return direct && !w.Sight.ClearUncached(gx, gy, exemptId, tx, ty, 0);
        }

        /// <summary>The drawn verdict for the cell whose centre is (tx, ty) px: in range (and inside the drawn ring), and a covered
        /// element of its direction with the cell beyond its near line (exact: the same slopes as the wedges).</summary>
        public bool BlindCell(int tx, int ty)
        {
            double dx0 = GunX - tx, dy0 = GunY - ty, dx1 = RingX - tx, dy1 = RingY - ty;
            if (Math.Sqrt(dx0 * dx0 + dy0 * dy0) > RangePx || Math.Sqrt(dx1 * dx1 + dy1 * dy1) > RangePx) return false;
            return BlindDirection(((tx >> 4) - Ax), ((ty >> 4) - Ay));
        }

        /// <summary>The drawn verdict for a tile offset (whole tiles) from the gun's tile, range not tested.</summary>
        public bool BlindDirection(int dx, int dy)
        {
            if (dx == 0 && dy == 0) return false;
            int oct = OctantOf(dx, dy, out double s, out int major);
            for (int i = 0; i < Elems.Count; i++)
            {
                var e = Elems[i];
                if (e.oct != oct) continue;
                bool inside = e.sLo == e.sHi ? s == e.sLo : (s > e.sLo && s < e.sHi) || (s == e.sLo && e.loIn) || (s == e.sHi && e.hiIn);
                if (!inside) continue;
                return e.covered && major > e.near;
            }
            return false;
        }

        /// <summary>The drawn verdict at any map point (px), between the cell centres too (a ring shared by two guns of a type keeps
        /// only what is blind for both): its angle's element, beyond that element's near line, inside the ring.</summary>
        public bool BlindPoint(double x, double y)
        {
            double gx = x - GunX, gy = y - GunY, rx = x - RingX, ry = y - RingY;
            if (Math.Sqrt(gx * gx + gy * gy) > RangePx + OuterOut || Math.Sqrt(rx * rx + ry * ry) > RangePx + OuterOut) return false;
            double dx = (x - ApexX) / Tile, dy = (y - ApexY) / Tile;
            if (dx == 0 && dy == 0) return false;
            double th = Math.Atan2(dy, dx); if (th < 0) th += Math.PI * 2;
            for (int i = 0; i < Elems.Count; i++)
            {
                var e = Elems[i];
                if (th < e.thA || th > e.thB) continue;
                double major = OctXMajor[e.oct] ? Math.Abs(dx) : Math.Abs(dy);
                return e.covered && major > e.near;
            }
            return false;
        }

        // ------------------------------------------------------------------ change test
        /// <summary>A hash of every blocker tile in the box (barrier / sight state): changes when a tree is felled, a rock, hut or
        /// building is destroyed or rebuilt. Cheap (no allocation): run it every frame while the ring shows.</summary>
        public long Signature(ZWorld w)
        {
            long h = 1469598103934665603L;
            var sight = Direct ? w.Sight : null;
            for (int y = Y0; y <= Y1; y++)
                for (int x = X0; x <= X1; x++)
                {
                    int b = (w.Path.HasDestroyableBarrier(x, y) ? 1 : 0) | (sight != null && sight.BlockedTile(x, y) ? 2 : 0);
                    if (b != 0) h = unchecked((h ^ ((long)(y * 4096 + x) * 4 + b)) * 1099511628211L);
                }
            return h;
        }

        // ------------------------------------------------------------------ compute
        struct Piece { public double lo, hi; public bool loIn, hiIn; public double near; }
        readonly List<Piece>[] pieces = { new List<Piece>(), new List<Piece>(), new List<Piece>(), new List<Piece>(), new List<Piece>(), new List<Piece>(), new List<Piece>(), new List<Piece>() };
        readonly List<double> bps = new List<double>(512);
        readonly List<Elem> octElems = new List<Elem>(256);

        /// <summary>The sectors of a gun centred on (gx, gy) px with range px; direct = the line-of-sight rule applies to it;
        /// exemptId = the object whose sight tiles it ignores (its fort for a tower gun, else 0). Returns false when nothing is blind.</summary>
        public bool Compute(ZWorld w, int gx, int gy, int range, bool direct, int exemptId) => Compute(w, gx, gy, range, direct, exemptId, gx, gy);

        /// <summary>As above, with the drawn ring centred on (ringX, ringY) px.</summary>
        public bool Compute(ZWorld w, int gx, int gy, int range, bool direct, int exemptId, double ringX, double ringY)
        {
            GunX = gx; GunY = gy; RangePx = range; Direct = direct; ExemptId = exemptId; RingX = ringX; RingY = ringY;
            Ax = gx >> 4; Ay = gy >> 4;
            Elems.Clear(); Runs.Clear(); Verts.Clear(); Tris.Clear(); Dots.Clear();
            FullCircle = false; BarrierTiles = SightTiles = Pieces = 0; LineEdges = 0;
            int mw = w.Map.width, mh = w.Map.height;
            int rt = (range + 3) / Tile + 2;
            X0 = Math.Max(0, Ax - rt); X1 = Math.Min(mw - 1, Ax + rt); Y0 = Math.Max(0, Ay - rt); Y1 = Math.Min(mh - 1, Ay + rt);
            if (range <= 0) return false;
            for (int o = 0; o < 8; o++) pieces[o].Clear();
            var sight = direct ? w.Sight : null;
            ZObject ex = exemptId > 0 ? w.GetAny(exemptId) : null;
            for (int y = Y0; y <= Y1; y++)
                for (int x = X0; x <= X1; x++)
                {
                    if (x == Ax && y == Ay) continue;
                    bool barrier = w.Path.HasDestroyableBarrier(x, y);
                    bool block = sight != null && sight.BlockedTile(x, y) && !ExemptTile(w, ex, exemptId, x, y);
                    if (!barrier && !block) continue;
                    if (barrier) BarrierTiles++; else SightTiles++;
                    AddBlocker(x - Ax, y - Ay, barrier);
                }
            Union();
            Assemble();
            BuildMesh(w);
            return Runs.Count > 0 || FullCircle;
        }

        /// <summary>A blocked tile of the exempt object (a tower gun's own fort) does not block this gun: asked of the rules' own
        /// walk over that one tile (from its west neighbour to its east one, or north to south at a map edge).</summary>
        static bool ExemptTile(ZWorld w, ZObject ex, int exemptId, int x, int y)
        {
            if (ex == null) return false;
            if (x < (ex.LocX >> 4) || y < (ex.LocY >> 4) || x >= ((ex.LocX + ex.WidthPix) >> 4) || y >= ((ex.LocY + ex.HeightPix) >> 4)) return false;
            int mw = w.Map.width, mh = w.Map.height;
            if (x > 0 && x < mw - 1) return w.Sight.ClearUncached((x - 1) * Tile + 8, y * Tile + 8, exemptId, (x + 1) * Tile + 8, y * Tile + 8, 0);
            if (y > 0 && y < mh - 1) return w.Sight.ClearUncached(x * Tile + 8, (y - 1) * Tile + 8, exemptId, x * Tile + 8, (y + 1) * Tile + 8, 0);
            return false;
        }

        void AddBlocker(int dx, int dy, bool barrier)
        {
            for (int o = 0; o < 8; o++)
            {
                int k = OctXMajor[o] ? dx * OctSx[o] : dy * OctSy[o];
                int m = OctXMajor[o] ? dy * OctSy[o] : dx * OctSx[o];
                if (k < 1 || m < 0 || m > k) continue;
                if ((k - 0.5) * Tile > RangePx + 12) continue;                                            // its wedge starts past the ring (the ring's centre is 11.3 px from the apex)
                double nearOut = barrier ? k - 0.5 : k + 0.5, nearBack = k + 0.5;
                // out: floor(s (k + 1)) == m, s < 1
                Add(o, m / (k + 1.0), (m + 1) / (k + 1.0), true, false, nearOut);
                // the diagonal (x-major octants own slope 1): the line is the diagonal both ways
                if (OctXMajor[o] && m == k) Add(o, 1.0, 1.0, true, true, nearOut);
                // back: ceil(s (k - 1)) == m, s < 1
                if (k >= 2) Add(o, (m - 1) / (k - 1.0), m / (k - 1.0), false, true, nearBack);
                else if (m == 0) Add(o, 0.0, 1.0, true, false, nearBack);
            }
        }

        void Add(int o, double lo, double hi, bool loIn, bool hiIn, double near)
        {
            // slopes below 1 only (slope 1 is the diagonal piece); clip to [0, 1)
            if (lo == hi) { if (lo == 1.0 && OctHiIn[o]) { pieces[o].Add(new Piece { lo = 1, hi = 1, loIn = true, hiIn = true, near = near }); Pieces++; } return; }
            if (hi >= 1.0) { hi = 1.0; hiIn = false; }
            if (lo < 0.0) { lo = 0.0; loIn = true; }
            if (lo > hi || (lo == hi && !(loIn && hiIn))) return;
            pieces[o].Add(new Piece { lo = lo, hi = hi, loIn = loIn, hiIn = hiIn, near = near }); Pieces++;
        }

        // the union per octant: points and open intervals between the breakpoints, each with the nearest near of what covers it
        void Union()
        {
            for (int o = 0; o < 8; o++)
            {
                var ps = pieces[o];
                bps.Clear(); bps.Add(0.0); bps.Add(1.0);
                foreach (var p in ps) { bps.Add(p.lo); bps.Add(p.hi); }
                bps.Sort();
                int n = 0;
                for (int i = 0; i < bps.Count; i++) if (n == 0 || bps[i] != bps[n - 1]) bps[n++] = bps[i];
                bps.RemoveRange(n, bps.Count - n);
                octElems.Clear();
                for (int i = 0; i < bps.Count; i++)
                {
                    double b = bps[i];
                    bool allowed = (b > 0.0 && b < 1.0) || (b == 0.0 && OctLoIn[o]) || (b == 1.0 && OctHiIn[o]);
                    if (allowed)
                    {
                        double near = double.MaxValue; bool cov = false;
                        foreach (var p in ps)
                            if ((p.lo < b && b < p.hi) || (b == p.lo && p.loIn) || (b == p.hi && p.hiIn)) { cov = true; if (p.near < near) near = p.near; }
                        octElems.Add(new Elem { oct = o, sLo = b, sHi = b, loIn = true, hiIn = true, covered = cov, near = cov ? near : 0 });
                    }
                    if (i + 1 < bps.Count)
                    {
                        double a = b, c = bps[i + 1], near = double.MaxValue; bool cov = false;
                        foreach (var p in ps)
                            if (p.lo <= a && p.hi >= c) { cov = true; if (p.near < near) near = p.near; }
                        octElems.Add(new Elem { oct = o, sLo = a, sHi = c, loIn = false, hiIn = false, covered = cov, near = cov ? near : 0 });
                    }
                }
                // merge neighbours with the same verdict (and the same near) inside the octant
                int m = 0;
                for (int i = 0; i < octElems.Count; i++)
                {
                    var e = octElems[i];
                    if (m > 0)
                    {
                        var q = octElems[m - 1];
                        if (q.covered == e.covered && (!e.covered || q.near == e.near))
                        {
                            q.sHi = e.sHi; q.hiIn = e.hiIn; octElems[m - 1] = q; continue;
                        }
                    }
                    octElems[m++] = e;
                }
                octElems.RemoveRange(m, octElems.Count - m);
                // to angles, in angle order
                int start = Elems.Count;
                for (int i = 0; i < octElems.Count; i++)
                {
                    var e = octElems[i];
                    double t1 = Theta(o, e.sLo), t2 = Theta(o, e.sHi);
                    if (OctRising[o]) { e.thA = t1; e.thB = t2; e.aIn = e.loIn; e.bIn = e.hiIn; }
                    else { e.thA = t2; e.thB = t1; e.aIn = e.hiIn; e.bIn = e.loIn; }
                    if (e.sLo == e.sHi) e.aIn = e.bIn = true;
                    Elems.Add(e);
                }
                if (!OctRising[o]) Elems.Reverse(start, Elems.Count - start);
            }
        }

        // the sectors: runs of covered elements round the circle
        void Assemble()
        {
            int n = Elems.Count, firstGap = -1;
            for (int i = 0; i < n; i++) if (!Elems[i].covered) { firstGap = i; break; }
            if (firstGap < 0)
            {
                if (n == 0) return;
                FullCircle = true;                                                                 // a gun shut in on every side: blind all round beyond each element's near line
                Runs.Add((0, n));
                return;
            }
            int j = 0;
            while (j < n)
            {
                int i = (firstGap + 1 + j) % n;
                if (!Elems[i].covered) { j++; continue; }
                int c = 0;
                while (j + c < n && Elems[(firstGap + 1 + j + c) % n].covered) c++;
                Runs.Add((i, c));
                j += c;
            }
        }

        // ------------------------------------------------------------------ the 2D mesh (map px)
        struct Spoke { public double th, near; public bool xMajor; public int fv, nv; }
        readonly List<Spoke> spokes = new List<Spoke>(512);
        readonly List<double> radii = new List<double>(64);
        readonly List<float> alphas = new List<float>(64);
        void Push(double r, float a) { radii.Add(r); alphas.Add(a); }

        double NearPx(double th, double near, bool xMajor)
        {
            double c = xMajor ? Math.Abs(Math.Cos(th)) : Math.Abs(Math.Sin(th));
            return near * Tile / Math.Max(1e-6, c);
        }

        /// <summary>From the apex along angle th: the distance to the circle of radius rho round the gun's centre, or round the
        /// drawn ring's centre when that one is nearer.</summary>
        double ToCircle(double th, double rho) => Math.Min(ToCircle(th, rho, GunX, GunY), ToCircle(th, rho, RingX, RingY));

        double ToCircle(double th, double rho, double cx, double cy)
        {
            double ux = Math.Cos(th), uy = Math.Sin(th), ox = ApexX - cx, oy = ApexY - cy;
            double b = ux * ox + uy * oy, c = ox * ox + oy * oy - rho * rho, d = b * b - c;
            return d <= 0 ? 0 : Math.Max(0, -b + Math.Sqrt(d));
        }

        void BuildMesh(ZWorld w)
        {
            if (Runs.Count == 0) { SightLines(w); return; }                                                // K333b: the lines and marks are cleared too
            int n = Elems.Count;
            double mapW = w.Map.width * Tile, mapH = w.Map.height * Tile;
            foreach (var run in Runs)
            {
                spokes.Clear();
                bool full = FullCircle;
                for (int q = 0; q < run.count; q++)
                {
                    int idx = (run.first + q) % n;
                    var e = Elems[idx];
                    double wrap = 0;
                    if (!full && q > 0)
                    {   // keep the angles rising through the run (it may pass 2 pi)
                        double prev = spokes.Count > 0 ? spokes[spokes.Count - 1].th : e.thA;
                        while (e.thA + wrap < prev - 1e-9) wrap += Math.PI * 2;
                    }
                    else if (full) wrap = 0;
                    bool xm = OctXMajor[e.oct];
                    bool first = q == 0 && !full, last = q == run.count - 1 && !full;
                    if (e.sLo == e.sHi)
                    {   // a single ray
                        double th = e.thA + wrap;
                        if (first) AddSpoke(th - EdgeEps, e.near, xm);
                        AddSpoke(th, e.near, xm);
                        if (last) AddSpoke(th + EdgeEps, e.near, xm);
                    }
                    else if (e.thB - e.thA <= 4 * EdgeEps)
                    {   // (never on a map: two slopes of the tile lines differ by far more) one spoke in the middle
                        AddSpoke(0.5 * (e.thA + e.thB) + wrap, e.near, xm);
                    }
                    else
                    {   // an interval: an end that belongs to it gets a spoke on its ray (just outside it too at a sector side), then
                        // spokes just inside both ends and every SpokeStep between
                        double a = e.thA + wrap, b = e.thB + wrap;
                        if (e.aIn) { if (first) AddSpoke(a - EdgeEps, e.near, xm); AddSpoke(a, e.near, xm); }
                        AddSpoke(a + EdgeEps, e.near, xm);
                        int steps = (int)Math.Ceiling((b - a - 2 * EdgeEps) / SpokeStep);
                        for (int s = 1; s < steps; s++) AddSpoke(a + EdgeEps + (b - a - 2 * EdgeEps) * s / steps, e.near, xm);
                        AddSpoke(b - EdgeEps, e.near, xm);
                        if (e.bIn) { AddSpoke(b, e.near, xm); if (last) AddSpoke(b + EdgeEps, e.near, xm); }
                    }
                }
                if (full && spokes.Count > 0) { var s0 = spokes[0]; s0.th += Math.PI * 2; AddSpoke(s0.th, s0.near, s0.xMajor); }
                // vertices along every spoke
                for (int i = 0; i < spokes.Count; i++)
                {
                    var sp = spokes[i];
                    double rN = NearPx(sp.th, sp.near, sp.xMajor);
                    double rO1 = ToCircle(sp.th, RangePx + OuterIn), rO2 = ToCircle(sp.th, RangePx + OuterOut);
                    double r0 = Math.Max(0.5, rN - NearSoft), r1 = rN + NearSoftBehind;                             // K333b: a softer start behind the blocker
                    radii.Clear(); alphas.Clear();
                    if (r0 >= rO1) { Push(rO1 - 0.02, 0f); Push(rO1, 0f); }                                  // the sector starts past the ring here: nothing to draw
                    else if (r1 >= rO1) { Push(r0, 0f); Push(rO1, (float)((rO1 - r0) / (r1 - r0))); Push(Math.Max(rO2, rO1 + 0.01), 0f); }
                    else
                    {
                        Push(r0, 0f); Push(r1, 1f);                                                            // the soft start behind the blocker
                        for (double r = Math.Ceiling(r1 / RadialStep) * RadialStep; r < rO1 - 0.25; r += RadialStep) if (r > r1 + 0.25) Push(r, 1f);
                        Push(rO1, 1f); Push(Math.Max(rO2, rO1 + 0.01), 0f);                                    // the ring line
                    }
                    sp.fv = Verts.Count; sp.nv = radii.Count;
                    double ux = Math.Cos(sp.th), uy = Math.Sin(sp.th);
                    for (int k = 0; k < radii.Count; k++)
                    {
                        double r = radii[k], x = ApexX + ux * r, y = ApexY + uy * r;
                        float a = alphas[k];
                        if (x < 0 || y < 0 || x > mapW || y > mapH) a = 0f;                                    // off the map
                        double gx = x - RingX, gy = y - RingY;
                        Verts.Add(new V { x = x, y = y, a = a, dC = Math.Sqrt(gx * gx + gy * gy) });
                    }
                    spokes[i] = sp;
                }
                for (int i = 0; i + 1 < spokes.Count; i++) Zip(spokes[i], spokes[i + 1]);
            }
            SightLines(w);                                                                                   // K333b: every side of the blind ground its line from the gun, big wedges a mark (OzBlindSectorsCore.FromGun.cs)
        }

        void AddSpoke(double th, double near, bool xm) => spokes.Add(new Spoke { th = th, near = near, xMajor = xm });

        // triangles between two neighbouring spokes (their vertex lists sorted by radius)
        void Zip(Spoke p, Spoke q)
        {
            int i = 0, j = 0;
            while (i < p.nv - 1 || j < q.nv - 1)
            {
                bool advP;
                if (i >= p.nv - 1) advP = false;
                else if (j >= q.nv - 1) advP = true;
                else advP = Rad(p, i + 1) <= Rad(q, j + 1);
                if (advP) { Tris.Add(p.fv + i); Tris.Add(p.fv + i + 1); Tris.Add(q.fv + j); i++; }
                else { Tris.Add(p.fv + i); Tris.Add(q.fv + j + 1); Tris.Add(q.fv + j); j++; }
            }
        }

        double Rad(Spoke s, int k) { var v = Verts[s.fv + k]; double x = v.x - ApexX, y = v.y - ApexY; return Math.Sqrt(x * x + y * y); }

        // the faint dotted line along a sector side: from the gun to the ring, weaker in front of the sector
        void EdgeLine(in (int first, int count) run, Spoke edge, bool start, int n)
        {
            double width = RunWidth(run, n), gap = GapWidth(run, start, n);
            double rRing = RangePx + 3;
            if (width * rRing < LineMinArc || gap * rRing < LineMinArc) return;
            LineEdges++;
            double ux = Math.Cos(edge.th), uy = Math.Sin(edge.th);
            double rN = NearPx(edge.th, edge.near, edge.xMajor), rEnd = ToCircle(edge.th, RangePx - 1.0);
            for (double r = DotStart; r <= rEnd; r += DotSpacing)
            {
                double x = ApexX + ux * r, y = ApexY + uy * r, gx = x - RingX, gy = y - RingY;
                double k = r < rN - 4 ? LineLead : r > rN + 4 ? 1.0 : LineLead + (1.0 - LineLead) * (r - (rN - 4)) / 8.0;
                Dots.Add(new Dot { x = x, y = y, dx = ux, dy = uy, a = (float)k, dC = Math.Sqrt(gx * gx + gy * gy) });
            }
        }

        double RunWidth(in (int first, int count) run, int n)
        {
            var a = Elems[run.first]; var b = Elems[(run.first + run.count - 1) % n];
            double w = b.thB - a.thA; while (w < 0) w += Math.PI * 2;
            return w;
        }

        double GapWidth(in (int first, int count) run, bool before, int n)
        {
            // the open run of uncovered elements next to this side
            int i = before ? (run.first - 1 + n) % n : (run.first + run.count) % n;
            var e0 = Elems[i]; double a = e0.thA, b = e0.thB;
            int step = before ? -1 : 1, guard = 0;
            int j = (i + step + n) % n;
            while (!Elems[j].covered && guard++ < n) { if (before) a = Elems[j].thA; else b = Elems[j].thB; j = (j + step + n) % n; }
            double w = b - a; while (w < 0) w += Math.PI * 2;
            return w;
        }

        // ------------------------------------------------------------------ checks: the drawn mesh at a point
        /// <summary>The mesh's alpha (0..1, before the fade) at map point (x, y) px: barycentric inside the triangle that holds it
        /// (the larger value where triangles touch). For the counted checks; linear in the number of triangles.</summary>
        public double MeshAlphaAt(double x, double y)
        {
            double best = 0;
            for (int t = 0; t + 2 < Tris.Count; t += 3)
            {
                var a = Verts[Tris[t]]; var b = Verts[Tris[t + 1]]; var c = Verts[Tris[t + 2]];
                double minX = Math.Min(a.x, Math.Min(b.x, c.x)), maxX = Math.Max(a.x, Math.Max(b.x, c.x));
                if (x < minX - 1e-9 || x > maxX + 1e-9) continue;
                double minY = Math.Min(a.y, Math.Min(b.y, c.y)), maxY = Math.Max(a.y, Math.Max(b.y, c.y));
                if (y < minY - 1e-9 || y > maxY + 1e-9) continue;
                double d = (b.y - c.y) * (a.x - c.x) + (c.x - b.x) * (a.y - c.y);
                if (Math.Abs(d) < 1e-12) continue;
                double l1 = ((b.y - c.y) * (x - c.x) + (c.x - b.x) * (y - c.y)) / d;
                double l2 = ((c.y - a.y) * (x - c.x) + (a.x - c.x) * (y - c.y)) / d;
                double l3 = 1 - l1 - l2;
                const double eps = -1e-9;
                if (l1 < eps || l2 < eps || l3 < eps) continue;
                double v = l1 * a.a + l2 * b.a + l3 * c.a;
                if (v > best) best = v;
            }
            return best;
        }
    }

    /// <summary>
    /// The ground heights under a gun's blind sectors, on a fixed lattice of the map: a node every Step px (1 m, nodes on whole
    /// metres, node (i, j) at map px (i Step, j Step) = world (i, -j) m), kept while the ring shows. A gun being placed moves in tile
    /// steps and its new sectors find the heights already sampled under nearly all of their area, so the darkening follows each
    /// step at once instead of vanishing and coming back over several frames. The view samples the missing nodes (ZGame.GroundY, within a
    /// budget a frame) and reads each vertex's height bilinear from its 4 nodes. Plain C#; no allocation once it has grown.
    /// </summary>
    public sealed class OzGroundLattice
    {
        public const int Step = 8;                                                                      // px (1 m: the corners of the terrain's 1 m quads)
        /// <summary>World metres of node (i, j): x = i Step / 8, z = -j Step / 8.</summary>
        public static float WorldX(int i) => i * (Step / 8f);
        public static float WorldZ(int j) => -j * (Step / 8f);
        float[] h = new float[0], spare = new float[0];
        int i0, j0, nx, ny;                                                                             // nx x ny nodes from node (i0, j0)

        /// <summary>Nodes in the frame (the slow refresh walks them by index).</summary>
        public int Nodes => nx * ny;

        static int Node(double px) => (int)Math.Floor(px / Step);

        /// <summary>Covers the box [x0, x1] x [y0, y1] map px (and the nodes round it): keeps every node already sampled that is
        /// still inside, forgets the rest.</summary>
        public void Frame(double x0, double y0, double x1, double y1)
        {
            int ni0 = Node(x0), nj0 = Node(y0), nnx = Node(x1) + 2 - ni0, nny = Node(y1) + 2 - nj0;
            if (nnx < 1) nnx = 1; if (nny < 1) nny = 1;
            if (ni0 == i0 && nj0 == j0 && nnx == nx && nny == ny) return;
            int need = nnx * nny;
            if (spare.Length < need) { int cap = 256; while (cap < need) cap *= 2; spare = new float[cap]; }   // grows a few times, then never again
            for (int k = 0; k < need; k++) spare[k] = float.NaN;
            int ia = Math.Max(i0, ni0), ib = Math.Min(i0 + nx, ni0 + nnx), ja = Math.Max(j0, nj0), jb = Math.Min(j0 + ny, nj0 + nny);
            for (int j = ja; j < jb; j++)
                for (int i = ia; i < ib; i++) spare[(j - nj0) * nnx + (i - ni0)] = h[(j - j0) * nx + (i - i0)];
            var t = h; h = spare; spare = t;
            i0 = ni0; j0 = nj0; nx = nnx; ny = nny;
        }

        /// <summary>Forgets every sample (the ring shows again after a pause: craters may have changed the ground meanwhile).</summary>
        public void Clear() { for (int k = 0; k < nx * ny; k++) h[k] = float.NaN; }

        /// <summary>Sets node (i, j) (outside the frame: ignored).</summary>
        public void Set(int i, int j, float y)
        {
            if (i < i0 || j < j0 || i >= i0 + nx || j >= j0 + ny) return;
            h[(j - j0) * nx + (i - i0)] = y;
        }

        /// <summary>Node k of the frame (0 .. Nodes - 1): its lattice index and height; false while it is not sampled.</summary>
        public bool NodeAt(int k, out int i, out int j, out float y)
        {
            i = i0 + k % nx; j = j0 + k / nx; y = h[k];
            return !float.IsNaN(y);
        }

        /// <summary>The height at map point (x, y) px, bilinear over its 4 nodes. False while one of them is not sampled: (mi, mj)
        /// is that node; outside the frame mi = int.MinValue (never the case for a point of the framed box).</summary>
        public bool At(double x, double y, out float hgt, out int mi, out int mj)
        {
            double u = x / Step, v = y / Step;
            int i = (int)Math.Floor(u), j = (int)Math.Floor(v);
            hgt = 0f; mi = mj = int.MinValue;
            if (i < i0 || j < j0 || i + 1 >= i0 + nx || j + 1 >= j0 + ny) return false;
            int k = (j - j0) * nx + (i - i0);
            float a = h[k], b = h[k + 1], c = h[k + nx], d = h[k + nx + 1];
            if (float.IsNaN(a)) { mi = i; mj = j; return false; }
            if (float.IsNaN(b)) { mi = i + 1; mj = j; return false; }
            if (float.IsNaN(c)) { mi = i; mj = j + 1; return false; }
            if (float.IsNaN(d)) { mi = i + 1; mj = j + 1; return false; }
            float fu = (float)(u - i), fv = (float)(v - j);
            hgt = (a + (b - a) * fu) * (1f - fv) + (c + (d - c) * fu) * fv;
            return true;
        }
    }
}
