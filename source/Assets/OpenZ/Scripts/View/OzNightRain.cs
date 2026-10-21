using UnityEngine;

namespace OpenZ
{
    /// <summary>
    /// The player's option NIGHT &amp; RAIN (OPTIONS, GRAPHICS tab, in the main menu and in the pause menu). The switch is
    /// OzWeather.Enabled, saved as oz_weather, default ON, so every player keeps the choice made under the row's old name.
    /// <list type="bullet">
    /// <item>ON (the default): the day / night cycle and the weather exactly as before.</item>
    /// <item>OFF, on every level: the day clock never goes into dusk or night (OzDayOnlyClock: the level-start sunrise runs on into
    /// the day and the clock stands there), so no night look, no lamps, searchlights or night sounds, and the endings play by day;
    /// no rain, no storm, no lightning, no thunder, no wet ground (the city's night storm and the jungle's day rain start nothing,
    /// not even when forced). Switched off during a match: dusk or night travels back to day in 4 s, the rain fades in 3 s, the wet
    /// ground dries in a few seconds and a thunder still on its way is dropped.</item>
    /// <item>View only: the rules never read the day clock or the weather, so the switch changes nothing in play.</item>
    /// </list>
    /// Hooked from DayNight.Update (one line). The counters below are for the Play check (menu OpenZ/V1.1/KW ...).
    /// </summary>
    public static class OzNightRain
    {
        public const string Label = "NIGHT & RAIN";
        public const string Tip = "NIGHT & RAIN|On: day turns to night and it can rain. Off: always day, no rain.|";
        /// <summary>The clock while the option is off (its numbers can be tuned live).</summary>
        public static readonly OzDayOnlyClock Day = new OzDayOnlyClock();
        /// <summary>Once the option is off, the city level's wet ground dries with this time constant (s; on: its own 45 s).</summary>
        public static float OffDrySeconds = 0.7f;

        /// <summary>True on the frames the day-only clock holds the level (the option off, a match running, no briefing).</summary>
        public static bool DayOnlyNow { get; private set; }

        // ------------------------------------------------------------------ counters for the Play check (ResetCounters)
        /// <summary>Frames held by the day-only clock; of them, frames outside a travel back to day that still had any night shade.</summary>
        public static int OffFrames, OffNightFrames, TravelsSeen;
        /// <summary>The most night shade (NightGrade.Night, 0 day .. 1 night) outside a travel; the largest change of the clock in one
        /// frame (day fractions); the game seconds of the last travel back to day.</summary>
        public static float MaxOffNightK, MaxOffStep, LastTravelSeconds = -1f;
        static bool wasEasing; static float easeAt;

        public static void ResetCounters()
        {
            OffFrames = OffNightFrames = TravelsSeen = 0; MaxOffNightK = MaxOffStep = 0f; LastTravelSeconds = -1f;
        }

        /// <summary>Called by DayNight.Update in Play with the clock before and after its own advance; returns the clock to show.</summary>
        public static float Clock(DayNight dn, float before, float after, bool briefing)
        {
            bool active = Application.isPlaying && !OzWeather.Enabled && dn && dn.running && !briefing && ZGame.I;
            if (active) Day.Look = dn.LookModel();                                    // the travel back to day eased by the look (OzDayLook.cs)
            float t = Day.Step(before, after, Time.deltaTime, active);
            if (active != DayOnlyNow)
            {
                DayOnlyNow = active;
                if (active) Debug.Log($"[OzNightRain] NIGHT & RAIN off: the level stays in daylight (clock t {before:F3}; the morning runs on to {Day.Hold:F3} and stands there)");
            }
            if (!active) { wasEasing = false; return t; }
            OffFrames++;
            float step = Mathf.Abs(Mathf.DeltaAngle(before * 360f, t * 360f)) / 360f;
            if (step > MaxOffStep) MaxOffStep = step;
            if (Day.Easing)
            {
                if (!wasEasing)
                {
                    easeAt = Time.time; TravelsSeen++;
                    Debug.Log($"[OzNightRain] NIGHT & RAIN off at clock t {Day.EaseFrom:F3}: back to day ({Day.Hold:F3}) in {Day.EaseSeconds:0.#} s, {(Day.EaseTravel >= 0f ? "forward through the dawn" : "back through the afternoon")}");
                }
            }
            else
            {
                if (wasEasing) LastTravelSeconds = Time.time - easeAt;
                float n = NightGrade.Night(t);
                if (n > MaxOffNightK) MaxOffNightK = n;
                if (n > 0.001f) OffNightFrames++;
            }
            wasEasing = Day.Easing;
            return t;
        }

        public static string Status() =>
            $"[OzNightRain] option {(OzWeather.Enabled ? "ON" : "OFF")}, day-only {(DayOnlyNow ? "holding" : "idle")}{(Day.Easing ? " (travelling back to day)" : "")}, clock t {NightGrade.TimeOfDay:F3}, night shade {NightGrade.NightK:F2}"
            + $" | held frames {OffFrames}, of them with night shade {OffNightFrames} (most {MaxOffNightK:F3}), travels {TravelsSeen} (last {LastTravelSeconds:F2} s), largest clock step {MaxOffStep:F4}";
    }
}
