using System.Collections;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

namespace OpenZ
{
    /// <summary>
    /// <c></c>
    /// transmission-camera frames of the red-button insert (bronze segmented prop arm, tighter / lower framing, his body not drawn)
    /// plus one frame after the cut back (his body drawn again); logs the prop state and the Zod sounds (whoosh, clunk).
    /// </summary>
    public sealed class FistInsertProof : MonoBehaviour
    {
        public static string Dir = global::OpenZ.OzRelease.Milestones("task_zod_fist_insert_fix");
        public static string Last = "";
        static FistInsertProof run;

        static void Log(string s) { Directory.CreateDirectory(Dir); File.AppendAllText(Dir + "/proof_log.txt", System.DateTime.Now.ToString("HH:mm:ss.f") + " " + s + "\n"); Debug.Log("[FistInsertProof] " + s); Last = s; }

        public static string Run()
        {
            if (run) return "busy";
            var go = new GameObject("FistInsertProof"); run = go.AddComponent<FistInsertProof>();
            run.StartCoroutine(run.Seq());
            return "started";
        }

        IEnumerator Seq()
        {
            float s0 = Time.unscaledTime;
            Log("task_zod_fist_insert_fix Play proof, PropFist " + ZodStage.PropFist + ", HideBodyInInsert " + ZodStage.HideBodyInInsert + ", insert " + ZodStage.PropInsert.ToString("F2") + " fov " + ZodStage.PropInsertFov);
            Log("DebugNuke: " + ZodBriefing.DebugNuke());
            var st = FindFirstObjectByType<ZodStage>();
            float[] times = { 0.10f, 0.22f, 0.30f, 0.37f, 0.44f, 0.62f, 0.86f, 1.05f };
            int next = 0; bool after = false; float tq = Time.unscaledTime, doneAt = -1f;
            while (Time.unscaledTime - tq < 25f)
            {
                yield return new WaitForEndOfFrame();
                if (!st) st = FindFirstObjectByType<ZodStage>();
                if (!st) continue;
                if (next >= times.Length)
                {
                    if (doneAt < 0f) doneAt = Time.unscaledTime;
                    if (!after && !st.Slamming && Time.unscaledTime - doneAt > 0.35f)
                    {
                        after = true; Save(st.CaptureFeed(960, 720), "after_feed.png");
                        Log("after the insert: " + st.FistStatus()); break;
                    }
                    continue;
                }
                if (!st.PressPlaced || st.PressU < 0f || st.PressU > 3f) continue;
                while (next < times.Length && st.PressU >= times[next])
                {
                    string f = "fist_" + ((int)(times[next] * 100f)).ToString("D3") + ".png";
                    Save(st.CaptureFeed(960, 720), f);
                    Log("insert " + f + " u " + st.PressU.ToString("F2") + ": " + st.FistStatus() + " | " + st.PressStatus());
                    next++;
                }
            }
            var lines = new List<string>();
            foreach (var e in OzSoundLog.Since(s0 - 0.5f)) { var s = OzSoundLog.Format(new List<OzSoundLog.Entry> { e }); if (s.Contains("Zod")) lines.Add(s.Trim()); }
            Log("insert sounds: " + string.Join(" | ", lines.ToArray()));
            Log("DONE in " + (Time.unscaledTime - s0).ToString("F0") + " s");
            run = null; Destroy(gameObject);
        }

        static void Save(Texture2D tex, string file)
        {
            if (!tex) return;
            Directory.CreateDirectory(Dir); File.WriteAllBytes(Dir + "/" + file, tex.EncodeToPNG()); Destroy(tex);
        }
    }
}
