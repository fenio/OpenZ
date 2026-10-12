using System;
using System.Diagnostics;
using System.IO;
using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEngine;
using Debug = UnityEngine.Debug;

namespace OpenZ.EditorTools
{
    /// <summary>
    /// profiling, no profiler auto-connect, no OZ_RELEASE) of the enabled Build Settings scenes into builds\soak_&lt;yyyyMMdd_HHmm&gt;\OpenZ.exe,
    /// then runs it: OpenZ.exe -ozsoak &lt;min&gt; -ozsoakdir &lt;build&gt;\soak -logFile &lt;build&gt;\soak\Player.log at 2560x1440 fullscreen, waits
    /// (EditorApplication.update, the editor stays usable; a timeout of minutes + 15 kills the player) and opens soak\SOAK_REPORT.md.
    /// The build number in build_info.json (OzBuildStampHook) is put back afterwards: a soak build is no release step.
    /// A heavy job: nothing else heavy at the same time (no Play, no render); the player's own heat guard caps it at 78 / 82 C.
    /// </summary>
    public static class OpenZSoakBuild
    {
        const string Full = "OpenZ/Perf/Build soak player + run (20 min)", Quick = "OpenZ/Perf/Build soak player + run (quick 5 min)",
            Again = "OpenZ/Perf/Soak: run the last soak build again (20 min)", Open = "OpenZ/Perf/Soak: open the last report";
        public const int Width = 2560, Height = 1440;
        static Process running; static string runDir; static DateTime killAt; static int runMinutes;

        [MenuItem(Full, false, 60)] static void BuildRun20() => BuildAndRun(20);
        [MenuItem(Quick, false, 61)] static void BuildRun5() => BuildAndRun(5);
        [MenuItem(Again, false, 62)] static void RunAgain()
        {
            var d = LastBuild();
            if (d == null) { EditorUtility.DisplayDialog("OpenZ soak", "No builds\\soak_* folder with OpenZ.exe yet.", "OK"); return; }
            Run(d, 20);
        }
        [MenuItem(Open, false, 63)] static void OpenLast()
        {
            var d = LastBuild(); var r = d == null ? null : Path.Combine(d, "soak", "SOAK_REPORT.md");
            if (r != null && File.Exists(r)) EditorUtility.OpenWithDefaultApp(r); else Debug.LogWarning("[OpenZ Soak] no SOAK_REPORT.md yet");
        }
        [MenuItem(Full, true)] [MenuItem(Quick, true)] [MenuItem(Again, true)] static bool CanRun() => !EditorApplication.isPlaying && !EditorApplication.isCompiling && running == null;

        static string Root => Path.GetFullPath(Path.Combine(Application.dataPath, "../../../.."));

        static string LastBuild()
        {
            var b = Path.Combine(Root, "builds");
            if (!Directory.Exists(b)) return null;
            string best = null; DateTime bt = DateTime.MinValue;
            foreach (var d in Directory.GetDirectories(b, "soak_*"))
                if (File.Exists(Path.Combine(d, "OpenZ.exe")) && Directory.GetLastWriteTime(d) > bt) { bt = Directory.GetLastWriteTime(d); best = d; }
            return best;
        }

        public static string BuildAndRun(int minutes)
        {
            if (EditorApplication.isPlaying || EditorApplication.isCompiling) { Debug.LogWarning("[OpenZ Soak] stop Play / wait for the compile first"); return "busy"; }
            if (Array.IndexOf(PlayerSettings.GetScriptingDefineSymbols(UnityEditor.Build.NamedBuildTarget.Standalone).Split(';'), "OZ_RELEASE") >= 0)
            { Debug.LogError("[OpenZ Soak] OZ_RELEASE is in the project's Standalone scripting defines: the soak runner would be compiled out. Remove it (the release build adds it by itself)"); return "OZ_RELEASE define"; }
            if (Process.GetProcessesByName("OpenZ").Length > 0 && !EditorUtility.DisplayDialog("OpenZ soak", "An OpenZ.exe is running. One heavy job at a time: close it first?", "Build anyway", "Cancel")) return "cancelled";
            UnityEditor.SceneManagement.EditorSceneManager.SaveOpenScenes(); AssetDatabase.SaveAssets();
            string dir = Path.Combine(Root, "builds", "soak_" + DateTime.Now.ToString("yyyyMMdd_HHmm"));
            if (Directory.Exists(dir)) dir += "_" + DateTime.Now.ToString("ss");
            Directory.CreateDirectory(dir);
            string stampPath = OzBuildStampHook.Path, stampBefore = File.Exists(stampPath) ? File.ReadAllText(stampPath) : null;
            PlayerPrefs.SetInt("oz_testmode", 0); PlayerPrefs.SetInt("oz_gate", 0); PlayerPrefs.SetInt("oz_botmatch", 0); PlayerPrefs.Save();
            var opts = new BuildPlayerOptions
            {
                scenes = Array.ConvertAll(Array.FindAll(EditorBuildSettings.scenes, e => e.enabled), e => e.path),
                locationPathName = Path.Combine(dir, "OpenZ.exe"),
                target = BuildTarget.StandaloneWindows64,
                options = BuildOptions.Development,                                                        // GC alloc + memory counters; never OZ_RELEASE
            };
            if ((opts.options & (BuildOptions.ConnectWithProfiler | BuildOptions.EnableDeepProfilingSupport | BuildOptions.AllowDebugging)) != 0) throw new InvalidOperationException("soak build must not auto-connect / deep profile");
            Debug.Log("[OpenZ Soak] " + OpenZBuild.IncludeRuntimeShaders());
            BuildReport rep = null;
            try { rep = BuildPipeline.BuildPlayer(opts); }
            catch (Exception e) { Debug.LogError("[OpenZ Soak] build threw: " + e); }
            finally
            {
                if (stampBefore != null && File.Exists(stampPath) && File.ReadAllText(stampPath) != stampBefore)
                { File.WriteAllText(stampPath, stampBefore); AssetDatabase.ImportAsset(stampPath, ImportAssetOptions.ForceSynchronousImport); OzVersion.ResetInfo(); Debug.Log("[OpenZ Soak] build_info.json put back (a soak build is no release step)"); }
            }
            var s = rep != null ? rep.summary : default;
            string info = $"OpenZ SOAK development build\nresult: {(rep != null ? s.result.ToString() : "exception")}\ntime: {DateTime.Now:yyyy-MM-dd HH:mm}\nduration: {(rep != null ? s.totalTime.ToString() : "-")}\n" +
                          $"size: {(rep != null ? s.totalSize / (1024f * 1024f) : 0):F0} MB\nerrors: {(rep != null ? s.totalErrors : -1)}, warnings: {(rep != null ? s.totalWarnings : -1)}\n" +
                          $"run: OpenZ.exe -ozsoak <minutes> -ozsoakdir soak -logFile soak\\Player.log -screen-fullscreen 1 -screen-width {Width} -screen-height {Height}\nNOT for players: development build with the soak runner.\n";
            File.WriteAllText(Path.Combine(dir, "BUILD_INFO.txt"), info);
            Debug.Log("[OpenZ Soak] " + info);
            if (rep == null || s.result != BuildResult.Succeeded) { Debug.LogError("[OpenZ Soak] build failed: no run"); return "build failed"; }
            Run(dir, minutes);
            return dir;
        }

        static void Run(string dir, int minutes)
        {
            string soak = Path.Combine(dir, "soak");
            if (Directory.Exists(soak) && File.Exists(Path.Combine(soak, "SOAK_REPORT.md")))
                Directory.Move(soak, soak + "_" + Directory.GetLastWriteTime(soak).ToString("HHmmss"));                // keep the previous run
            Directory.CreateDirectory(soak);
            var psi = new ProcessStartInfo(Path.Combine(dir, "OpenZ.exe"),
                $"-ozsoak {minutes} -ozsoakdir \"{soak}\" -logFile \"{Path.Combine(soak, "Player.log")}\" -screen-fullscreen 1 -screen-width {Width} -screen-height {Height}")
            { UseShellExecute = false, WorkingDirectory = dir };
            try { running = Process.Start(psi); }
            catch (Exception e) { Debug.LogError("[OpenZ Soak] could not start the player: " + e.Message); running = null; return; }
            runDir = soak; runMinutes = minutes; killAt = DateTime.Now.AddMinutes(minutes + 15);
            try { SessionState.SetInt(KPid, running.Id); } catch (Exception) { }
            SessionState.SetString(KDir, runDir); SessionState.SetInt(KMin, runMinutes); SessionState.SetString(KKill, killAt.ToBinary().ToString());
            Debug.Log($"[OpenZ Soak] running {minutes} min: {psi.FileName} {psi.Arguments} (the report opens when it ends; timeout {killAt:HH:mm})");
            EditorApplication.update -= Poll; EditorApplication.update += Poll;
        }

        const string KPid = "OZ_Soak_Pid", KDir = "OZ_Soak_Dir", KMin = "OZ_Soak_Min", KKill = "OZ_Soak_KillAt";
        [InitializeOnLoadMethod]
        static void Reattach()
        {
            int pid = SessionState.GetInt(KPid, 0);
            if (pid == 0 || running != null) return;
            runDir = SessionState.GetString(KDir, ""); runMinutes = SessionState.GetInt(KMin, 20);
            killAt = long.TryParse(SessionState.GetString(KKill, ""), out var kb) && kb != 0 ? DateTime.FromBinary(kb) : DateTime.Now.AddMinutes(35);
            try { var p = Process.GetProcessById(pid); running = p.ProcessName.Equals("OpenZ", StringComparison.OrdinalIgnoreCase) ? p : null; } catch (Exception) { running = null; }
            if (running == null) { EditorApplication.delayCall += Finish; return; }                       // it ended during the reload
            Debug.Log($"[OpenZ Soak] watching the running soak player again after a domain reload (pid {pid})");
            EditorApplication.update -= Poll; EditorApplication.update += Poll;
        }

        static double nextPoll;
        static void Poll()
        {
            if (running == null) { EditorApplication.update -= Poll; return; }
            if (EditorApplication.timeSinceStartup < nextPoll) return;
            nextPoll = EditorApplication.timeSinceStartup + 2.0;
            bool exited;
            try { exited = running.HasExited; } catch (Exception) { exited = true; }
            if (!exited && DateTime.Now > killAt)
            {
                Debug.LogError($"[OpenZ Soak] the player ran past {runMinutes} + 15 min: killed");
                try { running.Kill(); } catch (Exception) { }
                exited = true;
            }
            if (!exited) return;
            EditorApplication.update -= Poll;
            try { running.Dispose(); } catch (Exception) { }
            running = null;
            Finish();
        }

        static void Finish()
        {
            SessionState.EraseInt(KPid); SessionState.EraseString(KDir); SessionState.EraseInt(KMin); SessionState.EraseString(KKill);
            if (string.IsNullOrEmpty(runDir) || !Directory.Exists(runDir)) return;
            string report = Path.Combine(runDir, "SOAK_REPORT.md");
            if (!File.Exists(report))
                File.WriteAllText(report, "# OpenZ soak report\n\n**OVERALL: FAIL** - the player ended without writing its report (crash, kill or timeout).\n" +
                                          "Read soak.csv / events.csv / hitches.csv and Player.log in this folder; run the analysis again with\n" +
                                          "`python soak_summary.py \"" + runDir + "\"`.\n");
            string head = "";
            try { foreach (var l in File.ReadAllLines(report)) if (l.StartsWith("**OVERALL", StringComparison.Ordinal)) head = l; } catch (Exception) { }
            Debug.Log($"[OpenZ Soak] finished: {report} {head}");
            Debug.Log("[OpenZ Soak] report path (not opened: no window on the desktop): " + report);
        }
    }
}
