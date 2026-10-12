#if UNITY_EDITOR
using System;
using System.Collections;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using UnityEngine;

namespace OpenZ
{
    /// <summary>
    /// music_v3 handover Play proof (editor only). PlayerPrefs oz_mv3proof_auto = "L02" or "L17" before entering Play on that level:
    /// it runs README_IMPORT.md's core timeline by itself with the Music v3 force API (briefing duck, [L02: skip, one unit, idle units],
    /// a big attack, the resume check, the interlude, a HEAVY attack, a Zod line over the drama, the fort falls (L02 win, L17 loss),
    /// the end screen and the end music), samples the music state + the listener loudness every 0.25 s, records what the player hears
    /// (after the master) to a WAV, and leaves Play. Heat: stops at GpuGuard 86 C held 30 s, or 90 C at once.
    /// </summary>
    public class MusicV3PlayProof : MonoBehaviour
    {
        static readonly string Dir = Path.GetFullPath(Path.Combine(Application.dataPath, "../../../../work/records/milestones/music_v3"));
        string lv; bool full; float t0; readonly StringBuilder log = new StringBuilder(); readonly StringBuilder csv = new StringBuilder();
        readonly List<(float t, string s)> lines = new List<(float, string)>();
        int errors; bool done; float hotSince = -1f, lastSample = -1f; string abort;
        Tap tap;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Auto()
        {
            string job = PlayerPrefs.GetString("oz_mv3proof_auto", "");
            if (string.IsNullOrEmpty(job)) return;
            PlayerPrefs.DeleteKey("oz_mv3proof_auto"); PlayerPrefs.Save();
            var p = new GameObject("MusicV3PlayProof").AddComponent<MusicV3PlayProof>();
            p.lv = job; p.full = job == "L02";
        }

        float T => Time.unscaledTime - t0;
        void Mark(string s) { log.AppendLine($"[{T,7:0.00}] ## {s}"); Debug.Log("[mv3proof] " + s); }
        void Snap(string s) { var ad = AudioDirector.I; log.AppendLine($"[{T,7:0.00}] ## SNAP {s}\n{(ad ? ad.MusicV3Status() : "no AudioDirector")}"); }
        bool SeenSince(string needle, float since) { foreach (var l in lines) if (l.t >= since && l.s.Contains(needle)) return true; return false; }
        float FirstSince(string needle, float since) { foreach (var l in lines) if (l.t >= since && l.s.Contains(needle)) return l.t; return -1f; }

        void OnEnable() { Application.logMessageReceived += OnLog; }
        void OnDisable() { Application.logMessageReceived -= OnLog; if (!done) Finish("disabled"); }
        void OnLog(string c, string st, LogType type)
        {
            if (type == LogType.Error || type == LogType.Exception) { errors++; lines.Add((T, "ERROR " + c)); log.AppendLine($"[{T,7:0.00}] ERROR {c}"); return; }
            if (c.StartsWith("[MusicV3]") || c.StartsWith("[ZodCameo]") || c.StartsWith("[music_v3]") || c.StartsWith("[Victory") || c.StartsWith("[OpenZVictory"))
            { lines.Add((T, c)); log.AppendLine($"[{T,7:0.00}] {c}"); }
        }

        static int GpuTemp()
        {
            foreach (var a in AppDomain.CurrentDomain.GetAssemblies())
            {
                var t = a.GetType("OpenZ.EditorTools.GpuGuard");
                if (t == null) continue;
                var p = t.GetProperty("LastTemp"); return p != null ? (int)p.GetValue(null) : -1;
            }
            return -1;
        }

        IEnumerator Start()
        {
            t0 = Time.unscaledTime; Directory.CreateDirectory(Dir);
            Mark($"music_v3 Play proof {lv} {DateTime.Now:yyyy-MM-dd HH:mm:ss}, oz_testmode {PlayerPrefs.GetInt("oz_testmode", 0)}, oz_music {PlayerPrefs.GetInt("oz_music", 1)}, oz_zod_briefing {PlayerPrefs.GetInt("oz_zod_briefing", 1)}");
            csv.AppendLine("t,layer,duck_db,zod_db,momentary_lufs,short_lufs,zod_speaking,briefing,gpu_c,voices");
            float w = 0f;
            while ((!AudioDirector.I || AudioDirector.I.MusicV3Core == null) && w < 40f) { w += Time.unscaledDeltaTime; yield return null; }
            var ad = AudioDirector.I;
            if (!ad || ad.MusicV3Core == null) { Finish("music_v3 never started"); yield break; }
            AudioDirector.MusicV3Overlay = true;
            var lis = OzLoudnessMeter.I ? OzLoudnessMeter.I.gameObject : FindFirstObjectByType<AudioListener>()?.gameObject;
            if (lis) { tap = lis.AddComponent<Tap>(); tap.Open(Path.Combine(Dir, lv + "_mix.wav")); }
            StartCoroutine(Sample());
            Mark("level ready, music_v3 live" + (lis ? ", recording the listener output" : ", NO listener found"));

            // 1 briefing duck
            w = 0f; while (!ZodBriefing.BriefingActive && w < 8f) { w += Time.unscaledDeltaTime; yield return null; }
            if (ZodBriefing.BriefingActive)
            {
                Mark("briefing playing"); yield return new WaitForSecondsRealtime(4f); Snap("during Zod's briefing");
                w = 0f; while (ZodBriefing.BriefingActive && w < 70f) { w += Time.unscaledDeltaTime; yield return null; }
                Mark("briefing over");
            }
            else Mark("NO briefing (check oz_zod_briefing / test mode)");
            yield return new WaitForSecondsRealtime(3f); Snap("after the briefing");

            // 2 skip + not-an-attack (L02 only)
            if (full)
            {
                ad.MusicV3Skip(); Mark("Skip regular song"); yield return new WaitForSecondsRealtime(7f); Snap("after skip");
                float s1 = T; ad.MusicV3Force(3, 10f); yield return new WaitForSecondsRealtime(5f); Snap("ONE unit shooting our fort");
                yield return new WaitForSecondsRealtime(6f);
                Mark("ONE unit: " + (SeenSince("BIG ATTACK", s1) ? "FAIL (BIG ATTACK)" : "OK, no drama"));
                float s2 = T; ad.MusicV3Force(5, 8f); yield return new WaitForSecondsRealtime(5f); Snap("idle units parked at our fort");
                yield return new WaitForSecondsRealtime(4f);
                Mark("idle units: " + (SeenSince("BIG ATTACK", s2) ? "FAIL (BIG ATTACK)" : "OK, no drama"));
            }

            // 3 big attack
            int first = full ? 1 : 2;
            float a0 = T; ad.MusicV3Force(first, 20f); Mark("Force big attack on " + (first == 1 ? "OUR" : "the ENEMY") + " fort 20 s");
            w = 0f; while (!SeenSince("BIG ATTACK", a0) && w < 12f) { w += Time.unscaledDeltaTime; yield return null; }
            float big = FirstSince("BIG ATTACK", a0);
            Mark(big > 0 ? $"BIG ATTACK {big - a0:0.00} s after the force" : "FAIL: no BIG ATTACK within 12 s");
            yield return new WaitForSecondsRealtime(4.5f); Snap("drama in"); Shot("drama");

            // 4 resume
            w = 0f; while (!SeenSince("resume check", a0) && w < 70f) { w += Time.unscaledDeltaTime; yield return null; }
            float back = FirstSince("ToRegular", a0), rc = FirstSince("resume check", a0);
            Mark(rc > 0 ? $"resume check {rc - a0:0.0} s after the force, fade back at {back - a0:0.0} s: {(SeenSince("-> OK", a0) ? "OK" : "NOT OK")}" : "FAIL: no resume check within 70 s");
            Snap("resumed"); Shot("resumed");

            // 5 interlude, then heavy
            yield return new WaitForSecondsRealtime(4f);
            float i0 = T; ad.MusicV3Force(first, 14f); Mark("Force big attack again 5 s after the fade back (interlude must hold)");
            yield return new WaitForSecondsRealtime(6f); Snap("interlude");
            yield return new WaitForSecondsRealtime(9f);
            Mark("interlude: " + (SeenSince("BIG ATTACK", i0) ? "FAIL (BIG ATTACK inside the interlude)" : "OK, no drama"));
            float wait = back > 0 ? back + 27f - T : 0f; if (wait > 0f) yield return new WaitForSecondsRealtime(wait);
            float h0 = T; ad.MusicV3Force(4, 30f); Mark($"Force HEAVY attack on OUR fort ({(back > 0 ? T - back : -1f):0.0} s after the fade back)");
            w = 0f; while (!SeenSince("BIG ATTACK", h0) && w < 12f) { w += Time.unscaledDeltaTime; yield return null; }
            float hb = FirstSince("BIG ATTACK", h0);
            Mark(hb > 0 ? $"HEAVY: BIG ATTACK {hb - h0:0.00} s after the force" : "FAIL: no drama for the heavy attack");
            yield return new WaitForSecondsRealtime(5f); Snap("heavy drama");

            // 7 Zod over the drama
            Mark("Zod line now: " + ZodBriefing.DebugIdle());
            yield return new WaitForSecondsRealtime(1.5f); Snap("Zod speaking over the drama");
            yield return new WaitForSecondsRealtime(5f); Snap("after Zod's line");

            // 10 / 11 the fort falls
            bool win = full;
            float g0 = T; Mark("DebugFortFalls(" + win + "): " + OpenZVictory.DebugFortFalls(win));
            w = 0f; while (!SeenSince("END SCREEN", g0) && w < 100f) { w += Time.unscaledDeltaTime; yield return null; }
            float es = FirstSince("END SCREEN", g0);
            Mark(es > 0 ? $"END SCREEN {es - g0:0.0} s after the fall; drama held: {(SeenSince("game over (Drama)", g0) ? "yes" : "NO")}" : "FAIL: no END SCREEN within 100 s");
            Snap("end screen"); Shot("endscreen");
            w = 0f; while (!SeenSince("(end music)", g0) && w < 15f) { w += Time.unscaledDeltaTime; yield return null; }
            float em = FirstSince("(end music)", g0);
            Mark(em > 0 ? $"end music started {em - es:0.0} s after the END SCREEN line" : "FAIL: no end music within 15 s");
            yield return new WaitForSecondsRealtime(6f); Snap("end music");
            Finish(null);
        }

        void Shot(string name) { try { ScreenCapture.CaptureScreenshot(Path.Combine(Dir, $"{lv}_{name}.png")); } catch (Exception e) { log.AppendLine("shot failed " + e.Message); } }

        static readonly Regex rxLayer = new Regex(@"music_v3 (\w+) \("), rxDuck = new Regex(@"duck (-?[\d.]+) dB \(zod (-?[\d.]+)\)"), rxVoice = new Regex(@"  (v\d: \w+ \w+ [^\n]*)");
        IEnumerator Sample()
        {
            while (!done)
            {
                var ad = AudioDirector.I; int gpu = GpuTemp();
                string st = ad ? ad.MusicV3Status() : "";
                var ml = rxLayer.Match(st); var md = rxDuck.Match(st);
                var vs = new List<string>(); foreach (Match m in rxVoice.Matches(st)) vs.Add(m.Groups[1].Value.Trim());
                var lm = OzLoudnessMeter.I;
                csv.AppendLine(string.Format(CultureInfo.InvariantCulture, "{0:0.00},{1},{2},{3},{4:0.0},{5:0.0},{6},{7},{8},\"{9}\"", T,
                    ml.Success ? ml.Groups[1].Value : "-", md.Success ? md.Groups[1].Value : "", md.Success ? md.Groups[2].Value : "",
                    lm ? lm.Momentary : -120f, lm ? lm.ShortTerm : -120f, ZodBriefing.Speaking ? 1 : 0, ZodBriefing.BriefingActive ? 1 : 0, gpu, string.Join(" | ", vs)));
                if (gpu >= 90) { abort = "GPU 90 C"; Finish(abort); yield break; }
                float step = lastSample < 0f ? 0.25f : T - lastSample; lastSample = T;
                if (gpu >= 86) { hotSince = hotSince < 0f ? step : hotSince + step; if (hotSince >= 30f) { abort = "GPU >= 86 C for 30 s (hysteresis 84 C)"; Finish(abort); yield break; } }
                else if (gpu >= 0 && gpu < 84) hotSince = -1f;
                yield return new WaitForSecondsRealtime(0.25f);
            }
        }

        void Finish(string why)
        {
            if (done) return; done = true;
            try
            {
                if (tap) tap.Close();
                var ad = AudioDirector.I;
                Mark(why == null ? "DONE" : "STOPPED: " + why);
                log.AppendLine($"errors/exceptions in the Console during the run: {errors}");
                if (ad && ad.MusicV3Core != null) { log.AppendLine("## MusicCore history"); foreach (var h in ad.MusicV3Core.History) log.AppendLine("  " + h); }
                File.WriteAllText(Path.Combine(Dir, lv + "_log.txt"), log.ToString());
                File.WriteAllText(Path.Combine(Dir, lv + "_timeline.csv"), csv.ToString());
                int prev = PlayerPrefs.GetInt("oz_mv3proof_prevbrief", -99);
                if (prev != -99) { PlayerPrefs.SetInt("oz_zod_briefing", prev); PlayerPrefs.DeleteKey("oz_mv3proof_prevbrief"); PlayerPrefs.Save(); }
            }
            catch (Exception e) { Debug.LogError("[mv3proof] finish: " + e); }
            UnityEditor.EditorApplication.isPlaying = false;
        }

        /// <summary>Records the listener output (after the master and the meter) as 16-bit PCM WAV.</summary>
        public class Tap : MonoBehaviour
        {
            readonly ConcurrentQueue<float[]> q = new ConcurrentQueue<float[]>();
            BinaryWriter bw; int ch = 2, rate; long frames; volatile bool on;
            public void Open(string path)
            {
                rate = AudioSettings.outputSampleRate;
                bw = new BinaryWriter(File.Create(path));
                bw.Write(new byte[44]); on = true;
            }
            void OnAudioFilterRead(float[] data, int channels) { if (!on) return; ch = channels; var c = new float[data.Length]; Array.Copy(data, c, data.Length); q.Enqueue(c); }
            void Update() { Drain(); }
            void Drain()
            {
                if (bw == null) return;
                while (q.TryDequeue(out var b)) { foreach (var f in b) bw.Write((short)Mathf.Clamp(Mathf.RoundToInt(f * 32767f), -32768, 32767)); frames += b.Length / Math.Max(1, ch); }
            }
            public void Close()
            {
                on = false; Drain(); if (bw == null) return;
                long bytes = frames * ch * 2; bw.Seek(0, SeekOrigin.Begin);
                bw.Write(Encoding.ASCII.GetBytes("RIFF")); bw.Write((int)(36 + bytes)); bw.Write(Encoding.ASCII.GetBytes("WAVEfmt "));
                bw.Write(16); bw.Write((short)1); bw.Write((short)ch); bw.Write(rate); bw.Write(rate * ch * 2); bw.Write((short)(ch * 2)); bw.Write((short)16);
                bw.Write(Encoding.ASCII.GetBytes("data")); bw.Write((int)bytes); bw.Close(); bw = null;
            }
            void OnDestroy() { Close(); }
        }
    }
}
#endif
