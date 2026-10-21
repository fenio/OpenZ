#if !OPENZ_HARNESS
using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEngine;

namespace OpenZ.EditorTools
{
    /// <summary>
    /// K439 counted check out of Play (menu OpenZ/V1.1/Zod: K439 Win Can + Giggles Check): the win line zod_win_02's rocket fuel can and the
    /// boys' giggles (View/Zod/ZodBriefing.WinSoda.cs), measured on the real files:
    ///  C1 the four clips load: mono, 44.1 kHz, plain PCM files (fmt + data only), lengths in range;
    ///  C2 the line and its word sheet are there; the plan has the can + three giggles;
    ///  C3 the can sits in his pause: its moment is the end of "hurt...", and its loud part (within 20 dB of its peak) is over before his
    ///     voice comes back over -30 dBFS;
    ///  C4 the giggles start after his last cue, and his clip is silent (under -45 dBFS) from the first giggle on;
    ///  C5 the levels: the can and every giggle (their loudest 400 ms, at their dB) stay 8-16 dB under his words (the median 400 ms of his
    ///     cues), the can's peak under his words' peak;
    ///  C6 the window holds until the last giggle ends + the hold, at most 1.0 s longer than before;
    ///  C7 the switch is on by default.
    /// </summary>
    public static class OzWinSodaCount
    {
        static int pass, fail; static StringBuilder sb;
        static void Check(string what, bool ok) { if (ok) pass++; else fail++; sb.AppendLine((ok ? "PASS " : "FAIL ") + what); }

        static float[] Samples(AudioClip c)
        {
            if (!c) return null;
            if (c.loadState != AudioDataLoadState.Loaded) c.LoadAudioData();
            var d = new float[c.samples * c.channels];
            if (!c.GetData(d, 0)) return null;
            if (c.channels == 1) return d;
            var m = new float[c.samples];
            for (int i = 0; i < c.samples; i++) { float s = 0f; for (int k = 0; k < c.channels; k++) s += d[i * c.channels + k]; m[i] = s / c.channels; }
            return m;
        }

        /// <summary>RMS in dBFS of the window [t0, t0 + len) (s) of mono samples at the given rate (silence past the end).</summary>
        static float Db(float[] x, int rate, float t0, float len)
        {
            int a = Mathf.Max(0, (int)(t0 * rate)), b = Mathf.Min(x.Length, (int)((t0 + len) * rate)), n = Mathf.Max(1, (int)(len * rate));
            if (b <= a) return -120f;
            double e = 0; for (int i = a; i < b; i++) e += x[i] * x[i];
            return 10f * Mathf.Log10((float)(e / n) + 1e-12f);
        }

        static float Peak(float[] x) { float p = 0f; foreach (var v in x) p = Mathf.Max(p, Mathf.Abs(v)); return 20f * Mathf.Log10(p + 1e-9f); }

        static bool PlainPcm(string assetPath, out string chunks)
        {
            chunks = "?";
            string full = Path.Combine(Path.GetDirectoryName(Application.dataPath), assetPath);
            if (!File.Exists(full)) { chunks = "file missing"; return false; }
            var d = File.ReadAllBytes(full);
            if (d.Length < 12 || Encoding.ASCII.GetString(d, 0, 4) != "RIFF" || Encoding.ASCII.GetString(d, 8, 4) != "WAVE") { chunks = "not RIFF WAVE"; return false; }
            var ids = new List<string>(); int i = 12; short fmtTag = 0, ch = 0, bits = 0; int rate = 0;
            while (i + 8 <= d.Length)
            {
                string id = Encoding.ASCII.GetString(d, i, 4); int n = BitConverter.ToInt32(d, i + 4); ids.Add(id);
                if (id == "fmt " && n >= 16) { fmtTag = BitConverter.ToInt16(d, i + 8); ch = BitConverter.ToInt16(d, i + 10); rate = BitConverter.ToInt32(d, i + 12); bits = BitConverter.ToInt16(d, i + 22); }
                i += 8 + n + (n & 1);
            }
            chunks = string.Join("+", ids) + $" (format {fmtTag}, {ch} ch, {rate} Hz, {bits} bit)";
            return ids.Count == 2 && ids[0] == "fmt " && ids[1] == "data" && fmtTag == 1 && ch == 1 && rate == 44100 && bits == 16;
        }

        public static string Check()
        {
            pass = 0; fail = 0; sb = new StringBuilder();
            sb.AppendLine("K439 win line can + giggles check (zod_win_02; switch ZodBriefing.WinSodaLaughsOn " + ZodBriefing.WinSodaLaughsOn + ")");
            // ---- C1 the clips
            var res = new[] { (ZodBriefing.WinSodaCanRes, 0.30f, 0.60f), (ZodBriefing.WinSodaAlanARes, 0.35f, 1.20f), (ZodBriefing.WinSodaBradRes, 0.35f, 1.20f), (ZodBriefing.WinSodaAlanBRes, 0.35f, 1.20f) };
            var clips = new Dictionary<string, AudioClip>(); var smp = new Dictionary<string, float[]>();
            foreach (var (r, lo, hi) in res)
            {
                var c = Resources.Load<AudioClip>(r); clips[r] = c;
                bool pcm = PlainPcm("Assets/OpenZ/Resources/" + r + ".wav", out string chunks);
                var s = Samples(c); smp[r] = s;
                Check($"C1 {r}: loads {(c ? "yes" : "NO")}, {(c ? c.channels : 0)} ch, {(c ? c.frequency : 0)} Hz, {(c ? c.length : 0f):0.000} s ({lo:0.00}-{hi:0.00}); file {chunks}",
                      c && s != null && c.channels == 1 && c.frequency == 44100 && c.length >= lo && c.length <= hi && pcm);
            }
            // ---- C2 the line and the plan
            var lib = ZodLibrary.Shared; var line = lib.Line(ZodBriefing.WinSodaLineId); var voice = lib.Clip(ZodBriefing.WinSodaLineId);
            var plan = ZodBriefing.WinSodaPlan(line, out string why);
            Check($"C2 the line {ZodBriefing.WinSodaLineId} ({(voice ? voice.length : 0f):0.00} s) and its sheet are there; the plan: {(plan == null ? "NONE (" + why + ")" : plan.Count + " sounds")}",
                  line != null && voice && plan != null && plan.Count == 4 && plan[0].key == "can" && !plan[0].giggle && plan[1].giggle && plan[2].giggle && plan[3].giggle);
            var z = Samples(voice);
            if (plan == null || z == null || smp[ZodBriefing.WinSodaCanRes] == null) { sb.AppendLine($"{pass} PASS, {fail + 1} FAIL (nothing more to measure)"); return sb.ToString(); }
            int zr = voice.frequency;
            // ---- C3 the can in his pause
            ZodWord hurt = null; ZodWord next = null;
            for (int i = 0; i < line.words.Length; i++)
                if (line.words[i].word.StartsWith(ZodBriefing.WinSodaCanWord, StringComparison.OrdinalIgnoreCase)) { hurt = line.words[i]; next = i + 1 < line.words.Length ? line.words[i + 1] : null; break; }
            var can = smp[ZodBriefing.WinSodaCanRes]; int cr = clips[ZodBriefing.WinSodaCanRes].frequency;
            float canPk = -120f; for (float t = 0f; t < can.Length / (float)cr; t += 0.01f) canPk = Mathf.Max(canPk, Db(can, cr, t, 0.01f));
            float canLoudEnd = 0f; for (float t = 0f; t < can.Length / (float)cr; t += 0.01f) if (Db(can, cr, t, 0.01f) > canPk - 20f) canLoudEnd = t + 0.01f;
            float back = -1f; for (float t = hurt.end + 0.05f; t < voice.length; t += 0.01f) if (Db(z, zr, t, 0.01f) > -30f) { back = t; break; }
            float canAt = plan[0].at;
            Check($"C3 the can at {canAt:0.00} s = the end of '{hurt.word}' ({hurt.end:0.00}) + {ZodBriefing.WinSodaCanAfterWord:0.00}; loud until {canAt + canLoudEnd:0.00} s, his voice back over -30 dBFS at {back:0.00} s (next word '{next?.word}')",
                  Mathf.Abs(canAt - (hurt.end + ZodBriefing.WinSodaCanAfterWord)) < 0.001f && back > 0f && canAt + canLoudEnd <= back);
            // ---- C4 the giggles after his last word
            float lastCue = 0f; foreach (var c in line.cues) lastCue = Mathf.Max(lastCue, c.end);
            float firstGig = float.MaxValue, lastGigEnd = 0f, zLoudAfter = -120f;
            for (int i = 1; i < plan.Count; i++) { firstGig = Mathf.Min(firstGig, plan[i].at); lastGigEnd = Mathf.Max(lastGigEnd, plan[i].at + clips[plan[i].res].length); }
            for (float t = firstGig; t < voice.length; t += 0.01f) zLoudAfter = Mathf.Max(zLoudAfter, Db(z, zr, t, 0.01f));
            Check($"C4 the giggles start {firstGig:0.00} s, after his last cue ({lastCue:0.00}); his clip from there at most {zLoudAfter:0.0} dBFS (under -45)", firstGig >= lastCue && zLoudAfter < -45f);
            // ---- C5 levels against his words
            var win = new List<float>();
            foreach (var c in line.cues) for (float t = c.start; t + 0.4f <= c.end; t += 0.1f) win.Add(Db(z, zr, t, 0.4f));
            win.Sort(); float med = win[win.Count / 2];
            float zPk = Peak(z);
            foreach (var st in plan)
            {
                var x = smp[st.res]; int rr = clips[st.res].frequency; float top = -120f;
                for (float t = 0f; t < Mathf.Max(0.01f, x.Length / (float)rr - 0.05f); t += 0.05f) top = Mathf.Max(top, Db(x, rr, t, 0.4f));
                top += st.db; float pk = Peak(x) + st.db; float under = med - top;
                Check($"C5 {st.key} at {st.db:0.0} dB: its loudest 400 ms {top:0.0} dBFS, {under:0.0} dB under his words' median ({med:0.0}); peak {pk:0.0} vs his {zPk:0.0} dBFS", under >= 8f && under <= 16f && pk < zPk);
            }
            // ---- C6 the window
            var beats = lib.Beats(ZodBriefing.WinSodaLineId);
            float voiceEnd = Mathf.Min(voice.length, line.duration > 0f ? line.duration : voice.length), endOld = voiceEnd + beats.tail, endNew = Mathf.Max(endOld, lastGigEnd + ZodBriefing.WinSodaHoldAfter);
            Check($"C6 the window: last giggle ends {lastGigEnd:0.00} s, the line's end {endOld:0.00} -> {endNew:0.00} s (+{endNew - endOld:0.00}, at most 1.0)", endNew >= lastGigEnd + ZodBriefing.WinSodaHoldAfter - 1e-4f && endNew - endOld <= 1.0f);
            Check("C7 the switch WinSodaLaughsOn is on", ZodBriefing.WinSodaLaughsOn);
            sb.AppendLine($"{pass} PASS, {fail} FAIL");
            return sb.ToString();
        }
    }
}
#endif
