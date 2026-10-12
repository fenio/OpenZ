#if UNITY_EDITOR
using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Text.RegularExpressions;
using OpenZ.Sim;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace OpenZ
{
    /// <summary>
    /// OpenZ/Final Gate/Run capture set (L02 + L17) (Editor/OzFinalGate.cs), which hands the job name over in SessionState:
    ///  * "L02" / "L17": the level in the FULL look. CPU bots paused (restored before the ending), the Zod briefing skipped, then a fixed
    ///    list of named RTS-camera shots (forts, factories, bridges and water, the L02 oasis pond, the L17 quay, supply, grenade crates,
    ///    sandbags, gates), DAY then NIGHT; a staged mid-battle with every unit type of both teams (night + day frames); the pause menu
    ///    options with the audio sliders; the ending (L02 WIN, L17 LOSS) with its fall, blast and end-screen frames.
    ///  * "MENU": the main menu, ROADMAP, CREDITS and OPTIONS (audio sliders) panels.
    /// is written in the shot row). Per shot: average and worst frame time over the settle window before the capture.
    /// Per job: every console error / warning, the sim events seen on screen vs the OzSoundLog lines next to them (silent-action census),
    /// the OzSoundLog DROP counts, AudioDirector's coverage table. Output rows go to final_gate/_parts/&lt;job&gt;.*; the editor writes
    /// REPORT.md after the last job. Robust: every shot has a timeout, a missing object is logged and skipped, an exception in a shot
    /// fails that shot only; a job watchdog (no progress 120 s) and the heat rule (GpuGuard &gt;= 86 C for 30 s, or 90 C) end the job.
    /// </summary>
    [DefaultExecutionOrder(45)]                                  // after AudioDirector (40) has read World.Events, before CombatView (50) clears them
    public class OzFinalGateRunner : MonoBehaviour
    {
        public const string JobKey = "OpenZ.FinalGate.Job";
        /// <summary></summary>
        public const string ChainKey = "OpenZ.FinalGate.Chain";
        static readonly Dictionary<string, string> ChainScene = new Dictionary<string, string>
        {
            ["L02"] = "Assets/OpenZ/Scenes/L02_Desert.unity", ["L17"] = "Assets/OpenZ/Scenes/L17_CarPark.unity", ["MENU"] = "Assets/OpenZ/Scenes/MainMenu.unity"
        };
        static string pendingJob;
        static void OnChainLoaded(Scene sc, LoadSceneMode mode)
        {
            SceneManager.sceneLoaded -= OnChainLoaded;
            if (string.IsNullOrEmpty(pendingJob)) return;
            var go = new GameObject("OzFinalGateRunner"); DontDestroyOnLoad(go);
            var r = go.AddComponent<OzFinalGateRunner>(); r.job = pendingJob; pendingJob = null;
            Debug.Log("[FinalGate] chained job " + r.job + " in scene " + sc.name);
        }
        public const int ShotW = 2560, ShotH = 1440;
        public static readonly string Root = Path.GetFullPath(Path.Combine(Application.dataPath, "../../../.."));
        public static string OutDir => Path.Combine(Root, "work", "records", "milestones", "final_gate");
        public static string PartsDir => Path.Combine(OutDir, "_parts");
        public static float DayTod = 0.08f, NightTod = 0.62f;
        public static float JobTimeout = 900f, ShotTimeout = 25f, WatchdogSeconds = 120f;

        // ------------------------------------------------------------------ boot (job handed over by the editor through SessionState)
        static readonly List<string> earlyLog = new List<string>();
        static bool earlyHooked;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        static void HookEarly()
        {
            if (string.IsNullOrEmpty(UnityEditor.SessionState.GetString(JobKey, ""))) return;
            if (earlyHooked) return;
            earlyHooked = true; earlyLog.Clear();
            Application.logMessageReceived += EarlyLog;
        }
        static void EarlyLog(string c, string st, LogType t)
        {
            if (t == LogType.Log || earlyLog.Count > 400) return;
            earlyLog.Add(t + "\t" + (c ?? "").Split('\n')[0]);
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Boot()
        {
            string job = UnityEditor.SessionState.GetString(JobKey, "");
            if (string.IsNullOrEmpty(job)) return;
            UnityEditor.SessionState.EraseString(JobKey);
            if (earlyHooked) { Application.logMessageReceived -= EarlyLog; earlyHooked = false; }
            var go = new GameObject("OzFinalGateRunner"); DontDestroyOnLoad(go);
            var r = go.AddComponent<OzFinalGateRunner>(); r.job = job;
            r.console.AddRange(earlyLog.Select(l => "before the runner\t" + l));
            Debug.Log("[FinalGate] job " + job + " in scene " + SceneManager.GetActiveScene().name);
        }

        // ------------------------------------------------------------------ state
        string job;
        bool done;
        readonly System.Diagnostics.Stopwatch clock = System.Diagnostics.Stopwatch.StartNew();
        float Raw => (float)clock.Elapsed.TotalSeconds;
        float stallTotal, lastFrameRaw = -1f; int stalls; float stallMax;
        /// <summary>
        /// the first L02 run lost its whole job to the watchdog after a 200 s warm-up stall).</summary>
        float T => Raw - stallTotal;
        void StallClock()
        {
            float r = Raw;
            if (lastFrameRaw >= 0f && r - lastFrameRaw > 2f) { float gap = r - lastFrameRaw; stallTotal += gap; stalls++; stallMax = Mathf.Max(stallMax, gap); log.AppendLine($"[{T,7:0.00}] editor stall {gap:0.0} s (not counted)"); }
            lastFrameRaw = r;
        }
        float lastMark;
        readonly StringBuilder log = new StringBuilder();
        readonly List<string> console = new List<string>();
        readonly List<string> shotRows = new List<string>();
        readonly List<string> skipped = new List<string>();
        int errors, warnings, peakGpu = -1; float hot = -1f, lastHeat = -1f;
        string stopReason;

        ZGame g; ZWorld W; RtsCamera cam; DayNight dn; bool dnRunning0; float tod0 = -1f;
        readonly List<ZBot> savedBots = new List<ZBot>();
        readonly List<ZObject> staged = new List<ZObject>();
        float soundT0;

        // frame timing (per shot window)
        bool measuring; readonly List<float> frameMs = new List<float>();

        // census: sim events seen in LateUpdate (after AudioDirector read them)
        struct Seen { public ZEventType type; public float t; public bool onScreen; public Vector3 pos; }
        readonly List<Seen> seen = new List<Seen>();

        void Mark(string s) { lastMark = T; log.AppendLine($"[{T,7:0.00}] {s}"); Debug.Log("[FinalGate] " + s); }

        void OnEnable() { Application.logMessageReceived += OnLog; }
        void OnDisable() { Application.logMessageReceived -= OnLog; if (!done) Finish("runner disabled (Play stopped by hand?)"); }
        void OnLog(string c, string st, LogType type)
        {
            if (c != null && c.StartsWith("[FinalGate]")) return;
            if (type == LogType.Error || type == LogType.Exception || type == LogType.Assert) errors++;
            else if (type == LogType.Warning) warnings++;
            else return;
            if (console.Count < 1500) console.Add($"{T:0.0}\t{type}\t{(c ?? "").Split('\n')[0]}");
        }

        void Update()
        {
            StallClock();
            if (measuring) frameMs.Add(Time.unscaledDeltaTime * 1000f);
        }

        void LateUpdate()
        {
            var game = g ? g : ZGame.I;
            if (game == null || game.World == null || seen.Count > 60000) return;
            var cm = Camera.main;
            var ev = game.World.Events;
            for (int i = 0; i < ev.Count; i++)
            {
                var e = ev[i];
                Vector3 p;
                try { var a = game.World.GetAny(e.objId); p = a != null ? game.CenterWorld(a) : ZGame.PxToWorld(e.x, e.y); }
                catch { p = ZGame.PxToWorld(e.x, e.y); }
                bool on = false;
                if (cm) { var vp = cm.WorldToViewportPoint(p); on = vp.z > 0f && vp.x >= 0f && vp.x <= 1f && vp.y >= 0f && vp.y <= 1f; }
                seen.Add(new Seen { type = e.type, t = Time.unscaledTime, onScreen = on, pos = p });
            }
        }

        // ------------------------------------------------------------------ heat + watchdog
        static int GpuTemp()
        {
            try
            {
                foreach (var a in AppDomain.CurrentDomain.GetAssemblies())
                {
                    var t = a.GetType("OpenZ.EditorTools.GpuGuard"); if (t == null) continue;
                    var p = t.GetProperty("LastTemp"); return p != null ? (int)p.GetValue(null) : -1;
                }
            }
            catch { }
            return -1;
        }

        IEnumerator Watch()
        {
            while (!done)
            {
                StallClock();
                int gpu = GpuTemp(); peakGpu = Mathf.Max(peakGpu, gpu);
                float step = lastHeat < 0f ? 0.5f : T - lastHeat; lastHeat = T;
                if (gpu >= 90) { Finish("heat: GPU 90 C"); yield break; }
                if (gpu >= 86) { hot = hot < 0f ? step : hot + step; if (hot >= 30f) { Finish("heat: GPU >= 86 C for 30 s"); yield break; } }
                else if (gpu >= 0 && gpu < 84) hot = -1f;
                if (T - lastMark > WatchdogSeconds) { Finish($"watchdog: no progress for {WatchdogSeconds:0} s"); yield break; }
                if (T > JobTimeout) { Finish($"job timeout {JobTimeout:0} s"); yield break; }
                yield return new WaitForSecondsRealtime(0.5f);
            }
        }

        // ------------------------------------------------------------------ the run
        IEnumerator Start()
        {
            lastMark = T;
            Directory.CreateDirectory(OutDir); Directory.CreateDirectory(PartsDir);
            Application.targetFrameRate = 60;                                    // the Play cap (the project rules); frame times are measured under it
            soundT0 = Time.unscaledTime;
            StartCoroutine(Watch());
            Mark($"final gate job {job}, {DateTime.Now:yyyy-MM-dd HH:mm:ss}, scene {SceneManager.GetActiveScene().name}, screen {Screen.width}x{Screen.height}, " +
                 $"oz_testmode {PlayerPrefs.GetInt("oz_testmode", 0)}, oz_music {PlayerPrefs.GetInt("oz_music", 1)}, GPU {SystemInfo.graphicsDeviceName}, " +
                 $"QualityLevel {QualitySettings.names[QualitySettings.GetQualityLevel()]}, targetFrameRate {Application.targetFrameRate}");
            if (Screen.width != ShotW || Screen.height != ShotH)
            {   // the editor selected 2560x1440 before Play; a Game view opened or re-docked by Play may not have it yet
                OzFinalGateView.SetFixed(ShotW, ShotH, "OpenZ Final Gate", out string gv);
                for (int i = 0; i < 5; i++) yield return null;
                Mark($"Game view re-set in Play: {gv}; screen now {Screen.width}x{Screen.height}");
            }
            if (Screen.width != ShotW || Screen.height != ShotH) Mark($"WARNING: Game view is {Screen.width}x{Screen.height}, not {ShotW}x{ShotH}: frames keep this size");
            IEnumerator body = job == "MENU" ? MenuJob() : LevelJob();
            yield return Guard(body, "job " + job, JobTimeout);
            Finish(null);
        }

        /// <summary>Runs a body frame by frame: an exception or a timeout ends this body only (logged), never the run.</summary>
        IEnumerator Guard(IEnumerator body, string what, float timeout)
        {
            float start = T;
            while (!done)
            {
                object cur;
                try { if (!body.MoveNext()) yield break; cur = body.Current; }
                catch (Exception e) { Mark($"FAILED {what}: {e.GetType().Name}: {e.Message}"); skipped.Add($"{what}: exception {e.GetType().Name}: {e.Message}"); yield break; }
                if (T - start > timeout) { Mark($"TIMEOUT {what} after {timeout:0} s"); skipped.Add($"{what}: timeout {timeout:0} s"); yield break; }
                lastMark = T;
                yield return cur;
            }
        }

        IEnumerator WaitReal(float s) { float end = T + s; while (T < end && !done) yield return null; }

        // ================================================================== level job
        IEnumerator LevelJob()
        {
            float w0 = T;
            while ((!ZGame.I || ZGame.I.World == null) && T - w0 < 90f) yield return null;
            if (!ZGame.I || ZGame.I.World == null) { skipped.Add("level never came up within 90 s"); yield break; }
            g = ZGame.I; W = g.World; cam = FindFirstObjectByType<RtsCamera>(); dn = FindFirstObjectByType<DayNight>();
            if (dn) { dnRunning0 = dn.running; tod0 = dn.timeOfDay; }
            Mark($"level {g.level}, player {g.player}, objects {W.Objects.Count}, bots {W.Bots.Count}, RTS camera {(cam ? "yes" : "MISSING")}, DayNight {(dn ? "yes" : "MISSING")}");
            savedBots.Clear(); savedBots.AddRange(W.Bots); W.Bots.Clear();
            Mark($"CPU bots paused ({savedBots.Count}) for still frames; back on before the ending");
            yield return Guard(SkipBriefing(), "skip briefing", 60f);
            yield return WaitReal(1.5f);
            if (g.level == "L02") yield return Guard(MinimapFrame(), "minimap 1920x1080 (UI7)", 40f);

            var shots = LevelShots(g.level);
            Mark($"{shots.Count} static shots per time of day");
            yield return Guard(SetTod(DayTod), "day", 10f);
            foreach (var s in shots) yield return Guard(StaticShot(s, false), s.name + "_day", ShotTimeout);
            yield return Guard(SetTod(NightTod), "night", 10f);
            foreach (var s in shots) yield return Guard(StaticShot(s, true), s.name + "_night", ShotTimeout);

            yield return Guard(Battle(), "battle", 90f);
            yield return Guard(SetTod(DayTod), "day (settings / ending)", 10f);
            yield return Guard(PauseOptions(), "settings (pause menu options)", ShotTimeout);
            if (g.level == "L02") yield return Guard(LegacyLineUp(), "LegacyMatchCheck F6 / F7 / F8 (VX38)", 120f);
            else yield return Guard(WallWalk(), "GP51 L17 wall walk", 90f);
            if (dn) dn.running = dnRunning0;
            W.Bots.Clear(); W.Bots.AddRange(savedBots); Mark($"CPU bots back ({savedBots.Count})");
            yield return Guard(Ending(g.level != "L17"), "ending", 120f);
        }

        IEnumerator SkipBriefing()
        {
            float w0 = T; bool aborted = false;
            var pf = typeof(ZodBriefing).GetField("perf", BindingFlags.NonPublic | BindingFlags.Instance);
            double sim0 = W.Time;
            while (T - w0 < 45f && !done)
            {
                if (ZodBriefing.BriefingActive)
                {
                    if (!aborted && ZodBriefing.I && pf != null && T - w0 > 0.6f)
                    {
                        var p = pf.GetValue(ZodBriefing.I);
                        var ab = p?.GetType().GetMethod("Abort");
                        if (ab != null) { ab.Invoke(p, new object[] { 0.22f }); aborted = true; Mark("briefing: Abort(0.22 s) sent"); }
                    }
                }
                else if (!g.paused && W.Time > sim0 + 0.5) break;
                yield return null;
            }
            Mark($"briefing over after {T - w0:0.0} s (sim t {W.Time:0.0})");
        }

        IEnumerator SetTod(float tod)
        {
            if (!dn) { Mark("DayNight missing: time of day not set"); yield break; }
            dn.running = false; dn.timeOfDay = tod; dn.Apply(tod);
            Mark($"time of day {tod:0.00}");
            yield return WaitReal(1.5f);                                          // lamps, night grade, exposure settle
        }

        // ------------------------------------------------------------------ shot list
        public sealed class Shot
        {
            public string name, target, note; public float h = 60f, pitch = 55f, settle = 1.6f;
            public bool low, dayOnly; public float yaw;
            public Shot(string n, string t, float h = 60f, float pitch = 55f, string note = null) { name = n; target = t; this.h = h; this.pitch = pitch; this.note = note; }
        }

        /// <summary>The fixed list (targets: fort:&lt;team&gt;, bld:&lt;type&gt;[:team], sim:&lt;type prefix&gt;, go:&lt;exact|exact&gt;,
        /// goprefix:&lt;prefix|prefix&gt;, gate:&lt;fort|factory&gt;, pos:x,z). Alternatives separated by ';' are tried in order.</summary>
        public static List<Shot> LevelShots(string lv)
        {
            var l = new List<Shot>
            {
                new Shot("forts_red", "fort:red", 70f),
                new Shot("forts_blue", "fort:blue", 70f),
                new Shot("factory_robot", "bld:robot_factory:red;bld:robot_factory", 55f),
                new Shot("factory_vehicle", "bld:vehicle_factory:red;bld:vehicle_factory", 55f),
                new Shot("bridge_water", "go:WAT_BridgeSpan_1|WAT_BridgeSpan_0;sim:bridge", 60f),
            };
            if (lv == "L17")
            {
                l.Add(new Shot("quay_a", "pos:102,-49", 55f, 55f, "props_on_land quay spot 1"));
                l.Add(new Shot("quay_b", "pos:35.5,-161.5", 55f, 55f, "props_on_land quay spot 2"));
                l.Add(new Shot("quay_c", "pos:19,-214", 55f, 55f, "props_on_land quay spot 3"));
                l.Add(new Shot("bridge_water_2", "go:WAT_BridgeSpan_0;sim:bridge", 45f, 50f));
            }
            else
            {
                l.Add(new Shot("oasis_pond", "pos:86,-97", 55f, 55f, "east-middle bridge south end"));
                l.Add(new Shot("waterfall_pond", "pos3:72.05,-13.0,-0.35", 60f, 55f, "the plunge pond (WF3 bank, lace, rings)"));
                l.Add(new Shot("bridge_water_2", "go:WAT_BridgeSpan_2|WAT_BridgeSpan_3;sim:bridge", 45f, 50f));
            }
            l.Add(new Shot("supply", $"goprefix:BP2_{lv}_Supply;goprefix:BP2_;goprefix:Supply", 45f));
            if (lv == "L17")
            {   // VX77: both fort guns at 22 m and 55 m, day + night (red A desert shark, blue B claws)
                l.Add(new Shot("fortgun_red_h22", "fortgun:red", 22f, 55f, "VX77 red fort gun, h 22"));
                l.Add(new Shot("fortgun_red_h55", "fortgun:red", 55f, 55f, "VX77 red fort gun, h 55"));
                l.Add(new Shot("fortgun_blue_h22", "fortgun:blue", 22f, 55f, "VX77 blue fort gun, h 22"));
                l.Add(new Shot("fortgun_blue_h55", "fortgun:blue", 55f, 55f, "VX77 blue fort gun, h 55"));
            }
            else
            {   // waterfall v3: the h 70 game camera (as play_h70.png) and the source basin from above
                l.Add(new Shot("waterfall_h70", "pos3:72.3,-10.8,6", 70f, 55f, "waterfall v3 at the game camera h 70 (vs play_h70.png)"));
                l.Add(new Shot("waterfall_top", "pos3:72.4,-7.9,17.8", 40f, 62f, "waterfall v3 source basin from above (vs v2_top.png)"));
            }
            // VX15 clipping / stretching sweep: low and close angles (day only)
            Func<string, string, float, float, float, string, Shot> low = (n, t, h, p, y, note) => new Shot(n, t, h, p, note) { low = true, dayOnly = true, yaw = y };
            if (lv == "L17")
            {
                l.Add(low("low_quay_a", "pos:102,-49", 5f, 14f, 0f, "VX15 lamp heads / props on the canal edge, low"));
                l.Add(low("low_quay_b", "pos:35.5,-161.5", 5f, 14f, 30f, "VX15 canal edge, low"));
                l.Add(low("low_quay_c", "pos:19,-214", 5f, 14f, -30f, "VX15 canal edge, low"));
                l.Add(low("low_bridge", "go:WAT_BridgeSpan_1|WAT_BridgeSpan_0;sim:bridge", 5f, 12f, 25f, "VX15 bridge deck / debris, low"));
                l.Add(low("low_fort_red", "fort:red", 9f, 16f, 20f, "VX15 fort walls on the concrete, low (texels)"));
                l.Add(low("low_concrete", "fort:red", 22f, 40f, 0f, "VX15 L17 concrete plates at the closest zoom (texels)"));
            }
            else
            {
                l.Add(low("low_waterfall_plunge", "pos3:72.05,-13.0,-0.35", 4.5f, 10f, 0f, "waterfall v3 plunge, low (vs v2_low_plunge.png)"));
                l.Add(low("low_waterfall_side", "pos3:72.3,-11,6", 7f, 8f, -55f, "waterfall v3 side: curtain, trickles, cascade breaks"));
                l.Add(low("low_bridge", "go:WAT_BridgeSpan_1|WAT_BridgeSpan_0;sim:bridge", 5f, 12f, 25f, "VX15 bridge deck / river banks, low"));
                l.Add(low("low_oasis", "pos:86,-97", 5f, 12f, 0f, "VX15 oasis pond banks / props, low"));
                l.Add(low("low_fort_red", "fort:red", 9f, 16f, 20f, "VX15 fort on the sand, low"));
            }
            l.Add(low("low_sandbags", "goprefix:DEC_Sandbag_;go:Sandbags", 4f, 14f, 35f, "VX15 sandbags, low"));
            l.Add(low("low_crates", "sim:grenades", 4f, 14f, -35f, "VX15 grenade crate, low"));
            l.Add(low("low_gate_fort", "gate:fort", 5f, 14f, 15f, "VX15 fort gate, low"));
            l.Add(low("low_supply", $"goprefix:BP2_{lv}_Supply;goprefix:BP2_;goprefix:Supply", 5f, 14f, -20f, "VX15 supply props, low"));
            l.Add(new Shot("crates", "sim:grenades", 35f, 50f));
            l.Add(new Shot("sandbags", "goprefix:DEC_Sandbag_;go:Sandbags", 35f, 50f));
            l.Add(new Shot("gate_fort", "gate:fort", 30f, 45f));
            l.Add(new Shot("gate_factory", "gate:factory", 30f, 45f));
            return l;
        }

        // ------------------------------------------------------------------ target resolver (null = missing: logged + skipped)
        Vector3 RedFortPos()
        {
            var fs = W.Objects.Where(o => o != null && o.IsFort && !o.Destroyed && o.Owner == g.player).ToList();
            if (fs.Count == 0) return Vector3.zero;
            var s = Vector3.zero; foreach (var f in fs) s += Where(f); return s / fs.Count;
        }
        Vector3 Where(ZObject o) => g.StaticViews.TryGetValue(o.Id, out var go) && go ? go.transform.position : g.CenterWorld(o);
        static Vector3 Flat(Vector3 v) { v.y = 0f; return v; }

        Vector3? Resolve(string spec, out string what)
        {
            what = null;
            foreach (var alt in spec.Split(';'))
            {
                var p = alt.Trim(); int c = p.IndexOf(':'); if (c < 0) continue;
                string kind = p.Substring(0, c), arg = p.Substring(c + 1);
                Vector3? r = null;
                try { r = ResolveOne(kind, arg, out what); } catch (Exception e) { what = "resolver exception " + e.Message; r = null; }
                if (r.HasValue) return r;
            }
            return null;
        }

        Vector3? ResolveOne(string kind, string arg, out string what)
        {
            what = kind + ":" + arg;
            var near = RedFortPos();
            switch (kind)
            {
                case "fort":
                {
                    var team = arg == "blue" ? Team.Blue : Team.Red;
                    var fs = W.Objects.Where(o => o != null && o.IsFort && !o.Destroyed && o.Owner == team).ToList();
                    if (fs.Count == 0) return null;
                    var s = Vector3.zero; foreach (var f in fs) s += Where(f);
                    what = $"{fs.Count} {team} fort part(s)"; return s / fs.Count;
                }
                case "bld":
                {
                    var a = arg.Split(':'); string type = a[0]; Team? team = a.Length > 1 ? (a[1] == "blue" ? Team.Blue : a[1] == "grey" ? Team.Neutral : Team.Red) : (Team?)null;
                    var o = W.Objects.Where(x => x != null && x.Kind == ZKind.Building && !x.Destroyed && x.Type == type && (team == null || x.Owner == team.Value))
                                     .OrderBy(x => (Where(x) - near).sqrMagnitude).FirstOrDefault();
                    if (o == null) return null;
                    what = $"{o.Type} #{o.Id} {o.Owner}"; return Where(o);
                }
                case "sim":
                {
                    var o = W.Objects.Where(x => x != null && !x.Destroyed && x.Type != null && x.Type.StartsWith(arg, StringComparison.Ordinal))
                                     .OrderBy(x => (Where(x) - near).sqrMagnitude).FirstOrDefault();
                    if (o == null) return null;
                    what = $"{o.Type} #{o.Id} {o.Owner}"; return Where(o);
                }
                case "go":
                {
                    foreach (var n in arg.Split('|'))
                    {
                        var go = GameObject.Find(n) ?? FindByName(n, false);
                        if (go) { what = "GameObject " + PathOf(go.transform); return Centre(go); }
                    }
                    return null;
                }
                case "goprefix":
                {
                    GameObject best = null; float bd = float.MaxValue; int n = 0;
                    foreach (var pre in arg.Split('|'))
                    {
                        foreach (var t in AllTransforms())
                        {
                            if (!t.gameObject.activeInHierarchy || !t.name.StartsWith(pre, StringComparison.Ordinal)) continue;
                            n++; float d = (Flat(t.position) - Flat(near)).sqrMagnitude;
                            if (d < bd) { bd = d; best = t.gameObject; }
                        }
                        if (best) break;
                    }
                    if (!best) return null;
                    what = $"GameObject {PathOf(best.transform)} (nearest of {n} to our fort)"; return Centre(best);
                }
                case "gate":
                {
                    GateKitView best = null; float bd = float.MaxValue; int n = 0;
                    foreach (var gk in FindObjectsByType<GateKitView>(FindObjectsSortMode.None))
                    {
                        if (!gk || !gk.isActiveAndEnabled) continue;
                        string key = ((gk.Key ?? "") + "|" + PathOf(gk.transform)).ToLowerInvariant();
                        bool fort = key.Contains("fort");
                        if ((arg == "fort") != fort) continue;
                        n++; float d = (Flat(gk.transform.position) - Flat(near)).sqrMagnitude;
                        if (d < bd) { bd = d; best = gk; }
                    }
                    if (!best) return null;
                    var dp = best.DoorPoint; if (dp == Vector3.zero) dp = best.transform.position;
                    what = $"GateKitView {best.Key} on {PathOf(best.transform)} (nearest of {n})"; return dp;
                }
                case "fortgun":
                {
                    var team = arg == "blue" ? Team.Blue : Team.Red;
                    foreach (var fg in FindObjectsByType<FortGunView>(FindObjectsSortMode.None))
                    {
                        if (!fg || fg.Fort == null || fg.Fort.Owner != team) continue;
                        Transform yawT = null; foreach (var t in fg.GetComponentsInChildren<Transform>(true)) if (t.name == fg.yawNode) { yawT = t; break; }
                        what = $"FortGunView on {PathOf(fg.transform)} ({team})"; return yawT ? yawT.position : fg.transform.position;
                    }
                    return null;
                }
                case "pos3":
                {
                    var xyz = arg.Split(',');
                    what = $"world ({xyz[0]}, {xyz[2]}, {xyz[1]})";
                    return new Vector3(float.Parse(xyz[0], CultureInfo.InvariantCulture), float.Parse(xyz[2], CultureInfo.InvariantCulture), float.Parse(xyz[1], CultureInfo.InvariantCulture));
                }
                case "pos":
                {
                    var xy = arg.Split(',');
                    float x = float.Parse(xy[0], CultureInfo.InvariantCulture), z = float.Parse(xy[1], CultureInfo.InvariantCulture);
                    float y = 0f; try { y = g.GroundY(x, z); } catch { }
                    what = $"world ({x:0.0}, {z:0.0})"; return new Vector3(x, y, z);
                }
            }
            return null;
        }

        static IEnumerable<Transform> AllTransforms()
        {
            for (int i = 0; i < SceneManager.sceneCount; i++)
            {
                var sc = SceneManager.GetSceneAt(i); if (!sc.isLoaded) continue;
                foreach (var r in sc.GetRootGameObjects()) foreach (var t in r.GetComponentsInChildren<Transform>(false)) yield return t;
            }
        }
        static GameObject FindByName(string n, bool prefix)
        {
            foreach (var t in AllTransforms()) if (t.gameObject.activeInHierarchy && (prefix ? t.name.StartsWith(n, StringComparison.Ordinal) : t.name == n)) return t.gameObject;
            return null;
        }
        static Vector3 Centre(GameObject go)
        {
            bool any = false; Bounds b = default;
            foreach (var r in go.GetComponentsInChildren<Renderer>(false))
            {
                if (!r.enabled || r is ParticleSystemRenderer) continue;
                if (!any) { b = r.bounds; any = true; } else b.Encapsulate(r.bounds);
            }
            return any ? b.center : go.transform.position;
        }
        static string PathOf(Transform t) { var s = t.name; for (var p = t.parent; p; p = p.parent) s = p.name + "/" + s; return s; }

        void Aim(Vector3 at, float h, float pitch)
        {
            cam.follow = null; cam.pitch = pitch;
            cam.focus = new Vector3(at.x, 0f, at.z);
            cam.SetHeightImmediate(h);
            var fwd = Flat(cam.transform.forward); if (fwd.sqrMagnitude > 1e-4f && at.y > 0.5f) cam.focus += fwd.normalized * (at.y * 0.9f);   // look at the object, not the ground under it (BundleFortProof.Focus)
            cam.SetHeightImmediate(h);
        }

        // ------------------------------------------------------------------ one shot: aim, settle (frame times measured), capture
        IEnumerator StaticShot(Shot s, bool night)
        {
            if (s.dayOnly && night) yield break;
            string name = $"{g.level}_{s.name}_{(night ? "night" : "day")}";
            if (!cam) { Row(name, night, "SKIPPED", null, "no RtsCamera"); yield break; }
            var at = Resolve(s.target, out string what);
            if (!at.HasValue) { Row(name, night, "MISSING", null, $"target {s.target} not found: skipped"); Mark($"MISSING {name}: {s.target}"); yield break; }
            float minH0 = cam.minHeight;
            try
            {
                if (s.low) { cam.minHeight = Mathf.Min(minH0, s.h); cam.yaw = s.yaw; cam.yawHeld = true; cam.unclamped = true; }
                Aim(at.Value, s.h, s.pitch);
                yield return Capture(name, night, s.settle, $"{what}; h {s.h:0.#} pitch {s.pitch:0} yaw {s.yaw:0}{(s.note != null ? "; " + s.note : "")}");
            }
            finally
            {
                if (s.low) { cam.minHeight = minH0; cam.yaw = 0f; cam.yawHeld = false; cam.unclamped = false; cam.pitch = 55f; cam.SetHeightImmediate(Mathf.Max(minH0, 55f)); }
            }
        }

        IEnumerator Capture(string name, bool night, float settle, string what)
        {
            float t0 = T;
            yield return WaitReal(Mathf.Max(0.3f, settle - 1f));
            frameMs.Clear(); measuring = true;
            yield return WaitReal(Mathf.Min(1f, settle));
            measuring = false;
            var ms = frameMs.ToArray();
            yield return new WaitForEndOfFrame();
            string file = Path.Combine(OutDir, name + ".png");
            Texture2D tex = null; string status = "OK";
            try
            {
                tex = ScreenCapture.CaptureScreenshotAsTexture();
                File.WriteAllBytes(file, tex.EncodeToPNG());
                if (tex.width != ShotW || tex.height != ShotH) status = $"OK (SIZE {tex.width}x{tex.height})";
            }
            catch (Exception e) { status = "FAILED"; what += "; capture exception " + e.Message; file = null; }
            finally { if (tex) Destroy(tex); }
            float avg = ms.Length > 0 ? ms.Average() : -1f, worst = ms.Length > 0 ? ms.Max() : -1f;
            Row(name, night, status, file, what, avg, worst, ms.Length);
            Mark($"{status} {name}: {what}; frames {ms.Length}, avg {avg:0.0} ms, worst {worst:0.0} ms ({T - t0:0.0} s)");
        }

        void Row(string name, bool night, string status, string file, string what, float avg = -1f, float worst = -1f, int frames = 0)
        {
            string lv = g ? g.level : job;
            shotRows.Add(string.Join("\t", name, lv, night ? "night" : "day", status, file != null ? Path.GetFileName(file) : "-",
                avg >= 0 ? avg.ToString("0.0", CultureInfo.InvariantCulture) : "-", worst >= 0 ? worst.ToString("0.0", CultureInfo.InvariantCulture) : "-",
                frames.ToString(), (what ?? "").Replace("\t", " ").Replace("\n", " ")));
            if (status == "MISSING" || status == "SKIPPED" || status == "FAILED") skipped.Add($"{name}: {status} {what}");
        }

        // ------------------------------------------------------------------ mid-battle with every unit type (both teams)
        public static readonly string[] Robots = { "grunt", "psycho", "sniper", "tough", "pyro", "laser", "electrobot" };
        public static readonly string[] Vehicles = { "jeep", "light", "medium", "heavy", "apc", "missile_launcher", "crane" };
        public static readonly string[] Cannons = { "gatling", "gun", "howitzer", "missile_cannon" };

        IEnumerator Battle()
        {
            if (!cam) { skipped.Add("battle: no RtsCamera"); yield break; }
            var red = W.Objects.Where(o => o != null && o.IsFort && !o.Destroyed && o.Owner == Team.Red).ToList();
            var blue = W.Objects.Where(o => o != null && o.IsFort && !o.Destroyed && o.Owner == Team.Blue).ToList();
            if (red.Count == 0 || blue.Count == 0) { skipped.Add("battle: a fort is missing"); yield break; }
            double rx = red.Average(o => o.CenterX), ry = red.Average(o => o.CenterY), bx = blue.Average(o => o.CenterX), by = blue.Average(o => o.CenterY);
            double mx = (rx + bx) * 0.5, my = (ry + by) * 0.5, dx = bx - rx, dy = by - ry, len = Math.Max(1.0, Math.Sqrt(dx * dx + dy * dy)); dx /= len; dy /= len;
            const double Gap = 4.5 * 16;                                       // each side 4.5 tiles from the middle: 9 tiles apart, inside every attack radius
            var made = new List<string>(); var failed = new List<string>();
            foreach (var team in new[] { Team.Red, Team.Blue })
            {
                double sx = team == Team.Red ? -1 : 1;
                double ax = mx + dx * Gap * sx, ay = my + dy * Gap * sx;
                int i = 0;
                foreach (var type in Robots.Concat(Vehicles).Concat(Cannons))
                {
                    double side = ((i % 6) - 2.5) * 2.2 * 16, back = (i / 6) * 2.2 * 16 * sx; i++;
                    int px = (int)(ax - dy * side + dx * back), py = (int)(ay + dx * side + dy * back);
                    try
                    {
                        var o = Spawn(type, team, px, py);
                        if (o != null) made.Add(team + " " + type); else failed.Add(team + " " + type);
                    }
                    catch (Exception e) { failed.Add($"{team} {type} ({e.GetType().Name})"); }
                }
                yield return null;
            }
            Mark($"battle staged: {made.Count} units ({string.Join(", ", made.Where(m => m.StartsWith("Red")).Select(m => m.Substring(4)))}); not made: {(failed.Count > 0 ? string.Join(", ", failed) : "none")}");
            if (failed.Count > 0) skipped.Add("battle: unit types not spawned: " + string.Join(", ", failed));
            var mid = ZGame.PxToWorld(mx, my); try { mid.y = g.GroundY(mid.x, mid.z); } catch { }
            Aim(mid, 48f, 55f);
            yield return WaitReal(5f);                                            // first shots, turrets turning, robots aiming
            yield return Capture($"{g.level}_battle_all_units_night", true, 1.6f, $"{made.Count} staged units (every type, both teams) at the map middle; h 48");
            yield return Guard(SetTod(DayTod), "battle day", 10f);
            Aim(mid, 48f, 55f);
            yield return WaitReal(1.5f);
            yield return Capture($"{g.level}_battle_all_units_day", false, 1.6f, $"{made.Count} staged units, day; h 48");
            Aim(mid, 30f, 45f);
            yield return Capture($"{g.level}_battle_all_units_day_close", false, 1.6f, "the same battle, close (h 30, pitch 45)");
            int killed = 0; foreach (var o in staged) if (Kill(o)) killed++;
            Mark($"battle over: {killed} staged units killed (their deaths and explosions are in the census)");
            yield return WaitReal(4f);
        }

        ZObject Spawn(string type, Team team, int px, int py)
        {
            bool robot = ZBuildLists.IsRobot(type);
            if (!OzCheatSpawner.FreeTile(W, px >> 4, py >> 4, robot ? 1 : 2, robot, out int tx, out int ty)) return null;
            ZObject o;
            if (robot)
            {
                o = W.CreateRobotGroup(type, tx * 16, ty * 16, team); if (o == null) return null;
                g.SpawnUnitView(o); foreach (var m in o.Minions) g.SpawnUnitView(m);
                staged.Add(o); staged.AddRange(o.Minions);
                Unkillable(o); foreach (var m in o.Minions) Unkillable(m);
            }
            else
            {
                o = W.CreateObject(ZBuildLists.IsCannon(type) ? "cannon" : "vehicle", type, tx * 16, ty * 16, team); if (o == null) return null;
                g.SpawnUnitView(o); staged.Add(o); Unkillable(o);
            }
            return o;
        }
        static void Unkillable(ZObject o) { o.MaxHealth = 1000000000; o.SetHealth(o.MaxHealth); }
        bool Kill(ZObject o)
        {
            if (o == null || o.Destroyed || W.Get(o.Id) != o) return false;
            try { o.Waypoints.Clear(); o.Disengage(); o.SetHealth(0); W.UpdateObjectHealth(o); return true; } catch { return false; }
        }

        // ------------------------------------------------------------------ settings: the pause menu options (audio sliders)
        IEnumerator PauseOptions()
        {
            var hud = OpenZHud.I;
            if (!hud || !hud.Pause) { Row($"{g.level}_settings_pause_options", false, "MISSING", null, "no OpenZHud / pause menu"); yield break; }
            if (cam) Aim(RedFortPos(), 70f, 55f);
            hud.Pause.Open();
            yield return WaitReal(0.3f);
            yield return Capture($"{g.level}_settings_pause_options", false, 1.4f, $"pause menu open: {hud.Pause.IsOpen}; OPTIONS (audio sliders, toggles)");
            hud.Pause.Close();
            yield return WaitReal(1f);
        }

        // ------------------------------------------------------------------ ending (WIN on L02, LOSS on L17)
        IEnumerator Ending(bool win)
        {
            string kind = win ? "win" : "loss";
            int n = OpenZVictory.DebugFortFalls(win);
            Mark($"ending {kind}: DebugFortFalls({win}) -> {n} fort part(s) destroyed");
            if (n == 0) { skipped.Add($"ending {kind}: no fort part to destroy"); yield break; }
            float f0 = T;
            yield return WaitReal(2.5f);
            yield return Capture($"{g.level}_ending_{kind}_fall", false, 0.6f, $"+{T - f0:0.0} s after the fort fell");
            while (T - f0 < 7f) yield return null;
            yield return Capture($"{g.level}_ending_{kind}_blast", false, 0.6f, $"+{T - f0:0.0} s after the fort fell");
            while (T - f0 < 11.5f) yield return null;
            yield return Capture($"{g.level}_ending_{kind}_aftermath", false, 0.6f, $"+{T - f0:0.0} s after the fort fell");
            var hud = OpenZHud.I;
            while (T - f0 < 75f && !(hud && hud.EndScreen && hud.EndScreen.IsShown)) yield return null;
            if (!(hud && hud.EndScreen && hud.EndScreen.IsShown)) { Row($"{g.level}_ending_{kind}_screen", false, "MISSING", null, "end screen not shown within 75 s"); yield break; }
            float shownAt = T - f0;
            yield return WaitReal(3f);                                            // band, plates and chart animate in
            yield return Capture($"{g.level}_ending_{kind}_screen", false, 1.2f, $"end screen shown at +{shownAt:0.0} s; winner {W.Winner}");
        }

        /// <summary>UI7 / DC18: the minimap at the U5s_selection_ctrlA.png resolution (1920x1080, L02, camera over our fort).</summary>
        IEnumerator MinimapFrame()
        {
            int idx0 = OzFinalGateView.SelectedIndex();
            OzFinalGateView.SetFixed(1920, 1080, "OpenZ Final Gate 1080", out string gv);
            for (int i = 0; i < 6; i++) yield return null;
            Mark($"minimap frame: {gv}; screen {Screen.width}x{Screen.height}");
            if (cam) Aim(RedFortPos(), 45f, 55f);
            yield return Capture($"{g.level}_minimap_1080", false, 1.6f, $"UI7 minimap at {Screen.width}x{Screen.height} (U5s_selection_ctrlA.png is 1920x1080), camera h 45 over our fort");
            OzFinalGateView.SetFixed(ShotW, ShotH, "OpenZ Final Gate", out gv);
            for (int i = 0; i < 6; i++) yield return null;
            Mark($"Game view back: {gv}; screen {Screen.width}x{Screen.height} (index before {idx0})");
        }

        /// <summary>VX38: LegacyMatchCheck F6 overlay values, the F7 explosion line-up (19 tiers) and the F8 weapon line-up, framed at the
        /// default zoom over open sand, with every OzSoundLog line of each line-up written next to the report.</summary>
        IEnumerator LegacyLineUp()
        {
            var amc = FindFirstObjectByType<LegacyMatchCheck>();
            if (!amc) { skipped.Add("VX38: no LegacyMatchCheck in Play"); yield break; }
            var red = W.Objects.Where(o => o != null && o.IsFort && !o.Destroyed && o.Owner == Team.Red).ToList();
            var blue = W.Objects.Where(o => o != null && o.IsFort && !o.Destroyed && o.Owner == Team.Blue).ToList();
            Vector3 mid = red.Count > 0 && blue.Count > 0
                ? ZGame.PxToWorld((red.Average(o => o.CenterX) + blue.Average(o => o.CenterX)) * 0.5, (red.Average(o => o.CenterY) + blue.Average(o => o.CenterY)) * 0.5)
                : cam.focus;
            mid += new Vector3(-14f, 0f, 10f);                                    // off the battle spot: clean sand
            Aim(mid, 70f, 55f);
            yield return WaitReal(1.2f);
            var au = CombatAudio.I; var eng = LegacyEngineAudio.I; var comp = FindFirstObjectByType<LegacyMixCompressor>();
            string f6 = (au ? $"duck {20f * Mathf.Log10(Mathf.Max(1e-4f, au.Duck)):0.0} dB, zoom {20f * Mathf.Log10(Mathf.Max(1e-4f, au.ZoomGain)):0.0} dB, range x{au.RangeScale:0.00}" : "no CombatAudio")
                + (eng ? $", engines {eng.ActiveLoops}/{eng.maxAudible}" : ", no engines") + (comp ? $", comp GR {comp.GainReductionDb:0.0} dB" : ", no compressor")
                + (au && au.ListenerRig ? $", listener y {au.ListenerRig.position.y:0.0}" : ", listener on camera") + $", camera h {cam.height:0}";
            int listeners = FindObjectsByType<AudioListener>(FindObjectsSortMode.None).Count(l => l.isActiveAndEnabled);
            Mark($"VX38 check 1 (F6 overlay values): {f6}; active AudioListeners {listeners}");
            var t = typeof(LegacyMatchCheck);
            var plan = new[] { ("Blasts", 28.5f, new[] { 8.6f, 19.0f, 26.4f }), ("Weapons", 26f, new[] { 4.0f, 11.2f, 15.6f }) };
            foreach (var (method, secs, frames) in plan)
            {
                var mi = t.GetMethod(method, BindingFlags.NonPublic | BindingFlags.Instance);
                if (mi == null) { skipped.Add($"VX38: LegacyMatchCheck.{method} not found"); continue; }
                float s0 = Time.unscaledTime, t0 = T;
                string tag = method == "Blasts" ? "F7" : "F8";
                amc.StopAllCoroutines(); amc.StartCoroutine((IEnumerator)mi.Invoke(amc, new object[] { new Vector3(mid.x, g.GroundY(mid.x, mid.z), mid.z) }));
                Mark($"VX38 {tag} {(method == "Blasts" ? "explosion" : "weapon")} line-up started");
                int k = 0;
                foreach (var at in frames)
                {
                    while (T - t0 < at - 0.6f) yield return null;
                    k++;
                    yield return Capture($"{g.level}_legacy_{tag}_{k}", false, 0.6f, $"LegacyMatchCheck {method} +{at:0.0} s");
                }
                while (T - t0 < secs) yield return null;
                List<OzSoundLog.Entry> sl;
                try { sl = OzSoundLog.Since(s0); } catch { sl = new List<OzSoundLog.Entry>(); }
                try { File.WriteAllText(Path.Combine(PartsDir, $"{g.level}_legacy_{tag}_ozsoundlog.txt"), OzSoundLog.Format(sl)); } catch { }
                var keys = sl.Where(e => e.outcome != null && e.outcome.StartsWith("PLAY")).GroupBy(e => e.key ?? "?").OrderByDescending(x => x.Count()).Select(x => x.Key + " x" + x.Count()).Take(30).ToList();
                Mark($"VX38 {tag}: {sl.Count(e => e.outcome != null && e.outcome.StartsWith("PLAY"))} PLAY / {sl.Count(e => e.outcome != null && e.outcome.StartsWith("DROP"))} DROP lines: {string.Join(", ", keys)}");
                comp = FindFirstObjectByType<LegacyMixCompressor>();
                Mark($"VX38 after {tag}: comp GR {(comp ? comp.GainReductionDb.ToString("0.0") : "-")} dB");
            }
        }

        /// <summary>GP51 (L17): a grunt squad and a vehicle column (light + heavy tank) at each fort and at the two factories nearest the
        /// map middle, ordered along the walls onto mesh-covered tiles; sampled every 0.25 s for 24 s: no unit may stand on a mesh tile.</summary>
        IEnumerator WallWalk()
        {
            var C = W.Clearance;
            if (C == null) { skipped.Add("GP51: no ZClearance"); yield break; }
            var blds = new List<ZObject>();
            var rf = W.Objects.FirstOrDefault(o => o != null && o.IsFort && o.IsProducer && !o.Destroyed && o.Owner == Team.Red); if (rf != null) blds.Add(rf);
            var bf = W.Objects.FirstOrDefault(o => o != null && o.IsFort && o.IsProducer && !o.Destroyed && o.Owner == Team.Blue); if (bf != null) blds.Add(bf);
            int cx = W.Map.WidthPix / 2, cy = W.Map.HeightPix / 2;
            blds.AddRange(W.Objects.Where(o => o != null && o.Kind == ZKind.Building && !o.Destroyed && !o.IsFort && !o.IsBridge && o.Type != null && o.Type.Contains("factory"))
                .OrderBy(o => Math.Abs(o.CenterX - cx) + Math.Abs(o.CenterY - cy)).Take(2));
            var units = new List<ZObject>(); var rows = new List<string>();
            foreach (var b in blds)
            {
                int bx = b.CenterX / 16, by = b.CenterY / 16;
                var mesh = new List<(int x, int y)>();
                for (int ty = by - 9; ty <= by + 9; ty++) for (int tx = bx - 9; tx <= bx + 9; tx++) if (C.MeshOwner(tx, ty) != 0 && !C.KeptOpenTile(tx, ty)) mesh.Add((tx, ty));
                if (mesh.Count == 0) { rows.Add($"{b.Type}#{b.Id}: no mesh tile found"); continue; }
                int dxm = Math.Sign(cx / 16 - bx), dym = Math.Sign(cy / 16 - by);
                if (dxm == 0 && dym == 0) dym = 1;
                var a = mesh.OrderBy(m => m.x * dym - m.y * dxm).First();
                var z = mesh.OrderBy(m => -(m.x * dym - m.y * dxm)).First();       // the two ends across the side facing the middle
                var face = mesh.OrderBy(m => -(m.x * dxm + m.y * dym)).First();    // the wall facing the middle
                int sx = bx + dxm * 8, sy = by + dym * 8;
                ZObject sq = null, l1 = null, h1 = null;
                if (OzCheatSpawner.FreeTile(W, sx, sy, 1, true, out int rx, out int ry)) sq = W.CreateRobotGroup("grunt", rx * 16, ry * 16, Team.Red);
                if (OzCheatSpawner.FreeTile(W, sx + 2, sy + 2, 2, false, out int vx, out int vy)) l1 = W.CreateObject("vehicle", "light", vx * 16, vy * 16, Team.Red);
                if (OzCheatSpawner.FreeTile(W, sx + 4, sy + 3, 2, false, out int hx, out int hy)) h1 = W.CreateObject("vehicle", "heavy", hx * 16, hy * 16, Team.Red);
                foreach (var u in new[] { sq, l1, h1 })
                {
                    if (u == null) continue;
                    g.SpawnUnitView(u); Unkillable(u); staged.Add(u); units.Add(u);
                    foreach (var m in u.Minions) { g.SpawnUnitView(m); Unkillable(m); staged.Add(m); units.Add(m); }
                    W.SendWaypoints(Team.Red, u, new List<ZWaypoint> { ZWaypoint.Move(a.x * 16 + 8, a.y * 16 + 8), ZWaypoint.Move(face.x * 16 + 8, face.y * 16 + 8), ZWaypoint.Move(z.x * 16 + 8, z.y * 16 + 8) });
                }
                rows.Add($"{b.Type}#{b.Id} {b.Owner}: {mesh.Count} mesh tiles; route {a.x},{a.y} -> {face.x},{face.y} -> {z.x},{z.y}; squad {(sq != null ? "yes" : "no")}, light {(l1 != null ? "yes" : "no")}, heavy {(h1 != null ? "yes" : "no")}");
            }
            Mark("GP51 wall walk: " + string.Join(" | ", rows));
            float t0 = T; int samples = 0, shot = 0; var inMesh = new Dictionary<string, int>();
            while (T - t0 < 24f)
            {
                yield return WaitReal(0.25f);
                foreach (var u in units)
                {
                    if (u == null || u.Destroyed) continue;
                    int tx = u.CenterX / 16, ty = u.CenterY / 16; samples++;
                    if (C.MeshOwner(tx, ty) != 0 && !C.KeptOpenTile(tx, ty))
                    {
                        string key = $"{u.Type}#{u.Id}@{tx},{ty}";
                        inMesh[key] = inMesh.TryGetValue(key, out int c) ? c + 1 : 1;
                    }
                }
                if (shot < blds.Count && T - t0 > 12f + shot * 2.5f)
                {
                    var b = blds[shot]; shot++;
                    Aim(Where(b), 40f, 55f);
                    yield return Capture($"{g.level}_gp51_wallwalk_{shot}", false, 0.8f, $"GP51 wall walk at {b.Type}#{b.Id} ({b.Owner})");
                }
            }
            bool ok = inMesh.Count == 0;
            Mark($"GP51 RESULT {(ok ? "PASS" : "FAIL")}: {units.Count} units, {samples} unit-samples over 24 s, standing on mesh tiles: {(ok ? "none" : string.Join(" ", inMesh.Select(x => x.Key + " x" + x.Value)))}");
            foreach (var u in units) Kill(u);
            yield return WaitReal(2f);
        }

        // ================================================================== main menu job
        IEnumerator MenuJob()
        {
            float w0 = T; OpenZMainMenu menu = null;
            while (!menu && T - w0 < 30f) { menu = FindFirstObjectByType<OpenZMainMenu>(); yield return null; }
            Mark($"main menu {(menu ? "up" : "MISSING")}; scene {SceneManager.GetActiveScene().name}; intro option {(OpenZ.Intro.OzIntro.Enabled ? "ON" : "OFF")}, intro running {OpenZ.Intro.OzIntro.Running}");
            if (!menu) { Row("menu_main", false, "MISSING", null, "no OpenZMainMenu in the scene"); yield break; }
            yield return WaitReal(7f);                                            // logo stamp-in, items, first flyby frame
            yield return Capture("menu_main", false, 1.6f, "main menu (flyby background)");
            var t = typeof(OpenZMainMenu);
            var open = t.GetMethod("OpenSide", BindingFlags.NonPublic | BindingFlags.Instance);
            var close = t.GetMethod("CloseSide", BindingFlags.NonPublic | BindingFlags.Instance);
            var sideT = t.GetNestedType("Side", BindingFlags.NonPublic);
            foreach (var (side, shot, what) in new[] { ("Roadmap", "menu_roadmap", "ROADMAP panel"), ("Credits", "menu_credits", "CREDITS panel"), ("Options", "menu_options_audio", "OPTIONS panel (audio sliders)") })
            {
                object sv = null;
                try { if (sideT != null && Enum.IsDefined(sideT, side)) sv = Enum.Parse(sideT, side); } catch { }
                if (open == null || sv == null) { Row(shot, false, "MISSING", null, $"OpenZMainMenu.OpenSide / Side.{side} not found"); continue; }
                var ps = open.GetParameters();
                try { open.Invoke(menu, ps.Length == 2 ? new[] { sv, (object)false } : new[] { sv }); }
                catch (Exception e) { var ie = e.InnerException ?? e; Row(shot, false, "FAILED", null, $"OpenSide({side}) threw {ie.GetType().Name}: {ie.Message}"); continue; }   // this panel only
                yield return WaitReal(0.4f);
                yield return Capture(shot, false, 1.6f, what);
                if (close != null) { try { close.Invoke(menu, null); } catch (Exception e) { Mark($"CloseSide after {side} threw {(e.InnerException ?? e).Message}"); } }
                yield return WaitReal(0.9f);
            }
        }

        // ================================================================== finish: part files for the editor's REPORT.md
        static readonly Dictionary<ZEventType, string> SoundPattern = new Dictionary<ZEventType, string>
        {
            [ZEventType.Hitscan] = "wpn|fire|shot|gun|bolt|laser|rifle|imp|hit|flame|pyro",
            [ZEventType.MissileFired] = "wpn|fire|launch|rocket|missile|shell|cannon|gun|bazooka|mortar|howitzer|grenade|throw",
            [ZEventType.MissileExploded] = "exp|imp|blast|explo|hit|boom|splash|debris|dst",
            [ZEventType.Death] = "death|die|cry|body|exp|imp|vo_|dst|wreck|blast|explo",
            [ZEventType.Sniped] = "snipe|imp|vo_|ann_|driver|eject",
            [ZEventType.DriverHit] = "driver|imp",
            [ZEventType.Entered] = "brd|enter|board|vo_|hatch|veh|door|capture",
            [ZEventType.Ejected] = "eject|brd|vo_|hatch|door",
            [ZEventType.GrenadesPicked] = "grenade|pickup|crate",
            [ZEventType.OwnerChanged] = "own|capture|ann_|flag|brd|zone",
            [ZEventType.FlagCaptured] = "flag|territory|ann_|capture|zone",
            [ZEventType.UnitBuilt] = "rollout|manufactured|door|built|bld|shutter|gate",
            [ZEventType.CannonStored] = "gun_stored|ann_gun|bld",
            [ZEventType.CannonPlaced] = "placed|bld",
            [ZEventType.RepairIn] = "repair",
            [ZEventType.RepairOut] = "repair",
            [ZEventType.CraneAnim] = "crane",
            [ZEventType.FortEntered] = "fort|breach",
            [ZEventType.TeamEliminated] = "fort|nuke|ann_|exp|blast",
            [ZEventType.GameOver] = "victory|defeat|win|lose|mus|ann_|end",
            [ZEventType.Promoted] = "promot",
            [ZEventType.RunOverHit] = "run|squash|crush|imp|over",
        };
        const float SoundWindowBefore = 0.1f, SoundWindowAfter = 1.0f;

        string Census()
        {
            var sb = new StringBuilder();
            List<OzSoundLog.Entry> sl;
            try { sl = OzSoundLog.Since(soundT0); } catch { sl = new List<OzSoundLog.Entry>(); }
            var plays = sl.Where(e => e.outcome != null && (e.outcome.StartsWith("PLAY") || e.outcome.StartsWith("ALARM START") || e.outcome.StartsWith("ALONG"))).OrderBy(e => e.t).ToList();
            var times = plays.Select(e => e.t).ToArray();
            sb.AppendLine($"OzSoundLog: {sl.Count} lines in this job ({plays.Count} PLAY, {sl.Count(e => e.outcome != null && e.outcome.StartsWith("DROP"))} DROP); ring buffer {OzSoundLog.Max} (older lines may have rolled out; the census reads what is left).");
            sb.AppendLine($"Window per event: {SoundWindowBefore:0.0} s before to {SoundWindowAfter:0.0} s after (heuristic: the matching line has a key / clip with a word that fits the event).");
            sb.AppendLine();
            sb.AppendLine("| sim event | seen | on screen | on screen with a fitting PLAY line | on screen with only another line | on screen with no line | verdict | lines heard (top keys) |");
            sb.AppendLine("|---|---|---|---|---|---|---|---|");
            float firstLog = sl.Count > 0 ? sl.Min(e => e.t) : float.MaxValue;
            foreach (var grp in seen.GroupBy(s => s.type).OrderBy(x => x.Key.ToString()))
            {
                SoundPattern.TryGetValue(grp.Key, out var pat);
                var rx = pat != null ? new Regex(pat, RegexOptions.IgnoreCase) : null;
                int on = 0, fit = 0, other = 0, none = 0; var heard = new Dictionary<string, int>();
                foreach (var s in grp)
                {
                    if (!s.onScreen || s.t < firstLog) continue;
                    on++;
                    int lo = LowerBound(times, s.t - SoundWindowBefore);
                    bool any = false, ok = false;
                    for (int k = lo; k < plays.Count && plays[k].t <= s.t + SoundWindowAfter; k++)
                    {
                        any = true; var e = plays[k];
                        if (rx == null || rx.IsMatch(e.key ?? "") || rx.IsMatch(e.clip ?? "")) { ok = true; heard[e.key ?? "?"] = heard.TryGetValue(e.key ?? "?", out var c) ? c + 1 : 1; }
                    }
                    if (ok) fit++; else if (any) other++; else none++;
                }
                string verdict = on == 0 ? "n/a (not on screen)" : fit > 0 && none == 0 && other == 0 ? "PASS" : fit > 0 ? "PASS (some unmatched: check)" : none > 0 && other == 0 ? "FAIL (silent)" : "FAIL? (no fitting line)";
                if (rx == null && on > 0) verdict = (none == on ? "FAIL (silent)" : "PASS (any line, no pattern)");
                sb.AppendLine($"| {grp.Key} | {grp.Count()} | {on} | {fit} | {other} | {none} | {verdict} | {string.Join(", ", heard.OrderByDescending(x => x.Value).Take(4).Select(x => x.Key + " " + x.Value))} |");
            }
            if (seen.Count == 0) sb.AppendLine("| (no sim events seen) | 0 | 0 | 0 | 0 | 0 | n/a | |");
            sb.AppendLine();
            var drops = sl.Where(e => e.outcome != null && e.outcome.StartsWith("DROP")).GroupBy(e => e.key + " (" + e.outcome + ")").OrderByDescending(x => x.Count()).Take(25).ToList();
            sb.AppendLine("OzSoundLog DROP reasons (top 25): " + (drops.Count == 0 ? "none" : string.Join("; ", drops.Select(d => d.Key + " x" + d.Count()))));
            sb.AppendLine();
            try { File.WriteAllText(Path.Combine(PartsDir, job + "_ozsoundlog.txt"), OzSoundLog.Format(sl)); } catch { }
            return sb.ToString();
        }
        static int LowerBound(float[] a, float v) { int lo = 0, hi = a.Length; while (lo < hi) { int m = (lo + hi) >> 1; if (a[m] < v) lo = m + 1; else hi = m; } return lo; }

        void Finish(string why)
        {
            if (done) return; done = true; measuring = false;
            stopReason = why;
            try
            {
                if (W != null)
                {
                    foreach (var o in staged) Kill(o);
                    if (savedBots.Count > 0 && W.Bots.Count == 0) W.Bots.AddRange(savedBots);
                }
                if (dn) { dn.running = dnRunning0; if (tod0 >= 0f) dn.timeOfDay = tod0; }
                Time.timeScale = 1f;
                Mark(why == null ? "DONE" : "STOPPED: " + why);
                var md = new StringBuilder();
                md.AppendLine($"### Job {job}: {(why == null ? "completed" : "STOPPED: " + why)}");
                md.AppendLine();
                md.AppendLine($"Console during the job: {errors} error(s) / exception(s), {warnings} warning(s). GPU peak (GpuGuard) {peakGpu} C. Screen {Screen.width}x{Screen.height}. Editor stalls not counted: {stalls} ({stallTotal:0} s, longest {stallMax:0.0} s).");
                md.AppendLine();
                if (job != "MENU")
                {
                    md.AppendLine("#### Sound census (sim events on screen vs OzSoundLog)");
                    md.AppendLine();
                    md.AppendLine(Census());
                    try
                    {
                        if (AudioDirector.I)
                        {
                            md.AppendLine("#### AudioDirector coverage (this job)");
                            md.AppendLine();
                            md.AppendLine(AudioDirector.I.CoverageReport(false));
                        }
                        else md.AppendLine("AudioDirector coverage: no AudioDirector in this scene.");
                    }
                    catch (Exception e) { md.AppendLine("AudioDirector coverage failed: " + e.Message); }
                    md.AppendLine();
                }
                md.AppendLine("#### Skipped / missing / failed in this job");
                md.AppendLine();
                if (skipped.Count == 0) md.AppendLine("- none");
                foreach (var s in skipped) md.AppendLine("- " + s);
                md.AppendLine();
                File.WriteAllText(Path.Combine(PartsDir, job + ".md"), md.ToString());
                File.WriteAllText(Path.Combine(PartsDir, job + "_shots.tsv"), string.Join("\n", shotRows) + "\n");
                File.WriteAllText(Path.Combine(PartsDir, job + "_console.tsv"), string.Join("\n", console) + "\n");
                File.WriteAllText(Path.Combine(PartsDir, job + "_log.txt"), log.ToString());
                File.WriteAllText(Path.Combine(PartsDir, job + ".done"), (why ?? "OK") + "\n");
            }
            catch (Exception e) { Debug.LogError("[FinalGate] finish: " + e); }
            string chain = UnityEditor.SessionState.GetString(ChainKey, "");
            if (!string.IsNullOrEmpty(chain) && (why == null || !why.StartsWith("heat")))
            {
                var list = chain.Split(new[] { '|' }, StringSplitOptions.RemoveEmptyEntries).ToList();
                string next = list[0]; list.RemoveAt(0);
                UnityEditor.SessionState.SetString(ChainKey, string.Join("|", list));
                if (ChainScene.TryGetValue(next, out var path))
                {
                    pendingJob = next; SceneManager.sceneLoaded -= OnChainLoaded; SceneManager.sceneLoaded += OnChainLoaded;
                    Debug.Log($"[FinalGate] job {job} done ({why ?? "OK"}); loading {path} for job {next} in the same Play session");
                    UnityEditor.SceneManagement.EditorSceneManager.LoadSceneInPlayMode(path, new LoadSceneParameters(LoadSceneMode.Single));
                    Destroy(gameObject);
                    return;
                }
            }
            UnityEditor.SessionState.EraseString(ChainKey);
            UnityEditor.EditorApplication.isPlaying = false;
        }
    }

    /// <summary>Game view fixed resolution (reflection on the editor's GameViewSizes; used by the runner's editor menu).</summary>
    public static class OzFinalGateView
    {
        const BindingFlags BF = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
        static Type GameViewType => typeof(UnityEditor.EditorWindow).Assembly.GetType("UnityEditor.GameView");

        public static int SelectedIndex()
        {
            try
            {
                var p = GameViewType?.GetProperty("selectedSizeIndex", BF); if (p == null) return -1;
                foreach (var w in Resources.FindObjectsOfTypeAll(GameViewType)) return (int)p.GetValue(w);
            }
            catch { }
            return -1;
        }

        public static void Select(int idx)
        {
            if (idx < 0) return;
            try
            {
                var p = GameViewType?.GetProperty("selectedSizeIndex", BF); if (p == null) return;
                foreach (var w in Resources.FindObjectsOfTypeAll(GameViewType)) p.SetValue(w, idx);
            }
            catch (Exception e) { Debug.LogWarning("[FinalGate] Game view size: " + e.Message); }
        }

        /// <summary>Finds (or adds) the fixed size w x h in the current group and selects it in every Game view. Returns the index or -1.</summary>
        public static int SetFixed(int w, int h, string label, out string info)
        {
            info = "";
            try
            {
                var asm = typeof(UnityEditor.Editor).Assembly;
                var sizesT = asm.GetType("UnityEditor.GameViewSizes");
                var single = typeof(UnityEditor.ScriptableSingleton<>).MakeGenericType(sizesT);
                var inst = single.GetProperty("instance", BindingFlags.Public | BindingFlags.Static).GetValue(null);
                var group = sizesT.GetProperty("currentGroup", BF).GetValue(inst);
                var gt = group.GetType();
                int total = (int)gt.GetMethod("GetTotalCount", BF).Invoke(group, null);
                var get = gt.GetMethod("GetGameViewSize", BF);
                int idx = -1;
                for (int i = 0; i < total && idx < 0; i++)
                {
                    var s = get.Invoke(group, new object[] { i }); var st = s.GetType();
                    int sw = (int)st.GetProperty("width", BF).GetValue(s), sh = (int)st.GetProperty("height", BF).GetValue(s);
                    string kind = st.GetProperty("sizeType", BF).GetValue(s).ToString();
                    if (sw == w && sh == h && kind == "FixedResolution") idx = i;
                }
                if (idx < 0)
                {
                    var gvsT = asm.GetType("UnityEditor.GameViewSize"); var typeT = asm.GetType("UnityEditor.GameViewSizeType");
                    var ctor = gvsT.GetConstructor(BF, null, new[] { typeT, typeof(int), typeof(int), typeof(string) }, null);
                    var ns = ctor.Invoke(new object[] { Enum.Parse(typeT, "FixedResolution"), w, h, label });
                    gt.GetMethod("AddCustomSize", BF).Invoke(group, new[] { ns });
                    idx = total; info = "added ";
                }
                Select(idx);
                info += $"Game view size index {idx} = {w}x{h}";
                return idx;
            }
            catch (Exception e) { info = "could not set the Game view size (" + e.GetType().Name + ": " + e.Message + "): set it to " + w + "x" + h + " by hand"; return -1; }
        }
    }
}
#endif
