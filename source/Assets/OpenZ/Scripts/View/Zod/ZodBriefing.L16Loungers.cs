// OpenZ (GPL-3.0) - Level 16: the two robots hold the mission-start briefing themselves, on their sun loungers at the swamp.
// A line of zod_lines.json with "host": "boys" on a level that has a lounger set (OzL16LoungerSet, level L16 only) is held by them:
//  - StartPerf calls BoysHostBegin instead of the opening on their backs in the commander's HQ and instead of the cut to the two standing:
//    the window shows the lounger set through its own camera for the whole line (ZodStage.LoungerFeed), the plate under the window and the
//    speaker label read ALAN + BRAD (ZodComms.Host), each robot's jaw moves inside his own subtitle cues only ("Alan: ..." / "Brad: ...":
//    as many cues per speaker as the line has), and the set plays the cue sheet's beats on the line's clock.
//  - Every other line, and every other level, runs exactly as before: BoysHost answers false.
// The name-plate joke is not played on this briefing: that rule belongs to the level's shot list (ZodShotList.QuietOpening).
// WRITTEN BY READING ONLY: compiled off-line with the project's code, never run in the engine. Remove this line once it runs.
using System.Collections.Generic;
using UnityEngine;

namespace OpenZ
{
    public sealed partial class ZodBriefing
    {
        public const string BoysHostName = "ALAN + BRAD";
        /// <summary>Play check: how many lines the two robots have held on their loungers in this session.</summary>
        public static int BoysHostLines { get; private set; }

        static bool IsBradCue(ZodCue c) => c != null && c.text != null && c.text.StartsWith("Brad:");

        /// <summary>The two robots hold this line: a level-start briefing whose line says so, on a level with a lounger set that is ready.</summary>
        bool BoysHost(Kind kind, ZodLine line)
        {
            ZodComms.Host = null;                                                                       // every line starts with the commander's plate; BoysHostBegin sets the other name
            if (kind != Kind.Briefing || mode != Mode.Level || line == null || line.host != "boys" || !game) return false;
            var set = OzL16LoungerSet.For(game.level);
            return set && set.Ready;
        }

        /// <summary>StartPerf, in place of StartBoys + AddAlanCueFx. False = the set could not start (the caller opens as on every level).</summary>
        bool BoysHostBegin(Perf p)
        {
            var set = OzL16LoungerSet.For(game.level);
            if (!set || p?.line == null) return false;
            var a = new List<Vector2>(); var b = new List<Vector2>();
            if (p.line.cues != null)
                foreach (var c in p.line.cues)
                {
                    if (IsAlanCue(c)) a.Add(new Vector2(c.start, c.end));
                    else if (IsBradCue(c)) b.Add(new Vector2(c.start, c.end));
                }
            // the line's clock: seconds on the voice clip (negative before it); +infinity while the window closes; NaN when the line is gone
            if (!set.BriefBegin(stage, voice, a, b, () => perf == p ? (p.done ? float.PositiveInfinity : p.t) : float.NaN, VoiceGain())) return false;
            ZodComms.Host = BoysHostName; comms?.ApplyRank();
            stage.GazeBoys = false;
            BoysHostLines++;
            Debug.Log($"[ZodCameo] {p.id}: held by the two robots on their loungers ({a.Count} cues of the smaller one, {b.Count} of the bigger one), plate {BoysHostName}");
            return true;
        }

        /// <summary>The lounger set: its briefing is over, the plate is the commander's again.</summary>
        internal static void BoysHostEnd()
        {
            ZodComms.Host = null;
            if (I && I.comms != null) I.comms.ApplyRank();
        }

        /// <summary>The window's signal meter also follows the two robots' jaws on their loungers.</summary>
        static float LoungerMouth01 => OzL16LoungerSet.I ? OzL16LoungerSet.I.Mouth01 : 0f;
    }
}
