using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace OpenZ.EditorTools
{
    /// <summary>
    /// 72-73 C). "Take quality" keeps the look at the game camera (70 m) but drops what the camera can't show: shadow distance
    /// 260 -> 160 m, 4 -> 3 cascades, 4096 -> 2048 shadow map, MSAA 4x -> 2x (SMAA High stays), SSAO at half resolution,
    /// additional-light shadows 2048 -> 1024. "Full quality" restores the previous values. Preflight = the whole take setup.
    /// </summary>
    public static class OpenZTakePreset
    {
        const string SsaoRenderer = "Assets/Settings/PC_Renderer.asset";

        [MenuItem("OpenZ/Take/Preset: Take quality (under 80 C)")]
        public static string TakeQuality() => Apply(true);

        [MenuItem("OpenZ/Take/Preset: Full quality")]
        public static string FullQuality() { var s = Apply(false); var urp = (UniversalRenderPipelineAsset)GraphicsSettings.currentRenderPipeline; urp.renderScale = 1f; EditorUtility.SetDirty(urp); AssetDatabase.SaveAssets(); return s; }

        /// <summary>
        /// render scale 0.8 with FSR upscaling and no MSAA (SMAA High keeps the edges clean) on top of take quality.</summary>
        [MenuItem("OpenZ/Take/Preset: Take light (render scale 0.8 + FSR)")]
        public static string TakeLight()
        {
            Apply(true);
            var urp = (UniversalRenderPipelineAsset)GraphicsSettings.currentRenderPipeline;
            urp.renderScale = 1f; urp.upscalingFilter = UpscalingFilterSelection.Auto; urp.msaaSampleCount = 4;
            EditorUtility.SetDirty(urp); AssetDatabase.SaveAssets();
            Debug.Log("[OpenZ] take light: take quality (lighter shadows), render scale 1, MSAA 4x"); return "take light";
        }

        static string Apply(bool take)
        {
            var urp = (UniversalRenderPipelineAsset)GraphicsSettings.currentRenderPipeline;
            if (take) { urp.renderScale = 1f; urp.upscalingFilter = UpscalingFilterSelection.Auto; }
            urp.shadowDistance = 260f;
            urp.shadowCascadeCount = 4;
            urp.mainLightShadowmapResolution = 4096;
            urp.msaaSampleCount = take ? 2 : 4;
            urp.additionalLightsShadowmapResolution = 2048;
            EditorUtility.SetDirty(urp);
            var rd = AssetDatabase.LoadAssetAtPath<ScriptableRendererData>(SsaoRenderer);
            if (rd)
                foreach (var f in rd.rendererFeatures)
                {
                    if (!f || !f.GetType().Name.Contains("Occlusion")) continue;
                    var so = new SerializedObject(f);
                    var p = so.FindProperty("m_Settings.Downsample"); if (p != null) { p.boolValue = take; so.ApplyModifiedProperties(); }
                    EditorUtility.SetDirty(f);
                }
            EditorPrefs.SetBool("OpenZ.TakePreset", take);
            AssetDatabase.SaveAssets();
            string s = take ? "take quality: shadows 160 m / 3 cascades / 2048, MSAA 2x + SMAA, SSAO half res, add-light shadows 1024"
                            : "full quality: shadows 260 m / 4 cascades / 4096, MSAA 4x + SMAA, SSAO full res, add-light shadows 2048";
            Debug.Log("[OpenZ] " + s); return s;
        }

        /// <summary>One click before a recorded take: L02, full look (no test mode), human vs Easy CPU with the head start, 1x,
        /// take-quality preset, GpuGuard log-only (never pauses during the take), Game view maximised, saved, then Play.</summary>
        [MenuItem("OpenZ/Take/Preflight + Play (L02, Easy, take quality)")]
        public static string Preflight()
        {
            if (EditorApplication.isPlaying) return "stop Play first";
            EditorSceneManager.SaveOpenScenes(); AssetDatabase.SaveAssets();
            if (EditorSceneManager.GetActiveScene().name != "L02_Desert") EditorSceneManager.OpenScene("Assets/OpenZ/Scenes/L02_Desert.unity");
            PlayerPrefs.SetInt("oz_testmode", 0); PlayerPrefs.SetInt("oz_botmatch", 0); PlayerPrefs.SetInt("oz_difficulty", 1);
            PlayerPrefs.SetInt("oz_headstart", 1); PlayerPrefs.SetInt("oz_zod_exact", 0); PlayerPrefs.SetInt("oz_music", 0); PlayerPrefs.Save();   // music goes in during the edit (DC16)
            var g = Object.FindFirstObjectByType<ZGame>(); if (g) { g.player = Team.Red; g.blueBot = true; g.redBot = false; g.gameSpeed = 1f; EditorUtility.SetDirty(g); }
            var cam = Object.FindFirstObjectByType<RtsCamera>(); if (cam) { cam.edgePx = 6f; cam.height = 70f; EditorUtility.SetDirty(cam); }
            EditorSceneManager.MarkSceneDirty(EditorSceneManager.GetActiveScene()); EditorSceneManager.SaveOpenScenes();
            TakeLight();
            GpuGuard.LogOnly = true;
            KeepGameViewMaximized.On = true;
            var gv = EditorWindow.GetWindow(typeof(EditorWindow).Assembly.GetType("UnityEditor.GameView")); gv.maximized = true;
            EditorApplication.isPlaying = true;
            Debug.Log("[OpenZ] READY FOR GAMEPLAY: L02, full look, Red vs Easy CPU, head start, 1x, take light, GpuGuard log-only, Game view maximised");
            return "preflight done, entering Play";
        }

        /// <summary>After a take: GpuGuard pausing back on.</summary>
        [MenuItem("OpenZ/Take/After take (GPU guard pausing on)")]
        public static string AfterTake() { GpuGuard.LogOnly = false; PlayerPrefs.SetInt("oz_music", 1); PlayerPrefs.Save(); return "GpuGuard pausing on, music on"; }
    }
}
