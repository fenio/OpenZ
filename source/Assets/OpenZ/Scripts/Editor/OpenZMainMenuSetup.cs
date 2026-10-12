using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem.UI;
using UnityEngine.Rendering.Universal;
using UnityEngine.SceneManagement;

namespace OpenZ.EditorTools
{
    /// <summary>
    /// Front-end setup (UI_STYLE_GUIDE.md 9.14 / 9.15): creates Assets/OpenZ/Scenes/MainMenu.unity (camera, EventSystem with
    /// the Input System UI module, the OpenZMainMenu bootstrap), puts the build order to [MainMenu, L02_Desert, L17_CarPark],
    /// sets the import settings of the menu previews and copies CREDITS.md into Resources as the credits TextAsset. Every
    /// step is idempotent; the level scenes are not touched and still start on their own.
    /// </summary>
    public static class OpenZMainMenuSetup
    {
        public const string ScenesDir = "Assets/OpenZ/Scenes";
        public const string MenuScene = ScenesDir + "/MainMenu.unity";
        public const string MenuResources = "Assets/OpenZ/Resources/OpenZ/Menu";
        public const string CreditsAsset = MenuResources + "/credits.txt";
        static readonly string[] Order = { MenuScene, ScenesDir + "/L02_Desert.unity", ScenesDir + "/L17_CarPark.unity" };

        [MenuItem("OpenZ/Menu/Build Main Menu Scene", false, 1)]
        public static void BuildMainMenuScene()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode) { Debug.LogError("[OpenZ Menu] stop Play mode first"); return; }
            EditorSceneManager.SaveOpenScenes();                                                             // never lose the level being edited
            AssetDatabase.SaveAssets();
            string previous = SceneManager.GetActiveScene().path;
            Directory.CreateDirectory(ScenesDir);
            Directory.CreateDirectory(MenuResources);
            RefreshCredits();
            ConfigureMenuAssets();

            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

            // camera: clears to GM_950; the whole menu is a Screen Space Overlay canvas, so it draws nothing else
            var camGo = new GameObject("MenuCamera") { tag = "MainCamera" };
            var cam = camGo.AddComponent<Camera>();
            cam.clearFlags = CameraClearFlags.SolidColor; cam.backgroundColor = new Color32(11, 13, 12, 255);
            cam.cullingMask = 0; cam.orthographic = true; cam.nearClipPlane = 0.1f; cam.farClipPlane = 10f; cam.allowMSAA = false; cam.allowHDR = false;
            var urp = camGo.GetComponent<UniversalAdditionalCameraData>();
            if (!urp) urp = camGo.AddComponent<UniversalAdditionalCameraData>();
            urp.renderPostProcessing = false; urp.antialiasing = AntialiasingMode.None; urp.renderShadows = false;
            camGo.AddComponent<AudioListener>();

            // EventSystem with the Input System UI module (the project runs the new Input System only)
            var esGo = new GameObject("EventSystem");
            esGo.AddComponent<EventSystem>();
            var module = esGo.AddComponent<InputSystemUIInputModule>();
            module.AssignDefaultActions();

            // bootstrap: the runtime menu builds its whole UI in code (like OpenZHud)
            var boot = new GameObject("OpenZ_MainMenu");
            boot.AddComponent<OpenZMainMenu>();

            RenderSettings.ambientMode = UnityEngine.Rendering.AmbientMode.Flat;
            RenderSettings.ambientLight = new Color32(11, 13, 12, 255);
            RenderSettings.skybox = null;

            if (!EditorSceneManager.SaveScene(scene, MenuScene)) { Debug.LogError("[OpenZ Menu] could not save " + MenuScene); return; }
            SetBuildOrder();
            AssetDatabase.SaveAssets();
            Debug.Log($"[OpenZ Menu] {MenuScene} built and saved (camera, EventSystem + InputSystemUIInputModule, OpenZ_MainMenu). Build order: {string.Join(", ", EditorBuildSettings.scenes.Select(s => Path.GetFileNameWithoutExtension(s.path)))}."
                      + (string.IsNullOrEmpty(previous) ? "" : $" Previous scene: {previous}"));
        }

        /// <summary>MainMenu, L02_Desert, L17_CarPark first (in this order); any other scene stays after them with its flag.</summary>
        [MenuItem("OpenZ/Menu/Set Build Order (MainMenu first)", false, 2)]
        public static void SetBuildOrder()
        {
            var old = EditorBuildSettings.scenes.ToList();
            var list = new List<EditorBuildSettingsScene>();
            foreach (var p in Order)
            {
                if (!File.Exists(p)) { Debug.LogWarning("[OpenZ Menu] missing scene " + p + " (skipped in the build order)"); continue; }
                list.Add(new EditorBuildSettingsScene(p, true));
            }
            foreach (var s in old) if (!list.Any(x => x.path == s.path)) list.Add(s);
            EditorBuildSettings.scenes = list.ToArray();
            Debug.Log("[OpenZ Menu] build order: " + string.Join(" > ", list.Select((s, i) => $"{i}:{Path.GetFileNameWithoutExtension(s.path)}{(s.enabled ? "" : " (off)")}")));
        }

        /// <summary>Copies the repository's CREDITS.md into Resources/OpenZ/Menu/credits.txt (a TextAsset the menu shows).
        /// Looks next to the Unity project and up to three folders above it (repo layouts differ).</summary>
        [MenuItem("OpenZ/Menu/Refresh Credits Text", false, 3)]
        public static bool RefreshCredits()
        {
            string dir = Path.GetFullPath(Path.Combine(Application.dataPath, ".."));
            string src = null;
            for (int i = 0; i < 4 && dir != null; i++, dir = Path.GetDirectoryName(dir))
            {
                var c = Path.Combine(dir, "CREDITS.md");
                if (File.Exists(c)) { src = c; break; }
            }
            if (src == null) { Debug.LogWarning("[OpenZ Menu] CREDITS.md not found near the project: keeping " + CreditsAsset); return false; }
            Directory.CreateDirectory(MenuResources);
            string text = File.ReadAllText(src).Replace("\r\n", "\n");
            if (File.Exists(CreditsAsset) && File.ReadAllText(CreditsAsset).Replace("\r\n", "\n") == text) return false;
            File.WriteAllText(CreditsAsset, text);
            AssetDatabase.ImportAsset(CreditsAsset);
            Debug.Log("[OpenZ Menu] credits refreshed from CREDITS.md (" + text.Length + " chars)");
            return true;
        }

        /// <summary>Menu previews: stills as clamped, uncompressed-quality UI textures without mips; clips keep their H.264
        /// stream (no transcode) so the VideoPlayer decodes them in hardware.</summary>
        [MenuItem("OpenZ/Menu/Apply Preview Import Settings", false, 4)]
        public static void ConfigureMenuAssets()
        {
            if (!AssetDatabase.IsValidFolder(MenuResources)) return;
            foreach (var guid in AssetDatabase.FindAssets("", new[] { MenuResources }))
            {
                var path = AssetDatabase.GUIDToAssetPath(guid);
                var ai = AssetImporter.GetAtPath(path);
                if (ai is TextureImporter ti)
                {
                    bool dirty = ti.textureType != TextureImporterType.Default || ti.mipmapEnabled || ti.npotScale != TextureImporterNPOTScale.None
                                 || ti.wrapMode != TextureWrapMode.Clamp || ti.textureCompression != TextureImporterCompression.CompressedHQ || ti.maxTextureSize < 2048;
                    if (!dirty) continue;
                    ti.textureType = TextureImporterType.Default; ti.sRGBTexture = true; ti.mipmapEnabled = false; ti.npotScale = TextureImporterNPOTScale.None;
                    ti.wrapMode = TextureWrapMode.Clamp; ti.filterMode = FilterMode.Bilinear; ti.textureCompression = TextureImporterCompression.CompressedHQ;
                    ti.maxTextureSize = Mathf.Max(ti.maxTextureSize, 2048); ti.alphaSource = TextureImporterAlphaSource.None;
                    ti.SaveAndReimport();
                    Debug.Log("[OpenZ Menu] import settings: " + path);
                }
                else if (ai is VideoClipImporter vi)
                {
                    var ts = vi.defaultTargetSettings;
                    if (!ts.enableTranscoding) continue;
                    ts.enableTranscoding = false; vi.defaultTargetSettings = ts;
                    vi.SaveAndReimport();
                    Debug.Log("[OpenZ Menu] video import (no transcode): " + path);
                }
            }
        }

        [MenuItem("OpenZ/Menu/Open Main Menu Scene", false, 20)]
        static void OpenMenu()
        {
            if (!File.Exists(MenuScene)) { Debug.LogWarning("[OpenZ Menu] " + MenuScene + " does not exist yet: OpenZ/Menu/Build Main Menu Scene"); return; }
            EditorSceneManager.SaveOpenScenes();
            EditorSceneManager.OpenScene(MenuScene);
        }
    }

    /// <summary>Every player build ships the current CREDITS.md and warns when the front end is not scene 0.</summary>
    public sealed class OpenZMenuBuildHook : IPreprocessBuildWithReport
    {
        public int callbackOrder => 0;
        public void OnPreprocessBuild(BuildReport report)
        {
            OpenZMainMenuSetup.RefreshCredits();
            var first = EditorBuildSettings.scenes.FirstOrDefault(s => s.enabled);
            if (first == null || first.path != OpenZMainMenuSetup.MenuScene)
                Debug.LogWarning("[OpenZ Menu] the first enabled build scene is " + (first?.path ?? "none") + ", not " + OpenZMainMenuSetup.MenuScene + " (menu OpenZ/Menu/Set Build Order)");
        }
    }
}
