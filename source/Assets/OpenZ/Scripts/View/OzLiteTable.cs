// OpenZ (GPL-3.0) - version 1.1, the lighter MEDIUM / LOW presets of GRAPHICS QUALITY: their numbers and the two small rules
// that use them. Nothing of the engine is used in this file, so both rules are also counted outside the editor.
// HIGH has no row of its own here that the game reads: index 0 only repeats what HIGH has today, to be printed beside the others.
namespace OpenZ
{
    public static class OzLiteTable
    {
        // ------------------------------------------------------------------ the cuts (one bit each in OzLite.Off)
        public const int CutSunReach = 0, CutSunSteps = 1, CutNightLights = 2, CutNightShadows = 3, CutRain = 4, CutLampGlow = 5, CutLampBeams = 6, CutCount = 7;
        public static readonly string[] CutNames = { "shadow reach", "shadow steps", "night lights", "night light shadows", "rain", "lamp glow", "lamp beams" };

        // ------------------------------------------------------------------ the numbers: { HIGH as it is today, MEDIUM, LOW }
        /// <summary>The sun's (at night the moon's) shadows reach as far as the farthest ground in view, between these limits (m).
        /// At the full overview (camera 230 m up) the screen's lower edge lies 243 m away and its top corners 399 m.</summary>
        public static readonly float[] ReachMin = { 110f, 110f, 60f }, ReachMax = { 480f, 320f, 200f };
        /// <summary>What each preset's own settings file holds (m). Before this change that fixed number was the reach of a preset
        /// picked in the Options during play (a game STARTED on a preset followed the view up to HIGH's 480 m instead). Only
        /// printed and measured against; the game does not read it.</summary>
        public static readonly float[] ReachFile = { 260f, 180f, 120f };
        /// <summary>Shadow steps (cascades) of the sun. SunStepsBefore = what the preset had before this change.</summary>
        public static readonly int[] SunSteps = { 4, 2, 1 }, SunStepsBefore = { 4, 2, 2 };
        /// <summary>Night lights that really light the ground and the units at once (lamps, floods, searchlights and headlights share
        /// them; the others keep their glowing head, their beam and the painted pool of light on the ground).</summary>
        public static readonly int[] NightReal = { 40, 24, 12 };
        /// <summary>Of those, the most that go to vehicle headlights; and the vehicles that show a headlight cone and pool.</summary>
        public static readonly int[] Headlights = { 8, 6, 4 }, HeadlightCones = { 8, 6, 4 };
        /// <summary>Night lights that cast shadows: fort searchlights, nearby lamps and floods, and how many of the nearby ones get the sharp shadow map.</summary>
        public static readonly int[] SearchShadows = { 3, 3, 2 }, NearbyShadows = { 5, 3, 1 }, SharpShadows = { 2, 1, 0 };
        /// <summary>Share of the rain drawn (drops, ground splashes, drips from the trees). Wet ground, rain sound and thunder are not touched.</summary>
        public static readonly float[] Rain = { 1f, 0.65f, 0.4f };
        /// <summary>Night lights, LOW alone: is the soft glow around each lamp head drawn, and the cone of light in the air under it?
        /// (The game's own two switches; the fort's red alarm light and the Jungle's fires follow them too.) The lens, the bulb,
        /// the painted pool on the ground and the real light are not touched.</summary>
        public static readonly bool[] Halos = { true, true, false }, Beams = { true, true, false };

        /// <summary>Does this cut lower anything on this preset (1 = MEDIUM, 2 = LOW)? The measuring menu leaves the others out.</summary>
        public static bool Lowers(int cut, int level)
        {
            if (level < 1 || level > 2) return false;
            switch (cut)
            {
                case CutSunReach: return ReachMin[level] != ReachMin[0] || ReachMax[level] != ReachMax[0];
                case CutSunSteps: return SunSteps[level] != SunStepsBefore[level];
                case CutNightLights: return NightReal[level] < NightReal[0] || Headlights[level] < Headlights[0] || HeadlightCones[level] < HeadlightCones[0];
                case CutNightShadows: return SearchShadows[level] < SearchShadows[0] || NearbyShadows[level] < NearbyShadows[0] || SharpShadows[level] < SharpShadows[0];
                case CutRain: return Rain[level] < 1f;
                case CutLampGlow: return !Halos[level];
                case CutLampBeams: return !Beams[level];
                default: return false;
            }
        }

        // ------------------------------------------------------------------ the rule for keeping a cut, and the targets
        /// <summary>A cut stays only if, in the worst view of at least one level, the frame costs at least this much more without it
        /// (mean of 300 frames; main thread, or graphics chip).</summary>
        public const float LeastMainMs = 0.25f, LeastGpuMs = 0.40f;
        /// <summary>A preset must not cost more than it does today (either way of reaching it) in a level's worst view. More than this
        /// over today's window counts as more (main thread, graphics chip; the same allowances the measuring has for its own noise).</summary>
        public const float TodaySlackMainMs = 0.20f, TodaySlackGpuMs = 0.40f;
        /// <summary>Wanted: this share less main-thread time than HIGH in each level's worst view (MEDIUM, LOW).</summary>
        public static readonly float[] TargetLess = { 0f, 0.25f, 0.45f };

        // ------------------------------------------------------------------ rule 1: how many night lights the budget grants
        /// <summary>The night-light budget's own limit rule with this preset's floor. With the cut off (or at HIGH) it is the game's
        /// rule as it always was: never under 16, never over the fixture limit (16 wins when the limit is lower).</summary>
        public static int NightMax(int level, bool cut, int max, int maxLights)
        {
            int floor = 16;
            if (cut && level > 0 && level < NightReal.Length && NightReal[level] < floor) floor = NightReal[level];
            if (max < floor) return floor;
            if (max > maxLights) return maxLights;
            return max;
        }

        // ------------------------------------------------------------------ rule 2: how far the sun's shadows reach
        /// <summary>The game's own rule (shadows reach the farthest ground in view plus a margin, in steps, growing at once and
        /// shrinking late) between this preset's limits. With the cut off (or at HIGH) the limits are the game's own.</summary>
        /// <param name="far">distance from the camera to the farthest ground point in view (m)</param>
        /// <param name="current">the reach in force (m), 0 = none yet</param>
        public static float Reach(int level, bool cut, float far, float current, float highMin, float highMax, float margin, float step)
        {
            bool mine = cut && level > 0 && level < ReachMax.Length;
            float min = mine ? ReachMin[level] : highMin, max = mine ? ReachMax[level] : highMax;
            float want = far * margin;
            if (want < min) want = min; else if (want > max) want = max;
            float snapped = (float)System.Math.Ceiling(want / step) * step;
            return current <= 0f || snapped > current || snapped < current - step * 1.5f ? snapped : current;
        }
    }
}
