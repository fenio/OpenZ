using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using OpenZ.Sim;
using Unity.Profiling;
using Unity.Profiling.LowLevel.Unsafe;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace OpenZ.EditorTools
{
    /// <summary>
    /// The measuring tool of the lighter MEDIUM / LOW presets (View/OzLitePresets.cs, View/OzLiteTable.cs). Editor only.
    /// Menu: OpenZ/V1.1/Measure Lighter Presets (f41, Play mode), in a level, Game view at the size to be judged.
    /// PLAY MUST HAVE BEEN STARTED WITH GRAPHICS QUALITY ON HIGH: only then does HIGH's own shadow reach follow the view in the
    /// session (the game's rule drives the settings file that was in force at the start); the tool looks and stops if not.
    /// What it does, by itself, in about 8 to 12 minutes per level:
    ///  1. brings the game to 2:00 on the game clock (at speed 4 if it is earlier) and lets the rules stand still there, so HIGH,
    ///     MEDIUM and LOW are taken on exactly the same picture; day / night clock stopped;
    ///  2. THE HITCH TEST FIRST: 240 free-running frames on HIGH at the start view. More than one frame over 2.5 x the median, or
    ///     one over 100 ms, and it stops: "HITCH, nothing measured" (numbers taken in a hitching editor are worth nothing);
    ///  3. THE TABLE: three fixed views (the fort from 70 m by day, the map's middle from 120 m by day, the whole map from 230 m
    ///     in the level's heaviest moment: Car Park night storm, Jungle rain, Desert night; on the Jungle also the whole map at
    ///     night). Per view HIGH, MEDIUM, LOW and HIGH again (the second HIGH shows how far the machine drifted): 300 frames
    ///     after 120, the frame rate running free only for those frames; main thread ms and graphics ms from the engine's frame
    ///     timing, the frame time, batches, lights. And beside MEDIUM and beside LOW two windows of THE PRESET AS IT IS TODAY:
    ///     "started on it" (every cut off: today a game started on a preset lets its shadows follow the view like HIGH's) and
    ///     "picked during play" (every cut off and the reach held at the fixed number of the preset's settings file, which is
    ///     what a preset picked in the Options has today). The kit is printed against both: a preset must not cost more than
    ///     it does today. One full-screen picture per window, and of two close views more (the fort from 22 m by day, from
    ///     40 m at night) for the eye only;
    ///  4. WHERE THE MAIN THREAD GOES: on HIGH and LOW in the worst view, the engine's own phases of a frame and the game's
    ///     scripts, the dearest first (as far as this editor offers them);
    ///  5. EACH CUT ALONE: in the worst view (on the Jungle also at night), MEDIUM and LOW, every cut that lowers something on
    ///     that preset is switched off by itself between control windows; what the frame costs more without it is that cut's gain. KEEP if it pays
    ///     OzLiteTable.LeastMainMs on the main thread or LeastGpuMs on the graphics chip, else DROP;
    ///  6. one window on HIGH with the rules running, to show what the rules themselves cost there.
    /// It ends with one log line, writes measure_LEVEL.txt and measure_LEVEL.csv and the pictures into Folder (by default the
    /// the speed, the clock, the camera, the weather, the frame limit and the two limits of the game's shadow reach rule, also
    /// when a step throws or Play stops.
    /// The graphics chip gets hot when the frame rate runs free: between windows it waits at 30 frames a second while the
    /// chip is at or over HoldAtC, until it is under CoolC (each wait is counted in the report).
    /// </summary>
    public static class OzV11LitePresetsMeasure
    {
        const string Tag = "[OpenZ lighter presets]";
        public static string Report = "not run";
        /// <summary></summary>
        public static string Folder;
        public static int Settle = 120, Frames = 300, HitchFrames = 240, CutSettle = 90;
        /// <summary>A window never starts sooner than this after its change (s): night lights fade for 0.35 s after a count of their
        /// budget (five a second), rain drops live about a second; a fast machine runs the settle frames in less.</summary>
        public static float SettleSeconds = 1.2f;
        public static float StartClock = 120f, DayTod = 0.08f, NightTod = 0.62f;
        public static int HoldAtC = 80, CoolC = 74;
        /// <summary>True = measure although the hitch test failed (the report then says so in its first line).</summary>
        public static bool SkipHitchTest = false;
        /// <summary>The control windows of a cut may differ by this much (ms, main thread) before its gain counts as not valid.</summary>
        public static float ControlSpreadMs = 0.20f;

        static readonly Stack<IEnumerator> stack = new Stack<IEnumerator>();
        static int waitFrame; static float waitUntil;
        static readonly StringBuilder text = new StringBuilder(), csv = new StringBuilder();
        static string dir, levelName = "";
        static bool withCuts;

        // ------------------------------------------------------------------ menus
        [MenuItem("OpenZ/V1.1/Measure Lighter Presets (f41, Play mode)")]
        public static void Run() { Begin(true); }
        [MenuItem("OpenZ/V1.1/Measure Lighter Presets, table only (f41, Play mode)")]
        public static void RunTable() { Begin(false); }

        static void Begin(bool cuts)
        {
            if (!EditorApplication.isPlaying || !ZGame.I || ZGame.I.World == null) { Debug.LogWarning(Tag + " enter Play mode in a level first"); return; }
            if (stack.Count > 0) { Debug.LogWarning(Tag + " a run is still going"); return; }
            withCuts = cuts; text.Clear(); csv.Clear(); Report = "running"; dir = OutDir(); levelName = ZGame.I.level;
            csv.Append("level,view,preset,cuts_off,frames,main_ms,player_loop_ms,gpu_ms,cpu_ms,render_thread_ms,present_wait_ms,frame_ms,median_frame_ms,worst_frame_ms,hitch_frames,batches,setpass,triangles,shadow_casters,lights_on,lights_shadowed,rule_ms,gpu_c,game_clock_s,frame_limit\n");
            stack.Push(RunAll());
            waitFrame = 0; waitUntil = 0f; EditorApplication.update += Step;
        }

        static void Step()
        {
            if (stack.Count == 0 || !EditorApplication.isPlaying) { stack.Clear(); PutBack(); EditorApplication.update -= Step; return; }
            if (Time.frameCount < waitFrame || Time.realtimeSinceStartup < waitUntil) return;
            var top = stack.Peek(); bool more;
            try { more = top.MoveNext(); }
            catch (System.Exception ex) { Debug.LogException(ex); stack.Clear(); PutBack(); Report = Tag + " FAIL: exception " + ex.Message; Debug.LogError(Report); EditorApplication.update -= Step; return; }
            if (!more) { stack.Pop(); if (stack.Count == 0) EditorApplication.update -= Step; return; }
            if (top.Current is IEnumerator child) stack.Push(child);
            else if (top.Current is float s) waitUntil = Time.realtimeSinceStartup + s;       // seconds, real time
            else waitFrame = Time.frameCount + (top.Current is int n ? n : 1);                // frames
        }

        /// <summary>The folder of this kit's records (also used by the counted check).</summary>
        public static string OutDir()
        {
            string d;
            if (!string.IsNullOrEmpty(Folder)) d = Path.GetFullPath(Folder);
            else
            {
                string all = Path.GetFullPath(Path.Combine(Application.dataPath, "../../../../work/records/milestones")), stage = "v1_1";
                if (Directory.Exists(all))
                {
                    var ds = Directory.GetDirectories(all, "v1_1*");
                    System.Array.Sort(ds, System.StringComparer.Ordinal);
                    if (ds.Length > 0) stage = Path.GetFileName(ds[ds.Length - 1]);
                }
                d = Path.Combine(all, stage, "f41_lighter_presets");
            }
            Directory.CreateDirectory(d);
            return d;
        }

        // ------------------------------------------------------------------ what is kept and put back
        static bool kept, paused0, running0, hadDn; static int level0, vsync0, fps0, cap0; static float speed0, clock0, camH0; static Vector3 camF0;
        static bool freeOn, rained, reachKept; static float reachMin0, reachMax0;
        static readonly float[] fileReach = new float[3];

        static void Keep()
        {
            var g = ZGame.I; var dn = Object.FindFirstObjectByType<DayNight>(); var rts = Object.FindFirstObjectByType<RtsCamera>();
            level0 = OzQuality.Level; paused0 = g.paused; speed0 = g.gameSpeed; hadDn = dn; running0 = dn && dn.running; clock0 = dn ? dn.timeOfDay : 0f;
            camF0 = rts ? rts.focus : Vector3.zero; camH0 = rts ? rts.height : 70f; rained = false; kept = true;
            reachMin0 = ShadowDistanceByHeight.Min; reachMax0 = ShadowDistanceByHeight.Max; reachKept = true;
        }

        /// <summary>The shadow reach a preset's own settings file holds on disk (m): the reach of that preset today when it is picked during play.</summary>
        static float FileReach(int p)
        {
            string f = Path.Combine(Path.GetDirectoryName(Application.dataPath) ?? "", "Assets/OpenZ/Resources/" + OzQuality.ResDir + (p == 1 ? OzQuality.MediumAsset : OzQuality.LowAsset) + ".asset");
            if (File.Exists(f))
            {
                var m = System.Text.RegularExpressions.Regex.Match(File.ReadAllText(f), @"^\s*m_ShadowDistance:\s*(\S+)\s*$", System.Text.RegularExpressions.RegexOptions.Multiline);
                if (m.Success && float.TryParse(m.Groups[1].Value, NumberStyles.Float, CultureInfo.InvariantCulture, out float d) && d > 0f) return d;
            }
            return OzLiteTable.ReachFile[p];
        }

        /// <summary>For one window on MEDIUM / LOW: the preset as it is TODAY. Every cut off = today's preset in a game started on it
        /// (its shadows follow the view like HIGH's). With fixedReach > 0 the reach is also held at that number = today's preset
        /// picked in the Options during play. on = false puts the kit's own state back.</summary>
        static void Today(bool on, float fixedReach)
        {
            bool hold = on && fixedReach > 0f;
            if (reachKept) { ShadowDistanceByHeight.Min = hold ? fixedReach : reachMin0; ShadowDistanceByHeight.Max = hold ? fixedReach : reachMax0; }
            if (OzLite.Active) OzLite.SetOff(on ? ~0 : 0);                                     // also starts the reach fresh
        }

        static void PutBack()
        {
            FreeRun(false);
            if (reachKept) { reachKept = false; ShadowDistanceByHeight.Min = reachMin0; ShadowDistanceByHeight.Max = reachMax0; }
            if (!kept) return; kept = false;
            if (EditorApplication.isPlaying)
            {
                if (OzLite.Active || OzLite.Off != 0) OzLite.SetOff(0);
                if (OzQuality.Level != level0) OzQuality.Level = level0;
                if (rained) StopRain();
                var g = ZGame.I; if (g) { g.paused = paused0; g.gameSpeed = speed0; }
                var dn = Object.FindFirstObjectByType<DayNight>(); if (dn && hadDn) { dn.running = running0; dn.timeOfDay = clock0; dn.Apply(clock0); }
                var rts = Object.FindFirstObjectByType<RtsCamera>(); if (rts) { rts.focus = camF0; rts.SetHeightImmediate(camH0); }
            }
            else { PlayerPrefs.SetInt(OzQuality.Pref, level0); PlayerPrefs.Save(); }           // Play stopped half way: only the saved preset is left to put back
        }

        /// <summary>The frame rate runs free while a window is measured, and only then.</summary>
        static void FreeRun(bool on)
        {
            if (on == freeOn) return; freeOn = on;
            if (on) { vsync0 = QualitySettings.vSyncCount; fps0 = Application.targetFrameRate; cap0 = GameBoot.CapFps; QualitySettings.vSyncCount = 0; Application.targetFrameRate = -1; GameBoot.CapFps = -1; }
            else { QualitySettings.vSyncCount = vsync0; Application.targetFrameRate = fps0; GameBoot.CapFps = cap0; }
        }

        static void StopRain() { if (OzWeather.I) OzWeather.I.StopStorm(1f); if (OzJungleWeather.I) OzJungleWeather.I.StopSpell(1f); rained = false; }

        // ------------------------------------------------------------------ one measured window
        sealed class Win
        {
            public string view = "", preset = "", cutsOff = "", label = "";
            public int frames, hitches, lights, shadowed, temp, limit;
            public float main, loop, gpu, cpu, render, wait, frame, median, worst, batches, setpass, tris, casters, ruleMs, clock;
        }
        static readonly List<float> dts = new List<float>(512), mains = new List<float>(512), gpus = new List<float>(512), cpus = new List<float>(512),
            renders = new List<float>(512), waits = new List<float>(512), loops = new List<float>(512);
        static readonly FrameTiming[] ft = new FrameTiming[1];
        static int holds;

        static float Median(List<float> l)
        {
            if (l.Count == 0) return 0f;
            var c = new List<float>(l); c.Sort();
            return c[c.Count / 2];
        }
        /// <summary>Mean without the values over 2.5 x the median (a stall of the editor is not the preset's cost).</summary>
        static float Mean(List<float> l)
        {
            if (l.Count == 0) return 0f;
            float med = Median(l), lim = med * 2.5f; double s = 0; int n = 0;
            for (int i = 0; i < l.Count; i++) if (med <= 0f || l[i] <= lim) { s += l[i]; n++; }
            return n > 0 ? (float)(s / n) : 0f;
        }

        static IEnumerator Cool()
        {
            int t = GpuGuard.LastTemp;
            if (t < HoldAtC) yield break;
            holds++; FreeRun(false);
            int fpsWas = Application.targetFrameRate; Application.targetFrameRate = 30;
            float until = Time.realtimeSinceStartup + 150f;
            while (GpuGuard.LastTemp >= CoolC && Time.realtimeSinceStartup < until) yield return 1f;
            Application.targetFrameRate = fpsWas;
        }

        static IEnumerator Measure(Win w, int settle, int frames)
        {
            yield return Cool();
            var recLoop = new ProfilerRecorder("PlayerLoop", 1, ProfilerRecorderOptions.Default); recLoop.Start();
            var recBatch = ProfilerRecorder.StartNew(ProfilerCategory.Render, "Batches Count");
            var recSet = ProfilerRecorder.StartNew(ProfilerCategory.Render, "SetPass Calls Count");
            var recTri = ProfilerRecorder.StartNew(ProfilerCategory.Render, "Triangles Count");
            var recCast = ProfilerRecorder.StartNew(ProfilerCategory.Render, "Shadow Casters Count");
            FreeRun(true);
            int f0 = Time.frameCount; float t0 = Time.realtimeSinceStartup;
            while (Time.frameCount - f0 < settle || Time.realtimeSinceStartup - t0 < SettleSeconds) yield return 1;
            dts.Clear(); mains.Clear(); gpus.Clear(); cpus.Clear(); renders.Clear(); waits.Clear(); loops.Clear();
            double b = 0, sp = 0, tr = 0, ca = 0, rule = 0; int n = 0, rn = 0, last = Time.frameCount;
            while (n < frames)
            {
                yield return 1;
                if (Time.frameCount == last) continue; last = Time.frameCount;
                dts.Add(Time.unscaledDeltaTime * 1000f);
                FrameTimingManager.CaptureFrameTimings();
                if (FrameTimingManager.GetLatestTimings(1, ft) > 0)
                {
                    mains.Add((float)ft[0].cpuMainThreadFrameTime); gpus.Add((float)ft[0].gpuFrameTime); cpus.Add((float)ft[0].cpuFrameTime);
                    renders.Add((float)ft[0].cpuRenderThreadFrameTime); waits.Add((float)ft[0].cpuMainThreadPresentWaitTime);
                }
                if (recLoop.Valid) loops.Add(recLoop.LastValue * 1e-6f);
                if (recBatch.Valid) { b += recBatch.LastValue; sp += recSet.Valid ? recSet.LastValue : 0; tr += recTri.Valid ? recTri.LastValue : 0; ca += recCast.Valid ? recCast.LastValue : 0; rn++; }
                rule += ZGame.RuleMsLastFrame;
                n++;
            }
            w.limit = Application.targetFrameRate;                                            // -1 unless something set a frame limit again during the window
            FreeRun(false);
            recLoop.Dispose(); recBatch.Dispose(); recSet.Dispose(); recTri.Dispose(); recCast.Dispose();
            w.frames = n; w.median = Median(dts); w.frame = Mean(dts); w.worst = 0f; w.hitches = 0;
            for (int i = 0; i < dts.Count; i++) { if (dts[i] > w.worst) w.worst = dts[i]; if (w.median > 0f && dts[i] > w.median * 2.5f) w.hitches++; }
            w.main = Mean(mains); w.gpu = Mean(gpus); w.cpu = Mean(cpus); w.render = Mean(renders); w.wait = Mean(waits); w.loop = Mean(loops);
            w.batches = rn > 0 ? (float)(b / rn) : -1f; w.setpass = rn > 0 ? (float)(sp / rn) : -1f; w.tris = rn > 0 ? (float)(tr / rn) : -1f; w.casters = rn > 0 ? (float)(ca / rn) : -1f;
            w.ruleMs = n > 0 ? (float)(rule / n) : 0f; w.temp = GpuGuard.LastTemp; w.clock = ZGame.I && ZGame.I.World != null ? (float)ZGame.I.World.Time : 0f;
            w.lights = w.shadowed = 0;
            foreach (var l in Object.FindObjectsByType<Light>(FindObjectsSortMode.None))
            {
                if (!l || !l.isActiveAndEnabled || l.intensity <= 0f) continue;
                w.lights++; if (l.shadows != LightShadows.None) w.shadowed++;
            }
            var ci = CultureInfo.InvariantCulture;
            csv.Append(string.Join(",", new[] { levelName, w.view, w.preset, w.cutsOff.Replace(',', ';'), w.frames.ToString(ci), w.main.ToString("0.000", ci), w.loop.ToString("0.000", ci),
                w.gpu.ToString("0.000", ci), w.cpu.ToString("0.000", ci), w.render.ToString("0.000", ci), w.wait.ToString("0.000", ci), w.frame.ToString("0.000", ci), w.median.ToString("0.000", ci),
                w.worst.ToString("0.0", ci), w.hitches.ToString(ci), w.batches.ToString("0", ci), w.setpass.ToString("0", ci), w.tris.ToString("0", ci), w.casters.ToString("0", ci),
                w.lights.ToString(ci), w.shadowed.ToString(ci), w.ruleMs.ToString("0.000", ci), w.temp.ToString(ci), w.clock.ToString("0.0", ci), w.limit.ToString(ci) })).Append('\n');
        }

        /// <summary>The decisive number of a window: the engine's main-thread time; if this editor gives none, the PlayerLoop marker.</summary>
        static float MainOf(Win w) => w.main > 0.01f ? w.main : w.loop;

        static string Row(Win w) =>
            $"{(w.label.Length > 0 ? w.label : w.preset),-26} main {MainOf(w),6:0.00} ms | graphics {w.gpu,6:0.00} ms | frame {w.frame,6:0.00} ms (worst {w.worst:0.0}, {w.hitches} hitch) | whole loop {w.loop:0.00} | render thread {w.render:0.00} | " +
            $"batches {w.batches:0} setpass {w.setpass:0} shadow casters {w.casters:0} | lights {w.lights} ({w.shadowed} shadowed) | {w.temp} C{(w.limit > 0 ? " | FRAME LIMIT " + w.limit + " WAS ON: NOT VALID" : "")}";

        // ------------------------------------------------------------------ views
        sealed class View { public string name; public Vector3 focus; public float height; public int light; }   // light: 0 day, 1 night, 2 the level's heaviest moment
        static void Line(string s) { text.Append(s).Append('\n'); }

        static IEnumerator Show(View v)
        {
            var g = ZGame.I; var rts = Object.FindFirstObjectByType<RtsCamera>(); var dn = Object.FindFirstObjectByType<DayNight>();
            if (rts) { rts.focus = v.focus; rts.SetHeightImmediate(v.height); }
            bool night = v.light == 1 || (v.light == 2 && g.level != "L16");
            bool rain = v.light == 2 && (g.level == "L17" || g.level == "L16");
            if (!rain && rained) { StopRain(); yield return 4f; }
            if (dn) { dn.running = false; float tod = night ? NightTod : DayTod; dn.timeOfDay = tod; dn.Apply(tod); }
            string what = night ? "night" : "day";
            if (rain)
            {
                bool ok = g.level == "L17" ? OzWeather.Force(900f) : OzJungleWeather.Force(900f);
                rained = rained || ok;
                what += ok ? (g.level == "L17" ? ", storm" : ", rain") : ", NO RAIN (the WEATHER option is off or the level has none)";
                if (dn) { dn.running = false; float tod = night ? NightTod : DayTod; dn.timeOfDay = tod; dn.Apply(tod); }
                yield return ok ? 15f : 2f;
            }
            else yield return night ? 7f : 2f;
            Line($"VIEW {v.name}: camera {v.height:0} m over ({v.focus.x:0}, {v.focus.z:0}), {what}");
        }

        static IEnumerator SetPreset(int p)
        {
            if (OzQuality.Level != p) { OzQuality.Level = p; yield return 3; }
        }

        static IEnumerator Picture(string name)
        {
            yield return 2;
            ScreenCapture.CaptureScreenshot(Path.Combine(dir, name + ".png"));
            yield return 3;
        }

        // ------------------------------------------------------------------ where the main thread goes
        static bool PhaseName(string n)
        {
            int dot = n.IndexOf('.'); if (dot <= 0 || n.IndexOf('.', dot + 1) >= 0) return false;
            string p = n.Substring(0, dot);
            return p == "Initialization" || p == "EarlyUpdate" || p == "FixedUpdate" || p == "PreUpdate" || p == "Update" || p == "PreLateUpdate" || p == "PostLateUpdate" || p == "TimeUpdate";
        }
        static bool ScriptName(string n) => n.EndsWith("[Invoke]") || n.EndsWith(".Update()") || n.EndsWith(".LateUpdate()") || n.EndsWith(".FixedUpdate()");
        static readonly string[] InsideWords = { "RenderSingleCamera", "CullScriptable", "Culling", "Shadow", "ForwardLights", "SetupLights", "RenderGraph", "Canvas", "UGUI", "Animator", "Director.",
            "ParticleSystem", "Physics.", "Audio", "MeshSkinning", "RenderLoop", "Gfx.WaitFor", "WaitForTargetFPS", "GC.Collect", "UpdateRendererBoundingVolumes", "Camera.Render", "UniversalRenderPipeline" };
        static bool InsideName(string n) { for (int i = 0; i < InsideWords.Length; i++) if (n.IndexOf(InsideWords[i], System.StringComparison.Ordinal) >= 0) return true; return false; }

        static IEnumerator Split(string label, int frames)
        {
            var recs = new List<ProfilerRecorder>(); var names = new List<string>(); var kinds = new List<int>();
            try
            {
                var handles = new List<ProfilerRecorderHandle>(); ProfilerRecorderHandle.GetAvailable(handles);
                foreach (var h in handles)
                {
                    var d = ProfilerRecorderHandle.GetDescription(h);
                    if (d.UnitType != ProfilerMarkerDataUnit.TimeNanoseconds) continue;
                    string n = d.Name; if (string.IsNullOrEmpty(n)) continue;
                    int kind = PhaseName(n) ? 0 : ScriptName(n) ? 1 : InsideName(n) ? 2 : -1;
                    if (kind < 0 || recs.Count >= 900) continue;
                    var r = new ProfilerRecorder(h, 1, ProfilerRecorderOptions.Default); r.Start();
                    recs.Add(r); names.Add(n); kinds.Add(kind);
                }
            }
            catch (System.Exception ex) { Line("      (this editor does not list its markers: " + ex.Message + ")"); }
            var recLoop = new ProfilerRecorder("PlayerLoop", 1, ProfilerRecorderOptions.Default); recLoop.Start();
            var sum = new double[recs.Count]; double loop = 0; int n2 = 0, last = Time.frameCount;
            FreeRun(true);
            int f0 = Time.frameCount; while (Time.frameCount - f0 < 30) yield return 1;
            while (n2 < frames)
            {
                yield return 1;
                if (Time.frameCount == last) continue; last = Time.frameCount;
                for (int i = 0; i < recs.Count; i++) if (recs[i].Valid) sum[i] += recs[i].LastValue * 1e-6;
                if (recLoop.Valid) loop += recLoop.LastValue * 1e-6;
                n2++;
            }
            FreeRun(false);
            for (int i = 0; i < recs.Count; i++) recs[i].Dispose();
            recLoop.Dispose();
            Line($"WHERE THE MAIN THREAD GOES, {label}: {recs.Count} markers watched over {n2} frames; the whole loop of a frame {(n2 > 0 ? loop / n2 : 0):0.00} ms (with the markers watched, so a little over the table's)");
            string[] heads = { "the engine's phases of a frame (they do not overlap; together they are the loop)", "the game's scripts (inside the phases above)", "named parts (inside the phases; they overlap each other)" };
            int[] show = { 16, 24, 16 };
            for (int k = 0; k < 3; k++)
            {
                var idx = new List<int>();
                for (int i = 0; i < recs.Count; i++) if (kinds[i] == k && sum[i] > 0) idx.Add(i);
                idx.Sort((a, c) => sum[c].CompareTo(sum[a]));
                Line("   " + heads[k] + (idx.Count == 0 ? ": none offered by this editor" : ":"));
                for (int i = 0; i < idx.Count && i < show[k]; i++) Line($"      {sum[idx[i]] / Mathf.Max(1, n2),7:0.000} ms  {names[idx[i]]}");
            }
        }

        // ------------------------------------------------------------------ the run
        static IEnumerator RunAll()
        {
            Keep();
            var g = ZGame.I; var rts = Object.FindFirstObjectByType<RtsCamera>();
            holds = 0;
            int robots = 0, vehicles = 0, cannons = 0; Vector3 fort = Vector3.zero; bool hasFort = false;
            foreach (var o in g.World.Objects)
            {
                if (o == null || o.Destroyed) continue;
                if (o.Kind == ZKind.Robot) robots++; else if (o.Kind == ZKind.Vehicle) vehicles++; else if (o.Kind == ZKind.Cannon) cannons++;
                if (!hasFort && o.IsFort && o.Owner == g.player) { fort = g.CenterWorld(o, false); hasFort = true; }
            }
            Vector2 map = rts ? rts.mapSizeMeters : new Vector2(144f, 180f);
            var mid = new Vector3(map.x * 0.5f, 0f, -map.y * 0.5f);
            if (!hasFort) fort = mid;
            var views = new List<View>
            {
                new View { name = "fort_h70", focus = fort, height = 70f, light = 0 },
                new View { name = "middle_h120", focus = mid, height = 120f, light = 0 },
                new View { name = "whole_map_h230_heaviest", focus = mid, height = 230f, light = 2 },
            };
            if (g.level == "L16") views.Add(new View { name = "whole_map_h230_night", focus = mid, height = 230f, light = 1 });

            Line($"LIGHTER PRESETS, level {g.level}, Game view {Screen.width} x {Screen.height}, the preset the session came with: {OzQuality.Names[level0]}, night light shadows: {NightShadows.Name}, weather option {(OzWeather.Enabled ? "on" : "off")}");
            Line($"per window: {Frames} frames after {Settle} (and after {SettleSeconds:0.0} s at least), frame rate free; means leave out frames over 2.5 x the median; 'main' = the engine's main-thread time of a frame, 'graphics' = the graphics chip's");
            Line("in the EDITOR every number is higher than in the game itself; what counts here is one preset against another on the same picture");

            // ---- was Play started on HIGH? Only then does HIGH's own shadow reach follow the view in this session: the game's rule
            // drives the settings file that was in force when the program started. Seen at the whole-map view, where the rule
            // stands at 440 m and HIGH's file would hold its fixed 260 m
            yield return SetPreset(0);
            if (rts) { rts.focus = mid; rts.SetHeightImmediate(230f); }
            yield return 3;
            var u0 = GraphicsSettings.currentRenderPipeline as UniversalRenderPipelineAsset;
            if (!u0 || !ShadowDistanceByHeight.On || Mathf.Abs(u0.shadowDistance - ShadowDistanceByHeight.Current) > 0.6f)
            {
                Finish($"NOT MEASURED: Play was not started on HIGH: HIGH's shadow reach does not follow the view in this session (its settings file holds {(u0 ? u0.shadowDistance : -1f):0} m at the whole-map view, " +
                       $"the game's rule stands at {ShadowDistanceByHeight.Current:0} m{(ShadowDistanceByHeight.On ? "" : ", and the rule is switched off")}). Stop Play, set GRAPHICS QUALITY to HIGH, start Play again");
                yield break;
            }
            fileReach[1] = FileReach(1); fileReach[2] = FileReach(2);
            Line($"Play was started on HIGH (HIGH's shadow reach follows the view: {u0.shadowDistance:0} m at the whole-map view). The presets' own settings files hold a reach of {fileReach[1]:0} m (MEDIUM) and {fileReach[2]:0} m (LOW): " +
                 "that fixed number is what a preset picked during play has today");

            // ---- the game clock
            if (g.World.Time < StartClock)
            {
                g.paused = false; g.gameSpeed = 4f;
                float guard = Time.realtimeSinceStartup + 240f;
                while (g.World.Time < StartClock && Time.realtimeSinceStartup < guard) yield return 1;
                g.gameSpeed = speed0;
            }
            g.paused = true;
            Line($"game clock {g.World.Time:0} s (wanted {StartClock:0}): the rules stand still from here; units alive: {robots} robots, {vehicles} vehicles, {cannons} guns at the start of the run");

            // ---- the hitch test first
            yield return Show(views[0]);
            var hw = new Win { view = "hitch_test", preset = "HIGH" };
            yield return Measure(hw, 60, HitchFrames);
            bool hitchOk = hw.hitches <= 1 && hw.worst < 100f;
            Line($"HITCH TEST {(hitchOk ? "PASS" : "FAIL")}: {hw.frames} free-running frames on HIGH, median {hw.median:0.00} ms, worst {hw.worst:0.0} ms, {hw.hitches} frame(s) over 2.5 x the median (allowed: 1, and none over 100 ms)");
            if (!hitchOk && !SkipHitchTest) { Finish("HITCH: the editor stalls, nothing measured. Close what else is running, start Play again, run again"); yield break; }
            if (!hitchOk) Line("   SkipHitchTest is set: measured all the same; read every number with that in mind");
            if (hw.main <= 0.01f) Line("NOTE: this editor gives no main-thread time of its own; 'main' is the PlayerLoop marker in every row (the game's part of a frame, without the editor's own)");
            if (hw.gpu <= 0.01f) Line("NOTE: this editor gives no graphics time: the graphics columns read 0 and no cut can be kept for a graphics gain here");

            // ---- the table. A row: 0 HIGH, 1 MEDIUM, 2 LOW, 3 HIGH again; the preset as it is today: 4 MEDIUM started on it,
            // 5 MEDIUM picked during play, 6 LOW started on it, 7 LOW picked during play
            var table = new List<Win[]>();
            foreach (var v in views)
            {
                yield return Show(v);
                var row = new Win[8];
                yield return SetPreset(0);
                row[0] = new Win { view = v.name, preset = "HIGH" };
                yield return Measure(row[0], Settle, Frames);
                Line("   " + Row(row[0]));
                yield return Picture(g.level + "_" + v.name + "_HIGH");
                for (int p = 1; p <= 2; p++)
                {
                    string nm = OzQuality.Names[p];
                    yield return SetPreset(p);
                    row[p] = new Win { view = v.name, preset = nm };
                    yield return Measure(row[p], Settle, Frames);
                    Line("   " + Row(row[p]));
                    yield return Picture(g.level + "_" + v.name + "_" + nm);
                    for (int way = 0; way < 2; way++)
                    {
                        Today(true, way == 0 ? 0f : fileReach[p]);
                        var w = row[2 + 2 * p + way] = new Win { view = v.name, preset = nm, label = nm + (way == 0 ? " today, started on" : " today, picked"),
                                                                 cutsOff = way == 0 ? "all: as today in a game started on it" : $"all and reach fixed {fileReach[p]:0} m: as today picked during play" };
                        yield return Measure(w, Settle, Frames);
                        Line("   " + Row(w));
                        yield return Picture(g.level + "_" + v.name + "_" + nm + (way == 0 ? "_today_started" : "_today_picked"));
                    }
                    Today(false, 0f);
                }
                yield return SetPreset(0);
                row[3] = new Win { view = v.name, preset = "HIGH again" };
                yield return Measure(row[3], Settle, Frames);
                Line("   " + Row(row[3]));
                float h = 0.5f * (MainOf(row[0]) + MainOf(row[3])), drift = MainOf(row[0]) > 0f ? Mathf.Abs(MainOf(row[3]) - MainOf(row[0])) / MainOf(row[0]) : 0f;
                float hg = 0.5f * (row[0].gpu + row[3].gpu);
                Line($"   -> main thread against HIGH ({h:0.00} ms): MEDIUM {Less(h, MainOf(row[1]))}, LOW {Less(h, MainOf(row[2]))}; graphics against HIGH ({hg:0.00} ms): MEDIUM {Less(hg, row[1].gpu)}, LOW {Less(hg, row[2].gpu)}; " +
                     $"the two HIGH windows differ by {drift * 100f:0.0} %{(drift > 0.04f ? " (OVER 4 %: the machine drifted, take this view again)" : "")}");
                for (int p = 1; p <= 2; p++)
                    Line($"   -> {OzQuality.Names[p]} against today's {OzQuality.Names[p]}: in a game started on it: main {Against(MainOf(row[2 + 2 * p]), MainOf(row[p]))}, graphics {Against(row[2 + 2 * p].gpu, row[p].gpu)}; " +
                         $"picked during play (reach fixed {fileReach[p]:0} m): main {Against(MainOf(row[3 + 2 * p]), MainOf(row[p]))}, graphics {Against(row[3 + 2 * p].gpu, row[p].gpu)}");
                table.Add(row);
            }
            // ---- two close views for the eye only (no timing): the shadows' detail by day, the lamps' light and shadows at night
            foreach (var v in new[] { new View { name = "fort_h22", focus = fort, height = 22f, light = 0 }, new View { name = "fort_h40_night", focus = fort, height = 40f, light = 1 } })
            {
                yield return Show(v);
                for (int p = 0; p <= 2; p++) { yield return SetPreset(p); yield return 60; yield return Picture(g.level + "_" + v.name + "_" + OzQuality.Names[p]); }
            }
            // the worst view = the one with the dearest main thread on HIGH
            int worst = 0; for (int i = 1; i < table.Count; i++) if (MainOf(table[i][0]) > MainOf(table[worst][0])) worst = i;
            float hw0 = 0.5f * (MainOf(table[worst][0]) + MainOf(table[worst][3]));
            float lessM = hw0 > 0f ? 1f - MainOf(table[worst][1]) / hw0 : 0f, lessL = hw0 > 0f ? 1f - MainOf(table[worst][2]) / hw0 : 0f;
            Line($"WORST VIEW on HIGH: {views[worst].name} ({hw0:0.00} ms main thread).");
            Line($"TARGET MEDIUM, at least {OzLiteTable.TargetLess[1] * 100f:0} % less main-thread time there: {(lessM >= OzLiteTable.TargetLess[1] ? "REACHED" : "NOT REACHED")} ({lessM * 100f:0.0} %)");
            Line($"TARGET LOW, at least {OzLiteTable.TargetLess[2] * 100f:0} % less: {(lessL >= OzLiteTable.TargetLess[2] ? "REACHED" : "NOT REACHED")} ({lessL * 100f:0.0} %)");
            // a preset must not cost more than it does today, either way of reaching it
            Line($"AGAINST TODAY in the worst view (a preset must not cost more than it does today; more than {OzLiteTable.TodaySlackMainMs:0.00} ms on the main thread or {OzLiteTable.TodaySlackGpuMs:0.00} ms on the graphics chip over today's window counts as more):");
            string today = "";
            for (int p = 1; p <= 2; p++)
                for (int way = 0; way < 2; way++)
                {
                    Win kit = table[worst][p], was = table[worst][2 + 2 * p + way];
                    float dm = MainOf(kit) - MainOf(was), dg = kit.gpu - was.gpu;
                    bool more = dm > OzLiteTable.TodaySlackMainMs || dg > OzLiteTable.TodaySlackGpuMs;
                    string how = way == 0 ? "started on it" : "picked during play";
                    Line($"      {OzQuality.Names[p],-6} against today's, {how,-18}: main {dm:+0.00;-0.00} ms, graphics {dg:+0.00;-0.00} ms: {(more ? "COSTS MORE THAN TODAY (SPEC.md part 6: lower the reach, or bring both pictures and both numbers for a choice between shadows and frames)" : "not dearer than today")}");
                    today += (today.Length > 0 ? ", " : "") + OzQuality.Names[p] + " " + how + (more ? " COSTS MORE" : " ok");
                }

            // ---- where the main thread goes, on HIGH and on LOW, in the worst view
            yield return Show(views[worst]);
            yield return SetPreset(0); yield return Settle;
            yield return Split("HIGH, " + views[worst].name, 150);
            yield return SetPreset(2); yield return Settle;
            yield return Split("LOW, " + views[worst].name, 150);

            // ---- each cut alone
            string verdicts = "";
            if (withCuts)
            {
                var scenes = new List<View> { views[worst] };
                if (g.level == "L16") scenes.Add(views[worst == 3 ? 2 : 3]);
                var bestMain = new float[OzLiteTable.CutCount]; var bestGpu = new float[OzLiteTable.CutCount]; var valid = new bool[OzLiteTable.CutCount];
                foreach (var sc in scenes)
                {
                    yield return Show(sc);
                    for (int p = 1; p <= 2; p++)
                    {
                        yield return SetPreset(p);
                        Line($"EACH CUT ALONE, {OzQuality.Names[p]}, {sc.name}: the frame's cost with that one cut switched off, against the control windows (all cuts on) beside it");
                        // a control window (-1), then two cuts, a control window, and so on; only the cuts that lower something on this preset
                        var seq = new List<int> { -1 }; var left = new List<string>(); int nc = 0;
                        for (int c = 0; c < OzLiteTable.CutCount; c++)
                        {
                            if (!OzLiteTable.Lowers(c, p)) { left.Add(OzLiteTable.CutNames[c]); continue; }
                            seq.Add(c); if (++nc % 2 == 0) seq.Add(-1);
                        }
                        if (seq[seq.Count - 1] >= 0) seq.Add(-1);
                        if (left.Count > 0) Line($"      (not lowered on {OzQuality.Names[p]}, so not measured here: {string.Join(", ", left)})");
                        var wins = new Win[seq.Count];
                        for (int i = 0; i < seq.Count; i++)
                        {
                            OzLite.SetOff(seq[i] < 0 ? 0 : 1 << seq[i]);
                            wins[i] = new Win { view = sc.name, preset = OzQuality.Names[p], cutsOff = seq[i] < 0 ? "" : OzLiteTable.CutNames[seq[i]] };
                            yield return Measure(wins[i], CutSettle, Frames);
                        }
                        OzLite.SetOff(0);
                        for (int i = 0; i < seq.Count; i++)
                        {
                            if (seq[i] < 0) { Line($"      control        main {MainOf(wins[i]),6:0.00} ms | graphics {wins[i].gpu,6:0.00} ms | batches {wins[i].batches:0} | lights {wins[i].lights} ({wins[i].shadowed} shadowed)"); continue; }
                            int a = i; while (seq[a] >= 0) a--; int c = i; while (seq[c] >= 0) c++;
                            float cm = 0.5f * (MainOf(wins[a]) + MainOf(wins[c])), cg = 0.5f * (wins[a].gpu + wins[c].gpu), spread = Mathf.Abs(MainOf(wins[a]) - MainOf(wins[c]));
                            float gm = MainOf(wins[i]) - cm, gg = wins[i].gpu - cg; bool ok = spread <= ControlSpreadMs;
                            Line($"      without {OzLiteTable.CutNames[seq[i]],-19} main {MainOf(wins[i]),6:0.00} ms ({gm:+0.00;-0.00}) | graphics {wins[i].gpu,6:0.00} ms ({gg:+0.00;-0.00}) | batches {wins[i].batches:0} | lights {wins[i].lights} ({wins[i].shadowed} shadowed)" +
                                 (ok ? "" : $" | NOT VALID: its control windows differ by {spread:0.00} ms"));
                            if (ok) { valid[seq[i]] = true; if (gm > bestMain[seq[i]]) bestMain[seq[i]] = gm; if (gg > bestGpu[seq[i]]) bestGpu[seq[i]] = gg; }
                        }
                    }
                }
                Line($"VERDICT PER CUT on this level (KEEP = it pays at least {OzLiteTable.LeastMainMs:0.00} ms on the main thread or {OzLiteTable.LeastGpuMs:0.00} ms on the graphics chip in one of the windows above):");
                for (int c = 0; c < OzLiteTable.CutCount; c++)
                {
                    string vd = !valid[c] ? "NOT VALID, measure again" : bestMain[c] >= OzLiteTable.LeastMainMs || bestGpu[c] >= OzLiteTable.LeastGpuMs ? "KEEP" : "does not pay here";
                    Line($"      {OzLiteTable.CutNames[c],-19} best gain: main {bestMain[c]:0.00} ms, graphics {bestGpu[c]:0.00} ms: {vd}");
                    verdicts += (c > 0 ? ", " : "") + OzLiteTable.CutNames[c] + " " + vd;
                }
                Line("   a cut that pays on NO level is taken out (SPEC.md, part 6); the pictures decide the rest");
            }

            // ---- what the rules themselves cost, HIGH, worst view
            yield return Show(views[worst]);
            yield return SetPreset(0);
            g.paused = false;
            var rw = new Win { view = views[worst].name + "_rules_running", preset = "HIGH" };
            yield return Measure(rw, 60, Frames);
            g.paused = true;
            Line($"WITH THE RULES RUNNING (HIGH, {views[worst].name}): main {MainOf(rw):0.00} ms, of it the rules {rw.ruleMs:0.00} ms a frame; the presets cannot lower that part");
            Line($"waits for a cooler graphics chip: {holds}");

            Finish($"worst view {views[worst].name}: HIGH {hw0:0.00} ms main thread, MEDIUM {lessM * 100f:0.0} % less (wanted {OzLiteTable.TargetLess[1] * 100f:0}), LOW {lessL * 100f:0.0} % less (wanted {OzLiteTable.TargetLess[2] * 100f:0})" +
                   "; against today: " + today + (withCuts ? "; cuts: " + verdicts : ""));
        }

        static string Less(float high, float now) => high > 0f ? $"{now:0.00} ms = {(1f - now / high) * 100f:0.0} % less" : "?";
        /// <summary>The kit's window against today's window of the same preset.</summary>
        static string Against(float today, float now) => $"{now:0.00} ms, today {today:0.00} ({(now <= today ? (today - now).ToString("0.00", CultureInfo.InvariantCulture) + " ms less" : (now - today).ToString("0.00", CultureInfo.InvariantCulture) + " ms MORE")})";

        static void Finish(string head)
        {
            PutBack();
            Report = head + "\n" + text;
            File.WriteAllText(Path.Combine(dir, "measure_" + levelName + ".txt"), Report);
            File.WriteAllText(Path.Combine(dir, "measure_" + levelName + ".csv"), csv.ToString());
            Debug.Log(Tag + " " + levelName + ": " + head + "  (measure_" + levelName + ".txt, .csv and the pictures: " + dir + ")");
        }
    }
}
