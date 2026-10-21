using System;

namespace OpenZ
{
    /// <summary>
    /// A rough measure of how bright the picture is at a time of day (pure C#, no UnityEngine, so the check outside the engine runs this
    /// very code). Built by DayNight.LookModel from its own day keys; OzDayOnlyClock uses it to spread the travel back to day (the option
    /// NIGHT &amp; RAIN switched off at dusk or at night) evenly over its seconds, so the picture brightens over the whole travel instead of
    /// in the short stretch of the clock where the night shade and the sun change.
    /// <list type="bullet">
    /// <item>Linear: the light on flat ground: the sun (or moon) colour x intensity x sin(elevation), plus the ambient (sky, horizon and
    /// ground colours), with the city's morning lift, the night's moon / ambient scale and the night grade's exposure, as DayNight.Apply and
    /// NightGrade.Apply set them. Colours are the keys' sRGB values blended as Apply blends them, then made linear.</item>
    /// <item>Lightness: the cube root of Linear against noon (0 black, 1 noon), close to how the eye judges brightness.</item>
    /// <item>Shade: the night shade (NightGrade.Night: the night grade's weight, the cool rim, the night fill).</item>
    /// </list>
    /// </summary>
    public sealed class OzDayLook
    {
        /// <summary>Numbers per key in the table: t, elevation, intensity, sun r g b, sky r g b, equator r g b, ground r g b.</summary>
        public const int Row = 15;
        /// <summary>The city's morning lift of sun and ambient (DayNight.MorningSunLift / MorningAmbientLift on a cool level, else 0).</summary>
        public float SunLift, AmbientLift;
        /// <summary>The night's moon and ambient scale and the night grade's exposure (NightGrade.MoonScale / AmbientScale / Exposure).</summary>
        public float MoonScale = 1f, AmbientScale = 1f, Exposure;

        readonly float[] k;
        readonly int n;
        readonly Func<float, float> night, liftK;
        readonly float noon;

        /// <param name="table">The day keys, Row numbers each, in time order (sRGB colours).</param>
        /// <param name="night">The night shade at t (NightGrade.Night).</param>
        /// <param name="liftK">The morning lift's share at t (DayNight.MorningLiftK).</param>
        public OzDayLook(float[] table, Func<float, float> night, Func<float, float> liftK)
        {
            k = table ?? new float[0];
            n = k.Length / Row;
            this.night = night; this.liftK = liftK;
            noon = Math.Max(1e-4f, LinearRaw(0f, false));
        }

        public static float Rep(float x) => x - (float)Math.Floor(x);
        static float Smooth(float x) { x = Math.Min(1f, Math.Max(0f, x)); return x * x * (3f - 2f * x); }
        static float Lerp(float a, float b, float f) => a + (b - a) * f;
        static float Lin(float c) => c <= 0.04045f ? c / 12.92f : (float)Math.Pow((c + 0.055f) / 1.055f, 2.4f);

        /// <summary>The linear luminance of the blend of two keys' sRGB colours (column col).</summary>
        float Lum(int a, int b, int col, float f)
        {
            float r = Lin(Lerp(k[a + col], k[b + col], f)), g = Lin(Lerp(k[a + col + 1], k[b + col + 1], f)), bl = Lin(Lerp(k[a + col + 2], k[b + col + 2], f));
            return 0.2126f * r + 0.7152f * g + 0.0722f * bl;
        }

        /// <summary>The night shade at t (0 by day, 1 at night).</summary>
        public float Shade(float t) => night != null ? night(Rep(t)) : 0f;

        /// <summary>The light on flat ground at t (linear, noon about 1.8).</summary>
        public float Linear(float t) => LinearRaw(Rep(t), true);

        float LinearRaw(float t, bool extras)
        {
            if (n < 2) return 1f;
            int i = 0;
            while (i < n - 2 && t > k[(i + 1) * Row]) i++;                                         // the key pair, as DayNight.Apply picks it
            int a = i * Row, b = (i + 1) * Row;
            float span = k[b] - k[a], f = Smooth(span > 1e-6f ? (t - k[a]) / span : 0f);
            float el = Lerp(k[a + 1], k[b + 1], f), inten = Lerp(k[a + 2], k[b + 2], f);
            float lift = extras && liftK != null ? liftK(t) : 0f, nk = extras ? Shade(t) : 0f;
            inten *= 1f + SunLift * lift;
            if (extras && MoonScale != 1f) inten *= Lerp(1f, MoonScale, nk);
            float sun = Lum(a, b, 3, f) * inten * Math.Max(0f, (float)Math.Sin(el * Math.PI / 180.0));
            float amb = 0.5f * Lum(a, b, 6, f) + 0.4f * Lum(a, b, 9, f) + 0.1f * Lum(a, b, 12, f);   // up-facing ground sees mostly sky
            amb *= 1f + AmbientLift * lift;
            if (extras && AmbientScale != 1f) amb *= Lerp(1f, AmbientScale, nk);
            float y = sun + amb;
            return extras ? y * (float)Math.Pow(2.0, Exposure * nk) : y;                                // the night grade's exposure (weight = shade)
        }

        /// <summary>0 black .. 1 noon: the cube root of the light against noon.</summary>
        public float Lightness(float t) => (float)Math.Pow(Math.Max(0f, Linear(t)) / noon, 1.0 / 3.0);
    }
}
