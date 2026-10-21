using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEngine;

namespace OpenZ.EditorTools
{
    /// <summary>
    /// The EXPERIMENTAL macOS player: a build method of its own beside the Windows release menu, which it never calls or changes.
    /// Three menu items, used in this order, each safe to call again:
    ///   OpenZ/Mac experimental/1 Switch the editor to macOS       (the first switch imports the project again for the Mac target)
    ///   OpenZ/Mac experimental/2 Build the Mac test app            (once per editor session, behind a SessionState guard)
    ///   OpenZ/Mac experimental/3 Switch the editor back to Windows
    ///  * target macOS, Intel 64-bit only, Mono, non-development; the same scenes, scripting define, company name, version tag and
    ///    player settings as the Windows release build, for this build only (restored afterwards). The Intel build also runs on
    ///    Macs with an Apple chip through the system's own translation; an unsigned Apple-chip binary would not start there;
    ///  * the Mac build module is used without a compile-time reference (its architecture setting is found by name), so this file
    ///    also compiles in an editor that has no Mac module installed;
    ///  * output: release\OpenZ_&lt;tag&gt;_mac_EXPERIMENTAL\OpenZ.app, and NEXT to that folder the build record
    ///    OpenZ_&lt;tag&gt;_mac_EXPERIMENTAL_BUILD_INFO.txt. The header of the built executable is read back: the record names its
    ///    processor type, and the build counts as not usable when that is not Intel 64-bit only;
    ///    that keeps the Unix run permission) is done by the post-build scripts outside the editor.
    /// The result is unsigned and has never been run on a real Mac: it is handed out as it is, labelled experimental.
    /// A heavy job: run it alone (no Play, no render).
    /// </summary>
    public static class OpenZMacExperimentalBuild
    {
        const string GuardKey = "oz_mac_experimental_build_started";
        const string ResultKey = "oz_mac_experimental_build_result";
        const string MacBundleId = "com.OpenZ.OpenZ";
        const string FolderSuffix = "_mac_EXPERIMENTAL";
        static readonly string[] RequiredScenes = { "Intro", "MainMenu", "L02_Desert", "L16_Jungle", "L17_CarPark" };

        static string JobRoot => Path.GetFullPath(Path.Combine(Application.dataPath, "../../../.."));
        static bool ModuleSeen => BuildPipeline.IsBuildTargetSupported(BuildTargetGroup.Standalone, BuildTarget.StandaloneOSX);

        [MenuItem("OpenZ/Mac experimental/1 Switch the editor to macOS", false, 60)]
        public static string SwitchToMac()
        {
            if (EditorApplication.isPlaying) return Note("stop Play first");
            if (EditorApplication.isCompiling) return Note("wait for the compile to finish");
            if (!ModuleSeen) return Fail("this editor does not see the Mac build module: quit the editor and start it again once");
            if (EditorUserBuildSettings.activeBuildTarget == BuildTarget.StandaloneOSX) return Note("the editor is on macOS already");
            UnityEditor.SceneManagement.EditorSceneManager.SaveOpenScenes(); AssetDatabase.SaveAssets();
            DateTime t0 = DateTime.Now;
            bool ok = EditorUserBuildSettings.SwitchActiveBuildTarget(BuildTargetGroup.Standalone, BuildTarget.StandaloneOSX);
            return Note((ok ? "switched to macOS" : "the switch to macOS was REFUSED") + $" after {(DateTime.Now - t0).TotalMinutes:F1} min; active target now {EditorUserBuildSettings.activeBuildTarget}");
        }

        [MenuItem("OpenZ/Mac experimental/2 Build the Mac test app", false, 61)]
        public static string BuildMac()
        {
            if (SessionState.GetBool(GuardKey, false))
                return Note("the Mac build was already started in this editor session and is not started twice. Its result: "
                            + SessionState.GetString(ResultKey, "none recorded yet (still running, or interrupted: read the console and the build record)"));
            if (EditorApplication.isPlaying) return Note("stop Play first");
            if (EditorApplication.isCompiling) return Note("wait for the compile to finish");
            if (!ModuleSeen) return Fail("this editor does not see the Mac build module: quit the editor and start it again once");
            if (EditorUserBuildSettings.activeBuildTarget != BuildTarget.StandaloneOSX)
                return Fail("the editor is not on macOS: call '1 Switch the editor to macOS' first and wait until it has finished compiling");
            if (PlayerSettings.GetScriptingBackend(NamedBuildTarget.Standalone) != ScriptingImplementation.Mono2x)
                return Fail("the standalone scripting backend is not Mono (the Mac player can only be built from Windows with Mono)");

            // ---- pre-flight, the same two checks as the Windows release build
            var scenes = Array.ConvertAll(Array.FindAll(EditorBuildSettings.scenes, e => e.enabled), e => e.path);
            var missing = RequiredScenes.Where(r => !scenes.Any(s => Path.GetFileNameWithoutExtension(s) == r)).ToArray();
            if (missing.Length > 0) return Fail("Build Settings scenes miss " + string.Join(", ", missing));
            string introSrc = Path.Combine(Application.dataPath, "OpenZ/Scripts/Intro/OzIntroData.cs");
            if (File.Exists(introSrc) && !File.ReadAllText(introSrc).Contains("PlayerPrefs.GetInt(Pref, 0) == 1"))
                return Fail("the intro default is not OFF in OzIntroData.cs (expected PlayerPrefs.GetInt(Pref, 0) == 1)");

            SessionState.SetBool(GuardKey, true);          // from here on a repeated call builds nothing
            string result;
            try { result = Run(scenes); }
            catch (Exception e) { result = Fail("the Mac build method threw " + e.GetType().Name + ": " + e.Message); }
            SessionState.SetString(ResultKey, result.Length > 1500 ? result.Substring(0, 1500) : result);
            return result;
        }

        [MenuItem("OpenZ/Mac experimental/3 Switch the editor back to Windows", false, 62)]
        public static string SwitchBackToWindows()
        {
            if (EditorApplication.isPlaying) return Note("stop Play first");
            if (EditorApplication.isCompiling) return Note("wait for the compile to finish");
            if (EditorUserBuildSettings.activeBuildTarget == BuildTarget.StandaloneWindows64) return Note("the editor is on Windows already");
            UnityEditor.SceneManagement.EditorSceneManager.SaveOpenScenes(); AssetDatabase.SaveAssets();
            DateTime t0 = DateTime.Now;
            bool ok = EditorUserBuildSettings.SwitchActiveBuildTarget(BuildTargetGroup.Standalone, BuildTarget.StandaloneWindows64);
            return Note((ok ? "switched back to Windows" : "the switch back to Windows was REFUSED") + $" after {(DateTime.Now - t0).TotalMinutes:F1} min; active target now {EditorUserBuildSettings.activeBuildTarget}");
        }

        [MenuItem("OpenZ/Mac experimental/State", false, 80)]
        public static string State()
        {
            return Note($"Mac module seen: {ModuleSeen}; active target: {EditorUserBuildSettings.activeBuildTarget}; build started in this session: {SessionState.GetBool(GuardKey, false)}; "
                        + "last result: " + SessionState.GetString(ResultKey, "none"));
        }

        [MenuItem("OpenZ/Mac experimental/Allow one more Mac build in this session", false, 81)]
        public static string AllowOneMore()
        {
            SessionState.EraseBool(GuardKey); SessionState.EraseString(ResultKey);
            return Note("the guard is cleared: '2 Build the Mac test app' will build once more");
        }

        static string Run(string[] scenes)
        {
            UnityEditor.SceneManagement.EditorSceneManager.SaveOpenScenes(); AssetDatabase.SaveAssets();
            DateTime now = DateTime.Now;
            string tag = OzVersion.Tag(now);
            string relRoot = Path.Combine(JobRoot, "release");
            string dir = Path.Combine(relRoot, "OpenZ_" + tag + FolderSuffix);
            string record = dir + "_BUILD_INFO.txt";
            Directory.CreateDirectory(relRoot);
            string kept = null;
            if (Directory.Exists(dir))
            {
                kept = dir + "_prev_" + now.ToString("yyyyMMdd_HHmmss");
                try { Directory.Move(dir, kept); }
                catch (Exception e) { return Fail("could not keep the older same-day Mac folder " + dir + ": " + e.Message); }
            }
            Directory.CreateDirectory(dir);

            // ---- the player settings the Windows release build sets (the same values; they stay as that build leaves them)
            PlayerPrefs.SetInt("oz_testmode", 0); PlayerPrefs.SetInt("oz_gate", 0); PlayerPrefs.Save();
            Debug.Log("[OpenZ Mac] " + OpenZBuild.IncludeRuntimeShaders());
            PlayerSettings.fullScreenMode = FullScreenMode.FullScreenWindow; PlayerSettings.defaultIsNativeResolution = true; PlayerSettings.allowFullscreenSwitch = true;
            PlayerSettings.defaultScreenWidth = 1920; PlayerSettings.defaultScreenHeight = 1080;
            PlayerSettings.resizableWindow = true; PlayerSettings.runInBackground = true;

            // ---- release settings, for this build only (restored in finally)
            string oldCompany = PlayerSettings.companyName, oldVersion = PlayerSettings.bundleVersion;
            string oldId = PlayerSettings.GetApplicationIdentifier(NamedBuildTarget.Standalone);
            string archNote = AskForIntel64();

            var opts = new BuildPlayerOptions
            {
                scenes = scenes,
                locationPathName = Path.Combine(dir, "OpenZ.app"),
                target = BuildTarget.StandaloneOSX,
                targetGroup = BuildTargetGroup.Standalone,
                options = BuildOptions.None,
                extraScriptingDefines = new[] { OpenZReleaseBuild.Define },
            };

            BuildReport rep;
            try
            {
                PlayerSettings.companyName = OpenZReleaseBuild.ReleaseCompany;
                PlayerSettings.bundleVersion = tag;
                PlayerSettings.SetApplicationIdentifier(NamedBuildTarget.Standalone, MacBundleId);
                OpenZReleaseBuild.Building = true;
                rep = BuildPipeline.BuildPlayer(opts);
            }
            catch (Exception e) { return Fail("BuildPlayer threw " + e.GetType().Name + ": " + e.Message); }
            finally
            {
                OpenZReleaseBuild.Building = false;
                PlayerSettings.companyName = oldCompany;
                PlayerSettings.bundleVersion = oldVersion;
                if (!string.IsNullOrEmpty(oldId)) PlayerSettings.SetApplicationIdentifier(NamedBuildTarget.Standalone, oldId);
                AssetDatabase.SaveAssets();
            }
            if (rep == null) return Fail("BuildPlayer returned no report");

            var s = rep.summary;
            string exe = Path.Combine(dir, "OpenZ.app", "Contents", "MacOS", "OpenZ");
            string arch = File.Exists(exe) ? MachoProcessor(exe) : "no executable (" + exe + ")";
            bool built = s.result == BuildResult.Succeeded && (s.options & BuildOptions.Development) == 0;
            bool usable = built && arch == "x86_64";

            var info = new StringBuilder();
            info.AppendLine($"OpenZ EXPERIMENTAL Mac build {OzVersion.Display(now)}  (tag {tag})");
            info.AppendLine($"result: {s.result}");
            info.AppendLine("usable: " + (usable ? "yes" : "NO"));
            info.AppendLine($"time: {now:yyyy-MM-dd HH:mm}  duration: {s.totalTime}");
            info.AppendLine($"errors: {s.totalErrors}, warnings: {s.totalWarnings}");
            info.AppendLine($"options: {s.options} (non-development), define {OpenZReleaseBuild.Define}, company {OpenZReleaseBuild.ReleaseCompany}, bundle id {MacBundleId}, bundleVersion {tag} (restored to '{oldCompany}' / '{oldId}' / '{oldVersion}')");
            info.AppendLine("scenes: " + string.Join(", ", scenes.Select(Path.GetFileNameWithoutExtension)));
            info.AppendLine($"target: {s.platform}, asked for Intel 64-bit ({archNote})");
            info.AppendLine("executable header: " + arch);
            info.AppendLine("output: " + dir);
            if (kept != null) info.AppendLine("older same-day Mac folder kept: " + kept);
            info.AppendLine(BuildMessages(rep));
            info.AppendLine("next: the post-build script for the Mac folder, outside the editor");
            try { File.WriteAllText(record, info.ToString()); }
            catch (Exception e) { Debug.LogError("[OpenZ Mac] could not write the build record " + record + ": " + e.Message); }

            if (!built) return Fail($"build {s.result} ({s.totalErrors} errors), see the console; record {record}");
            if (!usable) return Fail("the built executable is not Intel 64-bit only (" + arch + "); record " + record);
            Debug.Log("[OpenZ Mac] " + info);
            return info.ToString();
        }

        /// <summary>Asks the Mac build module for an Intel 64-bit player. The module is not referenced at compile time: its
        /// setting is looked up by name, and the plain platform setting of the same meaning is written as well. Whether it took
        /// effect is proven after the build by reading the executable's header.</summary>
        static string AskForIntel64()
        {
            var notes = new List<string>();
            try
            {
                foreach (var asm in AppDomain.CurrentDomain.GetAssemblies())
                {
                    Type t = asm.GetType("UnityEditor.OSXStandalone.UserBuildSettings", false);
                    if (t == null) continue;
                    PropertyInfo p = t.GetProperty("architecture", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static);
                    if (p == null || !p.CanWrite || !p.PropertyType.IsEnum) continue;
                    object before = p.GetValue(null, null);
                    p.SetValue(null, Enum.Parse(p.PropertyType, "x64", true), null);
                    notes.Add("module setting " + before + " -> " + p.GetValue(null, null));
                    break;
                }
            }
            catch (Exception e) { notes.Add("module setting not set: " + e.GetType().Name); }
            try
            {
                string before = EditorUserBuildSettings.GetPlatformSettings("OSXUniversal", "Architecture");
                if (!string.Equals(before, "x64", StringComparison.OrdinalIgnoreCase))
                {
                    EditorUserBuildSettings.SetPlatformSettings("OSXUniversal", "Architecture", "x64");
                    notes.Add("platform setting '" + before + "' -> 'x64'");
                }
                else notes.Add("platform setting x64");
            }
            catch (Exception e) { notes.Add("platform setting not set: " + e.GetType().Name); }
            return string.Join("; ", notes);
        }

        /// <summary>The processor type in the header of a Mach-O file: "x86_64", "arm64", or a text that says what else it is.</summary>
        public static string MachoProcessor(string path)
        {
            try
            {
                var h = new byte[8];
                using (var fs = File.OpenRead(path)) { if (fs.Read(h, 0, 8) < 8) return "too short to be a program"; }
                uint magic = BitConverter.ToUInt32(h, 0), cpu = BitConverter.ToUInt32(h, 4);
                if (magic == 0xFEEDFACF) return cpu == 0x01000007 ? "x86_64" : cpu == 0x0100000C ? "arm64" : "processor type 0x" + cpu.ToString("X8");
                if (magic == 0xBEBAFECA || magic == 0xBFBAFECA) return "universal (more than one processor type)";
                return "not a 64-bit Mach-O file (first bytes 0x" + magic.ToString("X8") + ")";
            }
            catch (Exception e) { return "unreadable: " + e.Message; }
        }

        /// <summary>The error lines of the build report, and every line that names a shader, for the build record (at most 30).</summary>
        static string BuildMessages(BuildReport rep)
        {
            var lines = new List<string>(); int errors = 0, shader = 0;
            try
            {
                foreach (var step in rep.steps)
                    foreach (var m in step.messages)
                    {
                        bool isError = m.type == LogType.Error || m.type == LogType.Exception || m.type == LogType.Assert;
                        bool isShader = m.content != null && m.content.IndexOf("shader", StringComparison.OrdinalIgnoreCase) >= 0;
                        if (isError) errors++;
                        if (isShader) shader++;
                        if ((isError || isShader) && lines.Count < 30)
                        {
                            string c = (m.content ?? "").Replace("\r", " ").Replace("\n", " ");
                            lines.Add("   " + m.type + ": " + (c.Length > 300 ? c.Substring(0, 300) : c));
                        }
                    }
            }
            catch (Exception e) { lines.Add("   (the build report's messages could not be read: " + e.GetType().Name + ")"); }
            return $"build report messages: {errors} error line(s), {shader} line(s) that name a shader" + (lines.Count > 0 ? "\n" + string.Join("\n", lines) : "");
        }

        static string Note(string what) { Debug.Log("[OpenZ Mac] " + what); return what; }
        static string Fail(string why) { Debug.LogError("[OpenZ Mac] NOT BUILT: " + why); return "FAILED: " + why; }
    }
}
