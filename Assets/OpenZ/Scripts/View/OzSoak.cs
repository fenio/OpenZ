#if !OZ_RELEASE
using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text;
using OpenZ.Sim;
using Unity.Profiling;
using Unity.Profiling.LowLevel.Unsafe;
using UnityEngine;
using UnityEngine.Profiling;
using UnityEngine.SceneManagement;
using Debug = UnityEngine.Debug;

namespace OpenZ
{
    /// <summary>
    /// the one-click SOAK of a DEVELOPMENT player. Compiled out of the release exe (#if !OZ_RELEASE) and inert everywhere else: it only
    /// starts in a development player (Debug.isDebugBuild) started with -ozsoak &lt;minutes&gt; (GameBoot.DevArg). Never in the editor.
    ///  - DRIVE: main menu -> L02 -> menu -> L17 -> menu, 3 cycles (-ozsoakcycles n). Each level is entered through the menu's own DEPLOY
    ///    path (level select + loading screen, by reflection; a direct LoadSceneAsync if the menu changed) and left through OzScenes.MainMenu
    ///    (the pause menu's QUIT TO MAIN MENU). In the level both sides play on Zod bots (the -ozautopilot way: a bot on the player's
    ///    side, Zod's briefing and flyby stay live), the day / night clock runs one full day per level visit and a night storm is forced
    ///    at 45 % of the visit (OzWeather.Force). The time per level visit adapts so the whole run ends near &lt;minutes&gt;.
    ///  - LOG (next to the exe, soak_&lt;stamp&gt;\ or -ozsoakdir): soak.csv one row a second (frame time avg / p95 / p99 / worst, CPU / GPU
    ///    ms, GC alloc per frame, GC collections + collect ms, GC.GetTotalMemory, mono used / heap, total allocated / reserved, gfx driver,
    ///    the Memory profiler counters, process private bytes, live units, views, the last census); census.csv (GameObjects, Components,
    ///    Textures / RenderTextures, Meshes, Materials, AudioClips, AudioSources, ParticleSystems, inactive (pooled) GameObjects, the
    ///    static registries' and singleton pools' item totals); registries.csv (every static / singleton collection's Count at every
    ///    census); events.csv; hitches.csv; gc_spikes.csv; checkpoints (menu, after a clean) as checkpoint_*.txt; counters.txt.
    ///  - REPORT: SOAK_REPORT.md (OzSoakReport.cs) with the LEAK / hitch / GC / fps / sky-unload verdicts, then the player quits.
    /// Its own costs are measured and kept out of the verdicts: the census frames are flagged "soak" and their milliseconds logged.
    /// </summary>
    public sealed partial class OzSoak : MonoBehaviour
    {
        public static OzSoak I { get; private set; }
        public const string ArgName = "-ozsoak";

        // ------------------------------------------------------------------ settings (command line)
        float minutes = 20f, speed = 1f, censusEvery = 30f, menuDwell = 12f;
        int cycles = 3;
        string dir;
        readonly string[] levelOrder = { "L02", "L17" };

        // ------------------------------------------------------------------ run state
        string phase = "boot"; int cycle; string visitLevel = ""; float levelStart = -1f, runStart, deadline;
        int visitIndex = -1;
        readonly StringBuilder sb = new StringBuilder(1024);
        StreamWriter csv, censusCsv, regCsv, eventCsv;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Boot()
        {
            if (I || Application.isEditor || !GameBoot.DevArg(ArgName)) return;
            if (!Debug.isDebugBuild) { Debug.LogWarning("[OpenZ Soak] -ozsoak needs a Development player (menu OpenZ/Perf/Build soak player + run): ignored"); return; }
            var go = new GameObject("OZ_Soak"); DontDestroyOnLoad(go); I = go.AddComponent<OzSoak>();
        }

        static string ArgVal(string a)
        {
            var v = Environment.GetCommandLineArgs();
            for (int i = 0; i < v.Length - 1; i++) if (v[i].Equals(a, StringComparison.OrdinalIgnoreCase)) return v[i + 1];
            return null;
        }
        static float ArgF(string a, float def, float lo, float hi)
        {
            var s = ArgVal(a);
            return s != null && float.TryParse(s, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var f) ? Mathf.Clamp(f, lo, hi) : def;
        }

        void Awake()
        {
            minutes = ArgF(ArgName, 20f, 3f, 600f);
            cycles = Mathf.RoundToInt(ArgF("-ozsoakcycles", 3f, 1f, 20f));
            speed = ArgF("-ozsoakspeed", 1f, 1f, 4f);
            censusEvery = ArgF("-ozsoakcensus", 30f, 10f, 600f);
            string exeDir = Path.GetDirectoryName(Application.dataPath);
            dir = ArgVal("-ozsoakdir");
            if (string.IsNullOrEmpty(dir)) dir = Path.Combine(exeDir ?? ".", "soak_" + DateTime.Now.ToString("yyyyMMdd_HHmmss"));
            try { Directory.CreateDirectory(dir); }
            catch (Exception e) { Debug.LogWarning("[OpenZ Soak] cannot create " + dir + " (" + e.Message + "): using persistentDataPath"); dir = Path.Combine(Application.persistentDataPath, "soak_" + DateTime.Now.ToString("yyyyMMdd_HHmmss")); Directory.CreateDirectory(dir); }
            Application.logMessageReceived += OnLog;
            StartRecorders();
            OpenFiles();
            runStart = Time.unscaledTime; deadline = runStart + minutes * 60f;
            if (PlayerPrefs.GetInt("oz_botmatch", 0) == 1) { PlayerPrefs.SetInt("oz_botmatch", 0); PlayerPrefs.Save(); }   // a left-over demo flag would skip the briefing / flyby
            prefLevelBefore = PlayerPrefs.GetString(OpenZMainMenu.PrefLevel, "L02");
            Debug.Log($"[OpenZ Soak] ON: {minutes:0.#} min, {cycles} cycles (menu -> L02 -> menu -> L17 -> menu), game speed x{speed:0.#}, census every {censusEvery:0} s, " +
                      $"{Screen.width}x{Screen.height}, graphics {OzQuality.Name}, folder {dir}");
            Event("soak start " + minutes.ToString("0.#", Inv) + " min");
        }

        void Start() { StartCoroutine(Drive()); }

        void OnDestroy()
        {
            Application.logMessageReceived -= OnLog;
            StopRecorders();
            CloseFiles();
            if (I == this) I = null;
        }

        void OnApplicationQuit()
        {
            if (!reportWritten) { Event("player quit before the end"); try { WriteReport("the player quit before the soak ended"); } catch (Exception e) { Debug.LogWarning("[OpenZ Soak] report: " + e.Message); } }
        }

        // ================================================================== the drive
        string prefLevelBefore; bool reportWritten;
        IEnumerator Drive()
        {
            yield return WaitFor(() => IsMenu(), 90f, "main menu");
            if (!IsMenu()) { Event("no main menu after 90 s: loading it"); OzScenes.MainMenu(); yield return WaitFor(() => IsMenu(), 60f, "main menu"); }
            phase = "menu";
            yield return MenuCheckpoint(0, "start");
            int visitsTotal = cycles * levelOrder.Length, visitsDone = 0;
            for (int c = 1; c <= cycles; c++)
            {
                cycle = c;
                for (int k = 0; k < levelOrder.Length; k++)
                {
                    int menusLeft = (cycles - c) * levelOrder.Length + (levelOrder.Length - k);
                    float overhead = menusLeft * (menuDwell + 8f) + (visitsTotal - visitsDone) * 25f;   // menu dwell + clean, load + unload
                    float slice = Mathf.Max(30f, (deadline - Time.unscaledTime - overhead) / Mathf.Max(1, visitsTotal - visitsDone));
                    yield return PlayLevel(levelOrder[k], slice);
                    visitsDone++;
                    yield return ToMenu();
                    yield return MenuCheckpoint(c, k == levelOrder.Length - 1 ? "end" : "after " + levelOrder[k]);
                }
            }
            phase = "report";
            PlayerPrefs.SetString(OpenZMainMenu.PrefLevel, prefLevelBefore); PlayerPrefs.Save();         // the menu's DEPLOY saved our picks: the player's own last level back
            try { WriteReport(null); } catch (Exception e) { Debug.LogError("[OpenZ Soak] report failed: " + e); }
            yield return new WaitForSecondsRealtime(1f);
            Application.Quit();
        }

        static bool IsMenu() { var n = SceneManager.GetActiveScene().name; foreach (var m in OzScenes.MenuNames) if (n == m) return true; return false; }

        IEnumerator WaitFor(Func<bool> ok, float timeout, string what)
        {
            float t0 = Time.unscaledTime;
            while (!ok() && Time.unscaledTime - t0 < timeout) yield return null;
            if (!ok()) Event("timeout waiting for " + what);
        }

        IEnumerator ToMenu()
        {
            phase = "unload"; Event("to main menu");
            OzScenes.MainMenu();
            yield return null;
            yield return WaitFor(() => IsMenu(), 90f, "main menu");
            phase = "menu"; visitLevel = ""; levelStart = -1f;
        }

        // ------------------------------------------------------------------ one level visit
        sealed class Visit
        {
            public string level; public int cycle, index; public float start, end, slice;
            public bool flyover, skyLoaded, skyUnloaded, unloadChecked; public float skyMb = -1f, flyoverEnd = -1f; public string lastUnload = "";
            public bool storm; public int peakUnits; public string winner = "";
        }
        readonly List<Visit> visits = new List<Visit>();
        Visit cur;

        IEnumerator PlayLevel(string id, float slice)
        {
            phase = "load"; visitLevel = id; levelStart = -1f;
            OzLevelDef def = null; foreach (var d in OzLevelDef.All) if (d.Id == id) def = d;
            string scene = def != null ? def.Scene : (id == "L17" ? "L17_CarPark" : "L02_Desert");
            Event("deploy " + id + " (slice " + slice.ToString("0", Inv) + " s)");
            bool viaMenu = false;
            try { viaMenu = MenuDeploy(id); } catch (Exception e) { Event("menu deploy failed: " + e.Message); }
            if (!viaMenu) { Event("menu deploy unavailable: LoadSceneAsync " + scene); SceneManager.LoadSceneAsync(scene, LoadSceneMode.Single); }
            yield return WaitFor(() => SceneManager.GetActiveScene().name == scene && ZGame.I && ZGame.I.World != null, 120f, scene);
            var g = ZGame.I;
            if (!g || g.World == null) { Event("level " + id + " did not start"); yield break; }
            yield return null;
            phase = "match"; levelStart = Time.unscaledTime;
            cur = new Visit { level = id, cycle = cycle, index = ++visitIndex, start = levelStart, slice = slice };
            visits.Add(cur);
            Bots(g);
            if (speed > 1f) g.gameSpeed = speed;
            var dn = FindFirstObjectByType<DayNight>();
            if (dn) { dn.cycleSeconds = Mathf.Clamp(slice, 60f, 720f); dn.running = true; }
            Event($"match {id} c{cycle}: bots both sides, day cycle {(dn ? dn.cycleSeconds.ToString("0", Inv) + " s" : "n/a")}, weather option {(OzWeather.Enabled ? "ON" : "OFF")}");
            while (Time.unscaledTime - levelStart < slice)
            {
                TrackVisit(g);
                if (!cur.storm && Time.unscaledTime - levelStart > slice * 0.45f)
                {
                    cur.storm = true;
                    Event(OzWeather.Force(Mathf.Max(30f, slice * 0.4f)) ? "night storm forced" : "no OzWeather: storm skipped");
                }
                yield return null;
                if (!ZGame.I || ZGame.I != g) { Event("level object changed"); break; }
            }
            if (g && g.World != null && g.World.GameOver) cur.winner = g.World.Winner.ToString();
            cur.end = Time.unscaledTime;
            if (cur.flyover && !cur.unloadChecked) { cur.unloadChecked = true; cur.skyUnloaded = FlybySky.I == null || FlybySky.I.Panorama == null; cur.lastUnload = FlybySky.LastUnload; }
        }

        void Bots(ZGame g)
        {   // both sides on Zod bots without the demo pref: the briefing, the flyby and the transmissions stay as in a real match
            foreach (var t in new[] { Team.Red, Team.Blue })
            {
                bool has = false; foreach (var b in g.World.Bots) if (b.Team == t) has = true;
                if (!has) g.World.Bots.Add(new ZBot(g.World, t, g.seed + (t == g.player ? 202 : 101)) { Difficulty = ZBotDifficulty.Normal });
            }
        }

        void TrackVisit(ZGame g)
        {
            var fly = ZodFlyover.Live;
            bool flying = fly != null && fly.Active;
            if (flying && !cur.flyover) { cur.flyover = true; Event("Zod flyover start"); }
            var sky = FlybySky.I;
            if (sky && sky.Panorama)
            {
                if (!cur.skyLoaded) { cur.skyLoaded = true; long b = Profiler.GetRuntimeMemorySizeLong(sky.Panorama); cur.skyMb = b / 1048576f; Event("sky panorama resident " + cur.skyMb.ToString("0.0", Inv) + " MB"); }
            }
            if (cur.flyover && !flying && cur.flyoverEnd < 0f) { cur.flyoverEnd = Time.unscaledTime; Event("Zod flyover end"); }
            if (cur.flyoverEnd > 0f && !cur.unloadChecked && Time.unscaledTime - cur.flyoverEnd > 8f)
            {   // FlybySky unloads 2 s after its weight reaches 0 (the 0.25 s late fade)
                cur.unloadChecked = true;
                cur.skyUnloaded = !sky || !sky.Panorama;
                cur.lastUnload = FlybySky.LastUnload;
                Event("sky after flyover: " + (cur.skyUnloaded ? "unloaded (" + FlybySky.LastUnload + ")" : "STILL RESIDENT"));
            }
        }

        /// <summary>The menu's own DEPLOY (level select + loading screen) by reflection: GoSelect, focusCard, Deploy. False = not there.</summary>
        bool MenuDeploy(string id)
        {
            var menu = FindFirstObjectByType<OpenZMainMenu>();
            if (!menu) return false;
            const System.Reflection.BindingFlags F = System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Public;
            var t = typeof(OpenZMainMenu);
            var goSelect = t.GetMethod("GoSelect", F, null, Type.EmptyTypes, null);
            var deploy = t.GetMethod("Deploy", F, null, Type.EmptyTypes, null);
            var focus = t.GetField("focusCard", F);
            var levels = t.GetField("levels", F);
            if (goSelect == null || deploy == null || focus == null) return false;
            int idx = -1;
            if (levels != null && levels.GetValue(menu) is OzLevelDef[] arr) for (int i = 0; i < arr.Length; i++) if (arr[i].Id == id) idx = i;
            if (idx < 0) for (int i = 0; i < OzLevelDef.All.Length; i++) if (OzLevelDef.All[i].Id == id) idx = i;
            if (idx < 0) return false;
            goSelect.Invoke(menu, null);
            focus.SetValue(menu, idx);
            deploy.Invoke(menu, null);
            var view = t.GetField("view", F);
            return view == null || view.GetValue(menu)?.ToString() == "Loading";
        }

        // ------------------------------------------------------------------ menu checkpoint (after a clean: the leak comparison)
        sealed class Checkpoint
        {
            public int cycle; public string label; public float t;
            public readonly Dictionary<string, double> m = new Dictionary<string, double>();
            public Dictionary<string, int> types, names, regs;
            public bool trimmed;                                                                                 // soak_hitch_fix
        }
        readonly List<Checkpoint> checkpoints = new List<Checkpoint>();

        IEnumerator MenuCheckpoint(int c, string label)
        {
            phase = "menu";
            yield return new WaitForSecondsRealtime(menuDwell);                                        // menu previews play, the old level is gone
            phase = "menu-clean";
            Event($"checkpoint c{c} {label}: clean (UnloadUnusedAssets + GC.Collect)");
            yield return Resources.UnloadUnusedAssets();
            GC.Collect(); GC.WaitForPendingFinalizers(); GC.Collect();
            yield return null; yield return null;
            var sw = Stopwatch.StartNew();
            var cp = new Checkpoint { cycle = c, label = label, t = Time.unscaledTime };
            var cs = TakeCensus(true);
            cp.types = lastTypes; cp.names = lastNames; cp.regs = new Dictionary<string, int>(lastRegs);
            cp.m["managed_gc_total_mb"] = GC.GetTotalMemory(false) / MB;
            cp.m["mono_used_mb"] = Profiler.GetMonoUsedSizeLong() / MB;
            cp.m["mono_heap_mb"] = Profiler.GetMonoHeapSizeLong() / MB;
            cp.m["total_allocated_mb"] = Profiler.GetTotalAllocatedMemoryLong() / MB;
            cp.m["total_reserved_mb"] = Profiler.GetTotalReservedMemoryLong() / MB;
            cp.m["gfx_driver_mb"] = Profiler.GetAllocatedMemoryForGraphicsDriver() / MB;
            cp.m["gfx_used_mb"] = Rec(rGfx) / MB;
            cp.m["texture_mem_mb"] = Rec(rTexMem) / MB;
            cp.m["mesh_mem_mb"] = Rec(rMeshMem) / MB;
            cp.m["process_private_mb"] = ProcessPrivate() / MB;
            cp.m["gameobjects"] = cs.go; cp.m["components"] = cs.comp; cp.m["textures"] = cs.tex; cp.m["rendertextures"] = cs.rt;
            cp.m["meshes"] = cs.mesh; cp.m["materials"] = cs.mat; cp.m["audioclips"] = cs.clip; cp.m["audiosources"] = cs.asrc;
            cp.m["particlesystems"] = cs.ps; cp.m["inactive_gameobjects"] = cs.inactive; cp.m["registry_items"] = cs.regItems; cp.m["pool_items"] = cs.poolItems;
            checkpoints.Add(cp);
            WriteCheckpoint(cp);
            TrimCensus();                                                                                        // soak_hitch_fix
            soakWorkMs += (float)sw.Elapsed.TotalMilliseconds;
            Event($"checkpoint c{c} {label}: mono {cp.m["mono_used_mb"]:0.0} MB, total {cp.m["total_allocated_mb"]:0.0} MB, GO {cs.go}, materials {cs.mat}, textures {cs.tex}");
            phase = "menu";
            yield return new WaitForSecondsRealtime(2f);
        }

        /// <summary>soak_hitch_fix: every checkpoint kept its full name census (about 4,600 strings, ~0.6 MB of managed heap each, two a
        /// cycle): the soak's OWN data grew the managed-memory verdict by ~1.2 MB a cycle. The checkpoint file already holds it, and the
        /// report compares only the first and the last "end" checkpoints by name; the others keep just their Texture2D names (the
        /// menu-sky check).</summary>
        void TrimCensus()
        {
            Checkpoint firstEnd = null, lastEnd = null;
            foreach (var c in checkpoints) if (c.label == "end") { if (firstEnd == null) firstEnd = c; lastEnd = c; }
            foreach (var c in checkpoints)
            {
                if (c == firstEnd || c == lastEnd || c.trimmed || c.names == null) continue;
                var keep = new Dictionary<string, int>();
                foreach (var kv in c.names) if (kv.Key.StartsWith("Texture2D: ", StringComparison.Ordinal)) keep[kv.Key] = kv.Value;
                c.names = keep; c.trimmed = true;
            }
        }

        /// <summary>soak_hitch_fix: main-thread profiler markers read every frame; a hitch row names the biggest ones of the long frame
        /// (and the fixed steps of the frame after it), so the next soak shows WHERE a 300-500 ms frame went without a profiler session.</summary>
        static readonly string[] HitchMarkers =
        {
            "FixedUpdate.PhysicsFixedUpdate", "FixedBehaviourUpdate", "Physics.Simulate", "Physics.Processing", "Physics.FetchResults", "Physics.UpdateBodies",
            "Physics.ProcessReports", "Physics.Interpolation", "Physics.SyncTransforms", "Physics.BakeCollisionMeshes", "Physics.UpdateCooking",
            "Update.ScriptRunBehaviourUpdate", "PreLateUpdate.ScriptRunBehaviourLateUpdate", "Update.ScriptRunDelayedDynamicFrameRate", "Coroutines.DelayedCalls",
            "PreLateUpdate.DirectorUpdateAnimationBegin", "PreLateUpdate.DirectorUpdateAnimationEnd", "PostLateUpdate.UpdateAllSkinnedMeshes",
            "PostLateUpdate.UpdateAllRenderers", "PostLateUpdate.FinishFrameRendering", "PostLateUpdate.PresentAfterDraw", "PostLateUpdate.UpdateAudio",
            "Gfx.WaitForPresentOnGfxThread", "Gfx.WaitForRenderThread", "Gfx.WaitForGfxCommandsFromMainThread", "Gfx.PresentFrame",
            "Shader.CreateGPUProgram", "CreateGPUProgram", "Shader.Parse", "Loading.ReadObject", "Loading.AwakeFromLoad", "Loading.LockPersistentManager",
            "Mesh.UploadMeshData", "Texture.AwakeFromLoad", "GC.Collect", "GC.MarkDependencies", "Initialization.AsyncUploadTimeSlicedUpdate",
            "PreUpdate.AIUpdatePostScript", "Update.DirectorUpdate", "ParticleSystem.Update", "ParticleSystem.WaitForUpdateThreads",
            "PlayerLoop", "BehaviourUpdate", "LateBehaviourUpdate", "CoroutinesDelayedCalls", "Director.Update", "Animators.Update",
            "Physics.FetchPhysXResults", "Physics.UpdateRigidbodies", "Physics.BakePhysXCollisionMeshData", "GameObject.Activate", "GameObject.Deactivate",
            "Camera.Render", "Culling", "RenderLoop", "MeshSkinning.WaitForSkinThreads", "Device.Present", "Graphics.PresentAndSync",
            "GfxDeviceD3D11.WaitForQueuedPresentsToComplete", "GfxDeviceD3D12.WaitForGPU", "GarbageCollector.CollectIncremental",
            "Texture.WaitForAsyncUpload", "Async.UploadMesh", "ParticleSystem.EndUpdateAll",
        };
        readonly List<(string n, ProfilerRecorder r)> rHitch = new List<(string, ProfilerRecorder)>();
        int fixedSteps, fixedStepsLast;
        void FixedUpdate() { fixedSteps++; }

        // ================================================================== per-frame / per-second sampling
        const double MB = 1048576.0;
        static readonly System.Globalization.CultureInfo Inv = System.Globalization.CultureInfo.InvariantCulture;
        ProfilerRecorder rGcAlloc, rGcCount, rGfx, rTexMem, rMeshMem, rAudio, rVideo, rTexCount, rMeshCount, rMatCount, rGoCount, rObjCount;
        readonly List<ProfilerRecorder> rGcMarkers = new List<ProfilerRecorder>();
        readonly List<string> counterNames = new List<string>();
        readonly FrameTiming[] ft = new FrameTiming[1];
        readonly List<float> secDt = new List<float>(256), allMatchDt = new List<float>(1 << 16);
        double secCpu, secGpu, secGcBytes, secGcCount; int secCpuN, secGpuN, secGcN, secCollections; float secGcMax, secCollectMs, secStart = -1f;
        int lastCollections; long lastMonoUsed; float soakWorkMs; float nextCensus; long procPrivate; float nextProc;
        int heatSecond; float censusMs;

        void StartRecorders()
        {
            var handles = new List<ProfilerRecorderHandle>();
            ProfilerRecorderHandle.GetAvailable(handles);
            var byName = new Dictionary<string, ProfilerRecorderHandle>();
            foreach (var h in handles)
            {
                var d = ProfilerRecorderHandle.GetDescription(h);
                string n = d.Name ?? ""; counterNames.Add(d.Category.Name + " / " + n + " (" + d.UnitType + ")");
                string k = Norm(n); if (!byName.ContainsKey(k)) byName[k] = h;
            }
            ProfilerRecorder Start(params string[] names)
            {
                foreach (var n in names) if (byName.TryGetValue(Norm(n), out var h)) return new ProfilerRecorder(h, 1, ProfilerRecorderOptions.Default | ProfilerRecorderOptions.StartImmediately);
                return default;
            }
            rGcAlloc = Start("GC Allocated In Frame"); rGcCount = Start("GC Allocation In Frame Count");
            rGfx = Start("Gfx Used Memory"); rTexMem = Start("Texture Memory"); rMeshMem = Start("Mesh Memory");
            rAudio = Start("Audio Used Memory"); rVideo = Start("Video Used Memory");
            rTexCount = Start("Texture Count"); rMeshCount = Start("Mesh Count"); rMatCount = Start("Material Count");
            rGoCount = Start("Game Object Count", "GameObject Count", "GameObjects Count"); rObjCount = Start("Object Count");
            foreach (var n in new[] { "GC.Collect", "GC.MarkDependencies", "GC.ProcessFinalizers", "GC.Finalize" })
            {
                var r = Start(n); if (r.Valid) rGcMarkers.Add(r);
            }
            foreach (var n in HitchMarkers) { var r = Start(n); if (r.Valid) { r.Start(); rHitch.Add((n, r)); } }   // soak_hitch_fix: Start(): Default options do not record
            counterNames.Sort(StringComparer.Ordinal);
            try { File.WriteAllLines(Path.Combine(dir, "counters.txt"), counterNames); } catch (Exception) { }
            lastCollections = GC.CollectionCount(0); lastMonoUsed = Profiler.GetMonoUsedSizeLong();
        }

        static string Norm(string n) { var b = new StringBuilder(n.Length); foreach (var ch in n) if (ch != ' ' && ch != '_') b.Append(char.ToLowerInvariant(ch)); return b.ToString(); }
        static double Rec(ProfilerRecorder r) => r.Valid ? r.LastValue : -1;

        void StopRecorders()
        {
            foreach (var r in new[] { rGcAlloc, rGcCount, rGfx, rTexMem, rMeshMem, rAudio, rVideo, rTexCount, rMeshCount, rMatCount, rGoCount, rObjCount }) if (r.Valid) r.Dispose();
            foreach (var r in rGcMarkers) if (r.Valid) r.Dispose();
            rGcMarkers.Clear();
            foreach (var x in rHitch) if (x.r.Valid) x.r.Dispose();                                               // soak_hitch_fix
            rHitch.Clear();
        }

        void Update()
        {
            float now = Time.unscaledTime, dt = Time.unscaledDeltaTime * 1000f;
            if (secStart < 0f) secStart = now;
            // frame time + the soak's own work in the previous frame (this dt spans it; kept out of the hitch verdict)
            float own = soakWorkMs; soakWorkMs = 0f;
            fixedStepsLast = fixedSteps; fixedSteps = 0;                                                         // soak_hitch_fix: physics steps run this frame (after the long one)
            secDt.Add(dt);
            bool inMatch = phase == "match" && levelStart > 0f && now - levelStart > 3f;
            if (inMatch && !Heat()) allMatchDt.Add(dt);
            FrameTimingManager.CaptureFrameTimings();
            if (FrameTimingManager.GetLatestTimings(1, ft) > 0)
            {
                if (ft[0].cpuMainThreadFrameTime > 0) { secCpu += ft[0].cpuMainThreadFrameTime; secCpuN++; }
                if (ft[0].gpuFrameTime > 0) { secGpu += ft[0].gpuFrameTime; secGpuN++; }
            }
            // GC: bytes allocated this frame (profiler counter, else the mono-used delta), collections, collect time
            double gcBytes;
            long mono = Profiler.GetMonoUsedSizeLong();
            if (rGcAlloc.Valid) gcBytes = rGcAlloc.LastValue; else gcBytes = Math.Max(0, mono - lastMonoUsed);
            lastMonoUsed = mono;
            secGcBytes += gcBytes; secGcN++; if (gcBytes > secGcMax) secGcMax = (float)gcBytes;
            if (rGcCount.Valid) secGcCount += rGcCount.LastValue;
            int col = GC.CollectionCount(0);
            float collectMs = 0f; foreach (var r in rGcMarkers) collectMs += r.LastValue / 1e6f;
            bool collected = col != lastCollections;
            if (collected) { secCollections += col - lastCollections; lastCollections = col; }
            if (collectMs > secCollectMs) secCollectMs = collectMs;
            if (collectMs > 2f) GcSpike(now, collectMs, collected);
            if (dt > 50f) Hitch(now, dt, own, collectMs, collected);
            // census (low rate, heavy) and the 1 s row
            if (phase == "match" && now >= nextCensus) { nextCensus = now + censusEvery; var sw = Stopwatch.StartNew(); TakeCensus(false); censusMs = (float)sw.Elapsed.TotalMilliseconds; soakWorkMs += censusMs; }
            if (now - secStart >= 1f) FlushSecond(now);
        }

        bool Heat() => Application.targetFrameRate > 0 && Application.targetFrameRate < GameBoot.CapFps;   // HeatGuardRuntime's 45 / 30 fps steps

        // ------------------------------------------------------------------ events, log ring, hitches, GC spikes
        struct LogLine { public float t; public string s; public LogType type; }
        readonly LogLine[] logRing = new LogLine[96]; int logHead, logCount, errors, exceptions;
        readonly List<(float t, string s)> events = new List<(float, string)>();
        string secEvents = "";

        void OnLog(string msg, string stack, LogType type)
        {
            if (type == LogType.Exception) exceptions++; else if (type == LogType.Error) errors++;
            if (msg != null && msg.StartsWith("[OpenZ Soak]", StringComparison.Ordinal)) return;
            logRing[logHead] = new LogLine { t = Time.unscaledTime, s = msg != null && msg.Length > 180 ? msg.Substring(0, 180) : msg, type = type };
            logHead = (logHead + 1) % logRing.Length; if (logCount < logRing.Length) logCount++;
        }

        void Event(string s)
        {
            float t = Time.unscaledTime;
            events.Add((t, s));
            secEvents = secEvents.Length == 0 ? s : secEvents + " | " + s;
            if (eventCsv != null) eventCsv.WriteLine(F(t - runStart) + "," + cycle + "," + phase + "," + Csv(SceneManager.GetActiveScene().name) + "," + Csv(s));
            Debug.Log("[OpenZ Soak] " + s);
        }

        string Context(float t, float back)
        {
            var b = new StringBuilder();
            foreach (var e in events) if (e.t >= t - back && e.t <= t) b.Append("event: ").Append(e.s).Append("; ");
            int n = 0;
            for (int i = 0; i < logCount && n < 4; i++)
            {
                var l = logRing[(logHead - 1 - i + logRing.Length * 2) % logRing.Length];
                if (l.t < t - back) break;
                if (l.t <= t) { b.Append("log: ").Append(l.s).Append("; "); n++; }
            }
            try
            {
                var snd = OzSoundLog.Since(t - back); var seen = new HashSet<string>(); int k = 0;
                foreach (var e in snd) if (k < 5 && !string.IsNullOrEmpty(e.key) && seen.Add(e.key)) { b.Append(k == 0 ? "sounds: " : ", ").Append(e.key); k++; }
                if (k > 0) b.Append("; ");
            }
            catch (Exception) { }
            var g = ZGame.I;
            if (g && g.World != null) b.Append("units ").Append(LiveUnits(g)).Append(", game t ").Append(g.World.Time.ToString("0", Inv)).Append(" s");
            return b.ToString().Replace('\n', ' ').Replace('\r', ' ');
        }

        sealed class Hitch_ { public float t, dt, own, gcMs; public bool gc; public string phase, scene, kind, ctx; public int cycle; public float levelT; }
        readonly List<Hitch_> hitches = new List<Hitch_>();
        sealed class Spike { public float t, ms; public bool collected; public string phase, scene; public int cycle; }
        readonly List<Spike> spikes = new List<Spike>();
        StreamWriter hitchCsv, spikeCsv;

        void Hitch(float now, float dt, float own, float gcMs, bool gc)
        {
            string kind;
            if (phase != "match") kind = "load";                                                          // load, unload, menu, menu clean
            else if (levelStart < 0f || now - levelStart <= 3f) kind = "load";                            // the first 3 s of a level: the load tail
            else if (own > 0f && dt - own < 50f) kind = "soak";                                           // our own census / checkpoint work
            else kind = "play";
            var h = new Hitch_ { t = now, dt = dt, own = own, gcMs = gcMs, gc = gc, phase = phase, scene = SceneManager.GetActiveScene().name, kind = kind, cycle = cycle, levelT = levelStart > 0f ? now - levelStart : -1f };
            h.ctx = Context(now, 1.5f) + HitchMarkerText(dt);                                                    // soak_hitch_fix
            hitches.Add(h);
            hitchCsv?.WriteLine(F(now - runStart) + "," + cycle + "," + h.phase + "," + Csv(h.scene) + "," + F(h.levelT) + "," + F(dt) + "," + kind + "," + F(own) + "," + F(gcMs) + "," + (gc ? 1 : 0) + "," + Csv(h.ctx));
        }

        /// <summary>soak_hitch_fix: "; frame: <marker> N ms, ... ; fixed steps N, max dt S" (markers over 5 ms of the long frame, biggest first; PlayerLoop = the whole main loop, so a small one means the time went outside it).</summary>
        string HitchMarkerText(float dt)
        {
            var top = new List<(string n, double ms)>();
            foreach (var x in rHitch) { if (!x.r.Valid) continue; double ms = x.r.LastValue / 1e6; if (ms >= 5.0) top.Add((x.n, ms)); }
            top.Sort((a, b) => b.ms.CompareTo(a.ms));
            var b2 = new StringBuilder("; frame:");
            if (top.Count == 0) b2.Append(" no marker over 5 ms (outside the script / physics / render markers: driver, OS or another process)");
            for (int i = 0; i < top.Count && i < 8; i++) b2.Append(i == 0 ? " " : ", ").Append(top[i].n).Append(' ').Append(top[i].ms.ToString("0.0", Inv)).Append(" ms");
            b2.Append("; fixed steps next frame ").Append(fixedStepsLast).Append(" (max dt ").Append(Time.maximumDeltaTime.ToString("0.###", Inv)).Append(" s, fixed ").Append(Time.fixedDeltaTime.ToString("0.###", Inv)).Append(" s)");
            b2.Append("; wreck runtime clips ").Append(WreckPieces.RuntimeClipSets).Append(" (worst ").Append(WreckPieces.RuntimeClipMsMax.ToString("0.0", Inv)).Append(" ms), baked hits ").Append(WreckPieces.BakedHits);
            return b2.ToString();
        }

        void GcSpike(float now, float ms, bool collected)
        {
            var s = new Spike { t = now, ms = ms, collected = collected, phase = phase, scene = SceneManager.GetActiveScene().name, cycle = cycle };
            spikes.Add(s);
            spikeCsv?.WriteLine(F(now - runStart) + "," + cycle + "," + phase + "," + Csv(s.scene) + "," + F(ms) + "," + (collected ? 1 : 0));
        }

        // ------------------------------------------------------------------ the 1 s row
        sealed class Sec { public float t, levelT, fps, avg, p95, p99, worst, cpu, gpu, gcKb, gcMaxKb, collectMs, monoMb, totalMb, gfxMb; public int collections; public string phase, level; public int cycle, visit; public bool heat; public int units; }
        readonly List<Sec> seconds = new List<Sec>(4096);

        void FlushSecond(float now)
        {
            float span = now - secStart; int n = secDt.Count;
            double sum = 0; float worst = 0f; foreach (var d in secDt) { sum += d; if (d > worst) worst = d; }
            secDt.Sort();
            float p95 = n > 0 ? secDt[Mathf.Clamp(Mathf.CeilToInt(0.95f * n) - 1, 0, n - 1)] : -1f, p99 = n > 0 ? secDt[Mathf.Clamp(Mathf.CeilToInt(0.99f * n) - 1, 0, n - 1)] : -1f;
            if (now >= nextProc) { nextProc = now + 5f; procPrivate = ProcessPrivate(); }
            var g = ZGame.I; int units = g && g.World != null ? LiveUnits(g) : 0;
            bool heat = Heat(); if (heat) heatSecond++;
            var s = new Sec
            {
                t = now - runStart, levelT = levelStart > 0f ? now - levelStart : -1f, fps = span > 0f ? n / span : 0f, avg = n > 0 ? (float)(sum / n) : -1f, p95 = p95, p99 = p99, worst = worst,
                cpu = secCpuN > 0 ? (float)(secCpu / secCpuN) : -1f, gpu = secGpuN > 0 ? (float)(secGpu / secGpuN) : -1f, gcKb = secGcN > 0 ? (float)(secGcBytes / secGcN / 1024.0) : -1f, gcMaxKb = secGcMax / 1024f,
                collectMs = secCollectMs, collections = secCollections, monoMb = (float)(Profiler.GetMonoUsedSizeLong() / MB), totalMb = (float)(Profiler.GetTotalAllocatedMemoryLong() / MB),
                gfxMb = (float)(Profiler.GetAllocatedMemoryForGraphicsDriver() / MB), phase = phase, level = visitLevel, cycle = cycle, visit = phase == "match" ? visitIndex : -1, heat = heat, units = units,
            };
            seconds.Add(s);
            if (csv != null)
            {
                sb.Length = 0;
                sb.Append(F(s.t)).Append(',').Append(DateTime.Now.ToString("HH:mm:ss", Inv)).Append(',').Append(cycle).Append(',').Append(phase).Append(',').Append(Csv(SceneManager.GetActiveScene().name)).Append(',').Append(F(s.levelT)).Append(',')
                  .Append(n).Append(',').Append(F(s.fps)).Append(',').Append(F(s.avg)).Append(',').Append(F(p95)).Append(',').Append(F(p99)).Append(',').Append(F(worst)).Append(',')
                  .Append(F(s.cpu)).Append(',').Append(F(s.gpu)).Append(',').Append(F(s.gcKb)).Append(',').Append(F(s.gcMaxKb)).Append(',').Append(F(secGcN > 0 ? (float)(secGcCount / secGcN) : -1f)).Append(',')
                  .Append(secCollections).Append(',').Append(F(secCollectMs)).Append(',')
                  .Append(F((float)(GC.GetTotalMemory(false) / MB))).Append(',').Append(F(s.monoMb)).Append(',').Append(F((float)(Profiler.GetMonoHeapSizeLong() / MB))).Append(',')
                  .Append(F(s.totalMb)).Append(',').Append(F((float)(Profiler.GetTotalReservedMemoryLong() / MB))).Append(',').Append(F(s.gfxMb)).Append(',')
                  .Append(F((float)(Rec(rGfx) / MB))).Append(',').Append(F((float)(Rec(rTexMem) / MB))).Append(',').Append(F((float)(Rec(rMeshMem) / MB))).Append(',')
                  .Append(F((float)(Rec(rAudio) / MB))).Append(',').Append(F((float)(Rec(rVideo) / MB))).Append(',').Append(F((float)(procPrivate / MB))).Append(',')
                  .Append(Rec(rGoCount).ToString("0", Inv)).Append(',').Append(Rec(rObjCount).ToString("0", Inv)).Append(',').Append(Rec(rTexCount).ToString("0", Inv)).Append(',')
                  .Append(Rec(rMeshCount).ToString("0", Inv)).Append(',').Append(Rec(rMatCount).ToString("0", Inv)).Append(',')
                  .Append(last.go).Append(',').Append(last.comp).Append(',').Append(last.tex).Append(',').Append(last.rt).Append(',').Append(last.mesh).Append(',').Append(last.mat).Append(',')
                  .Append(last.clip).Append(',').Append(last.asrc).Append(',').Append(last.ps).Append(',').Append(last.inactive).Append(',').Append(last.poolItems).Append(',').Append(last.regItems).Append(',')
                  .Append(units).Append(',').Append(g && g.World != null ? g.World.Objects.Count : 0).Append(',').Append(g ? g.Views.Count : 0).Append(',')
                  .Append(HeatGuardRuntime.GpuTempC).Append(',').Append(heat ? "HEAT" : "OK").Append(',').Append(Application.targetFrameRate).Append(',').Append(F(censusMs)).Append(',').Append(Csv(secEvents));
                csv.WriteLine(sb.ToString());
            }
            secDt.Clear(); secCpu = secGpu = secGcBytes = secGcCount = 0; secCpuN = secGpuN = secGcN = secCollections = 0; secGcMax = 0f; secCollectMs = 0f; secEvents = ""; censusMs = 0f;
            secStart = now;
            if (cur != null && phase == "match" && units > cur.peakUnits) cur.peakUnits = units;
        }

        static int LiveUnits(ZGame g)
        {
            int n = 0;
            foreach (var o in g.World.Objects) if (o != null && !o.Destroyed && (o.Kind == ZKind.Robot || o.Kind == ZKind.Vehicle || o.Kind == ZKind.Cannon)) n++;
            return n;
        }

        static long ProcessPrivate()
        {
            try { using (var p = Process.GetCurrentProcess()) return p.PrivateMemorySize64; } catch (Exception) { return -1; }
        }

        static string F(float v) => v.ToString("0.###", Inv);
        static string Csv(string s) => string.IsNullOrEmpty(s) ? "" : (s.IndexOf(',') >= 0 || s.IndexOf('"') >= 0 ? "\"" + s.Replace("\"", "'") + "\"" : s);

        void OpenFiles()
        {
            StreamWriter W(string name, string header)
            {
                try { var w = new StreamWriter(Path.Combine(dir, name), false, new UTF8Encoding(false)) { AutoFlush = true }; w.WriteLine(header); return w; }
                catch (Exception e) { Debug.LogWarning("[OpenZ Soak] " + name + ": " + e.Message); return null; }
            }
            csv = W("soak.csv", "t_s,clock,cycle,phase,scene,level_t_s,frames,fps,ft_avg_ms,ft_p95_ms,ft_p99_ms,ft_worst_ms,cpu_main_ms,gpu_ms,gc_alloc_kb_per_frame,gc_alloc_kb_max_frame,gc_allocs_per_frame," +
                           "gc_collections,gc_collect_ms_max,gc_total_mb,mono_used_mb,mono_heap_mb,total_allocated_mb,total_reserved_mb,gfx_driver_mb,gfx_used_mb,texture_mem_mb,mesh_mem_mb,audio_mb,video_mb,process_private_mb," +
                           "ctr_gameobjects,ctr_objects,ctr_textures,ctr_meshes,ctr_materials,census_gameobjects,census_components,census_textures,census_rendertextures,census_meshes,census_materials," +
                           "census_audioclips,census_audiosources,census_particlesystems,census_inactive_go,pool_items,registry_items,live_units,world_objects,views,gpu_c,heat,target_fps,soak_census_ms,events");
            censusCsv = W("census.csv", "t_s,cycle,phase,scene,level_t_s,gameobjects,active_go,inactive_go,components,textures,texture2d,rendertextures,cubemaps,meshes,materials,audioclips,audiosources,audiosources_playing,particlesystems,particles_playing,pool_items,registry_items,census_ms");
            regCsv = W("registries.csv", "t_s,cycle,phase,scene,registry,count");
            eventCsv = W("events.csv", "t_s,cycle,phase,scene,event");
            hitchCsv = W("hitches.csv", "t_s,cycle,phase,scene,level_t_s,frame_ms,kind,soak_work_ms,gc_ms,gc_collected,context");
            spikeCsv = W("gc_spikes.csv", "t_s,cycle,phase,scene,gc_ms,collected");
        }

        void CloseFiles()
        {
            foreach (var w in new[] { csv, censusCsv, regCsv, eventCsv, hitchCsv, spikeCsv }) try { w?.Dispose(); } catch (Exception) { }
            csv = censusCsv = regCsv = eventCsv = hitchCsv = spikeCsv = null;
        }
    }
}
#endif
