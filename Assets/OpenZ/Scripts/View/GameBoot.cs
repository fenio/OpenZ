using UnityEngine;

namespace OpenZ
{
    /// <summary>
    /// </summary>
    public static class GameBoot
    {
        public static bool TestMode => PlayerPrefs.GetInt("oz_testmode", 0) == 1;
        /// <summary>
        /// >= 57 distinct frames / s in a 60 fps recording). Session only, never saved; the heat guard keeps it (HeatGuardRuntime).</summary>
        public static int CapFps = 60;
        public static bool DevArg(string a) { if (global::OpenZ.OzRelease.IsRelease) return false; foreach (var s in System.Environment.GetCommandLineArgs()) if (s.Equals(a, System.StringComparison.OrdinalIgnoreCase)) return true; return false; }
        /// <summary>-ozdev on the command line (the editor counts as dev): gates the capture cap and the ending hotkeys.</summary>
        public static bool DevCmd => Application.isEditor || DevArg("-ozdev");

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        static void Init()
        {
            if (TestMode) { QualitySettings.vSyncCount = 0; Application.targetFrameRate = 30; }
            else
            {
                if (!Application.isEditor && DevArg("-ozdev") && DevArg("-cap72")) { CapFps = 72; Debug.Log("[OpenZ] -ozdev -cap72: capture session, 72 fps cap (OBS take recipe)"); }
                QualitySettings.vSyncCount = 0; Application.targetFrameRate = CapFps;
            }
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void ApplyTestMode()
        {
            if (!TestMode) return;
            foreach (var a in Object.FindObjectsByType<Atmosphere>(FindObjectsSortMode.None))
            {
                a.enabled = false;
                foreach (var ps in a.GetComponentsInChildren<ParticleSystem>(true)) ps.gameObject.SetActive(false);   // sand streaks, dust devils
            }
            Shader.SetGlobalFloat("_AtmoEnabled", 0f);
            // still 80 C at 30 fps: the test camera also drops SSAO, shadows and MSAA (restored when play mode ends)
            new GameObject("OZ_TestMode") { hideFlags = HideFlags.DontSave }.AddComponent<TestModeRuntime>();
            Debug.Log("OpenZ test mode: 30 fps, atmosphere, SSAO, shadows and MSAA off");
        }
    
    }

    /// <summary>Test-mode render savings, undone on exit so the URP asset keeps its full look.</summary>
    public class TestModeRuntime : MonoBehaviour
    {
        readonly System.Collections.Generic.List<UnityEngine.Rendering.Universal.ScriptableRendererFeature> off =
            new System.Collections.Generic.List<UnityEngine.Rendering.Universal.ScriptableRendererFeature>();

        void Start()
        {
            var urp = UnityEngine.Rendering.GraphicsSettings.currentRenderPipeline as UnityEngine.Rendering.Universal.UniversalRenderPipelineAsset;
            var f = typeof(UnityEngine.Rendering.Universal.UniversalRenderPipelineAsset).GetField("m_RendererDataList",
                        System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
            if (urp && f != null)
                foreach (var rd in (UnityEngine.Rendering.Universal.ScriptableRendererData[])f.GetValue(urp))
                    foreach (var feat in rd.rendererFeatures)
                        if (feat && feat.isActive && (feat.GetType().Name.Contains("Occlusion") || feat.name.Contains("Atmosphere"))) { feat.SetActive(false); off.Add(feat); }
            foreach (var c in Object.FindObjectsByType<Camera>(FindObjectsSortMode.None))
            {
                c.allowMSAA = false;
                var d = c.GetComponent<UnityEngine.Rendering.Universal.UniversalAdditionalCameraData>();
                if (d) { d.renderShadows = false; d.antialiasing = UnityEngine.Rendering.Universal.AntialiasingMode.None; }
            }
        }

        void OnDisable() { foreach (var feat in off) if (feat) feat.SetActive(true); off.Clear(); }
    }
}
