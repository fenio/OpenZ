// OpenZ (GPL-3.0) - the announcer line at the start of the level briefing.
// The start-of-mission briefing opens with a stretch in which nobody talks: the window is up, the glove corrects the name plate
// (ZodBriefing.GeneralJoke.cs), and only then Zod clears his throat and speaks. One announcer line fills that stretch. It starts
// before his next sound when the half second would leave it no room, on its own 2D source at the announcer's level (bus Voice/Announcer: master + the ANNOUNCER
// volume setting, like the queued announcer lines), and its start and end are written to the sound log.
// Nothing on the briefing clock moves for it: it plays only when it is over before his next sound is due (no joke = no quiet
// stretch = no line), it fades with the transmission when the briefing is skipped, and it never plays under his own voice.
// Play check: return OpenZ.ZodBriefing.DebugStartLine();
using UnityEngine;

namespace OpenZ
{
    public sealed partial class ZodBriefing
    {
        public const string StartLineId = "ann_briefing_start", StartLineRes = "OpenZ/Audio/Voice/announcer_briefing_start_1", StartLineBus = "Voice/Announcer";
        /// <summary></summary>
        public const float StartLineDelay = 0.5f;
        /// <summary>The window clock's longest step (a long first frame counts as this much): the line starts once one more step could leave it no room.</summary>
        const float StartLineStep = 0.1f;
        AudioSource startLine; AudioClip startLineClip; Perf startLineFor; bool startLineOn, startLinePending; float startLineAt;
        static string lastStartLine = "no briefing start line yet";

        /// <summary>The announcer's linear gain (the director's bus table carries the ANNOUNCER volume setting; -6 dB is that bus's own level).</summary>
        static float AnnouncerGain() =>
            Mathf.Clamp01(AudioDirector.I ? AudioDirector.I.VoiceBusLinear(StartLineBus) : PlayerPrefs.GetFloat("oz_vol_voice", 1f) * Mathf.Pow(10f, -6f / 20f));

        /// <summary>Per frame from TickPerf: starts the line with a new level briefing, then follows the volume setting and the skip
        /// fade until the line is over.</summary>
        void StartLineTick(Perf p)
        {
            if (p.kind == Kind.Briefing && startLineFor != p) { startLineFor = p; startLinePending = true; }
            if (startLinePending && p == startLineFor && (p.t + p.preroll >= StartLineDelay || StartLineSlack(p) <= StartLineStep))
            {
                startLinePending = false;
                if (!p.done && !p.closing && !p.voiceOn && p.fadeSpeed <= 0f) StartLineBegin(p);                      // skipped or his voice inside the delay: no line
            }
            if (!startLineOn) return;
            bool cut = p != startLineFor || p.done || p.closing || p.voiceOn || (joke != null && joke.ahemOn);   // skipped, over, or his own voice: never under him
            bool ended = !startLine || (!startLine.isPlaying && startLine.timeSamples == 0);                     // a paused source keeps its position
            if (!cut && !ended) { startLine.volume = AnnouncerGain() * p.fade; return; }
            StartLineStop(ended ? "END" : "STOP");
        }

        void StartLineBegin(Perf p)
        {
            if (StartLineSkipped(p)) return;                                                            // the two robots' own opening: no start line (ZodBriefing.L16Owner.cs)
            var clip = Resources.Load<AudioClip>(StartLineRes);
            if (!clip) { Debug.LogWarning("[ZodCameo] briefing start line: Resources/" + StartLineRes + " is missing"); return; }
            // the quiet stretch ahead: up to his "Ahem." while the joke runs, else up to his first word
            float room = StartLineRoom(p);
            if (clip.length > room)
            {
                lastStartLine = $"not played: {clip.length:F2} s does not fit into the {Mathf.Max(0f, room):F2} s before his next sound";
                Debug.Log("[ZodCameo] briefing start line " + lastStartLine);
                return;
            }
            if (!startLine) { startLine = NewStaticSource(); startLine.priority = 8; AudioDirector.RegisterExternalVoice(startLine); }
            startLine.Stop(); startLine.clip = clip; startLine.pitch = 1f; startLine.volume = AnnouncerGain() * p.fade; startLine.Play();
            startLineOn = true; startLineAt = Time.realtimeSinceStartup;                                    // real time: the level's first frame can be seconds long
            OzSoundLog.Add("VO", StartLineId, clip.name, "PLAY " + StartLineBus + " briefing start", startLine.volume);
            lastStartLine = $"{clip.name} ({clip.length:F2} s) started at {Time.unscaledTime:F2} s, {p.t + p.preroll:F2} s after the window opened, gain {startLine.volume:F3}; his next sound was due {room:F2} s later";
            Debug.Log("[ZodCameo] briefing start line: " + lastStartLine);
        }

        /// <summary>The quiet stretch ahead: up to his "Ahem." while the joke runs, else up to his first word.</summary>
        float StartLineRoom(Perf p) => joke != null && !joke.finished && !joke.ahemOn ? joke.ahemAt - (p.t - joke.start) : -p.t;

        /// <summary>Seconds the line could still wait and fit before his next sound (large when the clip is missing: the delay rules).</summary>
        float StartLineSlack(Perf p)
        {
            if (!startLineClip) startLineClip = Resources.Load<AudioClip>(StartLineRes);
            return startLineClip ? StartLineRoom(p) - startLineClip.length : float.MaxValue;
        }

        /// <summary>Stops the line if it still plays and writes its end to the sound log ("END" = played out, "STOP" = cut).</summary>
        void StartLineStop(string outcome)
        {
            if (!startLineOn) return;
            startLineOn = false;
            if (!startLine) return;
            if (startLine.isPlaying) startLine.Stop();
            float after = Time.realtimeSinceStartup - startLineAt;
            OzSoundLog.Add("VO", StartLineId, startLine.clip ? startLine.clip.name : null, outcome + " after " + after.ToString("F2") + " s");
            lastStartLine += $" | {outcome} after {after:F2} s";
        }

        /// <summary>Play check: what the last briefing start line did (start time, gain, how it ended).</summary>
        public static string DebugStartLine() => lastStartLine;
    }
}
