using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEngine;

namespace OpenZ
{
    /// <summary>
    /// K439 Play proof (menu OpenZ/V1.1/Zod: K439 Win Can + Giggles Proof, Editor/OzWinSodaMenu.cs), on any level in Play: the win line
    /// zod_win_02 played twice as the game's victory transmission (ZodBriefing.DebugLineAs, Kind.Victory), what the player hears recorded
    /// from the listener into a WAV. Counted PASS / FAIL lines:
    ///  P1 the line plays as a victory; four sounds planned (the can, Alan, Brad, Alan);
    ///  P2 the can starts on its moment (the end of "hurt...", within 0.06 s);
    ///  P3 every giggle starts on its moment (within 0.06 s), the first after his last cue;
    ///  P4 every sound plays at its level against his voice gain (within 0.3 dB);
    ///  P5 the commander counts as speaking (the radio, Rachel and Alan wait) until the last giggle ends, and stops within 0.4 s after;
    ///  P6 the window closes after the last giggle + the hold, later than before;
    ///  P7 the sound log: four PLAY lines (zod_win_can, zod_win_alan_a, zod_win_brad, zod_win_alan_b), no SKIP;
    ///  P8 the switch off (one more run): no sound, the window and the speaking end as before (the switch is put back after).
    /// Output: <see cref="Folder"/> (the work tree's records, milestones/zod_win_soda_k439, found upward at run time): win_soda_proof_log.txt,
    /// k439_win_line_on.wav and k439_win_line_off.wav (the listener's output, 16-bit). Test only.
    /// </summary>
    public sealed class OzWinSodaProof : MonoBehaviour
    {
        public static string Report = "idle";
        public static string Folder()
        {
            var d = new DirectoryInfo(Application.dataPath);
            for (int i = 0; i < 8 && d != null; i++, d = d.Parent)
                if (Directory.Exists(Path.Combine(d.FullName, "work", "records")))
                    return Path.Combine(d.FullName, "work", "records", "milestones", "zod_win_soda_k439");
            return Path.Combine(Application.persistentDataPath, "zod_win_soda_k439");
        }
        public static void Run() { Report = "running"; new GameObject("OzWinSodaProof").AddComponent<OzWinSodaProof>(); }

        /// <summary>Reads what the listener outputs (nothing is changed in the sound).</summary>
        sealed class Tap : MonoBehaviour
        {
            public readonly List<float> buf = new List<float>(); public int channels = 2; public volatile bool on;
            readonly object gate = new object();
            void OnAudioFilterRead(float[] data, int ch) { if (!on) return; lock (gate) { channels = ch; if (buf.Count < 48000 * 2 * 40) buf.AddRange(data); } }
            public float[] Take() { lock (gate) { var a = buf.ToArray(); buf.Clear(); return a; } }
        }

        readonly StringBuilder sb = new StringBuilder(); int fails, passes; string dir;
        void Log(string t) { sb.AppendLine(t); Debug.Log("[OzWinSodaProof] " + t); }
        void Check(string what, bool ok) { if (ok) passes++; else fails++; Log((ok ? "PASS " : "FAIL ") + what); }
        static string F(float v) => v.ToString("0.00", System.Globalization.CultureInfo.InvariantCulture);

        bool was = true, restore;
        void Start() { dir = Folder(); Directory.CreateDirectory(dir); StartCoroutine(All()); }
        void OnDestroy() { if (restore) ZodBriefing.WinSodaLaughsOn = was; }                          // the switch is never left off

        sealed class RunData
        {
            public bool started; public string reply; public float lineEnd = -1f, gigEnd = -1f, voiceEnd = -1f, speakLastT = -99f, tAfterSpeak = -99f;
            public List<(string key, float at, float firedAt, bool skipped, bool playing, float relDb)> cues = new List<(string, float, float, bool, bool, float)>();
            public readonly Dictionary<string, float> worstLevel = new Dictionary<string, float>();
            public int played, skipped, logPlay, logSkip; public float[] audio; public int ch;
        }

        IEnumerator Line(RunData r, string wav)
        {
            float wait = 0f;
            while ((ZodBriefing.DebugLineId != null || ZodBriefing.BriefingActive) && wait < 60f) { wait += Time.unscaledDeltaTime; yield return null; }
            yield return new WaitForSecondsRealtime(0.5f);
            var lis = FindFirstObjectByType<AudioListener>();
            var tap = lis ? lis.gameObject.AddComponent<Tap>() : null;
            int p0 = ZodBriefing.WinSodaPlayed, s0 = ZodBriefing.WinSodaSkipped; float t0 = Time.unscaledTime;
            if (tap) tap.on = true;
            r.reply = ZodBriefing.DebugLineAs(ZodBriefing.WinSodaLineId, ZodBriefing.Kind.Victory);
            r.started = r.reply != null && r.reply.StartsWith("playing");
            Log("   " + r.reply + " | plan: " + ZodBriefing.WinSodaLastPlan);
            var line = ZodLibrary.Shared.Line(ZodBriefing.WinSodaLineId); var clip = ZodLibrary.Shared.Clip(ZodBriefing.WinSodaLineId);
            r.voiceEnd = line != null && clip ? Mathf.Min(clip.length, line.duration > 0f ? line.duration : clip.length) : -1f;
            float alive = 0f;
            while (r.started && ZodBriefing.DebugLineId == ZodBriefing.WinSodaLineId && alive < 25f)
            {
                alive += Time.unscaledDeltaTime;
                float t = ZodBriefing.DebugLineT;
                r.lineEnd = ZodBriefing.WinSodaLineEnd; if (ZodBriefing.WinSodaGigglesEnd > 0f) r.gigEnd = ZodBriefing.WinSodaGigglesEnd;
                if (ZodBriefing.Speaking) r.speakLastT = t; else if (r.tAfterSpeak < -90f && r.speakLastT > 0f) r.tAfterSpeak = t;
                r.cues = ZodBriefing.WinSodaCues();
                foreach (var c in r.cues)
                    if (c.playing && c.relDb > -98f)
                    {
                        float want = c.key == "can" ? ZodBriefing.WinSodaCanDb : c.key == "alan_a" ? ZodBriefing.WinSodaAlanADb : c.key == "brad" ? ZodBriefing.WinSodaBradDb : ZodBriefing.WinSodaAlanBDb;
                        float err = Mathf.Abs(c.relDb - want);
                        r.worstLevel[c.key] = r.worstLevel.TryGetValue(c.key, out var e0) ? Mathf.Max(e0, err) : err;
                    }
                yield return null;
            }
            yield return new WaitForSecondsRealtime(0.4f);
            if (tap) { tap.on = false; r.audio = tap.Take(); r.ch = tap.channels; Destroy(tap); WriteWav(Path.Combine(dir, wav), r.audio, r.ch, AudioSettings.outputSampleRate); Log("   recording " + wav); }
            r.played = ZodBriefing.WinSodaPlayed - p0; r.skipped = ZodBriefing.WinSodaSkipped - s0;
            foreach (var e in OzSoundLog.Since(t0))
                if (e.src == "ZodCameo" && e.key != null && e.key.StartsWith("zod_win_")) { if (e.outcome.StartsWith("PLAY")) r.logPlay++; else if (e.outcome.StartsWith("SKIP")) r.logSkip++; }
        }

        IEnumerator All()
        {
            Log("K439 win can + giggles proof, level " + (ZGame.I ? ZGame.I.level : "?") + ", switch " + ZodBriefing.WinSodaLaughsOn + ", voice gain " + F(ZodBriefing.WinSodaVoiceGain));
            was = ZodBriefing.WinSodaLaughsOn; restore = true;
            var on = new RunData(); var off = new RunData();
            ZodBriefing.WinSodaLaughsOn = true;
            yield return Line(on, "k439_win_line_on.wav");
            ZodBriefing.WinSodaLaughsOn = false;
            yield return Line(off, "k439_win_line_off.wav");
            ZodBriefing.WinSodaLaughsOn = was; restore = false;

            var line = ZodLibrary.Shared.Line(ZodBriefing.WinSodaLineId);
            var plan = ZodBriefing.WinSodaPlan(line, out string why);
            float lastCue = 0f; if (line != null) foreach (var c in line.cues) lastCue = Mathf.Max(lastCue, c.end);
            var beats = ZodLibrary.Shared.Beats(ZodBriefing.WinSodaLineId);
            Check($"P1 the line played as a victory ({on.reply}); planned sounds {on.cues.Count} (the can + 3 giggles)", on.started && on.cues.Count == 4 && plan != null);
            foreach (var c in on.cues)
            {
                float d = c.firedAt - c.at;
                if (c.key == "can") Check($"P2 the can at {F(c.at)} s (end of '{ZodBriefing.WinSodaCanWord}...'), started {F(c.firedAt)} s ({F(d)} late)", c.firedAt > -90f && d >= 0f && d <= 0.06f);
                else Check($"P3 giggle {c.key} at {F(c.at)} s, started {F(c.firedAt)} s ({F(d)} late); after his last cue {F(lastCue)}", c.firedAt > -90f && d >= 0f && d <= 0.06f && c.at >= lastCue);
            }
            foreach (var c in on.cues)
                Check($"P4 {c.key}: level against his voice gain off by at most {F(on.worstLevel.TryGetValue(c.key, out var e) ? e : 99f)} dB", on.worstLevel.TryGetValue(c.key, out var e2) && e2 <= 0.3f);
            Check($"P5 speaking until {F(on.speakLastT)} s (last giggle ends {F(on.gigEnd)}), not speaking from {F(on.tAfterSpeak)} s", on.speakLastT >= on.gigEnd - 0.05f && on.tAfterSpeak > 0f && on.tAfterSpeak <= on.gigEnd + 0.4f);
            float endOld = on.voiceEnd + beats.tail;
            Check($"P6 the window's end {F(on.lineEnd)} s = last giggle end {F(on.gigEnd)} + hold {F(ZodBriefing.WinSodaHoldAfter)}; before it was {F(endOld)}",
                  Mathf.Abs(on.lineEnd - Mathf.Max(endOld, on.gigEnd + ZodBriefing.WinSodaHoldAfter)) <= 0.02f && on.lineEnd > endOld);
            Check($"P7 sounds played {on.played}, skipped {on.skipped}; sound log PLAY {on.logPlay}, SKIP {on.logSkip}", on.played == 4 && on.skipped == 0 && on.logPlay == 4 && on.logSkip == 0);
            Check($"P8 switch off: played {off.played}, planned {off.cues.Count}, window end {F(off.lineEnd)} (before {F(endOld)}), speaking until {F(off.speakLastT)} (voice end {F(off.voiceEnd)})",
                  off.started && off.played == 0 && off.cues.Count == 0 && Mathf.Abs(off.lineEnd - endOld) <= 0.02f && off.speakLastT <= off.voiceEnd + 0.2f);
            Log($"{passes} PASS, {fails} FAIL");
            Report = $"{passes} PASS, {fails} FAIL";
            File.WriteAllText(Path.Combine(dir, "win_soda_proof_log.txt"), sb.ToString());
            Destroy(gameObject);
        }

        static void WriteWav(string path, float[] x, int ch, int rate)
        {
            if (x == null) return;
            using (var f = new BinaryWriter(File.Create(path)))
            {
                int n = x.Length * 2;
                f.Write(Encoding.ASCII.GetBytes("RIFF")); f.Write(36 + n); f.Write(Encoding.ASCII.GetBytes("WAVE"));
                f.Write(Encoding.ASCII.GetBytes("fmt ")); f.Write(16); f.Write((short)1); f.Write((short)ch); f.Write(rate); f.Write(rate * ch * 2); f.Write((short)(ch * 2)); f.Write((short)16);
                f.Write(Encoding.ASCII.GetBytes("data")); f.Write(n);
                foreach (var v in x) f.Write((short)Mathf.Clamp(Mathf.RoundToInt(v * 32767f), -32768, 32767));
            }
        }
    }
}
