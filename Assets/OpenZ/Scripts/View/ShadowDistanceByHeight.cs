using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace OpenZ
{
    /// <summary>
    /// The URP asset's fixed 260 m reaches only part of the ground seen from the 230 m overview (the top of
    /// the view lies ~360 m away), and wastes cascade texels when zoomed in. Each frame the main-light shadow distance follows the
    /// farthest ground point in view (the viewport's top corners on the ground plane) + 8 %, clamped 110-480 m, in 20 m steps with
    /// hysteresis (no cascade swim while the camera only pans). View only; the asset's value is put back when Play ends.
    /// </summary>
    [DefaultExecutionOrder(900)]
    public class ShadowDistanceByHeight : MonoBehaviour
    {
        public static bool On = true;
        public static float Min = 110f, Max = 480f, Margin = 1.08f, Step = 20f;
        public static float Current { get; private set; }
        UniversalRenderPipelineAsset urp; float original = -1f;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Boot() { if (!FindFirstObjectByType<ShadowDistanceByHeight>()) { var go = new GameObject("OZ_ShadowDistance"); DontDestroyOnLoad(go); go.AddComponent<ShadowDistanceByHeight>(); } }

        void OnEnable() { urp = GraphicsSettings.currentRenderPipeline as UniversalRenderPipelineAsset; if (urp) original = urp.shadowDistance; }
        void OnDisable() { if (urp && original > 0f) urp.shadowDistance = original; }
        void OnApplicationQuit() => OnDisable();

        void LateUpdate()
        {
            var cam = Camera.main;
            if (!On || !urp || !cam || ZGame.I == null) return;
            float far = 0f; var plane = new Plane(Vector3.up, Vector3.zero);
            foreach (var vp in new[] { new Vector3(0f, 1f, 0f), new Vector3(1f, 1f, 0f), new Vector3(0.5f, 1f, 0f) })
            {
                var ray = cam.ViewportPointToRay(vp);
                far = Mathf.Max(far, plane.Raycast(ray, out float d) && d > 0f ? d : cam.farClipPlane * 0.3f);
            }
            float want = Mathf.Clamp(far * Margin, Min, Max);
            float snapped = Mathf.Ceil(want / Step) * Step;
            if (Current <= 0f || snapped > Current || snapped < Current - Step * 1.5f) Current = snapped;   // grow at once, shrink with hysteresis
            if (Mathf.Abs(urp.shadowDistance - Current) > 0.5f) urp.shadowDistance = Current;
        }
    }
}
