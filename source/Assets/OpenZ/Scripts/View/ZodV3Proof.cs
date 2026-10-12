using System.Collections;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

namespace OpenZ
{
    /// <summary>
    /// <c></c>
    /// For v1 and then v3 (ZodStage.ForceModel, session-only, the setting is never written) the same beats play as their real kinds
    /// (ZodBriefing.DebugLineAs): the L02 briefing, the nuke line (zod_nuke_01, scheming; the slam only logs) with the press and the
    /// cap-contact frame, zod_lose_01 (the HQ on fire, the facepalm react cam, the open mouth 4.6-5.8 s), zod_win_02 (the gaze at the boys,
    /// 1920x1080 feed frames at the same line times for both models (f_v1_* / f_v3_*), one full-screen frame of every beat in the real
    /// transmission window (s_v*_*), v3 feed frames at 10 fps (g_v3_*) for the gear-under-retargeting check, and proof_log.txt with the
    /// clip, the line time and the screen positions of head / chest / shoulders of every 1080p frame (for the crops). Last: the model
    /// setting with no proof override (migration, ModelVersion, face rig).
    /// </summary>
    public sealed class ZodV3Proof : MonoBehaviour
    {
        public static string Dir = global::OpenZ.OzRelease.Milestones("task_zod_v3_import");
        public static string Last = "";
        public static int[] Models = { 1, 3 };
        static ZodV3Proof run;
        public static bool Busy => run != null;

        static void Log(string s) { Directory.CreateDirectory(Dir); File.AppendAllText(Dir + "/proof_log.txt", System.DateTime.Now.ToString("HH:mm:ss.f") + " " + s + "\n"); Debug.Log("[ZodV3Proof] " + s); Last = s; }

        public static string Run()
        {
            if (run) return "busy";
            var go = new GameObject("ZodV3Proof"); run = go.AddComponent<ZodV3Proof>();
            run.StartCoroutine(run.Seq());
            return "started";
        }

        IEnumerator Seq()
        {
            float s0 = Time.unscaledTime;
            var g = ZGame.I;
            Log("task_zod_v3_import Play proof " + (g ? g.level : "?") + " " + Screen.width + "x" + Screen.height + ", setting before: " + ZodStage.ModelPrefState
                + ", migrated flag " + PlayerPrefs.GetInt(ZodStage.PrefModelMigrated, 0));
            int want = ZodStage.ModelWanted;                                                         // runs the one-time migration
            Log("setting after the first read: " + ZodStage.ModelPrefState + " -> ModelWanted " + want);
            foreach (int m in Models)
            {
                yield return Use(m);
                string v = "v" + m + "_";
                bool gear = m == 3;
                yield return Beat(v + "brief", "zod_brief_L02", ZodBriefing.Kind.Briefing, new[] { 1.5f, 4.0f }, 2.5f, gear, false);
                yield return Beat(v + "nuke", "zod_nuke_01", ZodBriefing.Kind.Nuke, new[] { 1.0f, 2.5f }, 2.0f, gear, true);
                yield return Beat(v + "lose01", "zod_lose_01", ZodBriefing.Kind.Defeat, new[] { 0.4f, 0.8f, 1.2f, 2.0f, 4.6f, 5.2f, 5.8f }, 1.0f, gear, false);
                yield return Beat(v + "win02", "zod_win_02", ZodBriefing.Kind.Victory, new[] { 1.0f, 2.4f, 4.0f }, 2.4f, gear, false);
                if (m == 3) yield return Beat(v + "win04", "zod_win_04", ZodBriefing.Kind.Victory, new[] { 1.0f, 2.0f }, 1.5f, true, false);
            }
            // ---- the setting with no override: what the editor / a build runs
            yield return Use(0);
            var tmp = ZodStage.Create(null, g ? g.level : "L02");
            Log("no override: " + ZodStage.ModelPrefState + " -> stage ModelVersion " + tmp.ModelVersion + " | " + tmp.FaceStatus());
            Destroy(tmp.gameObject);
            Log("DONE in " + (Time.unscaledTime - s0).ToString("F0") + " s");
            run = null; Destroy(gameObject);
        }

        IEnumerator Use(int m)
        {
            while (ZodBriefing.DebugLineId != null) yield return null;
            ZodStage.ForceModel = m;
            foreach (var st in FindObjectsByType<ZodStage>(FindObjectsInactive.Include, FindObjectsSortMode.None)) Destroy(st.gameObject);
            yield return null; yield return null;
            yield return new WaitForSecondsRealtime(0.6f);
            Log("model override " + (m == 0 ? "off" : "v" + m));
        }

        IEnumerator Beat(string tag, string id, ZodBriefing.Kind kind, float[] feedAt, float screenAt, bool gear10, bool press)
        {
            while (ZodBriefing.DebugLineId != null) yield return null;
            yield return new WaitForSecondsRealtime(0.5f);
            ZodTalkRig.ResetChinStats();
            Log(tag + ": " + ZodBriefing.DebugLineAs(id, kind));
            ZodStage st = null; int fi = 0, gi = 0; bool shot = false, pA = false, pB = false, pC = false; float nextGear = -1f; float tw = Time.unscaledTime;
            var clips = new List<string>(); string lastClip = null;
            while (Time.unscaledTime - tw < 60f)
            {
                yield return new WaitForEndOfFrame();
                if (ZodBriefing.DebugLineId != id) { if (Time.unscaledTime - tw > 1f) break; continue; }
                if (!st) st = FindFirstObjectByType<ZodStage>();
                if (!st) continue;
                float t = ZodBriefing.DebugLineT;
                if (st.CurrentClip != lastClip) { lastClip = st.CurrentClip; clips.Add(t.ToString("F1") + " " + lastClip); }
                if (fi < feedAt.Length && t >= feedAt[fi]) { Feed(st, "f_" + tag + "_" + feedAt[fi].ToString("F1") + "s.png", t); fi++; }
                if (!shot && t >= screenAt) { shot = true; ProofCapture.SaveJpg(Dir + "/s_" + tag + "_" + screenAt.ToString("F1") + "s.jpg", 0, 95); Log("screen s_" + tag + " at t " + t.ToString("F2") + " clip " + st.CurrentClip); }
                if (press && st.PressPlaced)
                {
                    float u = st.PressU;
                    if (!pA && u >= 0.10f) { pA = true; Feed(st, "f_" + tag + "_press.png", t); Log("press " + st.PressStatus()); }
                    if (!pB && u >= ZodStage.SlamHit) { pB = true; Feed(st, "f_" + tag + "_contact.png", t); Log("contact " + st.PressStatus()); }
                    if (!pC && u >= 0.62f) { pC = true; Feed(st, "f_" + tag + "_after.png", t); }
                }
                if (gear10 && t >= 0f && t >= nextGear)
                {
                    nextGear = (nextGear < 0f ? t : nextGear) + 0.1f;
                    var tex = st.CaptureFeed(960, 540);
                    if (tex) { File.WriteAllBytes(Dir + "/g_" + tag + "_" + (gi++).ToString("D3") + "_" + t.ToString("F1") + "_" + st.CurrentClip + ".jpg", tex.EncodeToJPG(90)); Destroy(tex); }
                }
            }
            Log(tag + " done: feed " + fi + "/" + feedAt.Length + ", gear frames " + gi + ", clips [" + string.Join(", ", clips.ToArray()) + "], chin-down peak " + ZodTalkRig.ChinDownPeak.ToString("F1")
                + " (shown " + ZodTalkRig.ChinDownOutPeak.ToString("F1") + "), roll peak " + ZodTalkRig.RollPeak.ToString("F1") + (st ? " | " + st.FaceStatus() : ""));
        }

        void Feed(ZodStage st, string file, float t)
        {
            var tex = st.CaptureFeed(1920, 1080);
            if (!tex) return;
            File.WriteAllBytes(Dir + "/" + file, tex.EncodeToPNG()); Destroy(tex);
            Log(file + " t " + t.ToString("F2") + " clip " + st.CurrentClip + " | " + Points(st));
        }

        /// <summary>Screen positions (pixels in the 1920x1080 frame, y down) of the head, the hat top, the chest, the neck and both shoulders.</summary>
        static string Points(ZodStage st)
        {
            Camera cam = null;
            foreach (var c in st.GetComponentsInChildren<Camera>(true)) { if (c.targetTexture == st.Feed) { cam = c; break; } if (!cam) cam = c; }
            if (!cam) return "no camera";
            float asp = cam.aspect; cam.aspect = 1920f / 1080f;
            var parts = new List<string>();
            foreach (var n in new[] { "mixamorigHead", "HatTop", "mixamorigNeck", "mixamorigSpine2", "mixamorigLeftArm", "mixamorigRightArm", "mixamorigLeftShoulder", "mixamorigRightShoulder" })
            {
                Transform tr = null;
                foreach (var x in st.GetComponentsInChildren<Transform>(true)) if (x.name == n) { tr = x; break; }
                if (!tr) continue;
                var vp = cam.WorldToViewportPoint(tr.position);
                parts.Add(n.Replace("mixamorig", "") + " " + (vp.x * 1920f).ToString("F0") + "," + ((1f - vp.y) * 1080f).ToString("F0") + " d" + vp.z.ToString("F2"));
            }
            cam.aspect = asp;
            return string.Join("; ", parts.ToArray());
        }
    }
}
