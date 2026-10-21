// OpenZ (GPL-3.0) - the briefing flyover check's line for the two robots' own script: nothing of the chase but the clacks and the water
// sounds under a word. Every chase sound of the stage in the sound log is put on the voice clock (sampled on every frame of the flight)
// with its clip's length; a roar, a growl or the fleeing robot's alarm that starts or still sounds within 0.05 s of a word of the line
// fails the line. Clacks (the crocodile's own jaw event) and the water (splash, wake, swell, strokes, churn) may lie under words.
// WRITTEN BY READING ONLY: compiled off-line with the project's code, never run in the engine.
using System.Collections.Generic;
using System.Text;
using UnityEngine;

namespace OpenZ.EditorTools
{
    public static partial class OzL16BriefingFlyoverCheck
    {
        /// <summary>The chase's sounds by kind: clacks and water may lie under a word, the others may not.</summary>
        public static readonly string[] ChaseClacks = { "critter_croc_jaw_snap", "brief_l16_croc_snap" },
            ChaseWater = { "brief_l16_croc_splash", "brief_l16_croc_wake", "brief_l16_croc_surface", "brief_l16_robot_stroke", "brief_l16_water_churn" },
            ChaseOther = { "brief_l16_croc_roar", "brief_l16_croc_growl", "brief_l16_robot_alarm" };
        static readonly List<Vector2> voiceClock = new List<Vector2>();                                 // (unscaled time, the voice clock) per frame

        static void ResetVoiceClock() => voiceClock.Clear();

        static void SampleVoiceClock()
        {
            var s = OzL16LoungerSet.I;
            if (s && s.BriefingOn && s.BriefClock > -50f) voiceClock.Add(new Vector2(Time.unscaledTime, s.BriefClock));
        }

        /// <summary>The voice clock at an unscaled time (NaN = not on the clock).</summary>
        static float VoiceAt(float t)
        {
            for (int i = voiceClock.Count - 1; i >= 0; i--)
                if (voiceClock[i].x <= t + 1e-4f) return t - voiceClock[i].x <= 0.5f ? voiceClock[i].y + (t - voiceClock[i].x) : float.NaN;
            return float.NaN;
        }

        static void WordsLine(List<OzSoundLog.Entry> log, ZodLine line)
        {
            if (line == null || line.words == null || line.words.Length == 0 || voiceClock.Count == 0)
            {
                NotRun("nothing but the clacks and water under a word: the line has no word times or the two robots did not hold the briefing");
                return;
            }
            int other = 0, otherBad = 0, skipped = 0, clacks = 0, clacksUnder = 0, water = 0, waterUnder = 0; var bad = new StringBuilder(); var gaps = new StringBuilder();
            foreach (var e in log)
            {
                if (e.src != "L16B" || e.outcome == null) continue;
                bool isOther = System.Array.IndexOf(ChaseOther, e.key) >= 0, isClack = System.Array.IndexOf(ChaseClacks, e.key) >= 0, isWater = System.Array.IndexOf(ChaseWater, e.key) >= 0;
                if (!isOther && !isClack && !isWater) continue;
                if (e.outcome.StartsWith("SKIP")) { if (isOther) skipped++; continue; }
                if (!(e.outcome.StartsWith("PLAY") || e.outcome.StartsWith("LOOP"))) continue;
                float at = VoiceAt(e.t); if (float.IsNaN(at)) continue;
                float len = e.outcome.StartsWith("LOOP") ? 0.5f : Mathf.Max(0.05f, OzL16BriefingStage.ClipSeconds(e.clip));
                bool under = OzL16BriefingStage.UnderWord(at, len, out string word);
                if (isOther) { other++; if (under) { otherBad++; if (bad.Length < 260) bad.Append($" {e.key} ({e.clip}) {at:F2}-{at + len:F2} s on '{word}';"); } }
                else if (isClack) { clacks++; if (under) clacksUnder++; else if (gaps.Length < 120) gaps.Append($" {at:F2}"); }
                else { water++; if (under) waterUnder++; }
            }
            Check(otherBad == 0,
                $"nothing but the clacks and water under a word (items 117, 130): {otherBad} of {other} roars / growls / alarms played start or sound on a word{bad}; "
                + $"{skipped} left out (the far clack, or under a word); clacks {clacks} ({clacks - clacksUnder} in a pause, at{gaps}), water {water} ({waterUnder} under words, allowed)");
        }
    }
}
