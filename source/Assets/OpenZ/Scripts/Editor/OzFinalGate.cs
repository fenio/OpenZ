using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Text.RegularExpressions;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace OpenZ.EditorTools
{
    /// <summary>
    ///  1. saves the open scenes, archives an older final_gate run into final_gate/_prev/&lt;stamp&gt;/, selects a fixed 2560x1440 Game view;
    ///  2. three Play sessions one after the other (no clicks): L02_Desert, L17_CarPark, MainMenu; each runs OzFinalGateRunner (View/),
    ///  3. back in edit mode: the read checks (OpenZ/Run Parity Report with its Deviations block, a headless bot match per level
    /// Robust: every Play job has a timeout (the editor stops Play at 20 min), every check runs in its own try / catch, a stopped job
    /// (heat, watchdog) ends the remaining Play jobs but the report is still written. OpenZ/Final Gate/Abort stops at any point.
    /// Test mode: the run needs the full look; a set oz_testmode is switched off for the run and put back after it (session only).
    /// </summary>
    [InitializeOnLoad]
    public static partial class OzFinalGate
    {
        const string KActive = "OpenZ.FinalGate.Active", KStages = "OpenZ.FinalGate.Stages", KIdx = "OpenZ.FinalGate.Idx", KAwait = "OpenZ.FinalGate.AwaitExit",
                     KScene = "OpenZ.FinalGate.Scene", KGvIdx = "OpenZ.FinalGate.GvIdx", KTest = "OpenZ.FinalGate.TestMode", KStart = "OpenZ.FinalGate.Start",
                     KStageStart = "OpenZ.FinalGate.StageStart", KStop = "OpenZ.FinalGate.Stop", KChecks = "OpenZ.FinalGate.Checks", KBotSeconds = "OpenZ.FinalGate.BotSeconds",
                     KPlayStart = "OpenZ.FinalGate.PlayStart";
        public static float StageTimeoutSeconds = 1200f;
        static string Out => OzFinalGateRunner.OutDir;
        static string Parts => OzFinalGateRunner.PartsDir;
        static readonly Dictionary<string, string> SceneOf = new Dictionary<string, string>
        {
            ["L02"] = "Assets/OpenZ/Scenes/L02_Desert.unity", ["L17"] = "Assets/OpenZ/Scenes/L17_CarPark.unity", ["MENU"] = "Assets/OpenZ/Scenes/MainMenu.unity"
        };

        static OzFinalGate()
        {
            EditorApplication.playModeStateChanged += OnPlayMode;
            EditorApplication.update += Tick;
            EditorApplication.delayCall += () => { if (Active && !EditorApplication.isPlayingOrWillChangePlaymode) StageEnded("resumed after a domain reload"); };
        }

        static bool Active => SessionState.GetBool(KActive, false);
        static string[] Stages => SessionState.GetString(KStages, "").Split(new[] { '|' }, StringSplitOptions.RemoveEmptyEntries);

        // ------------------------------------------------------------------ menus
        [MenuItem("OpenZ/Final Gate/Run capture set (L02 + L17)", false, 1)]
        public static void RunAll() { SessionState.EraseString(OzFinalGateRunner.ChainKey); Begin("L02|L17|MENU", true); }

        /// <summary>
        /// instead of three (about 10 min each here). No read checks (run them with the menu below).</summary>
        [MenuItem("OpenZ/Final Gate/Run capture set in ONE Play (L02 -> L17 -> menu)", false, 0)]
        public static void RunChain() { SessionState.SetString(OzFinalGateRunner.ChainKey, "L17|MENU"); Begin("L02", false); }

        [MenuItem("OpenZ/Final Gate/Run capture set, L02 only", false, 2)]
        static void RunL02() { SessionState.EraseString(OzFinalGateRunner.ChainKey); Begin("L02", false); }
        [MenuItem("OpenZ/Final Gate/Run capture set, L17 only", false, 3)]
        static void RunL17() { SessionState.EraseString(OzFinalGateRunner.ChainKey); Begin("L17", false); }
        [MenuItem("OpenZ/Final Gate/Run capture set, main menu only", false, 4)]
        static void RunMenu() { SessionState.EraseString(OzFinalGateRunner.ChainKey); Begin("MENU", false); }

        [MenuItem("OpenZ/Final Gate/Read checks only (edit mode) + REPORT.md", false, 20)]
        public static void ChecksOnly()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode) { Debug.LogError("[FinalGate] stop Play first"); return; }
            Directory.CreateDirectory(Out); Directory.CreateDirectory(Parts);
            SessionState.SetString(KStart, DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"));
            WriteReport(RunChecks(true));
        }

        [MenuItem("OpenZ/Final Gate/Write REPORT.md from the last run (no checks)", false, 21)]
        static void ReportOnly() => WriteReport(File.Exists(Path.Combine(Parts, "CHECKS.md")) ? File.ReadAllText(Path.Combine(Parts, "CHECKS.md")) : "(the read checks did not run)\n");

        [MenuItem("OpenZ/Final Gate/Abort", false, 40)]
        public static void Abort()
        {
            if (!Active) { Debug.Log("[FinalGate] nothing running"); return; }
            SessionState.SetString(KStop, "aborted by hand");
            if (EditorApplication.isPlaying) EditorApplication.isPlaying = false; else Wrap("aborted by hand");
        }

        // ------------------------------------------------------------------ start
        static void Begin(string stages, bool checks)
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode) { Debug.LogError("[FinalGate] stop Play first"); return; }
            if (Active) { Debug.LogError("[FinalGate] a run is active: OpenZ/Final Gate/Abort first"); return; }
            if (EditorApplication.isCompiling || EditorApplication.isUpdating) { Debug.LogError("[FinalGate] wait for the compile / import to finish"); return; }
            if (OpenZExeRunning()) { Debug.LogError("[FinalGate] OpenZ.exe is running: no Play runs now (the project rules)"); return; }
            try { EditorSceneManager.SaveOpenScenes(); AssetDatabase.SaveAssets(); } catch (Exception e) { Debug.LogWarning("[FinalGate] save: " + e.Message); }
            ArchivePrevious();
            Directory.CreateDirectory(Out); Directory.CreateDirectory(Parts);
            var cur = EditorSceneManager.GetActiveScene();
            SessionState.SetString(KScene, cur.path ?? "");
            SessionState.SetInt(KGvIdx, OzFinalGateView.SelectedIndex());
            int test = PlayerPrefs.GetInt("oz_testmode", 0);
            SessionState.SetInt(KTest, test);
            if (test == 1) { PlayerPrefs.SetInt("oz_testmode", 0); PlayerPrefs.Save(); Debug.Log("[FinalGate] oz_testmode 1 -> 0 for the run (full look); put back after it"); }
            SessionState.SetString(KStages, stages); SessionState.SetInt(KIdx, 0); SessionState.SetBool(KActive, true); SessionState.SetBool(KChecks, checks);
            SessionState.SetString(KStart, DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss")); SessionState.EraseString(KStop);
            File.WriteAllText(Path.Combine(Parts, "_run.txt"), $"started {DateTime.Now:yyyy-MM-dd HH:mm:ss}; stages {stages}; checks {checks}; Unity {Application.unityVersion}; oz_testmode was {test}\n");
            Debug.Log($"[FinalGate] run started: {stages.Replace("|", " -> ")}{(checks ? " -> read checks" : "")} -> REPORT.md ({Out})");
            StartStage(0);
        }

        static bool OpenZExeRunning()
        {
            try { return System.Diagnostics.Process.GetProcessesByName("OpenZ").Length > 0; } catch { return false; }
        }

        static void ArchivePrevious()
        {
            try
            {
                if (!Directory.Exists(Out)) return;
                var files = Directory.GetFiles(Out).Where(f => !Path.GetFileName(f).StartsWith("_") &&
                                                               !Path.GetFileName(f).Equals("approved_map.json", StringComparison.OrdinalIgnoreCase)).ToList();   // the approved pins stay (contact_sheets.py reads them here)
                bool parts = Directory.Exists(Parts) && Directory.GetFiles(Parts).Length > 0;
                if (files.Count == 0 && !parts) return;
                string dst = Path.Combine(Out, "_prev", DateTime.Now.ToString("yyyyMMdd_HHmmss"));
                Directory.CreateDirectory(dst);
                foreach (var f in files) File.Move(f, Path.Combine(dst, Path.GetFileName(f)));
                if (parts) Directory.Move(Parts, Path.Combine(dst, "_parts"));
                var sheets = Path.Combine(Out, "sheets"); if (Directory.Exists(sheets)) Directory.Move(sheets, Path.Combine(dst, "sheets"));
                Debug.Log($"[FinalGate] the previous run moved to {dst}");
            }
            catch (Exception e) { Debug.LogWarning("[FinalGate] archive of the previous run: " + e.Message); }
        }

        static void StartStage(int i)
        {
            var st = Stages; string stop = SessionState.GetString(KStop, "");
            if (i >= st.Length || stop.Length > 0) { Wrap(stop.StartsWith("aborted") ? stop : null); return; }   // heat / watchdog: the read checks still run (CPU only)
            string job = st[i];
            if (!SceneOf.TryGetValue(job, out var scene) || !File.Exists(Path.GetFullPath(scene)))
            {
                Note($"stage {job}: scene {scene} missing: skipped");
                SessionState.SetInt(KIdx, i + 1); StartStage(i + 1); return;
            }
            if (OpenZExeRunning()) { Note($"stage {job}: OpenZ.exe started meanwhile: the remaining Play jobs are skipped"); SessionState.SetString(KStop, "OpenZ.exe running"); Wrap(null); return; }
            try { EditorSceneManager.OpenScene(scene, OpenSceneMode.Single); }
            catch (Exception e) { Note($"stage {job}: OpenScene failed ({e.Message}): skipped"); SessionState.SetInt(KIdx, i + 1); StartStage(i + 1); return; }
            string gv; OzFinalGateView.SetFixed(OzFinalGateRunner.ShotW, OzFinalGateRunner.ShotH, "OpenZ Final Gate", out gv);
            Note($"stage {job}: {scene}; {gv}");
            SessionState.SetString(OzFinalGateRunner.JobKey, job);
            SessionState.SetBool(KAwait, true);
            SessionState.SetFloat(KStageStart, (float)EditorApplication.timeSinceStartup);
            EditorApplication.EnterPlaymode();
        }

        static void OnPlayMode(PlayModeStateChange s)
        {
            if (s == PlayModeStateChange.EnteredPlayMode && Active) { SessionState.SetFloat(KStageStart, 0f); SessionState.SetFloat(KPlayStart, (float)EditorApplication.timeSinceStartup); }
            if (s == PlayModeStateChange.EnteredEditMode && Active) { SessionState.SetFloat(KPlayStart, 0f); EditorApplication.delayCall += () => StageEnded("Play ended"); }
        }

        static void StageEnded(string why)
        {
            if (!Active || !SessionState.GetBool(KAwait, false) || EditorApplication.isPlayingOrWillChangePlaymode) return;
            SessionState.SetBool(KAwait, false);
            int i = SessionState.GetInt(KIdx, 0); var st = Stages; string job = i < st.Length ? st[i] : "?";
            string done = Path.Combine(Parts, job + ".done");
            string res = File.Exists(done) ? File.ReadAllText(done).Trim() : "no result file (Play stopped before the runner finished?)";
            Note($"stage {job} ended ({why}): {res}");
            if (res.StartsWith("heat") || res.StartsWith("watchdog") || res.StartsWith("job timeout")) { SessionState.SetString(KStop, $"{job}: {res}"); Note("remaining Play jobs skipped: " + res); }
            SessionState.SetInt(KIdx, i + 1);
            EditorApplication.delayCall += () => StartStage(i + 1);
        }

        static void Tick()
        {
            if (!Active) return;
            float t0 = SessionState.GetFloat(KStageStart, 0f);
            if (!EditorApplication.isPlaying)
            {
                if (t0 > 0f && SessionState.GetBool(KAwait, false) && !EditorApplication.isPlayingOrWillChangePlaymode && !EditorApplication.isCompiling &&
                    EditorApplication.timeSinceStartup - t0 > 90f)
                { SessionState.SetFloat(KStageStart, 0f); Note("Play did not start within 90 s"); StageEnded("Play never started"); }
                return;
            }
            float tp = SessionState.GetFloat(KPlayStart, 0f);
            if (tp > 0f && EditorApplication.timeSinceStartup - tp > StageTimeoutSeconds * 3f)
            {
                SessionState.SetFloat(KPlayStart, 0f);
                Note($"stage timeout {StageTimeoutSeconds:0} s: Play stopped by the editor");
                EditorApplication.isPlaying = false;
            }
        }

        static void Note(string s)
        {
            Debug.Log("[FinalGate] " + s);
            try { Directory.CreateDirectory(Parts); File.AppendAllText(Path.Combine(Parts, "_run.txt"), $"{DateTime.Now:HH:mm:ss} {s}\n"); } catch { }
        }

        // ------------------------------------------------------------------ wrap up: read checks, report, restore
        static void Wrap(string why)
        {
            if (!Active) return;
            SessionState.SetBool(KActive, false);
            string checks = "(read checks not part of this run)\n";
            try { if (SessionState.GetBool(KChecks, false) && string.IsNullOrEmpty(why)) checks = RunChecks(true); }
            catch (Exception e) { checks = "read checks FAILED: " + e + "\n"; }
            if (why != null) Note("run ended: " + why);
            try { WriteReport(checks); } catch (Exception e) { Debug.LogError("[FinalGate] report: " + e); }
            try
            {
                if (SessionState.GetInt(KTest, 0) == 1) { PlayerPrefs.SetInt("oz_testmode", 1); PlayerPrefs.Save(); Note("oz_testmode put back to 1"); }
                OzFinalGateView.Select(SessionState.GetInt(KGvIdx, -1));
                var sc = SessionState.GetString(KScene, "");
                if (!string.IsNullOrEmpty(sc) && File.Exists(Path.GetFullPath(sc))) EditorSceneManager.OpenScene(sc, OpenSceneMode.Single);
            }
            catch (Exception e) { Debug.LogWarning("[FinalGate] restore: " + e.Message); }
            Debug.Log("[FinalGate] DONE: " + Path.Combine(Out, "REPORT.md") + "  (contact sheets: python " +
                      Path.Combine(OpenZPaths.RepoRoot, "work", "staging", "prebuilt_final_gate_runner", "tools", "contact_sheets.py") + ")");
        }

        // ------------------------------------------------------------------ read checks (edit mode)
        sealed class Check { public string name, verdict, detail; }

        static string RunChecks(bool botMatches)
        {
            var rows = new List<Check>(); var body = new StringBuilder();
            void Add(string n, string v, string d) { rows.Add(new Check { name = n, verdict = v, detail = d }); Note($"check {n}: {v} {d}"); }
            var logs = new List<string>();
            Application.LogCallback cb = (c, st, t) => { if (t != LogType.Log && logs.Count < 400) logs.Add(t + "\t" + (c ?? "").Split('\n')[0]); };
            Application.logMessageReceived += cb;
            try
            {
                // 1. parity report (the same as the menu OpenZ/Run Parity Report, incl. its Deviations block)
                try
                {
                    var t0 = DateTime.Now;
                    string s = OpenZParity.Run();
                    string first = s.Split('\n')[0];
                    var m = Regex.Match(first, @"(\d+)/(\d+) PASS, (\d+) FAIL");
                    bool ok = m.Success && m.Groups[3].Value == "0";
                    Add("DV parity report (OpenZ/Run Parity Report)", m.Success ? (ok ? "PASS" : "FAIL") : "CHECK", $"{first} ({(DateTime.Now - t0).TotalSeconds:0} s); full report work/records/parity_report.md (copied to final_gate/parity_report.md)");
                    var src = Path.Combine(OpenZPaths.Records, "parity_report.md");
                    if (File.Exists(src)) File.Copy(src, Path.Combine(Out, "parity_report.md"), true);
                    body.AppendLine("### Parity report: summary and failing rows"); body.AppendLine(); body.AppendLine("```"); body.AppendLine(s.Trim()); body.AppendLine("```"); body.AppendLine();
                }
                catch (Exception e) { Add("DV parity report (OpenZ/Run Parity Report)", "FAIL", "exception " + e.GetType().Name + ": " + e.Message); }

                // 2. headless bot match per level (OpenZ/Bot vs Bot, the same sim the game runs)
                if (botMatches)
                {
                    double secs = SessionState.GetFloat(KBotSeconds, 3600f);
                    var results = new List<(string lv, OpenZBotMatch.Result r)>();
                    foreach (var lv in new[] { "L02", "L17" })
                    {
                        try
                        {
                            var t0 = DateTime.Now;
                            var r = OpenZBotMatch.Run(lv, 1, secs);
                            results.Add((lv, r));
                            bool exc = r.summary != null && r.summary.StartsWith("EXCEPTION");
                            Add($"headless bot match {lv} (seed 1, max {secs:0} s game time)", exc ? "FAIL" : r.badBuild > 0 ? "FAIL" : r.endTime < 0 ? "WARN" : "PASS",
                                $"{r.summary} ({(DateTime.Now - t0).TotalSeconds:0} s real); {r.file}");
                            if (File.Exists(r.file)) File.Copy(r.file, Path.Combine(Out, Path.GetFileName(r.file)), true);
                        }
                        catch (Exception e) { Add($"headless bot match {lv}", "FAIL", "exception " + e.GetType().Name + ": " + e.Message); }
                    }
                    if (results.Count == 2)
                        body.AppendLine($"Bot matches (one seed per level; balance needs more seeds, see OpenZ/Bot vs Bot): {string.Join("; ", results.Select(x => x.lv + " winner " + x.r.winner))}.\n");
                }

                // 3. intro v1: present (never deleted) and OFF by default
                try
                {
                    string root = OpenZPaths.RepoRoot;
                    var need = new[]
                    {
                        "projects/unity/OpenZ/Assets/OpenZ/Scenes/Intro.unity",
                        "projects/unity/OpenZ/Assets/OpenZ/Resources/OpenZ/Intro/openz_intro.mp4",
                        "work/records/milestones/intro/intro_prerender_r5_music.mp4",
                    };
                    var missing = need.Where(p => !File.Exists(Path.Combine(root, p))).ToList();
                    bool inBuild = EditorBuildSettings.scenes.Any(s => s.path.EndsWith("/Intro.unity"));
                    string src = ReadScript("Intro/OzIntroData.cs");
                    bool defOff = src != null && Regex.IsMatch(src, @"PlayerPrefs\.GetInt\(\s*Pref\s*,\s*0\s*\)\s*==\s*1");
                    bool prefNow = PlayerPrefs.GetInt("oz_intro", 0) == 1;
                    string menuSrc = ReadScript("UI/OpenZMainMenu.MenuExe.cs");
                    bool rowInSource = menuSrc != null && menuSrc.Contains("PLAY INTRO");
                    bool offered = OpenZ.Intro.OzIntro.Offered;                                   // the one switch: false = the row is not built and the intro never plays
                    string rowState = !rowInSource ? "NOT FOUND in the menu source"
                                    : offered ? "shown (OzIntro.Offered is true)"
                                    : "hidden (OzIntro.Offered is false: the row is not built and the intro never plays, whatever oz_intro says; the row's code is kept)";
                    string v = missing.Count == 0 && defOff ? "PASS" : "FAIL";
                    Add("intro v1 present and OFF by default", v,
                        $"files {(missing.Count == 0 ? "all present" : "MISSING: " + string.Join(", ", missing))}; Intro scene in the build list {inBuild}; code default {(defOff ? "OFF (GetInt(Pref, 0) == 1)" : "NOT OFF / not found")}; " +
                        $"Options row PLAY INTRO {rowState}; this editor's oz_intro now {(prefNow ? "ON (an editor pref; the default is what ships)" : "OFF")}");
                }
                catch (Exception e) { Add("intro v1 present and OFF by default", "FAIL", "exception " + e.Message); }

                // 4. grenade crate v1 / v2 switch (reflection: never calls the switch)
                try
                {
                    Type sw = null;
                    foreach (var a in AppDomain.CurrentDomain.GetAssemblies()) { sw = a.GetType("OpenZ.EditorTools.OpenZGrenadeCrateSwitch"); if (sw != null) break; }
                    var menus = new List<string>();
                    if (sw != null)
                        foreach (var m in sw.GetMethods(BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic))
                            foreach (var at in m.GetCustomAttributes(typeof(MenuItem), false).Cast<MenuItem>()) menus.Add(at.menuItem);
                    bool v2Menu = menus.Any(x => x.Contains("Use v2")), v1Menu = menus.Any(x => x.Contains("Revert to v1"));
                    string active = "?"; try { active = sw?.GetProperty("Active", BindingFlags.Static | BindingFlags.Public)?.GetValue(null) as string ?? "?"; } catch { }
                    string root = OpenZPaths.RepoRoot;
                    var dirs = new[] { "projects/unity/OpenZ/Assets/OpenZ/Models/Props/GrenadeCrate", "projects/unity/OpenZ/Assets/OpenZ/Models/Props/GrenadeCrate_v2",
                                       "work/staging/grenade_crate_v1", "work/staging/grenade_crate_v2" };
                    var dmiss = dirs.Where(d => !Directory.Exists(Path.Combine(root, d))).ToList();
                    var keep = new[] { "crate_v1", "crate_v2" }.Where(k => !Directory.Exists(Path.Combine(root, "projects/blender/export/base_props_v2", k))).ToList();
                    string v = sw != null && v1Menu && v2Menu && dmiss.Count == 0 ? (keep.Count == 0 ? "PASS" : "WARN") : "FAIL";
                    Add("grenade crate version switch (v1 / v2)", v,
                        $"switch class {(sw != null ? "present" : "MISSING")}; menus Use v2 {v2Menu}, Revert to v1 {v1Menu}; active {active}; folders {(dmiss.Count == 0 ? "all present" : "MISSING: " + string.Join(", ", dmiss))}; " +
                        $"Supply copies base_props_v2/crate_v1 + crate_v2 {(keep.Count == 0 ? "present" : "missing " + string.Join(", ", keep) + " (made at the first switch)")}");
                }
                catch (Exception e) { Add("grenade crate version switch (v1 / v2)", "FAIL", "exception " + e.Message); }

                try
                {
                    var live = ScanDefaults();
                    var (changed, gone, added) = CompareDefaults(DefaultsBaseline, live);
                    Add("settings defaults unchanged (vs the 6 Oct baseline)", changed.Count > 0 ? "FAIL" : gone.Count > 0 ? "WARN" : "PASS",
                        $"{live.Count} defaults scanned, {DefaultsBaseline.Length} in the baseline; {changed.Count} changed, {gone.Count} gone, {added.Count} new");
                    body.AppendLine("### Settings defaults"); body.AppendLine();
                    try { body.AppendLine("This editor's saved settings now: " + OzSettings.Describe()); } catch (Exception e) { body.AppendLine("OzSettings.Describe failed: " + e.Message); }
                    body.AppendLine();
                    foreach (var c in changed) body.AppendLine("- **CHANGED** " + c);
                    foreach (var c in gone) body.AppendLine("- GONE (getter removed or moved): " + c);
                    foreach (var c in added) body.AppendLine("- new: " + c);
                    if (changed.Count + gone.Count + added.Count == 0) body.AppendLine("- every default is as in the baseline");
                    body.AppendLine();
                }
                catch (Exception e) { Add("settings defaults unchanged", "FAIL", "exception " + e.Message); }
            }
            finally { Application.logMessageReceived -= cb; }

            var sb = new StringBuilder();
            sb.AppendLine("| Read check | Verdict | Detail |"); sb.AppendLine("|---|---|---|");
            foreach (var r in rows) sb.AppendLine($"| {r.name} | {r.verdict} | {Esc(r.detail)} |");
            sb.AppendLine();
            sb.Append(body);
            sb.AppendLine($"Console during the read checks: {logs.Count(l => !l.StartsWith("Warning"))} error(s), {logs.Count(l => l.StartsWith("Warning"))} warning(s).");
            foreach (var l in logs.Take(40)) sb.AppendLine("- " + Esc(l));
            sb.AppendLine();
            File.WriteAllText(Path.Combine(Parts, "CHECKS.md"), sb.ToString());
            return sb.ToString();
        }

        static string ReadScript(string rel)
        {
            var p = Path.Combine(Application.dataPath, "OpenZ", "Scripts", rel);
            return File.Exists(p) ? File.ReadAllText(p) : null;
        }

        static readonly Regex DefaultsRx = new Regex(@"PlayerPrefs\.Get(Int|Float|String)\s*\(\s*([^,()]+?)\s*,\s*((?:[^()]|\([^()]*\))*?)\s*\)", RegexOptions.Compiled);

        /// <summary>Same scan as tools/settings_defaults.py: relpath|Method|key|default for the runtime scripts (Editor/ excluded).</summary>
        public static List<string> ScanDefaults()
        {
            var root = Path.Combine(Application.dataPath, "OpenZ", "Scripts");
            var set = new SortedSet<string>(StringComparer.Ordinal);
            foreach (var f in Directory.GetFiles(root, "*.cs", SearchOption.AllDirectories))
            {
                string rel = f.Substring(root.Length + 1).Replace('\\', '/');
                if (rel.StartsWith("Editor/")) continue;
                foreach (Match m in DefaultsRx.Matches(File.ReadAllText(f)))
                    set.Add($"{rel}|{m.Groups[1].Value}|{Collapse(m.Groups[2].Value)}|{Collapse(m.Groups[3].Value)}");
            }
            return set.ToList();
        }
        static string Collapse(string s) => Regex.Replace(s.Trim(), @"\s+", " ");

        /// <summary>Groups by Method|key (a plain identifier key such as `key` or `Pref` stays per file); a group whose defaults differ = changed.</summary>
        static (List<string> changed, List<string> gone, List<string> added) CompareDefaults(IEnumerable<string> baseline, IEnumerable<string> live)
        {
            Dictionary<string, SortedSet<string>> Group(IEnumerable<string> lines)
            {
                var d = new Dictionary<string, SortedSet<string>>();
                foreach (var l in lines)
                {
                    var p = l.Split('|'); if (p.Length < 4) continue;
                    string key = p[2], def = string.Join("|", p.Skip(3));
                    bool plain = !key.Contains("\"") && !key.Contains(".");
                    string g = p[1] + "|" + key + (plain ? " @" + p[0] : "");
                    if (!d.TryGetValue(g, out var s)) d[g] = s = new SortedSet<string>(StringComparer.Ordinal);
                    s.Add(def);
                }
                return d;
            }
            var b = Group(baseline); var l2 = Group(live);
            var changed = new List<string>(); var gone = new List<string>(); var added = new List<string>();
            foreach (var kv in b)
            {
                if (!l2.TryGetValue(kv.Key, out var now)) { gone.Add($"`{kv.Key}` default {string.Join(" / ", kv.Value)}"); continue; }
                if (!now.SetEquals(kv.Value)) changed.Add($"`{kv.Key}`: baseline {string.Join(" / ", kv.Value)} -> now {string.Join(" / ", now)}");
            }
            foreach (var kv in l2) if (!b.ContainsKey(kv.Key)) added.Add($"`{kv.Key}` default {string.Join(" / ", kv.Value)}");
            return (changed, gone, added);
        }

        // ------------------------------------------------------------------ REPORT.md
        static string Esc(string s) => (s ?? "").Replace("|", "\\|").Replace("\n", " ");

        static void WriteReport(string checks)
        {
            Directory.CreateDirectory(Out);
            var sb = new StringBuilder();
            sb.AppendLine("# OpenZ final gate: capture set + read checks");
            sb.AppendLine();
            sb.AppendLine($"Run started {SessionState.GetString(KStart, "?")}, report written {DateTime.Now:yyyy-MM-dd HH:mm:ss}; Unity {Application.unityVersion}; GPU {SystemInfo.graphicsDeviceName}; " +
                          $"tool: OpenZ/Final Gate. Stop reason: {(string.IsNullOrEmpty(SessionState.GetString(KStop, "")) ? "none" : SessionState.GetString(KStop, ""))}.");
            sb.AppendLine();
            sb.AppendLine("This report is evidence for the critic, not a verdict on the look: every frame still has to be looked at side by side with the approved");
            sb.AppendLine("frames and the references (the project rules), with the contact sheets of tools/contact_sheets.py.");
            sb.AppendLine();

            // shots
            var rows = new List<string[]>();
            foreach (var job in new[] { "L02", "L17", "MENU" })
            {
                var f = Path.Combine(Parts, job + "_shots.tsv");
                if (File.Exists(f)) rows.AddRange(File.ReadAllLines(f).Where(l => l.Trim().Length > 0).Select(l => l.Split('\t')));
            }
            int ok = rows.Count(r => r.Length > 3 && r[3].StartsWith("OK")), miss = rows.Count - ok;
            var ms = rows.Where(r => r.Length > 6 && r[5] != "-").Select(r => (avg: float.Parse(r[5], CultureInfo.InvariantCulture), worst: float.Parse(r[6], CultureInfo.InvariantCulture), name: r[0])).ToList();
            sb.AppendLine("## Summary");
            sb.AppendLine();
            sb.AppendLine($"- Frames: {ok} captured, {miss} missing / skipped / failed (list below). Folder: `{Out}`.");
            if (ms.Count > 0)
            {
                var worst = ms.OrderByDescending(x => x.worst).First();
                sb.AppendLine($"- Frame time over the shot windows (60 fps cap): average of averages {ms.Average(x => x.avg):0.0} ms, worst single frame {worst.worst:0.0} ms ({worst.name}); shots with an average over 16.9 ms: {ms.Count(x => x.avg > 16.9f)}.");
            }
            foreach (var job in new[] { "L02", "L17", "MENU" })
            {
                var c = Path.Combine(Parts, job + "_console.tsv");
                if (!File.Exists(c)) { sb.AppendLine($"- {job}: no Play result (not run, or stopped before the runner finished)."); continue; }
                var lines = File.ReadAllLines(c).Where(l => l.Trim().Length > 0).ToList();
                int e = lines.Count(l => Regex.IsMatch(l, @"\t(Error|Exception|Assert)\t")), w = lines.Count(l => l.Contains("\tWarning\t"));
                string res = File.Exists(Path.Combine(Parts, job + ".done")) ? File.ReadAllText(Path.Combine(Parts, job + ".done")).Trim() : "?";
                sb.AppendLine($"- {job}: {res}; console {e} error(s) / exception(s), {w} warning(s).");
            }
            sb.AppendLine();
            sb.AppendLine("## Read checks");
            sb.AppendLine();
            sb.AppendLine(checks);
            sb.AppendLine("## Shots");
            sb.AppendLine();
            sb.AppendLine("| Shot | Level | Time | Status | File | Avg ms | Worst ms | Frames | What |");
            sb.AppendLine("|---|---|---|---|---|---|---|---|---|");
            foreach (var r in rows) { var p = r.Concat(Enumerable.Repeat("", 9)).ToArray(); sb.AppendLine($"| {p[0]} | {p[1]} | {p[2]} | {p[3]} | {p[4]} | {p[5]} | {p[6]} | {p[7]} | {Esc(p[8])} |"); }
            sb.AppendLine();
            foreach (var job in new[] { "L02", "L17", "MENU" })
            {
                var md = Path.Combine(Parts, job + ".md");
                sb.AppendLine($"## Play job {job}");
                sb.AppendLine();
                sb.AppendLine(File.Exists(md) ? File.ReadAllText(md) : "(no result)\n");
                var c = Path.Combine(Parts, job + "_console.tsv");
                if (File.Exists(c))
                {
                    var groups = File.ReadAllLines(c).Where(l => l.Trim().Length > 0).Select(l => { var p = l.Split('\t'); return p.Length >= 3 ? p[p.Length - 2] + ": " + p[p.Length - 1] : l; })
                                     .GroupBy(x => x).OrderByDescending(x => x.Count()).Take(40).ToList();
                    sb.AppendLine("#### Console errors and warnings (grouped, top 40; full list in _parts/" + job + "_console.tsv)");
                    sb.AppendLine();
                    if (groups.Count == 0) sb.AppendLine("- none");
                    foreach (var gr in groups) sb.AppendLine($"- {gr.Count()}x {Esc(gr.Key)}");
                    sb.AppendLine();
                }
            }
            var run = Path.Combine(Parts, "_run.txt");
            if (File.Exists(run)) { sb.AppendLine("## Run log"); sb.AppendLine(); sb.AppendLine("```"); sb.AppendLine(File.ReadAllText(run).Trim()); sb.AppendLine("```"); sb.AppendLine(); }
            sb.AppendLine("## Next");
            sb.AppendLine();
            sb.AppendLine("- Contact sheets (new frame next to the previous approved frame where one exists): `python " +
                          Path.Combine(OpenZPaths.RepoRoot, "work", "staging", "prebuilt_final_gate_runner", "tools", "contact_sheets.py") + "` -> final_gate/sheets/.");
            sb.AppendLine("- Look at every frame (RIGHT FIRST TIME list); the silent-action census is a heuristic: check every FAIL / FAIL? row by ear in OzSoundLog.");
            File.WriteAllText(Path.Combine(Out, "REPORT.md"), sb.ToString());
            Debug.Log("[FinalGate] REPORT.md written: " + Path.Combine(Out, "REPORT.md"));
        }
    }
}
