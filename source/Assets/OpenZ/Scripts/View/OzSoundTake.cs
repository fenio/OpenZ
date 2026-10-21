using System.Collections.Generic;
using System.Text;
using UnityEngine;

namespace OpenZ
{
    /// <summary>
    /// The whole take's sound log for a Play proof (K256e, FIX_LIST M15: the delivered logs stopped at the in-Play sound log's 2000 lines).
    /// <see cref="Begin"/> starts a take into a file; every <see cref="FlushSec"/> s (and at <see cref="End"/>) the new lines of OzSoundLog
    /// are appended, so nothing is lost to its ring buffer; <see cref="End"/> also writes the table per action: seen (asked), played,
    /// dropped (and the most common reason), the lowest and the loudest volume a play had (voices and ambience marked), so "every action
    /// has a sound" is counted on real plays only. One take at a time; test only (a proof starts and ends it).
    /// It also keeps every voice line the take played (FIX_LIST M13: Rachel's lines against the shuttle's, counted over the WHOLE take, not
    /// over the in-Play log's last 2000 lines): <see cref="VoiceOverlaps"/>, and a line of it at the end of the table.
    /// </summary>
    public sealed class OzSoundTake : MonoBehaviour
    {
        public static float FlushSec = 2f;
        public static OzSoundTake Current { get; private set; }

        public sealed class Row { public string key; public int seen, played, dropped; public float lo = 9f, hi; public string bus = "", why = ""; public readonly Dictionary<string, int> whys = new Dictionary<string, int>(); }
        /// <summary>A voice line the take played: when (unscaled s), its event, its clip, Rachel's or a robot's.</summary>
        public struct VoicePlay { public float t; public string key, clip; public bool rachel; }

        string path; float t0, lastT = -1f, nextFlush; int sameT; int lines;
        readonly Dictionary<string, Row> rows = new Dictionary<string, Row>();
        readonly List<VoicePlay> voices = new List<VoicePlay>();
        public IReadOnlyList<VoicePlay> Voices => voices;
        public IReadOnlyDictionary<string, Row> Rows => rows;
        public int Lines => lines;
        public float T0 => t0;

        /// <summary>Starts a take: the lines from now on go to <paramref name="file"/> (a new file).</summary>
        public static OzSoundTake Begin(string file)
        {
            if (Current) Current.End();
            var go = new GameObject("OzSoundTake"); DontDestroyOnLoad(go);
            var t = go.AddComponent<OzSoundTake>();
            t.path = file; t.t0 = Time.unscaledTime; t.lastT = t.t0 - 0.0001f; t.nextFlush = t.t0 + FlushSec;
            try { System.IO.File.WriteAllText(file, $"whole-take sound log (no line cap), from {t.t0:F2} s real time\n"); } catch (System.Exception ex) { Debug.LogWarning("[OzSoundTake] " + ex.Message); }
            Current = t;
            return t;
        }

        void Update() { if (Time.unscaledTime >= nextFlush) { nextFlush = Time.unscaledTime + FlushSec; Flush(); } }

        /// <summary>Appends the lines logged since the last flush (an entry of the very time of the last flush is not written twice).</summary>
        public void Flush()
        {
            var es = OzSoundLog.Since(lastT);
            if (es.Count == 0) return;
            var sb = new StringBuilder(); int skip = sameT; float newest = lastT; int same = 0;
            foreach (var e in es)
            {
                if (e.t == lastT && skip > 0) { skip--; continue; }
                sb.AppendLine($"{e.t - t0,8:F2}  {e.src,-4} {e.key,-28} {e.clip ?? "-",-32} {e.outcome,-24} vol {e.vol:F3}  dist {e.dist:F1} m");
                Count(e); lines++;
                if (e.t > newest) { newest = e.t; same = 1; } else if (e.t == newest) same++;
            }
            sameT = newest == lastT ? sameT + same : same; lastT = newest;
            try { System.IO.File.AppendAllText(path, sb.ToString()); } catch (System.Exception ex) { Debug.LogWarning("[OzSoundTake] " + ex.Message); }
        }

        static bool IsPlay(string outcome) => outcome != null && (outcome.StartsWith("PLAY") || outcome.StartsWith("LOOP") && !outcome.Contains("STOP"));
        static bool IsDrop(string outcome) => outcome != null && (outcome.StartsWith("DROP") || outcome.StartsWith("dropped") || outcome.Contains("skipped"));

        void Count(OzSoundLog.Entry e)
        {
            if (string.IsNullOrEmpty(e.key)) return;
            if (!rows.TryGetValue(e.key, out var r)) { r = new Row { key = e.key }; rows[e.key] = r; }
            r.seen++;
            if (e.src == "VO" && e.outcome != null && e.outcome.StartsWith("PLAY"))
                voices.Add(new VoicePlay { t = e.t, key = e.key, clip = e.clip, rachel = e.outcome.Contains(" rachel") });
            if (IsPlay(e.outcome) || e.src == "DOOR")
            {
                r.played++;
                if (e.vol > 0f) { r.lo = Mathf.Min(r.lo, e.vol); r.hi = Mathf.Max(r.hi, e.vol); }
                if (string.IsNullOrEmpty(r.bus) && e.outcome != null && e.outcome.Length > 5) r.bus = e.outcome.Substring(5).Trim();
            }
            else if (IsDrop(e.outcome))
            {
                r.dropped++;
                string why = e.outcome.Length > 5 ? e.outcome.Substring(5).Trim() : e.outcome;
                r.whys[why] = r.whys.TryGetValue(why, out var n) ? n + 1 : 1;
            }
        }

        /// <summary>The row of one action (null = never asked in this take).</summary>
        public Row Of(string key) => rows.TryGetValue(key, out var r) ? r : null;

        /// <summary>Played at least once, and its quietest play at least minVol (0 = any): the "every action has a sound" test.</summary>
        public bool Heard(string key, float minVol = 0f) { var r = Of(key); return r != null && r.played > 0 && (minVol <= 0f || r.lo >= minVol); }

        /// <summary>FIX_LIST M13 over the whole take since `since` (unscaled s; the lines flushed so far: call Flush first for the latest):
        /// pairs of a Rachel line and a shuttle line (vo_shuttle_*) whose clips overlap by more than 0.02 s. rachel / shuttle = the lines
        /// counted; worst = the longest overlap.</summary>
        public int VoiceOverlaps(float since, out string worst, out int rachel, out int shuttle)
        {
            var R = new List<(float a, float b, string k)>(); var S = new List<(float a, float b, string k)>();
            foreach (var v in voices)
            {
                if (v.t < since) continue;
                if (v.rachel) R.Add((v.t, v.t + VoiceLen(v.key, v.clip), v.key));
                else if (OzShuttleVoiceGate.IsShuttleLine(v.key)) S.Add((v.t, v.t + VoiceLen(v.key, v.clip), v.key));
            }
            rachel = R.Count; shuttle = S.Count; worst = "-"; int n = 0; float w = 0f;
            foreach (var r in R)
                foreach (var s in S)
                {
                    float ov = Mathf.Min(r.b, s.b) - Mathf.Max(r.a, s.a);
                    if (ov <= 0.02f) continue;
                    n++; if (ov > w) { w = ov; worst = $"{s.k} at {s.a - t0:F2} s and {r.k} at {r.a - t0:F2} s overlap {ov:F2} s"; }
                }
            return n;
        }

        static readonly Dictionary<string, float> clipLen = new Dictionary<string, float>();
        /// <summary>A voice clip's play length (s): its event's own clip at its pitch, else the clip by name, else 1.5 s.</summary>
        static float VoiceLen(string key, string clip)
        {
            if (string.IsNullOrEmpty(clip)) return 1.5f;
            var ad = AudioDirector.I; var d = ad && key != null ? ad.Def(key) : null;
            if (d != null && d.loaded != null)
                foreach (var c in d.loaded) if (c && c.name == clip) return c.length / Mathf.Max(0.3f, d.pitchMul * (1f - Mathf.Max(0f, d.pitchVar)));
            if (clipLen.TryGetValue(clip, out var len)) return len;
            len = 1.5f;
            foreach (var c in Resources.FindObjectsOfTypeAll<AudioClip>()) if (c && c.name == clip) { len = c.length; break; }
            clipLen[clip] = len;
            return len;
        }

        /// <summary>The table per action (keys = the actions to list first; then every other key of the take).</summary>
        public string Table(IEnumerable<string> keys)
        {
            Flush();
            var sb = new StringBuilder();
            sb.AppendLine($"per action ({lines} lines in the take): seen, played, dropped (the most common reason), quietest / loudest play");
            var done = new HashSet<string>();
            void One(string k)
            {
                if (!done.Add(k)) return;
                var r = Of(k);
                if (r == null) { sb.AppendLine($"  {k,-30} seen   0 played   0 dropped   0  NOT ASKED"); return; }
                string top = "-"; int best = 0; foreach (var kv in r.whys) if (kv.Value > best) { best = kv.Value; top = kv.Key; }
                string kind = k.StartsWith("vo_") || k.StartsWith("ann_") ? " (voice)" : r.bus.StartsWith("Ambience") || k.StartsWith("amb_") ? " (ambience)" : "";
                sb.AppendLine($"  {k,-30} seen {r.seen,3} played {r.played,3} dropped {r.dropped,3} ({top}) quietest {(r.played > 0 && r.lo < 9f ? r.lo.ToString("F3") : "-")} loudest {(r.played > 0 ? r.hi.ToString("F3") : "-")}{kind}");
            }
            if (keys != null) foreach (var k in keys) One(k);
            sb.AppendLine("  -- every other action of the take:");
            var rest = new List<string>(rows.Keys); rest.Sort(System.StringComparer.Ordinal);
            foreach (var k in rest) One(k);
            int ov = VoiceOverlaps(t0, out string worst, out int nr, out int ns);
            sb.AppendLine($"voices over the whole take (M13): Rachel {nr} lines, the shuttle's {ns} lines, overlapping pairs {ov} (worst: {worst})");
            return sb.ToString();
        }

        /// <summary>Ends the take: the last lines and the table (into &lt;file&gt;_table.txt).</summary>
        public void End(IEnumerable<string> keys = null)
        {
            if (Current != this) return;
            var table = Table(keys);
            try { System.IO.File.WriteAllText(System.IO.Path.ChangeExtension(path, null) + "_table.txt", table); } catch (System.Exception ex) { Debug.LogWarning("[OzSoundTake] " + ex.Message); }
            Current = null;
            Destroy(gameObject);
        }
    }
}
