#if UNITY_EDITOR
using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Text;
using OpenZ.Sim;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace OpenZ
{
    /// <summary>
    ///  L02 (full look): skip the level-start briefing, the camera over open sand, then
    ///   A. RobotsV3DetailLineup day (README 7.1) at the Game view size set before Play (2560x1600);
    ///   B. Game view -> 1920x1080 (the approved robot_types_show siblings' size), RobotTypesShow tough + sniper at its 42 m (README 7.3 / 7.5);
    ///   C. the same two types at 20 m, overlay off, plus a burst of frames 2-7 s into each segment (tube on the pad, launch, sniper stances);
    ///   D. GPU-light test mode switched on in the running level, WeaponsV2Check (README 7.3, logic only).
    ///  L17 (full look): the night lineup (README 7.2).
    /// </summary>
    public class RobotsV3DetailProof : MonoBehaviour
    {
        static readonly string Root = Path.GetFullPath(Path.Combine(Application.dataPath, "../../../.."));
        static readonly string Dir = Path.Combine(Root, "work/records/milestones/robots_v3_detail");
        string job; float lastMark; bool done; int errors; float hot = -1f, lastSample = -1f; int peak = -1;
        readonly StringBuilder log = new StringBuilder();
        readonly List<string> verdicts = new List<string>();
        string segType; float segAt = -1f;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Auto()
        {
            string job = PlayerPrefs.GetString("oz_r3d_auto", "");
            if (string.IsNullOrEmpty(job)) return;
            PlayerPrefs.DeleteKey("oz_r3d_auto"); PlayerPrefs.Save();
            var go = new GameObject("RobotsV3DetailProof"); DontDestroyOnLoad(go);
            go.AddComponent<RobotsV3DetailProof>().job = job;
        }

        readonly System.Diagnostics.Stopwatch clock = System.Diagnostics.Stopwatch.StartNew();
        float T => (float)clock.Elapsed.TotalSeconds;
        void Mark(string s) { lastMark = T; log.AppendLine($"[{T,7:0.00}] {s}"); Debug.Log("[r3dproof] " + s); }
        void Verdict(string step, bool ok, string what) { string v = $"{step}: {(ok ? "PASS" : "FAIL")} - {what}"; verdicts.Add(v); Mark("VERDICT " + v); }
        IEnumerator Wait(float s) { float end = T + s; while (T < end && !done) yield return null; }

        void OnEnable() { Application.logMessageReceived += OnLog; }
        void OnDisable() { Application.logMessageReceived -= OnLog; if (!done) Finish("disabled"); }
        void OnLog(string c, string st, LogType type)
        {
            if (c.StartsWith("[r3dproof]")) return;
            if (type == LogType.Error || type == LogType.Exception) { errors++; log.AppendLine($"[{T,7:0.00}] ERROR {c}"); return; }
            if (c.StartsWith("[RobotTypesShow]") || c.StartsWith("[RobotsV3DetailLineup]") || c.StartsWith("[ZodCameo]") || c.StartsWith("OpenZ test mode"))
            {
                string first = c.Split('\n')[0]; log.AppendLine($"[{T,7:0.00}] {first}");
                if (c.StartsWith("[RobotTypesShow] ") && c.Contains("/") && c.Contains(" vs blue robots"))
                {   // "[RobotTypesShow] 1/2  TOUGH  vs blue robots, then a blue tank: ..."
                    var parts = first.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
                    if (parts.Length > 2) { segType = parts[2].ToLowerInvariant(); segAt = T; }
                }
            }
        }

        // ---------------------------------------------------------------- heat
        static int GpuTemp()
        {
            foreach (var a in AppDomain.CurrentDomain.GetAssemblies())
            {
                var t = a.GetType("OpenZ.EditorTools.GpuGuard"); if (t == null) continue;
                var p = t.GetProperty("LastTemp"); return p != null ? (int)p.GetValue(null) : -1;
            }
            return -1;
        }
        IEnumerator Heat()
        {
            while (!done)
            {
                int gpu = GpuTemp(); peak = Mathf.Max(peak, gpu);
                float step = lastSample < 0f ? 0.5f : T - lastSample; lastSample = T;
                if (gpu >= 90) { Finish("GPU 90 C"); yield break; }
                if (gpu >= 86) { hot = hot < 0f ? step : hot + step; if (hot >= 30f) { Finish("GPU >= 86 C for 30 s"); yield break; } }
                else if (gpu >= 0 && gpu < 84) hot = -1f;
                if (T - lastMark > 120f) { Finish("watchdog: no progress for 120 s"); yield break; }
                yield return new WaitForSecondsRealtime(0.5f);
            }
        }

        // ---------------------------------------------------------------- helpers
        ZGame g; ZWorld W; RtsCamera cam;
        static void GameViewSize(int idx)
        {
            var gvt = typeof(UnityEditor.EditorWindow).Assembly.GetType("UnityEditor.GameView");
            var p = gvt?.GetProperty("selectedSizeIndex", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            if (p == null) return;
            foreach (var w in Resources.FindObjectsOfTypeAll(gvt)) p.SetValue(w, idx);
        }
        bool Open(int cx, int cy)
        {
            for (int x = cx - 9; x <= cx + 9; x++)
                for (int y = cy - 4; y <= cy + 4; y++)
                    if (!W.Path.TilePassable(x, y, true) || !W.Path.TilePassable(x, y, false)) return false;   // dry, open ground (no water, no rocks)
            foreach (var o in W.Objects)
            {
                if (o == null || o.Destroyed || o.Owner == Team.Neutral) continue;
                if (Mathf.Abs(o.CenterX / 16 - cx) <= 12 && Mathf.Abs(o.CenterY / 16 - cy) <= 12) return false;
            }
            return true;
        }
        (int, int) OpenNear(int fx, int fy)
        {
            for (int r = 0; r <= 50; r++)
                for (int dx = -r; dx <= r; dx++)
                    for (int dy = -r; dy <= r; dy++)
                        if (Mathf.Max(Mathf.Abs(dx), Mathf.Abs(dy)) == r && Open(fx + dx, fy + dy)) return (fx + dx, fy + dy);
            return (fx, fy);
        }
        void Focus(int tx, int ty) { if (!cam) return; var p = ZGame.PxToWorld(tx * 16 + 8, ty * 16 + 8); cam.focus = new Vector3(p.x, 0f, p.z); cam.SetHeightImmediate(cam.height); }
        ZObject Fort(Team t) { foreach (var o in W.Objects) if (o.IsFort && o.Owner == t && !o.Destroyed) return o; return null; }

        IEnumerator SkipBriefing()
        {
            float w0 = T; bool aborted = false; var pf = typeof(ZodBriefing).GetField("perf", BindingFlags.NonPublic | BindingFlags.Instance);
            double sim0 = W.Time;
            while (T - w0 < 45f && !done)
            {
                if (ZodBriefing.BriefingActive)
                {
                    if (!aborted && ZodBriefing.I && pf != null && T - w0 > 0.6f)
                    {
                        var p = pf.GetValue(ZodBriefing.I);
                        if (p != null) { p.GetType().GetMethod("Abort").Invoke(p, new object[] { 0.22f }); aborted = true; Mark("briefing: Abort(0.22 s) sent"); }
                    }
                }
                else if (!g.paused && W.Time > sim0 + 0.5) break;
                yield return null;
            }
            Mark($"briefing over after {T - w0:0.0} s (active {ZodBriefing.BriefingActive}, paused {g.paused}, sim {W.Time:0.0} s)");
        }

        List<string> MoveShow(string stamp, string prefix)
        {   // RobotTypesShow names its frames by HHmm: move them into Dir under a pass prefix, so the next pass cannot overwrite them
            var moved = new List<string>();
            var src = Path.Combine(Root, "work/records/milestones");
            foreach (var f in Directory.GetFiles(src, $"robot_types_show_{g.level}_{stamp}_*.png"))
            {
                string dst = Path.Combine(Dir, prefix + "_" + Path.GetFileName(f));
                try { if (File.Exists(dst)) File.Delete(dst); File.Move(f, dst); moved.Add(Path.GetFileName(dst)); } catch (Exception e) { Mark("move failed " + e.Message); }
            }
            var qc = Path.Combine(Root, "work/records/qc", $"robot_types_show_{g.level}_{stamp}.md");
            if (File.Exists(qc)) File.Copy(qc, Path.Combine(Dir, prefix + "_" + Path.GetFileName(qc)), true);
            return moved;
        }

        IEnumerator Show(string prefix, float height, bool overlay, bool burst)
        {
            RobotTypesShow.Order = new[] { "tough", "sniper" };
            RobotTypesShow.CameraHeight = height; RobotTypesShow.Overlay = overlay;
            var s = RobotTypesShow.Begin(false);
            if (!s) { Verdict(prefix, false, "RobotTypesShow did not start"); yield break; }
            string stamp = DateTime.Now.ToString("HHmm");
            Mark($"{prefix}: RobotTypesShow started ({string.Join(", ", RobotTypesShow.Order)}), camera {height:0} m, overlay {overlay}, stamp {stamp}, screen {Screen.width}x{Screen.height}");
            segAt = -1f; string lastSeg = null; int n = 0; float nextBurst = 0f;
            float w0 = T;
            while (!RobotTypesShow.Running && T - w0 < 3f && !done) yield return null;               // Running is set in its Start (next frame)
            w0 = T;
            while (RobotTypesShow.Running && T - w0 < 70f && !done)
            {
                if (burst && segAt > 0f && segType != null)
                {
                    if (segType != lastSeg) { lastSeg = segType; n = 0; nextBurst = segAt + 2.0f; }
                    if (n < 11 && T >= nextBurst)
                    {
                        string name = $"{prefix}_{g.level}_{segType}_{n:00}_{T - segAt:0.0}s.png";
                        ScreenCapture.CaptureScreenshot(Path.Combine(Dir, name)); n++; nextBurst += 0.5f; lastMark = T;
                    }
                }
                yield return null;
            }
            yield return Wait(0.6f);
            var moved = MoveShow(stamp, prefix);
            string rep = Path.Combine(Dir, $"{prefix}_robot_types_show_{g.level}_{stamp}.md");
            string res = File.Exists(rep) ? File.ReadAllText(rep) : "";
            int i = res.IndexOf("RESULT:", StringComparison.Ordinal);
            string line = i >= 0 ? res.Substring(i, Mathf.Min(60, res.Length - i)).Split('\n')[0] : "no report";
            Verdict(prefix, res.Contains("RESULT: PASS"), $"{line}; {moved.Count} show frames moved; {(RobotTypesShow.Running ? "still running at 70 s" : "ended")}");
        }

        // ---------------------------------------------------------------- the run
        IEnumerator Start()
        {
            Directory.CreateDirectory(Dir); lastMark = T;
            Mark($"robots_v3_detail Play proof {job} {DateTime.Now:yyyy-MM-dd HH:mm:ss}, scene {SceneManager.GetActiveScene().name}, oz_testmode {PlayerPrefs.GetInt("oz_testmode", 0)}, screen {Screen.width}x{Screen.height}");
            StartCoroutine(Heat());
            float w0 = T;
            while ((!ZGame.I || ZGame.I.World == null) && T - w0 < 90f) yield return null;
            if (!ZGame.I || ZGame.I.World == null) { Finish("the level never came up within 90 s"); yield break; }
            g = ZGame.I; W = g.World; cam = FindFirstObjectByType<RtsCamera>();
            Mark($"level up after {T - w0:0.0} s, level {g.level}");
            int bots = W.Bots.Count; W.Bots.Clear();
            yield return SkipBriefing();
            if (done) yield break;
            yield return Wait(1.5f);
            Mark($"CPU bots cleared ({bots})");

            int fx, fy;
            if (job == "L02") { fx = 45; fy = 52; }   // the approved RobotTypesShow stage (qc/robot_types_show_L02_0550.md: stage centre tile 45,52)
            else { var a = Fort(Team.Red); var b = Fort(Team.Blue); fx = (a.CenterX + b.CenterX) / 32; fy = (a.CenterY + b.CenterY) / 32; }
            var (cx, cy) = OpenNear(fx, fy);
            Focus(cx, cy);
            Mark($"camera over open ground at tile ({cx},{cy}) (asked {fx},{fy}), height {(cam ? cam.height : -1f):0} m");
            yield return Wait(1.0f);

            // A. the lineup (README 7.1 day / 7.2 night)
            bool night = job != "L02";
            var lu = RobotsV3DetailLineup.Begin(night);
            if (!lu) { Verdict("lineup", false, "did not start"); }
            else
            {
                Mark($"lineup {(night ? "night" : "day")} started at {Screen.width}x{Screen.height}");
                w0 = T; while (!RobotsV3DetailLineup.Running && T - w0 < 3f && !done) yield return null;   // Running is set in its Start (next frame)
                w0 = T; while (RobotsV3DetailLineup.Running && T - w0 < 40f && !done) yield return null;
                Verdict("lineup", !RobotsV3DetailLineup.Running, $"{(night ? "night" : "day")} lineup {(RobotsV3DetailLineup.Running ? "still running at 40 s" : "done")} (report work/records/qc/robots_v3_detail_lineup_{g.level}_*.md)");
            }
            if (done) yield break;
            if (job != "L02") { Finish(null); yield break; }

            // B. + C. the Tough firing and the sniper's stances
            GameViewSize(9); yield return Wait(1.2f);
            Mark($"Game view -> index 9, screen {Screen.width}x{Screen.height}");
            Focus(cx, cy);
            yield return Show("B42", 42f, true, false);
            if (done) yield break;
            yield return Wait(1f);
            Focus(cx, cy);
            yield return Show("C20", 20f, false, true);
            if (done) yield break;
            RobotTypesShow.CameraHeight = 42f; RobotTypesShow.Overlay = true;

            // D. logic only: the GPU-light test mode inside the running level, then the weapons check
            PlayerPrefs.SetInt("oz_testmode", 1);
            try { typeof(GameBoot).GetMethod("ApplyTestMode", BindingFlags.NonPublic | BindingFlags.Static).Invoke(null, null); } catch (Exception e) { Mark("test mode switch failed: " + e.Message); }
            Application.targetFrameRate = 30;
            Focus(cx, cy);
            yield return Wait(1f);
            var chk = WeaponsV2Check.Begin();
            if (!chk) Verdict("weapons_v2", false, "did not start");
            else
            {
                Mark("weapons_v2 check started (test mode, 30 fps)");
                w0 = T; while (chk && chk.Phase != "done" && T - w0 < 60f && !done) { if (T - lastMark > 20f) lastMark = T; yield return null; }
                yield return Wait(1.5f);
                string rp = Path.Combine(Root, "work/records/weapons_v2_check.md");
                string txt = File.Exists(rp) ? File.ReadAllText(rp) : "";
                if (txt.Length > 0) File.Copy(rp, Path.Combine(Dir, "L02_weapons_v2_check.md"), true);
                string toughRow = ""; foreach (var l in txt.Split('\n')) if (l.StartsWith("| tough")) toughRow = l;
                int fails = 0; foreach (var l in txt.Split('\n')) if (l.StartsWith("| ") && l.Contains("**FAIL**")) fails++;
                Verdict("weapons_v2", txt.Contains(DateTime.Now.ToString("yyyy-MM-dd")) && fails == 0 && toughRow.TrimEnd().EndsWith("PASS |"),
                        $"{fails} FAIL rows; tough row: {(toughRow.Length > 400 ? toughRow.Substring(0, 400) + "..." : toughRow)}");
            }
            Finish(null);
        }

        void Finish(string why)
        {
            if (done) return; done = true;
            try
            {
                Mark(why == null ? "DONE" : "STOPPED: " + why);
                log.AppendLine($"errors/exceptions in the Console during the run: {errors}; GPU peak (GpuGuard) {peak} C");
                log.AppendLine("## VERDICTS"); foreach (var v in verdicts) log.AppendLine("  " + v);
                File.WriteAllText(Path.Combine(Dir, job + "_proof_log.txt"), log.ToString());
                PlayerPrefs.SetInt("oz_testmode", 0); PlayerPrefs.Save();
                RobotTypesShow.CameraHeight = 42f; RobotTypesShow.Overlay = true;
                RobotTypesShow.Order = new[] { "grunt", "psycho", "tough", "sniper", "pyro", "laser" };
                GameViewSize(9);
            }
            catch (Exception e) { Debug.LogError("[r3dproof] finish: " + e); }
            UnityEditor.EditorApplication.isPlaying = false;
        }
    }
}
#endif
