using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEngine;

namespace OpenZ.EditorTools
{
    /// <summary>
    ///  * non-development player (BuildOptions.None, checked), Windows x64, the Build Settings scenes (Intro, MainMenu, L02_Desert,
    ///    L17_CarPark all required; the intro ships but stays OFF by default, checked in OzIntroData.cs);
    ///  * scripting define OZ_RELEASE for this build only (extraScriptingDefines): every -ozdev / take / capture / cheat path is
    ///  * version from ONE source: OzVersion.Number + the build day (OzVersion.Tag) -> PlayerSettings.bundleVersion (build only),
    ///    output release\OpenZ_&lt;tag&gt;\OpenZ.exe (an older same-day folder is kept as OpenZ_&lt;tag&gt;_prev_&lt;yyyyMMdd_HHmm&gt;,
    ///    nothing deleted, the builds\ convention);
    ///  * company name "OpenZ" for this build only (the project's own company name is a channel name: exe properties, app.info,
    ///    registry and AppData folders must not carry it); PDB copy off; restored afterwards;
    ///  * post-build: the Burst debug info (*_DoNotShip) and any *.pdb move to release\_symbols\OpenZ_&lt;tag&gt; (never shipped),
    ///    README.txt (player readme, no personal info) + LICENSES.txt at the top, then SHA256SUMS.txt over every shipped file;
    ///    the dev record goes NEXT to the folder (release\OpenZ_&lt;tag&gt;_BUILD_INFO.txt), not into it.
    /// </summary>
    public static class OpenZReleaseBuild
    {
        public const string Define = "OZ_RELEASE";
        public const string ReleaseCompany = "OpenZ";
        static readonly string[] RequiredScenes = { "Intro", "MainMenu", "L02_Desert", "L16_Jungle", "L17_CarPark" };
        internal static bool Building;
        /// <summary>A letter after the day tag for a second private test build on one day (release\OpenZ_&lt;tag&gt;&lt;suffix&gt;); empty by
        /// default. Only the folder names change: bundleVersion stays the day tag, so the in-game version text still reads the build day.</summary>
        public static string FolderSuffix = "";

        static string JobRoot => Path.GetFullPath(Path.Combine(Application.dataPath, "../../../.."));

        [MenuItem("OpenZ/Release/Build final exe", false, 30)]
        public static string BuildFinal()
        {
            if (EditorApplication.isPlaying) { Debug.LogWarning("[OpenZ Release] stop Play first"); return "playing"; }
            if (EditorApplication.isCompiling) { Debug.LogWarning("[OpenZ Release] wait for the compile to finish"); return "compiling"; }
            UnityEditor.SceneManagement.EditorSceneManager.SaveOpenScenes(); AssetDatabase.SaveAssets();

            // ---- pre-flight
            var scenes = Array.ConvertAll(Array.FindAll(EditorBuildSettings.scenes, e => e.enabled), e => e.path);
            var missing = RequiredScenes.Where(r => !scenes.Any(s => Path.GetFileNameWithoutExtension(s) == r)).ToArray();
            if (missing.Length > 0) return Fail("Build Settings scenes miss " + string.Join(", ", missing) + " (Intro, MainMenu, L02_Desert, L16_Jungle, L17_CarPark must be enabled)");
            string introSrc = Path.Combine(Application.dataPath, "OpenZ/Scripts/Intro/OzIntroData.cs");
            if (File.Exists(introSrc) && !File.ReadAllText(introSrc).Contains("PlayerPrefs.GetInt(Pref, 0) == 1"))
                return Fail("the intro default is not OFF in OzIntroData.cs (expected PlayerPrefs.GetInt(Pref, 0) == 1)");

            DateTime now = DateTime.Now;
            string tag = OzVersion.Tag(now);
            string relRoot = Path.Combine(JobRoot, "release");
            string dir = Path.Combine(relRoot, "OpenZ_" + tag + FolderSuffix);
            Directory.CreateDirectory(relRoot);
            string kept = null;
            if (Directory.Exists(dir))
            {
                kept = dir + "_prev_" + now.ToString("yyyyMMdd_HHmm");
                if (Directory.Exists(kept)) kept = dir + "_prev_" + now.ToString("yyyyMMdd_HHmmss");
                try { Directory.Move(dir, kept); }
                catch (Exception e) { return Fail("could not keep the older same-day release " + dir + " (open in Explorer / the exe running?): " + e.Message); }
            }
            Directory.CreateDirectory(dir);

            // ---- release settings (build only, restored in finally)
            string oldCompany = PlayerSettings.companyName, oldVersion = PlayerSettings.bundleVersion;
            string oldPdb = EditorUserBuildSettings.GetPlatformSettings("Standalone", "CopyPDBFiles");
            PlayerPrefs.SetInt("oz_testmode", 0); PlayerPrefs.SetInt("oz_gate", 0); PlayerPrefs.Save();
            Debug.Log("[OpenZ Release] " + OpenZBuild.IncludeRuntimeShaders());
            PlayerSettings.fullScreenMode = FullScreenMode.FullScreenWindow; PlayerSettings.defaultIsNativeResolution = true; PlayerSettings.allowFullscreenSwitch = true;
            PlayerSettings.defaultScreenWidth = 1920; PlayerSettings.defaultScreenHeight = 1080;
            PlayerSettings.resizableWindow = true; PlayerSettings.runInBackground = true;

            var opts = new BuildPlayerOptions
            {
                scenes = scenes,
                locationPathName = Path.Combine(dir, "OpenZ.exe"),
                target = BuildTarget.StandaloneWindows64,
                targetGroup = BuildTargetGroup.Standalone,
                options = BuildOptions.None,
                extraScriptingDefines = new[] { Define },
            };
            if ((opts.options & (BuildOptions.Development | BuildOptions.AllowDebugging | BuildOptions.ConnectWithProfiler | BuildOptions.EnableDeepProfilingSupport)) != 0)
                return Fail("development options set");

            BuildReport rep;
            try
            {
                PlayerSettings.companyName = ReleaseCompany;
                PlayerSettings.bundleVersion = tag;
                EditorUserBuildSettings.SetPlatformSettings("Standalone", "CopyPDBFiles", "false");
                Building = true;
                rep = BuildPipeline.BuildPlayer(opts);
            }
            catch (Exception e) { return Fail("BuildPlayer threw " + e.GetType().Name + ": " + e.Message); }
            finally
            {
                Building = false;
                PlayerSettings.companyName = oldCompany;
                PlayerSettings.bundleVersion = oldVersion;
                EditorUserBuildSettings.SetPlatformSettings("Standalone", "CopyPDBFiles", string.IsNullOrEmpty(oldPdb) ? "false" : oldPdb);
                AssetDatabase.SaveAssets();
            }

            if (rep == null) return Fail("BuildPlayer returned no report");
            var s = rep.summary;
            var info = new StringBuilder();
            info.AppendLine($"OpenZ release build {OzVersion.Display(now)}  (tag {tag}{(string.IsNullOrEmpty(FolderSuffix) ? "" : ", folder OpenZ_" + tag + FolderSuffix)})");
            info.AppendLine($"result: {s.result}");
            info.AppendLine($"time: {now:yyyy-MM-dd HH:mm}  duration: {s.totalTime}");
            info.AppendLine($"errors: {s.totalErrors}, warnings: {s.totalWarnings}");
            info.AppendLine($"options: {s.options} (non-development), define {Define}, company {ReleaseCompany}, bundleVersion {tag} (both restored to '{oldCompany}' / '{oldVersion}')");
            info.AppendLine("scenes: " + string.Join(", ", scenes.Select(Path.GetFileNameWithoutExtension)));
            info.AppendLine("output: " + dir);
            if (kept != null) info.AppendLine("older same-day release kept: " + kept);
            if (s.result != BuildResult.Succeeded || (s.options & BuildOptions.Development) != 0)
            {
                File.WriteAllText(dir + "_BUILD_INFO.txt", info.ToString());
                return Fail($"build {s.result} ({s.totalErrors} errors), see the console; record {dir}_BUILD_INFO.txt");
            }

            // ---- post-build: nothing debug ships
            string sym = Path.Combine(relRoot, "_symbols", "OpenZ_" + tag + FolderSuffix + (kept != null ? "_" + now.ToString("HHmm") : ""));
            if (Directory.Exists(sym)) sym += "_" + now.ToString("HHmmss");
            try
            {
            int moved = 0;
            foreach (var d in Directory.GetDirectories(dir))
            {
                string n = Path.GetFileName(d);
                if (n.EndsWith("_DoNotShip", StringComparison.OrdinalIgnoreCase) || n.IndexOf("ButDontShipItWithYourGame", StringComparison.OrdinalIgnoreCase) >= 0)
                { Directory.CreateDirectory(sym); Directory.Move(d, Path.Combine(sym, n)); moved++; }
            }
            foreach (var f in Directory.GetFiles(dir, "*", SearchOption.AllDirectories))
            {
                string ext = Path.GetExtension(f).ToLowerInvariant();
                if (ext == ".pdb" || ext == ".mdb" || ext == ".log" || ext == ".dmp")
                {
                    string to = Path.Combine(sym, f.Substring(dir.Length + 1));
                    Directory.CreateDirectory(Path.GetDirectoryName(to)); File.Move(f, to); moved++;
                }
            }
            info.AppendLine($"debug files moved out of the release: {moved} -> {sym}");

            info.AppendLine(ScrubPdbPaths(dir));
            File.WriteAllText(Path.Combine(dir, "README.txt"), PlayerReadme(now).Replace("\n", "\r\n"), new UTF8Encoding(false));
            string lic = Path.Combine(dir, "OpenZ_Data", "StreamingAssets", "LICENSES.txt");
            if (File.Exists(lic)) File.Copy(lic, Path.Combine(dir, "LICENSES.txt"), true);
            else info.AppendLine("WARNING: OpenZ_Data/StreamingAssets/LICENSES.txt missing (the licence texts must ship)");
            string licAssets = Path.Combine(dir, "OpenZ_Data", "StreamingAssets", "LICENSE_ASSETS.txt");
            if (File.Exists(licAssets)) File.Copy(licAssets, Path.Combine(dir, "LICENSE_ASSETS.txt"), true);
            else info.AppendLine("WARNING: OpenZ_Data/StreamingAssets/LICENSE_ASSETS.txt missing (the assets licence text must ship)");

            int n2 = WriteSha256(dir, out long bytes);
            info.AppendLine($"SHA256SUMS.txt: {n2} files, {bytes / (1024.0 * 1024.0):F0} MB");
            info.AppendLine(QuickLeakCheck(dir, oldCompany));
            }
            catch (Exception e)
            {
                info.AppendLine("POST-BUILD FAILED (" + e.GetType().Name + ": " + e.Message + "): do NOT ship this folder, fix and build again");
                File.WriteAllText(dir + "_BUILD_INFO.txt", info.ToString());
                return Fail("post-build step threw " + e.GetType().Name + ": " + e.Message + "; record " + dir + "_BUILD_INFO.txt");
            }
            info.AppendLine("next: python tools/scan_build_privacy.py \"" + dir + "\"  and  python tools/check_defaults.py --build \"" + dir + "\", then the 2-minute smoke test");
            File.WriteAllText(dir + "_BUILD_INFO.txt", info.ToString());
            Debug.Log("[OpenZ Release] " + info);
            return info.ToString();
        }

        /// <summary>Our assemblies carry the absolute path of their PDB in the PE debug directory (CodeView "RSDS" record:
        /// with the bare file name (zero padded, same length), in every Managed / Plugins dll whose record holds a drive path.
        /// Unity's own signed binaries (the exe stub, UnityPlayer.dll, MonoBleedingEdge) are not touched.</summary>
        public static string ScrubPdbPaths(string dir)
        {
            int files = 0, recs = 0;
            var roots = new[] { Path.Combine(dir, "OpenZ_Data", "Managed"), Path.Combine(dir, "OpenZ_Data", "Plugins") };
            foreach (var root in roots)
            {
                if (!Directory.Exists(root)) continue;
                foreach (var f in Directory.GetFiles(root, "*.dll", SearchOption.AllDirectories))
                {
                    byte[] d = File.ReadAllBytes(f); int changed = 0;
                    for (int i = 0; i + 24 < d.Length; i++)
                    {
                        if (d[i] != (byte)'R' || d[i + 1] != (byte)'S' || d[i + 2] != (byte)'D' || d[i + 3] != (byte)'S') continue;
                        int p0 = i + 24, p1 = p0;
                        while (p1 < d.Length && d[p1] != 0 && p1 - p0 < 1024) p1++;
                        if (p1 >= d.Length || p1 - p0 < 4) continue;
                        string path = Encoding.UTF8.GetString(d, p0, p1 - p0);
                        if (path.Length < 3 || path[1] != ':' || !path.EndsWith(".pdb", StringComparison.OrdinalIgnoreCase)) continue;
                        byte[] name = Encoding.UTF8.GetBytes(Path.GetFileName(path.Replace('/', '\\')));
                        for (int k = p0; k < p1; k++) d[k] = k - p0 < name.Length ? name[k - p0] : (byte)0;
                        changed++; i = p1;
                    }
                    if (changed > 0) { File.WriteAllBytes(f, d); files++; recs += changed; }
                }
            }
            return $"PDB paths scrubbed: {recs} record(s) in {files} dll(s)";
        }

        static string Fail(string why) { Debug.LogError("[OpenZ Release] NOT BUILT: " + why); return "FAILED: " + why; }

        /// <summary>SHA256SUMS.txt in the `sha256sum -c` format ("&lt;hex&gt; *&lt;path&gt;", forward slashes), every file but itself.</summary>
        public static int WriteSha256(string dir, out long bytes)
        {
            bytes = 0;
            var files = Directory.GetFiles(dir, "*", SearchOption.AllDirectories)
                .Select(f => f.Substring(dir.Length + 1).Replace('\\', '/'))
                .Where(r => !r.Equals("SHA256SUMS.txt", StringComparison.OrdinalIgnoreCase))
                .OrderBy(r => r, StringComparer.Ordinal).ToList();
            var sb = new StringBuilder();
            try
            {
                using (var sha = SHA256.Create())
                    for (int i = 0; i < files.Count; i++)
                    {
                        string p = Path.Combine(dir, files[i]);
                        if (i % 8 == 0) EditorUtility.DisplayProgressBar("OpenZ release", "SHA256 " + files[i], i / (float)files.Count);
                        using (var fs = File.OpenRead(p)) { bytes += fs.Length; sb.Append(BitConverter.ToString(sha.ComputeHash(fs)).Replace("-", "").ToLowerInvariant()).Append(" *").Append(files[i]).Append('\n'); }
                    }
            }
            finally { EditorUtility.ClearProgressBar(); }
            File.WriteAllText(Path.Combine(dir, "SHA256SUMS.txt"), sb.ToString(), new UTF8Encoding(false));
            return files.Count;
        }

        /// <summary>A fast in-editor look at the game dll + app.info for the job folder, user and company names.</summary>
        static string QuickLeakCheck(string dir, string projectCompany)
        {
            // project's own company name), so this source file carries no dev folder or channel name itself
            var terms = new List<string> { Path.GetFileName(Path.GetDirectoryName(JobRoot)), Environment.UserName, projectCompany };
            var hits = new List<string>();
            foreach (var rel in new[] { "OpenZ_Data/Managed/Assembly-CSharp.dll", "OpenZ_Data/app.info", "OpenZ.exe" })
            {
                string p = Path.Combine(dir, rel);
                if (!File.Exists(p)) continue;
                byte[] data = File.ReadAllBytes(p);
                string a = Encoding.ASCII.GetString(data), u = Encoding.Unicode.GetString(data);
                foreach (var t in terms)
                    if (!string.IsNullOrEmpty(t) && t.Length >= 3 && (a.IndexOf(t, StringComparison.OrdinalIgnoreCase) >= 0 || u.IndexOf(t, StringComparison.OrdinalIgnoreCase) >= 0))
                        hits.Add(rel + ": " + (t == Environment.UserName ? "<windows user name>" : t == projectCompany ? "<project company name>" : "<job-root folder name>"));
            }
            if (hits.Count > 0) Debug.LogError("[OpenZ Release] quick leak check FAILED: " + string.Join("; ", hits));
            return hits.Count == 0 ? "quick leak check (dll / app.info / exe): clean" : "quick leak check FAILED: " + string.Join("; ", hits);
        }

        static string PlayerReadme(DateTime d)
        {
            return
$@"{OzVersion.Display(d)}
==============================
A faithful fan tribute remake of Z by The Bitmap Brothers (1996), made for its 30th anniversary.
Free and non-commercial. Three maps: Z level 2 (desert), Z level 16 (jungle) and Z level 17 ""Car Park"" (city).

START
- Run OpenZ.exe. The game opens full screen at your screen's resolution (Alt+Enter or OPTIONS > FULLSCREEN for a window).
- Pick a map in the main menu, choose the difficulty and deploy.
- Capture the territory flags, build robots and vehicles in your factories and fort, and destroy the enemy fort.
- CONTROLS in the main menu lists every key and lets you change them. OPTIONS holds music, screen and game settings.

SYSTEM
- Windows 10 / 11, 64-bit, a DirectX 12 graphics card. About 4 GB on disk.
- Settings are stored per Windows user. A log for bug reports is written to
  %USERPROFILE%\AppData\LocalLow\OpenZ\OpenZ\Player.log
- If the game crashes or is closed by force, the log of that session is kept at the next start (the last 10) in
  %USERPROFILE%\AppData\LocalLow\OpenZ\OpenZ\CrashLogs
  Please attach the newest file from there to a bug report. The game itself sends nothing anywhere.

NEW IN 1.1
- A third map: Z level 16, the Jungle. Swamps only robots can cross, crocodiles, rain, ruins, rope bridges and trees you can shoot down.
- The Jungle has its own spoken briefing on the sun loungers and its own win and lose scenes.
- Alan & Brad join your army on the Jungle as a hero squad of two: tough, self-repairing, with grenades.
- Sniper headshots: now and then a Sniper's hit kills a robot at once.
- Grenades: a slightly wider blast and 15% more damage.
- Pyros set buildings and bridges on fire: hold Ctrl when you order the attack.
- The shuttle: on the Jungle (Easy and Medium), Alan and Brad's shuttle flies one squad over swamps, cliffs and trees and lands on any clear spot.
- Jeeps drive at big robots to run them over, then drive back; your orders always come first.
- No more empty vehicles blocking a bridge or a door: drivers can't be shot out there.
- FRIENDLY UNITS OVERLAP (OPTIONS, off by default): units pass through each other, as in the original.
- FAST UNITS PASS SLOW ONES (OPTIONS, off by default): faster units overtake slower ones.
- The CPU can gather a strike group and attack with it as one body (OPTIONS switches it off), and builds a wider mix of units.
- HIGH SCORES (key T on the map screen): your ten best victories per map and difficulty, each with a score and an army rank. Kept on your computer only.
- Finished guns wait as READY icons beside their building: click to place, or drag onto the map.
- A selected gun's range ring shows where it cannot hit behind rocks, cliffs and buildings.
- Drag the panels of the in-game interface where you want them; the U key hides and shows the lower panels.
- FREE CAMERA (OPTIONS, off by default): hold the middle mouse button to turn and tilt the camera.
- OPTIONS has a RESOLUTION setting for slower computers. The default is still your screen's own resolution.
- MEDIUM and LOW graphics quality are lighter on slower computers; HIGH, the default, is unchanged.
- A FRAME RATE LIMIT option (30 or 60), and a one-time tip when the game runs slowly.
- CHANGES & UPDATES in the main menu lists every change.

EXPERIMENTAL (provided as is, not tried on real devices: tell us how it works for you)
- Touch screen and gamepad input (the TOUCH and GAMEPAD row on the ADVANCED tab of OPTIONS).
- A Mac build, as a separate download.

FILES
- LICENSES.txt: the licence texts (GPL-3.0, the Zod Engine notices, the SIL Open Font License of the Inter Tight font).
- LICENSE_ASSETS.txt: the licence of the art, sound and all other assets (CC BY-NC-SA 4.0).
- SHA256SUMS.txt: a checksum of every file. Check one in PowerShell: Get-FileHash OpenZ.exe -Algorithm SHA256

NOTES
- OpenZ is an unofficial, free, non-commercial fan remake, not affiliated with or endorsed by Rebellion.
  Z (c) Rebellion. Originally created by The Bitmap Brothers (1996).
- No copyrighted material is used: every asset was made from scratch. The game rules follow the open-source Zod Engine (GPL-3.0).
- The program code is under the GNU GPL v3; the art, sound and all other assets are under the Creative Commons
  Attribution-NonCommercial-ShareAlike 4.0 International licence (CC BY-NC-SA 4.0). See LICENSES.txt and LICENSE_ASSETS.txt.
";
        }
    }

    /// <summary>
    /// of the release player. Other builds are untouched.</summary>
    class OzReleaseAssemblyFilter : IFilterBuildAssemblies
    {
        public int callbackOrder => 0;
        public string[] OnFilterAssemblies(BuildOptions buildOptions, string[] assemblies)
        {
            if (!OpenZReleaseBuild.Building) return assemblies;
            var keep = assemblies.Where(a => !Path.GetFileName(a).StartsWith("EditorBridge", StringComparison.OrdinalIgnoreCase)).ToArray();
            if (keep.Length != assemblies.Length) Debug.Log($"[OpenZ Release] left out of the player: {string.Join(", ", assemblies.Except(keep).Select(Path.GetFileName))}");
            return keep;
        }
    }
}
