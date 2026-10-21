using System.Collections.Generic;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEngine;

namespace OpenZ.EditorTools
{
    /// <summary>
    /// Version 1.1 checks of the plank bridge's wobble sounds (menu OpenZ/V1.1/Bridge Sounds ...), Level 16:
    /// <list type="bullet">
    /// <item>Events and clips (Edit or Play): the four events of OzBridgeWobble.Voice.cs are in the sound table, on the world bus,
    /// 3D, and every clip they list loads (name, length). PASS = 4 events, at least 3 clips each, 0 missing.</item>
    /// <item>Sound log (Play, Level 16): after vehicles (a jeep, a tank, a heavy tank or rocket launcher) and a robot squad have
    /// crossed the plank bridge, every line of these events since Play began, the counters per event and the mean volume factor per
    /// weight class. PASS = at least one knock, one creak and one rope strain played, 0 "no event" / "no clips" drops, and the
    /// heavier class is never the quieter one.</item>
    /// <item>Play each once at the view centre (Play): the four events one after another, each as many times as it has variations,
    /// 0.9 s apart, at the middle of the screen (to hear them without driving; their lines go into the sound log).</item>
    /// </list>
    /// Test tools only: nothing is saved.
    /// </summary>
    public static class OzBridgeVoiceCheck
    {
        const string M = "OpenZ/V1.1/Bridge Sounds ";
        static readonly string[] Events = { OzBridgeWobble.KnockEv, OzBridgeWobble.CreakEv, OzBridgeWobble.GroanEv, OzBridgeWobble.RopeEv };

        [MenuItem(M + "Events And Clips (Edit or Play)")]
        static void Table()
        {
            var ta = Resources.Load<TextAsset>(AudioDirector.TablePath);
            var sb = new StringBuilder("[OpenZ bridge] the wobble sounds in the sound table:\n");
            if (!ta) { Debug.LogError("[OpenZ bridge] FAIL: Resources/" + AudioDirector.TablePath + ".json missing"); return; }
            var t = JsonUtility.FromJson<SoundTable>(ta.text);
            int found = 0, missing = 0; bool pass = true;
            foreach (var id in Events)
            {
                var d = t.events.FirstOrDefault(e => e.id == id);
                if (d == null) { sb.AppendLine($"  {id,-24} NOT IN THE TABLE"); pass = false; continue; }
                found++;
                sb.AppendLine($"  {id,-24} bus {d.bus}, 3D {d.is3D}, {d.volDb:F1} dB, {d.minDist:F0}-{d.maxDist:F0} m, cooldown {d.cooldown:F2} s, {d.clips.Length} clips:");
                foreach (var c in d.clips)
                {
                    var clip = Resources.Load<AudioClip>(c.res);
                    if (!clip) missing++;
                    sb.AppendLine($"      {c.res,-52} {(clip ? clip.length.ToString("F2") + " s" : "MISSING")}  gain {c.gainDb:+0.0;-0.0;0.0} dB");
                }
                if (d.clips.Length < 3 || d.bus != "SFX/World" || !d.is3D) pass = false;
            }
            pass &= found == Events.Length && missing == 0;
            sb.Append(pass ? $"PASS bridge sounds table: {found} events, every clip loads" : $"FAIL bridge sounds table ({found} of {Events.Length} events, {missing} clips missing)");
            if (pass) Debug.Log(sb.ToString()); else Debug.LogError(sb.ToString());
        }

        [MenuItem(M + "Sound Log (Play)")]
        static void Log()
        {
            if (!Application.isPlaying) { Debug.LogWarning("[OpenZ bridge] enter Play on Level 16 first and send vehicles and a robot squad over the plank bridge"); return; }
            var es = OzSoundLog.Since(0f).Where(e => e.key != null && Events.Contains(e.key)).ToList();
            var sb = new StringBuilder($"[OpenZ bridge] wobble sound lines since Play began: {es.Count}\n");
            foreach (var e in es.Skip(Mathf.Max(0, es.Count - 60))) sb.AppendLine($"  {e.t,8:F2}  {e.src,-4} {e.key,-24} {e.clip ?? "-",-26} {e.outcome,-60} vol {e.vol:F3}");
            if (es.Count > 60) sb.AppendLine($"  ({es.Count - 60} earlier lines not shown)");
            var played = new Dictionary<string, int>();
            foreach (var id in Events) played[id] = es.Count(e => e.src == "AD" && e.key == id && e.outcome != null && e.outcome.StartsWith("PLAY"));
            int noTable = es.Count(e => e.outcome != null && (e.outcome.StartsWith("DROP no event") || e.outcome.StartsWith("DROP no clips")));
            sb.AppendLine("  played: " + string.Join(", ", Events.Select(id => id + " " + played[id])));
            sb.AppendLine($"  counters: knocks {OzBridgeWobble.Knocks}, creaks {OzBridgeWobble.Creaks} (under robots {OzBridgeWobble.RobotCreaks}), groans {OzBridgeWobble.Groans}, rope strains {OzBridgeWobble.Ropes}");
            string[] cls = { "robots", "jeep", "tank", "heavy" }; var mean = new float[4]; var seen = new List<int>();
            for (int i = 0; i < 4; i++)
            {
                int n = OzBridgeWobble.ClassCount[i]; mean[i] = n > 0 ? OzBridgeWobble.ClassVolSum[i] / n : 0f;
                if (n > 0) seen.Add(i);
                sb.AppendLine($"  {cls[i],-7}: {n,4} sounds, mean volume factor {(n > 0 ? mean[i].ToString("F2") : "-")}");
            }
            bool order = true; for (int k = 1; k < seen.Count; k++) if (mean[seen[k]] < mean[seen[k - 1]]) order = false;
            bool pass = played[OzBridgeWobble.KnockEv] > 0 && played[OzBridgeWobble.CreakEv] > 0 && played[OzBridgeWobble.RopeEv] > 0 && noTable == 0 && order;
            sb.Append(pass ? $"PASS bridge sounds: knock {played[OzBridgeWobble.KnockEv]}, creak {played[OzBridgeWobble.CreakEv]}, groan {played[OzBridgeWobble.GroanEv]}, rope {played[OzBridgeWobble.RopeEv]} played; louder with weight ({seen.Count} classes seen)"
                           : "FAIL bridge sounds (a knock, a creak and a rope strain played, 0 table drops and louder with weight wanted)");
            if (pass) Debug.Log(sb.ToString()); else Debug.LogError(sb.ToString());
        }

        static readonly Queue<(string id, int clip)> queue = new Queue<(string, int)>();
        static double nextAt;

        [MenuItem(M + "Play Each Once At The View Centre (Play)")]
        static void Each()
        {
            if (!Application.isPlaying || !AudioDirector.I) { Debug.LogWarning("[OpenZ bridge] enter Play first (any level)"); return; }
            queue.Clear();
            foreach (var id in Events) { var d = AudioDirector.I.Def(id); int n = d != null && d.loaded != null ? d.loaded.Count : 0; for (int i = 0; i < n; i++) queue.Enqueue((id, i)); }
            Debug.Log($"[OpenZ bridge] playing {queue.Count} wobble sounds at the view centre, 0.9 s apart (sound log lines follow)");
            nextAt = EditorApplication.timeSinceStartup; EditorApplication.update -= Tick; EditorApplication.update += Tick;
        }

        static void Tick()
        {
            if (!Application.isPlaying || queue.Count == 0) { EditorApplication.update -= Tick; return; }
            if (EditorApplication.timeSinceStartup < nextAt) return;
            nextAt = EditorApplication.timeSinceStartup + 0.9;
            var (id, k) = queue.Dequeue();
            var rts = Object.FindFirstObjectByType<RtsCamera>(); if (!rts || !ZGame.I) return;
            var f = rts.focus; var at = new Vector3(f.x, ZGame.I.GroundY(f.x, f.z), f.z);
            AudioDirector.I.Play(id, at, -100 - k, 1f, 1f);
            Debug.Log($"[OpenZ bridge] {id}, play {k + 1} (the table draws the variation, never the same twice in a row) at the view centre");
        }
    }
}
