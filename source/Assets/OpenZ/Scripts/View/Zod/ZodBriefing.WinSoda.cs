// OpenZ (GPL-3.0) - K439: the win line's rocket fuel can and the boys' giggles. The line zod_win_02 ("Now, boys, this is no time to
// celebrate! (Alan: But sir... what about the celebration rocket fuel?) Okay, one can of rocket fuel can't hurt... and then back to
// action!") gets two things in the background of the transmission:
//  - a can of rocket fuel opens (a short "pss") in his pause right after "can't hurt...", before "and then back to action!";
//  - after his last word Alan and Brad giggle at his joke, short and shy: Alan, then Brad, then Alan once more, a little overlapping.
// The moments come from the line's own sheet (zod_lines.json): the can on the end of the word that starts with WinSodaCanWord, the
// giggles after the line's last cue, so a re-timed sheet moves them along. Each sound has its own 2D source at Zod's voice gain (the
// VOICE slider) plus its own level: the can about 10 dB and the giggles 10-12 dB under his words, never over a word. The window stays
// open until the last giggle has ended (+ WinSodaHoldAfter), the other voices (radio, Rachel, Alan) keep waiting until then
// (ZodSpeaking reads SpeakingUntil) and his duck on the music / ambience / combat mix holds while they giggle. A sound whose moment
// passed more than WinSodaLateLimit ago (a hitch while his voice plays) is skipped, never played late (the can would land on his next
// words). Every sound goes into the sound log (source ZodCameo). Off: WinSodaLaughsOn = false (the line exactly as before).
// No subtitle: the transmission shows his words only. View only: nothing here touches the rules.
using System;
using System.Collections.Generic;
using UnityEngine;

namespace OpenZ
{
    public sealed partial class ZodBriefing
    {
        /// <summary>K439: the can and the giggles on the win line (on). Off: the line plays exactly as before.</summary>
        public static bool WinSodaLaughsOn = true;
        public const string WinSodaLineId = "zod_win_02";
        /// <summary>The can opens WinSodaCanAfterWord s after the end of the first word that starts with this (on the line's clock).</summary>
        public static string WinSodaCanWord = "hurt";
        public static float WinSodaCanAfterWord = 0.00f, WinSodaCanDb = -6.0f;
        /// <summary>The giggles start WinSodaGiggleAfterLine s after the line's last cue ends; each at its own offset from there (s) and
        /// level (dB relative to Zod's voice gain).</summary>
        public static float WinSodaGiggleAfterLine = 0.02f;
        public static float WinSodaAlanAAt = 0.00f, WinSodaBradAt = 0.34f, WinSodaAlanBAt = 0.80f;
        public static float WinSodaAlanADb = -12.0f, WinSodaBradDb = -12.0f, WinSodaAlanBDb = -14.0f;
        /// <summary>The window closes this long after the last giggle has ended (s).</summary>
        public static float WinSodaHoldAfter = 0.10f;
        /// <summary>A sound whose moment passed more than this long ago (s) is skipped, never played late.</summary>
        public static float WinSodaLateLimit = 0.20f;

        public const string WinSodaCanRes = "OpenZ/Zod/Sfx/zod_win_02_can_open";
        public const string WinSodaAlanARes = "OpenZ/Zod/Voice/zod_win_02_giggle_alan_a", WinSodaBradRes = "OpenZ/Zod/Voice/zod_win_02_giggle_brad",
                            WinSodaAlanBRes = "OpenZ/Zod/Voice/zod_win_02_giggle_alan_b";

        /// <summary>One sound of the plan: its key (can, alan_a, brad, alan_b), resource, moment on the line's clock (s) and level (dB).</summary>
        public struct WinSodaStep { public string key, res; public float at, db; public bool giggle; }

        sealed class WinSodaCue { public WinSodaStep step; public AudioClip clip; public AudioSource src; public bool fired, skipped; public float firedAt = -99f; }

        Perf winSodaPerf; float winSodaVoiceEnd;
        readonly List<WinSodaCue> winSodaCues = new List<WinSodaCue>();
        readonly Dictionary<string, AudioSource> winSodaSources = new Dictionary<string, AudioSource>();
        static readonly Dictionary<string, AudioClip> winSodaClips = new Dictionary<string, AudioClip>();

        /// <summary>Checks: sounds played / skipped since the game started, and the last line's plan as text.</summary>
        public static int WinSodaPlayed { get; private set; }
        public static int WinSodaSkipped { get; private set; }
        public static string WinSodaLastPlan { get; private set; } = "none yet";
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetWinSoda() { WinSodaPlayed = 0; WinSodaSkipped = 0; WinSodaLastPlan = "none yet"; winSodaClips.Clear(); }

        /// <summary>The plan for a line (null + why when the line has no sheet or no word to hang the can on). Used by the game and the checks.</summary>
        public static List<WinSodaStep> WinSodaPlan(ZodLine line, out string why)
        {
            why = null;
            if (line == null || line.words == null || line.cues == null || line.cues.Length == 0) { why = "the line has no word sheet"; return null; }
            ZodWord w = null;
            foreach (var x in line.words) if (x != null && x.word != null && x.word.StartsWith(WinSodaCanWord, StringComparison.OrdinalIgnoreCase)) { w = x; break; }
            if (w == null) { why = "no word '" + WinSodaCanWord + "' in the line"; return null; }
            float lastCue = 0f;
            foreach (var c in line.cues) if (c != null) lastCue = Mathf.Max(lastCue, c.end);
            float t0 = lastCue + WinSodaGiggleAfterLine;
            return new List<WinSodaStep>
            {
                new WinSodaStep { key = "can", res = WinSodaCanRes, at = w.end + WinSodaCanAfterWord, db = WinSodaCanDb },
                new WinSodaStep { key = "alan_a", res = WinSodaAlanARes, at = t0 + WinSodaAlanAAt, db = WinSodaAlanADb, giggle = true },
                new WinSodaStep { key = "brad", res = WinSodaBradRes, at = t0 + WinSodaBradAt, db = WinSodaBradDb, giggle = true },
                new WinSodaStep { key = "alan_b", res = WinSodaAlanBRes, at = t0 + WinSodaAlanBAt, db = WinSodaAlanBDb, giggle = true },
            };
        }

        public static AudioClip WinSodaClip(string res)
        {
            if (winSodaClips.TryGetValue(res, out var c) && c) return c;
            c = Resources.Load<AudioClip>(res);
            if (c && c.loadState == AudioDataLoadState.Unloaded) c.LoadAudioData();
            winSodaClips[res] = c;
            return c;
        }

        /// <summary>The line's voice counts as going on until this moment (s on its clock): its own end, or the last giggle's end on the win line.</summary>
        float SpeakingUntil(Perf p) => p != null && p == winSodaPerf ? Mathf.Max(p.voiceEnd, winSodaVoiceEnd) : p != null ? p.voiceEnd : 0f;

        AudioSource WinSodaSource(string key, bool voiceLike)
        {
            if (winSodaSources.TryGetValue(key, out var s) && s) return s;
            s = gameObject.AddComponent<AudioSource>();
            s.playOnAwake = false; s.loop = false; s.spatialBlend = 0f; s.dopplerLevel = 0f; s.bypassReverbZones = true; s.ignoreListenerPause = false;
            s.priority = voiceLike ? 20 : 40; s.pitch = 1f;
            if (voiceLike) AudioDirector.RegisterExternalVoice(s);                                      // the radio never talks over a giggle
            winSodaSources[key] = s;
            return s;
        }

        void WinSodaStopAll() { foreach (var kv in winSodaSources) if (kv.Value && kv.Value.isPlaying) kv.Value.Stop(); }

        /// <summary>StartPerf hook (right after the line's end is set): the plan for the win line, the window held open for the giggles.</summary>
        void WinSodaBegin(Perf p)
        {
            WinSodaStopAll();
            winSodaPerf = null; winSodaVoiceEnd = 0f; winSodaCues.Clear();
            if (!WinSodaLaughsOn || p == null || p.id != WinSodaLineId) return;
            var plan = WinSodaPlan(p.line, out string why);
            if (plan == null) { Debug.LogWarning("[ZodCameo] " + p.id + ": no can and giggles (" + why + ")"); return; }
            var sb = new System.Text.StringBuilder();
            foreach (var st in plan)
            {
                var clip = WinSodaClip(st.res);
                if (!clip)
                {
                    OzSoundLog.Add("ZodCameo", "zod_win_" + st.key, null, "DROP clip missing (" + st.res + ")");
                    Debug.LogWarning("[ZodCameo] " + p.id + ": the clip " + st.res + " is missing (that sound is left out)");
                    continue;
                }
                winSodaCues.Add(new WinSodaCue { step = st, clip = clip, src = WinSodaSource(st.key, st.giggle) });
                if (st.giggle) winSodaVoiceEnd = Mathf.Max(winSodaVoiceEnd, st.at + clip.length);
                sb.Append(sb.Length > 0 ? ", " : "").Append(st.key).Append(' ').Append(st.at.ToString("0.00")).Append(" s ").Append(st.db.ToString("0.0")).Append(" dB");
            }
            if (winSodaCues.Count == 0) return;
            float before = p.end;
            if (winSodaVoiceEnd > 0f) p.end = Mathf.Max(p.end, winSodaVoiceEnd + WinSodaHoldAfter);
            winSodaPerf = p;
            WinSodaLastPlan = sb + $"; window end {before:0.00} -> {p.end:0.00} s";
            Debug.Log("[ZodCameo] " + p.id + ": can and giggles planned (" + WinSodaLastPlan + ")");
        }

        /// <summary>TickPerf hook (unpaused frames, after the line's beats): starts each sound on its moment, keeps the levels on the voice
        /// gain and the line's fade.</summary>
        void WinSodaTick(Perf p)
        {
            if (p == null || winSodaCues.Count == 0) return;
            if (p != winSodaPerf) { WinSodaStopAll(); winSodaCues.Clear(); return; }
            float gain = VoiceGain() * p.fade;
            bool live = WinSodaLaughsOn && p.voiceOn && p.fadeSpeed <= 0f && !p.closing;
            foreach (var c in winSodaCues)
            {
                if (c.src && c.fired) c.src.volume = Mathf.Clamp01(gain * Mathf.Pow(10f, c.step.db / 20f));
                if (c.fired || c.skipped || p.t < c.step.at) continue;
                float late = p.t - c.step.at;
                if (!live || late > WinSodaLateLimit)
                {
                    c.skipped = true; WinSodaSkipped++;
                    OzSoundLog.Add("ZodCameo", "zod_win_" + c.step.key, c.clip.name, live ? $"SKIP late ({late:0.00} s after its moment)" : "SKIP the line is closing or fading");
                    continue;
                }
                c.src.Stop(); c.src.clip = c.clip; c.src.volume = Mathf.Clamp01(gain * Mathf.Pow(10f, c.step.db / 20f)); c.src.Play();
                c.fired = true; c.firedAt = p.t; WinSodaPlayed++;
                OzSoundLog.Add("ZodCameo", "zod_win_" + c.step.key, c.clip.name, $"PLAY 2D transmission background ({c.step.db:0.0} dB to his voice, at {p.t:0.00} s for {c.step.at:0.00} s)", c.src.volume);
            }
            if (p.fade <= 0f) { WinSodaStopAll(); return; }
            // the transmission's duck holds while the boys giggle after his last word, as it does under his words (TickPerf refreshes it
            // only while his voice plays)
            bool giggling = false;
            foreach (var c in winSodaCues) if (c.step.giggle && c.src && c.src.isPlaying) { giggling = true; break; }
            if (giggling && !voice.isPlaying && Time.unscaledTime >= nextDuck)
            {
                nextDuck = Time.unscaledTime + 0.3f;
                if (AudioDirector.I) AudioDirector.I.AddDuck(AudioDirector.ZodMusicDuckOwned ? 0f : duckMusicDb, 0f, duckAmbienceDb, duckAlanBusDb, 0.55f, 0.2f, 1.0f);
                if (CombatAudio.I) CombatAudio.I.DuckFor(0.55f, -6f, -2f);
            }
        }

        // ------------------------------------------------------------------------------------------------ Play checks
        /// <summary>Play check: the win line's sounds: each one's moment, when it started on the line's clock, whether it plays, its volume
        /// against the voice gain; and the window's end. Empty plan: "no can and giggles on this line".</summary>
        public static string WinSodaStatus()
        {
            if (!I) return "no ZodBriefing";
            if (I.winSodaCues.Count == 0) return "no can and giggles on this line (plan: " + WinSodaLastPlan + ")";
            var sb = new System.Text.StringBuilder();
            float vg = Mathf.Max(1e-4f, I.VoiceGain());
            foreach (var c in I.winSodaCues)
                sb.Append($"{c.step.key} at {c.step.at:0.00} fired {(c.fired ? c.firedAt.ToString("0.00") : c.skipped ? "SKIPPED" : "-")} playing {(c.src && c.src.isPlaying ? 1 : 0)} " +
                          $"vol/voice {(c.src ? 20f * Mathf.Log10(Mathf.Max(1e-6f, c.src.volume) / vg) : -99f):0.0} dB | ");
            if (I.perf != null) sb.Append($"line {I.perf.id} t={I.perf.t:0.00} end {I.perf.end:0.00} speaking until {I.SpeakingUntil(I.perf):0.00}");
            return sb.ToString();
        }

        /// <summary>Play check: the voice gain the sounds are measured against, and the last giggle's end of the line playing (s).</summary>
        public static float WinSodaVoiceGain => I ? I.VoiceGain() : 0f;
        public static float WinSodaGigglesEnd => I && I.perf != null && I.perf == I.winSodaPerf ? I.winSodaVoiceEnd : -1f;
        public static float WinSodaLineEnd => I && I.perf != null ? I.perf.end : -1f;
        /// <summary>Play check: (key, moment, started at, skipped, playing, volume relative to the voice gain in dB) of each planned sound.</summary>
        public static List<(string key, float at, float firedAt, bool skipped, bool playing, float relDb)> WinSodaCues()
        {
            var l = new List<(string, float, float, bool, bool, float)>();
            if (!I) return l;
            float vg = Mathf.Max(1e-4f, I.VoiceGain());
            foreach (var c in I.winSodaCues)
                l.Add((c.step.key, c.step.at, c.fired ? c.firedAt : -99f, c.skipped, c.src && c.src.isPlaying, c.src && c.fired ? 20f * Mathf.Log10(Mathf.Max(1e-6f, c.src.volume) / vg) : -99f));
            return l;
        }
    }
}
