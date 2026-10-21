using System;
using System.Collections.Generic;
using OpenZ.Sim;

namespace OpenZ
{
    /// <summary>
    /// The sectors stay exactly the
    /// rules' (OzBlindSectorsCore: every cell centre the rules refuse is drawn, nothing else); what changes is how they read:
    /// <list type="bullet">
    /// <item>every side of the blind ground gets a thin dashed line that starts AT the gun (it fades in from the gun's base) and runs
    /// straight past the blocker's edge: to the ring when the ground beside it is open, to the end of the step when the blind ground
    /// only steps further out there (a line of sight that passes one blocker and meets the next one);</item>
    /// <item>a small mark (an x) in every big blind wedge;</item>
    /// <item>the fill fades in a little after the blocker (no crisp shadow edge), and the view lays a pale veil on it instead of black
    /// (AttackRangeRings.BlindFromGun.cs), and dims the ring's dots over each blind arc.</item>
    /// </list>
    /// A side gets its line only when it is long enough, both the blind ground and the open ground beside it are wide enough there and
    /// the blind ground it bounds is big enough (LineMinArea: about three cells), so a crowd of thin slivers or crumbs at the ring never
    /// turns into a fan of lines; two lines that nearly meet at the ring are one. Plain C#, no engine types, no allocation once the
    /// lists have grown. Called at the end of the mesh (BuildMesh).
    /// </summary>
    public sealed partial class OzBlindSectorsCore
    {
        // ------------------------------------------------------------------ look (px; 8 px = 1 m)
        /// <summary>The fill fades in behind the blocker: alpha 0 at near - NearSoft, full at near + NearSoftBehind (px). Every refused
        /// cell centre lies at least 8 px behind the near line and every open one 8 px in front of it, so the cells stay exact.</summary>
        public double NearSoftBehind = 9.0;
        /// <summary>A side line fades in from the gun's base: nothing before LineFrom, full at LineFull (px from the gun's tile centre;
        /// the gun's model hides the very start).</summary>
        public double LineFrom = 10.0, LineFull = 26.0;
        /// <summary>A line's strength in front of the blind ground (the gun's own line of sight up to the blocker's edge), as a share of
        /// its strength along the blind ground (1: one even line from the gun to the ring).</summary>
        public double LineFront = 1.0;
        /// <summary>A side gets its line when its blind edge is at least this long (px, along the line) ...</summary>
        public double LineMinEdge = 12.0;
        /// <summary>... and the blind ground and the open ground beside it are both at least this wide there (px, across, at the edge's
        /// middle).</summary>
        public double LineMinSide = 10.0;
        /// <summary>A step of the blind ground (it only starts further out beside it, both sides blind further on) gets its line only when
        /// it is at least this long (px) and both sides at least this wide there (px): only the clear wedges, never a fan of lines.</summary>
        public double StepMinEdge = 32.0, StepMinSide = 24.0;
        /// <summary>... and the blind ground the side bounds is at least this big (px squared; 64 = 1 square metre, a cell is 256): a
        /// line from the gun for every wedge that matters, none for a crumb of blind ground (at the ring or at a step).</summary>
        public double LineMinArea = 768.0, StepMinArea = 1024.0;
        /// <summary>Two lines closer than this at the ring (px) are one (the one with the longer blind edge is kept).</summary>
        public double LineMerge = 20.0;
        /// <summary>A line whose blind edge ends inside the ring (a step of the blind ground) fades out over this after the edge (px).</summary>
        public double LineTail = 10.0;
        /// <summary>The dashes along a line: one every DashStep px.</summary>
        public double DashStep = 6.0;
        /// <summary>A blind wedge gets its mark when it is at least this wide across its middle and this deep (px).</summary>
        public double MarkMinWide = 48.0, MarkMinDeep = 48.0;
        /// <summary>The mark's reach from its centre (px) that must lie inside the blind ground.</summary>
        public double MarkReach = 5.0;

        /// <summary>The marks (centres, map px) of the last Compute; each is also two dashes of kind 1 in Dots (its arms).</summary>
        public struct Mark { public double x, y; public double dC; }
        public readonly List<Mark> Marks = new List<Mark>(8);
        /// <summary>Lines of the last Compute that end at a step inside the ring (the others reach the ring).</summary>
        public int StepLines;

        /// <summary>A side line of the last Compute: its angle round the apex (map space, rad), its blind edge from lo to hi (px from the
        /// apex: the blind ground beside it starts at lo), end = the open ground beside it reaches the ring (else a step of the blind
        /// ground), its dashes Dots[firstDot .. firstDot + dots - 1] (Dot.line = its index).</summary>
        public struct SightLine { public double th, lo, hi, area; public bool end; public int firstDot, dots; }
        public readonly List<SightLine> Lines = new List<SightLine>(32);

        struct Side { public double th, lo, hi, area; public bool end; }
        readonly List<Side> sides = new List<Side>(64), kept = new List<Side>(64);
        readonly List<int> ivs = new List<int>(256);

        double DepthAt(in Elem e, double th) => e.covered ? NearPx(th, e.near, OctXMajor[e.oct]) : double.MaxValue;

        static double Mid(in Elem e) => 0.5 * (e.thA + e.thB);

        /// <summary>The side lines from the gun and the marks (after the fill; Dots and LineEdges are this method's).</summary>
        void SightLines(ZWorld w)
        {
            Marks.Clear(); Lines.Clear(); StepLines = 0; LineEdges = 0; sides.Clear(); kept.Clear(); ivs.Clear();
            RealClear();                                                                                  // K427: the sectors of the last Compute (OzBlindSectorsCore.RealSectors.cs)
            int n = Elems.Count;
            if (Runs.Count == 0 || n == 0) return;
            for (int i = 0; i < n; i++) if (Elems[i].sHi != Elems[i].sLo) ivs.Add(i);                    // the intervals (a single ray has no width)
            int m = ivs.Count;
            RealFind(m);                                                                                  // K427: the blind ground cut into its sectors, real or not (OzBlindSectorsCore.RealSectors.cs)
            if (m < 2) return;
            // ---- every side of the blind ground: where two neighbouring intervals are blind from different depths
            for (int q = 0; q < m; q++)
            {
                var A = Elems[ivs[q]]; var B = Elems[ivs[(q + 1) % m]];
                double th = A.thB;                                                                        // their shared side (B starts there, mod 2 pi)
                double dA = DepthAt(A, th), dB = DepthAt(B, th);
                double ring = ToCircle(th, RangePx + OuterIn);
                double lo = Math.Min(dA, dB), hi = Math.Min(Math.Max(dA, dB), ring);
                bool end = hi >= ring - 0.5;                                                              // open ground beside it up to the ring
                double minEdge = end ? LineMinEdge : StepMinEdge, minSide = end ? LineMinSide : StepMinSide;
                if (lo >= ring - 1.0 || hi - lo < minEdge) continue;
                double mid = 0.5 * (lo + hi);
                bool blindA = dA < dB;
                double wBlind = SideWidth(q, blindA ? -1 : 1, mid, true, m), wOpen = SideWidth(q, blindA ? 1 : -1, mid, false, m);
                if (wBlind * mid < minSide || wOpen * mid < minSide) continue;
                double area = SideArea(q, blindA ? -1 : 1, end ? double.MaxValue : hi, m);
                if (area < (end ? LineMinArea : StepMinArea)) continue;
                if (!RealSide(blindA ? q : (q + 1) % m)) { LinesDropped++; continue; }                     // K427: lines only on a real blind sector (never for slivers)
                sides.Add(new Side { th = th, lo = lo, hi = hi, end = end, area = area });
            }
            // ---- two lines that nearly meet at the ring are one (the longer blind edge stays)
            double rRing = RangePx + 3;
            foreach (var s in sides)
            {
                if (kept.Count > 0 && AngleGap(kept[kept.Count - 1].th, s.th) * rRing < LineMerge)
                {
                    if (s.hi - s.lo > kept[kept.Count - 1].hi - kept[kept.Count - 1].lo) kept[kept.Count - 1] = s;
                    continue;
                }
                kept.Add(s);
            }
            if (kept.Count > 1 && AngleGap(kept[kept.Count - 1].th, kept[0].th) * rRing < LineMerge)
            {
                if (kept[kept.Count - 1].hi - kept[kept.Count - 1].lo > kept[0].hi - kept[0].lo) kept[0] = kept[kept.Count - 1];
                kept.RemoveAt(kept.Count - 1);
            }
            double mapW = w.Map.width * Tile, mapH = w.Map.height * Tile;
            // ---- the dashes: from the gun's base along the gun's line of sight, past the blocker's edge
            foreach (var s in kept)
            {
                LineEdges++; if (!s.end) StepLines++;
                int li = Lines.Count, first = Dots.Count;
                double ux = Math.Cos(s.th), uy = Math.Sin(s.th);
                double rMax = ToCircle(s.th, RangePx - 1.0), rEnd = s.end ? rMax : Math.Min(rMax, s.hi + LineTail);
                for (double r = LineFrom + DashStep * 0.5; r <= rEnd; r += DashStep)
                {
                    double k = Smooth((r - LineFrom) / Math.Max(1.0, LineFull - LineFrom));
                    k *= r < s.lo - 4 ? LineFront : r > s.lo + 4 ? 1.0 : LineFront + (1.0 - LineFront) * (r - (s.lo - 4)) / 8.0;
                    if (!s.end && r > s.hi) k *= Math.Max(0.0, 1.0 - (r - s.hi) / Math.Max(1.0, LineTail));
                    if (k < 0.02) continue;
                    double x = ApexX + ux * r, y = ApexY + uy * r;
                    if (x < 0 || y < 0 || x > mapW || y > mapH) continue;
                    double gx = x - RingX, gy = y - RingY;
                    Dots.Add(new Dot { x = x, y = y, dx = ux, dy = uy, a = (float)k, dC = Math.Sqrt(gx * gx + gy * gy), kind = 0, line = li });
                }
                Lines.Add(new SightLine { th = s.th, lo = s.lo, hi = s.hi, area = s.area, end = s.end, firstDot = first, dots = Dots.Count - first });
            }
            PlaceMarks(m);
        }

        static double Smooth(double t) { t = t < 0 ? 0 : t > 1 ? 1 : t; return t * t * (3 - 2 * t); }

        /// <summary>The angle from a to b going round the circle the short way (rad, 0 .. pi).</summary>
        static double AngleGap(double a, double b)
        {
            double d = Math.Abs(b - a) % (Math.PI * 2);
            return d > Math.PI ? Math.PI * 2 - d : d;
        }

        /// <summary>The width (rad) of the ground beside the side between interval q and q + 1, walking away from it (dir -1: from q
        /// backwards, +1: from q + 1 on) while it stays blind (blind = true) or open (false) at this depth (px from the apex).</summary>
        double SideWidth(int q, int dir, double depth, bool blind, int m)
        {
            double wsum = 0;
            int j = dir < 0 ? q : (q + 1) % m;
            for (int guard = 0; guard < m && wsum < Math.PI * 2; guard++)
            {
                var e = Elems[ivs[j]];
                double d = DepthAt(e, Mid(e));
                bool isBlind = d < depth;
                if (isBlind != blind) break;
                wsum += e.thB - e.thA;
                j = (j + dir + m) % m;
            }
            return wsum;
        }

        /// <summary>The blind ground a side bounds (px squared): walking from the side into its blind side (dir as SideWidth), every
        /// interval blind before 'upTo' (px from the apex, at most the ring) adds the ring piece between its near line and upTo.</summary>
        double SideArea(int q, int dir, double upTo, int m)
        {
            double area = 0, wsum = 0;
            int j = dir < 0 ? q : (q + 1) % m;
            for (int guard = 0; guard < m && wsum < Math.PI * 2; guard++)
            {
                var e = Elems[ivs[j]];
                double th = Mid(e), d = DepthAt(e, th), far = Math.Min(upTo, ToCircle(th, RangePx + OuterIn));
                if (d >= far) break;
                double wd = e.thB - e.thA;
                area += 0.5 * wd * (far * far - d * d);
                wsum += wd;
                j = (j + dir + m) % m;
            }
            return area;
        }

        // ------------------------------------------------------------------ the marks: a small x in every big blind wedge
        void PlaceMarks(int m)
        {
            // the stretches of intervals that are blind deep enough (their near line far enough in front of the ring), round the circle
            int start = -1;
            for (int q = 0; q < m; q++) if (!Deep(Elems[ivs[q]])) { start = q; break; }
            if (start < 0) return;                                                                        // blind deep all round: no mark (shut in)
            int qi = 0;
            while (qi < m)
            {
                int q = (start + 1 + qi) % m;
                if (!Deep(Elems[ivs[q]])) { qi++; continue; }
                int c = 0; double wid = 0, nearMax = 0;
                while (qi + c < m && Deep(Elems[ivs[(start + 1 + qi + c) % m]]))
                {
                    var e = Elems[ivs[(start + 1 + qi + c) % m]];
                    wid += e.thB - e.thA; nearMax = Math.Max(nearMax, DepthAt(e, Mid(e)));
                    c++;
                }
                var e0 = Elems[ivs[q]];
                double thm = e0.thA + 0.5 * wid;                                                          // the stretch's middle angle (rising through it)
                double ring = ToCircle(thm, RangePx + OuterIn);
                double r = 0.5 * (nearMax + NearSoftBehind + ring);
                if (ring - nearMax >= MarkMinDeep && wid * r >= MarkMinWide)
                {
                    double x = ApexX + Math.Cos(thm) * r, y = ApexY + Math.Sin(thm) * r, k = MarkReach * 0.7071;
                    if (BlindPoint(x, y) && BlindPoint(x + k, y + k) && BlindPoint(x - k, y - k) && BlindPoint(x + k, y - k) && BlindPoint(x - k, y + k))
                    {
                        double gx = x - RingX, gy = y - RingY, dC = Math.Sqrt(gx * gx + gy * gy);
                        Marks.Add(new Mark { x = x, y = y, dC = dC });
                        Dots.Add(new Dot { x = x, y = y, dx = 0.7071, dy = 0.7071, a = 1f, dC = dC, kind = 1, line = -1 });     // the x's two arms (map px: screen diagonals)
                        Dots.Add(new Dot { x = x, y = y, dx = 0.7071, dy = -0.7071, a = 1f, dC = dC, kind = 1, line = -1 });
                    }
                }
                qi += c;
            }
        }

        bool Deep(in Elem e)
        {
            if (!e.covered) return false;
            double th = Mid(e);
            return ToCircle(th, RangePx + OuterIn) - DepthAt(e, th) >= MarkMinDeep * 0.5;
        }
    }
}
