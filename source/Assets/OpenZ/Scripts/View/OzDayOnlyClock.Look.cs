using System;

namespace OpenZ
{
    /// <summary>
    /// The travel back to day, eased by the look (pure C#). The clock's own keys change the picture only in short stretches (from deep
    /// night nothing changes until the clock passes 0.84, then the night shade, the sun and the sky all change before 1.0), so a travel
    /// eased in clock time brightened the picture in a few tenths of a second near its end and read like a flash. Here each travel
    /// first walks its path once (Steps samples) and adds up how much the picture changes along it (OzDayLook: lightness, plus the night
    /// shade x ShadeWeight, plus the clock itself x ClockWeight so that stretches where nothing changes still take some time and the moon
    /// and its shadows never swing fast); the eased 0..1 of the travel is then a share of that sum, so the picture changes evenly over the
    /// whole EaseSeconds, slowest at the start and at the end. Without a look (Look null) the travel is eased in clock time as before.
    /// </summary>
    public sealed partial class OzDayOnlyClock
    {
        /// <summary>The look of the level's day keys (DayNight.LookModel); null = ease in clock time.</summary>
        public OzDayLook Look;
        /// <summary>How much a change of the night shade (0..1) counts against a change of lightness (0..1).</summary>
        public float ShadeWeight = 1.0f;
        /// <summary>How much the clock's own travel (day fractions) counts: a floor on its speed where the picture does not change.</summary>
        public float ClockWeight = 1.0f;
        /// <summary>Samples along a travel's path.</summary>
        public const int Steps = 160;

        readonly float[] pathM = new float[Steps + 1];
        bool lookPath;

        /// <summary>The travel just started (e0, eDist set): walk its path once and sum the change of the look.</summary>
        void BuildLookPath()
        {
            lookPath = false;
            var look = Look;
            if (look == null || Math.Abs(eDist) < 1e-5f) return;
            float m = 0f, pl = 0f, pn = 0f, dc = ClockWeight * Math.Abs(eDist) / Steps;
            for (int i = 0; i <= Steps; i++)
            {
                float c = Rep(e0 + eDist * i / Steps), l = look.Lightness(c), sh = look.Shade(c);
                if (float.IsNaN(l) || float.IsNaN(sh)) return;
                if (i > 0) m += Math.Abs(l - pl) + ShadeWeight * Math.Abs(sh - pn) + dc;
                pathM[i] = m; pl = l; pn = sh;
            }
            lookPath = m > 1e-6f;
        }

        /// <summary>u = the eased share of the travel (0..1) -> the share of the clock's path (0..1) where the look has changed by u of its sum.</summary>
        float AlongLook(float u)
        {
            if (!lookPath) return u;
            if (u <= 0f) return 0f;
            if (u >= 1f) return 1f;
            float want = u * pathM[Steps];
            int lo = 0, hi = Steps;
            while (hi - lo > 1) { int mid = (lo + hi) >> 1; if (pathM[mid] < want) lo = mid; else hi = mid; }
            float span = pathM[hi] - pathM[lo];
            return (lo + (span > 1e-9f ? (want - pathM[lo]) / span : 0f)) / Steps;
        }
    }
}
