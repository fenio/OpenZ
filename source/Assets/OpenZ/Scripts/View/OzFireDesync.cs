using UnityEngine;

namespace OpenZ
{
    /// <summary>
    /// A burning wreck draws its fire in bursts: every beat a few flame tongues and a short fire light (0.3 s, a steep
    /// fall). Every wreck fire used the same fixed beat (0.25 s; the shuttle's three fires started within 0.15 s of each other on the
    /// same frame-rounded beat, the fort cannon's pieces on the same frame), so their lights rose and fell together: the wreck
    /// field blinked like one lamp. Each fire now keeps its own beat, drawn once per death from a seed (the same death draws the
    /// same numbers again):
    /// <list type="bullet">
    /// <item>its own beat rate (x 0.75-1.25) and every beat jittered (x 0.8-1.2), so no two fires keep a common rhythm;</item>
    /// <item>its own size (flame tongues x 0.85-1.15) and light (x 0.8-1.2 brighter, x 0.85-1.3 longer);</item>
    /// <item>its own light flicker (phase and rate x 0.75-1.25; the pooled fire lights used the slot's fixed phase);</item>
    /// <item>a staggered start (0-0.6 s) and burn-out (the shuttle's main fire 1 s earlier to 0.4 s later, the others +-1.2 s);</item>
    /// <item>the ember rim on the cut edges of the shuttle's pieces: its own phases and rates (both waves were shared).</item>
    /// </list>
    /// Users: the shuttle's pieces (OzShuttleBreakup.FireBeat.cs), the fort cannon's pieces (OzFortGunBreakup.FireBeat.cs), the
    /// vehicle / cannon wrecks (UnitView.Deaths), the burning pieces of a vehicle that broke apart (WreckPieces), the burning ruins' light (OzRuinFires), the fort's wrecked cannon mount
    /// (FortGunMount). The fire light takes its style in OpenZFxPlayer.FireBeat.cs. Nothing here allocates per frame.
    /// </summary>
    public static class OzFireDesync
    {
        /// <summary>Tuning switch for side-by-side checks: false = every fire on the old common beat again.</summary>
        public static bool On = true;
        /// <summary>Spreads: beat rate (+-), every beat's jitter (+-), flame size (+-), light strength (+-), light life (min..max),
        /// light flicker rate (+-), start stagger (s), the ember rim's wave rates (+-).</summary>
        public static float RateSpread = 0.25f, BeatJitter = 0.2f, SizeSpread = 0.15f, LightSpread = 0.2f, LifeMin = 0.85f, LifeMax = 1.3f,
            FlickRateSpread = 0.25f, StartStagger = 0.6f, EmberRateSpread = 0.25f;
        /// <summary>Burn-out stagger of the shuttle's fires (s): the main one this much earlier .. later, the others the same.</summary>
        public static float MainOutEarly = 1f, MainOutLate = 0.4f, OtherOutEarly = 1.2f, OtherOutLate = 1.2f;
        /// <summary>The Play check on a shuttle kill: when it is measured (s after the break), the window it measures (s), the lag it
        /// searches (s) and the limit for the strongest pair (the light pulses' correlation, shifted by up to the lag).</summary>
        public static float ProbeAt = 10.5f, ProbeFrom = 1f, ProbeTo = 10f, ProbeLag = 0.3f, ProbeLimit = 0.6f;

        /// <summary>Play check counters since the program started.</summary>
        public static int Draws, VehicleBeats, RuinBeats, FortBeats, FortPieceStarts, PieceBeats, LightsTaken;
        /// <summary>The last shuttle kill's check: fires measured, the strongest pair at no shift and shifted, passed, the log line.</summary>
        public static int LastFires; public static float LastR0, LastLag; public static bool LastOk; public static string LastLine = "-";

        /// <summary>One fire's own numbers, drawn once (Draw).</summary>
        public struct Beat { public float rate, size, light, life, seed, flick, start, outR, emberA, emberB, emberRa, emberRb; }

        // ------------------------------------------------------------------ seeded numbers (no allocation)
        static uint Mix(uint h, uint v) { unchecked { h ^= v; h *= 16777619u; h ^= h >> 15; h *= 0x2C1B3C6Du; h ^= h >> 12; return h; } }
        /// <summary>A seed from three numbers (never 0: the xorshift needs a set bit).</summary>
        public static uint Seed(int a, int b, int c) { unchecked { uint h = Mix(Mix(Mix(2166136261u, (uint)a), (uint)b), (uint)c); return h == 0u ? 0x9E3779B9u : h; } }
        /// <summary>A stable hash of a name (the same in every run, unlike string.GetHashCode).</summary>
        public static int NameHash(string s)
        {
            unchecked { uint h = 2166136261u; if (s != null) for (int i = 0; i < s.Length; i++) { h ^= s[i]; h *= 16777619u; } return (int)h; }
        }
        /// <summary>0..1 from the state (xorshift32).</summary>
        public static float Rnd(ref uint s) { s ^= s << 13; s ^= s >> 17; s ^= s << 5; return (s & 0xFFFFFFu) / 16777216f; }
        static float Pm(ref uint s, float spread) => 1f + (Rnd(ref s) * 2f - 1f) * spread;

        /// <summary>A fire's own numbers, from its seed state (always the same twelve draws in the same order).</summary>
        public static Beat Draw(ref uint s)
        {
            Draws++;
            var b = new Beat();
            b.rate = Pm(ref s, RateSpread); b.size = Pm(ref s, SizeSpread); b.light = Pm(ref s, LightSpread);
            b.life = Mathf.Lerp(LifeMin, LifeMax, Rnd(ref s)); b.seed = Rnd(ref s) * 100f; b.flick = Pm(ref s, FlickRateSpread);
            b.start = Rnd(ref s) * StartStagger; b.outR = Rnd(ref s);
            b.emberA = Rnd(ref s) * 6.2832f; b.emberB = Rnd(ref s) * 6.2832f; b.emberRa = Pm(ref s, EmberRateSpread); b.emberRb = Pm(ref s, EmberRateSpread);
            return b;
        }

        /// <summary>The next beat: the old gap x the fire's rate x this beat's jitter (the old gap when off).</summary>
        public static float Gap(ref uint s, float baseGap, float rate) => On ? baseGap * rate * Pm(ref s, BeatJitter) : baseGap;

        /// <summary>The shuttle fire's burn-out shift (s), 0 when off.</summary>
        public static float OutShift(in Beat b, bool main) => !On ? 0f : main ? Mathf.Lerp(-MainOutEarly, MainOutLate, b.outR) : Mathf.Lerp(-OtherOutEarly, OtherOutLate, b.outR);

        // ------------------------------------------------------------------ the style handed to the next fire burst
        static bool pend, scaled; static float pendGain = 1f, pendLife = 1f, pendSeed, pendRate = 1f, flameMin0, flameVar0;

        /// <summary>Before one fire burst (WreckBurn / WreckFire / AddLight): its flame tongues take the fire's size, the light it adds
        /// takes the fire's strength, life, flicker phase and rate. End() right after, always.</summary>
        public static void Begin(in Beat b)
        {
            if (!On) return;
            pend = true; pendGain = b.light; pendLife = b.life; pendSeed = b.seed; pendRate = b.flick;
            if (!scaled) { flameMin0 = OpenZFxPlayer.WreckFlameMin; flameVar0 = OpenZFxPlayer.WreckFlameVar; scaled = true; }
            OpenZFxPlayer.WreckFlameMin = flameMin0 * b.size; OpenZFxPlayer.WreckFlameVar = flameVar0 * b.size;
        }

        public static void End()
        {
            pend = false;
            if (scaled) { OpenZFxPlayer.WreckFlameMin = flameMin0; OpenZFxPlayer.WreckFlameVar = flameVar0; scaled = false; }
        }

        /// <summary>OpenZFxPlayer.AddLight: the style of the burst in progress, taken once (false = a light of anything else).</summary>
        public static bool TakeLight(out float gain, out float life, out float seed, out float rate)
        {
            gain = pendGain; life = pendLife; seed = pendSeed; rate = pendRate;
            if (!pend) return false;
            pend = false; LightsTaken++;
            return true;
        }

        // ------------------------------------------------------------------ the other wreck fires (one-line hooks)
        /// <summary>UnitView.Deaths: a burning vehicle / cannon wreck's burst with its own style; `gap` (the old 0.25 s, or the
        /// smoulder's) becomes its own beat. The first burst comes on the death frame (wrecks killed by one blast together), so the
        /// gap after it takes the wreck's own start (0.1-0.6 s): their second bursts no longer land on the same frame.</summary>
        public static void VehicleBurn(OpenZFxPlayer fxp, int id, float deathT, ref float gap, Vector3 at, float k, bool burning, float spread, Vector3? fireAt)
        {
            uint s = Seed(id, 400, 1); var b = Draw(ref s);
            Begin(b);
            try { fxp.WreckBurn(at, k, burning, spread, fireAt); }
            finally { End(); }
            uint j = Seed(id, 400, 2 + Mathf.FloorToInt(deathT * 60f));
            gap = Gap(ref j, gap, b.rate) + (On && deathT >= 0f && deathT < 0.1f ? Mathf.Max(0.1f, b.start) : 0f);
            VehicleBeats++;
        }

        /// <summary>WreckPieces: a burning piece of a vehicle that broke apart (the three heaviest burn on a fixed 0.3 s beat, all
        /// three from the same frame, like the fort cannon's pieces): its burst with its own light style, and its own beat (rate,
        /// every beat's jitter, the gap after its first burst + 0.1-0.6 s). The piece's own numbers come from the wreck's id and the
        /// piece's seeded life time and mass (both fixed per piece at the break). Returns the gap to its next burst (was `gap`).</summary>
        public static float PieceBurn(OpenZFxPlayer fxp, int id, float life, float massKg, float age, float gap, Vector3 at, bool burning)
        {
            int key = Mathf.RoundToInt(life * 1000f), mass = Mathf.RoundToInt(massKg * 10f);
            uint s = Seed(id, key, mass ^ 400); var b = Draw(ref s);
            Begin(b);
            try { fxp.WreckSmoke(at, burning ? 0.6f : 0.35f, burning); }
            finally { End(); }
            uint j = Seed(id, key ^ 0x5A17, 8 + Mathf.FloorToInt(age * 60f));
            PieceBeats++;
            return Gap(ref j, gap, b.rate) + (On && age < 0.1f ? Mathf.Max(0.1f, b.start) : 0f);
        }

        /// <summary>OzRuinFires: a burning ruin's fire light with its own style; returns the gap to its next one (was 0.25 s).</summary>
        public static float RuinLight(OpenZFxPlayer fxp, int id, float now, Vector3 at, float hot)
        {
            uint s = Seed(id, 400, 3); var b = Draw(ref s);
            Begin(b);
            try { fxp.AddLight(at, OpenZFxCatalog.WreckFireLight.color, OpenZFxCatalog.BurnLightThree * 1.3f * hot, OpenZFxCatalog.BurnLightRange * 0.9f, 0.3f, 0.5f, 0.2f, false); }
            finally { End(); }
            uint j = Seed(id, 400, 4 + Mathf.FloorToInt(now * 60f));
            RuinBeats++;
            return Gap(ref j, 0.25f, b.rate);
        }

        /// <summary>FortGunMount: the wrecked mount's burst at one of its two fire spots (they took turns, so they blinked in turn: now
        /// each beat picks one by the seed), each spot with its own style; returns the gap to the next one.</summary>
        public static float FortBurn(OpenZFxPlayer fx, int fortId, int serial, Vector3 smokeAt, float k, bool fire, Vector3 fireA, Vector3 fireB, float gap)
        {
            uint j = Seed(fortId, 400, 5 + serial);
            bool a = On ? Rnd(ref j) < 0.5f : serial % 2 == 0;
            uint s = Seed(fortId, a ? 6 : 7, 400); var b = Draw(ref s);
            Begin(b);
            try { fx.WreckBurn(smokeAt, k, fire, 0.5f, fire ? (a ? fireA : fireB) : (Vector3?)null); }
            finally { End(); }
            FortBeats++;
            return Gap(ref j, gap, b.rate);
        }

        // ------------------------------------------------------------------ the Play check (once per shuttle kill, not per frame)
        /// <summary>The strongest pair of light pulse trains: rebuilt from each fire's beats (time, strength, life; the fire light's
        /// (1 - age / life)^0.5 fall), sampled at 30 Hz from ProbeFrom to ProbeTo; r0 = the correlation as they are, lag = the best
        /// correlation with one of them shifted by up to ProbeLag (a common rhythm shows here even when the phases differ).</summary>
        public static void Measure(float[][] times, float[][] gains, float[][] lives, int[] counts, int fires, out float r0Max, out float lagMax)
        {
            const float hz = 30f;
            int n = Mathf.Max(2, Mathf.RoundToInt((ProbeTo - ProbeFrom) * hz)), lagN = Mathf.RoundToInt(ProbeLag * hz);
            var sig = new float[fires][];
            for (int f = 0; f < fires; f++)
            {
                sig[f] = new float[n];
                for (int i = 0; i < n; i++)
                {
                    float t = ProbeFrom + i / hz, v = 0f;
                    for (int p = 0; p < counts[f]; p++)
                    {
                        float a = t - times[f][p];
                        if (a >= 0f && a < lives[f][p]) v += gains[f][p] * Mathf.Sqrt(1f - a / lives[f][p]);
                    }
                    sig[f][i] = v;
                }
                // minus the moving mean of the last / next 0.25 s: the burn's slow rise and burn-out out, the blinking kept
                int w = Mathf.RoundToInt(0.25f * hz); var c = new double[n + 1]; var d = new float[n];
                for (int i = 0; i < n; i++) c[i + 1] = c[i] + sig[f][i];
                for (int i = 0; i < n; i++) { int a = Mathf.Max(0, i - w), b = Mathf.Min(n, i + w + 1); d[i] = sig[f][i] - (float)((c[b] - c[a]) / (b - a)); }
                sig[f] = d;
            }
            r0Max = -1f; lagMax = -1f;
            for (int x = 0; x < fires; x++)
                for (int y = x + 1; y < fires; y++)
                    for (int l = -lagN; l <= lagN; l++)
                    {
                        float r = Corr(sig[x], sig[y], l, n);
                        if (l == 0) r0Max = Mathf.Max(r0Max, r);
                        lagMax = Mathf.Max(lagMax, r);
                    }
        }

        static float Corr(float[] a, float[] b, int lag, int n)
        {
            int i0 = Mathf.Max(0, -lag), i1 = Mathf.Min(n, n - lag), m = i1 - i0; if (m < 8) return 0f;
            double sa = 0, sb = 0; for (int i = i0; i < i1; i++) { sa += a[i]; sb += b[i + lag]; }
            double ma = sa / m, mb = sb / m, num = 0, da = 0, db = 0;
            for (int i = i0; i < i1; i++) { double u = a[i] - ma, v = b[i + lag] - mb; num += u * v; da += u * u; db += v * v; }
            return da > 1e-12 && db > 1e-12 ? (float)(num / System.Math.Sqrt(da * db)) : 0f;
        }
    }
}
