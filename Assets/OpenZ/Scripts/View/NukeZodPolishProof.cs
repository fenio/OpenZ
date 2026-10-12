using System.Collections.Generic;
using System.IO;
using UnityEngine;
using UnityEngine.Rendering.Universal;

namespace OpenZ
{
    /// <summary>
    /// <c></c>
    /// nuke line (ZodBriefing.DebugNuke: no strike is launched) and saves close frames of the red-button press from the
    /// transmission camera (1280 x 960) at fixed stage times with ZodStage.PressStatus; <c>OpenZ.NukeZodPolishProof.Nuke(true, "new")</c>
    /// fires a NukeFx at the RTS camera focus and saves the RTS view at 0.7 / 1.3 s (shockwave distortion) and the RTS + a close
    /// view at 3 / 6 / 10 s, logging the mushroom / shockwave / sound state. false = the old look (press v1, no puff mushroom, no
    /// </summary>
    [DefaultExecutionOrder(2000)]
    public sealed class NukeZodPolishProof : MonoBehaviour
    {
        public static string Dir = global::OpenZ.OzRelease.Milestones("task_nuke_zod_polish");
        static NukeZodPolishProof run;
        public static string Last = ""; public static float CloseOut = 55f, CloseUp = 30f, CloseLookUp = 22f;
        public static bool Busy => run != null;

        enum Job { Press, Nuke }
        Job job; string tag; bool v2; float t0; int next; float[] times; Vector3 at; Camera close; float soundT0;
        readonly List<string> log = new List<string>();

        static void Log(string s) { Directory.CreateDirectory(Dir); File.AppendAllText(Dir + "/proof_log.txt", System.DateTime.Now.ToString("HH:mm:ss") + " " + s + "\n"); Debug.Log("[NukeZodPolishProof] " + s); Last = s; }

        public static string Press(bool newPress, string tag)
        {
            if (run) return "busy";
            ZodStage.PressV2 = newPress;
            string r = ZodBriefing.DebugNuke();
            var go = new GameObject("NukeZodPolishProof"); run = go.AddComponent<NukeZodPolishProof>();
            run.job = Job.Press; run.tag = tag; run.v2 = newPress; run.t0 = Time.unscaledTime; run.times = new[] { 0.14f, 0.30f, 0.37f, 0.45f, 0.62f, 0.86f, 0.98f }; run.soundT0 = Time.unscaledTime;
            Log("press " + tag + " (" + (newPress ? "v2" : "v1") + "): " + r);
            return r;
        }

        public static string Nuke(bool newLook, string tag, float side = 0f)
        {
            if (run) return "busy";
            OpenZNukeMushroom.Enabled = newLook; OpenZFortShockwave.GroundDistort = newLook;
            var rts = FindFirstObjectByType<RtsCamera>();
            var c = rts ? rts.focus : Vector3.zero;
            if (Camera.main) { var f = Vector3.ProjectOnPlane(Camera.main.transform.right, Vector3.up).normalized; c += f * side; }
            c.y = ZGame.I ? ZGame.I.GroundY(c.x, c.z) : c.y;
            var go = new GameObject("NukeZodPolishProof"); run = go.AddComponent<NukeZodPolishProof>();
            run.job = Job.Nuke; run.tag = tag; run.v2 = newLook; run.at = c; run.t0 = Time.time; run.times = new[] { 0.7f, 1.3f, 3f, 6f, 10f }; run.soundT0 = Time.unscaledTime;
            NukeFx.Trigger(c);
            Log("nuke " + tag + " (" + (newLook ? "puff mushroom + ground distortion" : "old look") + ") at " + c.ToString("F0") + ", RTS height " + (rts ? rts.height.ToString("F0") : "?"));
            return "nuke at " + c;
        }

        void LateUpdate()
        {
            if (job == Job.Press)
            {
                var st = FindFirstObjectByType<ZodStage>();
                if (!st || Time.unscaledTime - t0 > 25f) { Log("press " + tag + ": timeout / no stage"); Done(); return; }
                if (!st.PressPlaced || st.PressU > 3f || st.PressU < 0f) return;
                while (next < times.Length && st.PressU >= times[next])
                {
                    var tex = st.CaptureFeed(1280, 960);
                    string f = Dir + "/zod_press_" + tag + "_" + ((int)(times[next] * 100f)).ToString("D3") + ".png";
                    if (tex) { Directory.CreateDirectory(Dir); File.WriteAllBytes(f, tex.EncodeToPNG()); Destroy(tex); }
                    Log("press " + tag + " frame " + Path.GetFileName(f) + ": " + st.PressStatus());
                    next++;
                }
                if (next >= times.Length)
                {
                    var snd = OzSoundLog.Since(soundT0 - 0.5f); var lines = new List<string>();
                    foreach (var e in snd) { var s = OzSoundLog.Format(new List<OzSoundLog.Entry> { e }); if (s.Contains("Zod") || s.Contains("button")) lines.Add(s.Trim()); }
                    Log("press " + tag + " sounds: " + string.Join(" | ", lines.ToArray()));
                    Done();
                }
                return;
            }
            float t = Time.time - t0;
            while (next < times.Length && t >= times[next])
            {
                string ts = times[next].ToString("F1").Replace('.', '_');
                Shot(Camera.main, Dir + "/nuke_" + tag + "_rts_" + ts + ".png", 1920, 1200);
                if (times[next] >= 3f) Shot(Close(), Dir + "/nuke_" + tag + "_close_" + ts + ".png", 1920, 1080);
                var m = OpenZNukeMushroom.Current;
                Log("nuke " + tag + " " + times[next].ToString("F1") + " s: " + (m ? m.Status(Camera.main) : "no puff mushroom") + "; ground ring " + OpenZFortShockwave.LastGroundRing.ToString("F3") +
                    ", area shimmer " + OpenZFortShockwave.LastAreaShimmer.ToString("F3") + ", NukeFx " + (NukeFx.Current ? "on, gain " + NukeFx.Current.GlowGain.ToString("F2") : "gone") + ", fps " + (1f / Mathf.Max(1e-4f, Time.smoothDeltaTime)).ToString("F0"));
                next++;
            }
            if (next >= times.Length)
            {
                var snd = OzSoundLog.Since(soundT0 - 0.2f); var cnt = new SortedDictionary<string, int>();
                foreach (var e in snd) { string k = e.key + "/" + (e.clip ?? "-") + " " + e.outcome; int n; cnt.TryGetValue(k, out n); cnt[k] = n + 1; }
                var parts = new List<string>(); foreach (var kv in cnt) parts.Add(kv.Key + " x" + kv.Value);
                Log("nuke " + tag + " sounds (" + snd.Count + "): " + string.Join(", ", parts.ToArray()));
                Done();
            }
        }

        Camera Close()
        {
            if (close) return close;
            var main = Camera.main;
            var go = new GameObject("NukeProofCloseCam"); close = go.AddComponent<Camera>();
            if (main) close.CopyFrom(main);
            close.enabled = false; close.fieldOfView = 48f; close.farClipPlane = 2000f;
            var d = main ? Vector3.ProjectOnPlane(main.transform.position - at, Vector3.up).normalized : Vector3.back;
            if (d.sqrMagnitude < 1e-4f) d = Vector3.back;
            go.transform.position = at + d * CloseOut + Vector3.up * CloseUp;
            go.transform.LookAt(at + Vector3.up * CloseLookUp);
            var src = main ? main.GetUniversalAdditionalCameraData() : null; var dst = close.GetUniversalAdditionalCameraData();
            if (src && dst) { dst.renderPostProcessing = src.renderPostProcessing; dst.antialiasing = src.antialiasing; dst.antialiasingQuality = src.antialiasingQuality; dst.volumeLayerMask = src.volumeLayerMask; dst.requiresDepthTexture = true; dst.requiresColorTexture = true; }
            return close;
        }

        static void Shot(Camera c, string file, int w, int h)
        {
            if (!c) return;
            var prev = c.targetTexture;
            var rt = RenderTexture.GetTemporary(w, h, 24, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB);
            c.targetTexture = rt; c.Render();
            var act = RenderTexture.active; RenderTexture.active = rt;
            var tex = new Texture2D(w, h, TextureFormat.RGB24, false); tex.ReadPixels(new Rect(0, 0, w, h), 0, 0); tex.Apply();
            RenderTexture.active = act; c.targetTexture = prev; RenderTexture.ReleaseTemporary(rt);
            Directory.CreateDirectory(Path.GetDirectoryName(file)); File.WriteAllBytes(file, tex.EncodeToPNG()); Destroy(tex);
        }

        void Done() { if (close) Destroy(close.gameObject); run = null; Destroy(gameObject); }
    }
}
