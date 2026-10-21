using System;
using System.Collections.Generic;

namespace OpenZ
{
    /// <summary>
    /// A gun that fires over obstacles (the howitzer) is stopped
    /// only by rocks, trees and huts (Zod's barrier rule), and it reaches far: behind each lone tree near its ring the rules leave a thin
    /// sliver of blind ground, often under that tree's own crown. Each sliver used to get its two long lines from the gun, so a howitzer in
    /// the jungle showed a fan of lines with hardly any grey between them. Now the blind ground is first cut into its sectors (each stretch
    /// round the gun where the blind ground starts in front of the ring, the grey that is drawn) and a sector gets its lines only when it is
    /// a real one: wide enough round the gun (RealMinDeg), wide enough across (RealMinWide: its area over its depth), deep enough
    /// (RealMinDeep) and big enough (RealMinArea). A gun with no real sector draws its ring and nothing else; the grey and the grey ring
    /// dots still show every blind cell (the rules' own, unchanged), only the lines go. The lines of a real sector are drawn exactly as
    /// before (the same sides, steps and marks). Plain C#, no engine types, no allocation once the lists have grown.
    /// </summary>
    public sealed partial class OzBlindSectorsCore
    {
        /// <summary>Lines only on real sectors (false: every side that passes the line rules gets its line, as before).</summary>
        public bool LinesOnlyReal = true;
        /// <summary>A real sector is at least this wide round the gun (degrees) ...</summary>
        public double RealMinDeg = 6.0;
        /// <summary>... at least this wide across on average (px: its area over its depth; 16 px = one tile, so 32 = two tiles) ...</summary>
        public double RealMinWide = 32.0;
        /// <summary>... at least this deep (px, from where its blind ground starts to the ring) ...</summary>
        public double RealMinDeep = 24.0;
        /// <summary>... and at least this big (px squared; a cell is 256).</summary>
        public double RealMinArea = 1536.0;

        /// <summary>A sector of the last Compute: the stretch of intervals Elems[ivs[first .. first + count - 1]] (round the circle) where the
        /// blind ground starts in front of the ring; its width round the gun (rad), nearest start, depth, area, mean width across (px).</summary>
        public struct BlindSector { public double thA, wid, near, deep, area, wide; public bool real; public int first, count, lines; }
        public readonly List<BlindSector> Sectors = new List<BlindSector>(32);
        /// <summary>Real sectors of the last Compute, and the sides that would have had a line but lie on a sector that is not real.</summary>
        public int RealSectorCount, LinesDropped;
        int[] secOf = new int[256];

        void RealClear() { Sectors.Clear(); RealSectorCount = 0; LinesDropped = 0; }

        /// <summary>The blind ground of interval q (of ivs) starts in front of the ring (its grey is drawn).</summary>
        bool RealVisible(int q)
        {
            var e = Elems[ivs[q]];
            double th = Mid(e);
            return DepthAt(e, th) < ToCircle(th, RangePx + OuterIn) - 1.0;
        }

        /// <summary>The sectors (after the intervals are listed, before the sides).</summary>
        void RealFind(int m)
        {
            RealClear();
            if (m <= 0) return;
            if (secOf.Length < m) secOf = new int[Math.Max(m, secOf.Length * 2)];
            for (int q = 0; q < m; q++) secOf[q] = -1;
            int start = -1;
            for (int q = 0; q < m; q++) if (!RealVisible(q)) { start = q; break; }
            if (start < 0) { RealAdd(0, m, m); return; }                                                      // blind all round (shut in): one sector
            int qi = 0;
            while (qi < m)
            {
                int q = (start + 1 + qi) % m;
                if (!RealVisible(q)) { qi++; continue; }
                int c = 0;
                while (qi + c < m && RealVisible((start + 1 + qi + c) % m)) c++;
                RealAdd(q, c, m);
                qi += c;
            }
        }

        void RealAdd(int first, int count, int m)
        {
            double wid = 0, area = 0, near = double.MaxValue, deep = 0;
            for (int k = 0; k < count; k++)
            {
                int q = (first + k) % m;
                var e = Elems[ivs[q]];
                double th = Mid(e), d = DepthAt(e, th), far = ToCircle(th, RangePx + OuterIn), wd = e.thB - e.thA;
                wid += wd; area += 0.5 * wd * (far * far - d * d);
                if (d < near) near = d;
                if (far - d > deep) deep = far - d;
                secOf[q] = Sectors.Count;
            }
            double wide = deep > 0 ? area / deep : 0;
            bool real = wid >= RealMinDeg * Math.PI / 180.0 && wide >= RealMinWide && deep >= RealMinDeep && area >= RealMinArea;
            if (real) RealSectorCount++;
            Sectors.Add(new BlindSector { thA = Elems[ivs[first]].thA, wid = wid, near = near, deep = deep, area = area, wide = wide, real = real, first = first, count = count });
        }

        /// <summary>The side whose blind ground is interval q (of ivs) may have its line: its sector is a real one (or the filter is off).
        /// Counts the line on its sector.</summary>
        bool RealSide(int q)
        {
            int s = q >= 0 && q < secOf.Length ? secOf[q] : -1;
            bool ok = !LinesOnlyReal || (s >= 0 && Sectors[s].real);
            if (ok && s >= 0) { var b = Sectors[s]; b.lines++; Sectors[s] = b; }
            return ok;
        }
    }
}
