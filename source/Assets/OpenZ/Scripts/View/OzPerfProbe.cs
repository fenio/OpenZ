using System.Collections.Generic;
using System.IO;
using System.Text;
using OpenZ.Sim;
using Unity.Profiling;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace OpenZ
{
    /// <summary>
    /// started with -ozperf, or the editor menu OpenZ > Perf > Probe: start is used in Play. While it runs:
    ///  - uncapped: Application.targetFrameRate -1, vSync 0, GameBoot.CapFps -1 (so the heat guard also comes back to uncapped);
    ///  - every WindowS seconds (5 s) one Player.log line "[OpenZ Perf] ..." and one CSV row: fps (avg, 1 % low), CPU frame /
    ///    main-thread / render-thread ms and GPU ms from FrameTimingManager (avg + p95), GC bytes allocated per frame, batches /
    ///    SetPass / draw calls / triangles, enabled lights (with shadows), live units, the GPU temperature, and a HEAT flag when
    ///    any fps cap was active in the window (drop those rows: they are not uncapped numbers);
    ///  - -ozperfstorm (with -ozperf): 30 s into each level the night rain storm is forced (OzWeather.Force: night + rain, 900 s),
    ///    so the heaviest weather moment is repeatable in the exe; the window is marked "storm".
    /// Nothing is saved (no PlayerPrefs), nothing is drawn, the sim is not touched. Mark("nuke") labels the current window.
    /// Counters a release player does not expose (GC Allocated In Frame needs a development build) are written as -1.
    /// </summary>
    public class OzPerfProbe : MonoBehaviour
    {
        public static OzPerfProbe I { get; private set; }
        public static float WindowS = 5f;
        public const int HotC = 83, CoolC = 76, HeatFps = 30;
        public static string CsvPath { get; private set; }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Boot()
        {
            if (Application.isEditor || !GameBoot.DevArg("-ozperf")) return;
            StormArg = GameBoot.DevArg("-ozperfstorm");
            Begin(StormArg ? "-ozperf -ozperfstorm" : "-ozperf");
        }
        /// <summary>-ozperfstorm: force the night storm 30 s into each level (exe profiling of the rain moment).</summary>
        public static bool StormArg;
        /// <summary>GPU temperature source (C, -1 = unknown). Null = HeatGuardRuntime's nvidia-smi poll (player only). The editor menu sets
        /// it to GpuGuard.LastTemp: HeatGuardRuntime does not run in the editor, so without it the 83 C hold never engaged there.</summary>
        public static System.Func<int> TempSource;

        public static void Begin(string why)
        {
            if (I) return;
            var go = new GameObject("OZ_PerfProbe");
            DontDestroyOnLoad(go);
            I = go.AddComponent<OzPerfProbe>();
            I.Init(why);
        }

        public static void End() { if (I) Destroy(I.gameObject); }

        /// <summary>Labels the current window (e.g. "nuke", "fort fall", "exp2 salvo") in the log line and the CSV row.</summary>
        public static void Mark(string label) { if (I) I.mark = string.IsNullOrEmpty(I.mark) ? label : I.mark + "+" + label; }

        // ------------------------------------------------------------------ state
        int capBefore, targetBefore, vsyncBefore;
        ProfilerRecorder gcRec, batchRec, setPassRec, drawRec, triRec;
        readonly FrameTiming[] ft = new FrameTiming[1];
        readonly List<float> dtMs = new List<float>(4096), cpuMs = new List<float>(4096), mainMs = new List<float>(4096),
            renderMs = new List<float>(4096), gpuMs = new List<float>(4096);
        double gcSum; long gcFrames; double batchSum, setPassSum, drawSum, triSum; long renderFrames;
        float windowStart; int maxTemp = -1; bool capped, hot, stormDone; string mark = ""; int sceneHandle = -1;
        StreamWriter csv;

        void Init(string why)
        {
            capBefore = GameBoot.CapFps; targetBefore = Application.targetFrameRate; vsyncBefore = QualitySettings.vSyncCount;
            GameBoot.CapFps = -1; Application.targetFrameRate = -1; QualitySettings.vSyncCount = 0;
            gcRec = ProfilerRecorder.StartNew(ProfilerCategory.Memory, "GC Allocated In Frame");
            batchRec = ProfilerRecorder.StartNew(ProfilerCategory.Render, "Batches Count");
            setPassRec = ProfilerRecorder.StartNew(ProfilerCategory.Render, "SetPass Calls Count");
            drawRec = ProfilerRecorder.StartNew(ProfilerCategory.Render, "Draw Calls Count");
            triRec = ProfilerRecorder.StartNew(ProfilerCategory.Render, "Triangles Count");
            try
            {
                Directory.CreateDirectory(Application.persistentDataPath);
                CsvPath = Path.Combine(Application.persistentDataPath, "OpenZ_perf_" + System.DateTime.Now.ToString("yyyyMMdd_HHmmss") + ".csv");
                csv = new StreamWriter(CsvPath, false, new UTF8Encoding(false)) { AutoFlush = true };
                csv.WriteLine("time_s,scene,level,res,quality,frames,fps_avg,fps_1pct_low,cpu_ms_avg,cpu_ms_p95,main_ms_avg,render_ms_avg,gpu_ms_avg,gpu_ms_p95," +
                              "gc_bytes_per_frame,batches,setpass,drawcalls,triangles,lights_on,lights_shadowed,robots,vehicles,cannons,gpu_c_max,heat,mark");
            }
            catch (System.Exception e) { csv = null; Debug.LogWarning("[OpenZ Perf] no CSV (" + e.Message + "); the log lines still come"); }
            windowStart = Time.unscaledTime;
            Debug.Log($"[OpenZ Perf] probe ON ({why}): uncapped (target fps -1, vSync 0; was cap {capBefore}, target {targetBefore}, vSync {vsyncBefore}), " +
                      $"{WindowS:0} s windows, heat hold {HeatFps} fps at {HotC} C until {CoolC} C, GC counter {(gcRec.Valid ? "on" : "n/a (release player)")}, " +
                      $"CSV {CsvPath ?? "none"}");
        }

        void Update()
        {
            int t = TempSource != null ? TempSource() : HeatGuardRuntime.GpuTempC;
            if (t > maxTemp) maxTemp = t;
            if (!hot && t >= HotC) { hot = true; Debug.Log($"[OpenZ Perf] GPU {t} C: holding {HeatFps} fps until {CoolC} C (windows flagged HEAT)"); }
            else if (hot && t >= 0 && t < CoolC) { hot = false; Application.targetFrameRate = -1; Debug.Log($"[OpenZ Perf] GPU {t} C: uncapped again"); }
            if (hot && Application.targetFrameRate != HeatFps) Application.targetFrameRate = HeatFps;
            if (hot || Application.targetFrameRate > 0 || QualitySettings.vSyncCount > 0) capped = true;
            int sh = SceneManager.GetActiveScene().handle;
            if (sh != sceneHandle) { sceneHandle = sh; stormDone = false; }
            if (StormArg && !stormDone && Time.timeSinceLevelLoad > 30f && ZGame.I && ZGame.I.World != null && OzWeather.Force(900f)) { stormDone = true; Mark("storm"); Debug.Log("[OpenZ Perf] -ozperfstorm: night rain storm forced (900 s)"); }

            dtMs.Add(Time.unscaledDeltaTime * 1000f);
            FrameTimingManager.CaptureFrameTimings();
            if (FrameTimingManager.GetLatestTimings(1, ft) > 0)
            {
                cpuMs.Add((float)ft[0].cpuFrameTime); mainMs.Add((float)ft[0].cpuMainThreadFrameTime);
                renderMs.Add((float)ft[0].cpuRenderThreadFrameTime); gpuMs.Add((float)ft[0].gpuFrameTime);
            }
            if (gcRec.Valid) { gcSum += gcRec.LastValue; gcFrames++; }
            if (batchRec.Valid) { batchSum += batchRec.LastValue; setPassSum += setPassRec.Valid ? setPassRec.LastValue : 0; drawSum += drawRec.Valid ? drawRec.LastValue : 0; triSum += triRec.Valid ? triRec.LastValue : 0; renderFrames++; }

            if (Time.unscaledTime - windowStart >= WindowS && dtMs.Count > 0) Flush();
        }

        static float Avg(List<float> l) { if (l.Count == 0) return -1f; double s = 0; for (int i = 0; i < l.Count; i++) s += l[i]; return (float)(s / l.Count); }
        static float Pct(List<float> l, float p) { if (l.Count == 0) return -1f; l.Sort(); return l[Mathf.Clamp(Mathf.CeilToInt(p * l.Count) - 1, 0, l.Count - 1)]; }

        void Flush()
        {
            float span = Time.unscaledTime - windowStart; int frames = dtMs.Count;
            float fps = span > 0f ? frames / span : 0f;
            float cpuA = Avg(cpuMs), mainA = Avg(mainMs), renA = Avg(renderMs), gpuA = Avg(gpuMs);
            float cpu95 = Pct(cpuMs, 0.95f), gpu95 = Pct(gpuMs, 0.95f), dt99 = Pct(dtMs, 0.99f);
            float low1 = dt99 > 0f ? 1000f / dt99 : 0f;
            double gcPer = gcFrames > 0 ? gcSum / gcFrames : -1;
            double batches = renderFrames > 0 ? batchSum / renderFrames : -1, setPass = renderFrames > 0 ? setPassSum / renderFrames : -1,
                   draws = renderFrames > 0 ? drawSum / renderFrames : -1, tris = renderFrames > 0 ? triSum / renderFrames : -1;
            int lightsOn = 0, lightsShadow = 0;
            foreach (var l in FindObjectsByType<Light>(FindObjectsSortMode.None))
            {
                if (!l || !l.isActiveAndEnabled || l.intensity <= 0f) continue;
                lightsOn++; if (l.shadows != LightShadows.None) lightsShadow++;
            }
            int robots = 0, vehicles = 0, cannons = 0;
            var g = ZGame.I;
            if (g && g.World != null)
                foreach (var o in g.World.Objects)
                {
                    if (o == null || o.Destroyed) continue;
                    if (o.Kind == ZKind.Robot) robots++; else if (o.Kind == ZKind.Vehicle) vehicles++; else if (o.Kind == ZKind.Cannon) cannons++;
                }
            string scene = SceneManager.GetActiveScene().name, level = g ? g.level : "-", res = Screen.width + "x" + Screen.height;
            int q = QualitySettings.GetQualityLevel();
            string heat = capped ? "HEAT/CAP" : "OK";
            Debug.Log($"[OpenZ Perf] {scene} {res} q{q} | {span:0.0} s {frames} f | fps {fps:0.0} (1% low {low1:0.0}) | CPU {cpuA:0.00} ms (p95 {cpu95:0.00}) main {mainA:0.00} render {renA:0.00}" +
                      $" | GPU {gpuA:0.00} ms (p95 {gpu95:0.00}) | GC {(gcPer < 0 ? "n/a" : (gcPer / 1024.0).ToString("0.00") + " KB/f")} | batches {batches:0} setpass {setPass:0} draws {draws:0} tris {tris / 1e6:0.00}M" +
                      $" | lights {lightsOn} ({lightsShadow} shadowed) | units R{robots} V{vehicles} C{cannons} | GPU {(maxTemp < 0 ? "n/a" : maxTemp + " C")} | {heat}{(mark.Length > 0 ? " | " + mark : "")}");
            if (csv != null)
            {
                var ci = System.Globalization.CultureInfo.InvariantCulture;
                csv.WriteLine(string.Join(",", new[] {
                    Time.unscaledTime.ToString("0.0", ci), scene, level, res, q.ToString(ci), frames.ToString(ci), fps.ToString("0.0", ci), low1.ToString("0.0", ci),
                    cpuA.ToString("0.000", ci), cpu95.ToString("0.000", ci), mainA.ToString("0.000", ci), renA.ToString("0.000", ci), gpuA.ToString("0.000", ci), gpu95.ToString("0.000", ci),
                    gcPer.ToString("0", ci), batches.ToString("0", ci), setPass.ToString("0", ci), draws.ToString("0", ci), tris.ToString("0", ci),
                    lightsOn.ToString(ci), lightsShadow.ToString(ci), robots.ToString(ci), vehicles.ToString(ci), cannons.ToString(ci), maxTemp.ToString(ci), heat, mark.Replace(',', ';') }));
            }
            dtMs.Clear(); cpuMs.Clear(); mainMs.Clear(); renderMs.Clear(); gpuMs.Clear();
            gcSum = 0; gcFrames = 0; batchSum = setPassSum = drawSum = triSum = 0; renderFrames = 0;
            maxTemp = -1; capped = hot; mark = ""; windowStart = Time.unscaledTime;
        }

        void OnDestroy()
        {
            if (I != this) return;
            I = null;
            if (dtMs.Count > 0) Flush();
            gcRec.Dispose(); batchRec.Dispose(); setPassRec.Dispose(); drawRec.Dispose(); triRec.Dispose();
            if (csv != null) { csv.Dispose(); csv = null; }
            GameBoot.CapFps = capBefore; Application.targetFrameRate = targetBefore; QualitySettings.vSyncCount = vsyncBefore;
            Debug.Log($"[OpenZ Perf] probe OFF: cap {capBefore} fps restored, CSV {CsvPath ?? "none"}");
        }
    }
}
