using System.IO;
using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEngine;

namespace OpenZ.EditorTools
{
    /// <summary>
    /// "OpenZ/Build Windows": the enabled Build Settings scenes (Intro = index 0, MainMenu, L02, L17) into builds/OpenZ/OpenZ.exe (repo root)
    /// (x64, windowed 1920x1080 default). Writes builds/OpenZ/BUILD_INFO.txt (time, size, result) and logs the report summary.
    /// A heavy job: run it alone (no Play, no render, GPU cool).
    /// </summary>
    public static class OpenZBuild
    {
        [MenuItem("OpenZ/Build Windows", false, 20)]
        public static string Build()
        {
            if (EditorApplication.isPlaying) { Debug.LogWarning("[OpenZ] stop Play first"); return "playing"; }
            UnityEditor.SceneManagement.EditorSceneManager.SaveOpenScenes(); AssetDatabase.SaveAssets();
            string root = Path.GetFullPath(Path.Combine(Application.dataPath, "../../../.."));
            string dir = Path.Combine(root, "builds", "OpenZ"); Directory.CreateDirectory(dir);
            PlayerPrefs.SetInt("oz_testmode", 0); PlayerPrefs.SetInt("oz_gate", 0); PlayerPrefs.Save();
            var opts = new BuildPlayerOptions
            {
                scenes = System.Array.ConvertAll(System.Array.FindAll(EditorBuildSettings.scenes, e => e.enabled), e => e.path),
                locationPathName = Path.Combine(dir, "OpenZ.exe"),
                target = BuildTarget.StandaloneWindows64,
                options = BuildOptions.None,
            };
            Debug.Log("[OpenZ] " + IncludeRuntimeShaders());
            PlayerSettings.fullScreenMode = FullScreenMode.FullScreenWindow; PlayerSettings.defaultIsNativeResolution = true; PlayerSettings.allowFullscreenSwitch = true;   // menu_exe_v1: fullscreen at the native resolution (OzSettings keeps it remembered)
            PlayerSettings.defaultScreenWidth = 1920; PlayerSettings.defaultScreenHeight = 1080;
            PlayerSettings.resizableWindow = true; PlayerSettings.runInBackground = true;
            var rep = BuildPipeline.BuildPlayer(opts);
            var s = rep.summary;
            string info = $"OpenZ Windows build\nresult: {s.result}\ntime: {System.DateTime.Now:yyyy-MM-dd HH:mm}\nduration: {s.totalTime}\nsize: {s.totalSize / (1024f * 1024f):F0} MB\nerrors: {s.totalErrors}, warnings: {s.totalWarnings}\n" +
                          "run: OpenZ.exe  (options: -level L02|L17, -demo, -cinematic, -showcase, -maptop; Ctrl+Shift+F12 toggles the capture kit)\n";
            File.WriteAllText(Path.Combine(dir, "BUILD_INFO.txt"), info);
            Debug.Log("[OpenZ] " + info);
            return info;
        }

        /// <summary>Shaders looked up by name at runtime (Shader.Find) only reach a player build when something references them:
        /// the first build had no OpenZ/ScorchDecal and no particle shader for the atmosphere. Adds every shader under
        /// Assets/OpenZ/Shaders plus the named URP / built-in ones to Graphics Settings > Always Included Shaders (URP Lit and
        /// TMP are pulled in by materials already; always-including Lit would explode the variant count).</summary>
        [MenuItem("OpenZ/Build: Include Runtime Shaders", false, 21)]
        public static string IncludeRuntimeShaders()
        {
            var gs = AssetDatabase.LoadAssetAtPath<Object>("ProjectSettings/GraphicsSettings.asset");
            var so = new SerializedObject(gs); var arr = so.FindProperty("m_AlwaysIncludedShaders");
            var have = new System.Collections.Generic.HashSet<Shader>();
            for (int i = 0; i < arr.arraySize; i++) { var s = arr.GetArrayElementAtIndex(i).objectReferenceValue as Shader; if (s) have.Add(s); }
            var want = new System.Collections.Generic.List<Shader>();
            foreach (var g in AssetDatabase.FindAssets("t:Shader", new[] { "Assets/OpenZ/Shaders" })) { var s = AssetDatabase.LoadAssetAtPath<Shader>(AssetDatabase.GUIDToAssetPath(g)); if (s) want.Add(s); }
            foreach (var n in new[] { "Universal Render Pipeline/Particles/Unlit", "Sprites/Default", "OpenZ/ScorchDecal" }) { var s = Shader.Find(n); if (s) want.Add(s); }
            int added = 0;
            foreach (var s in want)
            {
                if (have.Contains(s)) continue;
                arr.InsertArrayElementAtIndex(arr.arraySize); arr.GetArrayElementAtIndex(arr.arraySize - 1).objectReferenceValue = s; have.Add(s); added++;
            }
            so.ApplyModifiedProperties(); AssetDatabase.SaveAssets();
            return $"always-included shaders: +{added} (total {arr.arraySize})";
        }
    }
}
