using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering.Universal;

namespace OpenZ.EditorTools
{
    /// <summary>
    /// Test mode switches SSAO and the OpenZ Atmosphere renderer features off and restores them in OnDisable; a domain
    /// Entering Play with test mode OFF re-activates both features in the PC renderer.
    /// </summary>
    [InitializeOnLoad]
    public static class FullLookGuard
    {
        const string Renderer = "Assets/Settings/PC_Renderer.asset";

        static FullLookGuard() { EditorApplication.playModeStateChanged += OnPlayMode; }

        static void OnPlayMode(PlayModeStateChange s)
        {
            if (s != PlayModeStateChange.ExitingEditMode || PlayerPrefs.GetInt("oz_testmode", 0) == 1) return;
            var rd = AssetDatabase.LoadAssetAtPath<ScriptableRendererData>(Renderer); if (!rd) return;
            int n = 0;
            foreach (var f in rd.rendererFeatures)
                if (f && !f.isActive && (f.GetType().Name.Contains("Occlusion") || f.name.Contains("Atmosphere"))) { f.SetActive(true); EditorUtility.SetDirty(f); n++; }
            if (n > 0) { AssetDatabase.SaveAssets(); Debug.Log($"[OpenZ] FullLookGuard: {n} renderer feature(s) re-activated (SSAO / Atmosphere) for the full look"); }
        }
    }
}
