using UnityEngine;

namespace OpenZ
{
    /// <summary>
    /// Level 16: how loud the jungle's birds and monkeys are, and when they keep quiet. VIEW ONLY, nothing of the rules.
    /// <list type="bullet">
    /// <item>LOUDNESS. The parrots and monkeys of JungleFauna play through L16AnimalKit on the world bus, with no event table, no
    /// mix trim and no duck under voices: a group's take-off squawk reached about -19 dB at the listener and a monkey's alarm call
    /// -20 dB, over the briefing voice (-21) and the laser shot (-21). The critters of the other levels sit 10 dB under their table
    /// level (mix trim "critter_*"). <see cref="Gain"/> gives the jungle's birds and monkeys the same 10 dB (LifeDb) and the
    /// ambience bus's own duck (under voices and in a fight, as the jungle sound bed has it). The crocodiles and the snakes keep
    /// their levels.</item>
    /// <item>QUIET AT NIGHT AND IN RAIN. <see cref="Hushed"/>: night (the level's night grade over 0.6) or rain (the weather's rain
    /// over 0.15); it lets go at under 0.45 and under 0.03 (no flicker at the edge). JungleFauna's parrots fly off and its monkeys
    /// climb into the crowns while it holds, and come back after it; their calls are not played. The bird calls of the sound bed
    /// (AudioDirector) stop too, and the day bed steps down by up to 6 dB under rain (<see cref="DayBedShare"/>).</item>
    /// </list>
    /// Statics, so Play can tune them without a rebuild.
    /// </summary>
    public static class OzJungleLife
    {
        /// <summary>Level of the jungle's birds and monkeys against what JungleFauna asks for (dB).</summary>
        public static float LifeDb = -10f;
        /// <summary>Night grade and rain at which the birds and monkeys go, and under which they come back.</summary>
        public static float NightOn = 0.6f, NightOff = 0.45f, RainOn = 0.15f, RainOff = 0.03f;
        /// <summary>The day bed's share under full rain (0.25 = -6 dB: the bed is a loop played at the square root of its share).</summary>
        public static float DayBedRainShare = 0.25f;

        static bool hushed; static int frame = -1;
        /// <summary>Proof: how often the jungle went quiet / came back since the level started.</summary>
        public static int HushStarts, HushEnds;

        /// <summary>Is the event one of the jungle's birds or monkeys (not a crocodile, not a snake)?</summary>
        public static bool IsLife(string evt) => evt != null && (evt.Contains("parrot") || evt.Contains("monkey") || evt == "dtl_leaves_rustle");

        /// <summary>The factor L16AnimalKit puts on an event's volume.</summary>
        public static float Gain(string evt)
        {
            if (!IsLife(evt)) return 1f;
            float g = Mathf.Pow(10f, LifeDb / 20f);
            var ad = AudioDirector.I;
            if (ad)
            {   // the ambience bus's duck (voices, a fight), without the master volume a second time
                float master = ad.VoiceBusLinear(null), amb = ad.VoiceBusLinear("Ambience");
                if (master > 1e-4f) g *= Mathf.Clamp01(amb / master);
            }
            return g;
        }

        public static float NightNow => DetailKit.Night;
        public static float RainNow => OzJungleWeather.I ? OzJungleWeather.I.Rain : 0f;

        /// <summary>Night or rain on level 16: the birds and monkeys are away (once a frame, with a margin each way).</summary>
        public static bool Hushed
        {
            get
            {
                if (frame == Time.frameCount) return hushed;
                frame = Time.frameCount;
                if (!ZGame.I || ZGame.I.level != "L16") { hushed = false; return false; }
                float n = NightNow, r = RainNow;
                bool want = hushed ? (n > NightOff || r > RainOff) : (n > NightOn || r > RainOn);
                if (want != hushed) { hushed = want; if (want) HushStarts++; else HushEnds++; OzSoundLog.Add("L16", want ? "life_hush_start" : "life_hush_end", null, want ? "birds and monkeys away (night " + n.ToString("F2") + ", rain " + r.ToString("F2") + ")" : "birds and monkeys back", 0f); }
                return hushed;
            }
        }

        /// <summary>For AudioDirector's bird calls of the level-16 bed.</summary>
        public static bool BirdsAway => Hushed;

        /// <summary>For AudioDirector's level-16 day bed: its share of the day weight (1 dry, DayBedRainShare under full rain).</summary>
        public static float DayBedShare => Mathf.Lerp(1f, DayBedRainShare, Mathf.Clamp01(RainNow));

        public static string Status() => $"[OzJungleLife] hushed {Hushed} (night {NightNow:F2}, rain {RainNow:F2}); life level {LifeDb:+0;-0} dB; day bed share {DayBedShare:F2}; went quiet {HushStarts}x, came back {HushEnds}x";
    }
}
