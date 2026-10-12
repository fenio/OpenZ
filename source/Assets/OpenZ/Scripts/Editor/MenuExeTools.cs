using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace OpenZ.EditorTools
{
    /// <summary>menu_exe_v1: OpenZ > Menu Exe > Status (no Play: build settings, music in Resources, the saved settings and keys,
    /// the HUD layout math at 2560x1600 / 1080p), Run Play Proof (in Play, MainMenu scene), Apply Fullscreen PlayerSettings.</summary>
    public static class MenuExeTools
    {
        [MenuItem("OpenZ/Menu Exe/Status", false, 300)]
        public static string Status()
        {
            var sb = new System.Text.StringBuilder();
            sb.AppendLine($"[MenuExe] PlayerSettings: fullScreenMode {PlayerSettings.fullScreenMode}, native resolution {PlayerSettings.defaultIsNativeResolution}, default {PlayerSettings.defaultScreenWidth}x{PlayerSettings.defaultScreenHeight}, resizable {PlayerSettings.resizableWindow}, Alt+Enter {PlayerSettings.allowFullscreenSwitch} (OpenZ/Build Windows sets FullScreenWindow + native before building)");
            var scenes = EditorBuildSettings.scenes.Where(s => s.enabled).Select(s => Path.GetFileNameWithoutExtension(s.path)).ToArray();
            sb.AppendLine($"[MenuExe] build scenes (Build Settings): {string.Join(", ", scenes)}; OpenZBuild.Build uses MainMenu, L02_Desert, L17_CarPark");
            string mdir = "Assets/OpenZ/Resources/OpenZ/Audio/Music";
            int clips = Directory.Exists(mdir) ? Directory.GetFiles(mdir).Count(f => !f.EndsWith(".meta")) : 0;
            var menuClips = new[] { "OpenZ/Audio/Music/M1_menu_Brass_Tactics", "OpenZ/Audio/Music/M1_menu_Servo_Sergeant" }.Select(p => p.Substring(p.LastIndexOf('/') + 1) + (Resources.Load<AudioClip>(p) ? " OK" : " MISSING")).ToArray();
            sb.AppendLine($"[MenuExe] music in Resources (ships in every build): {clips} files; menu tracks {string.Join(", ", menuClips)}");
            sb.AppendLine("[MenuExe] settings (editor prefs): " + OzSettings.Describe());
            sb.AppendLine("[MenuExe] keys: " + OzBindings.Summary());
            foreach (var c in new[] { new Vector3(2560, 1600, 0), new Vector3(1920, 1080, 0), new Vector3(2560, 1440, 0), new Vector3(2560, 1600, 1.15f), new Vector3(1440, 1080, 1f) })
            {
                bool ok = MenuExeProof.Layout((int)c.x, (int)c.y, c.z, out var d);
                sb.AppendLine($"[MenuExe] HUD {c.x}x{c.y} {(c.z > 0 ? Mathf.RoundToInt(c.z * 100) + " %" : "default")}: {(ok ? "OK" : "OVERLAP")} {d}");
            }
            Debug.Log(sb.ToString());
            return sb.ToString();
        }

        [MenuItem("OpenZ/Menu Exe/Run Play Proof (MainMenu, in Play)", false, 301)]
        public static string RunProof()
        {
            string r = MenuExeProof.Run();
            Debug.Log("[MenuExe] " + r);
            return r;
        }

        [MenuItem("OpenZ/Menu Exe/Apply Fullscreen PlayerSettings", false, 302)]
        public static string ApplyFullscreen()
        {
            PlayerSettings.fullScreenMode = FullScreenMode.FullScreenWindow;
            PlayerSettings.defaultIsNativeResolution = true;
            PlayerSettings.allowFullscreenSwitch = true;
            AssetDatabase.SaveAssets();
            string r = $"PlayerSettings: {PlayerSettings.fullScreenMode}, native {PlayerSettings.defaultIsNativeResolution}, Alt+Enter {PlayerSettings.allowFullscreenSwitch}";
            Debug.Log("[MenuExe] " + r);
            return r;
        }
    }
}
