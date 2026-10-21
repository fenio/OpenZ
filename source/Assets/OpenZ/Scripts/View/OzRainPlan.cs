using System;

namespace OpenZ
{
    /// <summary>
    /// The plan of one day's tropical rain on level 16, VIEW ONLY, beside the night plan of the city level (OzWeatherPlan.cs).
    /// Pure C# (no UnityEngine), so the headless check runs exactly this code. Its own generator per match and day
    /// (OzWeatherPlan.Rng, seeded from the match seed, the weather salt and the day number): never the game's own dice, never
    /// the rules, no effect on play.
    /// <list type="bullet">
    /// <item>never on the first day: a level starts at sunrise (day clock 0.935), the roll is made when the clock passes the
    /// </item>
    /// <item>every day from the second: one roll, 50 %, at most one spell a day</item>
    /// <item>the first drops fall between clock 0.97 and (0.22 minus the spell), so the whole spell lies in full daylight:
    /// </item>
    /// <item>90 to 150 s in all, 15 s fade in, 25 s fade out, a slow swell between 80 and 100 % in between</item>
    /// <item>the wind turns and rises 8 s before the first drops and stays 15 s after the last; one direction per spell;
    /// it turns the short way round while its speed rises (BlendWind), never through a calm</item>
    /// <item>2 to 4 soft far rumbles, none in the first 15 s, at least 12 s apart; no flash, no light, no bolt</item>
    /// </list>
    /// </summary>
    public sealed class OzRainSpell
    {
        public struct Rumble
        {
            public float At;        // seconds after the first drops
            public float Volume;    // 0..1
            public float Pitch;
            public uint Seed;       // which take
        }
        public int Day;                       // 2 = the second day; -1 = forced (proofs)
        public ulong Seed;
        public bool Rain;
        public float StartOff;                // day fractions after the sunrise roll (clock 0.90) at which the first drops fall
        public float Total, FadeIn, FadeOut;  // seconds
        public float SwellA, SwellB;
        public float WindLead, WindTail;      // seconds
        public float WindAzimuth;             // degrees, the way the wind GOES: 0 = +z (north), 90 = +x (east)
        public Rumble[] Rumbles = Array.Empty<Rumble>();

        /// <summary>The day clock (0..1) of the first drops.</summary>
        public float StartClock { get { float t = OzRainDayPlan.SunriseRoll + StartOff; return t - (float)Math.Floor(t); } }
    }

    public static class OzRainDayPlan
    {
        public const float SunriseRoll = 0.90f;                 // the day clock at which a new day is counted and rolled
        public const float FirstDropsFrom = 0.07f;              // day fractions after the roll = clock 0.97
        public const float OverBy = 0.32f;                      // day fractions after the roll = clock 0.22: every spell has ended
        public const float Chance = 0.5f;
        public const float MinSeconds = 90f, MaxSeconds = 150f, FadeInSeconds = 15f, FadeOutSeconds = 25f;
        public const float WindLeadSeconds = 8f, WindTailSeconds = 15f;
        public const float MinPlaySeconds = 420f;               // the second lock: no rain before 7:00 of play
        public const int FirstRainDay = 2;
        public const float RumbleQuiet = 15f, RumbleGap = 12f;
        public const float LateStart = 0.03f;                   // a clock that jumped further past the start than this skips the spell

        /// <summary>The seed of one day: the match seed, the per-match weather salt and the day number, with a constant of this
        /// level so that the jungle days and the city nights of the same match never share a sequence.</summary>
        public static ulong DaySeed(int gameSeed, int salt, int day) => OzWeatherPlan.Mix(OzWeatherPlan.NightSeed(gameSeed, salt, day), 0x4A554E474C453136UL);

        /// <summary>The plan of one day: Rain false on about (1 - chance) of the days, and always before FirstRainDay.</summary>
        public static OzRainSpell ForDay(ulong seed, int day, float chance, float cycleSeconds)
        {
            var r = new OzWeatherPlan.Rng(seed);
            var p = new OzRainSpell { Day = day, Seed = seed };
            p.Rain = r.Value() < chance && day >= FirstRainDay;
            if (!p.Rain) return p;
            Fill(p, ref r, r.Range(MinSeconds, MaxSeconds));
            float span = cycleSeconds > 1f ? p.Total / cycleSeconds : 0f;
            float latest = Math.Max(FirstDropsFrom, OverBy - span);
            p.StartOff = r.Range(FirstDropsFrom, latest);
            return p;
        }

        /// <summary>A spell right now (proofs): the same rules, a chosen length and, if wanted, a chosen wind direction.</summary>
        public static OzRainSpell Forced(ulong seed, float total, float azimuth = -1f)
        {
            var r = new OzWeatherPlan.Rng(seed);
            var p = new OzRainSpell { Day = -1, Seed = seed, Rain = true };
            Fill(p, ref r, Math.Max(FadeInSeconds + FadeOutSeconds + 5f, total));
            if (azimuth >= 0f) p.WindAzimuth = azimuth;
            return p;
        }

        static void Fill(OzRainSpell p, ref OzWeatherPlan.Rng r, float total)
        {
            p.Total = total; p.FadeIn = FadeInSeconds; p.FadeOut = FadeOutSeconds;
            p.WindLead = WindLeadSeconds; p.WindTail = WindTailSeconds;
            p.SwellA = r.Range(0f, 6.2832f); p.SwellB = r.Range(0f, 6.2832f);
            p.WindAzimuth = r.Range(0f, 360f);
            // rumbles: n sorted points in the room that is left when (n - 1) gaps are taken out, then the gaps put back:
            // never two nearer than the gap, none in the quiet start, the last one before the rain has faded
            int n = r.Range(2, 5);
            float w0 = RumbleQuiet, w1 = total - p.FadeOut * 0.6f;
            while (n > 1 && w1 - w0 < (n - 1) * RumbleGap + 1f) n--;
            float room = Math.Max(0f, w1 - w0 - (n - 1) * RumbleGap);
            var at = new float[n];
            for (int i = 0; i < n; i++) at[i] = r.Value() * room;
            Array.Sort(at);
            p.Rumbles = new OzRainSpell.Rumble[n];
            for (int i = 0; i < n; i++)
                p.Rumbles[i] = new OzRainSpell.Rumble { At = w0 + at[i] + i * RumbleGap, Volume = r.Range(0.34f, 0.5f), Pitch = r.Range(0.9f, 1f), Seed = (uint)r.Next() };
        }

        static float Smooth(float x) { x = Math.Min(1f, Math.Max(0f, x)); return x * x * (3f - 2f * x); }

        /// <summary>Rain 0..1 at s seconds after the first drops: 15 s in, a slow swell (0.8 to 1.0), 25 s out.</summary>
        public static float Intensity(OzRainSpell p, float s)
        {
            if (p == null || !p.Rain || s < 0f || s > p.Total) return 0f;
            float fin = Smooth(s / Math.Max(0.01f, p.FadeIn)), fout = Smooth((p.Total - s) / Math.Max(0.01f, p.FadeOut));
            float swell = 0.9f + 0.07f * (float)Math.Sin(s * 0.11f + p.SwellA) + 0.03f * (float)Math.Sin(s * 0.37f + p.SwellB);
            return Math.Min(fin, fout) * swell;
        }

        /// <summary>The rain wind 0..1 at s seconds after the first drops: it rises from -WindLead, holds, and falls until
        /// Total + WindTail.</summary>
        public static float WindK(OzRainSpell p, float s)
        {
            if (p == null || !p.Rain || s < -p.WindLead || s > p.Total + p.WindTail) return 0f;
            if (s < 0f) return Smooth((s + p.WindLead) / Math.Max(0.01f, p.WindLead));
            if (s <= p.Total) return 1f;
            return Smooth((p.Total + p.WindTail - s) / Math.Max(0.01f, p.WindTail));
        }

        /// <summary>Seconds from the wind's first stir to its last: the whole spell as the player meets it.</summary>
        public static float WholeSeconds(OzRainSpell p) => p == null || !p.Rain ? 0f : p.WindLead + p.Total + p.WindTail;

        /// <summary>The level's calm wind (m/s, world x / z: 2.5 m/s on the bearing of the two other levels) and the speed of the rain wind.</summary>
        public const float CalmWindX = 2.236f, CalmWindZ = 1.118f, RainWindSpeed = 5f;

        /// <summary>The level's wind while a spell's wind comes and goes: k = 0 the calm wind, 1 the rain wind (rainDir = the
        /// way it goes, world x / z). The direction TURNS from the calm bearing to the spell's, the short way round, and the
        /// speed rises apart from it. (Two arrows blended in a straight line pass through a calm and flip when the spell
        /// blows against the calm wind: flags and plants would snap round.)</summary>
        public static void BlendWind(float calmX, float calmZ, float rainDirX, float rainDirZ, float rainSpeed, float k, out float x, out float z)
        {
            k = Math.Min(1f, Math.Max(0f, k));
            float calmSpeed = (float)Math.Sqrt(calmX * calmX + calmZ * calmZ);
            double a0 = calmSpeed > 1e-6f ? Math.Atan2(calmX, calmZ) : 0.0;                                   // bearing: 0 = +z (north), a quarter turn = +x (east)
            double a1 = rainDirX * rainDirX + rainDirZ * rainDirZ > 1e-12f ? Math.Atan2(rainDirX, rainDirZ) : a0;
            double turn = a1 - a0;
            while (turn > Math.PI) turn -= 2.0 * Math.PI;
            while (turn < -Math.PI) turn += 2.0 * Math.PI;
            double a = a0 + turn * k; float speed = calmSpeed + (rainSpeed - calmSpeed) * k;
            x = (float)Math.Sin(a) * speed; z = (float)Math.Cos(a) * speed;
        }
    }

    /// <summary>
    /// The day counter and the start of each spell, as a plain state machine fed once a frame with the day clock. The level's
    /// weather object drives it in the game; the headless check drives the same object on a synthetic clock.
    /// </summary>
    public sealed class OzRainSchedule
    {
        public readonly int GameSeed, Salt;
        public readonly float CycleSeconds;
        public float Chance = OzRainDayPlan.Chance;
        /// <summary>1 on the day the level starts in; counted up each time the clock passes the sunrise roll.</summary>
        public int Day { get; private set; } = 1;
        public OzRainSpell Today { get; private set; }
        public bool TodayUsed { get; private set; }
        /// <summary>Seconds of play (the day clock's own time base; the briefing and a pause do not count).</summary>
        public float PlaySeconds { get; private set; }
        public int Rolls { get; private set; }
        public int Started { get; private set; }
        public int Skipped { get; private set; }
        float last = -1f;

        public OzRainSchedule(int gameSeed, int salt, float cycleSeconds) { GameSeed = gameSeed; Salt = salt; CycleSeconds = cycleSeconds > 1f ? cycleSeconds : 720f; }

        static float Rep(float x) => x - (float)Math.Floor(x);

        /// <summary>One frame. clock = the day clock 0..1, dt = its seconds this frame (0 while paused or while the briefing
        /// holds the clock), enabled = the WEATHER option, busy = a spell is running. Returns the spell whose wind must rise
        /// NOW, else null. A spell that cannot start (option off, the 7:00 lock, the clock jumped past it) is skipped for
        /// that day, never moved.</summary>
        public OzRainSpell Step(float clock, float dt, bool enabled, bool busy)
        {
            PlaySeconds += Math.Max(0f, dt);
            if (last < 0f) { last = clock; return null; }
            float step = Rep(clock - last);                                    // forward travel of the clock this frame (0..1)
            float toRoll = Rep(OzRainDayPlan.SunriseRoll - last);              // travel left until the roll
            bool rolled = step > 0f && step < 0.5f && toRoll > 0f && toRoll <= step;
            last = clock;
            if (rolled)
            {
                Day++;
                Today = OzRainDayPlan.ForDay(OzRainDayPlan.DaySeed(GameSeed, Salt, Day), Day, Chance, CycleSeconds);
                TodayUsed = false; Rolls++;
            }
            if (Today == null || !Today.Rain || TodayUsed) return null;
            float since = Rep(clock - OzRainDayPlan.SunriseRoll);              // day fractions since the roll
            float lead = Today.WindLead / CycleSeconds;
            if (since < Today.StartOff - lead || since > 0.6f) return null;    // not yet (or the clock was set back behind the roll)
            TodayUsed = true;
            bool late = since > Today.StartOff + OzRainDayPlan.LateStart;
            if (!enabled || busy || late || PlaySeconds < OzRainDayPlan.MinPlaySeconds) { Skipped++; return null; }
            Started++;
            return Today;
        }
    }
}
