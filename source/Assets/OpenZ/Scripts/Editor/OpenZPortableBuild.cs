using System;
using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEngine;
using UnityEngine.Rendering;

namespace OpenZ.EditorTools
{
    /// <summary>Unofficial macOS/Linux release builds, independent of the Windows release workflow.</summary>
    public static class OpenZPortableBuild
    {
        static readonly string[] Scenes = { "MainMenu", "L02_Desert", "L16_Jungle", "L17_CarPark", "Intro" };

        /// <summary>Batch entry point: -ozPlatform macos|linux -ozOutput DIRECTORY [-ozArchitecture universal|arm64|x64].</summary>
        public static void Build()
        {
            try
            {
                string platform = Argument("-ozPlatform", null);
                string output = Argument("-ozOutput", null);
                if (string.IsNullOrEmpty(platform) || string.IsNullOrEmpty(output))
                    throw new ArgumentException("Supply -ozPlatform macos|linux and -ozOutput DIRECTORY.");
                BuildRelease(platform, output, Argument("-ozArchitecture", "universal"));
                if (Application.isBatchMode) EditorApplication.Exit(0);
            }
            catch (Exception e)
            {
                Debug.LogException(e);
                if (Application.isBatchMode) EditorApplication.Exit(1);
                else throw;
            }
        }

        [MenuItem("OpenZ/Unofficial/Build macOS release")]
        public static void BuildMacOS() => ChooseOutput("macos");

        [MenuItem("OpenZ/Unofficial/Build Linux release")]
        public static void BuildLinux() => ChooseOutput("linux");

        static void ChooseOutput(string platform)
        {
            string parent = EditorUtility.OpenFolderPanel("Choose parent folder for a new unofficial release", "", "");
            if (string.IsNullOrEmpty(parent)) return;
            BuildRelease(platform, Path.Combine(parent, "OpenZ-unofficial-" + platform + "-" + DateTime.Now.ToString("yyyyMMdd-HHmmss")), "universal");
        }

        static void BuildRelease(string platform, string output, string architecture)
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode || EditorApplication.isCompiling)
                throw new InvalidOperationException("Stop Play mode and wait for compilation before building.");

            BuildTarget target;
            if (platform == "macos") target = BuildTarget.StandaloneOSX;
            else if (platform == "linux") target = BuildTarget.StandaloneLinux64;
            else throw new ArgumentException("Unknown platform: " + platform);
            if (!BuildPipeline.IsBuildTargetSupported(BuildTargetGroup.Standalone, target))
                throw new InvalidOperationException("Install the Unity build support module for " + platform + ".");
            if (platform == "macos" && architecture != "universal" && architecture != "arm64" && architecture != "x64")
                throw new ArgumentException("macOS architecture must be universal, arm64 or x64.");
            if (platform == "linux" && architecture != "universal" && architecture != "x64")
                throw new ArgumentException("Linux builds support x64 only.");

            // The repository alone is not playable. Require the complete release project, including its notices.
            string[] scenes = Scenes.Select(s => "Assets/OpenZ/Scenes/" + s + ".unity").ToArray();
            foreach (string scene in scenes)
                if (!File.Exists(scene)) throw new FileNotFoundException("Missing scene; extract both full-project source archives first.", scene);
            string notices = Path.Combine(Application.streamingAssetsPath, "LICENSES.txt");
            if (!File.Exists(notices)) throw new FileNotFoundException("The release must include its license notices.", notices);
            string assetNotices = Path.Combine(Application.streamingAssetsPath, "LICENSE_ASSETS.txt");
            if (!File.Exists(assetNotices)) throw new FileNotFoundException("The release must include its asset license.", assetNotices);

            output = Path.GetFullPath(output);
            string assets = Path.GetFullPath(Application.dataPath);
            if (output.Equals(assets, StringComparison.OrdinalIgnoreCase) || output.StartsWith(assets + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
                throw new ArgumentException("Build outside Assets/.");
            foreach (string path in new[] { output, output + "_BUILD_INFO.txt", output + "_symbols" })
                if (Directory.Exists(path) || File.Exists(path))
                    throw new IOException("Output or build records already exist; choose a new directory. Nothing will be overwritten: " + path);

            string oldCompany = PlayerSettings.companyName, oldVersion = PlayerSettings.bundleVersion;
            FullScreenMode oldFullscreen = PlayerSettings.fullScreenMode;
            bool oldNative = PlayerSettings.defaultIsNativeResolution, oldResizable = PlayerSettings.resizableWindow;
            bool oldBackground = PlayerSettings.runInBackground, oldSwitch = PlayerSettings.allowFullscreenSwitch;
            int oldWidth = PlayerSettings.defaultScreenWidth, oldHeight = PlayerSettings.defaultScreenHeight;
            bool oldAutoAPI = PlayerSettings.GetUseDefaultGraphicsAPIs(target);
            GraphicsDeviceType[] oldAPIs = PlayerSettings.GetGraphicsAPIs(target);
#if UNITY_EDITOR_OSX
            OSArchitecture oldArchitecture = UnityEditor.OSXStandalone.UserBuildSettings.architecture;
#else
            if (platform == "macos") throw new InvalidOperationException("Build macOS releases on a Mac.");
#endif
            Directory.CreateDirectory(output);
            try
            {
                OpenZBuild.IncludeRuntimeShaders();
                PlayerSettings.companyName = OpenZReleaseBuild.ReleaseCompany;
                PlayerSettings.bundleVersion = OzVersion.Tag(DateTime.Now);
                PlayerSettings.fullScreenMode = FullScreenMode.FullScreenWindow;
                PlayerSettings.defaultIsNativeResolution = true;
                PlayerSettings.defaultScreenWidth = 1920; PlayerSettings.defaultScreenHeight = 1080;
                PlayerSettings.resizableWindow = true; PlayerSettings.runInBackground = true;
                PlayerSettings.allowFullscreenSwitch = true;
#if UNITY_EDITOR_OSX
                if (platform == "macos")
                {
                    UnityEditor.OSXStandalone.UserBuildSettings.architecture = architecture == "arm64" ? OSArchitecture.ARM64
                        : architecture == "x64" ? OSArchitecture.x64 : OSArchitecture.x64ARM64;
                    PlayerSettings.SetUseDefaultGraphicsAPIs(target, false);
                    PlayerSettings.SetGraphicsAPIs(target, new[] { GraphicsDeviceType.Metal });
                }
#endif
                OpenZReleaseBuild.Building = true; // Preserve the existing release assembly filter.
                var report = BuildPipeline.BuildPlayer(new BuildPlayerOptions
                {
                    scenes = scenes,
                    target = target,
                    targetGroup = BuildTargetGroup.Standalone,
                    locationPathName = Path.Combine(output, platform == "macos" ? "OpenZ.app" : "OpenZ"),
                    options = BuildOptions.None,
                    extraScriptingDefines = new[] { OpenZReleaseBuild.Define },
                });
                if (report == null) throw new InvalidOperationException("Unity returned no build report.");
                var summary = report.summary;
                File.WriteAllText(output + "_BUILD_INFO.txt",
                    $"Unofficial OpenZ build\nPlatform: {platform}\nArchitecture: {(platform == "linux" ? "x64" : architecture)}\n" +
                    $"Unity: {Application.unityVersion}\nResult: {summary.result}\nErrors: {summary.totalErrors}\nWarnings: {summary.totalWarnings}\nDuration: {summary.totalTime}\n");
                if (summary.result != BuildResult.Succeeded)
                    throw new InvalidOperationException("Build failed; do not distribute this folder. See the Unity log.");

                // Only move Unity's external debug folders. Do not modify the built .app (which may be signed).
                foreach (string directory in Directory.GetDirectories(output))
                {
                    string name = Path.GetFileName(directory);
                    if (!name.EndsWith("_DoNotShip", StringComparison.OrdinalIgnoreCase) &&
                        name.IndexOf("ButDontShipItWithYourGame", StringComparison.OrdinalIgnoreCase) < 0) continue;
                    string symbols = output + "_symbols";
                    Directory.CreateDirectory(symbols);
                    Directory.Move(directory, Path.Combine(symbols, name));
                }
                File.Copy(notices, Path.Combine(output, "LICENSES.txt"));
                File.Copy(assetNotices, Path.Combine(output, "LICENSE_ASSETS.txt"));
                File.WriteAllText(Path.Combine(output, "README-UNOFFICIAL.txt"),
                    $"OpenZ {OzVersion.Number} — UNOFFICIAL {platform} build\n" +
                    "Community build, not an official release or a promise of upstream support.\n" +
                    (platform == "macos" ? "Launch OpenZ.app.\n" : "Run ./OpenZ from this directory. Preserve the executable bit when extracting.\n") +
                    "Code: GPL-3.0. Assets: CC BY-NC-SA 4.0 (non-commercial, attribution, share-alike).\n" +
                    "Original project and credits: https://github.com/playopenz/OpenZ\n" +
                    "Not affiliated with Rebellion or The Bitmap Brothers.\n" +
                    "See LICENSES.txt and LICENSE_ASSETS.txt. Distributors must also provide the corresponding source and modifications.\n" +
                    "Verify files: shasum -a 256 -c SHA256SUMS.txt (macOS), sha256sum -c SHA256SUMS.txt (Linux).\n",
                    new UTF8Encoding(false));
                OpenZReleaseBuild.WriteSha256(output, out long bytes);
                Debug.Log($"[OpenZ Unofficial] Built {platform}: {output} ({bytes / (1024.0 * 1024.0):F0} MB). Test before distributing.");
            }
            finally
            {
                OpenZReleaseBuild.Building = false;
                PlayerSettings.companyName = oldCompany; PlayerSettings.bundleVersion = oldVersion;
                PlayerSettings.fullScreenMode = oldFullscreen; PlayerSettings.defaultIsNativeResolution = oldNative;
                PlayerSettings.defaultScreenWidth = oldWidth; PlayerSettings.defaultScreenHeight = oldHeight;
                PlayerSettings.resizableWindow = oldResizable; PlayerSettings.runInBackground = oldBackground;
                PlayerSettings.allowFullscreenSwitch = oldSwitch;
                PlayerSettings.SetGraphicsAPIs(target, oldAPIs);
                PlayerSettings.SetUseDefaultGraphicsAPIs(target, oldAutoAPI);
#if UNITY_EDITOR_OSX
                UnityEditor.OSXStandalone.UserBuildSettings.architecture = oldArchitecture;
#endif
                AssetDatabase.SaveAssets();
            }
        }

        static string Argument(string name, string fallback)
        {
            string[] args = Environment.GetCommandLineArgs();
            int index = Array.IndexOf(args, name);
            if (index < 0) return fallback;
            if (index + 1 == args.Length || args[index + 1].StartsWith("-", StringComparison.Ordinal))
                throw new ArgumentException("Missing value for " + name);
            return args[index + 1];
        }
    }
}
