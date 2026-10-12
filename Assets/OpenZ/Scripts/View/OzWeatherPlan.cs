using System;

namespace OpenZ
{
    /// <summary>
    /// weather_l17_v1: the storm plan of one night, VIEW ONLY. Pure C# (no UnityEngine) so the offline harness runs exactly this
    /// code. Its own SplitMix64 generator seeded per (match, night): never UnityEngine.Random, never the sim RNG, never ZWorld
    /// state; the storm has no effect on gameplay.
    /// <list type="bullet">
    /// <item>chance per night (default 20 %); the shower starts in the moonlit night window (DayNight 0.47 .. 0.84) so it
    /// fades in and out inside the night</item>
    /// <item>total length 60-120 s game time incl. ~10 s fade in and ~10 s fade out; intensity swells slowly (0.8-1.0) in between</item>
    /// <item>2-5 lightning flashes spread over the shower (one per slot, jittered, never two within 40 % of a slot), at most
    /// one close strike (the only one that may draw a bolt), one mid, the rest far; plus 1-2 far rumbles with a faint glow</item>
    /// <item>thunder delay = distance / 343 m/s, clamped to 0.8-4 s</item>
    /// </list>
    /// </summary>
    public sealed class OzStormPlan
    {
        public const int Far = 0, Mid = 1, Close = 2, Rumble = 3;
        public struct Flash
        {
            public float At;          // seconds after the storm start
            public int Kind;          // Far, Mid, Close, Rumble
            public float Distance;    // m (thunder delay, volume)
            public float Brightness;  // 0..1 peak of the light pulse
            public float Delay;       // thunder delay, s
            public int Pulses;        // 1-3 flickers
            public float Azimuth;     // deg, direction the light comes from (0 = +z / north, 90 = +x)
            public uint Seed;         // per-flash shape (flicker offsets, bolt path)
        }
        public int Night;
        public ulong Seed;
        public bool Storm;
        public float StartT;          // DayNight.timeOfDay at which it starts (ignored for a forced storm)
        public float Total, FadeIn, FadeOut;
        public float SwellA, SwellB;  // swell phases
        public Flash[] Flashes = Array.Empty<Flash>();

        public int VisibleFlashes { get { int n = 0; foreach (var f in Flashes) if (f.Kind != Rumble) n++; return n; } }
    }

    public static class OzWeatherPlan
    {
        public const float NightFrom = 0.47f, NightTo = 0.84f;        // storm window inside NightGrade's full night (0.52-0.84 +- fades)
        public const float NightStart = 0.46f, NightEnd = 0.86f;      // a new "night" (plan) begins when the clock enters 0.46
        public const float SoundSpeed = 343f;

        /// <summary>SplitMix64 (Steele, Lea, Flood 2014): tiny, portable, identical on Mono and .NET.</summary>
        public struct Rng
        {
            ulong s;
            public Rng(ulong seed) { s = seed; }
            public ulong Next() { ulong z = (s += 0x9E3779B97F4A7C15UL); z = (z ^ (z >> 30)) * 0xBF58476D1CE4E5B9UL; z = (z ^ (z >> 27)) * 0x94D049BB133111EBUL; return z ^ (z >> 31); }
            public float Value() => (Next() >> 40) * (1f / 16777216f);                     // [0, 1)
            public float Range(float a, float b) => a + (b - a) * Value();
            public int Range(int a, int bExclusive) => a + (int)(Value() * (bExclusive - a));
        }

        public static ulong Mix(ulong a, ulong b) { var r = new Rng(a * 0x9E3779B97F4A7C15UL ^ (b + 0x632BE59BD9B4E019UL)); r.Next(); return r.Next(); }

        /// <summary>The seed of one night: the match seed (ZGame.seed x the per-match weather salt) and the night number.</summary>
        public static ulong NightSeed(int gameSeed, int salt, int night) => Mix(Mix((ulong)(uint)gameSeed, (ulong)(uint)salt), (ulong)(uint)night);

        public static float ThunderDelay(float distance) => Math.Min(4f, Math.Max(0.8f, distance / SoundSpeed));

        /// <summary>The plan of one night: Storm false on ~(1 - chance) of the nights.</summary>
        public static OzStormPlan ForNight(ulong seed, int night, float chance, float cycleSeconds, float minTotal = 60f, float maxTotal = 120f, float fade = 10f)
        {
            var r = new Rng(seed);
            var p = new OzStormPlan { Night = night, Seed = seed };
            p.Storm = r.Value() < chance;
            if (!p.Storm) return p;
            Fill(p, ref r, r.Range(minTotal, maxTotal), fade);
            float span = cycleSeconds > 1f ? p.Total / cycleSeconds : 0f;                 // storm length in day fractions
            float latest = Math.Max(NightFrom, NightTo - span);
            p.StartT = r.Range(NightFrom, latest);
            return p;
        }

        /// <summary>A storm right now (ForceStorm, Play proofs): same rules, chosen length.</summary>
        public static OzStormPlan Forced(ulong seed, float total, float fade = 10f)
        {
            var r = new Rng(seed);
            var p = new OzStormPlan { Night = -1, Seed = seed, Storm = true };
            Fill(p, ref r, Math.Max(2f * fade + 10f, total), fade);
            return p;
        }

        static void Fill(OzStormPlan p, ref Rng r, float total, float fade)
        {
            p.Total = total; p.FadeIn = fade; p.FadeOut = fade;
            p.SwellA = r.Range(0f, 6.2832f); p.SwellB = r.Range(0f, 6.2832f);
            int n = r.Range(2, 6);                                                     // 2..5 visible flashes
            float w0 = p.FadeIn * 0.6f, w1 = p.Total - p.FadeOut * 0.8f;             // first light once the rain is on, last before it is gone
            float slot = (w1 - w0) / n;
            int close = n >= 3 && r.Value() < 0.65f ? r.Range(1, n) : -1;             // at most one close strike, never the first flash
            int mid = r.Range(0, n); if (mid == close) mid = (mid + 1) % n;
            var list = new System.Collections.Generic.List<OzStormPlan.Flash>();
            for (int i = 0; i < n; i++)
            {
                int kind = i == close ? OzStormPlan.Close : i == mid ? OzStormPlan.Mid : OzStormPlan.Far;
                var f = new OzStormPlan.Flash { At = w0 + slot * (i + 0.2f + 0.6f * r.Value()), Kind = kind, Azimuth = r.Range(0f, 360f), Seed = (uint)r.Next() };
                switch (kind)
                {
                    case OzStormPlan.Close: f.Distance = r.Range(275f, 420f); f.Brightness = r.Range(0.9f, 1f); f.Pulses = r.Range(2, 4); break;
                    case OzStormPlan.Mid: f.Distance = r.Range(430f, 800f); f.Brightness = r.Range(0.6f, 0.8f); f.Pulses = r.Range(1, 4); break;
                    default: f.Distance = r.Range(850f, 1370f); f.Brightness = r.Range(0.35f, 0.55f); f.Pulses = r.Range(1, 3); break;
                }
                f.Delay = ThunderDelay(f.Distance);
                list.Add(f);
            }
            int rumbles = r.Range(1, 3);                                               // far rumbles: thunder throughout, a faint glow
            for (int k = 0; k < rumbles; k++)
            {
                for (int attempt = 0; attempt < 8; attempt++)
                {
                    float at = r.Range(w0, w1);
                    bool free = true;
                    foreach (var o in list) if (Math.Abs(o.At - at) < 5f) { free = false; break; }
                    if (!free) continue;
                    var f = new OzStormPlan.Flash { At = at, Kind = OzStormPlan.Rumble, Distance = r.Range(1150f, 1400f), Brightness = 0.1f, Pulses = 1, Azimuth = r.Range(0f, 360f), Seed = (uint)r.Next() };
                    f.Delay = ThunderDelay(f.Distance);
                    list.Add(f);
                    break;
                }
            }
            list.Sort((a, b) => a.At.CompareTo(b.At));
            p.Flashes = list.ToArray();
        }

        static float Smooth(float x) { x = Math.Min(1f, Math.Max(0f, x)); return x * x * (3f - 2f * x); }

        /// <summary>Rain intensity 0..1 at s seconds into the storm: smooth fade in, a slow swell (0.8-1.0), smooth fade out.</summary>
        public static float Intensity(OzStormPlan p, float s)
        {
            if (p == null || !p.Storm || s < 0f || s > p.Total) return 0f;
            float fin = Smooth(s / Math.Max(0.01f, p.FadeIn)), fout = Smooth((p.Total - s) / Math.Max(0.01f, p.FadeOut));
            float swell = 0.9f + 0.07f * (float)Math.Sin(s * 0.11f + p.SwellA) + 0.03f * (float)Math.Sin(s * 0.37f + p.SwellB);
            return Math.Min(fin, fout) * swell;
        }

        /// <summary>Light of one flash t seconds after it fired (0..~1.2 x brightness): 1-3 flickers, 15 ms attack, ~80 ms decay.</summary>
        public static float FlashLight(in OzStormPlan.Flash f, float t)
        {
            if (t < 0f || t > 1.2f) return 0f;
            var r = new Rng(f.Seed);
            float sum = 0f, at = 0f;
            for (int k = 0; k < f.Pulses; k++)
            {
                float amp = k == 0 ? 1f : r.Range(0.45f, 0.95f);
                float decay = r.Range(0.06f, 0.12f);
                float dt = t - at;
                if (dt >= 0f) sum += amp * (dt < 0.015f ? dt / 0.015f : (float)Math.Exp(-(dt - 0.015f) / decay));
                at += r.Range(0.08f, 0.2f);
            }
            return Math.Min(1.25f, sum) * f.Brightness;
        }
    }
}
