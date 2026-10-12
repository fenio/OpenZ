// OpenZ (GPL-3.0)
//  - Alan already speaks inside the approved clips ("Alan:" cues of zod_lines.json, through the game's Alan voice chain):
//      zod_brief_L02 / L17  "What's our plan of attack?"                       -> FrontKind.Plan, then Zod's "Attack!"
//      zod_win_02           "But sir... what about the celebration rocket fuel?" -> FrontKind.Celebrate (cans), then "Okay, one can..."
//    So no new voice is needed and the timing is the line's own: the cut comes alanCutLead before his cue, the cut back alanCutTail
//    after it (never later than alanCutBeforeZod before Zod's next words).
//  - Lines whose beats have no "alan" fx on that cue (the two briefings) get one here (AddAlanCueFx); the "alan" fx of zod_win_02
//    (t 3.1, hold 3.43) plays the front shot instead of the over-the-shoulder cut. The shy Alan of zod_lose_push (value -1) keeps the
//    back cut (he hangs his head, seen from behind). Without the Alan / Brad prefabs everything stays as before.
//  - The first victory of a session plays zod_win_02 (alanOutroOnFirstWin), so the celebration shot is seen; afterwards the victory
//    line is random again (never twice in a row).
//  - Play checks (from a Play-mode script, or the OpenZ > Zod Boys menu):
//      return OpenZ.ZodBriefing.DebugFront();                 the front shot state
//      return OpenZ.ZodBriefing.DebugFrontHold(0.5f);         hold the push at 0..1 (shot start / middle / end), -1 = release
//      return OpenZ.ZodBriefing.DebugFrontFrames(@"<dir>");   saves the feed at the start, middle and end of every front shot (null = off)
using System.Collections;
using System.IO;
using UnityEngine;

namespace OpenZ
{
    public sealed partial class ZodBriefing
    {
        [Header("Alan + Brad front shots")]
        [Tooltip("Alan's lines cut to Alan and Brad standing (needs the Alan / Brad prefabs)")] public bool alanFrontShots = true;
        [Tooltip("The cut comes this long before Alan's first word (s)")] public float alanCutLead = 0.12f;
        [Tooltip("The cut back comes this long after his cue ends (s)")] public float alanCutTail = 0.10f;
        [Tooltip("... but at least this long before Zod's next words (s)")] public float alanCutBeforeZod = 0.18f;
        [Tooltip("The first victory of a session plays zod_win_02 (Alan asks about the celebration)")] public bool alanOutroOnFirstWin = true;
        public const string AlanOutroId = "zod_win_02";

        static bool alanOutroPlayed; static string frontFramesDir;
        public static int FrontCuts { get; private set; }
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetAlanBrad() { alanOutroPlayed = false; FrontCuts = 0; frontFramesDir = null; }

        static bool IsAlanCue(ZodCue c) => c != null && c.text != null && c.text.StartsWith("Alan:");

        /// <summary>Alan's cue at or after clip time t (null = none left).</summary>
        static ZodCue AlanCueFrom(ZodLine line, float t)
        {
            if (line?.cues == null) return null;
            foreach (var c in line.cues) if (IsAlanCue(c) && t <= c.end) return c;
            return null;
        }

        /// <summary>StartPerf: an "alan" fx on every Alan cue the beats do not cut on yet (the briefings).</summary>
        void AddAlanCueFx(Perf p)
        {
            if (!alanFrontShots || p?.line?.cues == null || !stage || !stage.FrontReady || !BoysFor(p.kind, p.line)) return;
            var cues = p.line.cues; bool added = false;
            for (int i = 0; i < cues.Length; i++)
            {
                var c = cues[i];
                if (!IsAlanCue(c)) continue;
                bool has = false;
                foreach (var x in p.fx) if (x.f != null && x.f.fx == "alan" && Mathf.Abs(x.t - c.start) < 0.8f) { has = true; break; }
                if (has) continue;
                float t0 = c.start - alanCutLead, t1 = c.end + alanCutTail;
                if (i + 1 < cues.Length) t1 = Mathf.Min(t1, cues[i + 1].start - alanCutBeforeZod);
                p.fx.Add((t0, new ZodFx { fx = "alan", value = 1f, hold = Mathf.Max(0.6f, t1 - t0), t = t0 }));
                added = true;
            }
            if (added) p.fx.Sort((a, b) => a.t.CompareTo(b.t));
        }

        /// <summary>DoFx "alan" (value >= 0): the front shot on Alan's cue. False = not possible (the over-the-shoulder cut runs).</summary>
        bool AlanFront(ZodFx f)
        {
            if (!alanFrontShots || f == null || f.value < 0f || perf == null || !stage || !stage.FrontReady || !BoysFor(perf.kind, perf.line)) return false;
            var c = AlanCueFrom(perf.line, perf.t - 0.05f);
            if (c == null) return false;
            bool party = perf.kind == Kind.Victory || perf.line.group == "victory";
            var kind = party ? ZodStage.FrontKind.Celebrate : ZodStage.FrontKind.Plan;
            float hold = f.hold > 0f ? f.hold : Mathf.Max(0.6f, c.end + alanCutTail - perf.t);
            if (!stage.FrontShot(kind, hold, voice, c.start, c.end, perf.t)) return false;
            comms.Glitch(0.3f, 0.07f);                                                                    // the signal hiccups on the cut, as the back cut did
            AlanCuts++; FrontCuts++;
            if (party) alanOutroPlayed = true;
            Debug.Log($"[ZodCameo] {perf.id}: cut to Alan + Brad ({kind}, {hold:F2} s, Alan's cue {c.start:F2}-{c.end:F2})");
            if (frontFramesDir != null) StartCoroutine(FrontFrames(perf.id));
            return true;
        }

        /// <summary>LevelRules: the victory line (the first win of the session = Alan's celebration question).</summary>
        string PickVictory()
        {
            if (alanOutroOnFirstWin && alanFrontShots && !alanOutroPlayed && boysEndScreen && stage && stage.FrontReady
                && lib.Line(AlanOutroId) != null && lib.Clip(AlanOutroId) && !(TeamColors.Swap && TeamColors.NamesColour(lib.Line(AlanOutroId).text)))
            {
                alanOutroPlayed = true;
                PlayerPrefs.SetString("oz_zod_last_victory", AlanOutroId);                                // the random pick after it still never repeats it
                Debug.Log("[ZodCameo] first victory of the session: " + AlanOutroId + " (Alan + Brad celebration shot)");
                return AlanOutroId;
            }
            return lib.Pick("victory");
        }

        IEnumerator FrontFrames(string id)
        {
            string dir = frontFramesDir; int n = FrontCuts;
            foreach (var (u, tag) in new[] { (0.04f, "start"), (0.5f, "middle"), (0.96f, "end") })
            {
                while (stage && stage.FrontOn && stage.FrontU < u) yield return null;
                if (!stage || !stage.FrontOn) yield break;
                yield return new WaitForEndOfFrame();
                Debug.Log("[ZodCameo] front frame: " + DebugSaveFeed(Path.Combine(dir, $"{id}_front{n}_{tag}.png")));
            }
        }

        // ------------------------------------------------------------------------------------------------ Play checks
        /// <summary>Play check: the Alan / Brad state (built, the front shot, the line clock).</summary>
        public static string DebugFront()
        {
            if (!I || !I.stage) return "no ZodBriefing / stage";
            return I.stage.FrontStatus() + $" | front cuts {FrontCuts}, outro played {alanOutroPlayed}" + (I.perf != null ? $" | line {I.perf.id} t={I.perf.t:F2}" : " | no line playing");
        }

        /// <summary>Play check: hold the front shot's push at u (0 = the cut, 1 = the end); negative releases it.</summary>
        public static string DebugFrontHold(float u)
        {
            if (!I || !I.stage) return "no stage";
            I.stage.FrontProofU = u;
            return "front hold " + u.ToString("F2") + " | " + I.stage.FrontStatus();
        }

        /// <summary>Play check: save the feed at the start / middle / end of every front shot into dir (null = off).</summary>
        public static string DebugFrontFrames(string dir)
        {
            frontFramesDir = string.IsNullOrEmpty(dir) ? null : dir;
            if (frontFramesDir != null) Directory.CreateDirectory(frontFramesDir);
            return "front frames " + (frontFramesDir ?? "off");
        }
    }
}
