using System;

namespace OpenZ
{
    /// <summary>
    /// The day clock while the player's option NIGHT &amp; RAIN is OFF (OzNightRain.cs): daylight for the whole match. Pure C# (no
    /// UnityEngine), so the check outside the engine runs exactly this code. DayNight.Update feeds it once a frame with the clock
    /// before and after its own advance (0..1: 0 noon, 0.32 golden dusk, 0.46 to 0.86 night, 0.90 dawn; a level starts at the
    /// sunrise, 0.935).
    /// <list type="bullet">
    /// <item>The morning (from dawn, DawnFrom, up to Hold): the clock runs on as usual and stops exactly at Hold.</item>
    /// <item>The day (Hold to Latest): the clock stands where it is.</item>
    /// <item>Anywhere else (the option switched off at dusk or at night, or the clock set there from outside): the clock travels to
    /// Hold the shorter way round in EaseSeconds of game time, smoothly in and out (no cut), then stands. A pause holds it.</item>
    /// </list>
    /// Not active (the option ON, the level-start briefing, a clock a tool has frozen): the clock is handed back untouched and a
    /// travel under way is dropped.
    /// </summary>
    public sealed partial class OzDayOnlyClock
    {
        /// <summary>The time of day the level keeps: the day look of every day picture (high sun, no lamps).</summary>
        public float Hold = 0.08f;
        /// <summary>The last time of day that still counts as day (the last day key before the golden dusk).</summary>
        public float Latest = 0.22f;
        /// <summary>From here (dawn, the night shade gone) up to Hold the clock runs on: the level-start sunrise lies in it.</summary>
        public float DawnFrom = 0.92f;
        /// <summary>Game seconds of the travel back to day.</summary>
        public float EaseSeconds = 4f;

        /// <summary>A travel back to day is under way.</summary>
        public bool Easing { get; private set; }
        /// <summary>Travels started since this object was made.</summary>
        public int Eases { get; private set; }
        /// <summary>The clock the last travel started from, and its length (day fractions; below 0 = backwards through the afternoon).</summary>
        public float EaseFrom => e0;
        public float EaseTravel => eDist;
        float e0, eDist, eT;

        public static float Rep(float x) => x - (float)Math.Floor(x);
        static float Smooth(float x) { x = Math.Min(1f, Math.Max(0f, x)); return x * x * (3f - 2f * x); }
        /// <summary>t on the arc that runs forward from a to b (a on it, b not).</summary>
        static bool OnArc(float t, float a, float b) => Rep(t - a) < Rep(b - a);

        /// <summary>The morning: from dawn up to (not including) Hold.</summary>
        public bool InMorning(float t) => OnArc(Rep(t), DawnFrom, Hold);
        /// <summary>The day: from Hold up to and including Latest.</summary>
        public bool InDay(float t) => Rep(Rep(t) - Hold) <= Rep(Latest - Hold);

        /// <summary>One frame. before = the clock at the start of the frame, after = the clock after DayNight's own advance,
        /// dt = game seconds of this frame (0 while paused), active = the option is off and the match clock may be held.
        /// Returns the clock to show this frame.</summary>
        public float Step(float before, float after, float dt, bool active)
        {
            if (!active) { Easing = false; return after; }
            if (!Easing)
            {
                float b = Rep(before);
                if (InMorning(b))
                {
                    float a = Rep(after), travel = Rep(a - b), left = Rep(Hold - b);
                    return travel < 0.5f && travel >= left ? Hold : a;                             // runs on, stops exactly at Hold
                }
                if (InDay(b)) return b;                                                             // stands
                float fwd = Rep(Hold - b), back = Rep(b - Hold);                                    // dusk, night or dawn: back to day
                e0 = b; eDist = fwd <= back ? fwd : -back; eT = 0f; Easing = true; Eases++;
                BuildLookPath();                                                                    // the travel eased by the look (OzDayOnlyClock.Look.cs)
            }
            eT += Math.Max(0f, dt);
            if (eT >= EaseSeconds) { Easing = false; return Hold; }
            return Rep(e0 + eDist * AlongLook(Smooth(eT / Math.Max(0.01f, EaseSeconds))));
        }
    }
}
