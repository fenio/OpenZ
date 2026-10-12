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
    ///  Group A (red heavy + neutral heavy + red jeep / light / medium / MLRS + an empty light tank) and group B (blue heavy + blue
    ///  jeep / light / medium / MLRS) on open ground far apart (nobody fights while posing). Frames at the level's default RTS zoom and
    ///  Then the fire re-check (NB6 / AU7): the red heavy fires at an unkillable blue bag, OzSoundLog heavyshot pairs + a frame burst.
    /// Heat: stops at 86 C held 30 s or 90 C at once (GpuGuard).
    /// </summary>
    public class HeavyLegacyProof : MonoBehaviour
    {
        static readonly string Root = Path.GetFullPath(Path.Combine(Application.dataPath, "../../../.."));
        static readonly string Dir = Path.Combine(Root, "work/records/milestones/HT-heavytank");
        string job; float lastMark; bool done; int errors; float hot = -1f, lastSample = -1f; int peak = -1;
        readonly StringBuilder log = new StringBuilder();
        readonly List<string> verdicts = new List<string>();

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Auto()
        {
            string job = PlayerPrefs.GetString("oz_htproof_auto", "");
            if (string.IsNullOrEmpty(job)) return;
            PlayerPrefs.DeleteKey("oz_htproof_auto"); PlayerPrefs.Save();
            var go = new GameObject("HeavyLegacyProof"); DontDestroyOnLoad(go);
            go.AddComponent<HeavyLegacyProof>().job = job;
        }

        readonly System.Diagnostics.Stopwatch clock = System.Diagnostics.Stopwatch.StartNew();
        float T => (float)clock.Elapsed.TotalSeconds;
        void Mark(string s) { lastMark = T; log.AppendLine($"[{T,7:0.00}] {s}"); Debug.Log("[htproof] " + s); }
        void Verdict(string step, bool ok, string what) { string v = $"{step}: {(ok ? "PASS" : "FAIL")} - {what}"; verdicts.Add(v); Mark("VERDICT " + v); }
        IEnumerator Wait(float s) { float end = T + s; while (T < end && !done) yield return null; }
        void OnEnable() { Application.logMessageReceived += OnLog; }
        void OnDisable() { Application.logMessageReceived -= OnLog; if (!done) Finish("disabled"); }
        void OnLog(string c, string st, LogType type)
        {
            if (c.StartsWith("[htproof]")) return;
            if (type == LogType.Error || type == LogType.Exception) { errors++; log.AppendLine($"[{T,7:0.00}] ERROR {c.Split('\n')[0]}"); }
        }

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
                if (T - lastMark > 90f) { Finish("watchdog: no progress for 90 s"); yield break; }
                yield return new WaitForSecondsRealtime(0.5f);
            }
        }

        ZGame g; ZWorld W; RtsCamera cam; DayNight dn; Camera ucam;
        static void GameViewSize(int idx)
        {
            var gvt = typeof(UnityEditor.EditorWindow).Assembly.GetType("UnityEditor.GameView");
            var p = gvt?.GetProperty("selectedSizeIndex", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            if (p == null) return;
            foreach (var w in Resources.FindObjectsOfTypeAll(gvt)) p.SetValue(w, idx);
        }
        bool Open(int cx, int cy)
        {
            for (int x = cx - 10; x <= cx + 10; x++)
                for (int y = cy - 5; y <= cy + 5; y++)
                    if (!W.Path.TilePassable(x, y, true) || !W.Path.TilePassable(x, y, false)) return false;
            return true;   // each group stands by its own fort (no enemy near), so owned objects are fine
        }
        (int, int) OpenNear(int fx, int fy, int avoidX = -999, int avoidY = -999, int minD = 0)
        {
            for (int r = 0; r <= 60; r++)
                for (int dx = -r; dx <= r; dx++)
                    for (int dy = -r; dy <= r; dy++)
                    {
                        if (Mathf.Max(Mathf.Abs(dx), Mathf.Abs(dy)) != r) continue;
                        int x = fx + dx, y = fy + dy;
                        if (minD > 0 && Mathf.Max(Mathf.Abs(x - avoidX), Mathf.Abs(y - avoidY)) < minD) continue;
                        if (Open(x, y)) return (x, y);
                    }
            return (fx, fy);
        }
        void Focus(int tx, int ty, float h) { var p = ZGame.PxToWorld(tx * 16 + 8, ty * 16 + 8); cam.focus = new Vector3(p.x, 0f, p.z); cam.SetHeightImmediate(h); }

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
            Mark($"briefing over after {T - w0:0.0} s");
        }

        static readonly (string mat, string legacy, string toy)[] Parts =
        {
            ("heavy_tank_HeavyTank_body_PBR", "heavy_tank_body_legacy.png", "heavy_tank_body_toy.png"),
            ("heavy_tank_HeavyTank_head_PBR", "heavy_tank_head_legacy.png", "heavy_tank_head_toy.png"),
            ("heavy_tank_HeavyTank_detail", "swatch_heavy_gunmetal.png", "swatch_khaki_deep.png"),
        };
        static void SetLook(bool legacy)
        {
            foreach (var p in Parts)
            {
                var m = UnityEditor.AssetDatabase.LoadAssetAtPath<Material>("Assets/OpenZ/Materials/Units/" + p.mat + ".mat");
                var t = UnityEditor.AssetDatabase.LoadAssetAtPath<Texture2D>("Assets/OpenZ/Models/Legacy/Toy/" + (legacy ? p.legacy : p.toy));
                if (!m || !t) continue;
                m.SetTexture("_BaseMap", t); m.SetFloat("_LegacyHeavy", legacy ? 1f : 0f);
            }
        }

        // ---------------------------------------------------------------- units
        readonly List<(ZObject o, string tag)> units = new List<(ZObject, string)>();
        ZObject Veh(string type, int tx, int ty, Team t, string tag)
        {
            var o = W.CreateObject("vehicle", type, tx * 16, ty * 16, t); g.SpawnUnitView(o);
            o.MaxHealth = 100000000; o.SetHealth(o.MaxHealth); o.Damage = 0;
            units.Add((o, tag)); return o;
        }
        string Boxes()
        {   // screen boxes (pixels from the top-left) of every posed unit, for the sheet crops
            var sb = new StringBuilder();
            foreach (var (o, tag) in units)
            {
                if (o.Destroyed || !g.Views.TryGetValue(o.Id, out var v) || !v) continue;
                bool any = false; var b = new Bounds();
                foreach (var r in v.GetComponentsInChildren<Renderer>())
                {
                    if (!r.enabled || r is ParticleSystemRenderer || r is TrailRenderer || r is LineRenderer) continue;
                    if (!any) { b = r.bounds; any = true; } else b.Encapsulate(r.bounds);
                }
                if (!any) continue;
                float x0 = 1e9f, y0 = 1e9f, x1 = -1e9f, y1 = -1e9f;
                for (int i = 0; i < 8; i++)
                {
                    var c = b.center + Vector3.Scale(b.extents, new Vector3((i & 1) * 2 - 1, (i & 2) - 1, (i & 4) / 2 - 1));
                    var s = ucam.WorldToScreenPoint(c);
                    x0 = Mathf.Min(x0, s.x); x1 = Mathf.Max(x1, s.x); y0 = Mathf.Min(y0, Screen.height - s.y); y1 = Mathf.Max(y1, Screen.height - s.y);
                }
                sb.Append($"{tag}={x0:0},{y0:0},{x1:0},{y1:0} ");
            }
            return sb.ToString();
        }
        IEnumerator Shot(string name, float settle = 0.5f)
        {
            yield return Wait(settle);
            string f = Path.Combine(Dir, $"{g.level}_{name}.png");
            ScreenCapture.CaptureScreenshot(f); yield return null; yield return null;
            Mark($"frame {Path.GetFileName(f)} {Screen.width}x{Screen.height} cam {cam.height:0} m | {Boxes()}");
        }
        IEnumerator TimeOfDay(float tod) { dn.timeOfDay = tod; dn.Apply(tod); yield return Wait(1.4f); }

        // ---------------------------------------------------------------- the run
        IEnumerator Start()
        {
            Directory.CreateDirectory(Dir); lastMark = T;
            Mark($"HT-heavytank Play proof {job} {DateTime.Now:yyyy-MM-dd HH:mm:ss}, scene {SceneManager.GetActiveScene().name}, oz_testmode {PlayerPrefs.GetInt("oz_testmode", 0)}, screen {Screen.width}x{Screen.height}");
            StartCoroutine(Heat());
            float w0 = T;
            while ((!ZGame.I || ZGame.I.World == null) && T - w0 < 90f) yield return null;
            if (!ZGame.I || ZGame.I.World == null) { Finish("the level never came up within 90 s"); yield break; }
            g = ZGame.I; W = g.World; cam = FindFirstObjectByType<RtsCamera>(); dn = FindFirstObjectByType<DayNight>(); ucam = cam.GetComponent<Camera>(); if (!ucam) ucam = Camera.main;
            float h0 = cam.height;
            Mark($"level up after {T - w0:0.0} s, level {g.level}, default RTS height {h0:0.0} m, pitch {cam.pitch:0}");
            int bots = W.Bots.Count; W.Bots.Clear();
            yield return SkipBriefing();
            if (done) yield break;
            bool run0 = dn.running; dn.running = false;
            Mark($"CPU bots cleared ({bots}); day/night clock stopped");

            var mats = new StringBuilder(); bool matsOk = true;
            foreach (var p in Parts)
            {
                var m = UnityEditor.AssetDatabase.LoadAssetAtPath<Material>("Assets/OpenZ/Materials/Units/" + p.mat + ".mat");
                var tex = m ? m.GetTexture("_BaseMap") : null;
                bool ok = m && tex && tex.name + ".png" == p.legacy && m.GetFloat("_LegacyHeavy") > 0.5f;
                matsOk &= ok; mats.Append($"{p.mat}: {(tex ? tex.name : "none")} _LegacyHeavy {(m ? m.GetFloat("_LegacyHeavy") : -1):0}; ");
            }
            Verdict("materials", matsOk, mats.ToString());

            // open ground picked offline from the Zod grids (refs/zod_maps/*_grid.json: a 13 x 9 tile window with no building, cannon,
            // unit, water or rock and at most one map item; L02 has exactly one, 20,37). Run 1 stood in the decorative creek and
            // under the HUD, run 2 (by the forts) inside the fort. Every posed unit has AttackRadius 0 and Damage 0: nobody fights.
            int ax = g.level == "L02" ? 20 : 92, ay = g.level == "L02" ? 37 : 57;
            Mark($"lineup window centre tile ({ax},{ay})");
            var heavyRed = Veh("heavy", ax - 5, ay - 2, Team.Red, "heavy_red");
            Veh("heavy", ax, ay - 2, Team.Neutral, "heavy_neutral");
            var heavyBlue = Veh("heavy", ax + 5, ay - 2, Team.Blue, "heavy_blue");
            Veh("jeep", ax - 6, ay + 3, Team.Red, "jeep_red"); Veh("light", ax - 2, ay + 3, Team.Red, "light_red");
            Veh("light", ax + 2, ay + 3, Team.Blue, "light_blue"); Veh("missile_launcher", ax + 6, ay + 3, Team.Blue, "mlrs_blue");
            foreach (var (o, tag) in units) o.AttackRadius = 0;
            yield return Wait(2.0f);
            // the HUD covers the right of the screen (selection panel, minimap): the window centre goes ~9 tiles left of the focus at
            // the default zoom, a close-up's tank ~4 tiles left
            yield return TimeOfDay(0.08f);
            foreach (var (legacy, tag) in new[] { (false, "before"), (true, "after") })
            {
                SetLook(legacy); yield return Wait(0.3f);
                Focus(ax + 9, ay, h0); yield return Shot($"A_{tag}_day_default");
                Focus(ax - 1, ay - 1, 32f); yield return Shot($"A_{tag}_day_close_red");
                Focus(ax + 4, ay - 1, 32f); yield return Shot($"A_{tag}_day_close_neutral");
                Focus(ax + 9, ay - 1, 32f); yield return Shot($"A_{tag}_day_close_blue");
                if (done) yield break;
            }
            yield return TimeOfDay(0.62f);
            Focus(ax + 9, ay, h0); yield return Shot("A_after_night_default", 0.8f);
            Focus(ax - 1, ay - 1, 32f); yield return Shot("A_after_night_close_red");
            Focus(ax + 4, ay - 1, 32f); yield return Shot("A_after_night_close_neutral");
            Focus(ax + 9, ay - 1, 32f); yield return Shot("A_after_night_close_blue");
            if (done) yield break;

            // fires at the unkillable blue heavy 5 tiles east. Timed on the SIM clock with no screen capture (run 1: a 2560x1600 PNG
            // burst stretched one pair to 0.89 s of wall time), then a short frame burst
            yield return TimeOfDay(0.08f);
            heavyRed.Damage = heavyRed.U.Damage; heavyRed.AttackRadius = heavyRed.U.attack_radius;
            Mark($"fire check: red heavy at ({ax - 5},{ay - 2}) (attack radius {heavyRed.AttackRadius} px) at the unkillable blue heavy ({ax + 5},{ay - 2})");
            Focus(ax + 4, ay, 32f);
            float t0 = Time.unscaledTime; float fw = T; int seen = 0;
            var simAt = new List<double>(); var clips = new HashSet<string>(); var sbl = new StringBuilder();
            while (T - fw < 16f && !done)
            {
                var es = OzSoundLog.Since(t0); int hs = 0;
                foreach (var e in es) if (e.key == "heavyshot" && e.outcome.StartsWith("PLAY")) hs++;
                for (; seen < hs; seen++) simAt.Add(W.Time);
                yield return null;
            }
            foreach (var e in OzSoundLog.Since(t0))
            {
                if (e.key != "heavyshot") continue;
                sbl.AppendLine($"    {e.t - t0,6:0.00}  {e.clip,-18} {e.outcome} vol {e.vol:0.000} dist {e.dist:0.0}");
                if (e.outcome.StartsWith("PLAY")) clips.Add(e.clip);
            }
            var within = new List<double>(); var between = new List<double>();
            for (int i = 1; i < simAt.Count; i++) { double d = simAt[i] - simAt[i - 1]; (d < 1.2 ? within : between).Add(d); }
            string ws = string.Join(" ", within.ConvertAll(d => d.ToString("0.00"))), bs = string.Join(" ", between.ConvertAll(d => d.ToString("0.00")));
            Mark($"fire window 16 s (no capture): {simAt.Count} heavyshot plays at sim t [{string.Join(" ", simAt.ConvertAll(d => d.ToString("0.00")))}], clips [{string.Join(", ", clips)}]\n{sbl}");
            bool clipsOk = clips.Count > 0; foreach (var c in clips) clipsOk &= c != null && c.StartsWith("heavy-tank-");
            bool pairsOk = within.Count >= 2 && within.TrueForAll(d => d >= 0.25 && d <= 0.75) && between.Count >= 1 && between.TrueForAll(d => d >= 2.5);
            Verdict("fire_one_two", pairsOk, $"sim-clock gaps within a pair [{ws}] s, pair to pair [{bs}] s");
            Verdict("fire_sound", clipsOk, $"clips [{string.Join(", ", clips)}] (the legacy heavy-tank-1..5 = BankHeavyTankShot)");
            Focus(ax - 1, ay - 1, 32f);   // the burst frames centre the FIRING red heavy (L02 run 3 framed the target side, the heavy was off screen)
            int burst = 0; float nextB = -1f; fw = T; int s2 = seen;
            while (T - fw < 7f && burst < 10 && !done)
            {
                int hs = 0; foreach (var e in OzSoundLog.Since(t0)) if (e.key == "heavyshot" && e.outcome.StartsWith("PLAY")) hs++;
                if (hs > s2 && nextB < 0f) nextB = T;
                if (nextB > 0f && T >= nextB)
                {
                    string fn = $"{g.level}_fire_{burst:00}.png"; ScreenCapture.CaptureScreenshot(Path.Combine(Dir, fn));
                    Mark($"frame {fn} {Screen.width}x{Screen.height} cam {cam.height:0} m | {Boxes()}"); burst++; nextB += 0.1f;
                }
                yield return null;
            }
            Mark($"fire burst: {burst} frames from the first shot of the next pair");
            dn.running = run0;
            Finish(null);
        }

        void Finish(string why)
        {
            if (done) return; done = true;
            try
            {
                SetLook(true);
                Mark(why == null ? "DONE" : "STOPPED: " + why);
                log.AppendLine($"errors/exceptions in the Console during the run: {errors}; GPU peak (GpuGuard) {peak} C");
                log.AppendLine("## VERDICTS"); foreach (var v in verdicts) log.AppendLine("  " + v);
                File.WriteAllText(Path.Combine(Dir, job + "_proof_log.txt"), log.ToString());
                GameViewSize(9);
            }
            catch (Exception e) { Debug.LogError("[htproof] finish: " + e); }
            UnityEditor.EditorApplication.isPlaying = false;
        }
    }
}
#endif
